using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using PCConnect.Core;

namespace PCConnect.App;

internal sealed record Settings(bool Primary = true, string Host = "", string Fingerprint = "", bool Right = true, bool Clipboard = true, int Port = 45831, int TransferKiB = 512);
internal static class LocalData
{
    public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCConnect");
    public static readonly string Inbox = Path.Combine(Root, "Received");
    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(Root, "settings.json"))) ?? new(); }
        catch { return new(); }
    }
    public static void Save(Settings settings) { Directory.CreateDirectory(Root); File.WriteAllText(Path.Combine(Root, "settings.json"), JsonSerializer.Serialize(settings)); }
    public static X509Certificate2 Identity()
    {
        Directory.CreateDirectory(Root); string file = Path.Combine(Root, "identity.dpapi");
        if (File.Exists(file))
        {
            byte[] bytes = Protect(File.ReadAllBytes(file), false);
            try { return X509CertificateLoader.LoadPkcs12(bytes, null, X509KeyStorageFlags.UserKeySet); }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
        }
        using var cert = Security.CreateIdentity();
        byte[] pfx = cert.Export(X509ContentType.Pfx);
        try
        {
            byte[] encrypted = Protect(pfx, true);
            using (var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None)) output.Write(encrypted);
            return X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.UserKeySet);
        }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(pfx); }
    }
    [StructLayout(LayoutKind.Sequential)] struct Blob { public int Length; public nint Data; }
    [DllImport("crypt32.dll", SetLastError = true)] static extern bool CryptProtectData(ref Blob input, nint description, nint entropy, nint reserved, nint prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)] static extern bool CryptUnprotectData(ref Blob input, nint description, nint entropy, nint reserved, nint prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")] static extern nint LocalFree(nint memory);
    static byte[] Protect(byte[] data, bool encrypt)
    {
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            var input = new Blob { Length = data.Length, Data = handle.AddrOfPinnedObject() };
            Blob output;
            bool ok = encrypt ? CryptProtectData(ref input, 0, 0, 0, 0, 1, out output) : CryptUnprotectData(ref input, 0, 0, 0, 0, 1, out output);
            if (!ok) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try { byte[] result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result; }
            finally { LocalFree(output.Data); }
        }
        finally { handle.Free(); }
    }
}
