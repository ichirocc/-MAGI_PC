using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using MagiApp.ViewModels;
using MagiEngine.V6;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;

namespace MagiApp.WinUI.Views;

/// <summary>[フェーズ9] 「ホーム」タブ。クラスKDoc（HomeView.xaml参照）。</summary>
public sealed partial class HomeView : UserControl
{
    private readonly MagiViewModel _vm;
    private readonly MainWindow _window;
    private readonly UiSubscription _uiSub;

    private bool _detailOpen;
    private Action _bigAction = () => { };
    private Action _helperAction = () => { };

    /// <summary>「AIの解決提案」が自動で探索した盤面（同じ盤面で二度探さないための鍵）。</summary>
    private object? _autoFixBoard;
    private long _autoFixHard = -1;

    /// <summary>「途中経過を見る」の開閉と、前回描いた途中盤面（赤枠＝今回変化、の比較元）。</summary>
    private bool _liveOpen;
    private IReadOnlyList<IReadOnlyList<int>>? _livePrev;

    public HomeView(MagiViewModel vm, MainWindow window)
    {
        _vm = vm;
        _window = window;
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
        MessageText.Text = ui.Message ?? (ui.Loaded ? "" : "データを読み込んでいます…");
        SummaryText.Text = ui.Loaded
            ? $"満足度 {ui.Satisfaction} ・ 必須違反 {ui.BestHard} ・ 合計 {ui.BestSoft}"
            : "";
        var editable = ui.Loaded && !ui.Running;
        MakeButton.IsEnabled = editable;
        SoftPolishButton.IsEnabled = editable;
        BackgroundButton.IsEnabled = editable;
        StopButton.IsEnabled = ui.Running;

        RenderNextAction(ui, editable);
        RenderLive(ui);
        RenderSmartAction(ui, editable);
        RenderCoverage(ui, editable);
        RenderAlternatives(ui, editable);
    }

