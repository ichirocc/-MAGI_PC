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
    private readonly CoalescedRender _renderCoalescer;

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
        _renderCoalescer = new CoalescedRender(DispatcherQueue, Render);
        // [レビュー指摘 2026-09-04] タブはキャッシュされ再利用されるので、Unloaded で外した購読を Loaded で戻す
        //   （旧: コンストラクタで一度だけ購読＝一度離れたタブは以後の状態変化を受け取らず、表示もボタンの活性も
        //   古いままだった）。再表示時は見えていなかった間の変化をまとめて描く（UiSubscription の KDoc 参照）。
        _uiSub = new UiSubscription(_vm.Ui, OnUiChanged);
        _uiSub.Attach();
        Loaded += (_, _) => { if (_uiSub.Attach()) Render(); };
        Unloaded += (_, _) => _uiSub.Detach();
        Render();
    }

    // [2026-09-10, カクつき/フリーズ対策] CoalescedRender のKDoc参照。
    private void OnUiChanged(object? sender, PropertyChangedEventArgs e) => _renderCoalescer.Request();

    private void Render()
    {
        var ui = _vm.Ui;
        MessageText.Text = ui.Message ?? (ui.Loaded ? "" : "データを読み込んでいます…");
        SummaryText.Text = ui.Loaded
            ? $"満足度 {ui.Satisfaction} ・ 必須違反 {ui.BestHard} ・ 合計 {ui.BestSoft}"
            : "";
        var editable = ui.Loaded && !ui.Running;
        EmptyStateCard.Visibility = !ui.Loaded && !ui.Running ? Visibility.Visible : Visibility.Collapsed;
        MakeButton.IsEnabled = editable;
        BackgroundButton.IsEnabled = editable;
        StopButton.IsEnabled = ui.Running;

        RenderNextAction(ui, editable);
        RenderLive(ui);
        RenderCopilot(ui, editable);
        RenderCoverage(ui, editable);
        RenderAlternatives(ui, editable);
        RenderConsults(ui);
        MaybeShowChainPreview(ui);
    }

    /// <summary>[3.645.0/仕様 5.3] 相談してから決める判断の一覧（Kotlin <c>ConsultCard</c>）。対象と検討内容を後から再確認できる。「開く」でセル、「済」で外す。</summary>
    private void RenderConsults(UiState ui)
    {
        ConsultCard.Visibility = ui.Consults.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ConsultListHost.Children.Clear();
        if (ui.Consults.Count == 0) return;
        ConsultTitle.Text = $"相談中（{ui.Consults.Count}件）";
        for (var i = 0; i < ui.Consults.Count; i++)
        {
            var c = ui.Consults[i];
            var idx = i;
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = c.Subject, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            text.Children.Add(new TextBlock { Text = c.Note, FontSize = 13, Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
            row.Children.Add(text);
            if (c.Staff is int s && c.Day is int d)
            {
                var open = new HyperlinkButton { Content = "開く", MinHeight = 44 };
                open.Click += (_, _) => _window.OpenCell(s, d);
                Grid.SetColumn(open, 1);
                row.Children.Add(open);
            }
            var done = new Button { Content = "済", MinHeight = 44 };
            done.Click += (_, _) => _vm.RemoveConsult(idx);
            Grid.SetColumn(done, 2);
            row.Children.Add(done);
            ConsultListHost.Children.Add(row);
        }
    }

    private ChainFixPreview? _chainPreviewShown;

    /// <summary>[3.644.0/UX-03] 複数人の入替を当てる前の一覧（Kotlin <c>ChainFixPreviewDialog</c>）。変わる人・日・勤務（前 → 後）と必須の増減を見せ、
    /// 「この入替を当てる」で <see cref="MagiViewModel.ApplyChainPreview"/>（指紋照合・FixApplyGate・Undo を通る）、「やめる」で閉じる。</summary>
    private async void MaybeShowChainPreview(UiState ui)
    {
        var p = ui.ChainPreview;
        if (p is null || ReferenceEquals(p, _chainPreviewShown)) return;
        _chainPreviewShown = p;
        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(new TextBlock { Text = p.Suggestion.Label, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = "変わる人と勤務（前 → 後）", FontSize = 13, Opacity = 0.8 });
        foreach (var line in p.Changes) body.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap });
        var (hardLine, caution) = NextActionGuide.FixImpactLines(p.Suggestion, AnalysisView.LabelOf);
        body.Children.Add(new TextBlock { Text = hardLine, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (caution is not null) body.Children.Add(new TextBlock { Text = caution, FontSize = 13, Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = "当てる直前にもう一度検査し、必須が減らない・希望の固定を崩す手順は当てません。当てたあとは「元に戻す」で取り消せます。", FontSize = 13, Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = p.Title, Content = new ScrollViewer { Content = body, MaxHeight = 420 },
            PrimaryButtonText = "この入替を当てる", SecondaryButtonText = ConsultList.Button, CloseButtonText = "やめる", DefaultButton = ContentDialogButton.Primary,
        };
        var r = await dialog.ShowAsync();
        _chainPreviewShown = null;
        if (!ReferenceEquals(_vm.Ui.ChainPreview, p)) return;
        switch (r)
        {
            case ContentDialogResult.Primary: _vm.ApplyChainPreview(); break;
            case ContentDialogResult.Secondary: _vm.AddConsult(ConsultList.Chain(p, hardLine)); _vm.DismissChainPreview(); break;   // [3.645.0] 相談してから決める
            default: _vm.DismissChainPreview(); break;
        }
    }

    /// <summary>
    /// [phase9 #2] 処方箋カード。Kotlin原本 <c>OperatorNextActionCard</c>（3.480.0/3.483.0）の状態分岐を
    /// そのまま写す。実行中は見出し・でき具合を出さない（進捗行は #3）。飛び先の判断は docs/phase9/blockers.md #2。
    /// </summary>
    private void RenderNextAction(UiState ui, bool editable)
    {
        if (!ui.Loaded)
        {
            NextActionCard.Visibility = Visibility.Collapsed;
            return;
        }
        NextActionCard.Visibility = Visibility.Visible;
        // [3.643.0] 旧 SmartActionCard の自動探索をここへ（必須違反が残り未探索なら 1 回）。副作用は候補リストの計算だけ。
        if (!ui.Running && ui.HasResult && ui.BestHard > 0)
        {
            var boardChanged = !ReferenceEquals(_autoFixBoard, ui.Schedule) || _autoFixHard != ui.BestHard;
            if (ui.FixFocusName.Length > 0 || (boardChanged && !ui.FixSearching && ui.FixSuggestions.Count == 0 && !ui.FixSearched))
            {
                _autoFixBoard = ui.Schedule;
                _autoFixHard = ui.BestHard;
                _vm.FindFixSuggestions();
            }
        }
        var diag = ui.CoverageDiag;
        var infeasible = diag?.AllInfeasible == true;
        var shortfalls = diag?.Shortfalls ?? System.Array.Empty<CoverageShortfall>();
        var shortDays = shortfalls.Select(x => x.DayIndex).Distinct().Count();
        var worstDay = shortfalls.Count > 0 ? shortfalls[0].DayLabel : null;
        // [UX監査 高2] 「なおし方を見る」の日は開くダイアログと同じ枠から取る（ホームとダイアログの日を一致させる）。
        var guidedFix = GuidedFixRules.GuidedFixTarget(shortfalls);
        // [S5 §2.1] 関わる希望（S5a の行か S5b の行）があるか。WISH・FLOOR の分岐がこれを見る。
        var cands = NextActionGuide.WishTrialCandidatesOf(ui);
        // 主ボタンに最初の対象を添える（押した先は変えない）。
        string StaffName(int i) => i < ui.StaffNames.Count ? ui.StaffNames[i] : $"職員{i + 1}";
        var firstWish = cands.Direct.FirstOrDefault() ?? cands.Shortfall.FirstOrDefault()?.Rows.FirstOrDefault();
        var wishLabel = AnalysisTriage.HomeTargetLabel("ぶつかっている希望を見る", firstWish is null ? null : StaffName(firstWish.Staff), firstWish is null ? null : DayText.Short(ui.StartDate, firstWish.Day));
        var firstMove = ui.FixSuggestions.FirstOrDefault(fs => fs.DeltaHard < 0)?.Ops.FirstOrDefault();
        var moveLabel = AnalysisTriage.HomeTargetLabel("直す1手を見る", firstMove is null ? null : StaffName(firstMove.Staff), firstMove is null ? null : DayText.Short(ui.StartDate, firstMove.Day));

        // [UX改善/Android同期, ユーザー指示「ゲーム要素廃止」] phase「狩猟」はRPG風の演出語のため、
        //   「完成」の対語である平易な「未完成」へ変更。
        string bg, fg, headline, bigLabel, phase, phaseHex;
        string? helperLabel;
        string? body = null, note = null;   // 見出しの下の本文と注記（S6 の段だけ使う）
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
            // [Android 3.509.4/3.510.3 同期] 完了カードに前後比較（変更人数・セル数・希望充足・
            // 個人回数・族別の増減）を1行足す。族名の日本語化は AnalysisView.BreakdownLabels（既存）。
            headline = (ui.ImpossibleWishCount > 0 ? $"③ 必須違反はありません。担当できない希望が {ui.ImpossibleWishCount} 件あります。" : "③ 必須条件を満たしました。中身を確認して配ってください。") + (ui.RunSummary is { } rs
                ? "\n" + rs.Line() + "\n" + rs.FamilyLine(k => AnalysisView.BreakdownLabels.TryGetValue(k, out var jp) ? jp : k)
                : "");
            bigLabel = "印刷・書き出し"; bigEnabled = true; helperLabel = "中身を見る";
            phase = "完成"; phaseHex = MagiAccent.Green;
            _bigAction = () => _ = _window.ExportScheduleCsvAsync(); _helperAction = () => _window.SelectTab("schedule");
        }
        else if (infeasible && shortfalls.Any(s => s.WishPinned.Count > 0))
        {
            // 重複除去の前で決める（S5a の行に吸収された S5b の人も数える）。例の日も希望で固定された人がいる日から。
            var pinnedDay = shortfalls.FirstOrDefault(s => s.WishPinned.Count > 0)?.DayLabel;
            var pinnedLanding = shortfalls.FirstOrDefault(s => s.WishPinned.Count > 0) is { } pinned ? GuidedFixRules.LandingFor(pinned) : null;
            bg = "MagiErrorContainerBrush"; fg = "MagiOnErrorContainerBrush";
            headline = "いまの希望のままでは、ここは埋められません。" + (pinnedDay is null ? "" : $"（例：{pinnedDay}）");
            bigLabel = wishLabel; bigEnabled = true; helperLabel = "データを見直す";
            phase = "未完成"; phaseHex = MagiAccent.Orange;
            _bigAction = () => _ = ShowWishConflictsAsync(); _helperAction = () => GoEditLanding(pinnedLanding);
        }
        else if (infeasible)
        {
            bg = "MagiErrorContainerBrush"; fg = "MagiOnErrorContainerBrush";
            headline = "このデータでは、ここは埋められません。" + (worstDay is null ? "" : $"（例：{worstDay}）");
            bigLabel = "データを見直す"; bigEnabled = true; helperLabel = "未充足のまま書き出す";
            phase = "未完成"; phaseHex = MagiAccent.Orange;
            var infeasibleLanding = shortfalls.FirstOrDefault(s => s.Verdict == CoverageVerdict.Infeasible) is { } inf ? GuidedFixRules.LandingFor(inf) : null;
            _bigAction = () => GoEditLanding(infeasibleLanding); _helperAction = () => _ = _window.ExportScheduleCsvAsync();
        }
        else
        {
            // [Android 3.612.0 思考誘導S0/S4] 未完成は「足りる？→必須を減らす1手ある？→下限の見込み？→希望が関わる？」の順に
            //   主ボタンを1つだけ出す（旧: 不足が無いと大ボタンが消え、並び・希望の必須に行き先が無かった）。
            bg = "MagiWarnContainerBrush"; fg = "MagiOnWarnContainerBrush";
            phase = "未完成"; phaseHex = MagiAccent.Orange;
            helperLabel = null; _helperAction = () => { };
            var hardFix = ui.FixSuggestions.Any(s => s.DeltaHard < 0);
            var remain = $"必須違反が {ui.BestHard}件 残っています。";
            if (guidedFix is { } gf)
            {
                headline = $"{gf.DayLabel} が人員不足です。";
                bigLabel = AnalysisTriage.HomeTargetLabel("なおし方を見る", null, gf.DayLabel); bigEnabled = true;
                _bigAction = () => _ = ShowGuidedFixAsync();
            }
            else if (hardFix && ui.FixFocusName.Length == 0)
            {
                // [3.643.0] 旧「AIの解決提案」をここへ統合: 手の内容（本文）・効果（注記）・「この手を使う」（補助）。
                var top = ui.FixSuggestions.First(fs => fs.DeltaHard < 0);
                var (hardLine, caution) = NextActionGuide.FixImpactLines(top, AnalysisView.LabelOf);
                var diffTxt = string.Join("・", top.Diff.Select(d =>
                    (AnalysisView.BreakdownLabels.TryGetValue(d.Family, out var jp) ? jp : d.Family) + " " + (d.Delta < 0 ? $"−{-d.Delta}" : $"+{d.Delta}")));
                var totalTxt = top.DeltaTotal <= 0 ? $"−{-top.DeltaTotal}" : $"+{top.DeltaTotal}";
                headline = remain + "直す手があります。";
                body = top.Label;
                note = hardLine + (caution is null ? "" : "\n" + caution) + (diffTxt.Length > 0 ? $"\n違反 {totalTxt}（{diffTxt}）" : "") +
                    (ui.FixSuggestions.Count > 1 ? $"\nほかに {ui.FixSuggestions.Count - 1} 案あります（分析タブで比較できます）。" : "");
                bigLabel = moveLabel; bigEnabled = true;
                _bigAction = () => _window.SelectTab("analysis");
                helperLabel = "この手を使う（元に戻せます）"; _helperAction = () => _vm.ApplyFixSuggestion(top);
            }
            else if (ui.FixSearching)
            {
                headline = remain + "直し方を探しています…";
                bigLabel = ""; bigEnabled = false;
                _bigAction = () => { };
            }
            else if (ui.FixSearched && !hardFix && _vm.RelaxTrialFor() is { } relax)
            {
                // [S6 §2.1] 必須違反の一部が利用者自身の設定（上限 0）で塞がれているときだけ、希望の段より先に出す。
                var t = NextActionGuide.RelaxCardTextOf(relax.Result, ui, AnalysisView.LabelOf);
                headline = t.Headline; body = t.Body; note = t.Note;
                bigLabel = "緩める候補を見る"; bigEnabled = true;
                _bigAction = () => _ = ShowRelaxTrialAsync();
                if (!cands.IsEmpty) { helperLabel = "ぶつかっている希望を見る"; _helperAction = () => _ = ShowWishConflictsAsync(); }
            }
            else if (ui.FixSearched && !hardFix && ui.StalledHardFamilies.Count > 0 && !cands.IsEmpty)
            {
                // S6 の判定が済むまでは「下限」と言わない（設定を緩めれば減るかもしれない）。
                headline = ui.RelaxSearching ? remain : $"今の希望とルールでは、必須違反 {ui.BestHard}件 からこれ以上は減らせない見込みです。";
                bigLabel = wishLabel; bigEnabled = true;
                _bigAction = () => _ = ShowWishConflictsAsync();
                helperLabel = "このまま書き出す"; _helperAction = () => _ = _window.ExportScheduleCsvAsync();
            }
            else if (!cands.IsEmpty)
            {
                headline = remain + "希望とルールがぶつかっています。";
                bigLabel = wishLabel; bigEnabled = true;
                _bigAction = () => _ = ShowWishConflictsAsync();
            }
            else
            {
                headline = remain;
                bigLabel = "問題を見る"; bigEnabled = true;
                _bigAction = () => _window.SelectTab("analysis");
            }
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
        // [3.643.0] 探索がどう終わったか（停滞で早く終えた理由・残る必須の性質・次の一手）。内部名は出さない（StopExplanation）。
        var stopLine = ui.HasResult && !ui.Running && ui.StopSummary is { } stop && StopExplanation.Of(stop) is { } se
            ? se.Line + (StopExplanation.NextLabel(se.Next) is { } nx ? $" 次は: {nx}" : "")
            : null;
        StopText.Visibility = stopLine is null ? Visibility.Collapsed : Visibility.Visible;
        StopText.Text = stopLine ?? "";
        StopText.Foreground = fgBrush;
        BodyText.Visibility = body is null ? Visibility.Collapsed : Visibility.Visible;
        BodyText.Text = body ?? "";
        BodyText.Foreground = fgBrush;
        NoteText.Visibility = note is null ? Visibility.Collapsed : Visibility.Visible;
        NoteText.Text = note ?? "";
        NoteText.Foreground = fgBrush;
        // [S5 §9] 直近の「希望を取り消して再作成」の結果（VM が鮮度を照合済み）。
        var outcomeLine = ui.Running ? null : _vm.FixOutcomeLine() ?? _vm.CsvSavedLine() ?? _vm.WishCancelOutcomeLine() ?? _vm.RelaxDoneLine();
        OutcomeText.Visibility = outcomeLine is null ? Visibility.Collapsed : Visibility.Visible;
        OutcomeText.Text = outcomeLine ?? "";
        OutcomeText.Foreground = fgBrush;
        // [3.645.0/仕様 5.3] 相談中の件数（未確認事項）。完成の見出しは変えず、書き出しも止めない。
        var consultLine = ui.Running ? null : ConsultList.Line(ui.Consults.Count);
        ConsultText.Visibility = consultLine is null ? Visibility.Collapsed : Visibility.Visible;
        ConsultText.Text = consultLine ?? "";
        ConsultText.Foreground = fgBrush;
        var relaxFailed = !ui.RelaxSearching && ui.BestHard > 0 && _vm.RelaxFailed();
        var relaxStopped = relaxFailed || (!ui.RelaxSearching && ui.BestHard > 0 && _vm.RelaxStopped());
        RelaxSearchRow.Visibility = !ui.Running && (ui.RelaxSearching || relaxStopped) ? Visibility.Visible : Visibility.Collapsed;
        RelaxSearchText.Text = relaxFailed ? NextActionGuide.RelaxFailedText : relaxStopped ? NextActionGuide.RelaxStoppedText : NextActionGuide.RelaxSearchingText;
        RelaxStopButton.Content = relaxStopped ? NextActionGuide.RelaxRetryLabel : "やめる";
        RelaxSearchText.Foreground = fgBrush;

        var remaining = AnalysisTriage.HomeRemainingLabel(ui.BestHard, shortDays, ui.Breakdown, ui.HardWishConflict);
        var showResolve = !ui.Running;
        ResolveText.Visibility = showResolve ? Visibility.Visible : Visibility.Collapsed;
        ResolveBar.Visibility = showResolve ? Visibility.Visible : Visibility.Collapsed;
        ResolveText.Text = $"でき具合：{ui.Satisfaction}%（{remaining}）";
        ResolveText.Foreground = fgBrush;
        ResolveBar.Value = System.Math.Clamp(ui.Satisfaction, 0, 100);
        ResolveBar.Foreground = fgBrush;

        var showDetail = !ui.Running && ui.HasResult;
        DetailToggle.Visibility = showDetail ? Visibility.Visible : Visibility.Collapsed;
        DetailToggle.Content = _detailOpen ? "ⓘ 詳しい説明を閉じる" : "ⓘ でき具合の意味";
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

    private void OnStopRelaxClick(object sender, RoutedEventArgs e)
    {
        if (_vm.Ui.RelaxSearching) _vm.CancelRelaxTrial();
        else _vm.RetryRelaxTrial();
    }

    /// <summary>[S6] 設定を緩める候補（Kotlin <c>RelaxTrialDialog</c>、<c>docs/s6_relax_trial.md</c> §5）。盤面は見せず手順を言葉で出し、押したときだけ当てる。
    /// 確定の前に全部の変更（設定・手順の全セル）を読める＝窓の外の手も畳むだけで隠さない。</summary>
    internal async Task ShowRelaxTrialAsync((int I, int J)? from = null)
    {
        var panel = new StackPanel { Spacing = 4, MinWidth = 360 };
        var token = _vm.RelaxTrialFor();
        var t = token is null ? null : NextActionGuide.RelaxTrialTextOf(token.Result, _vm.Ui, AnalysisView.LabelOf);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = t?.DialogTitle ?? "例外として上限を緩める候補",
            Content = new ScrollViewer { Content = panel, MaxHeight = 420 },
            CloseButtonText = "閉じる", DefaultButton = ContentDialogButton.Close,
        };
        TextBlock Line(string text, double size = 14, bool dim = false, bool bold = false) => new()
        {
            Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Opacity = dim ? 0.8 : 1.0,
            FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
        };
        if (token is null || t is null) panel.Children.Add(Line("勤務表か設定が変わりました。もう一度試算してください。"));
        else
        {
            panel.Children.Add(Line(NextActionGuide.RelaxWishLine, bold: true));
            panel.Children.Add(Line(t.HardLine, bold: true));
            panel.Children.Add(Line(t.ScaleLine, dim: true));
            if (t.PrerequisiteRows.Count > 0)
            {
                panel.Children.Add(Line(NextActionGuide.RelaxPrereqHead, bold: true));
                panel.Children.Add(Line(NextActionGuide.RelaxPrereqWhy, dim: true));
                foreach (var r in t.PrerequisiteRows) panel.Children.Add(Line("・" + r));
            }
            panel.Children.Add(Line(NextActionGuide.RelaxSetHead, bold: true));
            panel.Children.Add(Line(t.Lead, 16));
            foreach (var r in t.Rows) panel.Children.Add(Line("・" + r));
            panel.Children.Add(Line(t.SolveNote, dim: true));
            panel.Children.Add(Line("手順", bold: true));
            foreach (var m in t.MoveLines) panel.Children.Add(Line(m));
            if (t.OtherMoves > 0)
            {
                var others = new StackPanel { Spacing = 4, Visibility = Visibility.Collapsed };
                foreach (var m in t.OtherMoveLines) others.Children.Add(Line(m));
                var showAll = new HyperlinkButton { Content = $"ほか {t.OtherMoves}セル（タップですべて表示）", MinHeight = 44, Padding = new Thickness(0) };
                showAll.Click += (_, _) => { showAll.Visibility = Visibility.Collapsed; others.Visibility = Visibility.Visible; };
                panel.Children.Add(showAll);
                panel.Children.Add(others);
            }
            if (t.KeepNote is { } keep) panel.Children.Add(Line(keep, dim: true));
            var confirm = new Button
            {
                Content = "上限を緩め、手順を当てる", HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 44,
                Style = (Style)Application.Current.Resources["AccentButtonStyle"], IsEnabled = !_vm.Ui.Running,
            };
            confirm.Click += (_, _) => { dialog.Hide(); _vm.RelaxAndApply(token); if (from is { } c) _window.OpenCell(c.I, c.J); };
            panel.Children.Add(confirm);
            panel.Children.Add(Line("元に戻すで設定と勤務表をまとめて戻せます。", dim: true));
        }
        await dialog.ShowAsync();
    }

    /// <summary>[思考誘導S3→S5] 必須違反に関わる希望と、人員不足の日に別の勤務の希望がある人を並べる（Kotlin <c>WishConflictDialog</c>）。行を押すとセル、「取り消したら？」で試算・確定（§5）。
    /// 試算の結果は VM が ctx つきで持ち、ここは組み直すたびに問い合わせる（古ければ隠す＝§8）。</summary>
    internal async Task ShowWishConflictsAsync()
    {
        var panel = new StackPanel { Spacing = 4, MinWidth = 360 };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = "ぶつかっている希望",
            Content = new ScrollViewer { Content = panel, MaxHeight = 420 },
            CloseButtonText = "閉じる", DefaultButton = ContentDialogButton.Close,
        };
        var expanded = new HashSet<(int Day, int Shift)>();
        TextBlock Small(string text, bool dim = true) =>
            new() { Text = text, FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 0, 0, 0), Opacity = dim ? 0.8 : 1.0 };
        Button TrialButton(WishTrialRow row, bool enabled)
        {
            var b = new Button { Content = "取り消したら？", MinHeight = 44, Margin = new Thickness(4, 0, 0, 0), IsEnabled = enabled };
            b.Click += (_, _) => _vm.StartWishTrial(row.Staff, row.Day);
            return b;
        }
        void AddRow(WishTrialRow row, UiState ui)
        {
            var label = new TextBlock { Text = $"{row.Name} ・ {DayText.Short(ui.StartDate, row.Day)}　{row.Reason}", TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            var content = new Grid { ColumnSpacing = 8 };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.Children.Add(label);
            if (row.WishOnly)
            {
                var chip = AnalysisView.TagChip(NextActionGuide.WishOnlyTag, MagiAccent.Orange);
                Grid.SetColumn(chip, 1);
                content.Children.Add(chip);
            }
            var open = new Button { Content = content, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, MinHeight = 44 };
            open.Click += (_, _) => { dialog.Hide(); _window.OpenCell(row.Staff, row.Day); };
            panel.Children.Add(open);
            // [3.645.0/仕様 5.3] 本人や上長に確認してから決める＝対象と検討内容を相談中の一覧へ（取消も勤務の変更もしない）。
            var consult = new HyperlinkButton { Content = ConsultList.Button, MinHeight = 44, Margin = new Thickness(4, 0, 0, 0) };
            consult.Click += (_, _) => _vm.AddConsult(ConsultList.Wish(row.Name, DayText.Short(ui.StartDate, row.Day),
                ui.Wishes.TryGetValue($"{row.Staff},{row.Day}", out var wk) && wk >= 0 && wk < ui.ShiftSymbols.Count ? ui.ShiftSymbols[wk] : null, row.Reason, row.Staff, row.Day));
            panel.Children.Add(consult);
            if (!row.Locked || !ui.Wishes.TryGetValue($"{row.Staff},{row.Day}", out var k))
            {
                panel.Children.Add(Small(row.Pinned ? NextActionGuide.WishTrialPinned : NextActionGuide.WishTrialNotLocked));
                return;
            }
            var canTrial = ui.WishTrialBusy is null && !ui.Running;
            switch (_vm.WishTrialFor(row.Staff, row.Day, k))
            {
                case WishTrialView.BusyView:
                    panel.Children.Add(Small("試算しています…"));
                    break;
                case WishTrialView.StaleView:
                    panel.Children.Add(Small("勤務表が変わりました。もう一度試算してください。"));
                    panel.Children.Add(TrialButton(row, canTrial));
                    break;
                case WishTrialView.Ready ready:
                    if (NextActionGuide.WishTrialText(ready.Outcome) is { } text) panel.Children.Add(Small(text, dim: false));
                    if (_vm.RelaxTrialFor() is not null && NextActionGuide.WishTrialNoGain(ready.Outcome))
                    {
                        var toRelax = new HyperlinkButton { Content = NextActionGuide.WishToRelaxLabel, MinHeight = 44, Margin = new Thickness(4, 0, 0, 0) };
                        toRelax.Click += (_, _) => { dialog.Hide(); _ = ShowRelaxTrialAsync(); };
                        panel.Children.Add(toRelax);
                    }
                    if (ready.Token.Result is not null)
                    {
                        var confirm = new Button
                        {
                            Content = "希望を取り消して再作成", MinHeight = 44, Margin = new Thickness(4, 0, 0, 0),
                            Foreground = BrushOf("MagiErrorBrush"), IsEnabled = !ui.Running,
                        };
                        var token = ready.Token;
                        confirm.Click += (_, _) => { dialog.Hide(); _vm.CancelWishAndRebuild(token); };
                        panel.Children.Add(confirm);
                    }
                    break;
                default:
                    panel.Children.Add(TrialButton(row, canTrial));
                    break;
            }
        }
        void Rebuild()
        {
            panel.Children.Clear();
            var ui = _vm.Ui;
            var cands = NextActionGuide.WishTrialCandidatesOf(ui);
            if (cands.IsEmpty)
            {
                panel.Children.Add(new TextBlock { Text = "いま必須違反に関わる希望はありません。", TextWrapping = TextWrapping.Wrap });
                return;
            }
            if (_vm.WishTrialControlFor() is { } control && NextActionGuide.WishTrialKeepOnlyText(control) is { } keepOnly)
            {
                panel.Children.Add(new TextBlock { Text = keepOnly, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                var rebuild = new Button { Content = "再作成", HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 44, IsEnabled = !ui.Running };
                rebuild.Click += (_, _) => { dialog.Hide(); _vm.RunV6FullOptimize(); };
                panel.Children.Add(rebuild);
            }
            if (cands.Direct.Count > 0)
            {
                panel.Children.Add(new TextBlock { Text = "この希望とルールがぶつかっています。1件ずつ開いて、希望を変えるか勤務を決めてください。", TextWrapping = TextWrapping.Wrap, Opacity = 0.85 });
                foreach (var row in cands.Direct) AddRow(row, ui);
            }
            if (cands.Shortfall.Count > 0)
            {
                panel.Children.Add(new TextBlock { Text = "人員不足の日に、別の勤務の希望がある人", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
                foreach (var g in cands.Shortfall)
                {
                    panel.Children.Add(new TextBlock { Text = g.Header, FontSize = 14, Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
                    var slot = (g.Day, g.Shift);
                    var rows = expanded.Contains(slot) ? g.Rows : g.Rows.Take(NextActionGuide.WishTrialGroupLimit).ToList();
                    foreach (var row in rows) AddRow(row, ui);
                    if (rows.Count < g.Rows.Count)
                    {
                        var more = new Button { Content = $"ほか {g.Rows.Count - rows.Count}人", MinHeight = 44 };
                        more.Click += (_, _) => { expanded.Add(slot); Rebuild(); };
                        panel.Children.Add(more);
                    }
                }
            }
            panel.Children.Add(new TextBlock { Text = NextActionGuide.WishKeepFooter, FontSize = 14, Opacity = 0.8, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        }
        void OnChanged(object? s, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is null or nameof(UiState.WishTrialRev) or nameof(UiState.WishTrialBusy) or nameof(UiState.Wishes)
                or nameof(UiState.Running)) Rebuild();
        }
        Rebuild();
        _vm.Ui.PropertyChanged += OnChanged;
        try { await dialog.ShowAsync(); }
        finally
        {
            // 閉じる・行を押してセルへ移る・確定、どの閉じ方でもここ 1 か所で試算を止める（§8）。
            _vm.CancelWishTrial();
            _vm.Ui.PropertyChanged -= OnChanged;
        }
    }

    /// <summary>
    /// [phase9 #24] 「なおし方を見る」（Kotlin原本 <c>GuidedFixDialog</c>、3.401.0/3.475.0）。判断は <see cref="GuidedFixPlan"/>、
    /// 候補の有効/無効は <see cref="GuidedFixFlow"/>（どちらも UI 非依存でテスト済み）。押したら押下後の再検査（<see cref="UiState.CheckRev"/>）が
    /// 反映されるまで全候補を無効にし「再検査中…」を出す。Schedule の変更だけでは再有効化しない（古い診断と新しい盤面の混在を防ぐ）。
    /// ボタンは常に「閉じる」だけ（「再作成」は下部バーに一本化）。
    /// </summary>
    private async System.Threading.Tasks.Task ShowGuidedFixAsync()
    {
        var panel = new StackPanel { Spacing = 8, MinWidth = 360 };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Content = new ScrollViewer { Content = panel, MaxHeight = 420 },
            CloseButtonText = "閉じる", DefaultButton = ContentDialogButton.Close,
        };
        var flow = new GuidedFixFlow();
        // 9人目以降を出している枠（日, シフト）。別の枠へ移ると閉じる（Kotlin の remember(day, shift) と同じ）。
        var showAllFor = (-1, -1);
        // 原因に対応する設定へ行く。押したらダイアログを閉じてから着地する（null＝編集タブの先頭）。
        Button LandingButton(EditLanding? landing)
        {
            var b = new Button { Content = GuidedFixRules.LandingButtonLabel(landing), HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 44 };
            b.Click += (_, _) => { dialog.Hide(); GoEditLanding(landing); };
            return b;
        }
        void Rebuild()
        {
            panel.Children.Clear();
            var ui = _vm.Ui;
            var plan = GuidedFixPlan.Build(ui.CoverageDiag);
            dialog.Title = plan.Title;
            var target = plan.Target;

            if (target is not null)
            {
                panel.Children.Add(new TextBlock { Text = $"{target.DayLabel} の「{target.ShiftSymbol}」が {target.Miss}人 足りません。", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                var cands = _vm.ShortageFixCandidates(target.DayIndex, target.ShiftIndex);
                if (cands.Count > 0)
                {
                    // [UX監査 中5] 件数を出し、既定の8人を超えたら「すべて表示」で9人目以降も選べる。
                    panel.Children.Add(new TextBlock { Text = $"この日に動かせる人が{cands.Count}人います。だれかを「{target.ShiftSymbol}」に入れますか？", FontSize = 14, Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
                    var showAll = showAllFor == (target.DayIndex, target.ShiftIndex);
                    var shown = showAll ? cands.ToList() : cands.Take(GuidedFixRules.GuidedFixPreview).ToList();
                    foreach (var c in shown)
                    {
                        var tail = c.FromRest ? "（休み）" : "";
                        var b = new Button
                        {
                            Content = $"{c.Name}{tail} を「{target.ShiftSymbol}」に入れる", HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 44,
                            IsEnabled = flow.CandidatesEnabled && !ui.Running,
                        };
                        var i = c.StaffIndex; var day = target.DayIndex; var shift = target.ShiftIndex;
                        b.Click += (_, _) =>
                        {
                            if (!flow.CandidatesEnabled || _vm.EditBlockedNow()) return;
                            flow.Press(_vm.Ui.CheckRev);
                            Rebuild();
                            _vm.NoteGuidedFix(day, shift, target.DayLabel, target.ShiftSymbol);
                            _vm.SetCell(i, day, shift);
                        };
                        panel.Children.Add(b);
                    }
                    if (cands.Count > GuidedFixRules.GuidedFixPreview)
                    {
                        var toggle = new Button
                        {
                            Content = showAll ? "一部だけ表示" : $"すべて表示（ほか{cands.Count - GuidedFixRules.GuidedFixPreview}人）",
                            HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 44,
                        };
                        toggle.Click += (_, _) => { showAllFor = showAll ? (-1, -1) : (target.DayIndex, target.ShiftIndex); Rebuild(); };
                        panel.Children.Add(toggle);
                    }
                    panel.Children.Add(new TextBlock
                    {
                        Text = flow.Pending ? "再検査中…（結果が反映されるまで候補は押せません）" : "入れたら「元に戻す」でいつでも取り消せます。",
                        FontSize = 14, Opacity = 0.8, TextWrapping = TextWrapping.Wrap,
                    });
                    // [3.645.0/仕様 5.3] だれを入れるかを相談してから決める＝対象と候補を相談中の一覧へ。
                    var consult = new HyperlinkButton { Content = ConsultList.Button, HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 44 };
                    consult.Click += (_, _) => _vm.AddConsult(ConsultList.Shortage(target.DayLabel, target.ShiftSymbol, cands.Select(c => c.Name).ToList()));
                    panel.Children.Add(consult);
                }
                else if (target.ChainVerified)
                {
                    // [UX監査 高1] 1人を動かすだけでは埋まらないが、複数人の入替で埋まると分析が確かめた枠。
                    //   [3.644.0/UX-03] 当てる前に一覧（だれの・どの日の・何→何と必須の増減）を見せる＝MaybeShowChainPreview。
                    panel.Children.Add(new TextBlock { Text = "だれか1人を動かすだけでは埋まりません。複数人の入れ替えで埋められます。", FontSize = 14, Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
                    var chain = new Button { Content = "入替の一覧と影響を見る", HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 44 };
                    chain.Click += (_, _) =>
                    {
                        dialog.Hide();
                        _vm.PrepareShortageChainFix(target.DayIndex, target.ShiftIndex, $"（玉突き）{target.DayLabel} の「{target.ShiftSymbol}」を複数人の入替で埋める");
                    };
                    panel.Children.Add(chain);
                }
                else
                {
                    panel.Children.Add(new TextBlock { Text = target.Reason, Foreground = BrushOf("MagiErrorBrush"), FontSize = 14, TextWrapping = TextWrapping.Wrap });
                    panel.Children.Add(LandingButton(GuidedFixRules.LandingFor(target)));
                }
            }
            else if (plan.Infeasible.Count > 0)
            {
                panel.Children.Add(new TextBlock { Text = "これ以上は自動で埋められません。", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                foreach (var sf in plan.Infeasible.Take(4))
                    panel.Children.Add(new TextBlock { Text = $"・{sf.DayLabel}「{sf.ShiftSymbol}」：{sf.Reason}", FontSize = 14, Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
                panel.Children.Add(new TextBlock { Text = "人を増やすか、担当できるシフトや希望を見直すと直せます。", FontSize = 14, Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
                panel.Children.Add(LandingButton(GuidedFixRules.LandingFor(plan.Infeasible[0])));
            }
            else if (plan.Blocked.Count > 0)
            {
                panel.Children.Add(new TextBlock { Text = "いまの希望・担当のままでは埋められない日が残っています。", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                foreach (var sf in plan.Blocked.Take(4))
                    panel.Children.Add(new TextBlock { Text = $"・{sf.DayLabel}「{sf.ShiftSymbol}」：{sf.Reason}", FontSize = 14, Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
                panel.Children.Add(new TextBlock { Text = "再作成しても、この日は同じ結果になります。希望を1件調整する（編集タブ＞月次条件）か、担当できるシフトを増やしてください（編集タブ＞年間マスター）。", FontSize = 14, Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
                panel.Children.Add(LandingButton(plan.Blocked.Select(sf => GuidedFixRules.LandingFor(sf)).FirstOrDefault(l => l is not null)));
            }
            else
            {
                panel.Children.Add(new TextBlock { Text = "人員不足の日はなくなりました。仕上げに再作成すると全体が整います。", TextWrapping = TextWrapping.Wrap });
            }
        }
        void OnChanged(object? s, PropertyChangedEventArgs e)
        {
            var rebuild = e.PropertyName switch
            {
                nameof(UiState.CheckRev) => flow.OnCheckReflected(_vm.Ui.CheckRev),
                nameof(UiState.Schedule) or nameof(UiState.CoverageDiag) => flow.OnScheduleChanged(),
                null => flow.OnCheckReflected(_vm.Ui.CheckRev),
                _ => false,
            };
            if (rebuild) Rebuild();
        }
        Rebuild();
        _vm.Ui.PropertyChanged += OnChanged;
        try { await dialog.ShowAsync(); }
        finally { flow.Close(); _vm.Ui.PropertyChanged -= OnChanged; }
    }

    private void OnHelperClick(object sender, RoutedEventArgs e) => _helperAction();

    /// <summary>[phase9 #4] コパイロット（Kotlin原本 <c>CopilotCard</c>）。3 つの助言はどれも「あるときだけ」出す。</summary>
    private void RenderCopilot(UiState ui, bool editable)
    {
        var wish = ui.ImpossibleWishCount > 0;
        var hint = !string.IsNullOrWhiteSpace(ui.CopilotHint);
        var polish = ui.PolishExhausted && !ui.Running;
        CopilotCard.Visibility = wish || hint || polish ? Visibility.Visible : Visibility.Collapsed;
        ImpossibleWishPanel.Visibility = wish ? Visibility.Visible : Visibility.Collapsed;
        ImpossibleWishText.Text = wish ? $"⚠ 実現できない希望が {ui.ImpossibleWishCount} 件（担当外シフトなど）。配布前に見直しを。" : "";
        CopilotHintPanel.Visibility = hint ? Visibility.Visible : Visibility.Collapsed;
        CopilotHintText.Text = hint ? $"💡 {ui.CopilotHint}" : "";
        PolishPanel.Visibility = polish ? Visibility.Visible : Visibility.Collapsed;
        SoftPolishButton.IsEnabled = editable;
    }

    private void OnGoEditClick(object sender, RoutedEventArgs e) => _window.SelectTab("edit");

    /// <summary>[UX監査 中4] 「データを見直す」の着地。原因が分かるときは対応する入口を開き（0=月次条件／2=年間マスター）、分からない（null）ときは編集タブの先頭。</summary>
    private void GoEditLanding(EditLanding? landing) => _window.GoEditLanding(landing);

    // 希望の編集は月次条件、手修正は勤務表タブ＝編集タブの今の入口に任せない。
    private void OnEditWishesClick(object sender, RoutedEventArgs e) => _window.OpenEditDoor(0);

    private void OnManualEditClick(object sender, RoutedEventArgs e) => _window.SelectTab("schedule");

    private async void OnEmptyOpenClick(object sender, RoutedEventArgs e) => await _window.OpenDataAsync();

    private async void OnEmptySampleClick(object sender, RoutedEventArgs e) => await _window.LoadFixtureAsync();

    /// <summary>空から作る。未読込なので確認ダイアログは出さず（失うものが無い）、年間マスターを整える編集タブへ送る。</summary>
    private void OnEmptyNewClick(object sender, RoutedEventArgs e)
    {
        _vm.InitBlankState();
        _window.SelectTab("edit");
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
                    ? $"不足 {diag.TotalShortfall} 人 — うち {diag.BlockedNowSlots} 枠はいまの希望のままでは埋められません（残りは再作成すると解消し得ます）。"
                    : diag.InfeasibleSlots == 0
                        ? $"不足 {diag.TotalShortfall} 人は枠が足りています。再作成するか設定の見直しで解消し得ます。"
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
        // [2026-09-10, 可読性] このファイルのFontSizeはMagiThemeタイポスケールの本文最小(14)まで
        //   引き上げ済み（一覧行用に10〜13へ据え置いていたのを解消。Button.FontSizeはStyle
        //   (TargetType=TextBlock)を型的に受け付けられずトークン化の対象外なのは変わらず）。
        // 適用中の案は太字＋「適用中」（VM が持つ＝元に戻すで一覧ごと戻る）。残りの案へはそのまま切り替えられる。
        for (var i = 0; i < ui.Alternatives.Count; i++)
        {
            var applied = i == ui.AlternativeApplied;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new TextBlock
            {
                Text = ui.Alternatives[i], FontSize = 14, VerticalAlignment = VerticalAlignment.Center,
                FontWeight = applied ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            });
            var apply = new Button { Content = applied ? "適用中" : "適用", FontSize = 14, IsEnabled = editable && !applied };
            var idx = i;
            apply.Click += (_, _) => _vm.ApplyAlternative(idx);
            row.Children.Add(apply);
            AlternativesListHost.Children.Add(row);
        }
    }
}
