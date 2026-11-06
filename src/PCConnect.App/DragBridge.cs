using PCConnect.Core;
using System.Diagnostics;

namespace PCConnect.App;

// UI/STA coordinator. All file IO and TLS transfer work is delegated to PeerNode, never to input hooks.
internal sealed class DragBridge : IDisposable
{
    readonly Control owner;
    readonly PeerNode node;
    readonly InputEngine input;
    readonly bool primary, secondaryRight;
    readonly Action<string> status;
    readonly EdgePortal portal;
    readonly DragHud hud = new();
    readonly OleDragHost ole = new();
    readonly System.Windows.Forms.Timer timer = new() { Interval = 100 };
    Operation? operation;
    bool disposed;
    public bool Enabled { get; set; } = true;
    public DragBridge(Control owner, PeerNode node, InputEngine input, bool primary, bool secondaryRight, Action<string> status)
    {
        this.owner = owner; this.node = node; this.input = input; this.primary = primary; this.secondaryRight = secondaryRight; this.status = status;
        portal = new EdgePortal(primary ? secondaryRight : !secondaryRight);
        portal.DragEnter += Enter;
        portal.DragOver += (_, e) =>
        {
            e.Effect = operation is { State.Source: true, State.Terminal: false } ? DragDropEffects.Copy : DragDropEffects.None;
            if ((e.KeyState & 1) == 0 && operation?.State.Phase != DragPhase.Active) Cancel("Drag canceled before handoff.");
        };
        portal.DragLeave += (_, _) => { if (operation is { State.Source: true } op && op.State.Phase != DragPhase.Active) Cancel("Drag preparation canceled; source files are unchanged."); };
        portal.DragDrop += (_, e) => { e.Effect = DragDropEffects.None; if (operation?.State.Phase != DragPhase.Active) Cancel("Released before handoff. Keep holding at the edge until the pointer crosses."); };
        input.DragMessage += Message;
        input.DragInterrupted += Interrupted;
        node.DragFilesPrepared += Prepared;
        node.DragTransferFailed += Failed;
        timer.Tick += (_, _) => Tick(); timer.Start();
    }
    void Dispatch(Action action)
    {
        if (disposed || owner.IsDisposed || !owner.IsHandleCreated) return;
        try { owner.BeginInvoke(() => { if (!disposed) action(); }); } catch (InvalidOperationException) { }
    }
    void Message(DragSignal signal) => Dispatch(() => OnMessage(signal));
    void Interrupted() => Dispatch(() => Cancel("Drag canceled; local control restored."));
    void Prepared(string id, string folder) => Dispatch(() =>
    {
        if (operation is not { State.Source: false } op || op.State.Id != id || !op.State.Transition("ready")) return;
        op.Folder = folder; input.ArmDragTarget(id); Send("ready", id); ShowHud("Files verified. Waiting for the handoff…");
    });
    void Failed(string id) => Dispatch(() => { if (operation?.State.Id == id) Cancel("Drag preparation failed; no drop was performed."); });
    void Enter(object? sender, DragEventArgs e)
    {
        e.Effect = DragDropEffects.None;
        if (!Enabled || node.Current?.Connected != true || !input.CanStartDrag || operation != null || (e.KeyState & 1) == 0 || (e.AllowedEffect & DragDropEffects.Copy) == 0) return;
        try
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length is < 1 or > Transfers.MaxEntries) return;
            var op = new Operation(Guid.NewGuid().ToString("N"), true) { Paths = paths.ToArray() };
            operation = op; e.Effect = DragDropEffects.Copy;
            ShowHud("Hold at the edge · preparing a secure copy…"); status("Cross-screen drag: keep the left button held while files are prepared.");
            if (!Send("offer", op.State.Id)) Cancel("Unable to start the drag handoff.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Cancel("This drag source could not provide ordinary files or folders."); }
    }
    void OnMessage(DragSignal signal)
    {
        if (signal.Kind == "offer")
        {
            if (!Enabled || operation != null || !node.ArmIncomingDrag(signal.Id)) { Send("rejected", signal.Id); return; }
            operation = new Operation(signal.Id, false); operation.State.Transition("accepted"); Send("accepted", signal.Id); return;
        }
        if (operation is not { } op || op.State.Id != signal.Id) return; // Ignore stale IDs, never resurface a canceled gesture.
        switch (signal.Kind)
        {
            case "accepted" when op.State.Source && op.State.Transition("accepted"):
                _ = Prepare(op); break;
            case "ready" when op.State.Source && op.State.Transition("ready"):
                _ = Handoff(op); break;
            case "activate" when !op.State.Source && op.State.Transition("activate"):
                _ = owner.BeginInvoke(() => RunTarget(op)); break;
            case "finished" when op.State.Source && op.State.Transition("finished"):
                Finish(op, "Drop accepted by the other laptop. Explorer completes the destination copy."); break;
            case "cancel" or "rejected":
                Cancel(signal.Kind == "rejected" ? "Other laptop is busy or cross-screen drag is disabled." : "Cross-screen drag canceled.", false); break;
        }
    }
    async Task Prepare(Operation op)
    {
        try { await node.SendFiles(op.Paths!, op.State.Id, op.Stop.Token); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { if (operation == op && !op.State.Terminal) Cancel(ex is OperationCanceledException ? "Drag canceled." : "Drag transfer failed; source files are unchanged."); }
    }
    async Task Handoff(Operation op)
    {
        if (operation != op) return;
        if (!input.LeftHeld) { Cancel("Mouse released before handoff; no drop was performed."); return; }
        op.State.Transition("activate"); // Source OLE DragLeave during cancellation is now expected, not a user cancel.
        ShowHud("Crossing screens · keep holding…");
        bool handed;
        try { handed = await input.HandoffSource(op.State.Id, op.Stop.Token).WaitAsync(TimeSpan.FromSeconds(3)); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { if (operation == op) Cancel("Drag handoff timed out or sharing stopped."); return; }
        if (operation != op || op.State.Terminal) return;
        if (!handed) { Cancel("Windows could not hand off this drag."); return; }
        hud.Hide(); status("Drag on the other screen; release over an Explorer folder or the desktop to copy.");
    }
    void RunTarget(Operation op)
    {
        if (operation != op) return;
        if (op.State.Phase != DragPhase.Active || op.Folder == null || input.TargetDragId != op.State.Id || input.TargetGesture is not { } gesture) { Cancel("Drag handoff was no longer active."); return; }
        if (!gesture.Held) { Cancel("Mouse released before the destination drag was ready."); return; }
        portal.Hide(); ShowHud("Release in an Explorer folder to copy · Esc cancels");
        try
        {
            var effect = ole.Run(Directory.GetFileSystemEntries(op.Folder), gesture);
            if (operation != op || op.State.Terminal) return;
            if (effect == DragDropEffects.Copy && !gesture.Canceled)
            {
                op.State.Transition("finished"); Send("finished", op.State.Id);
                Finish(op, "Drop accepted. Explorer is responsible for completing the destination copy.");
            }
            else Cancel("Drop canceled or the destination does not accept file copies.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Cancel("Windows could not complete the OLE drop. Verified cache and originals are retained."); }
        finally { input.EndDragTarget(op.State.Id); node.RevokeIncomingDrag(op.State.Id); }
    }
    void Tick()
    {
        bool connected = node.Current?.Connected == true;
        if ((!connected || !Enabled) && operation != null) Cancel("Drag canceled because sharing stopped.");
        if (operation is { } op)
        {
            if (op.Age.Elapsed > TimeSpan.FromMinutes(10)) Cancel("Drag timed out after 10 minutes; no source files were removed.");
            else if (op.State.Source && op.State.Phase is DragPhase.Offered or DragPhase.Preparing or DragPhase.Ready && !input.LeftHeld) Cancel("Mouse released before handoff; no drop was performed.");
        }
        bool show = Enabled && connected && operation is not { State.Source: false, State.Phase: DragPhase.Active };
        if (show && !portal.Visible) { portal.UpdateEdge(); portal.Show(); }
        else if (!show && portal.Visible) portal.Hide();
    }
    public void Cancel(string message = "Cross-screen drag canceled.", bool notifyPeer = true)
    {
        if (operation is not { } op || op.State.Terminal) return;
        bool active = op.State.Phase == DragPhase.Active;
        op.State.Transition("cancel"); op.Stop.Cancel(); input.EndDragTarget(op.State.Id); node.RevokeIncomingDrag(op.State.Id);
        if (notifyPeer) Send("cancel", op.State.Id);
        if (active && op.State.Source && primary) input.ReturnLocal();
        Finish(op, message);
    }
    void Finish(Operation op, string message)
    {
        if (operation != op) return;
        operation = null; hud.Hide(); status(message);
        // Materialized cache is retained: Explorer may copy CF_HDROP asynchronously after DoDragDrop returns.
    }
    bool Send(string kind, string id) => node.Current?.SendDrag(new DragSignal(kind, id)) == true;
    void ShowHud(string text)
    {
        var screen = Screen.PrimaryScreen!.WorkingArea;
        hud.Message = text; hud.Location = new Point(screen.Left + (screen.Width - hud.Width) / 2, screen.Top + 24);
        if (!hud.Visible) hud.Show();
    }
    public void Dispose()
    {
        if (disposed) return; Cancel(); disposed = true; timer.Stop(); timer.Dispose();
        input.DragMessage -= Message; input.DragInterrupted -= Interrupted; node.DragFilesPrepared -= Prepared; node.DragTransferFailed -= Failed;
        portal.Close(); portal.Dispose(); hud.Close(); hud.Dispose(); ole.Dispose();
    }
    sealed class Operation(string id, bool source)
    {
        public DragTransaction State { get; } = new(id, source);
        public CancellationTokenSource Stop { get; } = new();
        public Stopwatch Age { get; } = Stopwatch.StartNew();
        public string[]? Paths { get; init; }
        public string? Folder { get; set; }
    }
    sealed class EdgePortal : Form
    {
        readonly bool right;
        public EdgePortal(bool right)
        {
            this.right = right; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
            StartPosition = FormStartPosition.Manual; BackColor = Theme.Accent; Opacity = .2; AllowDrop = true;
            Text = "Cross-screen drag boundary"; UpdateEdge();
        }
        public void UpdateEdge() { var s = Screen.PrimaryScreen!.Bounds; Bounds = new Rectangle(right ? s.Right - 12 : s.Left, s.Top, 12, s.Height); }
        protected override bool ShowWithoutActivation => true;
    }
    sealed class DragHud : Form
    {
        readonly Label label = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Ink, BackColor = Theme.Card, Font = Theme.Font(10, true) };
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string Message { set => label.Text = value; }
        public DragHud() { FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual; Size = new Size(560, 48); Controls.Add(label); }
        protected override bool ShowWithoutActivation => true;
    }
}
