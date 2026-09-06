using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using MagiApp.ViewModels;
using MagiEngine.V6;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace MagiApp.WinUI.Views;

/// <summary>
/// [フェーズ9] 「分析」タブ。読取専用の診断ビュー（クラスKDocは AnalysisView.xaml 参照）。
///
/// 描画は <see cref="Render"/> が <see cref="MagiViewModel.Ui"/> を読んで x:Name 付きの
/// <see cref="StackPanel"/> を作り直す方式（<c>ScheduleView.RenderSchedule</c> と同じ——
/// 内訳の行数も設定ミスの件数も可変で、XAML の静的テンプレートでは表現しづらい）。
/// このビューは <see cref="MagiViewModel"/> の状態を一切変更しない。
/// </summary>
public sealed partial class AnalysisView : UserControl
{
    /// <summary>診断ログの表示上限（行）。これを超えた分は先頭からの打ち切りと明示する。</summary>
    private const int MaxLogLines = 200;

    /// <summary>設定の見直しの表示上限（件）。重要な順に整列済みなので、超えた分は「まず上から直す」と案内する（Kotlin原本と同じ 6）。</summary>
    private const int MaxIssueRows = 6;
    private bool _issuesOpen;

    /// <summary>
    /// 違反の族キー → 画面に出す日本語名。Kotlin原本 <c>ui/BreakdownLabels.kt</c> の
    /// <c>breakdownLabels</c> の逐語移植（19族＝<c>MirrorKeys.All</c> と過不足なく一致する）。
    ///
    /// 引けなかったキーは生の英字キーがそのまま画面に出る＝<c>docs/operator_ux.md</c> の
    /// 「英字符号を画面に出さない」に正面から反するため、族を増やしたらここへ必ず足す
    /// （例外も警告も出ないので、書き忘れは実機で誰かが気づくまで分からない）。
    ///
    /// [2026-09-02] <c>internal</c> 化＝<see cref="SettingsView"/> の族別色設定UI（族キー→ラベル表示）が
    /// 同じ対応表を必要とするため、複製せずここへ委譲する（複製は必ずドリフトする＝確立済みの規約）。
    /// </summary>
    internal static readonly Dictionary<string, string> BreakdownLabels = new()
    {
        ["groupViol"] = "担当外シフト", ["pref"] = "希望違反", ["covU"] = "人員不足", ["c3n"] = "禁止の並び",
        ["low"] = "下限割れ", ["high"] = "上限超過", ["apt"] = "適切回数のズレ", ["fair"] = "公平化のズレ",
        ["weekly"] = "曜日の偏り",
        ["c1"] = "期間の制約", ["c2"] = "個人の合計", ["c3"] = "必須の並び", ["c3m"] = "推奨の並び",
        ["c3mn"] = "回避の並び", ["c41"] = "群のレンジ", ["c42"] = "群ペア",
        ["c41s"] = "スキル群のレンジ", ["c42s"] = "スキル群ペア", ["covO"] = "人員過剰",
    };

    /// <summary>違反の場所の表示上限（件）。930件(30名×31日)まで起こり得るため
    /// <see cref="MaxIssueRows"/> と同じ理由で打ち切る。</summary>
    private const int MaxLocationRows = 40;

    private readonly MagiViewModel _vm;

    private readonly UiSubscription _uiSub;

    /// <summary>[違反箇所へのジャンプ] <c>MainWindow.JumpToCell</c> — 勤務表タブへ切替＋
    /// 該当セルへスクロール＋一時ハイライト（<c>ScheduleView.FocusCell</c> 参照）。</summary>
    private readonly Action<int, int> _jumpToCell;

    public AnalysisView(MagiViewModel vm, Action<int, int> jumpToCell, Action? goEdit = null)
    {
        _vm = vm;
        _jumpToCell = jumpToCell;
        _goEdit = goEdit;
        InitializeComponent();
        // [レビュー指摘 2026-09-04] タブはキャッシュされ再利用されるので、Unloaded で外した購読を Loaded で戻す
        //   （旧: コンストラクタで一度だけ購読＝一度離れたタブは以後の状態変化を受け取らず、表示もボタンの活性も
        //   古いままだった）。再表示時は見えていなかった間の変化をまとめて描く（UiSubscription の KDoc 参照）。
        _uiSub = new UiSubscription(_vm.Ui, OnUiChanged);
        _uiSub.Attach();
        Loaded += (_, _) => { if (_uiSub.Attach()) Render(); };
        Unloaded += (_, _) => _uiSub.Detach();
        Render();
    }

