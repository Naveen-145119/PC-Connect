using System.Diagnostics;
using System.Net.Security;

namespace PCConnect.Core;

public sealed record Hello(int Version, string Role, string SessionId);

public sealed class Session : IAsyncDisposable
{
    readonly SslStream stream;
    readonly CancellationTokenSource stop = new();
    readonly InputMailbox outgoing = new();
    readonly TaskCompletionSource ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    long lastRead = Stopwatch.GetTimestamp();
    long pingStamp;
    int started; int abortRequested;
    public string Id { get; }
    public bool IsPrimary { get; }
    public bool Connected => started == 1 && !stop.IsCancellationRequested;
    public double RttMs { get; private set; }
    public long RttSequence { get; private set; }
    public event Action<InputEvent>? Input;
    public event Action<DragSignal>? Drag;
    public event Action? ReturnRequested;
    public event Action? Closed;
    public event Action<string>? Notice;
    public Task Completion => ended.Task;
    public Session(SslStream stream, string id, bool primary) { this.stream = stream; Id = id; IsPrimary = primary; }
    public bool Send(InputEvent e)
    {
        if (!Connected) return false;
        if (outgoing.TryPut(new Packet("input", Input: e))) return true;
        RequestAbort(); return false;
    }
    public bool SendDrag(DragSignal signal)
    {
        signal.Validate(); if (!Connected) return false;
        if (outgoing.TryPut(new Packet("drag", Drag: signal))) return true;
        RequestAbort(); return false;
    }
    public void ReturnControl() { if (!outgoing.TryPut(new Packet("return"))) RequestAbort(); }
    public void RequestAbort() => Interlocked.Exchange(ref abortRequested, 1);
    public void Abort() { stop.Cancel(); stream.Dispose(); }
    public async Task Run(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref started, 1) != 0) throw new InvalidOperationException("Session already started.");
        using var link = ct.Register(Abort);
        var tasks = new[] { ReadLoop(), WriteLoop(), Heartbeat() };
        try { await await Task.WhenAny(tasks); }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or System.Net.Sockets.SocketException or InvalidDataException or ObjectDisposedException)
        { Notice?.Invoke("Connection ended; local control restored."); }
        finally
        {
            Abort();
            foreach (var task in tasks) { try { await task; } catch { /* all workers are observed */ } }
            outgoing.Clear(); Closed?.Invoke(); ended.TrySetResult();
        }
    }
    async Task ReadLoop()
    {
        while (!stop.IsCancellationRequested)
        {
            var p = await Wire.Read<Packet>(stream, stop.Token);
            Interlocked.Exchange(ref lastRead, Stopwatch.GetTimestamp());
            switch (p.Kind)
            {
                case "input" when !IsPrimary && p.Input != null:
                    Wire.Validate(p.Input); Input?.Invoke(p.Input); break;
                case "drag" when p.Drag != null: p.Drag.Validate(); Drag?.Invoke(p.Drag); break;
                case "return" when IsPrimary: ReturnRequested?.Invoke(); break;
                case "ping":
                    if (!outgoing.TryPut(new Packet("pong", p.Stamp))) Abort(); break;
                case "pong":
                    if (p.Stamp != 0 && p.Stamp == Interlocked.Read(ref pingStamp)) { RttMs = Stopwatch.GetElapsedTime(p.Stamp).TotalMilliseconds; RttSequence++; Interlocked.Exchange(ref pingStamp, 0); }
                    break;
                default: throw new InvalidDataException("Unexpected control packet.");
            }
        }
    }
    async Task WriteLoop()
    {
        while (!stop.IsCancellationRequested)
        {
            if (outgoing.TryTake(out var packet)) await Wire.Write(stream, packet, stop.Token);
            else await outgoing.WaitAsync(stop.Token);
        }
    }
    async Task Heartbeat()
    {
        while (!stop.IsCancellationRequested)
        {
            if (Volatile.Read(ref abortRequested) != 0 || Stopwatch.GetElapsedTime(Interlocked.Read(ref lastRead)).TotalSeconds > 3) { Abort(); return; }
            long stamp = Stopwatch.GetTimestamp();
            if (Interlocked.CompareExchange(ref pingStamp, stamp, 0) == 0 && !outgoing.TryPut(new Packet("ping", stamp))) { Abort(); return; }
            await Task.Delay(250, stop.Token);
        }
    }
    public async ValueTask DisposeAsync() { Abort(); if (started != 0) await Completion; stop.Dispose(); }
}
