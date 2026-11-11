using System.Text;
using System.Text.RegularExpressions;

namespace ZapretControlApp;

public partial class MainForm
{
    Check[] chkServices;
    Check chkStopFirst;
    ScrollText txtCustom;
    SimpleList lbResults;
    ThinProgress prog;
    Label lblAuto;
    RoundButton btnAuto, btnAutoCancel, btnApplyBest, btnAddToList;
    CancellationTokenSource autoCts;
    List<TestResult> results = new List<TestResult>();
    List<TestResult> resultView = new List<TestResult>();

    Panel BuildPageAuto()
    {
        var p = Page();
        var t = Grid(1, 3);
        Cols(t, Fill());
        Rows(t, Abs(72), Fill(), Abs(76));

        var hint = Caption(
            "Для каждой стратегии программа запускает winws, открывает по HTTPS сайты выбранных сервисов и ваши домены и считает, сколько открылось. " +
            "Стратегии проверяются по очереди: одновременно их запускать нельзя, они бы перехватывали трафик друг друга. " +
            "VPN на время проверки лучше выключить. Telegram zapret чаще всего не лечит.",
            Fonts.Small, Muted, 72);
        hint.Dock = DockStyle.Fill;
        hint.TextAlign = ContentAlignment.TopLeft;
        t.Controls.Add(hint, 0, 0);

        var body = Grid(2, 1);
        Cols(body, (SizeType.Percent, 42), (SizeType.Percent, 58));

        var left = Grid(1, 6);
        left.Margin = Ui.Pad(0, 0, 16, 0);
        Rows(left, Abs(26), Abs(66), Abs(34), Abs(26), Fill(), Abs(46));
        left.Controls.Add(Lbl("Что проверять:", Muted), 0, 0);

        var sf = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Bg, Margin = Padding.Empty };
        chkServices = new Check[TestTargets.Services.Length];
        for (int i = 0; i < chkServices.Length; i++)
        {
            var c = Chk(TestTargets.Services[i].Name);
            c.Margin = Ui.Pad(0, 0, 14, 6);
            c.Checked = cfg.TestServices.Contains(TestTargets.Services[i].Name);
            chkServices[i] = c;
            sf.Controls.Add(c);
        }
        left.Controls.Add(sf, 0, 1);

        chkStopFirst = Chk("Остановиться, как только всё откроется");
        chkStopFirst.Checked = cfg.StopOnFirstSuccess;
        tip.SetToolTip(chkStopFirst, "Проверка закончится на первой стратегии, при которой открылись все выбранные сервисы и домены.\nБыстрее, но полного рейтинга остальных стратегий не будет.");
        left.Controls.Add(chkStopFirst, 0, 2);

        left.Controls.Add(Lbl("Свои домены для проверки (по одному в строке):", Muted), 0, 3);
        txtCustom = Input(true);
        txtCustom.Dock = DockStyle.Fill;
        txtCustom.Text = string.Join(Environment.NewLine, cfg.CustomDomains);
        left.Controls.Add(txtCustom, 0, 4);

