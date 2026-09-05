using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using MagiApp.ViewModels;
using MagiEngine.V6;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace MagiApp.WinUI.Views;

/// <summary>
/// [フェーズ8→9] 「勤務表」タブの内容。フェーズ8の縦断スライスで <c>MainWindow</c> に直接書いていた
/// グリッド描画コードを、フェーズ9のマルチタブ化に伴いこの <see cref="UserControl"/> へ切り出した
/// （コードビハインド駆動レンダリングのまま）。
///
/// [セル編集] データセルを <see cref="Button"/> にし、タップで <see cref="MenuFlyout"/> を開いて
/// <see cref="MagiViewModel.AllowedShiftsFor"/>（そのスタッフが担当可能なシフト一覧）から選ばせ、
/// <see cref="MagiViewModel.SetCell"/> で確定する。<c>SetCell</c> 自身が実行中(<c>OptimizeInFlight</c>)を
/// ガードして無言で拒否するため、ここでも <see cref="UiState.Running"/> の間はボタンを無効化して
/// 「押せるのに何も起きない」を避ける（二重の防御——ViewModel 側が最終防御）。
///
/// [違反ハイライト/希望バッジ] Kotlin原本 MagiScheduleViews.kt の色分け/バッジの本格移植ではなく、
/// <see cref="UiState.ViolationCells"/>/<see cref="UiState.Wishes"/> だけで表せる最小版。
/// セル枠の色=<see cref="MirrorKeys.Hard"/>（必須違反=濃い赤）/それ以外（要調整=橙）。
/// 右下の丸=希望シフトの有無（反映済み=緑・未反映=桃、Kotlin原本の「反映済みリング/未反映バッジ」相当）。
///
/// [元に戻す/やり直す] <see cref="MagiViewModel.Undo"/>/<see cref="MagiViewModel.Redo"/> へ配線。
/// <see cref="UiState.CanUndo"/>/<see cref="UiState.CanRedo"/> でボタンの有効/無効を反映する
/// （<c>Undo</c>/<c>Redo</c> 自身も実行中は無言で no-op なので、ここでも二重の防御）。
///
/// [違反箇所へのジャンプ] <see cref="FocusCell"/>（<c>MainWindow.JumpToCell</c> 経由で
/// <c>AnalysisView</c>「違反の場所」から呼ばれる）。指定セルへ <c>StartBringIntoView</c> で
/// スクロールし、約2.5秒だけ強調色の太枠を付けてから自動的に消す（Kotlin原本 <c>focusCell</c> の
/// 最小移植）。
///
/// [まとめて割当] <see cref="OnBulkAssignClick"/>。Kotlin原本 <c>AssignBulkSheet</c>（ドラッグ選択ではなく
/// フィルタ選択で複数セルへ一括代入）の最小移植——対象範囲(期間全体/この曜日)・対象（全職員/職員を選ぶ）・
/// シフトを選ばせ、canDo で担当外を自動除外したセル集合を <see cref="MagiViewModel.SetCells"/> へ渡す。
/// </summary>
public sealed partial class ScheduleView : UserControl
{
    private readonly MagiViewModel _vm;
    private readonly UiSubscription _uiSub;

    /// <summary>[違反箇所へのジャンプ] 分析タブから飛んできた注目セル。<see cref="FocusCell"/> 参照。</summary>
    private (int I, int J)? _focusCell;

    /// <summary>直近の <see cref="RenderSchedule"/> が作った注目セルの要素（無ければ null）。
    /// <see cref="FocusCell"/> が <c>StartBringIntoView</c> を呼ぶために使う。</summary>
    private Border? _focusCellElement;

    private DispatcherTimer? _focusTimer;

    /// <summary>[phase9 #6] 日ヘッダ要素（週送り・違反ジャンプの横スクロール先）と、直近タップしたセル（クロスハイライト）。</summary>
    private readonly List<Border> _dayHeaders = new();
    private Border? _nameHeader;
    private (int I, int J)? _tapped;
    private DispatcherTimer? _tappedTimer;
    private List<List<int>> _weeks = new();
    private List<int> _vioDays = new();
    private int _navIdx = -1;

    /// <summary>[phase9 #7 E7] 表示中のバケツ（初期は全 ON）と集中モード。表示のみ・スコアリング不変。</summary>
    private readonly HashSet<string> _vioEnabled = new(VioBuckets.AllKeys);
    private bool _focusMode;

    public ScheduleView(MagiViewModel vm)
    {
        _vm = vm;
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

    /// <summary>
    /// [違反箇所へのジャンプ] 分析タブの「違反の場所」からの遷移先。Kotlin原本の
    /// <c>focusCell</c>（約2.5秒だけ枠でハイライトして自動的に消える）の最小移植——
    /// この移植では枠色を一時的に強調色へ差し替え、タイマー満了で再描画して元に戻す
    /// （<see cref="Render"/> が毎回グリッドを作り直す設計のため、アニメーションではなく
    /// 「ハイライトを付けて描く／付けずに描き直す」の2状態で表現する）。
    /// </summary>
    public void FocusCell(int i, int j)
    {
        _focusCell = (i, j);
        Render();
        if (i < 0) ScrollToDay(j); else _focusCellElement?.StartBringIntoView();

        _focusTimer?.Stop();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _focusCell = null;
            Render();
        };
        _focusTimer = timer;
        timer.Start();
    }

