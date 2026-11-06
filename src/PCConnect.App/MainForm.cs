using PCConnect.Core;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace PCConnect.App;

internal sealed class MainForm : Form
{
    readonly X509Certificate2 identity;
    readonly ChoiceControl role = SelectBox();
    readonly ChoiceControl side = SelectBox();
    readonly TextBox host = Edit("e.g. 192.168.1.24");
    readonly TextBox pin = Edit("Paste the fingerprint from your other laptop");
    readonly NumericUpDown port = Number(1024, 65534, 45831);
    readonly NumericUpDown rate = Number(128, 4096, 512);
    readonly Switch clipboard = new() { Text = "Share text clipboard", Checked = true, BackColor = Theme.Card };
    readonly Switch edgeToggle = new() { Text = "Cross-screen drag (preview)", Checked = true, BackColor = Theme.Card };
    readonly ActionButton connect = new() { Text = "Connect securely  →", Primary = true, Width = 210 };
    readonly Label status = TextLabel("Disconnected · local control", 9, Theme.Muted);
    readonly Label telemetry = TextLabel("Network RTT: —  |  Control: this laptop", 9, Theme.Muted);
    readonly Label transfer = TextLabel("Ready when you are. No transfer running.", 10, Theme.Muted);
    readonly Label rttValue = TextLabel("—", 26, Theme.Ink, true);
    readonly Label connectionBadge = TextLabel("○  NOT CONNECTED", 8, Theme.Muted, true);
    readonly ThinProgress progress = new() { BackColor = Theme.Card };
    readonly WorkspaceCanvas canvas = new();
    readonly ListBox received = new() { DisplayMember = "Name", BackColor = Theme.Field, ForeColor = Theme.Ink, BorderStyle = BorderStyle.None, Font = Theme.Font(11), IntegralHeight = false };
    readonly TextBox diagnostics = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, BackColor = Theme.Card, ForeColor = Theme.Muted, Font = new Font("Consolas", 10) };
    readonly Panel content = new() { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(34, 24, 34, 16) };
    readonly Label breadcrumb = TextLabel("WORKSPACE  /  OVERVIEW", 8, Theme.Muted, true);
    readonly List<ActionButton> navigation = [];
    readonly List<Panel> pages = [];
    readonly System.Windows.Forms.Timer timer = new() { Interval = 200 };
    readonly List<Control> settingsControls = [];
    PeerNode? node; InputEngine? input; DragBridge? dragBridge;
    uint clipboardSequence; string? lastClipboard; string? pendingClipboard; TransferProgress? pendingProgress;
    bool clipboardBusy, closing, closeAllowed;

    public MainForm(X509Certificate2 identity)
    {
        this.identity = identity; Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Text = "PC Connect  ·  Your connected workspace"; Font = Theme.Font(10); ForeColor = Theme.Ink; BackColor = Theme.Bg;
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1200, 900); MinimumSize = new Size(1100, 830); StartPosition = FormStartPosition.CenterScreen;
        var sidebar = new Panel { Dock = DockStyle.Left, Width = 236, BackColor = Theme.Sidebar, Padding = new Padding(18) };
        var logo = TextLabel("↔", 26, Theme.Accent, true); logo.SetBounds(23, 25, 48, 48); sidebar.Controls.Add(logo);
        var brand = TextLabel("PC Connect", 13, Theme.Ink, true); brand.SetBounds(73, 30, 155, 40); sidebar.Controls.Add(brand);
        var version = TextLabel("YOUR DESK. CONNECTED.", 7, Theme.Muted, true); version.SetBounds(25, 83, 180, 20); sidebar.Controls.Add(version);
        var menu = new[] { "◫   Overview", "⇄   Pair devices", "↗   File transfers", "≡   Diagnostics" };
        for (int i = 0; i < menu.Length; i++)
        {
            int index = i; var button = new ActionButton { Text = menu[i], Navigation = true, BackColor = Theme.Sidebar };
            button.SetBounds(17, 145 + i * 51, 202, 43); button.Click += (_, _) => ShowPage(index); sidebar.Controls.Add(button); navigation.Add(button);
        }
        var sidebarBottom = new Panel { Dock = DockStyle.Bottom, Height = 145, BackColor = Theme.Sidebar };
        var privacy = TextLabel("PRIVATE BY DESIGN", 8, Theme.Accent, true); privacy.SetBounds(9, 10, 175, 20);
        var privacyBody = TextLabel("Your network. Your devices.\nNo accounts. No cloud.", 9, Theme.Muted); privacyBody.SetBounds(9, 35, 195, 60);
        connectionBadge.SetBounds(9, 103, 175, 25); sidebarBottom.Controls.AddRange([privacy, privacyBody, connectionBadge]); sidebar.Controls.Add(sidebarBottom);
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 49, BackColor = Theme.Bg, Padding = new Padding(34, 0, 25, 0) };
        status.Dock = DockStyle.Fill; footer.Controls.Add(status);
        var main = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg }; main.Controls.Add(content); main.Controls.Add(footer);
        Controls.Add(main); Controls.Add(sidebar);
        role.Items.AddRange(["Primary · personal laptop", "Secondary · office laptop"]); side.Items.AddRange(["Office laptop on the right", "Office laptop on the left"]);
        BuildOverview(); BuildPairing(); BuildTransfers(); BuildDiagnostics();
        settingsControls.AddRange([role, side, host, pin, port, rate]);
        var saved = LocalData.Load(); role.SelectedIndex = saved.Primary ? 0 : 1; side.SelectedIndex = saved.Right ? 0 : 1;
        host.Text = saved.Host; pin.Text = saved.Fingerprint; clipboard.Checked = saved.Clipboard;
        port.Value = Math.Clamp(saved.Port, 1024, 65534); rate.Value = Math.Clamp(saved.TransferKiB, 128, 4096);
        connect.Click += async (_, _) => { if (node == null) await Start(); else await Stop(); };
        clipboard.CheckedChanged += (_, _) => { if (node != null) node.ClipboardEnabled = clipboard.Checked; lastClipboard = null; clipboardSequence = Native.GetClipboardSequenceNumber(); };
        edgeToggle.CheckedChanged += (_, _) => UpdateEdge();
        role.SelectedIndexChanged += (_, _) => { host.Enabled = role.SelectedIndex == 0 && node == null; };
        received.MouseDown += (_, e) =>
        {
            int index = received.IndexFromPoint(e.Location); if (e.Button != MouseButtons.Left || index < 0) return;
            received.SelectedIndex = index;
            if (received.Items[index] is ReceivedItem item && Directory.Exists(item.Path))
                received.DoDragDrop(new DataObject(DataFormats.FileDrop, Directory.GetFileSystemEntries(item.Path)), DragDropEffects.Copy);
        };
        timer.Tick += async (_, _) => await Tick(); timer.Start(); ShowPage(0);
        FormClosing += async (_, e) => { if (closeAllowed) return; e.Cancel = true; if (closing) return; closing = true; Enabled = false; timer.Stop(); await Stop(); closeAllowed = true; BeginInvoke(Close); };
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) { int dark = 1; DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)); }
    }
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
    static Label TextLabel(string text, float size, Color color, bool bold = false) => new() { Text = text, Font = Theme.Font(size, bold), ForeColor = color, BackColor = Color.Transparent, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Height = 28 };
    static TextBox Edit(string placeholder) => new() { PlaceholderText = placeholder, BorderStyle = BorderStyle.None, BackColor = Theme.Field, ForeColor = Theme.Ink, Font = Theme.Font(11) };
    static NumericUpDown Number(int min, int max, int value) => new() { Minimum = min, Maximum = max, Value = value, BorderStyle = BorderStyle.None, BackColor = Theme.Field, ForeColor = Theme.Ink, Font = Theme.Font(11) };
    static ChoiceControl SelectBox() => new();
    Panel Page(string title, string subtitle)
    {
        var page = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, AutoScroll = true, Visible = false };
        var stack = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(0, 0, 10, 0), BackColor = Theme.Bg };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); page.Controls.Add(stack);
        var header = new Panel { Height = 122, Dock = DockStyle.Top, BackColor = Theme.Bg, Margin = new Padding(0) };
        var kicker = TextLabel("WORKSPACE  /  " + title.ToUpperInvariant(), 8, Theme.Muted, true); kicker.SetBounds(0, 0, 600, 18);
        var heading = TextLabel(title == "Overview" ? "Your desk, connected." : title, 26, Theme.Ink, true); heading.SetBounds(0, 23, 800, 70); heading.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        var detail = TextLabel(subtitle, 10, Theme.Muted); detail.SetBounds(0, 96, 800, 26); detail.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        header.Controls.AddRange([kicker, heading, detail]); stack.Controls.Add(header); pages.Add(page); content.Controls.Add(page); return stack;
    }
    static void Add(Panel stack, Control control) { control.Dock = DockStyle.Top; control.Margin = new Padding(0, 12, 0, 0); stack.Controls.Add(control); }
    static Surface Card(int height) => new() { Height = height, Dock = DockStyle.Top };
    static void LabelAt(Control parent, string text, int x, int y, int width, float size = 10, Color? color = null, bool bold = false, int height = 28)
    { var label = TextLabel(text, size, color ?? Theme.Ink, bold); label.SetBounds(x, y, width, height); parent.Controls.Add(label); }
    static Panel Field(Control input, string name, int x, int y, int width)
    {
        input.AccessibleName = name;
        var field = new Panel { BackColor = Theme.Field, Padding = new Padding(12, 10, 12, 8) }; field.SetBounds(x, y, width, 42);
        input.Dock = DockStyle.Fill; field.Controls.Add(input); return field;
    }
    static ActionButton Button(string text, Action action, bool primary = false)
    { var button = new ActionButton { Text = text, Primary = primary }; button.Click += (_, _) => action(); return button; }
    void BuildOverview()
    {
        var stack = Page("Overview", "Two independent laptops. One effortless workspace."); Add(stack, canvas);
        var metrics = new TableLayoutPanel { Height = 126, ColumnCount = 3, BackColor = Theme.Bg };
        for (int i = 0; i < 3; i++) metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        var latency = Card(126); latency.Dock = DockStyle.Fill; latency.Margin = new Padding(0, 0, 12, 0);
        LabelAt(latency, "NETWORK RTT", 20, 14, 200, 8, Theme.Muted, true); rttValue.SetBounds(19, 40, 225, 44); latency.Controls.Add(rttValue); LabelAt(latency, "Live network round-trip time", 20, 90, 250, 9, Theme.Muted);
        var secure = Card(126); secure.Dock = DockStyle.Fill; secure.Margin = new Padding(0, 0, 12, 0);
        LabelAt(secure, "PRIVATE CONNECTION", 20, 14, 220, 8, Theme.Muted, true); LabelAt(secure, "Encrypted", 20, 42, 250, 18, Theme.Ink, true, 40); LabelAt(secure, "Authenticated on both laptops", 20, 90, 270, 9, Theme.Muted);
        var clip = Card(126); clip.Dock = DockStyle.Fill; clip.Margin = Padding.Empty;
        LabelAt(clip, "BETTER TOGETHER", 20, 14, 220, 8, Theme.Muted, true); clipboard.SetBounds(20, 47, 250, 36); clip.Controls.Add(clipboard); LabelAt(clip, "Copy here. Paste over there.", 20, 90, 250, 9, Theme.Muted);
        metrics.Controls.Add(latency, 0, 0); metrics.Controls.Add(secure, 1, 0); metrics.Controls.Add(clip, 2, 0); Add(stack, metrics);
        var start = Card(104); LabelAt(start, "Make room for more.", 22, 14, 440, 16, Theme.Ink, true, 35); LabelAt(start, "Pair your laptops, arrange your screens, and get into flow.", 22, 53, 520, 10, Theme.Muted);
        var pair = Button("Pair your laptops  →", () => ShowPage(1), true); pair.Width = 195; pair.Anchor = AnchorStyles.Right | AnchorStyles.Top; start.Controls.Add(pair);
        start.Resize += (_, _) => pair.Location = new Point(start.Width - pair.Width - 22, 31); Add(stack, start);
        var bottom = new Panel { Height = 48, BackColor = Theme.Bg }; LabelAt(bottom, "Always in control.  Ctrl + Alt + F12 returns you to this laptop.", 0, 0, 700, 9, Theme.Muted, height: 44); Add(stack, bottom);
    }
    void BuildPairing()
    {
        var stack = Page("Pairing & screens", "A trusted connection starts with two fingerprints. Exchange them directly.");
        var layout = Card(112); LabelAt(layout, "THIS LAPTOP’S ROLE", 22, 12, 260, 8, Theme.Muted, true); LabelAt(layout, "SCREEN ARRANGEMENT", 350, 12, 330, 8, Theme.Muted, true);
        var roleField = Field(role, "This laptop role", 22, 46, 305); var sideField = Field(side, "Screen arrangement", 350, 46, 370); layout.Controls.AddRange([roleField, sideField]); Add(stack, layout);
        var local = Card(174); LabelAt(local, "01   THIS DEVICE", 22, 13, 350, 8, Theme.Accent, true);
        string addresses = string.Join("  ·  ", Dns.GetHostAddresses(Dns.GetHostName()).Where(a => a.AddressFamily == AddressFamily.InterNetwork).Select(a => a.ToString()));
        LabelAt(local, Environment.MachineName, 22, 40, 700, 14, Theme.Ink, true, 30); LabelAt(local, "Local IP   " + addresses, 22, 75, 800, 9, Theme.Muted);
        var own = Edit(""); own.Text = Security.Fingerprint(identity); own.ReadOnly = true; own.Font = new Font("Consolas", 10);
        var ownField = Field(own, "This laptop fingerprint", 22, 112, 640); ownField.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; local.Controls.Add(ownField);
        var copy = Button("Copy code", () => { try { Clipboard.SetText(own.Text); clipboardSequence = Native.GetClipboardSequenceNumber(); Log("Fingerprint copied. Paste it into your other laptop’s pairing screen."); } catch { Log("Clipboard busy; try again."); } }); copy.Width = 150; copy.Anchor = AnchorStyles.Right | AnchorStyles.Top; local.Controls.Add(copy);
        local.Resize += (_, _) => { copy.Location = new Point(local.Width - 172, 112); ownField.Width = local.Width - 210; }; Add(stack, local);
        var remote = Card(255); LabelAt(remote, "02   YOUR OTHER LAPTOP", 22, 13, 450, 8, Theme.Accent, true);
        LabelAt(remote, "Device fingerprint", 22, 45, 400, 10, Theme.Muted);
        var pinField = Field(pin, "Other laptop fingerprint", 22, 78, 750); pinField.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; remote.Controls.Add(pinField);
        LabelAt(remote, "Secondary laptop IP address", 22, 135, 350, 9, Theme.Muted); LabelAt(remote, "Port", 350, 131, 110, 9, Theme.Muted);
        remote.Controls.Add(Field(host, "Secondary laptop IP address", 22, 166, 305)); remote.Controls.Add(Field(port, "Port", 350, 166, 110));
        connect.Anchor = AnchorStyles.Right | AnchorStyles.Top; remote.Controls.Add(connect);
        remote.Resize += (_, _) => { pinField.Width = remote.Width - 44; connect.Location = new Point(remote.Width - connect.Width - 22, 166); };
        LabelAt(remote, "Use the same port on both laptops. Keep both apps open while connecting.", 22, 218, 780, 9, Theme.Muted); Add(stack, remote);
        var escape = new Panel { Height = 57, BackColor = Theme.Bg }; var back = Button("↶  Return control", () => input?.ReturnLocal()); back.SetBounds(0, 6, 170, 40); escape.Controls.Add(back); LabelAt(escape, "Emergency shortcut: Ctrl + Alt + F12", 192, 8, 600, 9, Theme.Muted, height: 40); Add(stack, escape);
    }
    void BuildTransfers()
    {
        var stack = Page("File transfers", "Send a copy. Keep your originals. Pick up exactly where you left off.");
        var drop = new DropCanvas(); BindDrop(drop); drop.Click += (_, _) => ChooseFiles(); Add(stack, drop);
        var actions = new Panel { Height = 49, BackColor = Theme.Bg };
        var files = Button("Choose files", ChooseFiles, true); files.SetBounds(0, 2, 150, 42); var folder = Button("Choose folder", ChooseFolder); folder.SetBounds(162, 2, 150, 42); var cancel = Button("Cancel transfer", () => { dragBridge?.Cancel(); node?.CancelTransfers(); }); cancel.SetBounds(324, 2, 150, 42); actions.Controls.AddRange([files, folder, cancel]); Add(stack, actions);
        var state = Card(75); transfer.SetBounds(22, 14, 800, 30); transfer.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; state.Controls.Add(transfer); progress.SetBounds(22, 56, 750, 5); progress.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; state.Controls.Add(progress); state.Resize += (_, _) => { progress.Width = state.Width - 44; transfer.Width = state.Width - 44; }; Add(stack, state);
        var inbox = Card(165); LabelAt(inbox, "Your arrivals", 22, 13, 500, 16, Theme.Ink, true, 36); LabelAt(inbox, "Drag a received item into File Explorer or your desktop.", 22, 51, 700, 10, Theme.Muted);
        received.SetBounds(22, 89, 750, 54); received.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; inbox.Controls.Add(received);
        var open = Button("Open inbox  ↗", OpenInbox); open.Width = 150; open.Anchor = AnchorStyles.Top | AnchorStyles.Right; inbox.Controls.Add(open); inbox.Resize += (_, _) => { open.Location = new Point(inbox.Width - 172, 17); received.Width = inbox.Width - 44; }; Add(stack, inbox);
        var tuning = Card(74); edgeToggle.SetBounds(22, 22, 290, 36); tuning.Controls.Add(edgeToggle); LabelAt(tuning, "Transfer cap  ·  KiB/s", 350, 26, 175, 9, Theme.Muted); tuning.Controls.Add(Field(rate, "Transfer rate limit KiB per second", 540, 21, 110)); Add(stack, tuning);
        var limitation = TextLabel("Hold files at the screen edge until verified, then keep dragging into Explorer on the other laptop. Copy only.", 9, Theme.Muted); limitation.Height = 36; Add(stack, limitation);
    }
    void BuildDiagnostics()
    {
        var stack = Page("Diagnostics", "Connection health, without recording your keys, clipboard text, or file contents.");
        var health = Card(92); LabelAt(health, "LIVE CONNECTION HEALTH", 22, 12, 500, 8, Theme.Accent, true); telemetry.SetBounds(22, 44, 800, 30); telemetry.Font = Theme.Font(13); health.Controls.Add(telemetry); Add(stack, health);
        var log = Card(340); LabelAt(log, "Session activity", 22, 13, 700, 16, Theme.Ink, true, 36); diagnostics.SetBounds(22, 67, 750, 247); diagnostics.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; log.Controls.Add(diagnostics); log.Resize += (_, _) => diagnostics.Width = log.Width - 44; Add(stack, log);
        var boundaries = Card(134); LabelAt(boundaries, "Designed for the Windows desktop", 22, 14, 700, 13, Theme.Ink, true, 32); LabelAt(boundaries, "UAC prompts, elevated applications and login screens remain under Windows control.\nNetwork RTT measures the connection, not total keyboard-to-screen latency.\nOne primary display per laptop. Your apps keep running independently.", 22, 52, 810, 10, Theme.Muted, height: 66); Add(stack, boundaries);
    }
    void ShowPage(int index)
    {
        for (int i = 0; i < pages.Count; i++) { pages[i].Visible = i == index; navigation[i].Selected = i == index; navigation[i].Invalidate(); }
        pages[index].BringToFront();
    }
    void UI(Action action) { if (!IsDisposed && IsHandleCreated) { try { BeginInvoke(action); } catch (InvalidOperationException) { } } }
    void Log(string text)
    {
        status.Text = text;
        diagnostics.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine);
        if (diagnostics.Lines.Length > 100) diagnostics.Lines = diagnostics.Lines.TakeLast(80).ToArray();
    }
    async Task Start()
    {
        try
        {
            string fingerprint = Security.Normalize(pin.Text);
            if (fingerprint == Security.Fingerprint(identity)) throw new ArgumentException("Paste the OTHER laptop’s fingerprint, not this laptop’s fingerprint.");
            bool primary = role.SelectedIndex == 0;
            if (primary && string.IsNullOrWhiteSpace(host.Text)) throw new ArgumentException("Enter the secondary laptop’s IP address.");
            var settings = new Settings(primary, host.Text.Trim(), fingerprint, side.SelectedIndex == 0, clipboard.Checked, (int)port.Value, (int)rate.Value);
            LocalData.Save(settings);
            input = new InputEngine();
            if (input.Fault != null) throw new IOException(input.Fault);
            node = new PeerNode(identity, new PeerOptions(primary, settings.Host, settings.Port, fingerprint, LocalData.Inbox, settings.TransferKiB, DragCache: Path.Combine(LocalData.Root, "DragCache"))) { ClipboardEnabled = clipboard.Checked };
            var activeNode = node;
            node.Connected += s => input?.Attach(s, primary, settings.Right);
            node.Status += message => UI(() => { if (node == activeNode) Log(message); });
            node.Progress += p => Interlocked.Exchange(ref pendingProgress, p);
            node.FilesReceived += path => UI(() => { received.Items.Insert(0, new ReceivedItem(path)); Log("Files received and verified. Drag the received item into your desired folder."); });
            node.ClipboardReceived += text => { if (node == activeNode) Interlocked.Exchange(ref pendingClipboard, text); };
            dragBridge = new DragBridge(this, node, input, primary, settings.Right, Log) { Enabled = edgeToggle.Checked };
            node.Start(); clipboardSequence = Native.GetClipboardSequenceNumber();
            foreach (var c in settingsControls) c.Enabled = false;
            connect.Text = "Disconnect"; UpdateEdge();
        }
        catch (Exception ex) { await Stop(); Log(ex.Message); }
    }
    async Task Stop()
    {
        dragBridge?.Dispose(); dragBridge = null; var old = node; node = null;
        input?.ReturnLocal(); if (old != null) await old.DisposeAsync(); input?.Dispose(); input = null;
        foreach (var c in settingsControls) c.Enabled = true; connect.Text = "Connect securely  →";
        Log("Disconnected · local control");
    }
    async Task Tick()
    {
        if (Interlocked.Exchange(ref pendingProgress, null) is { } update) { progress.Value = update.Total == 0 ? 100 : (int)Math.Clamp(update.Bytes * 100.0 / update.Total, 0, 100); transfer.Text = $"{update.State} · {update.Bytes / 1048576.0:F1} / {update.Total / 1048576.0:F1} MiB"; }
        if (Interlocked.Exchange(ref pendingClipboard, null) is { } incoming && clipboard.Checked && node?.Current?.Connected == true)
        {
            try { if (incoming.Length == 0) Clipboard.Clear(); else Clipboard.SetText(incoming); lastClipboard = incoming; clipboardSequence = Native.GetClipboardSequenceNumber(); }
            catch (System.Runtime.InteropServices.ExternalException) { Log("Clipboard busy; incoming text could not be applied."); }
        }
        var current = node?.Current;
        telemetry.Text = $"Network RTT: {(current?.Connected == true ? current.RttMs.ToString("F1") + " ms" : "—")}   |   Control: {(input?.Remote == true ? "other laptop" : "this laptop")}";
        rttValue.Text = current?.Connected == true ? current.RttMs.ToString("F1") + " ms" : "—";
        canvas.Connected = current?.Connected == true; canvas.Remote = input?.Remote == true; canvas.SecondaryRight = side.SelectedIndex == 0; canvas.IsPrimary = role.SelectedIndex == 0; canvas.Invalidate();
        connectionBadge.Text = current?.Connected == true ? "●  CONNECTED" : node != null ? "◌  CONNECTING" : "○  NOT CONNECTED";
        connectionBadge.ForeColor = current?.Connected == true ? Theme.Green : Theme.Muted;
        if (input?.Fault is { } fault && status.Text != fault) Log(fault);
        if (!clipboard.Checked || clipboardBusy || current?.Connected != true) return;
        uint sequence = Native.GetClipboardSequenceNumber(); if (sequence == clipboardSequence) return;
        clipboardBusy = true;
        try
        {
            if (Clipboard.ContainsText())
            {
                string text = Clipboard.GetText();
                if (text.Length <= 65536 && text != lastClipboard) { lastClipboard = text; if (node != null) await node.SendClipboard(text); }
            }
            clipboardSequence = sequence;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { /* retry clipboard contention on a later change; never log text */ clipboardSequence = sequence; }
        finally { clipboardBusy = false; }
    }
    void ChooseFiles() { using var dialog = new OpenFileDialog { Multiselect = true, Title = "Choose files to copy to the other laptop" }; if (dialog.ShowDialog(this) == DialogResult.OK) _ = Send(dialog.FileNames); }
    void ChooseFolder() { using var dialog = new FolderBrowserDialog { Description = "Choose a folder to copy to the other laptop", UseDescriptionForTitle = true }; if (dialog.ShowDialog(this) == DialogResult.OK) _ = Send([dialog.SelectedPath]); }
    async Task Send(string[] paths)
    {
        if (node == null) { Log("Connect to the other laptop before sending files."); return; }
        try { await node.SendFiles(paths); }
        catch (OperationCanceledException) { Log("Transfer canceled. Incomplete receiver files are discarded."); }
        catch (Exception ex) { Log("Transfer failed: " + ex.Message); }
    }
    void BindDrop(Control control)
    {
        control.AllowDrop = true;
        control.DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        if (control is DropCanvas drop) { control.DragEnter += (_, _) => { drop.Hovering = true; drop.Invalidate(); }; control.DragLeave += (_, _) => { drop.Hovering = false; drop.Invalidate(); }; control.DragDrop += (_, _) => { drop.Hovering = false; drop.Invalidate(); }; }
        control.DragDrop += (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths) _ = Send(paths); };
    }
    void UpdateEdge() { if (dragBridge != null) dragBridge.Enabled = edgeToggle.Checked; }
    static void OpenInbox() { Directory.CreateDirectory(LocalData.Inbox); var info = new ProcessStartInfo("explorer.exe") { UseShellExecute = true }; info.ArgumentList.Add(LocalData.Inbox); Process.Start(info); }
    sealed record ReceivedItem(string Path) { public string Name => System.IO.Path.GetFileName(Path); }
}
