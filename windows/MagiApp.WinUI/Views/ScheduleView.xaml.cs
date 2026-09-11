using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
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
/// グリッド描画コードを、フェーズ9のマルチタブ化に伴いこの <see cref="UserControl"/> へ切り出した。
///
/// [2026-09-11, ItemsView化] マトリックス本体は <c>ItemsView</c>（<c>ScheduleView.xaml</c>）＋
/// <see cref="ScheduleRowVm"/>/<see cref="ScheduleCellVm"/>（<c>ScheduleGridModels.cs</c>）へ移行。
/// <see cref="RenderSchedule"/> は毎回セルの UI 要素を作り直すのでなく、行・セルのコレクション自体は
/// 使い回して値だけ書き換える（<see cref="SyncCount{T}"/>）。実体化済みセルの参照は
/// <see cref="_cellElements"/> へ<see cref="OnScheduleCellLoaded"/>/<see cref="OnScheduleCellUnloaded"/>が
/// 自己登録・解除する（<c>ItemsRepeater</c>がコンテナを使い回すため、固定リストでなくこの方式で追う）。
///
/// [セル編集] データセルは <see cref="Border"/>＋<c>Tapped</c>（2026-09-11、旧: <see cref="Button"/>。
/// 大盤面ではセル数が数百〜千を超え、<see cref="Button"/> 1個ごとのControlTemplate/VisualStateManager
/// のコストが総描画時間を支配していた＝タップやフィルタ操作のたびに<see cref="RenderSchedule"/>が
/// グリッド全体を作り直す設計と組み合わさって体感カクつきの主因になっていた。軽量な
/// <see cref="Border"/>へ替え、押下フィードバックは薄暗いオーバーレイ<c>Rectangle</c>の表示/非表示のみに
/// 縮小（<see cref="Border.Opacity"/>自体は<see cref="ScheduleCellVm"/>から一方向バインドされているため、
/// 押下中だけ手動で書き換えるとバインディングと衝突する＝2026-09-11のItemsView化で変更）でタップを拾い、
/// <see cref="MenuFlyout"/> を開いて
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
    private readonly CoalescedRender _renderCoalescer;

    /// <summary>[違反箇所へのジャンプ] 分析タブから飛んできた注目セル。<see cref="FocusCell"/> 参照。</summary>
    private (int I, int J)? _focusCell;

    /// <summary>注目セルの実体化済み要素（無ければ null）。<see cref="OnScheduleCellLoaded"/>が
    /// 実体化のたびに更新する。<see cref="FocusCell"/> が <c>StartBringIntoView</c> を呼ぶために使う。</summary>
    private Border? _focusCellElement;

    private DispatcherTimer? _focusTimer;

    private static readonly string[] WeekdayJa = { "月", "火", "水", "木", "金", "土", "日" };

    /// <summary>[2026-09-11, ItemsView化] <see cref="ScheduleItemsView"/> の <c>ItemsSource</c>。
    /// <see cref="RenderSchedule"/> はこのコレクション自体を使い回し、値だけを書き換える
    /// （<see cref="ScheduleCellVm"/> のKDoc参照）。</summary>
    private readonly ObservableCollection<ScheduleRowVm> _rows = new();

    /// <summary>[2026-09-11, ItemsView化] 実体化済みセルの(行,列)→<see cref="Border"/>逆引き
    /// （<see cref="OnScheduleCellLoaded"/>/<see cref="OnScheduleCellUnloaded"/>が自己登録・解除する）。
    /// 旧実装の <c>_dayHeaders</c>/<c>_nameHeader</c> をこの1つへ統合。</summary>
    private readonly Dictionary<(int Row, int Col), Border> _cellElements = new();

    /// <summary>職員名ヘッダー(row=0,col=0)の実体。幅(<c>ActualWidth</c>)だけを読むために持つ
    /// （旧 <c>_nameHeader</c>）。</summary>
    private Border? _nameHeaderWidth;

    private (int I, int J)? _tapped;
    private DispatcherTimer? _tappedTimer;
    private List<List<int>> _weeks = new();
    private List<int> _vioDays = new();
    private int _navIdx = -1;

    /// <summary>[phase9 #7 E7] 表示中のバケツ（初期は全 ON）と集中モード。表示のみ・スコアリング不変。</summary>
    private readonly HashSet<string> _vioEnabled = new(VioBuckets.AllKeys);
    private bool _focusMode;

    /// <summary>[phase9 #10] シフト別の人員不足サマリー（covU のある日数をシフト別に多い順）。「人員」バケツ OFF なら他の covU 表示と同様に隠す。</summary>
    private void RenderShortageBanner(UiState ui)
    {
        if (!ui.Loaded || !_vioEnabled.Contains("need"))
        {
            ShortageBanner.Visibility = Visibility.Collapsed;
            return;
        }
        var byShift = new Dictionary<int, HashSet<int>>();
        foreach (var (key, cls) in ui.NeedViolations)
        {
            if (cls != "vio-covU") continue;
            var parts = key.Split(',');
            if (parts.Length != 2 || !int.TryParse(parts[0], out var k) || !int.TryParse(parts[1], out var j)) continue;
            if (!byShift.TryGetValue(k, out var days)) byShift[k] = days = new HashSet<int>();
            days.Add(j);
        }
        if (byShift.Count == 0)
        {
            ShortageBanner.Visibility = Visibility.Collapsed;
            return;
        }
        ShortageBanner.Visibility = Visibility.Visible;
        var body = string.Join(" ・ ", byShift.OrderByDescending(kv => kv.Value.Count)
            .Select(kv => $"{(kv.Key < ui.ShiftSymbols.Count ? ui.ShiftSymbols[kv.Key] : kv.Key.ToString())} {kv.Value.Count}日"));
        ShortageBannerText.Text = $"人員不足（全{Math.Max(1, ui.Days)}日中）: {body}";
    }

    /// <summary>[phase9 #8] 検索・凡例の開閉（既定は閉）と職員名の検索語。</summary>
    private bool _searchLegendOpen;
    private string _nameQuery = "";

    /// <summary>[phase9 #11] 内訳ダイアログの「直し方を探す」の飛び先（分析タブ）。</summary>
    private readonly Action? _goAnalysis;

    public ScheduleView(MagiViewModel vm, Action? goAnalysis = null)
    {
        _vm = vm;
        _goAnalysis = goAnalysis;
        InitializeComponent();
        ScheduleItemsView.ItemsSource = _rows;
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

    // [2026-09-10, カクつき/フリーズ対策] 進捗バーストで Render() が連打されないよう CoalescedRender で間引く
    //   （CoalescedRender のKDoc参照）。
    private void OnUiChanged(object? sender, PropertyChangedEventArgs e) => _renderCoalescer.Request();

    /// <summary>
    /// [違反箇所へのジャンプ] 分析タブの「違反の場所」からの遷移先。Kotlin原本の
    /// <c>focusCell</c>（約2.5秒だけ枠でハイライトして自動的に消える）の最小移植——
    /// この移植では枠色を一時的に強調色へ差し替え、タイマー満了で再描画して元に戻す
    /// （<see cref="Render"/> が毎回グリッドを作り直す設計のため、アニメーションではなく
    /// 「ハイライトを付けて描く／付けずに描き直す」の2状態で表現する）。
    /// [2026-09-11, ItemsView化] <see cref="_focusCellElement"/>は<see cref="OnScheduleCellLoaded"/>が
    /// レイアウト後に非同期で設定するため（旧実装はRenderSchedule内で同期的に作っていた）、
    /// スクロール操作はRenderNavBarと同じ「レイアウト後にもう一度」パターンへ委ねる。
    /// </summary>
    public void FocusCell(int i, int j)
    {
        _focusCell = (i, j);
        Render();
        DispatcherQueue.TryEnqueue(() => { if (i < 0) ScrollToDay(j); else _focusCellElement?.StartBringIntoView(); });

        _focusTimer?.Stop();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _focusCell = null;
            _renderCoalescer.Request();
        };
        _focusTimer = timer;
        timer.Start();
    }

    /// <summary>[3.444.0 クロスハイライト] タップしたセルの職員名と日付を約2.5秒強調（セルの違反枠は変えない）。</summary>
    private void MarkTapped(int i, int j)
    {
        _tapped = (i, j);
        // [2026-09-11/カクつき対策] StartBringIntoView等の描画結果への依存が無いためCoalescedRenderで
        // 間引く（FocusCellの直呼びは_focusCellElementへの依存があるため対象外のまま）。
        _renderCoalescer.Request();
        _tappedTimer?.Stop();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _tapped = null;
            _renderCoalescer.Request();
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
                _renderCoalescer.Request();
            };
            BucketChips.Children.Add(chip);
        }
    }

    /// <summary>[phase9 #8] 検索・凡例（Kotlin原本 <c>SearchLegendBar</c>／<c>ViolationLegend</c>／<c>ShiftColorLegend</c>）。</summary>
    private void RenderSearchLegend(UiState ui)
    {
        SearchLegendBar.Visibility = ui.Loaded ? Visibility.Visible : Visibility.Collapsed;
        if (!ui.Loaded) return;
        var title = "検索・凡例" + (!_searchLegendOpen && _nameQuery.Length > 0 ? $"（検索中: {_nameQuery}）" : "");
        SearchLegendToggle.Content = $"{title}  {(_searchLegendOpen ? "閉じる ▾" : "開く ▸")}";
        SearchLegendPanel.Visibility = _searchLegendOpen ? Visibility.Visible : Visibility.Collapsed;
        if (!_searchLegendOpen) return;
        if (SearchBox.Text != _nameQuery) SearchBox.Text = _nameQuery;
        SearchClearButton.Visibility = _nameQuery.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        ViolationLegendHost.Children.Clear();
        ViolationLegendHost.Visibility = ui.ViolationCells.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (ui.ViolationCells.Count > 0)
        {
            var hard = ResolveVioBrush(ui, "vio-covU");
            var soft = ResolveVioBrush(ui, "vio-covO");
            ViolationLegendHost.Children.Add(LegendItem(new Border { Width = 22, Height = 16, BorderBrush = hard, BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(4) }, "赤枠＝絶対NG"));
            ViolationLegendHost.Children.Add(LegendItem(new Border { Width = 22, Height = 16, BorderBrush = soft, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(4) }, "橙枠＝できれば直す"));
            ViolationLegendHost.Children.Add(LegendItem(new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(Colors.HotPink) }, "桃ドット＝希望が未反映"));
            ViolationLegendHost.Children.Add(LegendItem(new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(Colors.SeaGreen) }, "緑ドット＝希望が反映済み"));
        }

        ShiftLegendHost.Children.Clear();
        for (var k = 0; k < ui.ShiftSymbols.Count; k++)
        {
            if (string.IsNullOrWhiteSpace(ui.ShiftSymbols[k])) continue;
            var bg = k < ui.ShiftColorHex.Count ? ParseHexColor(ui.ShiftColorHex[k], Colors.Transparent) : Colors.Transparent;
            var fg = k < ui.ShiftTextHex.Count ? ParseHexColor(ui.ShiftTextHex[k], Colors.Black) : Colors.Black;
            ShiftLegendHost.Children.Add(new Border
            {
                Height = 32, MinWidth = 48, Padding = new Thickness(10, 0, 10, 0), CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(bg),
                Child = new TextBlock
                {
                    Text = ui.ShiftSymbols[k], Foreground = new SolidColorBrush(fg), FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            });
        }
        var anyShift = ShiftLegendHost.Children.Count > 0;
        ShiftLegendTitle.Visibility = anyShift ? Visibility.Visible : Visibility.Collapsed;
    }

    private static StackPanel LegendItem(UIElement swatch, string label)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(swatch);
        row.Children.Add(new TextBlock { Text = label, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8 });
        return row;
    }

    // [2026-09-11/カクつき対策] 以下5つは検索・フィルタのUI操作に伴う再描画で、直前の描画結果への
    //   依存が無いためCoalescedRenderで間引く。OnSearchTextChangedは特に、キー入力のたびに
    //   グリッド全体（数百〜千要素）を作り直していた分岐のため効果が大きい。
    private void OnSearchLegendToggleClick(object sender, RoutedEventArgs e)
    {
        _searchLegendOpen = !_searchLegendOpen;
        _renderCoalescer.Request();
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchBox.Text == _nameQuery) return;
        _nameQuery = SearchBox.Text;
        _renderCoalescer.Request();
    }

    private void OnSearchClearClick(object sender, RoutedEventArgs e)
    {
        _nameQuery = "";
        _renderCoalescer.Request();
    }

    private void OnShowAllVioClick(object sender, RoutedEventArgs e)
    {
        _vioEnabled.UnionWith(VioBuckets.AllKeys);
        _renderCoalescer.Request();
    }

    private void OnFocusToggleClick(object sender, RoutedEventArgs e)
    {
        _focusMode = FocusToggle.IsChecked == true;
        _renderCoalescer.Request();
    }

    /// <summary>現在の日数（ヘッダー行の列数-1）。<see cref="_rows"/>から読む＝<see cref="RenderSchedule"/>と
    /// 独立に呼べる。</summary>
    private int DayCount() => _rows.Count > 0 ? _rows[0].Cells.Count - 1 : 0;

    /// <summary>[2026-09-11, ItemsView化] 日ヘッダーの実体は<see cref="_cellElements"/>から都度引く
    /// （旧 <c>_dayHeaders</c>固定リストの代わり。<see cref="ItemsRepeater"/>はコンテナを使い回すため、
    /// 固定インデックスのリストで持たずキー引きにする）。未実体化（未Loaded）なら0を返す。</summary>
    private double HeaderX(int d) =>
        d >= 0 && _cellElements.TryGetValue((0, d + 1), out var header)
            ? header.TransformToVisual(ScheduleItemsView).TransformPoint(new Windows.Foundation.Point(0, 0)).X
            : 0;

    /// <summary>左端に見えている日から現在週を求める（自由スクロールにも追従）。</summary>
    private int CurrentWeek()
    {
        if (_weeks.Count == 0) return 0;
        var left = GridScroll.HorizontalOffset + (_nameHeaderWidth?.ActualWidth ?? 0);
        var d = 0;
        var dayCount = DayCount();
        for (; d < dayCount; d++)
        {
            if (!_cellElements.TryGetValue((0, d + 1), out var header)) break;
            if (HeaderX(d) + header.ActualWidth > left + 1) break;
        }
        var w = _weeks.FindIndex(wk => d <= wk[^1]);
        return w < 0 ? _weeks.Count - 1 : w;
    }

    private void ScrollToDay(int d)
    {
        if (d < 0 || d >= DayCount()) return;
        var x = HeaderX(d) - (_nameHeaderWidth?.ActualWidth ?? 0);
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

        // [2026-09-10, 可読性] このファイルのFontSizeはMagiThemeタイポスケールの本文最小(14)まで引き上げ済み
        //   （ユーザー報告「画面が見にくい」。密グリッド/ダイアログ用に10〜13へ据え置いていたのを解消）。
        var previewText = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14, Opacity = 0.85 };

        var panel = new StackPanel { Spacing = 8, Width = 340 };
        panel.Children.Add(new TextBlock { Text = "対象範囲", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 14 });
        panel.Children.Add(scopeCombo);
        panel.Children.Add(weekdayPanel);
        panel.Children.Add(new TextBlock { Text = "対象（誰に）", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 14 });
        panel.Children.Add(targetCombo);
        panel.Children.Add(staffList);
        panel.Children.Add(new TextBlock { Text = "シフト", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 14 });
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
        RenderShortageBanner(ui);
        RenderSearchLegend(ui);
        RenderSchedule(ui);
        RenderStaffTally(ui);
        RenderDayTally(ui);
        RenderNavBar(ui);
    }

    /// <summary>[2026-09-11, ItemsView化] コレクション(<paramref name="coll"/>)の要素数を<paramref name="count"/>に
    /// 合わせる。既存要素は使い回す（先頭から）＝<see cref="ScheduleItemsView"/>のコンテナ再利用・
    /// スクロール位置維持のため、行・列数が変わらない限り毎回新しいインスタンスを作らない。</summary>
    private static void SyncCount<T>(ObservableCollection<T> coll, int count, Func<int, T> factory)
    {
        while (coll.Count < count) coll.Add(factory(coll.Count));
        while (coll.Count > count) coll.RemoveAt(coll.Count - 1);
    }

    private void RenderSchedule(UiState ui)
    {
        if (!ui.Loaded || ui.Schedule.Count == 0)
        {
            _rows.Clear();
            return;
        }

        var staffCount = ui.Schedule.Count;
        var dayCount = ui.Schedule.Count > 0 ? ui.Schedule[0].Count : 0;
        DateOnly? startDay = DateOnly.TryParseExact(ui.StartDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var sd) ? sd : null;
        var today = DateOnly.FromDateTime(DateTime.Today);
        // [phase9 #10] 日別の不足人数「▼N」（covU 由来なので「人員」バケツ ON のときだけ）。
        var dayShort = new int[dayCount];
        if (_vioEnabled.Contains("need") && ui.V6 is { } v6)
            foreach (var r in v6.DayRisks) if (r.DayIndex >= 0 && r.DayIndex < dayCount) dayShort[r.DayIndex] = r.Shortage;

        // +1 行/列 = 日番号ヘッダー行(row 0)・職員名ヘッダー列(col 0)。
        SyncCount(_rows, staffCount + 1, _ => new ScheduleRowVm());
        for (var r = 0; r <= staffCount; r++)
        {
            var rr = r; // ローカルへコピー（クロージャ捕捉対策）
            SyncCount(_rows[r].Cells, dayCount + 1, c => new ScheduleCellVm { Row = rr, Col = c });
        }

        void UpdateHeaderCell(int row, int col, string text)
        {
            var cell = _rows[row].Cells[col];
            cell.IsHeader = true;
            cell.I = -1;
            cell.J = -1;
            cell.Text = text;
            cell.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            cell.MinWidth = col == 0 ? 96 : 32;
            cell.TextAlignment = TextAlignment.Center;
            cell.Foreground = new SolidColorBrush(Colors.Black);
            cell.Tooltip = null;
            cell.WishDotVisibility = Visibility.Collapsed;
            // [phase9 #9] 日ヘッダ＝日番号＋曜日。祝日と日曜は赤、土曜は青の淡い地（Kotlin原本 DayHeader、祝日は日曜と同じ扱い）。
            //   今日は濃緑の太字で、地色は重ねない（混同回避）。祝日名はツールチップへ。
            Color? tint = null;
            if (row == 0 && col > 0 && startDay is { } d0)
            {
                var date = d0.AddDays(col - 1);
                var dow = ((int)date.DayOfWeek + 6) % 7;
                var holiday = JapanHolidays.NameOf(date);
                cell.Text = $"{col}\n{WeekdayJa[dow]}";
                var isToday = date == today;
                if (holiday is not null || dow == 6) tint = ColorHex.Parse(MagiAccent.Red, Colors.Red);
                else if (dow == 5) tint = ColorHex.Parse(MagiAccent.Blue, Colors.Blue);
                if (isToday)
                {
                    cell.Foreground = (Brush)Application.Current.Resources["MagiTertiaryBrush"];
                    cell.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
                }
                else if (tint is { } tc)
                {
                    cell.Foreground = new SolidColorBrush(tc);
                }
                if (isToday) tint = null;
                if (holiday is not null) cell.Tooltip = $"{col}日 {WeekdayJa[dow]}曜日 {holiday}";
                if (dayShort[col - 1] > 0)
                {
                    cell.Text += $"\n▼{dayShort[col - 1]}";
                    if (!isToday) cell.Foreground = (Brush)Application.Current.Resources["MagiErrorBrush"];
                    cell.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
                }
            }
            // [検索] 一致する職員名を太字＋青で強調（行は隠さず＝被覆の文脈を保つ）。
            if (col == 0 && row > 0 && _nameQuery.Length > 0 && text.Contains(_nameQuery, StringComparison.OrdinalIgnoreCase))
            {
                cell.Foreground = new SolidColorBrush(ColorHex.Parse(MagiAccent.Blue, Colors.RoyalBlue));
                cell.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
            }
            // [2026-09-11] Kotlin原本 FlatCell は既定で無枠・角丸6dp・セル間は罫線でなく余白で分離
            // （plainBorder既定false）。旧実装は全セルに常時1dp灰色罫線を引いており、原本に無い
            // 「表計算ソフトの格子線」的な見た目のズレ＝張りぼて感・バラツキの主因だった。
            cell.BorderBrush = new SolidColorBrush(Colors.Transparent);
            cell.BorderThickness = new Thickness(0);
            cell.Background = tint is { } tintColor
                ? new SolidColorBrush(Color.FromArgb(36, tintColor.R, tintColor.G, tintColor.B))
                : new SolidColorBrush(Colors.Transparent);
            // [クロスハイライト／違反ジャンプ] 行=職員名は淡い主色地、列=日付は主色の太枠（Kotlin原本と同じ）。
            var dayIdx = col - 1;
            var dayFocused = row == 0 && dayIdx >= 0 &&
                ((_focusCell is { } fc && fc.I < 0 && fc.J == dayIdx) || (_tapped is { } tp && tp.J == dayIdx));
            if (dayFocused)
            {
                cell.BorderBrush = (Brush)Application.Current.Resources["MagiPrimaryBrush"];
                cell.BorderThickness = new Thickness(3);
            }
            if (col == 0 && row > 0 && _tapped is { } tp2 && tp2.I == row - 1)
            {
                var c = ((SolidColorBrush)Application.Current.Resources["MagiPrimaryBrush"]).Color;
                cell.Background = new SolidColorBrush(Color.FromArgb(31, c.R, c.G, c.B));
            }
            cell.Opacity = 1.0;
        }

        void UpdateDataCell(int row, int col, int i, int j, int k)
        {
            var cell = _rows[row].Cells[col];
            cell.IsHeader = false;
            cell.I = i;
            cell.J = j;
            cell.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
            cell.MinWidth = 32;
            cell.TextAlignment = TextAlignment.Center;
            cell.Tooltip = null;
            var sym = k >= 0 && k < ui.ShiftSymbols.Count ? ui.ShiftSymbols[k] : k.ToString();
            // [2026-09-02, 配線] ui.ShiftColorHex/ShiftTextHex は常に解決済みの色を持つ（既定パレット
            // または SettingsView の色設定で保存したもの）。従来はここで一切読んでおらず、色設定を
            // 変更しても勤務表グリッドの見た目が変わらない「論理的な箱」だった。
            var bg = k >= 0 && k < ui.ShiftColorHex.Count ? ParseHexColor(ui.ShiftColorHex[k], Colors.Transparent) : Colors.Transparent;
            var fg = k >= 0 && k < ui.ShiftTextHex.Count ? ParseHexColor(ui.ShiftTextHex[k], Colors.Black) : Colors.Black;
            cell.Text = sym;
            cell.Foreground = new SolidColorBrush(fg);
            cell.Background = new SolidColorBrush(bg);

            if (ui.Wishes.TryGetValue($"{i},{j}", out var wishK))
            {
                var reflected = wishK == k;
                cell.WishDotVisibility = Visibility.Visible;
                cell.WishDotColor = new SolidColorBrush(reflected ? Colors.SeaGreen : Colors.HotPink);
            }
            else
            {
                cell.WishDotVisibility = Visibility.Collapsed;
            }

            // [2026-09-11/Kotlin原本FlatCell準拠] 違反の無いセルは既定で無枠（plainBorder既定false）。
            // 枠は必須違反(赤・実線2dp)/要調整(橙・実線2dp)/フォーカス(主色・3dp)のときだけ出す。
            Brush borderBrush = new SolidColorBrush(Colors.Transparent);
            var thickness = new Thickness(0);
            var vioClass = VioBuckets.VisibleCellVio(ui, $"{i},{j}", _vioEnabled);
            if (vioClass is not null)
            {
                borderBrush = ResolveVioBrush(ui, vioClass);
                thickness = new Thickness(2);
            }
            // [集中モード] 違反・未反映希望・注目セル以外を淡色に沈める（非表示にはしない＝被覆の文脈は残す）。
            var unreflectedWish = ui.Wishes.TryGetValue($"{i},{j}", out var wk0) && wk0 != k;
            var cellFocused = _focusCell is { } fc0 && fc0.I == i && fc0.J == j;
            var dimmed = _focusMode && vioClass is null && !unreflectedWish && !cellFocused;
            // [違反箇所へのジャンプ] 注目セルは一時的に主色の太枠へ差し替える（FocusCell 参照。
            // Kotlin原本は cs.primary、旧実装のDodgerBlueは原本に無い独自色だったため差し替え）。
            var isFocused = _focusCell is { } fc && fc.I == i && fc.J == j;
            if (isFocused)
            {
                borderBrush = (Brush)Application.Current.Resources["MagiPrimaryBrush"];
                thickness = new Thickness(3);
            }
            cell.BorderBrush = borderBrush;
            cell.BorderThickness = thickness;
            cell.Opacity = dimmed ? 0.35 : (ui.Running ? 0.5 : 1.0);
        }

        UpdateHeaderCell(0, 0, "");
        for (var j = 0; j < dayCount; j++) UpdateHeaderCell(0, j + 1, $"{j + 1}");

        for (var i = 0; i < staffCount; i++)
        {
            var name = i < ui.StaffNames.Count ? ui.StaffNames[i] : $"#{i}";
            UpdateHeaderCell(i + 1, 0, name);
            var row = ui.Schedule[i];
            for (var j = 0; j < row.Count; j++) UpdateDataCell(i + 1, j + 1, i, j, row[j]);
        }
    }

    /// <summary>[2026-09-11, ItemsView化] 実体化しているセル要素を(行,列)で引けるように、
    /// 各セルの<c>Border</c>が読み込み/破棄されるたびに自分で登録/解除する（<see cref="ItemsRepeater"/>
    /// はコンテナを使い回すため、固定の参照を持たずこの方式で最新の実体を追う）。</summary>
    private void OnScheduleCellLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Border { DataContext: ScheduleCellVm vm } border) return;
        _cellElements[(vm.Row, vm.Col)] = border;
        if (vm.Row == 0 && vm.Col == 0) _nameHeaderWidth = border;
        if (_focusCell is { } fc && ((fc.I < 0 && vm.Row == 0 && fc.J == vm.Col - 1) || (fc.I >= 0 && vm.Row == fc.I + 1 && vm.Col == fc.J + 1)))
            _focusCellElement = border;
    }

    private void OnScheduleCellUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Border { DataContext: ScheduleCellVm vm } border) return;
        var key = (vm.Row, vm.Col);
        if (_cellElements.TryGetValue(key, out var existing) && ReferenceEquals(existing, border)) _cellElements.Remove(key);
        if (ReferenceEquals(_focusCellElement, border)) _focusCellElement = null;
        if (ReferenceEquals(_nameHeaderWidth, border)) _nameHeaderWidth = null;
    }

    private static Rectangle? PressOverlayOf(Border cell) =>
        cell.Child is Grid { Children.Count: > 0 } g && g.Children[^1] is Rectangle r ? r : null;

    private void OnScheduleCellPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is Border { DataContext: ScheduleCellVm { IsDataCell: true } } border && !_vm.Ui.Running)
            if (PressOverlayOf(border) is { } overlay) overlay.Visibility = Visibility.Visible;
    }

    private void OnScheduleCellPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is Border border && PressOverlayOf(border) is { } overlay) overlay.Visibility = Visibility.Collapsed;
    }

    private void OnScheduleCellTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (sender is not Border { DataContext: ScheduleCellVm { IsDataCell: true } vm } border) return;
        if (_vm.Ui.Running) return;
        MarkTapped(vm.I, vm.J);
        ShowCellEditor(border, vm.I, vm.J);
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
                var (si, sk, sc, sv) = (i, k, count, vioClass);
                Action<FrameworkElement>? onClick = vioClass is null ? null : _ => ShowStaffTallyDetail(ui, si, sk, sc, sv!);
                AddTallyCell(StaffTallyGridHost, i + 1, k + 1, count.ToString(), header: false, borderBrush: brush, thickness: thickness, onClick: onClick);
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
                // 不足は既存の候補フライアウト（動かせる人を 1 タップで割当）、過剰は内訳ダイアログ（希望固定の名指し・取消）。
                var (dk, dj, dc) = (k, j, count);
                Action<FrameworkElement>? onClick = vioClass == "vio-covU"
                    ? anchor => ShowShortageFixFlyout(anchor, dj, dk)
                    : vioClass == "vio-covO" ? _ => ShowDayTallyDetail(ui, dk, dj, dc) : null;
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
            FontSize = 14,
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
    /// <summary>[phase9 #11] 職員別セル(i,k)の内訳（Kotlin原本 <c>staffViolDetail</c>）: 現在回数と 下限/上限/目標 の差を数字で。</summary>
    private void ShowStaffTallyDetail(UiState ui, int i, int k, int count, string vio)
    {
        var (lo, hi, apt) = _vm.StaffCellLimits(i, k);
        var name = i < ui.StaffNames.Count ? ui.StaffNames[i] : i.ToString();
        var sym = k < ui.ShiftSymbols.Count ? ui.ShiftSymbols[k] : k.ToString();
        var lines = new List<string> { $"現在 {count}回" };
        switch (vio)
        {
            case "vio-low" when lo is { } l: lines.Add($"下限 {l}回 → {Math.Max(0, l - count)}回 不足"); break;
            case "vio-high" when hi is { } h: lines.Add($"上限 {h}回 → {Math.Max(0, count - h)}回 超過"); break;
            case "vio-aptLow" when apt is { } a: lines.Add($"目標 {a}回 → {Math.Max(0, a - count)}回 不足"); break;
            case "vio-aptHigh" when apt is { } a: lines.Add($"目標 {a}回 → {Math.Max(0, count - a)}回 超過"); break;
        }
        _ = ShowTallyDetailAsync($"{name} ・ {sym}", lines, focusStaff: i, shift: k, day: null, pinned: Array.Empty<int>());
    }

    /// <summary>[phase9 #11 → Android 3.515.2 同期] 日別セル(k,j)の内訳（Kotlin原本 <c>dayViolDetail</c>、過剰のとき）:
    /// 現在人数と適正の差、その枠の在勤者全員（希望で固定している人は注記）。旧実装は希望で固定している在勤者
    /// 「だけ」を名指ししていた（実機報告「誰と誰がA4の設定になっているか表示されない」）。</summary>
    private void ShowDayTallyDetail(UiState ui, int k, int j, int count)
    {
        var sym = k < ui.ShiftSymbols.Count ? ui.ShiftSymbols[k] : k.ToString();
        var lines = new List<string> { $"現在 {count}人" };
        if (_vm.NeedCellLimits(k, j) is { } lim) lines.Add($"適正 {lim.Hi}人 → {Math.Max(0, count - lim.Hi)}人 過剰");
        // 在勤者＝現在その枠に配置されている全員。希望で固定＝配置済みかつ希望一致（CoverageDiag と同じ判定）。
        var assigned = new List<int>();
        for (var i = 0; i < ui.Schedule.Count; i++)
            if (j < ui.Schedule[i].Count && ui.Schedule[i][j] == k) assigned.Add(i);
        var pinned = assigned.Where(i => ui.Wishes.TryGetValue($"{i},{j}", out var w) && w == k).ToList();
        if (assigned.Count > 0)
        {
            lines.Add("在勤: " + string.Join("・", assigned.Select(i =>
            {
                var nm = i < ui.StaffNames.Count ? ui.StaffNames[i] : $"#{i}";
                return pinned.Contains(i) ? $"{nm}（希望固定）" : nm;
            })));
        }
        if (pinned.Count > 0)
        {
            lines.Add("希望で固定: " + string.Join("・", pinned.Select(i => i < ui.StaffNames.Count ? ui.StaffNames[i] : $"#{i}")) +
                "（必須の希望どうしが同じ日に重なり、どちらかの希望を取り消さない限り過剰は残ります）");
        }
        _ = ShowTallyDetailAsync($"{sym} ・ {j + 1}日", lines, focusStaff: null, shift: k, day: j, pinned: pinned, assigned: assigned);
    }

    private async Task ShowTallyDetailAsync(string title, IReadOnlyList<string> lines, int? focusStaff, int shift, int? day, IReadOnlyList<int> pinned, IReadOnlyList<int>? assigned = null)
    {
        var ui = _vm.Ui;
        var panel = new StackPanel { Spacing = 4 };
        foreach (var l in lines) panel.Children.Add(new TextBlock { Text = l, TextWrapping = TextWrapping.Wrap });
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = title, Content = panel,
            PrimaryButtonText = "直し方を探す", CloseButtonText = "閉じる", DefaultButton = ContentDialogButton.Close,
        };
        if (day is { } dj)
        {
            foreach (var i in pinned)
            {
                var name = i < ui.StaffNames.Count ? ui.StaffNames[i] : $"#{i}";
                var cancel = new Button
                {
                    Content = $"{name} の希望を取り消す", HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 48,
                    Foreground = (Brush)Application.Current.Resources["MagiErrorBrush"], IsEnabled = !ui.Running,
                };
                var staff = i;
                cancel.Click += (_, _) => { dialog.Hide(); _vm.RemoveWish(staff, dj); };
                panel.Children.Add(cancel);
            }
            // [Android 3.515.2 同期] 希望で固定していない在勤者にも1人にしぼった「直し方を探す」への導線を足す。
            foreach (var i in (assigned ?? Array.Empty<int>()).Where(i => !pinned.Contains(i)))
            {
                var name = i < ui.StaffNames.Count ? ui.StaffNames[i] : $"#{i}";
                var fix = new Button
                {
                    Content = $"{name} の直し方を探す", HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 48,
                    IsEnabled = !ui.Running,
                };
                var staff = i;
                fix.Click += (_, _) => { dialog.Hide(); _vm.FindFixSuggestions(staff, shift); _goAnalysis?.Invoke(); };
                panel.Children.Add(fix);
            }
        }
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            _vm.FindFixSuggestions(focusStaff, shift);
            _goAnalysis?.Invoke();
        }
    }

    private void ShowShortageFixFlyout(FrameworkElement anchor, int day, int shift)
    {
        // [2026-09-02, 配線] EditBlockedNow。ShowCellEditor と同じ理由＝ SetCell を呼ぶ入口はここも同じで、
        // 旧実装はガード自体が無く実行中でも候補が出て SetCell が黙って弾かれるだけだった。
        if (_vm.EditBlockedNow()) return;
        var candidates = _vm.ShortageFixCandidates(day, shift);
        // [2026-09-10, ユーザー報告「メニューが出ない」] ContentDialog はこのファイル/他View含め全箇所で
        //   XamlRoot を明示設定している（MainWindow.xaml.cs:141 等）のに、この2つの MenuFlyout だけ未設定
        //   だった。ShowAt(placementTarget) は理論上 placementTarget の XamlRoot を自動継承するはずだが、
        //   実機でメニューが出ない報告と符合するため、他の popup と同じ明示設定へ揃える（安全な追加のみ）。
        var flyout = new MenuFlyout { XamlRoot = anchor.XamlRoot };
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
        // [2026-09-10, ユーザー報告「メニューが出ない」] ShowShortageFixFlyout と同じ理由でXamlRootを明示。
        var flyout = new MenuFlyout { XamlRoot = anchor.XamlRoot };
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
