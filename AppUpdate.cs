using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ZapretControlApp;

public enum UpdateStatus { Failed, UpToDate, Available }

public class ReleaseInfo
{
    public Version Version;
    public string Tag, PageUrl, Notes, AssetName, AssetUrl, Sha256;
    public long Size;
    public bool IsZip;
}

public class UpdateCheck
{
    public UpdateStatus Status;
    public ReleaseInfo Info;
}

public static class AppUpdate
{
    public const string RepoUrl = "https://github.com/Jan-Sova13/zapret-control-app";
    const string Api = "https://api.github.com/repos/Jan-Sova13/zapret-control-app/releases/latest";
    const string InstalledName = "Zapret Control App.exe";

    static Version Normalize(Version v) => new Version(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));

    public static Version Current => Normalize(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));

    public static string Format(Version v) => "v" + v.Major + "." + v.Minor + "." + v.Build;

    public static string UpdateDir => Path.Combine(AppSettings.Dir, "update");

    static HttpClient NewHttp(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ZapretControlApp/" + Format(Current));
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    static bool IsGithubHttps(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase);

    /// <summary>Релизов нет, непонятный тег или версия не новее — UpToDate; нет связи или ответ не разобрать — Failed.</summary>
    public static async Task<UpdateCheck> CheckAsync(CancellationToken ct)
    {
        try
        {
            using var http = NewHttp(TimeSpan.FromSeconds(10));
            using var resp = await http.GetAsync(Api, ct);
            if (resp.StatusCode == HttpStatusCode.NotFound) return new UpdateCheck { Status = UpdateStatus.UpToDate };
            if (!resp.IsSuccessStatusCode) return new UpdateCheck { Status = UpdateStatus.Failed };

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var rootEl = doc.RootElement;
            string tag = rootEl.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest) || Normalize(latest) <= Current)
                return new UpdateCheck { Status = UpdateStatus.UpToDate };

            var info = new ReleaseInfo
            {
                Version = Normalize(latest),
                PageUrl = rootEl.TryGetProperty("html_url", out var hu) && !string.IsNullOrEmpty(hu.GetString()) ? hu.GetString() : RepoUrl + "/releases/latest",
                Notes = rootEl.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
            };
            info.Tag = Format(info.Version);

            string sumsUrl = null;
            JsonElement? best = null;
            if (rootEl.TryGetProperty("assets", out var assets))
            {
                foreach (var a in assets.EnumerateArray())
                {
                    string name = a.GetProperty("name").GetString() ?? "";
                    string url = a.GetProperty("browser_download_url").GetString() ?? "";
                    if (name.IndexOf("sha256", StringComparison.OrdinalIgnoreCase) >= 0 && IsGithubHttps(url)) { sumsUrl = url; continue; }
                    bool exe = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase), zip = name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
                    if (!exe && !zip) continue;
                    if (best == null || (exe && !best.Value.GetProperty("name").GetString().EndsWith(".exe", StringComparison.OrdinalIgnoreCase))) best = a;
                }
            }

            if (best != null && IsGithubHttps(best.Value.GetProperty("browser_download_url").GetString()))
            {
                var a = best.Value;
                info.AssetName = a.GetProperty("name").GetString();
                info.AssetUrl = a.GetProperty("browser_download_url").GetString();
                info.IsZip = info.AssetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
                info.Size = a.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0;
                if (a.TryGetProperty("digest", out var dg) && dg.ValueKind == JsonValueKind.String)
                {
                    string d = dg.GetString() ?? "";
                    if (d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) info.Sha256 = d.Substring(7).ToLowerInvariant();
                }
                if (info.Sha256 == null && sumsUrl != null) info.Sha256 = await ReadSumAsync(http, sumsUrl, info.AssetName, ct);
            }
            return new UpdateCheck { Status = UpdateStatus.Available, Info = info };
        }
        catch { return new UpdateCheck { Status = UpdateStatus.Failed }; }
    }

    static async Task<string> ReadSumAsync(HttpClient http, string url, string assetName, CancellationToken ct)
    {
        try
        {
            string text = await http.GetStringAsync(url, ct);
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                var m = Regex.Match(raw.Trim(), @"^([0-9a-fA-F]{64})\s+\*?(.+)$");
                if (m.Success && m.Groups[2].Value.Trim().Equals(assetName, StringComparison.OrdinalIgnoreCase)) return m.Groups[1].Value.ToLowerInvariant();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { }
        return null;
    }

    /// <summary>Описание релиза обычным текстом (без разметки Markdown).</summary>
    public static string PlainNotes(string md)
    {
        if (string.IsNullOrWhiteSpace(md)) return "Описание релиза не указано.";
        string t = md.Replace("\r", "");
        t = Regex.Replace(t, @"^#{1,6}\s*", "", RegexOptions.Multiline);
        t = Regex.Replace(t, @"\[([^\]]+)\]\([^)]+\)", "$1");
        t = Regex.Replace(t, @"(\*\*|__|`)", "");
        t = Regex.Replace(t, @"^\s*[-*]\s+", "• ", RegexOptions.Multiline);
        t = Regex.Replace(t, @"\n{3,}", "\n\n");
        return t.Trim().Replace("\n", Environment.NewLine);
    }

    /// <summary>Скачивает релиз в папку настроек, сверяет размер и контрольную сумму, возвращает путь к готовому exe.</summary>
    public static async Task<string> DownloadAsync(ReleaseInfo info, IProgress<(long Done, long Total)> progress, CancellationToken ct)
    {
        if (info.AssetUrl == null) throw new InvalidOperationException("В релизе нет файла, который можно загрузить автоматически.");

        Directory.CreateDirectory(UpdateDir);
        try { foreach (var f in Directory.GetFiles(UpdateDir)) File.Delete(f); } catch { }

        string tmp = Path.Combine(UpdateDir, "download.tmp");
        string hash;
        using (var http = NewHttp(TimeSpan.FromMinutes(10)))
        {
            using var resp = await http.GetAsync(info.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            var final = resp.RequestMessage?.RequestUri;
            if (final == null || final.Scheme != Uri.UriSchemeHttps ||
                !(final.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || final.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Загрузка перенаправлена на недоверенный адрес.");

            long total = resp.Content.Headers.ContentLength ?? info.Size;
            using var inc = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long done = 0;
            using (var src = await resp.Content.ReadAsStreamAsync(ct))
            using (var dst = File.Create(tmp))
            {
                var buf = new byte[81920];
                int n;
                while ((n = await src.ReadAsync(buf, 0, buf.Length, ct)) > 0)
                {
                    await dst.WriteAsync(buf, 0, n, ct);
                    inc.AppendData(buf, 0, n);
                    done += n;
                    progress?.Report((done, total));
                }
            }
            if (info.Size > 0 && done != info.Size) throw new IOException("Файл скачан не полностью (обрыв соединения).");
            hash = Convert.ToHexString(inc.GetHashAndReset()).ToLowerInvariant();
        }

        if (info.Sha256 != null && !hash.Equals(info.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(tmp); } catch { }
            throw new InvalidDataException("Контрольная сумма не совпала: файл повреждён или подменён. Обновление отменено.");
        }

        string exe = Path.Combine(UpdateDir, "update.exe");
        if (info.IsZip)
        {
            using var zip = ZipFile.OpenRead(tmp);
            var entry = zip.Entries.Where(e => e.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(e => e.Name.Equals(InstalledName, StringComparison.OrdinalIgnoreCase)).ThenByDescending(e => e.Length).FirstOrDefault();
            if (entry == null) throw new InvalidDataException("В архиве нет exe-файла.");
            entry.ExtractToFile(exe, true);
            File.Delete(tmp);
        }
        else File.Move(tmp, exe, true);

        using (var fs = File.OpenRead(exe))
            if (fs.Length < 1_000_000 || fs.ReadByte() != 'M' || fs.ReadByte() != 'Z')
                throw new InvalidDataException("Загруженный файл не похож на программу.");
        return exe;
    }

    /// <summary>Запускает загруженный exe в режиме «завершить обновление». Возвращает текст ошибки или null.
    /// UAC не появляется: процесс уже запущен от администратора и наследует права.</summary>
    public static string LaunchInstaller(string newExe, bool resumeBypass)
    {
        try
        {
            var psi = new ProcessStartInfo(newExe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(newExe) };
            psi.ArgumentList.Add("--finish-update");
            psi.ArgumentList.Add(Environment.ProcessPath);
            psi.ArgumentList.Add(Environment.ProcessId.ToString());
            if (resumeBypass) psi.ArgumentList.Add("--resume-bypass");
            Process.Start(psi)?.Dispose();
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    /// <summary>Работает в новом экземпляре: ждёт выхода старого, заменяет его exe и запускает обновлённую программу.</summary>
    public static int FinishUpdate(string[] args)
    {
        try
        {
            string target = args[1];
            int pid = int.Parse(args[2]);
            bool resume = args.Contains("--resume-bypass");
            try { using var old = Process.GetProcessById(pid); old.WaitForExit(30000); } catch { }

            string self = Environment.ProcessPath, staged = target + ".new", backup = target + ".bak";
            Exception last = null;
            bool done = false;
            for (int i = 0; i < 40 && !done; i++)
            {
                try
                {
                    File.Copy(self, staged, true);
                    if (File.Exists(target)) File.Replace(staged, target, backup, true); else File.Move(staged, target);
                    done = true;
                }
                catch (Exception ex) { last = ex; Thread.Sleep(500); }
            }

            if (!done)
            {
                Diag.Write("finish-update", last);
                MessageBox.Show("Не удалось заменить файл программы: " + last?.Message + "\n\nСтарая версия осталась рабочей. Скачайте обновление вручную:\n" + RepoUrl + "/releases/latest",
                    "Zapret Control App", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                StartTarget(target, null, resume);
                return 1;
            }
            StartTarget(target, "--updated", resume);
            try { File.Delete(backup); } catch { }
            return 0;
        }
        catch (Exception ex)
        {
            Diag.Write("finish-update", ex);
            MessageBox.Show("Не удалось завершить обновление: " + ex.Message + "\n\nСкачайте программу вручную:\n" + RepoUrl + "/releases/latest",
                "Zapret Control App", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }
    }

    static void StartTarget(string target, string flag, bool resume)
    {
        try
        {
            var psi = new ProcessStartInfo(target) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target) };
            if (flag != null) psi.ArgumentList.Add(flag);
            if (resume) psi.ArgumentList.Add("--resume-bypass");
            Process.Start(psi)?.Dispose();
        }
        catch (Exception ex) { Diag.Write("start-target", ex); }
    }

    /// <summary>Чистит остатки обновления; запущенный временный exe может ещё закрываться, поэтому есть повторы.</summary>
    public static void CleanupTemp()
    {
        try
        {
            string exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
                foreach (var ext in new[] { ".new", ".bak" })
                    try { if (File.Exists(exe + ext)) File.Delete(exe + ext); } catch { }
        }
        catch { }
        for (int i = 0; i < 10; i++)
        {
            try { if (Directory.Exists(UpdateDir)) Directory.Delete(UpdateDir, true); return; }
            catch { Thread.Sleep(2000); }
        }
    }
}
