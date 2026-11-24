using System.Text.RegularExpressions;

namespace ZapretControlApp;

public partial class MainForm
{
    string zapretLatest;

    static bool TryVer(string s, out Version v)
    {
        v = null;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var m = Regex.Match(s, @"\d+(\.\d+){1,3}");
        return m.Success && Version.TryParse(m.Value, out v);
    }

    async Task CheckZapretVersionAsync()
    {
        string tag = await Updater.GetLatestTagAsync(CancellationToken.None);
        if (tag == null || shuttingDown || IsDisposed) return;
        zapretLatest = tag;
        RefreshZapretUpdateUi();
    }

    bool ZapretInstalled => Strategy.IsValidZapretDir(cfg.ZapretPath);

    /// <summary>Кнопка в шапке: «Установить Zapret», если его нет в выбранной папке, и «Обновить Zapret», если вышла новая версия. В настройках кнопка видна всегда.</summary>
    void RefreshZapretUpdateUi()
    {
        if (headerInfo == null) return;
        bool installed = ZapretInstalled;
        string cur = installed ? Strategy.ReadVersion(cfg.ZapretPath) : null;
        bool outdated = installed && TryVer(zapretLatest, out var latest) && TryVer(cur, out var now) && latest > now;
        bool show = !installed || outdated;
        string caption = installed ? "Обновить Zapret" : "Установить Zapret";
        btnUpdate.Text = btnUpdateSettings.Text = caption;
        btnUpdate.Visible = lblZapretNew.Visible = show;
        lblZapretNew.Text = installed ? "Вышла новая версия" : "Zapret не найден";
        headerInfo.ColumnStyles[1].Width = show ? Ui.S(190) : 0;
        if (lblZapretUpdate != null)
            lblZapretUpdate.Text = outdated ? "Вышла новая версия: " + zapretLatest.TrimStart('v', 'V') + " (сейчас " + cur + ")" : "";
    }
}
