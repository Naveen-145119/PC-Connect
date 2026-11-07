using System.Runtime.InteropServices;
using PCConnect.App;
using PCConnect.Core;

namespace PCConnect.UiTests;

internal static class Program
{
    [STAThread]
    static int Main()
    {
        Application.EnableVisualStyles();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Exception? uiError = null;
        Application.ThreadException += (_, args) => uiError ??= args.Exception;
        try
        {
            using var identity = Security.CreateIdentity();
            using var form = new MainForm(identity) { ShowInTaskbar = false };
            form.Show();
            var controls = Descendants(form).ToList();
            controls.OfType<Button>().Single(button => button.Text.Contains("Pair devices")).PerformClick();
            var role = controls.OfType<ChoiceControl>().Single(c => c.AccessibleName == "This laptop role");
            var side = controls.OfType<ChoiceControl>().Single(c => c.AccessibleName == "Screen arrangement");
            var host = controls.OfType<TextBox>().Single(c => c.AccessibleName == "Secondary laptop IP address");

            foreach (var choice in new[] { role, side })
            {
                choice.SelectedIndex = 0;
                for (int pass = 0; pass < 2; pass++)
                {
                    foreach (int index in new[] { 1, 0 })
                    {
                        var menu = Open(choice);
                        menu.Items[index].PerformClick();
                        Application.DoEvents();
                        if (uiError != null) throw new InvalidOperationException("Dropdown click raised a UI exception.", uiError);
                        if (menu.Visible || choice.SelectedIndex != index)
                            throw new InvalidOperationException($"{choice.AccessibleName} did not commit option {index} and dismiss.");
                        if (choice == role && host.Enabled != (index == 0))
                            throw new InvalidOperationException("Role change did not update the secondary IP field.");
                    }
                }
                var dismissed = Open(choice);
                dismissed.Close(ToolStripDropDownCloseReason.Keyboard);
                if (choice.SelectedIndex != 0) throw new InvalidOperationException("Dismissal changed the selection.");
                var reopened = Open(choice);
                reopened.Close();
            }
            if (uiError != null) throw uiError;
            Console.WriteLine("PASS repeated role and arrangement selection, IP field state, dismissal and reopening");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    // Send the opening key only to this test's control. Find its real popup on the current UI thread.
    static ContextMenuStrip Open(ChoiceControl choice)
    {
        choice.Focus();
        SendMessage(choice.Handle, 0x0100, (nint)Keys.Space, 0);
        Application.DoEvents();
        ContextMenuStrip? result = null;
        EnumThreadWindows(GetCurrentThreadId(), (window, _) =>
        {
            if (Control.FromHandle(window) is ContextMenuStrip menu && menu.Visible && menu.SourceControl == choice)
                result = menu;
            return result == null;
        }, 0);
        return result ?? throw new InvalidOperationException($"Could not open {choice.AccessibleName}.");
    }

    static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    delegate bool EnumWindow(nint window, nint parameter);
    [DllImport("user32.dll")] static extern bool EnumThreadWindows(uint thread, EnumWindow callback, nint parameter);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
}
