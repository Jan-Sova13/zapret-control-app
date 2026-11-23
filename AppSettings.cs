using System.Text.Json;

namespace ZapretControlApp;

public class AppSettings
{
    public const string AppName = "Zapret Control App";
    const int CurrentSettingsVersion = 2;
    static readonly string[] DefaultServices = { "YouTube", "Discord", "Cloudflare" };

    public string ZapretPath { get; set; } = "";
    public string Strategy { get; set; } = "";
    public string GameFilter { get; set; } = "off";   // off | all | tcp | udp
    public string Theme { get; set; } = "dark";       // dark | light | green
    public bool AutoStartApp { get; set; }
    public bool StartInTray { get; set; }
    public bool StartBypassOnLaunch { get; set; }
    public bool ManageTimestamps { get; set; } = true;
    public bool TimestampsChangedByUs { get; set; }
    public bool DriverMayLinger { get; set; }
    public bool StopOnFirstSuccess { get; set; } = true;
    public List<string> TestServices { get; set; } = new List<string>(DefaultServices);
    public List<string> CustomDomains { get; set; } = new List<string>();

    // v2: интерфейс
    public int SettingsVersion { get; set; }
    public bool AdvancedMode { get; set; }
    public string Notifications { get; set; } = "important";   // all | important | off | offhide

    // v2: сторожевой режим
    public bool WatchEnabled { get; set; } = true;
    public int WatchIntervalMin { get; set; } = 5;              // 0 — выкл, 2 | 5 | 10
    public bool RestartOnWake { get; set; } = true;
    public bool RestartOnCrash { get; set; } = true;
    public List<string> WatchServices { get; set; } = new List<string>(DefaultServices);
    public string OnProblems { get; set; } = "report";            // report | once | ten

    // v2: автоподбор
    public string AutoMode { get; set; } = "quick";             // quick | deep
    public int DeepCandidates { get; set; } = 5;
    public int SpeedThresholdKB { get; set; } = 300;
    public string LastAutoAt { get; set; } = "";
    public string LastAutoResult { get; set; } = "";

    // v2: обновление программы
    public bool AutoCheckUpdates { get; set; } = true;
    public string SkippedVersion { get; set; } = "";
    public string NotifiedVersion { get; set; } = "";

    static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    public static string Dir => Path.Combine(AppData, AppName);
    static string LegacyDir => Path.Combine(AppData, "ZapretControl");
    static string FilePath => Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        // Старая версия хранила настройки в %AppData%\ZapretControl — переносим папку целиком
        try
        {
            if (!Directory.Exists(Dir) && Directory.Exists(LegacyDir)) Directory.Move(LegacyDir, Dir);
        }
        catch { }

        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (s != null)
                {
                    s.Normalize();
                    if (s.SettingsVersion < CurrentSettingsVersion)
                    {
                        s.SettingsVersion = CurrentSettingsVersion;
                        s.Save();
                    }
                    return s;
                }
            }
        }
        catch { }
        return new AppSettings { SettingsVersion = CurrentSettingsVersion };
    }

    /// <summary>Отсутствующие и некорректные значения приводятся к допустимым: старый settings.json читается без ошибок.</summary>
    void Normalize()
    {
        if (TestServices == null) TestServices = new List<string>(DefaultServices);
        if (WatchServices == null) WatchServices = new List<string>(DefaultServices);
        TestServices.RemoveAll(n => !TestTargets.ServiceNames.Contains(n));
        WatchServices.RemoveAll(n => !TestTargets.ServiceNames.Contains(n));
        if (CustomDomains == null) CustomDomains = new List<string>();
        if (Theme != "light" && Theme != "green") Theme = "dark";
        if (Notifications != "off" && Notifications != "all" && Notifications != "offhide") Notifications = "important";
        if (WatchIntervalMin != 0 && WatchIntervalMin != 2 && WatchIntervalMin != 10) WatchIntervalMin = 5;
        if (OnProblems != "once" && OnProblems != "ten") OnProblems = "report";
        if (AutoMode != "deep") AutoMode = "quick";
        if (DeepCandidates < 1 || DeepCandidates > 20) DeepCandidates = 5;
        if (SpeedThresholdKB < 50 || SpeedThresholdKB > 100000) SpeedThresholdKB = 300;
        LastAutoAt ??= "";
        LastAutoResult ??= "";
        SkippedVersion ??= "";
        NotifiedVersion ??= "";
    }

    /// <summary>Удаляет всё, что программа хранит в %AppData%: настройки, журнал, бэкапы, временные файлы обновления. Возвращает текст ошибки или null.</summary>
    public static string DeleteAll()
    {
        string err = null;
        foreach (var d in new[] { Dir, LegacyDir })
        {
            try { if (Directory.Exists(d)) Directory.Delete(d, true); }
            catch (Exception ex) { err = ex.Message; }
        }
        return err;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
