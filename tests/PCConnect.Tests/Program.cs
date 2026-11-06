using PCConnect.Core;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

internal static class Program
{
    static readonly string Root = Path.Combine(Path.GetTempPath(), "PCConnect-Tests-" + Guid.NewGuid().ToString("N"));
    static int passed, failed;
    static readonly List<string> results = [];
    static async Task<int> Main(string[] args)
    {
        Directory.CreateDirectory(Root);
        if (args.Length > 0 && args[0] == "peer") return await PeerProcess(args);
        try
        {
            await Test("protocol round trip and Unicode", async () => { using var m = new MemoryStream(); await Wire.Write(m, new Packet("test", Text: "తెలుగు 日本語 🖥"), default); m.Position = 0; Check((await Wire.Read<Packet>(m, default)).Text == "తెలుగు 日本語 🖥"); });
            await Test("reject oversized, negative, and truncated frames", async () => { foreach (var bytes in new[] { new byte[] { 0x7f, 0xff, 0xff, 0xff }, new byte[] { 0xff, 0xff, 0xff, 0xff }, new byte[] { 0, 0, 0, 8, 1 } }) await Throws(async () => await Wire.Read<Packet>(new MemoryStream(bytes), default)); });
            await Test("reject malformed JSON", () => Throws(async () => await Wire.Read<Packet>(new MemoryStream([0, 0, 0, 2, 123, 123]), default)));
            await Test("input validation", () => { foreach (var e in new[] { new InputEvent("unknown"), new InputEvent("key", Code: 300), new InputEvent("move", -1), new InputEvent("key", -1, Code: 10), new InputEvent("button", Code: 0) }) ThrowsSync(() => Wire.Validate(e)); return Task.CompletedTask; });
            await Test("bounded input mailbox and motion ordering", () => { var q = new InputMailbox(3); Check(q.TryPut(new("input", Input: new("move", 1)))); Check(q.TryPut(new("input", Input: new("move", 2)))); Check(q.Count == 1); Check(q.TryPut(new("input", Input: new("button", Code: 1)))); Check(q.TryPut(new("input", Input: new("move", 3)))); Check(!q.TryPut(new("input", Input: new("key", Code: 65)))); Check(q.TryTake(out var first) && first.Input!.X == 2); Check(q.TryTake(out var second) && second.Input!.Kind == "button"); Check(q.TryTake(out var third) && third.Input!.X == 3); q.Clear(); Check(q.Count == 0); return Task.CompletedTask; });
            await Test("held input release is complete and idempotent", () => { var h = new HeldInputs(); h.Track(new("key", 30, Code: 65)); h.Track(new("key", 30, Code: 65)); h.Track(new("button", Code: 1)); Check(h.Count == 2); Check(h.Release().All(e => e.Up)); Check(h.Count == 0 && h.Release().Length == 0); return Task.CompletedTask; });
            await Test("reject malicious Windows paths", () => { foreach (var path in new[] { "../bad", "a/../../bad", "C:/bad", "//server/x", "a\\b", "x:ads", "CON.txt", "aux", "LPT1", "COM¹.txt", "bad.", "bad ", "a//b", "a/./b", "a/../b", "x?", "a\0b" }) ThrowsSync(() => SafePaths.Resolve(Root, path)); SafePaths.Resolve(Root, "వ్రాత/空 folder/ok.txt"); return Task.CompletedTask; });
            await Test("reject duplicate paths, file-as-parent, quota overflow", () => { ThrowsSync(() => Transfers.Validate([new("A", false, 0), new("a", false, 0)])); ThrowsSync(() => Transfers.Validate([new("a", false, 1), new("a/b", false, 1)])); ThrowsSync(() => Transfers.Validate([new("large", false, Transfers.MaxBytes + 1)])); return Task.CompletedTask; });
            await Test("drag protocol rejects invalid IDs, actions and positions", () =>
            {
                foreach (var signal in new[] { new DragSignal("offer", "../../bad"), new DragSignal("execute", Guid.NewGuid().ToString("N")), new DragSignal("activate", Guid.NewGuid().ToString("N"), -1) }) ThrowsSync(signal.Validate);
                return Task.CompletedTask;
            });
            await Test("drag state cannot skip readiness or resurrect cancellation", () =>
            {
                var d = new DragTransaction(Guid.NewGuid().ToString("N"), true); Check(!d.Transition("activate")); Check(d.Transition("accepted")); Check(!d.Transition("finished")); Check(d.Transition("ready")); Check(d.Transition("activate")); Check(d.Transition("finished")); Check(!d.Transition("activate"));
                foreach (int depth in Enumerable.Range(0, 4)) { var canceled = new DragTransaction("id", false); foreach (string action in new[] { "accepted", "ready", "activate" }.Take(depth)) canceled.Transition(action); Check(canceled.Transition("cancel")); Check(!canceled.Transition("ready") && !canceled.Transition("activate") && !canceled.Transition("finished")); }
                return Task.CompletedTask;
            });
            await Test("remote drag button lifetime and cancellation take precedence", () =>
            {
                var held = new DragGesture(); Check(held.QueryContinue(false) == 0); held.Release(); Check(held.QueryContinue(false) == 0x40100); held.Cancel(); held.Release(); Check(held.QueryContinue(false) == 0x40101);
                var escape = new DragGesture(); Check(escape.QueryContinue(true) == 0x40101); return Task.CompletedTask;
            });
            await Test("drag bulk permit is session-bound, single-use and revocable", () =>
            {
                using var permit = new DragPermit(); string id = Guid.NewGuid().ToString("N"); Check(permit.Arm("session-a", id)); Check(!permit.Arm("session-a", Guid.NewGuid().ToString("N")));
                ThrowsSync(() => permit.Consume("session-b", id)); ThrowsSync(() => permit.Consume("session-a", Guid.NewGuid().ToString("N")));
                var token = permit.Consume("session-a", id); ThrowsSync(() => permit.Consume("session-a", id)); permit.Revoke(); Check(token.IsCancellationRequested); ThrowsSync(() => permit.Consume("session-a", id)); return Task.CompletedTask;
            });
            await Test("drag activation is an input ordering barrier", () =>
            {
                var q = new InputMailbox(); q.TryPut(new("input", Input: new("move", 2))); q.TryPut(new("drag", Drag: new("activate", Guid.NewGuid().ToString("N")))); q.TryPut(new("input", Input: new("move", 4))); q.TryPut(new("input", Input: new("button", Code: 1, Up: true)));
                Check(q.Count == 4); Check(q.TryTake(out var m) && m.Input?.X == 2); Check(q.TryTake(out var d) && d.Drag?.Kind == "activate"); Check(q.TryTake(out var n) && n.Input?.X == 4); Check(q.TryTake(out var up) && up.Input?.Up == true); return Task.CompletedTask;
            });
            using var a = Security.CreateIdentity(); using var b = Security.CreateIdentity(); using var stranger = Security.CreateIdentity();
            await Test("pin verification rejects wrong device", () => { Check(Security.Matches(a, Security.Fingerprint(a))); Check(!Security.Matches(a, Security.Fingerprint(b))); Check(!Security.Matches(null, Security.Fingerprint(b))); return Task.CompletedTask; });
            await Test("mutual TLS accepts paired certificates", async () => { await TlsPair(a, b, Security.Fingerprint(b), Security.Fingerprint(a), async (client, server) => { await Wire.Write(client, new Packet("test"), default); Check((await Wire.Read<Packet>(server, default)).Kind == "test"); }); });
            await Test("mutual TLS rejects impostor server", () => Throws(() => TlsPair(a, stranger, Security.Fingerprint(b), Security.Fingerprint(a), (_, _) => Task.CompletedTask)));
            await Test("mutual TLS rejects impostor client", () => Throws(() => TlsPair(stranger, b, Security.Fingerprint(b), Security.Fingerprint(a), async (client, server) => { await Wire.Write(client, new Packet("test"), default); await Wire.Read<Packet>(server, default); })));
            string fixture = Path.Combine(Root, "source", "nested 日本語"); Directory.CreateDirectory(Path.Combine(fixture, "empty")); await File.WriteAllTextAsync(Path.Combine(fixture, "తెలుగు.txt"), "Hello 🖥"); await File.WriteAllBytesAsync(Path.Combine(fixture, "bytes.bin"), RandomNumberGenerator.GetBytes(50000));
            var plan = TransferPlan.Create([fixture]);
            await Test("nested folders, empty folders, Unicode and SHA-256", async () =>
            {
                string? received = null;
                await TlsPair(a, b, Security.Fingerprint(b), Security.Fingerprint(a), async (client, server) =>
                {
                    var send = Transfers.Send(client, plan, "test", new(() => 1, 16 * 1024 * 1024), null, default);
                    var offer = await Wire.Read<TransferOffer>(server, default, 2097152);
                    received = await Transfers.Receive(server, offer.Entries!, Path.Combine(Root, "inbox"), null, default); await send;
                });
                Check(Directory.Exists(Path.Combine(received!, "nested 日本語", "empty")));
                foreach (var file in Directory.GetFiles(fixture)) Check(SHA256.HashData(File.ReadAllBytes(file)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(Path.Combine(received!, "nested 日本語", Path.GetFileName(file))))));
            });
            await Test("corrupt content never publishes a complete transfer", async () =>
            {
                string dest = Path.Combine(Root, "corrupt");
                await TlsPair(a, b, Security.Fingerprint(b), Security.Fingerprint(a), async (client, server) =>
                {
                    var receive = Transfers.Receive(server, [new("bad.bin", false, 3)], dest, null, default);
                    await Wire.Read<TransferReply>(client, default); await client.WriteAsync(new byte[35]); await Throws(async () => await receive);
                }); Check(!Directory.EnumerateFileSystemEntries(dest).Any());
            });
            await Test("receiver cancellation removes incomplete data", async () =>
            {
                string dest = Path.Combine(Root, "cancel");
                await TlsPair(a, b, Security.Fingerprint(b), Security.Fingerprint(a), async (client, server) =>
                {
                    using var stop = new CancellationTokenSource(); var receive = Transfers.Receive(server, [new("file", false, 10000)], dest, null, stop.Token);
                    await Wire.Read<TransferReply>(client, default); await client.WriteAsync(new byte[12]); stop.Cancel(); await Throws(async () => await receive);
                }); Check(!Directory.EnumerateFileSystemEntries(dest).Any());
            });
            await Test("disconnect removes incomplete data", async () =>
            {
                string dest = Path.Combine(Root, "disconnect");
                await TlsPair(a, b, Security.Fingerprint(b), Security.Fingerprint(a), async (client, server) =>
                { var receive = Transfers.Receive(server, [new("file", false, 10000)], dest, null, default); await Wire.Read<TransferReply>(client, default); client.Dispose(); await Throws(async () => await receive); });
                Check(!Directory.EnumerateFileSystemEntries(dest).Any());
            });
            await Test("existing copies are never overwritten", async () =>
            {
                string dest = Path.Combine(Root, "repeat"); var folders = new HashSet<string>();
                for (int i = 0; i < 2; i++) await TlsPair(a, b, Security.Fingerprint(b), Security.Fingerprint(a), async (client, server) => { var send = Transfers.Send(client, plan, "test", new(() => 1, 16 * 1024 * 1024), null, default); var offer = await Wire.Read<TransferOffer>(server, default, 2097152); folders.Add(await Transfers.Receive(server, offer.Entries!, dest, null, default)); await send; });
                Check(folders.Count == 2);
            });
            await Test("adaptive pacing backs off on RTT inflation", async () => { double rtt = 1; var p = new TransferPacer(() => rtt); await p.Pace(1, default); Check(p.CurrentBytesPerSecond == 524288); rtt = 20; await p.Pace(1, default); Check(p.CurrentBytesPerSecond == 131072); });
            await Test("RTT still measures replies delayed beyond the heartbeat interval", async () =>
            {
                await TlsPair(a, b, Security.Fingerprint(b), Security.Fingerprint(a), async (client, server) =>
                {
                    var session = new Session(client, "slow-pong", true); var running = session.Run(default);
                    var ping = await Wire.Read<Packet>(server, default); Check(ping.Kind == "ping");
                    await Task.Delay(400); await Wire.Write(server, new Packet("pong", ping.Stamp), default);
                    await Until(() => session.RttSequence > 0, 2000); Check(session.RttMs >= 350); session.Abort(); await running;
                });
            });
            await Test("session timeout and input cleanup", async () =>
            {
                await TlsPair(a, b, Security.Fingerprint(b), Security.Fingerprint(a), async (client, server) =>
                {
                    var held = new HeldInputs(); held.Track(new("key", Code: 65));
                    var session = new Session(client, "test", true); session.Closed += () => held.Release();
                    var watch = Stopwatch.StartNew(); await session.Run(default); Check(watch.Elapsed.TotalSeconds is >= 3 and < 5); Check(held.Count == 0);
                });
            });
            await Test("two-process reconnect, bidirectional transfers and RTT benchmark", TwoProcesses);
        }
        finally
        {
            Console.WriteLine($"RESULT: {passed} passed, {failed} failed");
            if (args.Length == 2 && args[0] == "--report") { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!); await File.WriteAllLinesAsync(args[1], results.Append($"RESULT: {passed} passed, {failed} failed")); }
            // Only this uniquely generated test directory is deleted.
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
        return failed == 0 ? 0 : 1;
    }
    static async Task Test(string name, Func<Task> body)
    { try { await body().WaitAsync(TimeSpan.FromSeconds(100)); passed++; results.Add("PASS " + name); Console.WriteLine(results[^1]); } catch (Exception ex) { failed++; results.Add("FAIL " + name + ": " + ex); Console.WriteLine(results[^1]); } }
    static void Check(bool value) { if (!value) throw new Exception("Assertion failed"); }
    static void ThrowsSync(Action action) { try { action(); } catch { return; } throw new Exception("Expected rejection"); }
    static async Task Throws(Func<Task> action) { try { await action(); } catch { return; } throw new Exception("Expected rejection"); }
    static async Task TlsPair(X509Certificate2 clientIdentity, X509Certificate2 serverIdentity, string clientPin, string serverPin, Func<System.Net.Security.SslStream, System.Net.Security.SslStream, Task> body)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var client = new TcpClient();
        try
        {
            var accept = listener.AcceptTcpClientAsync(timeout.Token).AsTask();
            await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, timeout.Token);
            using var server = await accept;
            var authClient = Security.Authenticate(client, clientIdentity, clientPin, false, timeout.Token);
            var authServer = Security.Authenticate(server, serverIdentity, serverPin, true, timeout.Token);
            try { await Task.WhenAll(authClient, authServer); }
            catch { if (authClient.IsCompletedSuccessfully) authClient.Result.Dispose(); if (authServer.IsCompletedSuccessfully) authServer.Result.Dispose(); throw; }
            using var c = authClient.Result; using var s = authServer.Result; await body(c, s).WaitAsync(timeout.Token);
        }
        finally { listener.Stop(); }
    }
    static async Task TwoProcesses()
    {
        string dir = Path.Combine(Root, "processes"); Directory.CreateDirectory(dir);
        using var a = Security.CreateIdentity(); using var b = Security.CreateIdentity();
        File.WriteAllBytes(Path.Combine(dir, "a.pfx"), a.Export(X509ContentType.Pfx)); File.WriteAllBytes(Path.Combine(dir, "b.pfx"), b.Export(X509ContentType.Pfx));
        var reserve = new TcpListener(IPAddress.Loopback, 0); reserve.Start(); int port = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop(); if (port == 65535) port--;
        using var server = StartPeer("secondary", dir, port); using var client = StartPeer("primary", dir, port);
        var so = server.StandardOutput.ReadToEndAsync(); var se = server.StandardError.ReadToEndAsync(); var co = client.StandardOutput.ReadToEndAsync(); var ce = client.StandardError.ReadToEndAsync();
        try { await Task.WhenAll(client.WaitForExitAsync(), server.WaitForExitAsync()).WaitAsync(TimeSpan.FromSeconds(90)); }
        finally { if (!client.HasExited) client.Kill(true); if (!server.HasExited) server.Kill(true); }
        string output = await co + await so + await ce + await se; Console.WriteLine(output); results.Add(output);
        Check(client.ExitCode == 0 && server.ExitCode == 0);
    }
    static Process StartPeer(string role, string dir, int port)
    {
        string executable = Environment.ProcessPath!;
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) info.ArgumentList.Add(typeof(Program).Assembly.Location);
        foreach (string arg in new[] { "peer", role, dir, port.ToString() }) info.ArgumentList.Add(arg);
        return Process.Start(info)!;
    }
    static async Task<int> PeerProcess(string[] args)
    {
        try
        {
            bool primary = args[1] == "primary"; string dir = args[2]; int port = int.Parse(args[3]);
            using var identity = X509CertificateLoader.LoadPkcs12FromFile(Path.Combine(dir, primary ? "a.pfx" : "b.pfx"), null);
            using var other = X509CertificateLoader.LoadPkcs12FromFile(Path.Combine(dir, primary ? "b.pfx" : "a.pfx"), null);
            await using var node = new PeerNode(identity, new(primary, "127.0.0.2", port, Security.Fingerprint(other), Path.Combine(dir, primary ? "a-in" : "b-in"), 1024, primary ? "127.0.0.1" : "127.0.0.2"));
            int connections = 0, inputs = 0, releases = 0, files = 0, clips = 0, dragFiles = 0;
            var dragSignals = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
            var held = new HeldInputs();
            node.Connected += s => { Interlocked.Increment(ref connections); s.Input += e => { lock (held) { held.Track(e); inputs++; } }; s.Closed += () => { lock (held) { releases += held.Release().Length; } }; };
            node.Connected += s => s.Drag += d =>
            {
                if (d.Kind == "offer") { Check(node.ArmIncomingDrag(d.Id)); s.SendDrag(new("accepted", d.Id)); }
                else if (d.Kind == "activate") { s.SendDrag(new("finished", d.Id)); node.RevokeIncomingDrag(d.Id); }
                else dragSignals[d.Id] = d.Kind;
            };
            node.DragFilesPrepared += (id, folder) => { Check(Directory.EnumerateFileSystemEntries(folder).Any()); Interlocked.Increment(ref dragFiles); node.Current!.SendDrag(new("ready", id)); };
            async Task DragCopy(string source)
            {
                string id = Guid.NewGuid().ToString("N"); node.Current!.SendDrag(new("offer", id)); await Until(() => dragSignals.TryGetValue(id, out var v) && v == "accepted", 5000);
                await node.SendFiles([source], id); await Until(() => dragSignals.TryGetValue(id, out var v) && v == "ready", 5000);
                node.Current!.SendDrag(new("activate", id)); await Until(() => dragSignals.TryGetValue(id, out var v) && v == "finished", 5000);
            }
            node.FilesReceived += _ => Interlocked.Increment(ref files); node.ClipboardReceived += _ => Interlocked.Increment(ref clips);
            node.Start(); await Until(() => node.Current?.Connected == true, 15000);
            if (primary)
            {
                var idle = await Samples(node, 5000);
                string file = Path.Combine(dir, "large.bin"); await File.WriteAllBytesAsync(file, RandomNumberGenerator.GetBytes(8 * 1024 * 1024));
                var elapsed = Stopwatch.StartNew(); var send = node.SendFiles([file]); var busy = new List<double>(); long lastRtt = 0;
                while (!send.IsCompleted)
                {
                    node.Current!.Send(new("move", 12345, 54321)); node.Current.Send(new("key", 30, Code: 65)); node.Current.Send(new("key", 30, Code: 65, Up: true));
                    if (node.Current.RttSequence != lastRtt) { lastRtt = node.Current.RttSequence; busy.Add(node.Current.RttMs); } await Task.Delay(100);
                }
                await send; elapsed.Stop(); await node.SendClipboard("integration text");
                Check(files == 1 || await WaitFor(() => files == 1, 5000));
                await DragCopy(Path.Combine(dir, "reverse.txt"));
                await Throws(() => node.SendFiles([Path.Combine(dir, "reverse.txt")], Guid.NewGuid().ToString("N")));
                Check(dragFiles == 1 || await WaitFor(() => dragFiles == 1, 5000));
                node.Current!.Send(new("key", 30, Code: 65)); await Task.Delay(100); node.Current.Abort();
                await Until(() => connections >= 2 && node.Current?.Connected == true, 15000);
                await node.SendClipboard("after reconnect");
                string summary = $"LOOPBACK RTT idle {Stats(idle)}; during 8 MiB transfer at 1 MiB/s {Stats(busy)}; reconnects={connections - 1}; reverse transfers={files}; transfer duration={elapsed.Elapsed.TotalSeconds:F2}s, throughput={8 / elapsed.Elapsed.TotalSeconds:F2} MiB/s";
                Console.WriteLine(summary); await File.WriteAllTextAsync(Path.Combine(dir, "done"), summary);
            }
            else
            {
                string reverse = Path.Combine(dir, "reverse.txt"); await File.WriteAllTextAsync(reverse, "reverse direction"); await node.SendFiles([reverse]); await DragCopy(reverse);
                await Until(() => File.Exists(Path.Combine(dir, "done")), 60000);
                Check(dragFiles == 1 && files == 1 && clips >= 2 && inputs > 0 && releases >= 1); lock (held) Check(held.Count == 0);
                Console.WriteLine($"SECONDARY verified drag transfers={dragFiles}, received transfers={files}, clipboard messages={clips}, input events={inputs}, disconnected held-input releases={releases}");
            }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
    static async Task<List<double>> Samples(PeerNode node, int ms)
    { var result = new List<double>(); long lastRtt = 0; var clock = Stopwatch.StartNew(); while (clock.ElapsedMilliseconds < ms) { await Task.Delay(100); if (node.Current?.RttMs is > 0 && node.Current.RttSequence != lastRtt) { lastRtt = node.Current.RttSequence; result.Add(node.Current.RttMs); } } return result; }
    static string Stats(List<double> samples) { var sorted = samples.Order().ToArray(); return $"n={sorted.Length}, median={sorted[sorted.Length / 2]:F2} ms, p95={sorted[(int)((sorted.Length - 1) * .95)]:F2} ms, max={sorted[^1]:F2} ms"; }
    static async Task Until(Func<bool> predicate, int timeout) { if (!await WaitFor(predicate, timeout)) throw new TimeoutException(); }
    static async Task<bool> WaitFor(Func<bool> predicate, int timeout) { var clock = Stopwatch.StartNew(); while (clock.ElapsedMilliseconds < timeout) { if (predicate()) return true; await Task.Delay(50); } return false; }
}
