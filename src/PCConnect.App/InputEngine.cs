using PCConnect.Core;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace PCConnect.App;

// Hooks have their own STA/message pump. Injection and the independent safety watchdog use a worker.
internal sealed class InputEngine : IDisposable
{
    readonly Thread thread;
    readonly ManualResetEventSlim ready = new();
    readonly InputMailbox incoming = new();
    readonly HeldInputs held = new();
    readonly object injectionGate = new();
    readonly bool[] physical = new bool[256];
    readonly CancellationTokenSource stop = new();
    readonly Task injector;
    Native.HookProc? mouseProc, keyProc;
    nint mouseHook, keyHook;
    HookWindow? window;
    volatile Session? session;
    volatile bool primary, right = true, remote;
    volatile bool returnFlag; bool accepting;
    int buttons;
    double remoteX, remoteY;
    Rectangle bounds;
    Point anchor;
    readonly System.Collections.Concurrent.ConcurrentQueue<Action> hookActions = new();
    volatile DragGesture? targetGesture;
    volatile string? targetDragId, allowedDragId;
    volatile bool remoteLeft;
    public event Action<DragSignal>? DragMessage;
    public event Action? DragInterrupted;
    public DragGesture? TargetGesture => targetGesture;
    public string? TargetDragId => targetDragId;
    public bool LeftHeld => (Volatile.Read(ref buttons) & 2) != 0 || remoteLeft;
    public bool CanStartDrag => primary ? !remote && (Volatile.Read(ref buttons) & 2) != 0 : remoteLeft && accepting;
    public bool Connected => session?.Connected == true;
    public void ArmDragTarget(string id) => allowedDragId = id;
    public void EndDragTarget(string id)
    {
        if (allowedDragId == id) allowedDragId = null;
        if (targetDragId == id) { targetGesture?.Cancel(); targetGesture = null; targetDragId = null; }
    }
    public Task<bool> HandoffSource(string id, CancellationToken cancellation)
    {
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        hookActions.Enqueue(() =>
        {
            if (cancellation.IsCancellationRequested || !LeftHeld || session?.Connected != true) { result.TrySetResult(false); return; }
            Native.GetCursorPos(out var point);
            double y = Math.Clamp((point.Y - bounds.Top) / (double)Math.Max(1, bounds.Height - 1), 0, 1);
            // Cancel Explorer's source OLE operation; never report MOVE or delete source files.
            if (!CancelLocalOle()) { result.TrySetResult(false); return; }
            if (primary)
            {
                remote = true; remoteX = right ? .025 : .975; remoteY = y;
                Send(new InputEvent("enter")); Send(MoveEvent());
                session.SendDrag(new DragSignal("activate", id, (int)(y * 65535)));
                Native.SetCursorPos(anchor.X, anchor.Y);
            }
            else
            {
                lock (injectionGate) { accepting = false; remoteLeft = false; ReleaseAll(); }
                session.SendDrag(new DragSignal("activate", id, (int)(y * 65535)));
            }
            result.TrySetResult(true);
        });
        if (window == null || !Native.PostMessage(window.Handle, 0x8002, 0, 0)) result.TrySetResult(false);
        return result.Task;
    }
    static bool CancelLocalOle()
    {
        Native.Input[] events = [
            new() { Type = 1, Value = new Native.InputUnion { Key = new Native.KeyInput { Vk = 0x1b } } },
            new() { Type = 1, Value = new Native.InputUnion { Key = new Native.KeyInput { Vk = 0x1b, Flags = 2 } } },
            new() { Value = new Native.InputUnion { Mouse = new Native.MouseInput { Flags = 4 } } }
        ];
        return Native.SendInput(3, events, Marshal.SizeOf<Native.Input>()) == 3;
    }
    void ProcessDrag(DragSignal signal)
    {
        if (signal.Kind == "activate")
        {
            if (allowedDragId != signal.Id) return;
            void Activate()
            {
                if (allowedDragId != signal.Id || session?.Connected != true) return;
                allowedDragId = null;
                if (primary)
                {
                    if ((Volatile.Read(ref buttons) & 2) == 0) { session.SendDrag(new DragSignal("cancel", signal.Id)); DragMessage?.Invoke(new DragSignal("cancel", signal.Id)); return; }
                    remote = false; session.Send(new InputEvent("release"));
                    Native.SetCursorPos(right ? bounds.Right - 26 : bounds.Left + 26, bounds.Top + (int)(signal.Y / 65535.0 * (bounds.Height - 1)));
                }
                targetDragId = signal.Id; targetGesture = new DragGesture(); DragMessage?.Invoke(signal);
            }
            if (primary) { hookActions.Enqueue(Activate); if (window != null) Native.PostMessage(window.Handle, 0x8002, 0, 0); }
            else Activate();
        }
        else { if (signal.Kind is "cancel" or "rejected") EndDragTarget(signal.Id); DragMessage?.Invoke(signal); }
    }
    public bool Remote => remote;
    public string? Fault { get; private set; }
    public InputEngine()
    {
        thread = new Thread(HookLoop) { IsBackground = true, Name = "PC Connect input hooks" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); ready.Wait();
        injector = Task.Run(InjectLoop);
    }
    public void Attach(Session value, bool isPrimary, bool secondaryOnRight)
    {
        session = value; primary = isPrimary; right = secondaryOnRight;
        value.Input += e =>
        {
            if (!incoming.TryPut(new Packet("input", Input: e))) { value.RequestAbort(); ReleaseAll(); }
        };
        value.Drag += d => { if (!incoming.TryPut(new Packet("drag", Drag: d))) { targetGesture?.Cancel(); value.RequestAbort(); } };
        value.ReturnRequested += () => returnFlag = true;
        value.Closed += () => { targetGesture?.Cancel(); allowedDragId = null; returnFlag = true; incoming.Clear(); ReleaseAll(); DragInterrupted?.Invoke(); };
    }
    public void ReturnLocal() { returnFlag = true; if (!primary) session?.ReturnControl(); }
    void HookLoop()
    {
        try
        {
            bounds = Screen.PrimaryScreen!.Bounds;
            anchor = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
            window = new HookWindow(this);
            mouseProc = Mouse; keyProc = Keyboard;
            mouseHook = Native.SetWindowsHookEx(14, mouseProc, Native.GetModuleHandle(null), 0);
            keyHook = Native.SetWindowsHookEx(13, keyProc, Native.GetModuleHandle(null), 0);
            if (mouseHook == 0 || keyHook == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            using var timer = new System.Windows.Forms.Timer { Interval = 15 };
            timer.Tick += (_, _) =>
            {
                var currentBounds = Screen.PrimaryScreen!.Bounds;
                if (currentBounds != bounds) { Local(); bounds = currentBounds; anchor = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2); }
                if (returnFlag || (remote && session?.Connected != true)) { returnFlag = false; Local(); }
            };
            timer.Start(); ready.Set(); Application.Run();
        }
        catch (Exception ex) { Fault = "Input hooks unavailable: " + ex.Message; ready.Set(); }
        finally { if (mouseHook != 0) Native.UnhookWindowsHookEx(mouseHook); if (keyHook != 0) Native.UnhookWindowsHookEx(keyHook); window?.Destroy(); }
    }
    void Local()
    {
        targetGesture?.Cancel(); allowedDragId = null; DragInterrupted?.Invoke();
        if (remote)
        {
            remote = false;
            session?.Send(new InputEvent("release"));
            Native.SetCursorPos(right ? bounds.Right - 20 : bounds.Left + 20, Math.Clamp((int)(bounds.Top + remoteY * (bounds.Height - 1)), bounds.Top, bounds.Bottom - 1));
        }
        lock (injectionGate) { accepting = false; incoming.Clear(); ReleaseAll(); }
    }
    bool Send(InputEvent e)
    {
        if (session?.Send(e) == true) return true;
        remote = false; returnFlag = true; return false;
    }
    nint Mouse(int code, nint wParam, nint lParam)
    {
        if (code < 0) return Native.CallNextHookEx(0, code, wParam, lParam);
        var m = Marshal.PtrToStructure<Native.MouseHook>(lParam);
        if ((m.Flags & 1) != 0) return Native.CallNextHookEx(0, code, wParam, lParam);
        int message = (int)wParam;
        int button = message switch { 0x201 or 0x202 => 1, 0x204 or 0x205 => 2, 0x207 or 0x208 => 3, 0x20B or 0x20C => (int)(m.Data >> 16) == 1 ? 4 : 5, _ => 0 };
        bool up = message is 0x202 or 0x205 or 0x208 or 0x20C;
        if (button != 0) { if (up) buttons &= ~(1 << button); else buttons |= 1 << button; }
        if (targetGesture is { } gesture && (!primary || !remote))
        {
            if (button == 1 && up) gesture.Release();
            return Native.CallNextHookEx(0, code, wParam, lParam);
        }
        if (!primary || session?.Connected != true) return Native.CallNextHookEx(0, code, wParam, lParam);
        if (!remote && message == 0x200 && buttons == 0 && !physical.Any(v => v))
        {
            bool edge = right ? m.Point.X >= bounds.Right - 1 : m.Point.X <= bounds.Left;
            if (edge && m.Point.Y >= bounds.Top && m.Point.Y < bounds.Bottom)
            {
                remoteX = right ? 0.003 : 0.997; remoteY = Math.Clamp((m.Point.Y - bounds.Top) / (double)Math.Max(1, bounds.Height - 1), 0, 1);
                remote = true; Send(new InputEvent("enter")); Send(MoveEvent()); Native.SetCursorPos(anchor.X, anchor.Y); return 1;
            }
        }
        if (!remote) return Native.CallNextHookEx(0, code, wParam, lParam);
        if (message == 0x200)
        {
            remoteX += (m.Point.X - anchor.X) / (double)Math.Max(1, bounds.Width - 1);
            remoteY = Math.Clamp(remoteY + (m.Point.Y - anchor.Y) / (double)Math.Max(1, bounds.Height - 1), 0, 1);
            if (((right && remoteX < 0) || (!right && remoteX > 1)) && buttons == 0) { Local(); return 1; }
            remoteX = Math.Clamp(remoteX, 0, 1); Send(MoveEvent());
        }
        else if (button != 0) Send(new InputEvent("button", Code: button, Up: up));
        else if (message is 0x20A or 0x20E) Send(new InputEvent(message == 0x20A ? "wheel" : "hwheel", Code: unchecked((short)(m.Data >> 16))));
        return 1;
    }
    InputEvent MoveEvent() => new("move", (int)(remoteX * 65535), (int)(remoteY * 65535));
    nint Keyboard(int code, nint wParam, nint lParam)
    {
        if (code < 0) return Native.CallNextHookEx(0, code, wParam, lParam);
        var k = Marshal.PtrToStructure<Native.KeyHook>(lParam);
        if ((k.Flags & 16) != 0 || k.Vk > 255) return Native.CallNextHookEx(0, code, wParam, lParam);
        bool up = (k.Flags & 128) != 0;
        physical[k.Vk] = !up;
        bool ctrl = physical[0x11] || physical[0xA2] || physical[0xA3];
        bool alt = physical[0x12] || physical[0xA4] || physical[0xA5];
        if (k.Vk == 0x7B && !up && ctrl && alt)
        {
            Local(); if (!primary) session?.ReturnControl(); return 1;
        }
        if (targetGesture is { } gesture && k.Vk == 0x1B && !up) gesture.Cancel();
        if (primary && remote && session?.Connected == true)
        {
            Send(new InputEvent("key", X: (int)k.Scan, Code: (int)k.Vk, Up: up, Extended: (k.Flags & 1) != 0)); return 1;
        }
        return Native.CallNextHookEx(0, code, wParam, lParam);
    }
    async Task InjectLoop()
    {
        long desktopCheck = Stopwatch.GetTimestamp();
        try
        {
            while (!stop.IsCancellationRequested)
            {
                if (session?.Connected != true) { incoming.Clear(); accepting = false; ReleaseAll(); }
                if (Stopwatch.GetElapsedTime(desktopCheck).TotalMilliseconds > 250)
                {
                    desktopCheck = Stopwatch.GetTimestamp();
                    nint desktop = Native.OpenInputDesktop(0, false, 0x0100);
                    if (desktop == 0) { targetGesture?.Cancel(); returnFlag = true; ReleaseAll(); session?.RequestAbort(); }
                    else Native.CloseDesktop(desktop);
                }
                if (incoming.TryTake(out var packet))
                {
                    if (packet.Drag is { } signal) { ProcessDrag(signal); continue; }
                    if (packet.Input is not { } e) continue;
                    lock (injectionGate)
                    {
                        if (session?.Connected != true || primary) continue;
                        if (e.Kind is "release" or "enter") { targetGesture?.Cancel(); ReleaseAll(); accepting = e.Kind == "enter"; continue; }
                        if (!accepting) continue;
                        if (targetGesture is { } gesture)
                        {
                            if (e.Kind == "button" && e.Code == 1) { if (e.Up) gesture.Release(); else continue; }
                            if (e.Kind == "key") { if (e.Code == 0x1B && !e.Up) gesture.Cancel(); continue; }
                        }
                        if (e.Kind == "button" && e.Code == 1) remoteLeft = !e.Up;
                        Inject(e); held.Track(e);
                    }
                }
                else await incoming.WaitAsync(stop.Token, 250);
            }
        }
        catch (OperationCanceledException) { }
        finally { ReleaseAll(); }
    }
    void Inject(InputEvent e)
    {
        var input = new Native.Input();
        if (e.Kind == "key")
        {
            input.Type = 1;
            input.Value.Key = new Native.KeyInput { Vk = e.X == 0 ? (ushort)e.Code : (ushort)0, Scan = (ushort)e.X, Flags = (e.X != 0 ? 8u : 0u) | (e.Up ? 2u : 0u) | (e.Extended ? 1u : 0u) };
        }
        else
        {
            input.Value.Mouse = e.Kind switch
            {
                "move" => new Native.MouseInput { X = e.X, Y = e.Y, Flags = 0x8001 },
                "button" => new Native.MouseInput { Flags = e.Code switch { 1 => e.Up ? 4u : 2u, 2 => e.Up ? 16u : 8u, 3 => e.Up ? 64u : 32u, _ => e.Up ? 256u : 128u }, Data = e.Code >= 4 ? (uint)(e.Code - 3) : 0 },
                "wheel" or "hwheel" => new Native.MouseInput { Flags = e.Kind == "wheel" ? 0x800u : 0x1000u, Data = unchecked((uint)e.Code) },
                _ => default
            };
        }
        if (Native.SendInput(1, [input], Marshal.SizeOf<Native.Input>()) != 1)
        { Fault = "Windows blocked input injection. Elevated apps and secure desktops are unsupported."; session?.RequestAbort(); }
    }
    void ReleaseAll() { lock (injectionGate) { remoteLeft = false; foreach (var e in held.Release()) Inject(e); } }
    public void Dispose()
    {
        targetGesture?.Cancel(); returnFlag = true; stop.Cancel(); ReleaseAll();
        if (window != null) Native.PostMessage(window.Handle, 0x8001, 0, 0);
        thread.Join(1500); try { injector.Wait(1500); } catch (AggregateException) { }
    }
    sealed class HookWindow : NativeWindow
    {
        readonly InputEngine owner;
        public HookWindow(InputEngine owner) { this.owner = owner; CreateHandle(new CreateParams { Caption = "PC Connect input", Parent = new nint(-3) }); Native.RegisterHotKey(Handle, 1, 0x4003, 0x7B); }
        public void Destroy() { Native.UnregisterHotKey(Handle, 1); DestroyHandle(); }
        protected override void WndProc(ref Message m)
        { if (m.Msg == 0x312) { owner.Local(); if (!owner.primary) owner.session?.ReturnControl(); } else if (m.Msg == 0x8002) { while (owner.hookActions.TryDequeue(out var action)) action(); } else if (m.Msg == 0x8001) { owner.Local(); Application.ExitThread(); } else base.WndProc(ref m); }
    }
}