    /// <summary>[3.444.0 クロスハイライト] タップしたセルの職員名と日付を約2.5秒強調（セルの違反枠は変えない）。</summary>
    private void MarkTapped(int i, int j)
    {
        _tapped = (i, j);
        Render();
        _tappedTimer?.Stop();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _tapped = null;
            Render();
        };
        _tappedTimer = timer;
        timer.Start();
    }

    /// <summary>Kotlin原本 <c>mondayWeeks</c>：月曜始まりで日 index を週ごとに分ける。</summary>
    private static List<List<int>> MondayWeeks(string startDate, int days)
    {
        var sdow = 0;
        if (DateOnly.TryParseExact(startDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var d0))
        {
            sdow = ((int)d0.DayOfWeek + 6) % 7;
        }
        var weeks = new List<List<int>>();
        for (var d = 0; d < days; d++)
        {
            if (weeks.Count == 0 || (sdow + d) % 7 == 0) weeks.Add(new List<int>());
            weeks[^1].Add(d);
        }
        return weeks;
    }

    private List<int> VioDays(UiState ui)
    {
        var days = new SortedSet<int>();
        foreach (var key in ui.ViolationCells.Keys)
        {
            if (VioBuckets.VisibleCellVio(ui, key, _vioEnabled) is not null && int.TryParse(key[(key.IndexOf(',') + 1)..], out var j)) days.Add(j);
        }
        foreach (var (key, cls) in ui.NeedViolations)
        {
            if (VioBuckets.VioVisible(cls, _vioEnabled) && int.TryParse(key[(key.IndexOf(',') + 1)..], out var j)) days.Add(j);
        }
        return days.ToList();
    }

    /// <summary>[phase9 #7] 種別フィルタのバー（Kotlin原本 <c>ViolationBucketChips</c>）。違反ゼロなら隠す。</summary>
    private void RenderFilterBar(UiState ui)
    {
        var counts = VioBuckets.BucketLocCounts(ui);
        var any = ui.Loaded && counts.Values.Any(n => n > 0);
        FilterBar.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        if (!any) return;
        var loc = ui.ViolationCells.Count + ui.NeedViolations.Count + ui.CountViolations.Count;
        FilterTitle.Text = $"違反フィルタ（種別）・要確認 {loc}か所";
        ShowAllButton.Visibility = _vioEnabled.SetEquals(VioBuckets.AllKeys) ? Visibility.Collapsed : Visibility.Visible;
        FocusToggle.IsChecked = _focusMode;
        BucketChips.Children.Clear();
        foreach (var b in VioBuckets.Buckets)
        {
            var n = counts.GetValueOrDefault(b.Key);
            var chip = new ToggleButton
            {
                Content = $"{b.Label} {n}", IsChecked = _vioEnabled.Contains(b.Key), MinHeight = 40,
                Opacity = n == 0 ? 0.5 : 1.0,
            };
            var key = b.Key;
            chip.Click += (_, _) =>
            {
                if (!_vioEnabled.Remove(key)) _vioEnabled.Add(key);
                Render();
            };
            BucketChips.Children.Add(chip);
        }
    }

    private void OnShowAllVioClick(object sender, RoutedEventArgs e)
    {
        _vioEnabled.UnionWith(VioBuckets.AllKeys);
        Render();
    }

    private void OnFocusToggleClick(object sender, RoutedEventArgs e)
    {
        _focusMode = FocusToggle.IsChecked == true;
        Render();
    }

    private double HeaderX(int d) =>
        d >= 0 && d < _dayHeaders.Count
            ? _dayHeaders[d].TransformToVisual(ScheduleGridHost).TransformPoint(new Windows.Foundation.Point(0, 0)).X
            : 0;

    /// <summary>左端に見えている日から現在週を求める（自由スクロールにも追従）。</summary>
    private int CurrentWeek()
    {
        if (_weeks.Count == 0) return 0;
        var left = GridScroll.HorizontalOffset + (_nameHeader?.ActualWidth ?? 0);
        var d = 0;
        for (; d < _dayHeaders.Count; d++) if (HeaderX(d) + _dayHeaders[d].ActualWidth > left + 1) break;
        var w = _weeks.FindIndex(wk => d <= wk[^1]);
        return w < 0 ? _weeks.Count - 1 : w;
    }

    private void ScrollToDay(int d)
    {
        if (d < 0 || d >= _dayHeaders.Count) return;
        var x = HeaderX(d) - (_nameHeader?.ActualWidth ?? 0);
        GridScroll.ChangeView(Math.Max(0, x), null, null);
    }

    /// <summary>[phase9 #6] ナビバー（Kotlin原本 <c>ScheduleNavBar</c>）。週も違反日も無いときは隠す。</summary>
    private void RenderNavBar(UiState ui)
    {
        _weeks = ui.Loaded ? MondayWeeks(ui.StartDate, Math.Max(1, ui.Days)) : new List<List<int>>();
        var vio = ui.Loaded ? VioDays(ui) : new List<int>();
        if (!vio.SequenceEqual(_vioDays)) { _vioDays = vio; _navIdx = -1; }
        var show = _weeks.Count > 1 || _vioDays.Count > 0;
        NavBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;
        var hasWeeks = _weeks.Count > 1;
        PrevWeekButton.Visibility = hasWeeks ? Visibility.Visible : Visibility.Collapsed;
        NextWeekButton.Visibility = hasWeeks ? Visibility.Visible : Visibility.Collapsed;
        var hasVio = _vioDays.Count > 0;
        PrevVioButton.Visibility = hasVio ? Visibility.Visible : Visibility.Collapsed;
        NextVioButton.Visibility = hasVio ? Visibility.Visible : Visibility.Collapsed;
        UpdateNavLabel(ui);
        // グリッドは作り直した直後で幅が未確定なので、レイアウト後にもう一度ラベルを出す。
        DispatcherQueue.TryEnqueue(() => UpdateNavLabel(_vm.Ui));
    }

    private void UpdateNavLabel(UiState ui)
    {
        var cur = CurrentWeek();
        var weekLabel = "";
        if (_weeks.Count > 1 && cur < _weeks.Count && _weeks[cur].Count > 0)
        {
            weekLabel = DateOnly.TryParseExact(ui.StartDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var d0)
                ? $"{d0.AddDays(_weeks[cur][0]).Month}月 第{cur + 1}/{_weeks.Count}週"
                : $"第{cur + 1}/{_weeks.Count}週";
        }
        var vioLabel = _vioDays.Count == 0 ? ""
            : _navIdx < 0 ? $"違反 {_vioDays.Count}/{ui.Days}日"
            : $"違反日 {_navIdx + 1}/{_vioDays.Count}";
        NavLabel.Text = string.Join(" ・ ", new[] { weekLabel, vioLabel }.Where(t => t.Length > 0));
        PrevWeekButton.IsEnabled = cur > 0;
        NextWeekButton.IsEnabled = cur < _weeks.Count - 1;
    }

    private void OnGridScrollViewChanged(object? sender, ScrollViewerViewChangedEventArgs e) => UpdateNavLabel(_vm.Ui);

    private void OnPrevWeekClick(object sender, RoutedEventArgs e)
    {
        var cur = CurrentWeek();
        if (cur > 0) ScrollToDay(_weeks[cur - 1][0]);
    }

    private void OnNextWeekClick(object sender, RoutedEventArgs e)
    {
        var cur = CurrentWeek();
        if (cur < _weeks.Count - 1) ScrollToDay(_weeks[cur + 1][0]);
    }

    private void JumpToVio(int n)
    {
        if (n < 0 || n >= _vioDays.Count) return;
        _navIdx = n;
        FocusCell(-1, _vioDays[n]);
    }

    private void OnPrevVioClick(object sender, RoutedEventArgs e) => JumpToVio(_navIdx <= 0 ? _vioDays.Count - 1 : _navIdx - 1);

    private void OnNextVioClick(object sender, RoutedEventArgs e) => JumpToVio(_navIdx < 0 ? 0 : (_navIdx + 1) % _vioDays.Count);

    private void OnUndoClick(object sender, RoutedEventArgs e) => _vm.Undo();
    private void OnRedoClick(object sender, RoutedEventArgs e) => _vm.Redo();

    /// <summary>ListView（複数選択）の表示用ラッパー。氏名の重複があっても index で確実に職員を特定するため
    /// 文字列そのものではなくこれを ItemsSource に流す（<see cref="ListView.SelectedItems"/> から拾い戻す）。</summary>
    private sealed record StaffPickItem(int Index, string Name)
    {
        public override string ToString() => Name;
    }

    /// <summary>
    /// [2026-09-02, 配線] SetCells（プロ一括編集・フェーズ9で移植・テスト済み）はこれまで呼び出し口が無かった。
    /// Kotlin原本の AssignBulkSheet（ドラッグではなくフィルタ選択で複数セルへ一括代入＝片手一本指の制約に沿う）を
    /// 最小移植: ①対象範囲(期間全体/この曜日) ②対象（誰に・全職員/職員を選ぶ） ③シフト（単一選択）を選ばせ、
    /// canDo（<see cref="MagiViewModel.AllowedShiftsFor"/>）で担当外の職員を自動除外したうえでセル数を
    /// プレビューし、「確定」で <see cref="MagiViewModel.SetCells"/> を1回だけ呼ぶ（SetCells 自身が
    /// Undo1回・再チェック1回でまとめる）。
    /// </summary>
    private async void OnBulkAssignClick(object sender, RoutedEventArgs e)
    {
        // [監査(ShowCellEditor/ShowShortageFixFlyoutと同じ理由)] EditBlockedNow が実行中を理由つきで拒否する。
        if (_vm.EditBlockedNow()) return;
        var ui = _vm.Ui;
        if (!ui.Loaded || ui.Schedule.Count == 0 || ui.StaffNames.Count == 0 || ui.ShiftSymbols.Count == 0) return;

        var dow0 = 0;
        if (System.DateOnly.TryParseExact(
                ui.StartDate, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var start))
        {
            dow0 = (int)start.DayOfWeek; // .NET の DayOfWeek は日曜=0（EditView.RenderWishCalendar と同じ規約）
        }

        var scopeCombo = new ComboBox
        {
            ItemsSource = new[] { "期間全体", "この曜日" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        string[] weekdayLabels = { "日", "月", "火", "水", "木", "金", "土" };
        var weekdayXs = (double)Application.Current.Resources["MagiSpacingXS"];
        var weekdayChecks = weekdayLabels.Select(l => new CheckBox { Content = l, Padding = new Thickness(weekdayXs, 0, weekdayXs, 0) }).ToList();
        var weekdayPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 2, Visibility = Visibility.Collapsed,
        };
        foreach (var cb in weekdayChecks) weekdayPanel.Children.Add(cb);

        var targetCombo = new ComboBox
        {
            ItemsSource = new[] { "全職員", "職員を選ぶ" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var staffItems = ui.StaffNames.Select((n, i) => new StaffPickItem(i, n)).ToList();
        var staffList = new ListView
        {
            ItemsSource = staffItems,
            SelectionMode = ListViewSelectionMode.Multiple,
            MaxHeight = 160,
            Visibility = Visibility.Collapsed,
        };

        var shiftCombo = new ComboBox
        {
            ItemsSource = ui.ShiftSymbols.ToList(), HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        // [トークン非適用, ファイル共通] このファイルのFontSize=12はMagiThemeタイポスケール(14始まり)
        // より小さい密グリッド/ダイアログ用の調整値で、どのStyleとも厳密一致しない（据え置き）。
        var previewText = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.85 };

        var panel = new StackPanel { Spacing = 8, Width = 340 };
        panel.Children.Add(new TextBlock { Text = "対象範囲", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 12 });
        panel.Children.Add(scopeCombo);
        panel.Children.Add(weekdayPanel);
        panel.Children.Add(new TextBlock { Text = "対象（誰に）", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 12 });
        panel.Children.Add(targetCombo);
        panel.Children.Add(staffList);
        panel.Children.Add(new TextBlock { Text = "シフト", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 12 });
        panel.Children.Add(shiftCombo);
        panel.Children.Add(previewText);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "まとめて割当",
            Content = new ScrollViewer { Content = panel, MaxHeight = 480 },
            PrimaryButtonText = "確定",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Close,
            IsPrimaryButtonEnabled = false,
        };

        // 対象日（1始まりではなく0始まりのj）。対象範囲=この曜日 のときはチェック済み曜日に限定。
        List<int> TargetDays()
        {
            if (scopeCombo.SelectedIndex != 1) return Enumerable.Range(0, ui.Days).ToList();
            var selectedWd = weekdayChecks
                .Select((cb, idx) => (cb, idx))
                .Where(t => t.cb.IsChecked == true)
                .Select(t => t.idx)
                .ToHashSet();
            return Enumerable.Range(0, ui.Days).Where(j => selectedWd.Contains((dow0 + j) % 7)).ToList();
        }

        // 対象職員（canDo未適用の生選択）。
        List<int> TargetStaff() => targetCombo.SelectedIndex == 1
            ? staffList.SelectedItems.Cast<StaffPickItem>().Select(x => x.Index).ToList()
            : Enumerable.Range(0, ui.StaffNames.Count).ToList();

        void UpdatePreview()
        {
            weekdayPanel.Visibility = scopeCombo.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            staffList.Visibility = targetCombo.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;

            var k = shiftCombo.SelectedIndex;
            if (k < 0) { previewText.Text = "シフトを選んでください。"; dialog.IsPrimaryButtonEnabled = false; return; }

            var targetStaff = TargetStaff();
            if (targetCombo.SelectedIndex == 1 && targetStaff.Count == 0)
            {
                previewText.Text = "職員を選んでください。";
                dialog.IsPrimaryButtonEnabled = false;
                return;
            }
            // [canDo自動除外＝Kotlin原本 AssignBulkSheet と同型] 担当できない職員は対象から外す。
            var eligible = targetStaff.Where(i => _vm.AllowedShiftsFor(i).Contains(k)).ToList();
            var skipped = targetStaff.Count - eligible.Count;

            if (scopeCombo.SelectedIndex == 1 && weekdayChecks.All(cb => cb.IsChecked != true))
            {
                previewText.Text = "曜日を選んでください。";
                dialog.IsPrimaryButtonEnabled = false;
                return;
            }
            var days = TargetDays();
            var cellCount = eligible.Count * days.Count;
            var skipNote = skipped > 0 ? $"（担当外のため{skipped}名を自動除外）" : "";
            previewText.Text = cellCount > 0
                ? $"{eligible.Count}名 × {days.Count}日 = {cellCount}マスへ一括設定します{skipNote}。既存の割当は上書きされます。"
                : $"対象がありません{skipNote}。";
            dialog.IsPrimaryButtonEnabled = cellCount > 0;
        }

        scopeCombo.SelectionChanged += (_, _) => UpdatePreview();
        targetCombo.SelectionChanged += (_, _) => UpdatePreview();
        staffList.SelectionChanged += (_, _) => UpdatePreview();
        shiftCombo.SelectionChanged += (_, _) => UpdatePreview();
        foreach (var cb in weekdayChecks) cb.Click += (_, _) => UpdatePreview();
        UpdatePreview();

        dialog.PrimaryButtonClick += (_, _) =>
        {
            var k = shiftCombo.SelectedIndex;
            if (k < 0) return;
            var eligible = TargetStaff().Where(i => _vm.AllowedShiftsFor(i).Contains(k)).ToList();
            var days = TargetDays();
            if (eligible.Count == 0 || days.Count == 0) return;
            var cells = new List<(int I, int J)>(eligible.Count * days.Count);
            foreach (var i in eligible)
                foreach (var j in days)
                    cells.Add((i, j));
            // ここでも running を再確認（ダイアログ表示中に別経路で最適化が始まる可能性はほぼ無いが、
            // SetCell/ApplyWishes と同じ「編集は必ずガードを通す」原則に合わせ SetCells 自身のガードに委ねる）。
            _vm.SetCells(cells, k);
        };

        await dialog.ShowAsync();
    }

    private void Render()
    {
        var ui = _vm.Ui;
        StatusText.Text = ui.Loaded
            ? $"{ui.Message}（必須={ui.BestHard} 合計={ui.BestSoft}・{ui.Staff}名×{ui.Days}日×{ui.Shifts}シフト）"
            : ui.Message ?? "読込中…";
        UndoButton.IsEnabled = ui.CanUndo && !ui.Running;
        RedoButton.IsEnabled = ui.CanRedo && !ui.Running;
        // [まとめて割当] SetCell と同じ二重防御——EditBlockedNow が最終防御、ここは押せるのに拒否されるだけの
        // ボタンを見せないための表示上の抑止（EditView/HomeView と同じ方針）。
        BulkAssignButton.IsEnabled = ui.Loaded && ui.Schedule.Count > 0 && !ui.Running;
        RenderFilterBar(ui);
        RenderSchedule(ui);
        RenderStaffTally(ui);
        RenderDayTally(ui);
        RenderNavBar(ui);
    }

    private void RenderSchedule(UiState ui)
    {
        ScheduleGridHost.Children.Clear();
        ScheduleGridHost.RowDefinitions.Clear();
        ScheduleGridHost.ColumnDefinitions.Clear();
        _focusCellElement = null;
        _dayHeaders.Clear();
        _nameHeader = null;
        if (!ui.Loaded || ui.Schedule.Count == 0) return;

        var staffCount = ui.Schedule.Count;
        var dayCount = ui.Schedule.Count > 0 ? ui.Schedule[0].Count : 0;
        // +1 列/行 = 職員名ヘッダー列・日番号ヘッダー行。
        for (var r = 0; r <= staffCount; r++) ScheduleGridHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var c = 0; c <= dayCount; c++) ScheduleGridHost.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Border AddCell(int row, int col, string text, bool header)
        {
            var block = new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = header ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                // [Token] 6,4 は密なスケジュールグリッド用に調整済みの値でMagiSpacingスケール(4/8/12…)に
                // 一致しないため据え置き（トークン化するとグリッド全体のセル間隔が変わってしまう）。
                Padding = new Thickness(6, 4, 6, 4),
                MinWidth = header && col == 0 ? 96 : 32,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var border = new Border
            {
                Child = block,
                BorderBrush = new SolidColorBrush(Colors.LightGray),
                // [Token] 罫線1dpは意図的な最小値のため据え置き（スペーシングトークンの対象外）。
                BorderThickness = new Thickness(0, 0, 1, 1),
            };
            // [クロスハイライト／違反ジャンプ] 行=職員名は淡い主色地、列=日付は主色の太枠（Kotlin原本と同じ）。
            var dayIdx = col - 1;
            var dayFocused = row == 0 && dayIdx >= 0 &&
                ((_focusCell is { } fc && fc.I < 0 && fc.J == dayIdx) || (_tapped is { } tp && tp.J == dayIdx));
            if (dayFocused)
            {
                border.BorderBrush = (Brush)Application.Current.Resources["MagiPrimaryBrush"];
                border.BorderThickness = new Thickness(3);
            }
            if (col == 0 && row > 0 && _tapped is { } tp2 && tp2.I == row - 1)
            {
                var c = ((SolidColorBrush)Application.Current.Resources["MagiPrimaryBrush"]).Color;
                border.Background = new SolidColorBrush(Color.FromArgb(31, c.R, c.G, c.B));
            }
            Grid.SetRow(border, row);
            Grid.SetColumn(border, col);
            ScheduleGridHost.Children.Add(border);
            return border;
        }

        void AddDataCell(int row, int col, int i, int j, int k)
        {
            var sym = k >= 0 && k < ui.ShiftSymbols.Count ? ui.ShiftSymbols[k] : k.ToString();
            // [2026-09-02, 配線] ui.ShiftColorHex/ShiftTextHex は常に解決済みの色を持つ（既定パレット
            // または SettingsView の色設定で保存したもの）。従来はここで一切読んでおらず、色設定を
            // 変更しても勤務表グリッドの見た目が変わらない「論理的な箱」だった。
            var bg = k >= 0 && k < ui.ShiftColorHex.Count ? ParseHexColor(ui.ShiftColorHex[k], Colors.Transparent) : Colors.Transparent;
            var fg = k >= 0 && k < ui.ShiftTextHex.Count ? ParseHexColor(ui.ShiftTextHex[k], Colors.Black) : Colors.Black;
            var button = new Button
            {
                Content = new TextBlock { Text = sym, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Foreground = new SolidColorBrush(fg) },
                // [Token] セルパディング/枠/角丸は密グリッド用の意図的な値（据え置き。上のAddCellと同じ理由）。
                Padding = new Thickness(6, 4, 6, 4),
                MinWidth = 32,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Background = new SolidColorBrush(bg),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                IsEnabled = !ui.Running,
            };
            button.Click += (sender, _) =>
            {
                MarkTapped(i, j);
                ShowCellEditor((FrameworkElement)sender, i, j);
            };

            var cell = new Grid();
            cell.Children.Add(button);
            if (ui.Wishes.TryGetValue($"{i},{j}", out var wishK))
            {
                var reflected = wishK == k;
                cell.Children.Add(new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = new SolidColorBrush(reflected ? Colors.SeaGreen : Colors.HotPink),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    // [Token] 希望バッジ(8x8)の位置微調整値でスペーシングトークンの対象外のため据え置き。
                    Margin = new Thickness(0, 0, 2, 2),
                });
            }

            // [Token] 罫線1dp/違反枠2dp/フォーカス枠3dpはいずれも意図的な強調段階の値で
            // スペーシング/角丸トークンの対象外（据え置き）。
            Brush borderBrush = new SolidColorBrush(Colors.LightGray);
            var thickness = new Thickness(0, 0, 1, 1);
            var vioClass = VioBuckets.VisibleCellVio(ui, $"{i},{j}", _vioEnabled);
            if (vioClass is not null)
            {
                borderBrush = ResolveVioBrush(ui, vioClass);
                thickness = new Thickness(2);
            }
            // [集中モード] 違反・未反映希望・注目セル以外を淡色に沈める（非表示にはしない＝被覆の文脈は残す）。
            var unreflectedWish = ui.Wishes.TryGetValue($"{i},{j}", out var wk0) && wk0 != k;
            var cellFocused = _focusCell is { } fc0 && fc0.I == i && fc0.J == j;
            if (_focusMode && vioClass is null && !unreflectedWish && !cellFocused) button.Opacity = 0.35;
            // [違反箇所へのジャンプ] 注目セルは一時的に強調色の太枠へ差し替える（FocusCell 参照）。
            var isFocused = _focusCell is { } fc && fc.I == i && fc.J == j;
            if (isFocused)
            {
                borderBrush = new SolidColorBrush(Colors.DodgerBlue);
                thickness = new Thickness(3);
            }
            var border = new Border { Child = cell, BorderBrush = borderBrush, BorderThickness = thickness };
            if (isFocused) _focusCellElement = border;
            Grid.SetRow(border, row);
            Grid.SetColumn(border, col);
            ScheduleGridHost.Children.Add(border);
        }

        _nameHeader = AddCell(0, 0, "", header: true);
        for (var j = 0; j < dayCount; j++)
        {
            var h = AddCell(0, j + 1, $"{j + 1}", header: true);
            _dayHeaders.Add(h);
            if (_focusCell is { } fc && fc.I < 0 && fc.J == j) _focusCellElement = h;
        }

        for (var i = 0; i < staffCount; i++)
        {
            var name = i < ui.StaffNames.Count ? ui.StaffNames[i] : $"#{i}";
            AddCell(i + 1, 0, name, header: true);
            var row = ui.Schedule[i];
            for (var j = 0; j < row.Count; j++)
            {
                AddDataCell(i + 1, j + 1, i, j, row[j]);
            }
        }
    }

    /// <summary>
    /// [シフト集計＝Kotlin原本 TallyCard の最小移植] 職員別（職員×シフト回数）。
    /// 生カウントは <see cref="UiState.Schedule"/> から都度数える（S≤30・K≤12程度の規模なら軽い）。
    /// セル枠は <see cref="UiState.CountViolations"/>（"i,k"→low/high/apt 等）で色分けし、
    /// <see cref="RenderSchedule"/> の違反セルと同じ「必須=濃い赤／要調整=橙」の凡例を踏襲する。
    /// </summary>
    private void RenderStaffTally(UiState ui)
    {
        StaffTallyGridHost.Children.Clear();
        StaffTallyGridHost.RowDefinitions.Clear();
        StaffTallyGridHost.ColumnDefinitions.Clear();
        if (!ui.Loaded || ui.Schedule.Count == 0) return;

        var staffCount = ui.Schedule.Count;
        var shiftCount = ui.ShiftSymbols.Count;
        for (var r = 0; r <= staffCount; r++) StaffTallyGridHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var c = 0; c <= shiftCount; c++) StaffTallyGridHost.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        AddTallyCell(StaffTallyGridHost, 0, 0, "", header: true);
        for (var k = 0; k < shiftCount; k++) AddTallyCell(StaffTallyGridHost, 0, k + 1, ui.ShiftSymbols[k], header: true);

        for (var i = 0; i < staffCount; i++)
        {
            var name = i < ui.StaffNames.Count ? ui.StaffNames[i] : $"#{i}";
            AddTallyCell(StaffTallyGridHost, i + 1, 0, name, header: true);
            for (var k = 0; k < shiftCount; k++)
            {
                var count = ui.Schedule[i].Count(v => v == k);
                ui.CountViolations.TryGetValue($"{i},{k}", out var vioClass);
                if (!VioBuckets.VioVisible(vioClass, _vioEnabled)) vioClass = null;
                var brush = VioBorderBrush(ui, vioClass, out var thickness);
                AddTallyCell(StaffTallyGridHost, i + 1, k + 1, count.ToString(), header: false, borderBrush: brush, thickness: thickness);
            }
        }
    }

    /// <summary>
    /// [シフト集計＝Kotlin原本 TallyCard の最小移植] 日別（シフト×日 人数）。
    /// セル枠は <see cref="UiState.NeedViolations"/>（"k,j"→covU/covO 等）で色分けする。
    /// </summary>
    private void RenderDayTally(UiState ui)
    {
        DayTallyGridHost.Children.Clear();
        DayTallyGridHost.RowDefinitions.Clear();
        DayTallyGridHost.ColumnDefinitions.Clear();
        if (!ui.Loaded || ui.Schedule.Count == 0) return;

        var shiftCount = ui.ShiftSymbols.Count;
        var dayCount = ui.Schedule[0].Count;
        for (var r = 0; r <= shiftCount; r++) DayTallyGridHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var c = 0; c <= dayCount; c++) DayTallyGridHost.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        AddTallyCell(DayTallyGridHost, 0, 0, "", header: true);
        for (var j = 0; j < dayCount; j++) AddTallyCell(DayTallyGridHost, 0, j + 1, $"{j + 1}", header: true);

        for (var k = 0; k < shiftCount; k++)
        {
            AddTallyCell(DayTallyGridHost, k + 1, 0, ui.ShiftSymbols[k], header: true);
            for (var j = 0; j < dayCount; j++)
            {
                var count = 0;
                foreach (var row in ui.Schedule) if (j < row.Count && row[j] == k) count++;
                ui.NeedViolations.TryGetValue($"{k},{j}", out var vioClass);
                if (!VioBuckets.VioVisible(vioClass, _vioEnabled)) vioClass = null;
                var brush = VioBorderBrush(ui, vioClass, out var thickness);
                // [2026-09-02, 配線] ShortageFixCandidates（フェーズ9で移植・テスト済み）はこれまで
                // 呼び出し口が無かった。人員不足(covU)のセルだけボタン化し、タップで「動かせる人」の
                // 候補（担当可能・希望固定でない・禁止連続にならない・抜けても穴が空かない）をフライアウトで
                // 出し、選ぶと即 SetCell で割り当てる。
                Action<FrameworkElement>? onClick = vioClass == "vio-covU"
                    ? anchor => ShowShortageFixFlyout(anchor, j, k)
                    : null;
                AddTallyCell(DayTallyGridHost, k + 1, j + 1, count.ToString(), header: false, borderBrush: brush, thickness: thickness, onClick: onClick);
            }
        }
    }

    /// <summary>集計グリッド共通のセル描画（<see cref="RenderStaffTally"/>/<see cref="RenderDayTally"/> 共用）。
    /// <paramref name="onClick"/> を渡すと素のセルの代わりにボタン化し、タップ元(<see cref="FrameworkElement"/>)を
    /// フライアウトのアンカーとして渡す（<see cref="ShowShortageFixFlyout"/> 参照）。</summary>
    private static void AddTallyCell(Grid host, int row, int col, string text, bool header, Brush? borderBrush = null, Thickness? thickness = null, Action<FrameworkElement>? onClick = null)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = header ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        FrameworkElement content = block;
        if (onClick is not null)
        {
            // [Token] セルパディング/枠/角丸は密グリッド用の意図的な値（据え置き。RenderSchedule の
            // AddCell/AddDataCell と同じ理由でMagiSpacing/MagiCornerスケールに一致しない）。
            var button = new Button
            {
                Content = block,
                Padding = new Thickness(6, 4, 6, 4),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                MinWidth = 32,
            };
            button.Click += (sender, _) => onClick((FrameworkElement)sender);
            content = button;
        }
        else
        {
            block.Padding = new Thickness(6, 4, 6, 4);
            block.MinWidth = header && col == 0 ? 96 : 32;
        }
        var border = new Border
        {
            Child = content,
            BorderBrush = borderBrush ?? new SolidColorBrush(Colors.LightGray),
            // [Token] 罫線1dpは意図的な最小値のため据え置き（据え置き理由は AddCell と同じ）。
            BorderThickness = thickness ?? new Thickness(0, 0, 1, 1),
        };
        Grid.SetRow(border, row);
        Grid.SetColumn(border, col);
        host.Children.Add(border);
    }

    /// <summary>人員不足セルのタップ→動かせる候補一覧→ワンタップ割当。候補0件なら理由を出す
    /// （「動かせる人がいない」＝この画面の手には余る＝別の対処が要ることの表明）。</summary>
    private void ShowShortageFixFlyout(FrameworkElement anchor, int day, int shift)
    {
        // [2026-09-02, 配線] EditBlockedNow。ShowCellEditor と同じ理由＝ SetCell を呼ぶ入口はここも同じで、
        // 旧実装はガード自体が無く実行中でも候補が出て SetCell が黙って弾かれるだけだった。
        if (_vm.EditBlockedNow()) return;
        var candidates = _vm.ShortageFixCandidates(day, shift);
        var flyout = new MenuFlyout();
        foreach (var c in candidates)
        {
            var item = new MenuFlyoutItem { Text = c.FromRest ? $"{c.Name}（休み中）" : c.Name };
            var i = c.StaffIndex;
            item.Click += (_, _) => _vm.SetCell(i, day, shift);
            flyout.Items.Add(item);
        }
        if (flyout.Items.Count == 0)
        {
            flyout.Items.Add(new MenuFlyoutItem { Text = "動かせる候補がいません（担当可能・希望が固定でない・玉突きなしの人がいない）", IsEnabled = false });
        }
        flyout.ShowAt(anchor);
    }

    /// <summary>違反クラス文字列("vio-xxx"等)から集計セルの枠色/太さを決める。null=違反なし=既定枠。</summary>
    private static Brush? VioBorderBrush(UiState ui, string? vioClass, out Thickness? thickness)
    {
        if (vioClass is null) { thickness = null; return null; }
        // [Token] 違反枠2dpは RenderSchedule の違反強調と同じ意図的な値のため据え置き。
        thickness = new Thickness(2);
        return ResolveVioBrush(ui, vioClass);
    }

    /// <summary>
    /// [2026-09-02, 配線] 違反クラス("vio-xxx")→表示色。Kotlin原本 <c>resolvedVioColor(ui,cls,hard,soft)</c>
    /// の逐語移植（族別 <see cref="UiState.ViolationFamilyColorHex"/> が最優先、無ければ重大度の基準色
    /// <see cref="UiState.ViolationColorHex"/>/<see cref="UiState.ViolationSoftColorHex"/>、それも
    /// 未設定なら既定色）。<see cref="ScheduleView"/>（メイングリッド）と TallyCard の両方が共有する
    /// 唯一の解決元——従来はここが無く、SettingsView 側で色を変更しても勤務表の見た目が変わらない
    /// 「配線されていない ViewModel API」だった。
    /// </summary>
    private static Brush ResolveVioBrush(UiState ui, string vioClassRaw)
    {
        var stripped = vioClassRaw.StartsWith("vio-", StringComparison.Ordinal) ? vioClassRaw["vio-".Length..] : vioClassRaw;
        var family = stripped is "aptLow" or "aptHigh" ? "apt" : stripped;
        var hard = MirrorKeys.Hard.Contains(family);
        var fallback = hard ? DefaultHardVioColor : DefaultSoftVioColor;
        if (ui.ViolationFamilyColorHex.TryGetValue(family, out var famHex) && !string.IsNullOrWhiteSpace(famHex))
            return new SolidColorBrush(ParseHexColor(famHex, fallback));
        var baseHex = hard ? ui.ViolationColorHex : ui.ViolationSoftColorHex;
        return new SolidColorBrush(ParseHexColor(baseHex, fallback));
    }

    private static readonly Color DefaultHardVioColor = ColorHex.Parse(ColorHex.DefaultHardVioHex, Colors.Crimson);
    private static readonly Color DefaultSoftVioColor = ColorHex.Parse(ColorHex.DefaultSoftVioHex, Colors.DarkOrange);

    private static Color ParseHexColor(string? hex, Color fallback) => ColorHex.Parse(hex, fallback);

    /// <summary>タップされたセルの担当可能シフト一覧をフライアウトで出し、選択で <c>SetCell</c> を呼ぶ。</summary>
    private void ShowCellEditor(FrameworkElement anchor, int i, int j)
    {
        // [2026-09-02, 配線] EditBlockedNow（Kotlin 3.405.0 相当）。旧: Ui.Running の素通し判定のみで、
        // ボタン無効化の取りこぼし（Render 直後のタップ等）は理由も出さず無反応に見えていた。
        // SetCell 自身が使うのと同じ文言を Ui.Message へセットするので、StatusText（Render 側）に
        // 「なぜ何も起きなかったか」が出る。
        if (_vm.EditBlockedNow()) return;
        var ui = _vm.Ui;
        var allowed = _vm.AllowedShiftsFor(i);
        var flyout = new MenuFlyout();
        foreach (var k in allowed)
        {
            var sym = k >= 0 && k < ui.ShiftSymbols.Count ? ui.ShiftSymbols[k] : k.ToString();
            var item = new MenuFlyoutItem { Text = sym };
            item.Click += (_, _) => _vm.SetCell(i, j, k);
            flyout.Items.Add(item);
        }
        if (flyout.Items.Count == 0)
        {
            flyout.Items.Add(new MenuFlyoutItem { Text = "担当可能なシフトがありません", IsEnabled = false });
        }

        // [2026-09-02, 配線] AddReviewMemo（クラスKDoc参照）。違反セルのときだけ「見直し候補にする」を
        // 出す（違反の無いセルを見直し候補にする意味が無いため）。
        if (ui.ViolationCells.TryGetValue($"{i},{j}", out var vioClass))
        {
            flyout.Items.Add(new MenuFlyoutSeparator());
            var name = i < ui.StaffNames.Count ? ui.StaffNames[i] : $"#{i}";
            var family = vioClass.StartsWith("vio-", StringComparison.Ordinal) ? vioClass["vio-".Length..] : vioClass;
            var label = AnalysisView.BreakdownLabels.TryGetValue(family, out var jp) ? jp : family;
            var memoItem = new MenuFlyoutItem { Text = "この違反を見直し候補にする" };
            memoItem.Click += (_, _) => _vm.AddReviewMemo($"{name} {j + 1}日: {label}");
            flyout.Items.Add(memoItem);
        }

        flyout.ShowAt(anchor);
    }
}
