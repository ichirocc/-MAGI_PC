using System.ComponentModel;
using System.Linq;
using MagiApp.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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

    /// <summary>設定の見直し候補の表示上限（件）。これを超えた分は件数だけを添える。</summary>
    private const int MaxIssueRows = 12;

    /// <summary>
    /// 違反の族キー → 画面に出す日本語名。Kotlin原本 <c>ui/BreakdownLabels.kt</c> の
    /// <c>breakdownLabels</c> の逐語移植（19族＝<c>MirrorKeys.All</c> と過不足なく一致する）。
    ///
    /// 引けなかったキーは生の英字キーがそのまま画面に出る＝<c>docs/operator_ux.md</c> の
    /// 「英字符号を画面に出さない」に正面から反するため、族を増やしたらここへ必ず足す
    /// （例外も警告も出ないので、書き忘れは実機で誰かが気づくまで分からない）。
    /// </summary>
    private static readonly Dictionary<string, string> BreakdownLabels = new()
    {
        ["groupViol"] = "担当外シフト", ["pref"] = "希望違反", ["covU"] = "人員不足", ["c3n"] = "禁止の並び",
        ["low"] = "下限割れ", ["high"] = "上限超過", ["apt"] = "適切回数のズレ", ["fair"] = "公平化のズレ",
        ["weekly"] = "曜日の偏り",
        ["c1"] = "窓の要件", ["c2"] = "個人の合計", ["c3"] = "必須の並び", ["c3m"] = "推奨の並び",
        ["c3mn"] = "回避の並び", ["c41"] = "群のレンジ", ["c42"] = "群ペア",
        ["c41s"] = "スキル群のレンジ", ["c42s"] = "スキル群ペア", ["covO"] = "人員過剰",
    };

    private readonly MagiViewModel _vm;

    public AnalysisView(MagiViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        _vm.Ui.PropertyChanged += OnUiChanged;
        Unloaded += (_, _) => _vm.Ui.PropertyChanged -= OnUiChanged;
        Render();
    }

    private void OnUiChanged(object? sender, PropertyChangedEventArgs e) => Render();

    private void Render()
    {
        var ui = _vm.Ui;
        EmptyText.Visibility = ui.Loaded ? Visibility.Collapsed : Visibility.Visible;
        if (!ui.Loaded)
        {
            SummarySection.Visibility = Visibility.Collapsed;
            BreakdownSection.Visibility = Visibility.Collapsed;
            FixSection.Visibility = Visibility.Collapsed;
            IssuesSection.Visibility = Visibility.Collapsed;
            PinSection.Visibility = Visibility.Collapsed;
            LogSection.Visibility = Visibility.Collapsed;
            return;
        }

        RenderSummary(ui);
        RenderBreakdown(ui);
        RenderFix(ui);
        RenderIssues(ui);
        RenderPinTargets(ui);
        RenderLogs(ui);
    }

    private void OnFixSearchClick(object sender, RoutedEventArgs e) => _vm.FindFixSuggestions();

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

    /// <summary>③ 設定の見直し候補。1件＝どこが / 何が問題か / どう直すか の3行。</summary>
    private void RenderIssues(UiState ui)
    {
        IssuesList.Children.Clear();
        var issues = ui.SettingIssues;
        IssuesSection.Visibility = issues.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (issues.Count == 0) return;

        foreach (var issue in issues.Take(MaxIssueRows))
        {
            var row = new StackPanel { Spacing = 2 };
            row.Children.Add(BodyText(issue.Where, semiBold: true));
            row.Children.Add(BodyText(issue.Problem));
            row.Children.Add(BodyText($"→ {issue.Fix}", dim: true));
            IssuesList.Children.Add(row);
        }
        if (issues.Count > MaxIssueRows)
        {
            IssuesList.Children.Add(BodyText($"ほか {issues.Count - MaxIssueRows}件", dim: true));
        }
    }

    /// <summary>
    /// ④ 回数の固定で止まった手。観測できた試行が1回以上あるときだけ出す
    /// （0 は「緩めても変わらない」の証明にはならない＝<see cref="PinTargetView"/> のKDoc参照。
    /// 根拠の無い「緩めても無駄」を読ませないため、0 のときは節ごと隠す）。
    /// </summary>
    private void RenderPinTargets(UiState ui)
    {
        PinList.Children.Clear();
        PinSection.Visibility = ui.ObservedPinBlockedAttempts > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (ui.ObservedPinBlockedAttempts <= 0) return;

        PinSummaryText.Text =
            $"回数の固定だけが理由で見送った手が {ui.ObservedPinBlockedAttempts}回ありました（計測できた分）。" +
            "下の固定を緩めると、直せる違反が増える可能性があります。";
        foreach (var pin in ui.PinTargets)
        {
            PinList.Children.Add(BodyText(
                $"{pin.StaffName} の {pin.ShiftKigou}：{pin.PinnedCount}回に固定・{pin.Attempts}回ブロック"));
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
    private static TextBlock BodyText(string text, bool semiBold = false, bool dim = false) => new()
    {
        Text = text,
        FontSize = 13,
        TextWrapping = TextWrapping.Wrap,
        FontWeight = semiBold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
        Opacity = dim ? 0.8 : 1.0,
    };
}
