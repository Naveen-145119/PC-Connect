using PCConnect.Core;
using System.Runtime.InteropServices;

namespace PCConnect.App;

// Uses WinForms' real OLE IDataObject/IDropSource implementation. Only QueryContinueDrag's
// button lifetime is overridden: a remote physical button is not the local OS's physical button.
internal sealed class OleDragHost : Control
{
    DragGesture? gesture;
    public OleDragHost() { CreateControl(); }
    public DragDropEffects Run(string[] paths, DragGesture active)
    {
        if (!active.Held || paths.Length == 0) return DragDropEffects.None;
        gesture = active;
        var data = new DataObject(DataFormats.FileDrop, paths);
        data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(1)));
        using var wake = new System.Windows.Forms.Timer { Interval = 20 };
        wake.Tick += (_, _) =>
        {
            if (active.Held) return;
            // GetCapture only returns a window on this STA. No global synthetic click is needed.
            nint capture = GetCapture();
            if (capture != 0) Native.PostMessage(capture, active.Canceled ? 0x100u : 0x202u, active.Canceled ? 0x1B : 0, 0);
        };
        wake.Start();
        try { return DoDragDrop(data, DragDropEffects.Copy); }
        finally { wake.Stop(); gesture = null; GC.KeepAlive(data); }
    }
    protected override void OnQueryContinueDrag(QueryContinueDragEventArgs e)
    {
        e.Action = gesture?.QueryContinue(e.EscapePressed) switch
        { 0 => DragAction.Continue, 0x40100 => DragAction.Drop, _ => DragAction.Cancel };
        base.OnQueryContinueDrag(e);
    }
    [DllImport("user32.dll")] static extern nint GetCapture();
}