    /// <summary>
    /// [phase9 #2] 処方箋カード。Kotlin原本 <c>OperatorNextActionCard</c>（3.480.0/3.483.0）の状態分岐を
    /// そのまま写す。実行中は見出し・解消度を出さない（進捗行は #3）。飛び先の判断は docs/phase9/blockers.md #2。
    /// </summary>
    private void RenderNextAction(UiState ui, bool editable)
    {
        if (!ui.Loaded)
        {
            NextActionCard.Visibility = Visibility.Collapsed;
            return;
        }
        NextActionCard.Visibility = Visibility.Visible;
        var diag = ui.CoverageDiag;
        var infeasible = diag?.AllInfeasible == true;
        var shortfalls = diag?.Shortfalls ?? System.Array.Empty<CoverageShortfall>();
        var shortDays = shortfalls.Select(x => x.DayIndex).Distinct().Count();
        var worstDay = shortfalls.Count > 0 ? shortfalls[0].DayLabel : null;

        string bg, fg, headline, bigLabel, phase, phaseHex;
        string? helperLabel;
        bool bigEnabled;
        if (ui.Running)
        {
            bg = "MagiPrimaryContainerBrush"; fg = "MagiOnPrimaryContainerBrush";
            headline = ""; bigLabel = ""; bigEnabled = false; helperLabel = null;
            phase = ""; phaseHex = "";
            _bigAction = () => { }; _helperAction = () => { };
        }
        else if (!ui.HasResult)
        {
            bg = "MagiPrimaryContainerBrush"; fg = "MagiOnPrimaryContainerBrush";
            headline = "② ボタンひとつで、勤務表を作ります。";
            bigLabel = "勤務表をつくる"; bigEnabled = true;
            helperLabel = "下書きをつくる（希望と期間の制約を先に埋める）";
            phase = "探索"; phaseHex = MagiAccent.Blue;
            _bigAction = _vm.RunV6FullOptimize; _helperAction = _vm.GenerateSmartInitial;
        }
        else if (ui.BestHard == 0L)
        {
            bg = "MagiTertiaryContainerBrush"; fg = "MagiOnTertiaryContainerBrush";
            headline = "③ できました！ そのまま配れます。";
            bigLabel = "印刷・書き出し"; bigEnabled = true; helperLabel = "中身を見る";
            phase = "完成"; phaseHex = MagiAccent.Green;
            _bigAction = () => _ = _window.ExportScheduleCsvAsync(); _helperAction = () => _window.SelectTab("schedule");
        }
        else if (infeasible)
        {
            bg = "MagiErrorContainerBrush"; fg = "MagiOnErrorContainerBrush";
            headline = "このデータでは、ここは埋められません。" + (worstDay is null ? "" : $"（例：{worstDay}）");
            bigLabel = "データを見直す"; bigEnabled = true; helperLabel = "未充足のまま書き出す";
            phase = "狩猟"; phaseHex = MagiAccent.Orange;
            _bigAction = () => _window.SelectTab("edit"); _helperAction = () => _ = _window.ExportScheduleCsvAsync();
        }
        else
        {
            bg = "MagiWarnContainerBrush"; fg = "MagiOnWarnContainerBrush";
            headline = "もう少しです。" + (worstDay is null ? $"必須違反が {ui.BestHard}件 残っています。" : $"{worstDay} が人手不足です。");
            bigLabel = "なおすのを手伝って"; bigEnabled = shortfalls.Count > 0; helperLabel = null;
            phase = "狩猟"; phaseHex = MagiAccent.Orange;
            _bigAction = () => _window.SelectTab("schedule"); _helperAction = () => { };
        }

        NextActionCard.Background = BrushOf(bg);
        var fgBrush = BrushOf(fg);
        PhaseBadge.Visibility = phase.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (phase.Length > 0)
        {
            var phaseColor = ColorHex.Parse(phaseHex, Colors.Gray);
            PhaseBadge.Background = new SolidColorBrush(phaseColor);
            PhaseText.Text = phase;
            PhaseText.Foreground = new SolidColorBrush(ReadableOn(phaseColor));
        }
        HeadlineText.Visibility = headline.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        HeadlineText.Text = headline;
        HeadlineText.Foreground = fgBrush;

        var remaining = ui.BestHard > 0L ? $"必須 残り{ui.BestHard}件"
            : shortDays > 0 ? $"残り{shortDays}日"
            : ui.BestSoft > 0L ? $"必須は解消・調整 {ui.BestSoft}件"
            : "解消済み";
        var showResolve = !ui.Running;
        ResolveText.Visibility = showResolve ? Visibility.Visible : Visibility.Collapsed;
        ResolveBar.Visibility = showResolve ? Visibility.Visible : Visibility.Collapsed;
        ResolveText.Text = $"解消度：{ui.Satisfaction}%（{remaining}）";
        ResolveText.Foreground = fgBrush;
        ResolveBar.Value = System.Math.Clamp(ui.Satisfaction, 0, 100);
        ResolveBar.Foreground = fgBrush;

        var showDetail = !ui.Running && ui.HasResult;
        DetailToggle.Visibility = showDetail ? Visibility.Visible : Visibility.Collapsed;
        DetailToggle.Content = _detailOpen ? "ⓘ 詳しい説明を閉じる" : "ⓘ 解消度の意味";
        DetailToggle.Foreground = fgBrush;
        DetailText.Visibility = showDetail && _detailOpen ? Visibility.Visible : Visibility.Collapsed;
        DetailText.Foreground = fgBrush;

        ProgressRow.Visibility = ui.Running ? Visibility.Visible : Visibility.Collapsed;
        ProgressSpinner.IsActive = ui.Running;
        ProgressSpinner.Foreground = fgBrush;
        ProgressText.Text = ui.Running ? ProgressSummary(ui) : "";
        ProgressText.Foreground = fgBrush;

        BigButton.Visibility = bigEnabled ? Visibility.Visible : Visibility.Collapsed;
        BigButton.Content = bigLabel;
        BigButton.IsEnabled = editable;
        HelperButton.Visibility = helperLabel is null ? Visibility.Collapsed : Visibility.Visible;
        HelperButton.Content = helperLabel ?? "";
        HelperButton.IsEnabled = editable;
        RedraftLink.Visibility = showDetail ? Visibility.Visible : Visibility.Collapsed;
        RedraftLink.Foreground = fgBrush;
        RedraftLink.IsEnabled = editable;
    }

