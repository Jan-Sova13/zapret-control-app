using System.Text.Json;

namespace ZapretControlApp;

public class AppSettings
{
    public const string AppName = "Zapret Control App";

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
    public List<string> TestServices { get; set; } = new List<string>(TestTargets.ServiceNames);
    public List<string> CustomDomains { get; set; } = new List<string>();

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
                    if (s.TestServices == null) s.TestServices = new List<string>(TestTargets.ServiceNames);
                    if (s.CustomDomains == null) s.CustomDomains = new List<string>();
                    if (s.Theme != "light" && s.Theme != "green") s.Theme = "dark";
                    return s;
                }
            }
        }
        catch { }
        return new AppSettings();
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
