namespace ZapretControlApp;

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
        Application.ThreadException += (s, e) => Diag.Write("ui-thread", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (s, e) => Crash(form, e.ExceptionObject as Exception);

        bool autostart = args.Contains("--autostart");
        form = new MainForm(autostart);
        Application.Run(form);
    }

    /// <summary>Фатальная ошибка вне интерфейса: гасим обход и драйвер, пишем лог и выходим.
    /// Если не успеем (процесс убит), winws всё равно умрёт вместе с нами (Job Object).</summary>
    static void Crash(MainForm form, Exception ex)
    {
        try { form?.EmergencyShutdown(); } catch { }
        Diag.Write("fatal", ex);
        Environment.Exit(1);
    }
}