    /// <summary>Kotlin原本 <c>progressSummary</c>（3.393.0/3.396.0）。反復数は作り手の指標なので出さない。</summary>
    private static string ProgressSummary(UiState ui)
    {
        var parts = new List<string>(4);
        if (ui.BestHard > 0L)
        {
            parts.Add(ui.InitHard > ui.BestHard
                ? $"必須違反 残り{ui.BestHard}件（開始{ui.InitHard}件）"
                : $"必須違反 残り{ui.BestHard}件");
        }
        else if (ui.InitSoft > 0L)
        {
            var pct = System.Math.Max(0L, (ui.InitSoft - ui.BestSoft) * 100L / ui.InitSoft);
            parts.Add($"気になる点 {ui.BestSoft}件（開始{ui.InitSoft}件・{pct}%減）");
        }
        else parts.Add("気になる点 –");
        if (ui.BestHard > 0L && ui.TotalViolations > 0) parts.Add($"気になる点 全{ui.TotalViolations}件");
        var secLeft = System.Math.Max(0L, ui.BudgetSec * 1000L - ui.ElapsedMs) / 1000L;
        parts.Add($"残り {secLeft / 60}:{secLeft % 60:00}");
        return string.Join("  ・  ", parts);
    }

    /// <summary>
    /// [phase9 #3] 途中経過（Kotlin原本 <c>LiveScheduleCard</c>）。実行中に <see cref="UiState.LiveSchedule"/> が
    /// 届くたびに色タイルの盤面を描き、前回から変わったセルを赤枠で示す。開いているときだけ描く。
    /// </summary>
    private void RenderLive(UiState ui)
    {
        var cur = ui.LiveSchedule;
        if (!ui.Running || cur.Count == 0)
        {
            LiveCard.Visibility = Visibility.Collapsed;
            LiveGridHost.Children.Clear();
            _livePrev = null;
            return;
        }
        LiveCard.Visibility = Visibility.Visible;
        LiveToggle.Content = _liveOpen ? "途中経過を隠す" : "途中経過を見る";
        LivePanel.Visibility = _liveOpen ? Visibility.Visible : Visibility.Collapsed;
        if (!_liveOpen) return;
        if (ReferenceEquals(_livePrev, cur) && LiveGridHost.Children.Count > 0) return;

        var changed = new HashSet<(int, int)>();
        var prev = _livePrev;
        if (prev is not null && prev.Count == cur.Count)
        {
            for (var i = 0; i < cur.Count; i++)
            {
                if (prev[i].Count != cur[i].Count) continue;
                for (var j = 0; j < cur[i].Count; j++) if (prev[i][j] != cur[i][j]) changed.Add((i, j));
            }
        }
        _livePrev = cur;
        LiveCaption.Text = $"状態遷移  赤枠＝今回変化 ({changed.Count})";
        LiveGridHost.Children.Clear();
        var rest = BrushOf("MagiSurfaceVariantBrush");
        var err = BrushOf("MagiErrorBrush");
        for (var i = 0; i < cur.Count; i++)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
            for (var j = 0; j < cur[i].Count; j++)
            {
                var k = cur[i][j];
                var hex = k >= 0 && k < ui.ShiftColorHex.Count ? ui.ShiftColorHex[k] : null;
                var tile = new Border
                {
                    Width = 11, Height = 11, CornerRadius = new CornerRadius(2),
                    Background = k < 0 ? rest : new SolidColorBrush(ColorHex.Parse(hex, Colors.Transparent)),
                };
                if (changed.Contains((i, j)))
                {
                    tile.BorderBrush = err;
                    tile.BorderThickness = new Thickness(2);
                }
                row.Children.Add(tile);
            }
            LiveGridHost.Children.Add(row);
        }
    }

    private void OnLiveToggleClick(object sender, RoutedEventArgs e)
    {
        _liveOpen = !_liveOpen;
        _livePrev = null;
        Render();
    }

    /// <summary>
    /// [phase9 #2] 「AIの解決提案」。Kotlin原本 <c>SmartActionCard</c>（3.480.0）＝分析タブと同じ改善提案の
    /// 先頭候補を 1 ボタンで適用。必須違反が残る間だけ出し、盤面ごとに 1 回だけ自動で探す（盤面は変えない）。
    /// </summary>
    private void RenderSmartAction(UiState ui, bool editable)
    {
        if (ui.Running || !ui.HasResult || ui.BestHard <= 0L)
        {
            SmartActionCard.Visibility = Visibility.Collapsed;
            return;
        }
        var boardChanged = !ReferenceEquals(_autoFixBoard, ui.Schedule) || _autoFixHard != ui.BestHard;
        if (boardChanged && !ui.FixSearching && (ui.FixSuggestions.Count == 0 || ui.FixFocusName.Length > 0))
        {
            _autoFixBoard = ui.Schedule;
            _autoFixHard = ui.BestHard;
            _vm.FindFixSuggestions();
            return;
        }
        if (ui.FixFocusName.Length > 0 && !ui.FixSearching)
        {
            SmartActionCard.Visibility = Visibility.Collapsed;
            return;
        }
        SmartActionCard.Visibility = Visibility.Visible;
        var top = ui.FixSuggestions.Count > 0 ? ui.FixSuggestions[0] : null;
        if (ui.FixSearching)
        {
            SmartStatusText.Text = "いちばん効果のある直し方を探しています…";
            SmartStatusText.Visibility = Visibility.Visible;
            SmartTopPanel.Visibility = Visibility.Collapsed;
            return;
        }
        if (top is null)
        {
            SmartStatusText.Text = "1手で直せる候補は見つかりませんでした。下の詳細をご確認ください。";
            SmartStatusText.Visibility = Visibility.Visible;
            SmartTopPanel.Visibility = Visibility.Collapsed;
            return;
        }
        SmartStatusText.Visibility = Visibility.Collapsed;
        SmartTopPanel.Visibility = Visibility.Visible;
        var (tag, tagHex) = FixKindTag(top.Kind);
        var tagColor = ColorHex.Parse(tagHex, Colors.Gray);
        SmartKindBadge.Background = new SolidColorBrush(tagColor);
        SmartKindText.Text = tag;
        SmartKindText.Foreground = new SolidColorBrush(ReadableOn(tagColor));
        SmartLabelText.Text = top.Label;
        var diffTxt = string.Join("・", top.Diff.Select(d =>
            (AnalysisView.BreakdownLabels.TryGetValue(d.Family, out var jp) ? jp : d.Family) + " " +
            (d.Delta < 0 ? $"−{-d.Delta}" : $"+{d.Delta}")));
        var totalTxt = top.DeltaTotal <= 0 ? $"−{-top.DeltaTotal}" : $"+{top.DeltaTotal}";
        SmartDiffText.Text = $"違反 {totalTxt}" + (diffTxt.Length > 0 ? $"（{diffTxt}）" : "");
        SmartApplyButton.IsEnabled = editable;
        var more = ui.FixSuggestions.Count - 1;
        SmartMoreText.Visibility = more > 0 ? Visibility.Visible : Visibility.Collapsed;
        SmartMoreText.Text = more > 0 ? $"ほかに {more} 案あります（分析タブで比較できます）。" : "";
    }

    /// <summary>Kotlin原本 <c>fixKindTag</c> と同じ手の種別ラベルと色。</summary>
    private static (string, string) FixKindTag(FixKind k) => k switch
    {
        FixKind.Change => ("変更", MagiAccent.Green),
        FixKind.ChangeMulti => ("複数変更", MagiAccent.Green),
        FixKind.Swap => ("交換", MagiAccent.Blue),
        FixKind.SwapXDay => ("別日交換", MagiAccent.Blue),
        FixKind.SwapMulti => ("3人交換", MagiAccent.Purple),
        FixKind.Chain => ("連鎖", MagiAccent.Red),
        _ => ("再最適化", MagiAccent.Orange),
    };

    /// <summary>白文字のコントラストが 4.5:1 に届かない地色では黒文字にする（Kotlin原本 <c>ensureReadable</c>）。</summary>
    private static Windows.UI.Color ReadableOn(Windows.UI.Color bg)
    {
        static double Lin(byte c) { var v = c / 255.0; return v <= 0.03928 ? v / 12.92 : System.Math.Pow((v + 0.055) / 1.055, 2.4); }
        var lum = 0.2126 * Lin(bg.R) + 0.7152 * Lin(bg.G) + 0.0722 * Lin(bg.B);
        return (1.05 / (lum + 0.05)) >= 4.5 ? Colors.White : Colors.Black;
    }

    private void OnDetailToggleClick(object sender, RoutedEventArgs e)
    {
        _detailOpen = !_detailOpen;
        Render();
    }

    private void OnBigClick(object sender, RoutedEventArgs e) => _bigAction();

    private void OnHelperClick(object sender, RoutedEventArgs e) => _helperAction();

    private void OnSmartApplyClick(object sender, RoutedEventArgs e)
    {
        var ui = _vm.Ui;
        if (ui.FixSuggestions.Count > 0) _vm.ApplyFixSuggestion(ui.FixSuggestions[0]);
    }

    /// <summary>不足・過剰の各節に出す枠数の上限（Kotlin原本 CoverageDiagnosisCard の take(6) と同じ）。</summary>
    private const int MaxCoverageSlots = 6;

    /// <summary>担当追加の案の表示上限（Kotlin原本の take(4) と同じ）。</summary>
    private const int MaxRelaxations = 4;

    /// <summary>
    /// [phase9 #1] Kotlin原本 <c>CoverageDiagnosisCard</c> の逐語移植（見出しの場合分け=3.344.0、主因=3.406.0）。
    /// 過剰枠の「希望固定N人」は <see cref="CoverageSurplus.PinnedStaff"/> を名指しし、ボタン 1 つで
    /// <see cref="MagiViewModel.RemoveWish"/>（その職員のその日の希望を消す＝データ修正の導線、3.492.0）。
    /// </summary>
    private void RenderCoverage(UiState ui, bool editable)
    {
        var diag = ui.CoverageDiag;
        if (diag is null || (!diag.HasShortage && !diag.HasSurplus))
        {
            CoveragePanel.Visibility = Visibility.Collapsed;
            ShortageListHost.Children.Clear();
            RelaxationListHost.Children.Clear();
            SurplusListHost.Children.Clear();
            return;
        }
        CoveragePanel.Visibility = Visibility.Visible;
        RenderShortage(diag);
        RenderSurplus(ui, diag, editable);
    }

    private void RenderShortage(CoverageDiagnosis diag)
    {
        ShortageListHost.Children.Clear();
        RelaxationListHost.Children.Clear();
        if (!diag.HasShortage)
        {
            ShortagePanel.Visibility = Visibility.Collapsed;
            return;
        }
        ShortagePanel.Visibility = Visibility.Visible;
        ShortageHeadline.Text = diag.AllInfeasible
            ? $"不足 {diag.TotalShortfall} 人は全て充足不可。今のデータでは満たせません（想定内）。"
            : diag.AllBlockedNow
                ? $"不足 {diag.TotalShortfall} 人は、いまの希望・担当のままでは埋められません。希望を1件調整するか、担当を追加してください。"
                : diag.BlockedNowSlots > 0
                    ? $"不足 {diag.TotalShortfall} 人 — うち {diag.BlockedNowSlots} 枠はいまの希望のままでは埋められません（残りは再実行で解消し得ます）。"
                    : diag.InfeasibleSlots == 0
                        ? $"不足 {diag.TotalShortfall} 人は枠が足りています。再実行や設定の見直しで解消し得ます。"
                        : $"不足 {diag.TotalShortfall} 人 — 充足不可 {diag.InfeasibleSlots} 枠 / 充足可能 {diag.FixableSlots} 枠。";
        foreach (var s in diag.Shortfalls.Take(MaxCoverageSlots))
        {
            var infeasible = s.Verdict == CoverageVerdict.Infeasible;
            var body = new StackPanel { Spacing = 4 };
            var head = new Grid { ColumnSpacing = 8 };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = new TextBlock
            {
                Text = $"{s.DayLabel}  {s.ShiftSymbol}  必要{s.Need}/現状{s.Got}（不足{s.Miss}）",
                Style = StyleOf("MagiTitleSmallTextStyle"), TextWrapping = TextWrapping.Wrap,
            };
            head.Children.Add(title);
            var chip = new TextBlock
            {
                Text = infeasible ? "充足不可" : s.BlockedNow ? "今は不能" : "充足可能",
                Style = StyleOf("MagiLabelMediumTextStyle"), VerticalAlignment = VerticalAlignment.Center,
                Foreground = BrushOf(infeasible ? "MagiErrorBrush" : "MagiPrimaryBrush"),
            };
            Grid.SetColumn(chip, 1);
            head.Children.Add(chip);
            body.Children.Add(head);
            body.Children.Add(new TextBlock
            {
                Text = s.Reason, Style = StyleOf("MagiBodySmallTextStyle"), TextWrapping = TextWrapping.Wrap,
            });
            ShortageListHost.Children.Add(SlotCard(body, infeasible ? "MagiErrorContainerBrush" : "MagiSecondaryContainerBrush"));
        }
        var moreShort = diag.Shortfalls.Count - MaxCoverageSlots;
        ShortageMoreText.Visibility = moreShort > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShortageMoreText.Text = moreShort > 0 ? $"ほか {moreShort} 枠（詳細はログ出力を参照）" : "";
        RelaxationPanel.Visibility = diag.Relaxations.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var r in diag.Relaxations.Take(MaxRelaxations))
        {
            RelaxationListHost.Children.Add(new TextBlock
            {
                Text = "・" + r, Style = StyleOf("MagiBodySmallTextStyle"), TextWrapping = TextWrapping.Wrap,
            });
        }
    }

    private void RenderSurplus(UiState ui, CoverageDiagnosis diag, bool editable)
    {
        SurplusListHost.Children.Clear();
        if (!diag.HasSurplus)
        {
            SurplusPanel.Visibility = Visibility.Collapsed;
            return;
        }
        SurplusPanel.Visibility = Visibility.Visible;
        SurplusHeadline.Text = $"過剰 {diag.TotalSurplus} 人 — 在勤者を他シフトへ動かせば消えるはずが、動かない理由を枠ごとに示します。";
        foreach (var s in diag.Surpluses.Take(MaxCoverageSlots))
        {
            var body = new StackPanel { Spacing = 4 };
            body.Children.Add(new TextBlock
            {
                Text = $"{s.DayLabel}  {s.ShiftSymbol}  必要{s.Need}/現状{s.Got}（過剰{s.Excess}）",
                Style = StyleOf("MagiTitleSmallTextStyle"), TextWrapping = TextWrapping.Wrap,
            });
            var famJp = s.BlockedFamily is null ? null
                : AnalysisView.BreakdownLabels.TryGetValue(s.BlockedFamily, out var jp) ? jp : s.BlockedFamily;
            body.Children.Add(new TextBlock
            {
                Text = s.Reason + (famJp is null ? "" : $"（主因: {famJp}）"),
                Style = StyleOf("MagiBodySmallTextStyle"), TextWrapping = TextWrapping.Wrap,
            });
            foreach (var i in s.PinnedStaff ?? System.Array.Empty<int>())
            {
                var name = i >= 0 && i < ui.StaffNames.Count ? ui.StaffNames[i] : $"#{i}";
                var cancel = new Button
                {
                    Content = $"{name} の希望（{s.ShiftSymbol}）を取り消す",
                    HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 48,
                    Foreground = BrushOf("MagiErrorBrush"), IsEnabled = editable,
                };
                var staff = i;
                var day = s.DayIndex;
                cancel.Click += (_, _) => _vm.RemoveWish(staff, day);
                body.Children.Add(cancel);
            }
            SurplusListHost.Children.Add(SlotCard(body, "MagiSecondaryContainerBrush"));
        }
        var moreSurplus = diag.Surpluses.Count - MaxCoverageSlots;
        SurplusMoreText.Visibility = moreSurplus > 0 ? Visibility.Visible : Visibility.Collapsed;
        SurplusMoreText.Text = moreSurplus > 0 ? $"ほか {moreSurplus} 枠（詳細はログ出力を参照）" : "";
    }

    private static Border SlotCard(UIElement child, string brushKey) => new()
    {
        Child = child, Background = BrushOf(brushKey),
        CornerRadius = (CornerRadius)Application.Current.Resources["MagiCornerSM"],
        Padding = new Thickness((double)Application.Current.Resources["MagiSpacingMD"]),
    };

    private static Style StyleOf(string key) => (Style)Application.Current.Resources[key];

    private static Brush BrushOf(string key) => (Brush)Application.Current.Resources[key];

    private void OnMakeClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => _vm.RunV6FullOptimize();

    private void OnBackgroundClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => _vm.RunInBackground();

    private void OnStopClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => _vm.Stop();

    private void OnSmartInitialClick(object sender, RoutedEventArgs e) => _vm.GenerateSmartInitial();

    private void OnSoftPolishClick(object sender, RoutedEventArgs e) => _vm.RunSoftPolish();

    /// <summary>「他の案」の一覧＋適用ボタン。Portfolio実行後だけ <see cref="UiState.Alternatives"/> が
    /// 埋まる（それ以外のアルゴリズムでは常に空）ため、0件のときは節ごと隠す。</summary>
    private void RenderAlternatives(UiState ui, bool editable)
    {
        if (ui.Alternatives.Count == 0)
        {
            AlternativesPanel.Visibility = Visibility.Collapsed;
            AlternativesListHost.Children.Clear();
            return;
        }
        AlternativesPanel.Visibility = Visibility.Visible;
        AlternativesListHost.Children.Clear();
        // [トークン非適用, ファイル共通] FontSize=13(TextBlock)/12(Button)はMagiThemeタイポ
        // スケール(14始まり)より小さい一覧行用の調整値で厳密一致せず、Button.FontSizeはStyle
        // (TargetType=TextBlock)を型的に受け付けられないため、いずれもトークン化の対象外。
        for (var i = 0; i < ui.Alternatives.Count; i++)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new TextBlock
            {
                Text = ui.Alternatives[i], FontSize = 13, VerticalAlignment = VerticalAlignment.Center,
            });
            var apply = new Button { Content = "適用", FontSize = 12, IsEnabled = editable };
            var idx = i;
            apply.Click += (_, _) => _vm.ApplyAlternative(idx);
            row.Children.Add(apply);
            AlternativesListHost.Children.Add(row);
        }
    }
}
