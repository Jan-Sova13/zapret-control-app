namespace ZapretControlApp;

/// <summary>Журнал ошибок %AppData%\Zapret Control App\crash.log: по нему можно разобрать падение или сбой отрисовки.</summary>
public static class Diag
{
    static readonly object Sync = new object();

    public static void Write(string where, Exception ex)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(AppSettings.Dir);
                string path = Path.Combine(AppSettings.Dir, "crash.log");
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > 200_000) fi.Delete();
                File.AppendAllText(path,
                    DateTime.Now + " [" + where + "] " + AppUpdate.Format(AppUpdate.Current) + Environment.NewLine +
                    (ex?.ToString() ?? "unknown") + Environment.NewLine + Environment.NewLine);
            }
        }
        catch { }
    }
}
