using System.Diagnostics;
using System.Security.Cryptography;

namespace PCConnect.Core;

public sealed record TransferEntry(string Path, bool Directory, long Length);
public sealed record TransferOffer(string Kind, string SessionId, TransferEntry[]? Entries = null, string? Text = null, string? DragId = null);
public sealed record TransferReply(bool Ok, string Message);
public sealed record TransferProgress(long Bytes, long Total, string State);

public static class SafePaths
{
    public static string Resolve(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Length > 1000 || relative.StartsWith('/') || relative.Contains('\\') || relative.Contains(':'))
            throw new InvalidDataException("Unsafe transfer path.");
        foreach (string part in relative.Split('/'))
        {
            string stem = part.Split('.')[0].ToUpperInvariant();
            if (part is "" or "." or ".." || part.Length > 255 || part.EndsWith(' ') || part.EndsWith('.') || part.Any(c => c < 32 || "<>\"|?*".Contains(c))
                || stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$"
                || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && "123456789¹²³".Contains(stem[3])))
                throw new InvalidDataException("Unsafe or reserved transfer name.");
        }
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string result = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!result.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Path escapes destination.");
        return result;
    }
    public static void NoReparse(string path)
    {
        var current = new DirectoryInfo(Path.GetFullPath(path));
        while (current != null)
        {
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Links and junctions are not accepted in transfer destinations.");
            current = current.Parent;
        }
    }
}

public sealed class TransferPlan
{
    public TransferEntry[] Entries { get; }
    public string[] Sources { get; }
    TransferPlan(List<TransferEntry> entries, List<string> sources) { Entries = entries.ToArray(); Sources = sources.ToArray(); }
    public static TransferPlan Create(IEnumerable<string> paths, CancellationToken ct = default)
    {
        var entries = new List<TransferEntry>(); var sources = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string source, string relative, int depth)
        {
            ct.ThrowIfCancellationRequested();
            if (depth > 64 || entries.Count >= Transfers.MaxEntries) throw new IOException("Transfer contains too many entries or nesting levels.");
            SafePaths.Resolve(Path.GetTempPath(), relative);
            if (!seen.Add(relative)) throw new IOException("Duplicate names in transfer selection.");
            var attr = File.GetAttributes(source);
            if ((attr & FileAttributes.ReparsePoint) != 0) throw new IOException("Symbolic links, junctions, and cloud placeholders must be materialized before sending.");
            bool directory = (attr & FileAttributes.Directory) != 0;
            entries.Add(new(relative, directory, directory ? 0 : new FileInfo(source).Length)); sources.Add(source);
            if (directory) foreach (var child in Directory.EnumerateFileSystemEntries(source)) Add(child, relative + "/" + Path.GetFileName(child), depth + 1);
        }
        foreach (string path in paths) { string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar); Add(full, Path.GetFileName(full), 0); }
        Transfers.Validate(entries.ToArray());
        return new(entries, sources);
    }
}

public sealed class TransferPacer(Func<double> rtt, double bytesPerSecond = 512 * 1024)
{
    double baseline = double.MaxValue;
    public double CurrentBytesPerSecond { get; private set; } = 0;
    public async Task Pace(int bytes, CancellationToken ct)
    {
        double sample = rtt();
        if (sample > 0) baseline = Math.Min(baseline, sample);
        bool congested = sample > Math.Max(8, baseline * 2);
        CurrentBytesPerSecond = congested ? Math.Min(128 * 1024, bytesPerSecond) : bytesPerSecond;
        await Task.Delay(TimeSpan.FromSeconds(bytes / CurrentBytesPerSecond), ct);
    }
}