    private void OnUiChanged(object? sender, PropertyChangedEventArgs e) => Render();

    private void Render()
    {
        var ui = _vm.Ui;
        EmptyText.Visibility = ui.Loaded ? Visibility.Collapsed : Visibility.Visible;
        if (!ui.Loaded)
        {
            TriageSection.Visibility = Visibility.Collapsed;
            SummarySection.Visibility = Visibility.Collapsed;
            BreakdownSection.Visibility = Visibility.Collapsed;
            LocationsSection.Visibility = Visibility.Collapsed;
            FixSection.Visibility = Visibility.Collapsed;
            IssuesSection.Visibility = Visibility.Collapsed;
            C1PlateauSection.Visibility = Visibility.Collapsed;
            PinSection.Visibility = Visibility.Collapsed;
            ForbiddenSection.Visibility = Visibility.Collapsed;
            LogSection.Visibility = Visibility.Collapsed;
            return;
        }

        RenderTriage(ui);
        RenderSummary(ui);
        RenderBreakdown(ui);
        RenderLocations(ui);
        RenderFix(ui);
        RenderIssues(ui);
        RenderC1Plateau(ui);
        RenderPinTargets(ui);
        RenderForbiddenDiag(ui);
        RenderLogs(ui);
    }

    private void OnFixSearchClick(object sender, RoutedEventArgs e) => _vm.FindFixSuggestions();

    private readonly Action? _goEdit;
    private bool _triageSummaryOpen;

    private static Brush BrushOf(string key) => (Brush)Application.Current.Resources[key];

