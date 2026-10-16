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
        // 未読込は空状態のカードだけ。読込中は補足を 1 文にして 3 つを押せなくする（二重に開かない。Kotlin EmptyStateCard(loading)）。
        var loading = ui.Running && !ui.Loaded;
        EmptyStateCard.Visibility = ui.Loaded ? Visibility.Collapsed : Visibility.Visible;
        EmptyTitleText.Text = CellSheetLogic.EmptyStateTitle;
        EmptySubtitleText.Text = CellSheetLogic.EmptyStateSubtitle(loading);
        EmptyOpenButton.IsEnabled = !loading;
        EmptySampleButton.IsEnabled = !loading;
        EmptyNewButton.IsEnabled = !loading;
        var editable = ui.Loaded && !ui.Running;

        RenderNextAction(ui, editable);
        RenderLive(ui);
        RenderCopilot(ui, editable);
        RenderCoverage(ui, editable);
        RenderAlternatives(ui, editable);
        // [3.696.0] 診断の束は実行中は出さない＝置き換えている前の盤面の説明が途中経過の下に長く並ぶ（Android 端末報告「縦に長すぎる」）。
        if (ui.Running) { CopilotCard.Visibility = Visibility.Collapsed; CoveragePanel.Visibility = Visibility.Collapsed; AlternativesPanel.Visibility = Visibility.Collapsed; }
        RenderConsults(ui);
    }

    /// <summary>[3.645.0/仕様 5.3] 相談してから決める判断の一覧（Kotlin <c>ConsultCard</c>）。対象と検討内容を後から再確認できる。「開く」でセル、
    /// 入れ替えの相談は「案を見る」で今の勤務表の案（<see cref="MagiViewModel.ResumeConsultChain"/>）、「済」で外す。
    /// 複数人の入れ替えの一覧（<c>ChainPreview</c>）のダイアログは MainWindow が出す（3.646.0: タブに関係なく出るため）。</summary>
    private void RenderConsults(UiState ui)
    {
        ConsultCard.Visibility = ui.Consults.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ConsultListHost.Children.Clear();
        if (ui.Consults.Count == 0) return;
        var heldN = ui.Consults.Count(c => c.Held);
        ConsultTitle.Text = $"相談中（{ui.Consults.Count - heldN}件）" + (heldN > 0 ? $"・保留（{heldN}件）" : "");
        foreach (var c in ui.Consults)
        {
            // 対象は職員 ID（無ければ氏名）と実日付で今のデータに引き直す（月を移した・職員を消した・並べ替えたあとに別のセルを開かない。3.646.0 B01/B02）。
            var cell = c.Chain is null ? ConsultList.ConsultCell(c, ui.StartDate, ui.StaffNames, ui.Days, ui.StaffIds) : null;
            var chainOk = c.Chain is { } t && ConsultList.ConsultChainResumable(t, ui.StartDate, ui.ShiftSymbols, ui.Days);
            var note = ConsultList.ConsultTargetNote(c, ui.StartDate, ui.StaffNames, ui.ShiftSymbols, ui.Days, ui.StaffIds);
            // [3.696.0] 操作は文の下の 1 行に（右に 3 つ並べると文が潰れて縦に伸びた＝Android 端末報告「縦に長すぎる」）。
            var row = new StackPanel();
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = (c.Held ? $"【{ConsultList.HeldLabel}】" : "") + c.Subject, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            text.Children.Add(new TextBlock { Text = c.Note, FontSize = 13, Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
            if (note is not null) text.Children.Add(new TextBlock { Text = note, FontSize = 13, Foreground = BrushOf("MagiErrorBrush"), TextWrapping = TextWrapping.Wrap });
            // 回答は記録だけ（了承でも違反は減らさない）。押すたびに 回答待ち→了承→不可（Kotlin ConsultCard）。
            var answer = new HyperlinkButton { Content = ConsultList.AnswerLine(c, _vm.ConsultAnswerOf(c)), MinHeight = 44, Padding = new Thickness(0) };
            answer.Click += (_, _) => _vm.CycleConsultAnswer(c);
            text.Children.Add(answer);
            row.Children.Add(text);
            if (cell is { } cc)
            {
                var open = new HyperlinkButton { Content = ConsultList.Open, MinHeight = 44 };
                open.Click += (_, _) => _window.OpenCell(cc.I, cc.J);
                actions.Children.Add(open);
            }
            else if (chainOk)
            {
                var resume = new HyperlinkButton { Content = ConsultList.Resume, MinHeight = 44, IsEnabled = !ui.Running };
                resume.Click += (_, _) => _vm.ResumeConsultChain(c);
                actions.Children.Add(resume);
            }
            var hold = new HyperlinkButton { Content = c.Held ? "相談に戻す" : ConsultList.HoldButton, MinHeight = 44 };
            hold.Click += (_, _) => _vm.ToggleConsultHold(c);
            actions.Children.Add(hold);
            var done = new Button { Content = "済", MinHeight = 44 };
            done.Click += (_, _) => _vm.RemoveConsult(c);
            actions.Children.Add(done);
            row.Children.Add(actions);
            ConsultListHost.Children.Add(row);
        }
    }

    /// <summary>
    /// [phase9 #2] 処方箋カード。Kotlin原本 <c>OperatorNextActionCard</c> の状態分岐をそのまま写す。
    /// [Android 3.698.0] 並びは純関数 <see cref="CellSheetLogic.HomeCardSectionsOf"/> が決める＝XAML はその正順で区画を置き、ここは区画ごとの表示と中身だけを決める。
    /// 段の対象は <see cref="CellSheetLogic.CreationNextTarget"/> の 1 か所で決め、主ボタンの括弧と「次に確認する問題」の両方に使う（INV-01）。
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
        string StaffName(int i) => i < ui.StaffNames.Count ? ui.StaffNames[i] : $"職員{i + 1}";
        var firstWish = cands.Direct.FirstOrDefault() ?? cands.Shortfall.FirstOrDefault()?.Rows.FirstOrDefault();
        var firstMove = ui.FixSuggestions.FirstOrDefault(fs => fs.DeltaHard < 0)?.Ops.FirstOrDefault();
        // 必須違反の巡回の先頭（残りの一覧・探索中の段の対象）。
        var tour = !ui.Running && ui.HasResult && ui.BestHard > 0 ? _vm.HardViolationItemsFor(AnalysisView.LabelOf).FirstOrDefault() : null;
        var tourNext = tour is null ? null : CellSheetLogic.CreationNextProblem(tour.Cell.I < ui.StaffNames.Count ? ui.StaffNames[tour.Cell.I] : null, tour.Heading);

        string bg, fg, sentence, bigLabel;
        string? helperLabel;
        string? body = null, note = null;   // 主ボタンの下の本文と注記
        bool bigEnabled;
        var hardFix0 = ui.FixSuggestions.Any(s => s.DeltaHard < 0);
        var relax0 = !ui.Running && ui.HasResult && ui.BestHard > 0 ? _vm.RelaxTrialFor() : null;
        var hp = CellSheetLogic.HomePlanOf(ui.Running, ui.HasResult, ui.BestHard == 0L, infeasible, shortfalls.Any(s => s.WishPinned.Count > 0), guidedFix is not null,
            hardFix0, ui.FixFocusName.Length == 0, ui.FixSearching, ui.FixSearched, relax0 is not null, ui.StalledHardFamilies.Count > 0, !cands.IsEmpty,
            ui.SaveState == SaveState.Failed, ui.Consults.Count, draft: ui.DraftStage && ui.EngineRan);
        var kind = hp.Kind;
        var shortage = guidedFix is null ? null : CellSheetLogic.ShortageHeadlineOf(guidedFix);
        var nextTarget = CellSheetLogic.CreationNextTarget(kind,
            shortage: guidedFix is null ? null : $"{guidedFix.DayLabel} {guidedFix.ShiftSymbol}",
            move: firstMove is null ? null : $"{StaffName(firstMove.Staff)} {DayText.Short(ui.StartDate, firstMove.Day)}",
            wish: firstWish is null ? null : $"{StaffName(firstWish.Staff)} {DayText.Short(ui.StartDate, firstWish.Day)}",
            relax: relax0 is null ? null : NextActionGuide.RelaxTargetOf(relax0.Result, ui, AnalysisView.LabelOf) is { } rt ? $"{rt.Name} {rt.Span}" : null,
            tour: tourNext);
        var wishLabel = AnalysisTriage.HomeTargetLabel("ぶつかっている希望を見る", nextTarget, null);
        var hardNow = AnalysisTriage.HardText(ui.Breakdown, ui.BestHard);
        if (kind == HomePlanKind.Running)
        {
            bg = "MagiPrimaryContainerBrush"; fg = "MagiOnPrimaryContainerBrush";
            sentence = ""; bigLabel = ""; bigEnabled = false; helperLabel = null;
            _bigAction = () => { }; _helperAction = () => { };
        }
        else if (kind == HomePlanKind.Make)
        {
            bg = "MagiPrimaryContainerBrush"; fg = "MagiOnPrimaryContainerBrush";
            sentence = "";
            bigLabel = "勤務表をつくる"; bigEnabled = true;
            helperLabel = "下書きをつくる（希望と期間の制約を先に埋める）";
            _bigAction = _vm.RunV6FullOptimize; _helperAction = () => _vm.GenerateSmartInitial();
        }
        else if (kind == HomePlanKind.Draft)
        {
            bg = "MagiPrimaryContainerBrush"; fg = "MagiOnPrimaryContainerBrush";
            sentence = CellSheetLogic.HomeDraftStatus(hardNow, ui.BestHard);
            bigLabel = "勤務表をつくる"; bigEnabled = true; helperLabel = CellSheetLogic.HomeScheduleHelper;
            _bigAction = _vm.RunV6FullOptimize; _helperAction = () => _window.SelectTab("schedule");
        }
        else if (kind == HomePlanKind.PreDist)
        {
            bg = "MagiTertiaryContainerBrush"; fg = "MagiOnTertiaryContainerBrush";
            sentence = "";
            // [Android 3.509.4/3.510.3 同期] 前後比較（変更人数・セル数・希望充足・個人回数・族別の増減）の行は主ボタンの下。
            body = ui.RunSummary is { } rs ? rs.Line() + "\n" + rs.FamilyLine(k => AnalysisTriage.UnitOf(k), AnalysisView.LabelOf) : null;
            note = ui.ImpossibleWishCount > 0 ? $"担当外の希望が {ui.ImpossibleWishCount} 件あります。" : null;
            bigLabel = CellSheetLogic.HomePreDistButton; bigEnabled = true; helperLabel = CellSheetLogic.HomeScheduleHelper;
            _bigAction = () => _ = ShowPreDistributionAsync(); _helperAction = () => _window.SelectTab("schedule");
        }
        else if (kind == HomePlanKind.WishInfeasible)
        {
            // 重複除去の前で決める（S5a の行に吸収された S5b の人も数える）。例の日も希望で固定された人がいる日から。
            var pinnedDay = shortfalls.FirstOrDefault(s => s.WishPinned.Count > 0)?.DayLabel;
            var pinnedLanding = shortfalls.FirstOrDefault(s => s.WishPinned.Count > 0) is { } pinned ? GuidedFixRules.LandingFor(pinned) : null;
            bg = "MagiErrorContainerBrush"; fg = "MagiOnErrorContainerBrush";
            sentence = "いまの希望のままでは、ここは埋められません。" + (pinnedDay is null ? "" : $"（例：{pinnedDay}）");
            bigLabel = wishLabel; bigEnabled = true; helperLabel = "データを見直す";
            _bigAction = () => _ = ShowWishConflictsAsync(); _helperAction = () => GoEditLandingFrom(pinnedLanding, new EditReturn("なおし方", EditReturn.Guided));
        }
        else if (kind == HomePlanKind.DataInfeasible)
        {
            bg = "MagiErrorContainerBrush"; fg = "MagiOnErrorContainerBrush";
            sentence = "このデータでは、ここは埋められません。" + (worstDay is null ? "" : $"（例：{worstDay}）");
            bigLabel = "データを見直す"; bigEnabled = true; helperLabel = "未充足のまま書き出す";
            var infeasibleLanding = shortfalls.FirstOrDefault(s => s.Verdict == CoverageVerdict.Infeasible) is { } inf ? GuidedFixRules.LandingFor(inf) : null;
            _bigAction = () => GoEditLandingFrom(infeasibleLanding, new EditReturn("なおし方", EditReturn.Guided)); _helperAction = () => _ = ShowPreDistributionAsync();
        }
        else
        {
            // [Android 3.612.0 思考誘導S0/S4] 未完成は「足りる？→必須を減らす1手ある？→下限の見込み？→希望が関わる？」の順に主ボタンを1つだけ出す。
            bg = "MagiWarnContainerBrush"; fg = "MagiOnWarnContainerBrush";
            helperLabel = null; _helperAction = () => { };
            var remain = $"必須違反が {hardNow} 残っています。";
            if (kind == HomePlanKind.GuidedFix && shortage is not null)
            {
                sentence = shortage.Headline;
                bigLabel = AnalysisTriage.HomeTargetLabel("なおし方を見る", nextTarget, null); bigEnabled = true;
                _bigAction = () => _ = ShowGuidedFixAsync();
            }
            else if (kind == HomePlanKind.ApplyMove)
            {
                // [3.643.0] 旧「AIの解決提案」をここへ統合: 手の内容（本文）・効果（注記）。補助は確認の画面を開くだけ。
                var top = ui.FixSuggestions.First(fs => fs.DeltaHard < 0);
                var (hardLine, caution) = NextActionGuide.FixImpactLines(top, AnalysisView.LabelOf);
                // 必須の変化（族ごと）と増える要調整だけ（件・回・人・pt を 1 つの合計に足さない＝統合仕様 INV-07）。
                sentence = CellSheetLogic.HomeFixHeadline(hardNow, CellSheetLogic.TopFixResolvedLabels(ui));
                body = FixOutcomeText.FixDisplayLabel(top.Label);
                note = (CellSheetLogic.HardChangeLine(top.Diff, AnalysisView.LabelOf) ?? hardLine) + (caution is null ? "" : "\n" + caution);
                bigLabel = AnalysisTriage.HomeTargetLabel("直す1手を見る", nextTarget, null); bigEnabled = true;
                _bigAction = () => _window.SelectTab("analysis");
                helperLabel = CellSheetLogic.ReviewChangeButton; _helperAction = () => _vm.PreviewFixSuggestion(top, globalPolish: true);   // 2 セル以上は当てる前に一覧（3.646.0）
            }
            else if (kind == HomePlanKind.Searching)
            {
                sentence = remain + "直し方を探しています…";
                bigLabel = ""; bigEnabled = false;
                _bigAction = () => { };
            }
            else if (kind == HomePlanKind.Relax && relax0 is { } relax)
            {
                // [S6 §2.1] 必須違反の一部が利用者自身の設定（上限 0）で塞がれているときだけ、希望の段より先に出す。
                var t = NextActionGuide.RelaxCardTextOf(relax.Result, ui, AnalysisView.LabelOf);
                sentence = t.Headline; body = t.Body; note = t.Note;
                bigLabel = "緩める候補を見る"; bigEnabled = true;
                _bigAction = () => _ = ShowRelaxTrialAsync();
                if (!cands.IsEmpty) { helperLabel = "ぶつかっている希望を見る"; _helperAction = () => _ = ShowWishConflictsAsync(); }
            }
            else if (kind == HomePlanKind.WishFloor)
            {
                // S6 の判定が済むまでは「下限」と言わない（設定を緩めれば減るかもしれない）。
                sentence = ui.RelaxSearching ? remain : $"今の希望とルールでは、必須違反 {hardNow} からこれ以上は減らせない見込みです。";
                bigLabel = wishLabel; bigEnabled = true;
                _bigAction = () => _ = ShowWishConflictsAsync();
                helperLabel = "このまま書き出す"; _helperAction = () => _ = ShowPreDistributionAsync();
            }
            else if (kind == HomePlanKind.Wishes)
            {
                sentence = remain + "希望とルールがぶつかっています。";
                bigLabel = wishLabel; bigEnabled = true;
                _bigAction = () => _ = ShowWishConflictsAsync();
            }
            else
            {
                sentence = remain;
                bigLabel = AnalysisTriage.HomeTargetLabel(CellSheetLogic.HomeListButton, nextTarget, null); bigEnabled = true;
                _bigAction = () => _window.SelectTab("analysis");
            }
        }

        NextActionCard.Background = BrushOf(bg);
        var fgBrush = BrushOf(fg);
        var sections = CellSheetLogic.HomeCardSectionsOf(hp, ui);
        bool Has(HomeSection sec) => sections.Contains(sec);
        static Visibility Vis(bool v) => v ? Visibility.Visible : Visibility.Collapsed;
        var savedLine = ui.Running ? null : _vm.CsvSavedLine();
        // [3.645.0/仕様 5.3] 相談中の件数（未確認事項）。完成の見出しは変えず、書き出しも止めない。
        var consultLine = ui.Running ? null : ConsultList.Line(ui.Consults);
        var saveFailed = ui.SaveState == SaveState.Failed;
        var statusLines = CellSheetLogic.HomeStatusLines(kind, sentence, consultLine, savedLine, saveFailed);

        // CREATION: 作成の完了（見出しと同じ対象なら完了の 1 行だけ）。保存の失敗は状態の行へ（SF-1）。
        CreationHost.Children.Clear();
        CreationHost.Visibility = Vis(Has(HomeSection.Creation));
        if (Has(HomeSection.Creation))
        {
            foreach (var l in CellSheetLogic.CreationTopLines(nextTarget, ui.KeptPrevious, headlineTarget: nextTarget))
                CreationHost.Children.Add(new TextBlock { Text = l, TextWrapping = TextWrapping.Wrap, Foreground = fgBrush, Style = StyleOf("MagiBodyMediumTextStyle") });
        }

        HeadlineText.Visibility = Vis(Has(HomeSection.Headline));
        HeadlineText.Text = CellSheetLogic.HomeHeadline(kind, ui.StartDate);
        HeadlineText.Foreground = fgBrush;

        // BADGES: 札は見出しと同じ語で検査の結果だけを言う（「完成」とは言わない＝人の確認と分ける）。件と人は足さない（INV-07）。
        BadgeHost.Children.Clear();
        BadgeHost.Visibility = Vis(Has(HomeSection.Badges));
        if (Has(HomeSection.Badges))
        {
            var badgeColor = ColorHex.Parse(MagiAccent.Orange, Colors.Gray);
            foreach (var b in CellSheetLogic.HomeBadges(hardNow, shortfalls.FirstOrDefault(s => s.Miss > 0)))
            {
                BadgeHost.Children.Add(new Border
                {
                    Background = new SolidColorBrush(badgeColor), CornerRadius = (CornerRadius)Application.Current.Resources["MagiCornerLG"], Padding = new Thickness(10, 3, 10, 3),
                    Child = new TextBlock { Text = b, Style = StyleOf("MagiLabelMediumTextStyle"), FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(ReadableOn(badgeColor)) },
                });
            }
        }

        // STATUS: 2 行まで。先頭は必須ありの段だけ大きく。作成直後の自動探索はカードの中の文字ボタンで止める（下部バーは外枠のまま。3.402.0 の止められる性質は保つ）。
        StatusHost.Children.Clear();
        StatusHost.Visibility = Vis(Has(HomeSection.Status) && (statusLines.Count > 0 || kind == HomePlanKind.Searching));
        if (Has(HomeSection.Status))
        {
            // 段の 1 文は大きく、保存の失敗の行（段に関係なく先頭）は本文の太字。
            var lead = -1;
            for (var i = 0; i < statusLines.Count; i++) if (statusLines[i] != CellSheetLogic.HomeSaveFailedLine) { lead = i; break; }
            for (var i = 0; i < statusLines.Count; i++)
            {
                var line = new TextBlock { Text = statusLines[i], TextWrapping = TextWrapping.Wrap, Foreground = fgBrush, Style = StyleOf(i == lead && kind != HomePlanKind.PreDist ? "MagiTitleMediumTextStyle" : "MagiBodyMediumTextStyle") };
                if (statusLines[i] == CellSheetLogic.HomeSaveFailedLine) line.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
                StatusHost.Children.Add(line);
            }
            if (saveFailed)
            {
                var retry = new Button { Content = CellSheetLogic.SaveRetryButton, MinHeight = 48, Foreground = fgBrush, BorderBrush = fgBrush, BorderThickness = new Thickness(1), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
                retry.Click += (_, _) => _vm.RetrySave();
                StatusHost.Children.Add(retry);
            }
            if (kind == HomePlanKind.Searching)
            {
                var stop = new HyperlinkButton { Content = "やめる", MinHeight = 48, Foreground = fgBrush };
                stop.Click += (_, _) => _vm.Stop();
                StatusHost.Children.Add(stop);
            }
        }

        // PROGRESS: 何をしているかの語を先頭に。ProgressSummary（改善率/残り時間/探索数）はそのまま。
        ProgressRow.Visibility = Vis(Has(HomeSection.Progress));
        ProgressSpinner.IsActive = ui.Running;
        ProgressSpinner.Foreground = fgBrush;
        ProgressText.Text = ui.Running ? $"勤務表を組んでいます ・ {CellSheetLogic.ProgressSummary(ui)}" : "";
        ProgressText.Foreground = fgBrush;

        BigButton.Visibility = Vis(Has(HomeSection.Primary) && bigEnabled);
        BigButton.Content = bigLabel;
        BigButton.IsEnabled = editable;
        // HELPER: 段に補助が無ければ「表で全体を見る」。
        HelperButton.Visibility = Vis(Has(HomeSection.Helper));
        HelperButton.Content = helperLabel ?? CellSheetLogic.HomeScheduleHelper;
        if (helperLabel is null) _helperAction = () => _window.SelectTab("schedule");
        HelperButton.IsEnabled = editable;
        OthersButton.Visibility = Vis(Has(HomeSection.Others));
        OthersButton.Content = CellSheetLogic.HomeOthersButton;
        OthersButton.Foreground = fgBrush;

        BodyText.Visibility = Vis(body is not null);
        BodyText.Text = body ?? "";
        BodyText.Foreground = fgBrush;
        NoteText.Visibility = Vis(note is not null);
        NoteText.Text = note ?? "";
        NoteText.Foreground = fgBrush;
        // [S5 §9] 直近の「希望を外して再作成」の結果（VM が鮮度を照合済み）。
        var outcomeLine = ui.Running ? null : _vm.FixOutcomeLine(AnalysisView.LabelOf) ?? _vm.WishCancelOutcomeLine() ?? _vm.RelaxDoneLine();
        OutcomeText.Visibility = Vis(outcomeLine is not null);
        OutcomeText.Text = outcomeLine ?? "";
        OutcomeText.Foreground = fgBrush;
        // 勤務表 CSV の保存状態は直した結果の行とは別に出す（3.646.0 B03）。状態の行に出したときは繰り返さない。
        var showSaved = savedLine is not null && !statusLines.Contains(savedLine);
        SavedText.Visibility = Vis(showSaved);
        SavedText.Text = savedLine ?? "";
        SavedText.Foreground = fgBrush;
        // [3.643.0] 探索がどう終わったか（停滞で早く終えた理由・残る必須の性質）。内部名は出さない（StopExplanation）。次の一手は主ボタンが言う。
        var stopLine = ui.HasResult && !ui.Running && ui.StopSummary is { } stopSum && StopExplanation.Of(stopSum, ui.Breakdown) is { } se ? se.Line : null;
        StopText.Visibility = Vis(stopLine is not null);
        StopText.Text = stopLine ?? "";
        StopText.Foreground = fgBrush;
        var relaxFailed = !ui.RelaxSearching && ui.BestHard > 0 && _vm.RelaxFailed();
        var relaxStopped = relaxFailed || (!ui.RelaxSearching && ui.BestHard > 0 && _vm.RelaxStopped());
        RelaxSearchRow.Visibility = Vis(!ui.Running && (ui.RelaxSearching || relaxStopped));
        RelaxSearchText.Text = relaxFailed ? NextActionGuide.RelaxFailedText : relaxStopped ? NextActionGuide.RelaxStoppedText : NextActionGuide.RelaxSearchingText;
        RelaxStopButton.Content = relaxStopped ? NextActionGuide.RelaxRetryLabel : "やめる";
        RelaxSearchText.Foreground = fgBrush;
        DetailHost.Visibility = Vis(Has(HomeSection.Detail) && (body is not null || note is not null || outcomeLine is not null || showSaved || stopLine is not null || RelaxSearchRow.Visibility == Visibility.Visible));

        // SATISFACTION: [3.480.0] 文言（正直さ）は変えず「でき具合：N%（残りM件）」＋バー。[3.483.0 H-2] 必須0のときは残りの単位を調整件数へ。
        SatisfactionHost.Visibility = Vis(Has(HomeSection.Satisfaction));
        ResolveText.Text = $"でき具合：{ui.Satisfaction}%（{AnalysisTriage.HomeRemainingLabel(ui.BestHard, shortDays, ui.Breakdown, ui.HardWishConflict)}）";
        ResolveText.Foreground = fgBrush;
        ResolveBar.Value = System.Math.Clamp(ui.Satisfaction, 0, 100);
        ResolveBar.Foreground = fgBrush;
        SoftNoteText.Visibility = Vis(ui.BestHard > 0L);
        SoftNoteText.Text = CellSheetLogic.HomeSoftNote;
        SoftNoteText.Foreground = fgBrush;

        // INFO: 数字の根拠と結果採用の意味を 1 文字も変えず折りたたみへ。
        InfoHost.Visibility = Vis(Has(HomeSection.Info));
        DetailToggle.Content = _detailOpen ? "ⓘ 詳しい説明を閉じる" : "ⓘ でき具合の意味";
        DetailToggle.Foreground = fgBrush;
        DetailText.Visibility = Vis(_detailOpen);
        DetailText.Foreground = fgBrush;

        RedraftLink.Visibility = Vis(Has(HomeSection.DraftLink));
        RedraftLink.Foreground = fgBrush;
        RedraftLink.IsEnabled = editable;
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

    private void OnOthersClick(object sender, RoutedEventArgs e) => _window.SelectTab("analysis");

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

    /// <summary>[思考誘導S3→S5] 必須違反に関わる希望と、人員不足の日に別の勤務の希望がある人を並べる（Kotlin <c>WishConflictDialog</c>）。行を押すとセル、「希望を外したら？」で試算・確定（§5）。
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
            var b = new Button { Content = "希望を外したら？", MinHeight = 44, Margin = new Thickness(4, 0, 0, 0), IsEnabled = enabled };
            b.Click += (_, _) => _vm.StartWishTrial(row.Staff, row.Day);
            return b;
        }
        void AddRow(WishTrialRow row, UiState ui)
        {
            var label = new TextBlock { Text = $"{row.Name} ・ {DayText.Short(ui.StartDate, row.Day)}　{row.Reason}", TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            // [3.696.0] 札を右に並べると名前の先頭が欠けた（Android 端末報告）＝文は全幅、札はその下。
            var content = new StackPanel { Spacing = 4 };
            content.Children.Add(label);
            if (row.WishOnly) content.Children.Add(AnalysisView.TagChip(NextActionGuide.WishOnlyTag, MagiAccent.Orange));
            var open = new Button { Content = content, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, MinHeight = 44 };
            open.Click += (_, _) => { dialog.Hide(); _window.OpenCell(row.Staff, row.Day); };
            panel.Children.Add(open);
            // [3.645.0/仕様 5.3] 本人や上長に確認してから決める＝対象と検討内容を相談中の一覧へ（取消も勤務の変更もしない）。
            var consultItem = ConsultList.Wish(row.Name, DayText.Short(ui.StartDate, row.Day),
                ui.Wishes.TryGetValue($"{row.Staff},{row.Day}", out var wk) && wk >= 0 && wk < ui.ShiftSymbols.Count ? ui.ShiftSymbols[wk] : null, row.Reason, row.Staff, row.Day,
                ConsultList.IsoDate(ui.StartDate, row.Day));
            panel.Children.Add(ConsultButton(ui, consultItem));
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
                            Content = "希望を外して再作成（元に戻せます）", MinHeight = 44, Margin = new Thickness(4, 0, 0, 0),
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
                panel.Children.Add(new TextBlock { Text = "この希望とルールがぶつかっています。1件ずつ開いて、本人と相談して希望を調整するか、勤務を決めてください。", TextWrapping = TextWrapping.Wrap, Opacity = 0.85 });
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
    internal async System.Threading.Tasks.Task ShowGuidedFixAsync()
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
        var loggedFor = (-1, -1);   // [3.697.0] 「開く」の行を出した枠（Kotlin LaunchedEffect(day, shift)）
        var wentOn = false;   // 候補か着地を押して閉じた＝「閉じる（適用なし）」ではない
        // 原因に対応する設定へ行く。押したらダイアログを閉じてから着地する（null＝月次条件の先頭）。編集タブの先頭に「元の確認へ戻る」を出す（3.646.0 L03）。
        Button LandingButton(EditLanding? landing)
        {
            // 設定へ行くのがこの段の主操作＝塗り（H-5）。
            var b = new Button { Content = GuidedFixRules.LandingButtonLabel(landing), HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 48,
                Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
            b.Click += (_, _) => { wentOn = true; dialog.Hide(); GoEditLandingFrom(landing, new EditReturn("なおし方（人員不足）", EditReturn.Guided)); };
            return b;
        }
        void Rebuild()
        {
            panel.Children.Clear();
            var ui = _vm.Ui;
            var plan = GuidedFixPlan.Build(ui.CoverageDiag);
            var target = plan.Target;
            IReadOnlyList<FixCandidate> cands = target is null ? Array.Empty<FixCandidate>() : _vm.ShortageFixCandidates(target.DayIndex, target.ShiftIndex);
            dialog.Title = plan.TitleFor(cands.Count);

            if (target is not null)
            {
                foreach (var point in FixOutcomeText.GuidedStatusPoints(target.Need, target.Got, cands.Count))
                    panel.Children.Add(new TextBlock { Text = "・" + point, FontSize = 14, TextWrapping = TextWrapping.Wrap });
                if (loggedFor != (target.DayIndex, target.ShiftIndex)) { loggedFor = (target.DayIndex, target.ShiftIndex); _vm.LogUi(new OpLogText.UiOp.GuidedOpen(target.DayLabel, target.ShiftSymbol, cands.Count)); }
                if (cands.Count > 0)
                {
                    // [UX監査 中5] 件数を出し、既定の8人を超えたら「すべて表示」で9人目以降も選べる。
                    var showAll = showAllFor == (target.DayIndex, target.ShiftIndex);
                    var shown = showAll ? cands.ToList() : cands.Take(GuidedFixRules.GuidedFixPreview).ToList();
                    foreach (var c in shown)
                    {
                        var fromK = c.StaffIndex < ui.Schedule.Count && target.DayIndex < ui.Schedule[c.StaffIndex].Count && ui.Schedule[c.StaffIndex][target.DayIndex] >= 0
                            ? ui.Schedule[c.StaffIndex][target.DayIndex] : (int?)null;
                        var fromSym = fromK is { } fk && fk < ui.ShiftSymbols.Count ? ui.ShiftSymbols[fk] : null;
                        var b = new Button
                        {
                            Content = new TextBlock { Text = FixOutcomeText.GuidedCandidateLabel(c.Name, target.ShiftSymbol, fromSym), TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextAlignment = TextAlignment.Center },
                            HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 48,
                            IsEnabled = !ui.Running,
                        };
                        var i = c.StaffIndex; var day = target.DayIndex; var shift = target.ShiftIndex; var label = $"{c.Name}さんを{target.DayLabel}の「{target.ShiftSymbol}」に入れる";
                        // 押すと確認の画面へ（直接は書かない）。当てるときは指紋照合・FixApplyGate・INV-03 を通る。
                        b.Click += (_, _) =>
                        {
                            if (_vm.EditBlockedNow()) return;
                            wentOn = true;
                            dialog.Hide();
                            _vm.NoteGuidedFix(day, shift, target.DayLabel, target.ShiftSymbol);
                            _vm.PreviewShortageCandidate(i, day, shift, label);
                        };
                        panel.Children.Add(b);
                        var impact = FixOutcomeText.GuidedCandidateImpact(fromSym,
                            fromK is { } gk2 ? ui.Schedule[c.StaffIndex].Count(v => v == gk2) : 0, fromK is { } gk3 ? _vm.StaffCellLimits(c.StaffIndex, gk3).Lo : null);
                        if (impact is not null) panel.Children.Add(new TextBlock { Text = impact, FontSize = 13, Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
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
                        Text = "押すと、当てる前に変更と影響を確認できます。入れたあとも「元に戻す」で取り消せます。",
                        FontSize = 14, Opacity = 0.8, TextWrapping = TextWrapping.Wrap,
                    });
                    // [3.645.0/仕様 5.3] だれを入れるかを相談してから決める＝対象と候補を相談中の一覧へ。
                    var consultItem = ConsultList.Shortage(target.DayLabel, target.ShiftSymbol, cands.Select(c => c.Name).ToList());
                    panel.Children.Add(ConsultButton(ui, consultItem, stretch: true));
                }
                else if (target.ChainVerified)
                {
                    // [UX監査 高1] 1人を動かすだけでは埋まらないが、複数人の入れ替えで埋まると分析が確かめた枠。
                    //   [3.644.0/UX-03] 当てる前に一覧（だれの・どの日の・何→何と必須の増減）を見せる＝MainWindow.ShowChainPreviewAsync。
                    panel.Children.Add(new TextBlock { Text = "1人を動かすだけでは埋まりません。複数人の入れ替えで埋められます。", FontSize = 14, Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
                    var chain = new Button { Content = "入れ替えの一覧と影響を見る", HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 44 };
                    chain.Click += (_, _) =>
                    {
                        dialog.Hide();
                        _vm.PrepareShortageChainFix(target.DayIndex, target.ShiftIndex, $"（玉突き）{target.DayLabel} の「{target.ShiftSymbol}」を複数人の入れ替えで埋める");
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
                panel.Children.Add(new TextBlock { Text = $"{plan.Infeasible.Select(sf => sf.DayIndex).Distinct().Count()}日 は、これ以上は自動で埋められません。", TextWrapping = TextWrapping.Wrap, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                foreach (var sf in plan.Infeasible.Take(4))
                    panel.Children.Add(new TextBlock { Text = $"・{sf.DayLabel}「{sf.ShiftSymbol}」：{sf.Reason}", FontSize = 14, Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
                panel.Children.Add(new TextBlock { Text = "担当できる職員（担当できるシフト）や希望を見直すと直せます。", FontSize = 14, Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
                panel.Children.Add(LandingButton(GuidedFixRules.LandingFor(plan.Infeasible[0])));
            }
            else if (plan.Blocked.Count > 0)
            {
                panel.Children.Add(new TextBlock { Text = $"いまの希望・担当のままでは埋められない日が {plan.Blocked.Select(sf => sf.DayIndex).Distinct().Count()}日 残っています。", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                foreach (var sf in plan.Blocked.Take(4))
                    panel.Children.Add(new TextBlock { Text = $"・{sf.DayLabel}「{sf.ShiftSymbol}」：{sf.Reason}", FontSize = 14, Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
                panel.Children.Add(new TextBlock { Text = "再作成しても、この日は同じ結果になります。希望を1件調整する（編集タブ＞月次条件）か、担当できるシフトを増やしてください（編集タブ＞年間マスター）。", FontSize = 14, Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
                panel.Children.Add(LandingButton(plan.Blocked.Select(sf => GuidedFixRules.LandingFor(sf)).FirstOrDefault(l => l is not null)));
            }
            else
            {
                panel.Children.Add(new TextBlock { Text = "人員不足の日はなくなりました。", TextWrapping = TextWrapping.Wrap });
                if (FixOutcomeText.OtherHardAfterShortage(ui.Breakdown) is { } otherHard)
                {
                    panel.Children.Add(new TextBlock { Text = otherHard, TextWrapping = TextWrapping.Wrap });
                    // ほかの必須が残るなら巡回の先頭を開く（H-5、Kotlin onTour＝goTourItem(0)）。
                    var tour = _vm.HardViolationItemsFor(AnalysisView.LabelOf);
                    if (tour.Count > 0)
                    {
                        var tourButton = new Button
                        {
                            Content = FixOutcomeText.GuidedTourButton(tour.Count), HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 48,
                            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
                        };
                        var first = tour[0].Cell;
                        tourButton.Click += (_, _) => { wentOn = true; dialog.Hide(); _window.OpenCellSheet(first.I, first.J); };
                        panel.Children.Add(tourButton);
                    }
                }
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
        finally { flow.Close(); _vm.Ui.PropertyChanged -= OnChanged; if (!wentOn) _vm.LogUi(new OpLogText.UiOp.GuidedClose()); }
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
        SoftPolishLinkText.Text = CellSheetLogic.SoftPolishLink;
    }

    private void OnGoEditClick(object sender, RoutedEventArgs e) => _window.GoEditLanding(null);

    /// <summary>[UX監査 中4] 「データを見直す」の着地。原因が分かるときは対応する入口を開き（0=月次条件／2=年間マスター）、分からない（null）ときは月次条件の先頭。
    /// 呼出元 <paramref name="r"/> を渡すと編集タブの先頭に「元の確認へ戻る」が出る（3.646.0 L03）。</summary>
    private void GoEditLandingFrom(EditLanding? landing, EditReturn? r) => _window.GoEditLandingFrom(landing, r);

    /// <summary>「相談してから決める」。積んだあとは「相談中」の札＝結果を形で返す（Kotlin <c>ConsultButton</c>。ダイアログは Ui の変化で組み直るので状態が追従する。
    /// 無効ボタンの文字は状態色の基準 3:1 を割るので札にする＝ux_test_checklist A1、3.645.2）。
    /// 隣の［保留する］は相談に回さず未解決のまま積む（札は「保留中」）。</summary>
    private FrameworkElement ConsultButton(UiState ui, ConsultItem item, bool stretch = false)
    {
        if (ConsultList.Match(ui.Consults, item) is { } m) return ConsultedChip(m);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 0, 0) };
        if (stretch) row.HorizontalAlignment = HorizontalAlignment.Stretch;
        var b = new HyperlinkButton { Content = ConsultList.Button, MinHeight = 44 };
        b.Click += (_, _) => _vm.AddConsult(item);
        var h = new HyperlinkButton { Content = ConsultList.HoldButton, MinHeight = 44 };
        h.Click += (_, _) => _vm.AddConsult(item with { Held = true });
        row.Children.Add(b);
        row.Children.Add(h);
        return row;
    }

    /// <summary>[3.696.0] 「相談中／保留中」の札と、その隣の「取り消す」（積んだ直後の押し間違いを戻せる。Kotlin <c>ConsultButton(onUndo)</c>）。</summary>
    internal FrameworkElement ConsultedChip(ConsultItem m)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var chip = AnalysisView.TagChip(m.Held ? ConsultList.HeldLabel : ConsultList.Done, MagiAccent.Orange);
        chip.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(chip);
        var undo = new HyperlinkButton { Content = ConsultList.UndoButton, MinHeight = 44 };
        undo.Click += (_, _) => _vm.CancelConsult(m);
        row.Children.Add(undo);
        return row;
    }

    // 希望の編集は月次条件＝編集タブの今の入口に任せない。
    private void OnEditWishesClick(object sender, RoutedEventArgs e) => _window.OpenEditDoor(0);

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
            ? $"不足 {diag.TotalShortfall} 人はすべて、担当できる人が足りません。今のデータでは満たせません。" +
                "担当できる職員を確認すると直せます（編集タブ＞年間マスター①）。"
            : diag.AllBlockedNow && diag.SearchMissSlots == 0
                ? $"不足 {diag.TotalShortfall} 人は、いまの希望・担当のままでは埋められません。" +
                    "希望を1件調整する（編集タブ＞月次条件）か、担当を追加してください（編集タブ＞年間マスター①）。"
            : diag.AllBlockedNow
                ? $"不足 {diag.TotalShortfall} 人は、試した入れ替えでは埋まりませんでした（埋められないと決まったわけではありません）。" +
                    "再作成するか、希望を1件調整する（編集タブ＞月次条件）か、担当を追加すると埋まりやすくなります（編集タブ＞年間マスター①）。"
                : diag.ProvenBlockedSlots > 0
                    ? $"不足 {diag.TotalShortfall} 人 — うち {diag.ProvenBlockedSlots} 枠は" +
                        "いまの希望のままでは埋められません。その枠は希望を1件調整する（編集タブ＞月次条件）か、担当を追加すると直せます（編集タブ＞年間マスター①）。" +
                        "残りは再作成すると解消する場合があります。"
                : diag.SearchMissSlots > 0
                    ? $"不足 {diag.TotalShortfall} 人 — うち {diag.SearchMissSlots} 枠は試した入れ替えでは埋まりませんでした。" +
                        "再作成すると解消する場合があります。"
                    : diag.InfeasibleSlots == 0
                        ? $"不足 {diag.TotalShortfall} 人。{CellSheetLogic.ShortageEnoughText}"
                        : $"不足 {diag.TotalShortfall} 人 — {CellSheetLogic.ShortageLabelInfeasible} {diag.InfeasibleSlots} 枠 / {CellSheetLogic.ShortageLabelFixable} {diag.FixableSlots} 枠。" +
                            "動かせば埋まる枠は再作成すると解消する場合があります。担当できる人が足りない枠は担当できる職員を確認すると直せます（編集タブ＞年間マスター①）。";
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
                Text = infeasible ? CellSheetLogic.ShortageLabelInfeasible : s.BlockedNow && s.SearchMiss ? CellSheetLogic.ShortageLabelSearchMiss : s.BlockedNow ? CellSheetLogic.ShortageLabelBlocked : CellSheetLogic.ShortageLabelFixable,
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
        SurplusHeadline.Text = $"過剰 {diag.TotalSurplus} 人 — 勤務している人を他のシフトへ動かせば消えるはずの過剰について、動かない理由を枠ごとに示します。";
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
                    Content = $"{name}さんの希望（{s.ShiftSymbol}）を外して調整する",
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
        var allWorse = CellSheetLogic.AlternativesAllWorse(ui.AlternativesWorse, ui.Alternatives.Count);
        AlternativesWorseText.Text = allWorse ? CellSheetLogic.AlternativesAllWorseText : "";
        AlternativesWorseText.Visibility = allWorse ? Visibility.Visible : Visibility.Collapsed;
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
            apply.Click += async (_, _) =>
            {
                // [Android 3.683.0] 案は反映する前に確かめる（今より悪い案はそう添える）。
                var worse = idx < _vm.Ui.AlternativesWorse.Count && _vm.Ui.AlternativesWorse[idx];
                var text = (idx < _vm.Ui.Alternatives.Count ? _vm.Ui.Alternatives[idx] : "") + (worse ? "\n" + CellSheetLogic.AlternativesAllWorseText + "（元に戻すで取り消せます）" : "");
                var dialog = new ContentDialog
                {
                    XamlRoot = XamlRoot, Title = $"案{idx + 1}を反映しますか？", Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
                    PrimaryButtonText = "反映する", CloseButtonText = "反映しない", DefaultButton = ContentDialogButton.Close,
                };
                if (await dialog.ShowAsync() == ContentDialogResult.Primary) _vm.ApplyAlternative(idx);
            };
            row.Children.Add(apply);
            AlternativesListHost.Children.Add(row);
        }
    }

    /// <summary>配布前の確認（仕様 §10、Kotlin <c>PreDistributionDialog</c>）。配るかどうかは利用者が決める。</summary>
    internal async System.Threading.Tasks.Task ShowPreDistributionAsync()
    {
        var rows = PreDistributionCheck.Rows(_vm.Ui, _vm.CsvSavedLine(), k => AnalysisView.BreakdownLabels.TryGetValue(k, out var jp) ? jp : k);
        var panel = new StackPanel { Spacing = 8 };
        foreach (var r in rows)
            panel.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Text = $"{r.Label}：" + (r.Ok == true ? "✓ " : r.Ok == false ? "! " : "") + r.Value });
        panel.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Text = PreDistributionCheck.Note });
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = CellSheetLogic.PreDistTitle, Content = new ScrollViewer { Content = panel },
            PrimaryButtonText = "配ると決めて書き出す", CloseButtonText = "戻る", DefaultButton = ContentDialogButton.Primary,
        };
        _vm.LogUi(new OpLogText.UiOp.PreDistOpen());
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            _vm.LogUi(new OpLogText.UiOp.PreDistExport());
            if (await _window.ExportScheduleCsvAsync() is { } ok && CellSheetLogic.CsvExportReopensPreDist(ok)) await ShowPreDistributionAsync();
        }
        else _vm.LogUi(new OpLogText.UiOp.PreDistBack());
    }
}