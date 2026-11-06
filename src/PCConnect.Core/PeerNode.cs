using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace PCConnect.Core;

public sealed record PeerOptions(bool Primary, string Host, int Port, string PeerFingerprint, string Destination, double TransferKiB = 512, string BindAddress = "0.0.0.0", string? DragCache = null);

public sealed class PeerNode(X509Certificate2 identity, PeerOptions options) : IAsyncDisposable
{
    readonly CancellationTokenSource stop = new();
    readonly DragPermit dragPermit = new();
    readonly SemaphoreSlim sendGate = new(1, 1);
    readonly SemaphoreSlim receiveGate = new(1, 1);
    readonly SemaphoreSlim clients = new(4, 4);
    readonly System.Collections.Concurrent.ConcurrentDictionary<int, Task> workers = new();
    int workerId;
    Task? runner;
    CancellationTokenSource? sending, receiving;
    volatile Session? session;
    string? remoteHost;
    public Session? Current => session;
    public bool ClipboardEnabled { get; set; } = true;
    public event Action<Session>? Connected;
    public event Action<string>? Status;
    public event Action<string>? ClipboardReceived;
    public event Action<TransferProgress>? Progress;
    public event Action<string>? FilesReceived;
    public event Action<string, string>? DragFilesPrepared;
    public event Action<string>? DragTransferFailed;
    public bool ArmIncomingDrag(string id) => session?.Connected == true && dragPermit.Arm(session.Id, id);
    public void RevokeIncomingDrag(string id) => dragPermit.Revoke(id);
    public void Start()
    {
        if (runner != null) throw new InvalidOperationException("Already started.");
        Security.Normalize(options.PeerFingerprint);
        runner = Task.Run(Run);
    }
    public void CancelTransfers() { try { sending?.Cancel(); } catch (ObjectDisposedException) { } try { receiving?.Cancel(); } catch (ObjectDisposedException) { } }
    async Task Run()
    {
        var bulk = new TcpListener(IPAddress.Parse(options.BindAddress), options.Port + 1);
        TcpListener? input = options.Primary ? null : new TcpListener(IPAddress.Parse(options.BindAddress), options.Port);
        try
        {
            bulk.Start(4); input?.Start(4);
            var bulkTask = AcceptBulk(bulk);
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    TcpClient? tcp = null;
                    try
                    {
                        Status?.Invoke(options.Primary ? "Connecting…" : "Waiting for paired primary…");
                        if (options.Primary)
                        {
                            tcp = new TcpClient(new IPEndPoint(IPAddress.Parse(options.BindAddress), 0));
                            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); timeout.CancelAfter(5000);
                            await tcp.ConnectAsync(options.Host, options.Port, timeout.Token);
                        }
                        else tcp = await input!.AcceptTcpClientAsync(stop.Token);
                        using (tcp)
                        using (var ssl = await Security.Authenticate(tcp, identity, options.PeerFingerprint, !options.Primary, stop.Token))
                        {
                            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); handshake.CancelAfter(5000);
                            string id;
                            if (options.Primary)
                            {
                                id = Guid.NewGuid().ToString("N");
                                await Wire.Write(ssl, new Hello(Wire.Version, "primary", id), handshake.Token);
                                var ack = await Wire.Read<Hello>(ssl, handshake.Token);
                                if (ack.Version != Wire.Version || ack.Role != "secondary" || ack.SessionId != id) throw new InvalidDataException("Incompatible peer.");
                            }
                            else
                            {
                                var hello = await Wire.Read<Hello>(ssl, handshake.Token);
                                if (hello.Version != Wire.Version || hello.Role != "primary" || !Guid.TryParseExact(hello.SessionId, "N", out _)) throw new InvalidDataException("Incompatible peer.");
                                id = hello.SessionId;
                                await Wire.Write(ssl, new Hello(Wire.Version, "secondary", id), handshake.Token);
                            }
                            remoteHost = ((IPEndPoint)tcp.Client.RemoteEndPoint!).Address.ToString();
                            var connected = new Session(ssl, id, options.Primary);
                            session = connected; Connected?.Invoke(connected); Status?.Invoke("Connected · authenticated TLS");
                            await connected.Run(stop.Token);
                        }
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        tcp?.Dispose();
                        if (!stop.IsCancellationRequested) Status?.Invoke("Connection unavailable or authentication rejected; retrying in 2 seconds.");
                    }
                    finally { dragPermit.Revoke(); session = null; remoteHost = null; CancelTransfers(); }
                    if (!stop.IsCancellationRequested) await Task.Delay(2000, stop.Token);
                }
            }
            finally { bulk.Stop(); input?.Stop(); try { await bulkTask; } catch (OperationCanceledException) { } }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Status?.Invoke("Unable to listen: " + ex.Message); }
        finally { bulk.Stop(); input?.Stop(); Status?.Invoke("Disconnected · local control"); }
    }
    async Task AcceptBulk(TcpListener listener)
    {
        while (!stop.IsCancellationRequested)
        {
            var tcp = await listener.AcceptTcpClientAsync(stop.Token);
            if (!await clients.WaitAsync(0, stop.Token)) { tcp.Dispose(); continue; }
            int id = Interlocked.Increment(ref workerId);
            Task task = HandleBulk(tcp);
            workers[id] = task;
            _ = task.ContinueWith(_ => workers.TryRemove(id, out var ignored), TaskScheduler.Default);
        }
    }
    async Task HandleBulk(TcpClient tcp)
    {
        bool acquired = false; string? dragId = null;
        try
        {
            using (tcp)
            using (var ssl = await Security.Authenticate(tcp, identity, options.PeerFingerprint, true, stop.Token))
            using (var headerTimeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
            {
                headerTimeout.CancelAfter(8000);
                var offer = await Wire.Read<TransferOffer>(ssl, headerTimeout.Token, 2 * 1024 * 1024);
                Session? current = session;
                if (current?.Connected != true || offer.SessionId != current.Id) throw new InvalidDataException("Transfer is not bound to an active session.");
                if (offer.Kind == "clipboard")
                {
                    if (ClipboardEnabled && offer.Text is { Length: <= 65536 }) ClipboardReceived?.Invoke(offer.Text);
                    await Wire.Write(ssl, new TransferReply(true, "Received"), headerTimeout.Token); return;
                }
                if (offer.Kind is not ("files" or "drag-files") || offer.Entries == null) throw new InvalidDataException("Unsupported transfer.");
                if (!(acquired = await receiveGate.WaitAsync(0, stop.Token))) { await Wire.Write(ssl, new TransferReply(false, "Peer is already receiving a transfer."), headerTimeout.Token); return; }
                CancellationToken gestureToken = default;
                if (offer.Kind == "drag-files") { dragId = offer.DragId ?? throw new InvalidDataException("Missing drag identifier."); gestureToken = dragPermit.Consume(current.Id, dragId); }
                else if (offer.DragId != null) throw new InvalidDataException("Unexpected drag identifier.");
                using var transferStop = CancellationTokenSource.CreateLinkedTokenSource(stop.Token, gestureToken);
                receiving = transferStop;
                void Disconnected() => transferStop.Cancel();
                current.Closed += Disconnected;
                if (!current.Connected) transferStop.Cancel();
                try
                {
                    // A per-read inactivity deadline protects against stalled peers; cancellation closes the TLS stream.
                    using var deadlineStream = new IdleTimeoutStream(ssl, TimeSpan.FromSeconds(15));
                    string folder = await Transfers.Receive(deadlineStream, offer.Entries, dragId == null ? options.Destination : options.DragCache ?? Path.Combine(options.Destination, ".drag-cache"), p => Progress?.Invoke(p), transferStop.Token);
                    if (dragId == null) FilesReceived?.Invoke(folder); else DragFilesPrepared?.Invoke(dragId, folder);
                }
                finally { current.Closed -= Disconnected; receiving = null; }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { if (dragId != null) DragTransferFailed?.Invoke(dragId); Status?.Invoke(ex is OperationCanceledException ? "Transfer canceled." : "Incoming transfer failed: " + SafeError(ex)); }
        finally { tcp.Dispose(); if (acquired) receiveGate.Release(); clients.Release(); }
    }
    static string SafeError(Exception ex) => ex switch
    {
        InvalidDataException => ex.Message,
        System.Security.Authentication.AuthenticationException => "Peer authentication rejected.",
        EndOfStreamException => "Peer disconnected before completion.",
        _ => "Network or storage error (" + ex.GetType().Name + ")."
    };
    public async Task SendFiles(string[] paths, string? dragId = null, CancellationToken cancellation = default)
    {
        if (!await sendGate.WaitAsync(0)) throw new IOException("A transfer is already running.");
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(stop.Token, cancellation); sending = cancel;
        try
        {
            var current = session; string host = remoteHost ?? throw new IOException("Connect to the other laptop first.");
            if (current?.Connected != true) throw new IOException("Connect to the other laptop first.");
            void Disconnected() => cancel.Cancel();
            current.Closed += Disconnected;
            if (!current.Connected) cancel.Cancel();
            try
            {
                var plan = await Task.Run(() => TransferPlan.Create(paths, cancel.Token), cancel.Token);
                using var tcp = new TcpClient(new IPEndPoint(IPAddress.Parse(options.BindAddress), 0)); await tcp.ConnectAsync(host, options.Port + 1, cancel.Token);
                using var ssl = await Security.Authenticate(tcp, identity, options.PeerFingerprint, false, cancel.Token);
                using var stream = new IdleTimeoutStream(ssl, TimeSpan.FromSeconds(15));
                await Transfers.Send(stream, plan, current.Id, new TransferPacer(() => current.RttMs, options.TransferKiB * 1024), p => Progress?.Invoke(p), cancel.Token, dragId);
            }
            finally { current.Closed -= Disconnected; }
        }
        finally { sending = null; sendGate.Release(); }
    }
    public async Task SendClipboard(string text)
    {
        var current = session; string? host = remoteHost;
        if (!ClipboardEnabled || text.Length > 65536 || current?.Connected != true || host == null) return;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); cancel.CancelAfter(5000);
        using var tcp = new TcpClient(new IPEndPoint(IPAddress.Parse(options.BindAddress), 0)); await tcp.ConnectAsync(host, options.Port + 1, cancel.Token);
        using var ssl = await Security.Authenticate(tcp, identity, options.PeerFingerprint, false, cancel.Token);
        await Wire.Write(ssl, new TransferOffer("clipboard", current.Id, Text: text), cancel.Token, 2 * 1024 * 1024);
        await Wire.Read<TransferReply>(ssl, cancel.Token);
    }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel(); session?.Abort(); dragPermit.Revoke(); CancelTransfers();
        if (runner != null) await runner;
        await Task.WhenAll(workers.Values);
    }
}

// Deadlines reset per IO operation, so large healthy transfers may run indefinitely.
public sealed class IdleTimeoutStream(Stream inner, TimeSpan timeout) : Stream
{
    public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    { using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(timeout); return await inner.ReadAsync(buffer, deadline.Token); }
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    { using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(timeout); await inner.WriteAsync(buffer, deadline.Token); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() => inner.Flush();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