        var lf = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Bg, Padding = Ui.Pad(0, 12, 0, 0), Margin = Padding.Empty };
        btnAddToList = Btn("Добавить мои домены в список обхода", Line, 340, 34);
        tip.SetToolTip(btnAddToList,
            "Записывает домены из поля выше в «Свой список: домены для обхода».\n" +
            "zapret обрабатывает только домены из своих списков: если домена там нет,\n" +
            "обход на него не действует, и проверка покажет, что он «не открылся»\n" +
            "при любой стратегии. Сервисы выше (YouTube, Discord…) уже есть в стоковых\n" +
            "списках, добавлять их не нужно.");
        btnAddToList.Click += (s, e) => AddDomainsToUserList(ParseDomains(txtCustom.Text), false);
        lf.Controls.Add(btnAddToList);
        left.Controls.Add(lf, 0, 5);
        body.Controls.Add(left, 0, 0);

        var right = Grid(1, 2);
        Rows(right, Abs(26), Fill());
        right.Controls.Add(Lbl("Результаты (двойной клик — выбрать стратегию):", Muted), 0, 0);
        lbResults = new SimpleList { Dock = DockStyle.Fill, Font = Fonts.Mono, BackColor = Field, ForeColor = TextC, ShowTips = true, RowHeight = Ui.S(26), Margin = Padding.Empty };
        lbResults.ItemActivated += (s, e) => ApplySelectedResult();
        right.Controls.Add(lbResults, 0, 1);
        body.Controls.Add(right, 1, 0);
        t.Controls.Add(body, 0, 1);

        var bottom = Grid(1, 2);
        Rows(bottom, Abs(50), Abs(26));
        var bf = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Bg, Padding = Ui.Pad(0, 12, 0, 0), WrapContents = false, Margin = Padding.Empty };
        btnAuto = Btn("Запустить автоподбор", Accent, 220, 34);
        btnAutoCancel = Btn("Остановить", Danger, 130, 34);
        btnApplyBest = Btn("Выбрать выделенную стратегию", Line, 290, 34);
        btnAuto.Margin = btnAutoCancel.Margin = Ui.Pad(0, 0, 10, 0);
        btnAutoCancel.Enabled = false;
        btnAuto.Click += async (s, e) => await RunAutoAsync();
        btnAutoCancel.Click += (s, e) => autoCts?.Cancel();
        btnApplyBest.Click += (s, e) => ApplySelectedResult();
        bf.Controls.AddRange(new Control[] { btnAuto, btnAutoCancel, btnApplyBest });
        bottom.Controls.Add(bf, 0, 0);

        var pf = Grid(2, 1);
        Cols(pf, Abs(420), Fill());
        prog = new ThinProgress { Dock = DockStyle.Fill, Margin = Ui.Pad(0, 9, 12, 9), BackColor = Surface, ForeColor = AccentLight, Visible = false };
        lblAuto = Caption("", Fonts.Body, Muted, 26);
        lblAuto.Dock = DockStyle.Fill;
        pf.Controls.Add(prog, 0, 0);
        pf.Controls.Add(lblAuto, 1, 0);
        bottom.Controls.Add(pf, 0, 1);
        t.Controls.Add(bottom, 0, 2);

        p.Controls.Add(t);
        return p;
    }

    List<string> ParseDomains(string text)
    {
        var res = new List<string>();
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            string d = raw.Trim().ToLowerInvariant();
            if (d.Length == 0 || d.StartsWith("#")) continue;
            d = Regex.Replace(d, @"^https?://", "");
            int slash = d.IndexOf('/');
            if (slash >= 0) d = d.Substring(0, slash);
            if (d.Length > 0 && !res.Contains(d)) res.Add(d);
        }
        return res;
    }

    HashSet<string> LoadDomainSet(params string[] files)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            string path = Path.Combine(cfg.ZapretPath, "lists", f);
            if (!File.Exists(path)) continue;
            foreach (var line in File.ReadLines(path))
            {
                string t = line.Trim();
                if (t.Length > 0 && !t.StartsWith("#")) set.Add(t.ToLowerInvariant());
            }
        }
        return set;
    }

    /// <summary>Домен покрыт списком, если в нём есть он сам или любой его родительский домен.</summary>
    static bool Covered(string domain, HashSet<string> set)
    {
        var parts = domain.Split('.');
        for (int i = 0; i < parts.Length - 1; i++)
            if (set.Contains(string.Join(".", parts.Skip(i)))) return true;
        return false;
    }

    /// <summary>Дописывает домены в list-general-user.txt. true — если всё в порядке, в том числе когда добавлять нечего.</summary>
    bool AddDomainsToUserList(List<string> domains, bool silent)
    {
        if (!Strategy.IsValidZapretDir(cfg.ZapretPath)) { ShowPage(3); return false; }
        if (domains.Count == 0)
        {
            if (!silent) MessageBox.Show("Сначала впишите свои домены в поле «Свои домены для проверки».", AppSettings.AppName);
            return false;
        }
        try
        {
            Strategy.EnsureUserLists(cfg.ZapretPath);
            string path = Path.Combine(cfg.ZapretPath, "lists", "list-general-user.txt");
            var ed = editors.FirstOrDefault(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase));
            if (ed != null && ed.Dirty)
            {
                MessageBox.Show("В списке «list-general-user.txt» есть несохранённые правки. Сначала сохраните или отмените их на вкладке «Списки».", AppSettings.AppName);
                return false;
            }
            var existing = new HashSet<string>(File.ReadAllLines(path).Select(l => l.Trim().ToLowerInvariant()));
            var add = domains.Where(d => !existing.Contains(d)).ToList();
            if (add.Count == 0)
            {
                if (!silent) MessageBox.Show("Все эти домены уже есть в вашем списке.", AppSettings.AppName);
                return true;
            }
            string cur = File.ReadAllText(path);
            string nl = cur.EndsWith("\n") || cur.Length == 0 ? "" : "\r\n";
            File.WriteAllText(path, cur + nl + string.Join("\r\n", add) + "\r\n", new UTF8Encoding(false));
            AddLog("В list-general-user.txt добавлено: " + string.Join(", ", add));
            if (ed != null) LoadEditor(ed);
            if (!silent) MessageBox.Show("Добавлено в «Свой список: домены для обхода»: " + add.Count + ".\nЧтобы применилось, перезапустите обход.", AppSettings.AppName);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Не удалось: " + ex.Message, AppSettings.AppName);
            return false;
        }
    }

    void SetAutoBusy(bool busy)
    {
        btnAuto.Enabled = !busy;
        btnAutoCancel.Enabled = busy;
        btnApplyBest.Enabled = !busy;
        btnPower.Enabled = !busy;
        btnUpdate.Enabled = !busy;
        cbStrategy.Enabled = !busy;
        txtCustom.ReadOnly = busy;
        chkStopFirst.Enabled = !busy;
        foreach (var c in chkServices) c.Enabled = !busy;
        btnAddToList.Enabled = !busy;
        prog.Visible = busy;
    }

    List<Target> BuildTargets(List<string> custom)
    {
        var list = new List<Target>();
        for (int i = 0; i < chkServices.Length; i++)
        {
            if (!chkServices[i].Checked) continue;
            var svc = TestTargets.Services[i];
            foreach (var d in svc.Domains) list.Add(new Target { Domain = d, Group = svc.Short });
        }
        foreach (var d in custom)
            if (!list.Any(x => x.Domain == d)) list.Add(new Target { Domain = d, Group = null });
        return list;
    }

    TestResult MakeResult(string name, Strategy st, int order, List<(Target Target, bool Ok)> checks)
    {
        var res = new TestResult { Name = name, Strategy = st, Order = order, Total = checks.Count, Ok = checks.Count(x => x.Ok) };
        foreach (var item in checks)
        {
            if (item.Ok) continue;
            if (item.Target.Group == null) res.FailedCustom.Add(item.Target.Domain);
            else if (!res.FailedGroups.Contains(item.Target.Group)) res.FailedGroups.Add(item.Target.Group);
        }
        return res;
    }

    async Task RunAutoAsync()
    {
        if (!Strategy.IsValidZapretDir(cfg.ZapretPath)) { ShowPage(3); MessageBox.Show("Сначала укажите папку zapret.", AppSettings.AppName); return; }
        if (strategies.Count == 0) { MessageBox.Show("В папке нет стратегий (general*.bat).", AppSettings.AppName); return; }

        var custom = ParseDomains(txtCustom.Text);
        var targets = BuildTargets(custom);
        if (targets.Count == 0) { MessageBox.Show("Отметьте хотя бы один сервис или впишите свои домены.", AppSettings.AppName); return; }

        cfg.TestServices = TestTargets.Services.Where((svc, idx) => chkServices[idx].Checked).Select(svc => svc.Name).ToList();
        cfg.CustomDomains = custom;
        cfg.StopOnFirstSuccess = chkStopFirst.Checked;
        cfg.Save();

        if (custom.Count > 0)
        {
            var known = LoadDomainSet("list-general.txt", "list-general-user.txt", "list-google.txt");
            var missing = custom.Where(d => !Covered(d, known)).ToList();
            if (missing.Count > 0)
            {
                var ask = MessageBox.Show(
                    "Этих доменов нет в списках обхода:\n" + string.Join(", ", missing) +
                    "\n\nБез этого zapret их не обрабатывает, и проверка покажет, что они не открываются ни на одной стратегии.\n\n" +
                    "Добавить их в «Свой список: домены для обхода»?",
                    "Автоподбор", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (ask == DialogResult.Cancel) return;
                if (ask == DialogResult.Yes && !AddDomainsToUserList(missing, true)) return;
            }
        }

        if (!ResolveConflicts(false)) return;
        var route = await Task.Run(() => SystemTools.InspectDefaultRoute());
        if (route != null && route.IsVpn)
        {
            var vr = MessageBox.Show("Похоже, интернет идёт через VPN-адаптер «" + route.Name + "».\nТест через VPN покажет не то, что вам нужно. Продолжить всё равно?",
                "Автоподбор", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (vr != DialogResult.Yes) return;
        }

        if (runner.IsRunning) StopBypass();
        autoCts = new CancellationTokenSource();
        var ct = autoCts.Token;
        results = new List<TestResult>();
        lbResults.SetItems(new string[0]);
        SetAutoBusy(true);
        prog.Maximum = strategies.Count + 1;
        prog.Value = 0;
        Cursor = Cursors.AppStarting;

        try
        {
            EnsureTimestamps();
            cfg.DriverMayLinger = true;
            cfg.Save();

            lblAuto.Text = "Проверка без обхода…";
            var baseline = await Tester.Check(targets, ct);
            var baseRes = MakeResult("(без обхода)", null, -1, baseline);
            results.Add(baseRes);
            prog.Value = 1;
            RenderResults(false);
            if (baseRes.Ok == baseRes.Total) AddLog("Автоподбор: без обхода всё из списка и так открывается — проверьте, не включён ли VPN.");

            int n = 0;
            foreach (var st in strategies)
            {
                ct.ThrowIfCancellationRequested();
                n++;
                lblAuto.Text = "(" + n + "/" + strategies.Count + ") " + st.Name + "…";
                TestResult res;
                if (!runner.Start(cfg.ZapretPath, st, GameMode(), out string err))
                {
                    AddLog("Автоподбор: «" + st.Name + "» не запустилась: " + err);
                    res = new TestResult { Name = st.Name, Strategy = st, Ok = 0, Total = targets.Count, Note = "не запустилась", Order = n };
                }
                else
                {
                    await Task.Delay(800, ct);
                    var checks = await Tester.Check(targets, ct);
                    res = MakeResult(st.Name, st, n, checks);
                    runner.Stop(false);
                }
                results.Add(res);
                prog.Value = n + 1;
                RenderResults(false);

                if (cfg.StopOnFirstSuccess && res.Strategy != null && res.Ok == res.Total && res.Total > 0)
                {
                    lblAuto.Text = "Готово: найдена стратегия, при которой открылось всё — " + st.Name + ".";
                    prog.Value = prog.Maximum;
                    return;
                }
            }
            lblAuto.Text = "Готово.";
        }
        catch (OperationCanceledException)
        {
            lblAuto.Text = "Остановлено.";
        }
        catch (Exception ex)
        {
            lblAuto.Text = "Ошибка: " + ex.Message;
            AddLog("Автоподбор: " + ex);
        }
        finally
        {
            runner.Stop(true);
            cfg.DriverMayLinger = false;
            RestoreTimestamps();
            cfg.Save();
            Cursor = Cursors.Default;
            SetAutoBusy(false);
            RenderResults(true);
            UpdateUi();
        }
    }

    static string RowText(TestResult r)
    {
        string mark = r.Strategy == null ? "·" : (r.Ok == r.Total ? "✓" : (r.Ok == 0 ? "✗" : "~"));
        var bad = new List<string>();
        bad.AddRange(r.FailedGroups);
        bad.AddRange(r.FailedCustom);
        string tail = !string.IsNullOrEmpty(r.Note) ? "   — " + r.Note : (bad.Count > 0 ? "   ✗ " + string.Join(", ", bad) : "");
        return mark + " " + r.Ok + "/" + r.Total + "  " + r.Name + tail;
    }

    void RenderResults(bool sorted)
    {
        var list = sorted
            ? results.OrderByDescending(x => x.Ok).ThenBy(x => x.Order).ToList()
            : results.OrderBy(x => x.Order).ToList();
        resultView = list;
        lbResults.SetItems(list.Select(RowText));
        if (sorted && list.Count > 0)
        {
            int best = list.FindIndex(x => x.Strategy != null);
            if (best >= 0) lbResults.SelectedIndex = best;
        }
    }

    void ApplySelectedResult()
    {
        int i = lbResults.SelectedIndex;
        if (i < 0 || i >= resultView.Count || resultView[i].Strategy == null) return;
        string name = resultView[i].Name;
        int idx = strategies.FindIndex(x => x.Name == name);
        if (idx < 0) return;
        cbStrategy.SelectedIndex = idx;
        lblAuto.Text = "Выбрана стратегия: " + name + ". Нажмите «Включить».";
    }
}
