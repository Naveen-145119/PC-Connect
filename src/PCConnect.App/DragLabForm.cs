using PCConnect.Core;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace PCConnect.App;

// Isolated interactive regression fixture: original OLE drag -> boundary -> a new OLE drag,
// with one physical held-button gesture. Uses the same destination host as production.
internal sealed class DragLabForm : Form
{
    readonly string root, sourcePath, cachePath, destination;
    readonly Control source, boundary, target;
    readonly OleDragHost ole = new();
    readonly Label result;
    bool running, handoff, received;
    int starts;
    public DragLabForm(string reportDirectory)
    {
        root = Path.Combine(Path.GetFullPath(reportDirectory), "ole-lab-" + Guid.NewGuid().ToString("N"));
        sourcePath = Path.Combine(root, "source"); cachePath = Path.Combine(root, "cache"); destination = Path.Combine(root, "destination");
        Directory.CreateDirectory(Path.Combine(sourcePath, "Folder 日本語", "empty")); Directory.CreateDirectory(cachePath); Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(sourcePath, "Drag fixture.txt"), "PC Connect native OLE fixture 🖥");
        File.WriteAllText(Path.Combine(sourcePath, "Folder 日本語", "తెలుగు.txt"), "Unicode test");
        foreach (var path in Directory.GetFileSystemEntries(sourcePath)) Copy(path, Path.Combine(cachePath, Path.GetFileName(path)));
        Text = "PC Connect · Native OLE handoff lab"; ClientSize = new Size(1000, 460); StartPosition = FormStartPosition.CenterScreen; BackColor = Theme.Bg; ForeColor = Theme.Ink; Font = Theme.Font(11);
        Controls.Add(new Label { Text = "One held drag. Two real OLE drag loops.", Bounds = new Rectangle(35, 22, 920, 52), Font = Theme.Font(22, true), ForeColor = Theme.Ink });
        Controls.Add(new Label { Text = "Drag from SOURCE through BOUNDARY to TARGET without releasing. Only disposable fixtures are used.", Bounds = new Rectangle(35, 86, 920, 48), ForeColor = Theme.Muted });
        source = Tile("SOURCE\n2 fixture items", new Rectangle(35, 160, 300, 170), Color.FromArgb(37, 57, 71));
        boundary = Tile("BOUNDARY\nOLE handoff", new Rectangle(420, 160, 160, 170), Color.FromArgb(80, 58, 31));
        target = Tile("TARGET\nRelease to copy", new Rectangle(665, 160, 300, 170), Color.FromArgb(32, 63, 52));
        result = new Label { Text = "Ready. Results are written under: " + root, Bounds = new Rectangle(35, 360, 930, 76), ForeColor = Theme.Muted }; Controls.Add(result);
        boundary.AllowDrop = target.AllowDrop = true;
        boundary.DragEnter += (_, e) =>
        {
            if (!running || e.Data?.GetData(DataFormats.FileDrop) is not string[] paths || !paths.All(p => p.StartsWith(sourcePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) { e.Effect = DragDropEffects.None; return; }
            handoff = true; e.Effect = DragDropEffects.Copy;
            nint capture = GetCapture(); if (capture != 0) Native.PostMessage(capture, 0x100, 0x1B, 0);
        };
        source.QueryContinueDrag += (_, e) => { if (handoff) e.Action = DragAction.Cancel; };
        target.DragEnter += (_, e) => e.Effect = e.Data?.GetData(DataFormats.FileDrop) is string[] paths && paths.All(p => p.StartsWith(cachePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) ? DragDropEffects.Copy : DragDropEffects.None;
        target.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is not string[] paths || !paths.All(p => p.StartsWith(cachePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) { e.Effect = DragDropEffects.None; return; }
            string batch = Path.Combine(destination, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(batch);
            foreach (var path in paths) Copy(path, Path.Combine(batch, Path.GetFileName(path)));
            received = File.ReadAllBytes(Path.Combine(batch, "Drag fixture.txt")).SequenceEqual(File.ReadAllBytes(Path.Combine(sourcePath, "Drag fixture.txt"))) && Directory.Exists(Path.Combine(batch, "Folder 日本語", "empty"));
            e.Effect = DragDropEffects.Copy;
        };
        source.MouseMove += (_, e) =>
        {
            if (running || e.Button != MouseButtons.Left) return;
            running = true; handoff = received = false; starts++; result.Text = "Original OLE drag active…";
            DragDropEffects first = DragDropEffects.None, second = DragDropEffects.None;
            try
            {
                first = source.DoDragDrop(new DataObject(DataFormats.FileDrop, Directory.GetFileSystemEntries(sourcePath)), DragDropEffects.Copy);
                if (handoff)
                {
                    result.Text = "Source loop canceled; destination OLE loop active with the same held gesture…";
                    var gesture = new DragGesture();
                    void Track(object? sender, QueryContinueDragEventArgs args)
                    {
                        if (args.EscapePressed) gesture.Cancel(); else if ((args.KeyState & 1) == 0) gesture.Release();
                        args.Action = gesture.QueryContinue(false) switch { 0 => DragAction.Continue, 0x40100 => DragAction.Drop, _ => DragAction.Cancel };
                    }
                    ole.QueryContinueDrag += Track;
                    try { second = ole.Run(Directory.GetFileSystemEntries(cachePath), gesture); }
                    finally { ole.QueryContinueDrag -= Track; }
                }
                bool passed = handoff && first == DragDropEffects.None && second == DragDropEffects.Copy && received;
                var report = new { Passed = passed, Canceled = !received, OriginalEffect = first.ToString(), DestinationEffect = second.ToString(), Handoff = handoff, Verified = received, Starts = starts, Runtime = Environment.Version.ToString(), OS = Environment.OSVersion.VersionString };
                File.AppendAllText(Path.Combine(root, "results.jsonl"), JsonSerializer.Serialize(report) + Environment.NewLine);
                result.Text = passed ? "PASS — one held gesture, source canceled, destination copied, Unicode/empty-folder fixtures verified." : "Canceled / no copy. Drag through the boundary and release over TARGET to run the success case.";
            }
            finally { running = false; }
        };
        FormClosed += (_, _) => ole.Dispose();
    }
    Control Tile(string text, Rectangle bounds, Color color)
    { var c = new Label { Text = text, Bounds = bounds, BackColor = color, ForeColor = Theme.Ink, Font = Theme.Font(17, true), TextAlign = ContentAlignment.MiddleCenter }; Controls.Add(c); return c; }
    static void Copy(string source, string destination)
    {
        if (Directory.Exists(source)) { Directory.CreateDirectory(destination); foreach (var child in Directory.GetFileSystemEntries(source)) Copy(child, Path.Combine(destination, Path.GetFileName(child))); }
        else File.Copy(source, destination, false);
    }
    [DllImport("user32.dll")] static extern nint GetCapture();
}