    /// <summary>
    /// [phase9 #18] 「要確認」（Kotlin原本 <c>AnalysisTriageCard</c>、3.471.0）。分類は <see cref="AnalysisTriage.Build"/>（純関数）。
    /// 上段=直さないと消えない項目（必須違反＋診断が壁と判定した族＋設定の破綻）、中段=エンジンが挑戦する項目、
    /// 下段=0件の族を畳んだサマリー。必須違反の場所は下の「違反の場所」節が担う（Kotlin の ConfirmRow 相当）。
    /// </summary>
    private void RenderTriage(UiState ui)
    {
        TriageSection.Visibility = Visibility.Visible;
        var t = AnalysisTriage.Build(ui, LabelOf);

        TriageBadge.Background = BrushOf(t.Computed ? "MagiTertiaryContainerBrush" : "MagiSurfaceVariantBrush");
        TriageBadgeText.Foreground = t.Computed ? BrushOf("MagiOnTertiaryContainerBrush") : new SolidColorBrush(Microsoft.UI.Colors.DimGray);
        TriageBadgeText.Text = t.Computed ? "計算済み" : "未計算";
        TriageAllClearText.Visibility = !t.HasAnything && ui.Loaded && !ui.Running ? Visibility.Visible : Visibility.Collapsed;

        var upper = t.Blockers.Count > 0 || t.Issues.Count > 0;
        TriageBlockersTitle.Visibility = upper ? Visibility.Visible : Visibility.Collapsed;
        TriageBlockersList.Visibility = upper ? Visibility.Visible : Visibility.Collapsed;
        TriageBlockersList.Children.Clear();
        foreach (var row in t.Blockers)
        {
            var box = new StackPanel { Spacing = 2 };
            box.Children.Add(new TextBlock
            {
                Text = $"{row.Label} {row.Count}{row.Unit}" + (row.Promoted ? "（構造的に残ると判定）" : ""),
                FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = BrushOf("MagiOnErrorContainerBrush"), TextWrapping = TextWrapping.Wrap,
            });
            if (row.Detail.Length > 0)
                box.Children.Add(new TextBlock { Text = row.Detail, FontSize = 12, Foreground = BrushOf("MagiOnErrorContainerBrush"), TextWrapping = TextWrapping.Wrap });
            TriageBlockersList.Children.Add(new Border
            {
                Background = BrushOf("MagiErrorContainerBrush"), CornerRadius = new CornerRadius(8), Padding = new Thickness(10), Child = box,
            });
        }
        foreach (var row in t.Issues)
        {
            var grid = new Grid { ColumnSpacing = 8 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var box = new StackPanel { Spacing = 2 };
            box.Children.Add(new TextBlock
            {
                Text = $"{row.Label} {row.Count}件", FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = BrushOf("MagiOnWarnContainerBrush"), TextWrapping = TextWrapping.Wrap,
            });
            if (row.Detail.Length > 0)
                box.Children.Add(new TextBlock { Text = row.Detail, FontSize = 12, Foreground = BrushOf("MagiOnWarnContainerBrush"), TextWrapping = TextWrapping.Wrap, MaxLines = 2 });
            Grid.SetColumn(box, 0); grid.Children.Add(box);
            if (_goEdit is not null)
            {
                var go = new Button { Content = "設定へ", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
                go.Click += (_, _) => _goEdit();
                Grid.SetColumn(go, 1); grid.Children.Add(go);
            }
            TriageBlockersList.Children.Add(new Border
            {
                Background = BrushOf("MagiWarnContainerBrush"), CornerRadius = new CornerRadius(8), Padding = new Thickness(10), Child = grid,
            });
        }

        var mid = t.Searching.Count > 0;
        TriageSearchTitle.Visibility = mid ? Visibility.Visible : Visibility.Collapsed;
        TriageSearchPanel.Visibility = mid ? Visibility.Visible : Visibility.Collapsed;
        TriageSearchTitle.Text = t.Computed ? "計算後に残っている項目" : "エンジンが挑戦する項目（未計算）";
        TriageSearchList.Children.Clear();
        foreach (var row in t.Searching)
        {
            var line = new Grid { ColumnSpacing = 8 };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = new TextBlock { Text = "・" + row.Label, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
            var count = new TextBlock { Text = $"{row.Count}{row.Unit}", FontSize = 13, Opacity = 0.8 };
            Grid.SetColumn(label, 0); Grid.SetColumn(count, 1);
            line.Children.Add(label); line.Children.Add(count);
            TriageSearchList.Children.Add(line);
        }
        if (mid) TriageSearchList.Children.Add(new TextBlock { Text = "※" + t.SearchNote, FontSize = 12, Opacity = 0.8, TextWrapping = TextWrapping.Wrap });

        // 0件の族は畳む（全19族を並べると画面の半分を占める）。
        var all = t.OkFamilies.Count + t.BusyFamilies.Count;
        TriageSummaryToggle.Content = $"制約充足サマリー（正常 {t.OkFamilies.Count} / 残り {t.BusyFamilies.Count}）　" +
            (_triageSummaryOpen ? "閉じる ∧" : $"全{all}項目を展開 ∨");
        TriageSummaryList.Visibility = _triageSummaryOpen ? Visibility.Visible : Visibility.Collapsed;
        TriageSummaryList.Children.Clear();
        if (_triageSummaryOpen)
        {
            if (t.OkFamilies.Count > 0)
                TriageSummaryList.Children.Add(BodyText($"✔ 正常（{t.OkFamilies.Count}項目）: " + string.Join(" / ", t.OkFamilies), dim: true));
            if (t.BusyFamilies.Count > 0)
                TriageSummaryList.Children.Add(BodyText($"⚠ 残っている（{t.BusyFamilies.Count}項目）: " + string.Join(" / ", t.BusyFamilies), dim: true));
        }
        TriageRunningNote.Visibility = ui.Running ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnTriageSummaryToggleClick(object sender, RoutedEventArgs e)
    {
        _triageSummaryOpen = !_triageSummaryOpen;
        Render();
    }

    /// <summary>
    /// 「直し方を探す」。<see cref="UiState.FixSuggestions"/> は空リストが既定値（未検索/0件を区別しない
    /// ——Kotlin原本も同じ）ため、ボタン/検索中インジケータは常に出し、一覧だけを件数で切り替える。
    /// </summary>
    private void RenderFix(UiState ui)
    {
        FixSection.Visibility = Visibility.Visible;
        FixSearchButton.IsEnabled = !ui.FixSearching;
        FixSearchingText.Visibility = ui.FixSearching ? Visibility.Visible : Visibility.Collapsed;

        FixList.Children.Clear();
        foreach (var s in ui.FixSuggestions)
        {
            var row = new StackPanel { Spacing = 2 };
            row.Children.Add(BodyText(s.Label, semiBold: true));
            row.Children.Add(BodyText($"必須 {DeltaText(s.DeltaHard)} ・ 合計 {DeltaText(s.DeltaTotal)}", dim: true));
            var apply = new Button { Content = "適用", HorizontalAlignment = HorizontalAlignment.Left };
            apply.Click += (_, _) => _vm.ApplyFixSuggestion(s);
            row.Children.Add(apply);
            FixList.Children.Add(row);
        }
        if (ui.FixSuggestions.Count == 0 && !ui.FixSearching)
        {
            FixList.Children.Add(BodyText("「直し方を探す」を押すと候補が出ます（見つからないこともあります）。", dim: true));
        }
    }

    private static string DeltaText(int delta) => delta <= 0 ? delta.ToString() : $"+{delta}";

    /// <summary>① サマリー。読込済みなら常に出す（0件でも「0件」に意味がある）。</summary>
    private void RenderSummary(UiState ui)
    {
        SummarySection.Visibility = Visibility.Visible;
        SummaryText.Text =
            $"必須違反 {ui.BestHard}件 ・ 違反の合計 {ui.TotalViolations}件 ・ " +
            $"重みつきスコア {ui.WeightedScore:0.#} ・ 満足度 {ui.Satisfaction}";

        // 補足（あるときだけ）: 操作の助言・研磨の限界・そもそも叶えられない希望の件数。
        var hints = new List<string>();
        if (!string.IsNullOrWhiteSpace(ui.CopilotHint)) hints.Add(ui.CopilotHint!);
        if (ui.PolishExhausted) hints.Add("仕上げ最適化はこれ以上の改善を見つけられませんでした。設定を1つ緩めると変わる可能性があります。");
        if (ui.ImpossibleWishCount > 0) hints.Add($"どう組んでも叶えられない希望が {ui.ImpossibleWishCount}件あります。");
        HintText.Text = string.Join("\n", hints);
        HintText.Visibility = hints.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>② 違反の内訳。0件の族は出さない（19族すべてを並べると読めなくなる）。</summary>
    private void RenderBreakdown(UiState ui)
    {
        BreakdownList.Children.Clear();
        // 多い順。同数のときはキー順で固定する（表示が実行ごとに入れ替わらないように）。
        var rows = ui.Breakdown
            .Where(kv => kv.Value > 0)
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .ToList();
        BreakdownSection.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (rows.Count == 0) return;

        foreach (var kv in rows)
        {
            BreakdownList.Children.Add(BodyText($"{LabelOf(kv.Key)} {kv.Value}件"));
        }
    }

    /// <summary>
    /// [違反箇所へのジャンプ] <see cref="UiState.ViolationCells"/>（"i,j"→"vio-族"、セル単位の
    /// 違反のみ載る＝クラスKDoc参照）を1行1セルで列挙し、タップで <see cref="_jumpToCell"/> を呼ぶ。
    /// 職員×日の自然な読み順（i, j 昇順）で安定させる。930件(30名×31日)まで起こり得るため
    /// <see cref="MaxLocationRows"/> で打ち切る。
    /// </summary>
    private void RenderLocations(UiState ui)
    {
        LocationsList.Children.Clear();
        var locations = ui.ViolationCells
            .Select(kv => ParseLocation(kv.Key, kv.Value))
            .Where(l => l is not null)
            .Select(l => l!.Value)
            .OrderBy(l => l.I).ThenBy(l => l.J)
            .ToList();
        LocationsSection.Visibility = locations.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (locations.Count == 0) return;

        foreach (var loc in locations.Take(MaxLocationRows))
        {
            var name = loc.I < ui.StaffNames.Count ? ui.StaffNames[loc.I] : $"#{loc.I}";
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(BodyText($"{name}　{loc.J + 1}日　{LabelOf(loc.Family)}"));
            var jump = new Button { Content = "勤務表へ", FontSize = 12 };
            var i = loc.I; var j = loc.J;
            jump.Click += (_, _) => _jumpToCell(i, j);
            row.Children.Add(jump);
            LocationsList.Children.Add(row);
        }
        if (locations.Count > MaxLocationRows)
        {
            LocationsList.Children.Add(BodyText($"ほか {locations.Count - MaxLocationRows}件", dim: true));
        }
    }

    private static (int I, int J, string Family)? ParseLocation(string key, string vioClass)
    {
        var parts = key.Split(',');
        if (parts.Length != 2) return null;
        if (!int.TryParse(parts[0], out var i) || !int.TryParse(parts[1], out var j)) return null;
        var family = vioClass.StartsWith("vio-", StringComparison.Ordinal) ? vioClass["vio-".Length..] : vioClass;
        return (i, j, family);
    }

    /// <summary>
    /// ③ 設定の見直し（Kotlin原本 <c>SettingIssuesCard</c>、3.480.0）。1件＝種類チップ＋どこが / 何が問題か / どう直すか＋ワンタップ修正。
    /// 「担当外の希望」は同型行がまとまりやすいので一括クリアを先頭に置き、一覧は既定で畳む。
    /// </summary>
    private void RenderIssues(UiState ui)
    {
        IssuesList.Children.Clear();
        var issues = ui.SettingIssues;
        IssuesSection.Visibility = issues.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (issues.Count == 0) return;

        IssuesTitle.Text = $"設定の見直し（{issues.Count}件）";
        var wishClear = issues.Count(i => i.Kind == IssueKind.Wish && i.Action == SettingFixAction.RemoveWish);
        ClearOutOfScopeWishesButton.Visibility = wishClear > 1 ? Visibility.Visible : Visibility.Collapsed;
        ClearOutOfScopeWishesButton.Content = $"担当外の希望を一括クリア（{wishClear}件）";
        ClearOutOfScopeWishesButton.IsEnabled = !ui.Running;
        IssuesToggle.Content = _issuesOpen ? "ⓘ 一覧を閉じる" : $"ⓘ 一覧を見る（{issues.Count}件）";
        IssuesList.Visibility = _issuesOpen ? Visibility.Visible : Visibility.Collapsed;
        IssuesGoEditButton.Visibility = _goEdit is null ? Visibility.Collapsed : Visibility.Visible;
        IssuesGoEditButton.IsEnabled = !ui.Running;
        if (!_issuesOpen) return;

        foreach (var issue in issues.Take(MaxIssueRows))
        {
            var (tag, hex) = issue.Kind switch
            {
                IssueKind.Wish => ("希望", MagiAccent.Blue),
                IssueKind.Constraint => ("制約", MagiAccent.Red),
                IssueKind.Demand => ("必要人数", MagiAccent.Red),
                _ => ("回数", MagiAccent.Orange),
            };
            var fg = BrushOf("MagiOnErrorContainerBrush");
            var box = new StackPanel { Spacing = 4 };
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            head.Children.Add(TagChip(tag, hex));
            head.Children.Add(new TextBlock
            {
                Text = issue.Where, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = fg,
                TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
            });
            box.Children.Add(head);
            box.Children.Add(new TextBlock { Text = issue.Problem, FontSize = 12, Foreground = fg, TextWrapping = TextWrapping.Wrap });
            box.Children.Add(new TextBlock { Text = $"→ {issue.Fix}", FontSize = 13, Foreground = fg, TextWrapping = TextWrapping.Wrap });
            // [設定ミスのワンタップ修正] Action==None のものは提案文だけ（自動修正の当てが無い）。
            if (issue.Action != SettingFixAction.None)
            {
                var apply = new Button
                {
                    Content = issue.ActionLabel.Length > 0 ? issue.ActionLabel : "この修正を適用",
                    FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, IsEnabled = !ui.Running,
                };
                apply.Click += (_, _) => _vm.ApplySettingFix(issue);
                box.Children.Add(apply);
            }
            IssuesList.Children.Add(new Border
            {
                Background = BrushOf("MagiErrorContainerBrush"), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Child = box,
            });
        }
        if (issues.Count > MaxIssueRows)
        {
            IssuesList.Children.Add(BodyText($"ほか {issues.Count - MaxIssueRows} 件（重要な順に表示中。まず上から直してください）", dim: true));
        }
    }

    private void OnClearOutOfScopeWishesClick(object sender, RoutedEventArgs e) => _vm.ClearOutOfScopeWishes();

    private void OnIssuesToggleClick(object sender, RoutedEventArgs e)
    {
        _issuesOpen = !_issuesOpen;
        Render();
    }

    private void OnIssuesGoEditClick(object sender, RoutedEventArgs e) => _goEdit?.Invoke();

    /// <summary>MagiTagChip 相当（枠と文字を同じアクセント色にした小さなラベル）。</summary>
    private static Border TagChip(string text, string hex)
    {
        var color = new SolidColorBrush(ColorHex.Parse(hex, Colors.Gray));
        return new Border
        {
            BorderBrush = color, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 1, 6, 1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = text, FontSize = 11, Foreground = color },
        };
    }

    private bool _c1DetailOpen;
    private bool _pinDetailOpen;
    private const int MaxC1Rows = 6;
    private const int MaxPinRows = 5;

    /// <summary>
    /// [phase9 #20] 期間の制約(c1)がなぜ直せなかったか（Kotlin原本 <c>C1PlateauCard</c>）。根拠は「研磨が実際に候補を作って却下した」
    /// 観測なので「構造的に不能」とは言わない。観測が無い（CauseUnknown）ときは理由を語らず「原因未確定」とだけ言う。
    /// 内訳の数字とエンジン内部の注記は既定で畳む（3.480.0）。
    /// </summary>
    private void RenderC1Plateau(UiState ui)
    {
        var diag = ui.C1Plateau;
        var show = diag is not null && (diag.CauseUnknown || diag.HasEntries);
        C1PlateauSection.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        C1List.Children.Clear();
        if (!show) return;

        if (diag!.CauseUnknown)
        {
            C1Title.Text = "期間の制約が残っています（原因未確定）";
            C1UnknownText.Text = $"残り {diag.RemainingC1} 件。今回の整えでは、この残りについて直し方を試した記録が残っていません。" +
                "原因は特定できていません。もう一度つくると記録が取れる場合があります。";
            C1UnknownText.Visibility = Visibility.Visible;
            C1NoteText.Visibility = C1MoreText.Visibility = C1DetailToggle.Visibility = C1GoEditButton.Visibility = Visibility.Collapsed;
            return;
        }

        C1Title.Text = "期間の制約がなぜ直せなかったか";
        C1UnknownText.Visibility = Visibility.Collapsed;
        C1NoteText.Visibility = _c1DetailOpen ? Visibility.Visible : Visibility.Collapsed;
        foreach (var e in diag.Entries.Take(MaxC1Rows))
        {
            var pin = e.Cause == C1PlateauCause.PinConstrained;
            var fg = BrushOf(pin ? "MagiOnErrorContainerBrush" : "MagiOnSecondaryContainerBrush");
            var box = new StackPanel { Spacing = 4 };
            var head = new Grid { ColumnSpacing = 8 };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = new TextBlock { Text = e.Label, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = fg, TextWrapping = TextWrapping.Wrap };
            var chip = TagChip(e.Cause switch
            {
                C1PlateauCause.PinConstrained => "回数固定で却下",
                C1PlateauCause.ScoreTradeoff => "他とのトレードオフ",
                _ => "この直し方では候補なし",
            }, pin ? MagiAccent.Red : MagiAccent.Blue);
            Grid.SetColumn(label, 0); Grid.SetColumn(chip, 1);
            head.Children.Add(label); head.Children.Add(chip);
            box.Children.Add(head);
            if (_c1DetailOpen)
            {
                // 根拠の内訳。「試した手が何件あって、何で落ちたか」を数で示す（推測でなく観測）。
                var parts = new List<string>();
                if (e.RejectedByPin > 0) parts.Add($"回数固定で却下 {e.RejectedByPin}件");
                if (e.RejectedByScore > 0) parts.Add($"総合評価で却下 {e.RejectedByScore}件");
                if (e.NoCandidate > 0) parts.Add($"候補なし {e.NoCandidate}件");
                box.Children.Add(new TextBlock { Text = string.Join(" ・ ", parts), FontSize = 12, Foreground = fg, TextWrapping = TextWrapping.Wrap });
            }
            box.Children.Add(new TextBlock { Text = e.RecommendedAction(LabelOf), FontSize = 12, Foreground = fg, TextWrapping = TextWrapping.Wrap });
            C1List.Children.Add(new Border
            {
                Background = BrushOf(pin ? "MagiErrorContainerBrush" : "MagiSecondaryContainerBrush"),
                CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Child = box,
            });
        }
        C1MoreText.Visibility = diag.Entries.Count > MaxC1Rows ? Visibility.Visible : Visibility.Collapsed;
        C1MoreText.Text = $"ほか {diag.Entries.Count - MaxC1Rows} 件（詳細はログ出力を参照）";
        C1DetailToggle.Visibility = Visibility.Visible;
        C1DetailToggle.Content = _c1DetailOpen ? "ⓘ 詳しい説明を閉じる" : "ⓘ 詳しい説明（数え方と内訳）";
        C1GoEditButton.Visibility = diag.PinConstrained > 0 && _goEdit is not null ? Visibility.Visible : Visibility.Collapsed;
        C1GoEditButton.IsEnabled = !ui.Running;
    }

    /// <summary>
    /// ④ 回数の固定が計算に与えた影響（Kotlin原本 <c>PinFixedImpactCard</c>）。観測できた試行が1回以上あるときだけ出す
    /// （0 は「緩めても変わらない」の証明にはならない＝<see cref="PinTargetView"/> のKDoc参照）。
    /// 緩め幅は決め打ちしない（実測で ±1 と ±3 の優劣が逆転した）＝下限側・上限側を別々に1段だけ。
    /// </summary>
    private void RenderPinTargets(UiState ui)
    {
        PinList.Children.Clear();
        var attempts = ui.ObservedPinBlockedAttempts;
        PinSection.Visibility = attempts > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (attempts <= 0) return;

        PinSummaryText.Text = $"回数を固定していることだけが理由で見送られた試行が、少なくとも {attempts} 回ありました。これらは他の条件では採用できる手でした。";
        PinDetailToggle.Content = _pinDetailOpen ? "ⓘ 詳しい説明を閉じる" : "ⓘ 詳しい説明（数の読み方）";
        PinDetailPanel.Visibility = _pinDetailOpen ? Visibility.Visible : Visibility.Collapsed;

        var targets = ui.PinTargets;
        var hasTargets = targets.Count > 0;
        PinTargetsTitle.Visibility = PinHintText.Visibility = hasTargets ? Visibility.Visible : Visibility.Collapsed;
        foreach (var t in targets.Take(MaxPinRows))
        {
            var fg = BrushOf("MagiOnSecondaryContainerBrush");
            var box = new StackPanel { Spacing = 4 };
            box.Children.Add(new TextBlock
            {
                Text = $"{t.StaffName} {t.ShiftKigou}：{t.PinnedCount}回に固定（{t.Attempts}回の試行を止めました）",
                FontSize = 13, Foreground = fg, TextWrapping = TextWrapping.Wrap,
            });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var staff = t.Staff; var shift = t.Shift;
            // 0回に固定されている行では「下限を1下げる」が 0 でクランプされて無操作になるので、下げられるときだけ出す。
            if (t.PinnedCount > 0)
            {
                var lo = new Button { Content = $"下限を1下げる（{t.PinnedCount - 1}〜{t.PinnedCount}）", FontSize = 12, IsEnabled = !ui.Running };
                lo.Click += (_, _) => _vm.RelaxStaffRangePin(staff, shift, -1, 0);
                buttons.Children.Add(lo);
            }
            var hi = new Button { Content = $"上限を1上げる（{t.PinnedCount}〜{t.PinnedCount + 1}）", FontSize = 12, IsEnabled = !ui.Running };
            hi.Click += (_, _) => _vm.RelaxStaffRangePin(staff, shift, 0, 1);
            buttons.Children.Add(hi);
            box.Children.Add(buttons);
            PinList.Children.Add(new Border
            {
                Background = BrushOf("MagiSecondaryContainerBrush"), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Child = box,
            });
        }
        PinMoreText.Visibility = targets.Count > MaxPinRows ? Visibility.Visible : Visibility.Collapsed;
        PinMoreText.Text = $"ほか {targets.Count - MaxPinRows} 件（詳細はログ出力を参照）";
        PinGoEditButton.Visibility = _goEdit is null ? Visibility.Collapsed : Visibility.Visible;
        PinGoEditButton.IsEnabled = !ui.Running;
    }

    private void OnC1DetailToggleClick(object sender, RoutedEventArgs e) { _c1DetailOpen = !_c1DetailOpen; Render(); }
    private void OnPinDetailToggleClick(object sender, RoutedEventArgs e) { _pinDetailOpen = !_pinDetailOpen; Render(); }
    private void OnGoEditClick(object sender, RoutedEventArgs e) => _goEdit?.Invoke();

    /// <summary>
    /// [2026-09-02, 配線] ④' 禁止の並び(c3n)診断。<see cref="UiState.ForbiddenDiag"/>
    /// （<see cref="MagiViewModel.RelaxForbiddenRule"/> と対だが、これまでこの画面が
    /// ForbiddenDiag自体を一度も読んでおらず、診断結果もその緩和手段もどちらも見えなかった）。
    /// 「このデータ・希望のままでは崩せない」(!Escapable)と判定された run だけを列挙する
    /// （崩せる見込みがある run まで並べると、緩和ボタンの意味が薄まる）。
    /// </summary>
    private void RenderForbiddenDiag(UiState ui)
    {
        ForbiddenList.Children.Clear();
        var diag = ui.ForbiddenDiag;
        var blocked = diag?.Runs.Where(r => !r.Escapable).ToList() ?? new List<ForbiddenRunDiag>();
        ForbiddenSection.Visibility = blocked.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (blocked.Count == 0) return;

        foreach (var run in blocked)
        {
            var row = new StackPanel { Spacing = 2 };
            row.Children.Add(BodyText($"{run.StaffName} の {run.SeqLabel}（{run.StartDay + 1}日目〜）", semiBold: true));
            row.Children.Add(BodyText(run.Hint, dim: true));
            var relax = new Button { Content = "この禁止の並びを緩める（削除）", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Left };
            var seqLabel = run.SeqLabel;
            relax.Click += (_, _) => _vm.RelaxForbiddenRule(seqLabel);
            row.Children.Add(relax);
            ForbiddenList.Children.Add(row);
        }
    }

    /// <summary>⑤ 診断ログ。等幅・選択可（そのままコピーして共有できるように）。</summary>
    private void RenderLogs(UiState ui)
    {
        var logs = ui.Logs;
        LogSection.Visibility = logs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (logs.Count == 0)
        {
            LogText.Text = "";
            return;
        }

        var lines = logs.Take(MaxLogLines).ToList();
        if (logs.Count > MaxLogLines) lines.Add($"…（ほか {logs.Count - MaxLogLines}行）");
        LogText.Text = string.Join("\n", lines);
    }

    private static string LabelOf(string family) =>
        BreakdownLabels.TryGetValue(family, out var jp) ? jp : family;

    /// <summary>本文1行。節の中身はすべてこの形（13px・折り返しあり）で作る。</summary>
    /// <remarks>
    /// [トークン非適用] FontSize=13はMagiThemeタイポスケール(14始まり)より小さい一覧行用の
    /// 調整値で厳密一致しない。このファイルのButton.FontSize(12、行186/227/264/293)も、
    /// Style(TargetType=TextBlock)を型的に受け付けられないためトークン化の対象外。
    /// </remarks>
    private static TextBlock BodyText(string text, bool semiBold = false, bool dim = false) => new()
    {
        Text = text,
        FontSize = 13,
        TextWrapping = TextWrapping.Wrap,
        FontWeight = semiBold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
        Opacity = dim ? 0.8 : 1.0,
    };
}
