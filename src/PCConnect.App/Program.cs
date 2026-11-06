namespace PCConnect.App;
internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length == 2 && args[0] == "--drag-lab") { using var lab = new DragLabForm(args[1]); Application.Run(lab); return; }
        using var singleton = new Mutex(true, "Local\\PCConnect-" + Environment.UserName, out bool first);
        if (!first) { MessageBox.Show("PC Connect is already running for this user."); return; }
        try
        {
            using var identity = LocalData.Identity();
            if (args.Length == 2 && args[0] == "--self-check")
            {
                using var engine = new InputEngine();
                using var reopened = LocalData.Identity();
                bool valid = engine.Fault == null && PCConnect.Core.Security.Fingerprint(identity) == PCConnect.Core.Security.Fingerprint(reopened) && System.Runtime.InteropServices.Marshal.SizeOf<Native.Input>() == (IntPtr.Size == 8 ? 40 : 28);
                File.WriteAllText(args[1], System.Text.Json.JsonSerializer.Serialize(new { Passed = valid, Hooks = engine.Fault ?? "Installed and removed on dedicated thread", Identity = "DPAPI identity reopened", InputSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.Input>(), Runtime = Environment.Version.ToString(), OS = Environment.OSVersion.VersionString }));
                Environment.ExitCode = valid ? 0 : 1; return;
            }
            using var form = new MainForm(identity);
            if (args.Contains("--smoke"))
            {
                using var timer = new System.Windows.Forms.Timer { Interval = 1500 };
                timer.Tick += (_, _) => { timer.Stop(); form.Close(); }; timer.Start(); Application.Run(form);
            }
            else Application.Run(form);
        }
        catch (Exception ex) { MessageBox.Show("PC Connect could not start. " + ex.Message, "PC Connect", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
