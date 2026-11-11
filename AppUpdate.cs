using System.Reflection;
using System.Text.Json;

namespace ZapretControlApp;

public static class AppUpdate
{
    public const string RepoUrl = "https://github.com/Jan-Sova13/zapret-control-app";
    const string Api = "https://api.github.com/repos/Jan-Sova13/zapret-control-app/releases/latest";

    static Version Normalize(Version v) => new Version(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));

    public static Version Current => Normalize(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));

    public static string Format(Version v) => "v" + v.Major + "." + v.Minor + "." + v.Build;

    /// <summary>Ссылка на страницу релиза, если на GitHub есть версия новее текущей. Во всех остальных случаях
    /// (релизов нет, нет доступа к GitHub, непонятный тег) возвращает null и ничего не сообщает.</summary>
    public static async Task<string> CheckAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ZapretControlApp/" + Format(Current));
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            using var resp = await http.GetAsync(Api);
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            string tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest)) return null;
            if (Normalize(latest) <= Current) return null;

            return doc.RootElement.TryGetProperty("html_url", out var url) && !string.IsNullOrEmpty(url.GetString())
                ? url.GetString()
                : RepoUrl + "/releases/latest";
        }
        catch { return null; }
    }
}
