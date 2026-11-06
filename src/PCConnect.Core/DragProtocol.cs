namespace PCConnect.Core;

public sealed record DragSignal(string Kind, string Id, int Y = 32768)
{
    public void Validate()
    {
        if (!Guid.TryParseExact(Id, "N", out _) || Y is < 0 or > 65535 ||
            Kind is not ("offer" or "accepted" or "ready" or "activate" or "cancel" or "finished" or "rejected"))
            throw new InvalidDataException("Invalid drag signal.");
    }
}
public enum DragPhase { Offered, Preparing, Ready, Active, Finished, Canceled }

// Used by both ends. Terminal states are irreversible; late ready/activate frames cannot resurrect a canceled gesture.
public sealed class DragTransaction(string id, bool source)
{
    public string Id { get; } = id;
    public bool Source { get; } = source;
    public DragPhase Phase { get; private set; } = DragPhase.Offered;
    public bool Terminal => Phase is DragPhase.Finished or DragPhase.Canceled;
    public bool Transition(string action)
    {
        if (Terminal) return false;
        if (action is "cancel" or "rejected") { Phase = DragPhase.Canceled; return true; }
        var next = (Phase, action) switch
        {
            (DragPhase.Offered, "accepted") => DragPhase.Preparing,
            (DragPhase.Preparing, "ready") => DragPhase.Ready,
            (DragPhase.Ready, "activate") => DragPhase.Active,
            (DragPhase.Active, "finished") => DragPhase.Finished,
            _ => (DragPhase?)null
        };
        if (next == null) return false;
        Phase = next.Value; return true;
    }
}

// An authoritative button lifetime, independent of the receiving desktop's physical mouse state.
public sealed class DragGesture
{
    int state; // held, released, canceled
    public bool Held => Volatile.Read(ref state) == 0;
    public bool Canceled => Volatile.Read(ref state) == 2;
    public void Release() => Interlocked.CompareExchange(ref state, 1, 0);
    public void Cancel() => Interlocked.Exchange(ref state, 2);
    public int QueryContinue(bool escape)
    {
        if (escape) Cancel();
        return Volatile.Read(ref state) switch { 2 => 0x40101, 1 => 0x40100, _ => 0 };
    }
}

// One session-bound, single-use bulk authorization per user gesture. Bulk TLS alone cannot start a drag.
public sealed class DragPermit : IDisposable
{
    readonly object gate = new();
    string? session, id;
    CancellationTokenSource? cancel;
    bool consumed;
    public bool Arm(string sessionId, string dragId)
    {
        new DragSignal("offer", dragId).Validate();
        lock (gate)
        {
            if (id != null) return false;
            session = sessionId; id = dragId; consumed = false; cancel = new(); return true;
        }
    }
    public CancellationToken Consume(string sessionId, string dragId)
    {
        lock (gate)
        {
            if (session != sessionId || id != dragId || consumed || cancel == null) throw new InvalidDataException("Drag transfer is not authorized by a current gesture.");
            consumed = true; return cancel.Token;
        }
    }
    public void Revoke(string? dragId = null)
    {
        lock (gate)
        {
            if (dragId != null && dragId != id) return;
            cancel?.Cancel(); cancel?.Dispose(); cancel = null; session = id = null; consumed = false;
        }
    }
    public void Dispose() => Revoke();
}
