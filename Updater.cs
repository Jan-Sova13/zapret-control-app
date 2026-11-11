using System.IO.Compression;
using System.Text.Json;

namespace ZapretControlApp;

public static class Updater
{
    const string Api = "https://api.github.com/repos/Flowseal/zapret-discord-youtube/releases/latest";

    /// <summary>Скачивает последний релиз и полностью заменяет bin / lists / utils / .bat в папке zapret.
    /// Свои списки (*-user.txt) сохраняются. Версии не сравниваются — обновляется всегда.</summary>
    public static async Task<string> UpdateAsync(string zapretDir, Action<string> log, CancellationToken ct)
    {
        // Защита: не трогаем папку, которая не похожа на zapret и не пустая
        bool exists = Directory.Exists(zapretDir);
        bool empty = !exists || !Directory.EnumerateFileSystemEntries(zapretDir).Any();
        if (!empty && !Strategy.IsValidZapretDir(zapretDir))
            throw new InvalidOperationException("Папка не пустая и не похожа на zapret (нет bin\\winws.exe). Чтобы не удалить лишнего, обновление отменено.");

        string tmp = Path.Combine(Path.GetTempPath(), "zapretctl_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ZapretControlApp/" + AppUpdate.Format(AppUpdate.Current));
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            log("Запрашиваю последний релиз на GitHub…");
            string json = await http.GetStringAsync(Api, ct);
            using var doc = JsonDocument.Parse(json);
            string tag = doc.RootElement.GetProperty("tag_name").GetString();
            string url = null;
            foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
            {
                string name = a.GetProperty("name").GetString() ?? "";
                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    url = a.GetProperty("browser_download_url").GetString();
                    break;
                }
            }
            if (url == null) throw new Exception("В релизе " + tag + " нет zip-архива.");

            log("Скачиваю " + tag + "…");
            string zip = Path.Combine(tmp, "z.zip");
            using (var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                using var fs = File.Create(zip);
                await resp.Content.CopyToAsync(fs, ct);
            }

            log("Распаковываю…");
            string extract = Path.Combine(tmp, "x");
            await Task.Run(() => ZipFile.ExtractToDirectory(zip, extract), ct);
            string root = FindRoot(extract, 0);
            if (root == null) throw new Exception("В архиве не найдена папка с bin и lists.");

            log("Заменяю файлы…");
            await Task.Run(() => Replace(zapretDir, root, log), ct);
            return tag;
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { }
        }
    }

    static string FindRoot(string dir, int depth)
    {
        if (Directory.Exists(Path.Combine(dir, "bin")) && Directory.Exists(Path.Combine(dir, "lists"))) return dir;
        if (depth >= 3) return null;
        foreach (var d in Directory.GetDirectories(dir))
        {
            var r = FindRoot(d, depth + 1);
            if (r != null) return r;
        }
        return null;
    }

    static void Replace(string target, string newRoot, Action<string> log)
    {
        Directory.CreateDirectory(target);

        // 1. Освобождаем драйвер, чтобы не было блокировки WinDivert64.sys
        Runner.CleanupDriver();

        // 2. Сохраняем свои списки и делаем бэкап старых lists в AppData
        var keep = new Dictionary<string, byte[]>();
        string lists = Path.Combine(target, "lists");
        if (Directory.Exists(lists))
        {
            foreach (var f in Directory.GetFiles(lists, "*-user.txt"))
                keep[Path.GetFileName(f)] = File.ReadAllBytes(f);

            string backup = Path.Combine(AppSettings.Dir, "backup", DateTime.Now.ToString("yyyyMMdd-HHmmss"), "lists");
            try { CopyDir(lists, backup); log("Бэкап старых списков: " + backup); } catch { }
        }

        // 3. Удаляем старое (только известные части zapret)
        foreach (var d in new[] { "bin", "lists", "utils", ".service" })
        {
            string p = Path.Combine(target, d);
            if (Directory.Exists(p)) Directory.Delete(p, true);
        }
        foreach (var f in Directory.GetFiles(target, "*.bat")) File.Delete(f);

        // 4. Копируем новое
        CopyDir(newRoot, target);

        // 5. Возвращаем свои списки
        Directory.CreateDirectory(lists);
        foreach (var kv in keep) File.WriteAllBytes(Path.Combine(lists, kv.Key), kv.Value);
    }

    static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src))
            File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(src))
            CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }
}
