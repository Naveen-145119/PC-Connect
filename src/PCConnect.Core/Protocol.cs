using System.Buffers.Binary;
using System.Text.Json;

namespace PCConnect.Core;

public sealed record Packet(string Kind, long Stamp = 0, InputEvent? Input = null, string? Text = null, DragSignal? Drag = null);
public sealed record InputEvent(string Kind, int X = 0, int Y = 0, int Code = 0, bool Up = false, bool Extended = false);

public static class Wire
{
    public const int Version = 2;
    public static async Task Write<T>(Stream stream, T value, CancellationToken ct, int limit = 8192)
    {
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(value);
        if (data.Length > limit) throw new InvalidDataException("Message exceeds protocol limit.");
        byte[] length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        await stream.WriteAsync(length, ct);
        await stream.WriteAsync(data, ct);
    }
    public static async Task<T> Read<T>(Stream stream, CancellationToken ct, int limit = 8192)
    {
        byte[] length = new byte[4];
        await stream.ReadExactlyAsync(length, ct);
        int count = BinaryPrimitives.ReadInt32BigEndian(length);
        if (count < 2 || count > limit) throw new InvalidDataException("Invalid message length.");
        byte[] data = new byte[count];
        await stream.ReadExactlyAsync(data, ct);
        return JsonSerializer.Deserialize<T>(data) ?? throw new InvalidDataException("Empty message.");
    }
    public static void Validate(InputEvent e)
    {
        bool valid = e.Kind switch
        {
            "move" => e.X is >= 0 and <= 65535 && e.Y is >= 0 and <= 65535,
            "key" => e.Code is > 0 and <= 255 && e.X is >= 0 and <= 65535,
            "button" => e.Code is >= 1 and <= 5,
            "wheel" or "hwheel" => e.Code is >= -12000 and <= 12000,
            "release" or "enter" => true,
            _ => false
        };
        if (!valid) throw new InvalidDataException("Invalid input event.");
    }
}

// Consecutive moves are replaceable; a button/key creates an ordering barrier.
// No blocking IO, allocation of serialization buffers, or waiting for capacity here.
public sealed class InputMailbox(int capacity = 512)
{
    readonly object gate = new();
    readonly LinkedList<Packet> queue = new();
    readonly SemaphoreSlim available = new(0, 1);
    public Task<bool> WaitAsync(CancellationToken ct, int timeout = -1) => available.WaitAsync(timeout, ct);
    public bool TryPut(Packet packet)
    {
        lock (gate)
        {
            if (packet.Input?.Kind == "move" && queue.Last?.Value.Input?.Kind == "move")
                queue.Last.Value = packet;
            else
            {
                if (queue.Count >= capacity) return false;
                bool empty = queue.Count == 0;
                queue.AddLast(packet);
                if (empty && available.CurrentCount == 0) available.Release();
            }
            return true;
        }
    }
    public bool TryTake(out Packet packet)
    {
        lock (gate)
        {
            if (queue.First is null) { packet = null!; return false; }
            packet = queue.First.Value; queue.RemoveFirst(); return true;
        }
    }
    public void Clear() { lock (gate) { queue.Clear(); while (available.Wait(0)) { } } }
    public int Count { get { lock (gate) return queue.Count; } }
}

public sealed class HeldInputs
{
    readonly Dictionary<(string, int), InputEvent> held = new();
    public int Count => held.Count;
    public void Track(InputEvent e)
    {
        if (e.Kind is not ("key" or "button")) return;
        if (e.Up) held.Remove((e.Kind, e.Code)); else held[(e.Kind, e.Code)] = e;
    }
    public InputEvent[] Release()
    {
        var result = held.Values.Select(e => e with { Up = true }).ToArray();
        held.Clear(); return result;
    }
}