public static class Transfers
{
    public const int MaxEntries = 10000;
    public const long MaxBytes = 20L * 1024 * 1024 * 1024;
    public static long Validate(TransferEntry[] entries)
    {
        if (entries.Length is < 1 or > MaxEntries) throw new InvalidDataException("Invalid entry count (limit 10,000).");
        var paths = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in entries)
        {
            SafePaths.Resolve(Path.GetTempPath(), entry.Path);
            if (entry.Length < 0 || entry.Length > MaxBytes || (entry.Directory && entry.Length != 0)) throw new InvalidDataException("Invalid file size.");
            if (!paths.TryAdd(entry.Path, entry.Directory)) throw new InvalidDataException("Duplicate transfer path.");
            total = checked(total + entry.Length);
            if (total > MaxBytes) throw new InvalidDataException("Transfer exceeds 20 GiB limit.");
            string parent = entry.Path;
            while (parent.Contains('/'))
            {
                parent = parent[..parent.LastIndexOf('/')];
                if (!paths.TryGetValue(parent, out bool isDirectory) || !isDirectory) throw new InvalidDataException("Parent directory missing or invalid.");
            }
        }
        return total;
    }
    public static async Task Send(Stream stream, TransferPlan plan, string sessionId, TransferPacer pacer, Action<TransferProgress>? progress, CancellationToken ct, string? dragId = null)
    {
        if (dragId != null) new DragSignal("offer", dragId).Validate();
        long total = Validate(plan.Entries), sent = 0;
        await Wire.Write(stream, new TransferOffer(dragId == null ? "files" : "drag-files", sessionId, plan.Entries, DragId: dragId), ct, 2 * 1024 * 1024);
        var accepted = await Wire.Read<TransferReply>(stream, ct);
        if (!accepted.Ok) throw new IOException(accepted.Message);
        byte[] buffer = new byte[16384];
        for (int i = 0; i < plan.Entries.Length; i++)
        {
            var entry = plan.Entries[i]; if (entry.Directory) continue;
            if ((File.GetAttributes(plan.Sources[i]) & FileAttributes.ReparsePoint) != 0) throw new IOException("Source changed into a link.");
            await using var file = new FileStream(plan.Sources[i], FileMode.Open, FileAccess.Read, FileShare.Read, buffer.Length, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (file.Length != entry.Length) throw new IOException("Source file size changed; retry transfer.");
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long remaining = entry.Length;
            while (remaining > 0)
            {
                int n = await file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(remaining, buffer.Length)), ct);
                if (n == 0) throw new EndOfStreamException("Source changed during transfer.");
                await pacer.Pace(n, ct);
                hash.AppendData(buffer, 0, n); await stream.WriteAsync(buffer.AsMemory(0, n), ct);
                remaining -= n; sent += n; progress?.Invoke(new(sent, total, "Sending"));
            }
            await stream.WriteAsync(hash.GetHashAndReset(), ct);
        }
        var result = await Wire.Read<TransferReply>(stream, ct);
        if (!result.Ok) throw new IOException(result.Message);
        progress?.Invoke(new(total, total, "Delivered and SHA-256 verified"));
    }
    public static async Task<string> Receive(Stream stream, TransferEntry[] entries, string destination, Action<TransferProgress>? progress, CancellationToken ct)
    {
        long total = Validate(entries), received = 0;
        Directory.CreateDirectory(destination); SafePaths.NoReparse(destination);
        string id = Guid.NewGuid().ToString("N");
        string staging = Path.Combine(destination, ".partial-" + id);
        string completed = Path.Combine(destination, "Received-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + id[..8]);
        Directory.CreateDirectory(staging);
        try
        {
            if (new DriveInfo(Path.GetPathRoot(Path.GetFullPath(destination))!).AvailableFreeSpace < total + 16 * 1024 * 1024) throw new IOException("Insufficient free space.");
            await Wire.Write(stream, new TransferReply(true, "Ready"), ct);
            byte[] buffer = new byte[16384];
            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                string target = SafePaths.Resolve(staging, entry.Path);
                if (entry.Directory) { Directory.CreateDirectory(target); continue; }
                await using (var file = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    long remaining = entry.Length;
                    while (remaining > 0)
                    {
                        int n = (int)Math.Min(remaining, buffer.Length);
                        await stream.ReadExactlyAsync(buffer.AsMemory(0, n), ct);
                        hash.AppendData(buffer, 0, n); await file.WriteAsync(buffer.AsMemory(0, n), ct);
                        remaining -= n; received += n; progress?.Invoke(new(received, total, "Receiving"));
                    }
                    byte[] expected = new byte[32]; await stream.ReadExactlyAsync(expected, ct);
                    if (!CryptographicOperations.FixedTimeEquals(expected, hash.GetHashAndReset())) throw new InvalidDataException("File integrity verification failed.");
                    await file.FlushAsync(ct);
                }
            }
            ct.ThrowIfCancellationRequested(); SafePaths.NoReparse(destination);
            Directory.Move(staging, completed); // Atomic publication, unique directory: never overwrite an existing destination.
            await Wire.Write(stream, new TransferReply(true, "Verified"), ct);
            progress?.Invoke(new(total, total, "Received and SHA-256 verified"));
            return completed;
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
}
