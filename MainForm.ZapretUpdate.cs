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

    /// <summary>Кнопка в шапке и подпись в настройках показываются, только если установленный Zapret старее последнего релиза.</summary>
    void RefreshZapretUpdateUi()
    {
        if (headerInfo == null) return;
        string cur = Strategy.IsValidZapretDir(cfg.ZapretPath) ? Strategy.ReadVersion(cfg.ZapretPath) : null;
        bool outdated = TryVer(zapretLatest, out var latest) && TryVer(cur, out var now) && latest > now;
        btnUpdate.Visible = lblZapretNew.Visible = outdated;
        headerInfo.ColumnStyles[1].Width = outdated ? Ui.S(190) : 0;
        if (lblZapretUpdate != null)
            lblZapretUpdate.Text = outdated ? "Вышла новая версия: " + zapretLatest.TrimStart('v', 'V') + " (сейчас " + cur + ")" : "";
    }
}
