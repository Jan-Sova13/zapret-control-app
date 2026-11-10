namespace ZapretControl;

static class Program
{
    static Mutex _mutex;

    [STAThread]
    static void Main(string[] args)
    {
        _mutex = new Mutex(true, "Global\\ZapretControlApp.SingleInstance", out bool created);
        if (!created)
        {
            MessageBox.Show("Программа уже запущена (проверьте значок в трее).", "Zapret Control App",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        MainForm form = null;
        Application.ThreadException += (s, e) => Crash(form, e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (s, e) => Crash(form, e.ExceptionObject as Exception);

        bool autostart = args.Contains("--autostart");
        form = new MainForm(autostart);
        Application.Run(form);
    }

    // При падении: гасим обход и драйвер, пишем лог и выходим.
    // Если не успеем (например, процесс убит) — winws всё равно умрёт вместе с нами (Job Object).
    static void Crash(MainForm form, Exception ex)
    {
        try { form?.EmergencyShutdown(); } catch { }
        try
        {
            Directory.CreateDirectory(AppSettings.Dir);
            File.WriteAllText(Path.Combine(AppSettings.Dir, "crash.log"), DateTime.Now + Environment.NewLine + (ex?.ToString() ?? "unknown"));
        }
        catch { }
        Environment.Exit(1);
    }
}
