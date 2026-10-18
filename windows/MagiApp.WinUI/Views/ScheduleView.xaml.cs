using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
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
/// [元に戻す/やり直す] シェル（MainWindow）のボタンに一本化（3.702.0 G2-9）。シートの［↶］は残す。
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

    /// <summary>勤務表タブの状態の 1 行（Kotlin <c>ScheduleStateRow</c>、G2-1）。必須違反は赤、巡回の先頭へ行けるときだけ押せる。</summary>
    private void RenderStateLine(UiState ui)
    {
        if (!ui.Loaded)
        {
            StateLineButton.Visibility = Visibility.Collapsed;
            return;
        }
        var first = _vm.HardViolationItemsFor(LabelOf).FirstOrDefault();
        var soft = ui.Display is { } d && d.FreshFor(ui) ? d.SoftText : AnalysisTriage.UnitTotals(ui.Breakdown, MirrorKeys.Soft) ?? "なし";
        var next = first is null ? null
            : $"{DayMD(ui.StartDate, first.Days[0])} {(first.Staff < ui.StaffNames.Count ? ui.StaffNames[first.Staff] : $"職員{first.Staff + 1}")}";
        var line = CellSheetLogic.ScheduleStateLineOf(ui.HasResult, ui.Running, ui.BestHard, DisplaySnapshots.DisplayHardText(ui), soft, next);
        StateLineButton.Visibility = Visibility.Visible;
        StateLineButton.IsHitTestVisible = line.TourFirst;
        StateLineText.Text = line.Text;
        StateLineText.Foreground = (Brush)Application.Current.Resources[line.Hard ? "MagiErrorBrush" : "MagiOnSurfaceVariantBrush"];
    }

    private void OnStateLineClick(object sender, RoutedEventArgs e)
    {
        if (_vm.HardViolationItemsFor(LabelOf).FirstOrDefault() is { } t) OpenCellSheet(t.Cell.I, t.Cell.J);
    }

    /// <summary>期間開始日 + j → "M/D"（Kotlin <c>dayMD</c>）。解析失敗時は "(j+1)日"。</summary>
    private static string DayMD(string startDate, int j) =>
        DateTime.TryParse(startDate, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d)
            ? $"{d.AddDays(j).Month}/{d.AddDays(j).Day}" : $"{j + 1}日";

    /// <summary>[phase9 #8] 検索・凡例の開閉（既定は閉）と職員名の検索語。</summary>
    private bool _searchLegendOpen;
    private string _nameQuery = "";


    /// <summary>手が見つからなかったときの次の一歩（編集タブへの着地と、セルから来たときの「元の確認へ戻る」の呼出元。Kotlin <c>FixNav</c>、3.646.0 L01/L03）。</summary>
    private readonly Action<EditLanding?, EditReturn?>? _goEditLanding;

    /// <summary>[S6] セルシートの［緩める候補を見る］＝ホームの RelaxTrialDialog を開く（同じ組・同じ確定）。確定の後は渡したセルへ戻る。</summary>
    private readonly Action<(int I, int J)?>? _showRelax;

    /// <summary>反映の欄の［残っている問題を見る］で、巡回できる必須違反が無いときの行き先（分析タブ）。</summary>
    private readonly Action? _goAnalysis;

    public ScheduleView(MagiViewModel vm, Action? goAnalysis = null, Action<EditLanding?, EditReturn?>? goEditLanding = null, Action<(int I, int J)?>? showRelax = null)
    {
        _vm = vm;
        _goAnalysis = goAnalysis;
        _goEditLanding = goEditLanding;
        _showRelax = showRelax;
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

    /// <summary>被覆キーごとの重なった全クラス（<c>NeedViolations</c> は最重 1 クラスだけ＝同じ重みの族に隠れた covO を取りこぼす）。</summary>
    private static IEnumerable<(string Key, IReadOnlyList<string> Classes)> NeedClassesAll(UiState ui) =>
        ui.NeedFamilies.Count > 0
            ? ui.NeedFamilies.Select(kv => (kv.Key, kv.Value))
            : ui.NeedViolations.Select(kv => (kv.Key, (IReadOnlyList<string>)new[] { kv.Value }));

    private static string? FirstVisible(IReadOnlyDictionary<string, IReadOnlyList<string>> fams, IReadOnlyDictionary<string, string> heaviest, string key, IReadOnlySet<string> enabled)
    {
        IReadOnlyList<string> classes = fams.TryGetValue(key, out var f) ? f : heaviest.TryGetValue(key, out var one) ? new[] { one } : Array.Empty<string>();
        return classes.FirstOrDefault(c => VioBuckets.VioVisible(c, enabled));
    }

    private List<int> VioDays(UiState ui)
    {
        var days = new SortedSet<int>();
        foreach (var key in ui.ViolationCells.Keys)
        {
            if (VioBuckets.VisibleCellVio(ui, key, _vioEnabled) is not null && int.TryParse(key[(key.IndexOf(',') + 1)..], out var j)) days.Add(j);
        }
        foreach (var (key, fams) in NeedClassesAll(ui))
        {
            if (fams.Any(c => VioBuckets.VioVisible(c, _vioEnabled)) && int.TryParse(key[(key.IndexOf(',') + 1)..], out var j)) days.Add(j);
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
        FilterTitle.Text = $"要確認 {loc}か所";
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
            ViolationLegendHost.Children.Add(LegendItem(new Border { Width = 22, Height = 16, BorderBrush = hard, BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(4) }, "実線の枠＝必須"));
            ViolationLegendHost.Children.Add(LegendItem(new Rectangle { Width = 22, Height = 16, Stroke = soft, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 3, 2 }, RadiusX = 4, RadiusY = 4 }, "破線の枠＝要調整（重）"));
            var cornerSample = new Grid { Width = 22, Height = 16 };
            cornerSample.Children.Add(new Polygon { Points = new PointCollection { new Windows.Foundation.Point(0, 0), new Windows.Foundation.Point(8, 0), new Windows.Foundation.Point(8, 8) }, Fill = soft, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top });
            ViolationLegendHost.Children.Add(LegendItem(cornerSample, "右上の角＝要調整（軽）"));
            ViolationLegendHost.Children.Add(LegendItem(new Ellipse { Width = 8, Height = 8, Fill = soft }, "左上の点＝ほかの種類も重なっている"));
            ViolationLegendHost.Children.Add(new TextBlock { MaxWidth = 360, Text = "名前の横の ▼▲＝回数の不足・超過／日付の下の「休▲」＝そのシフトの人員不足▼・過剰▲（タップで内訳）", TextWrapping = TextWrapping.Wrap });
            ViolationLegendHost.Children.Add(new TextBlock { MaxWidth = 360, Text = "個人の上限0のシフトが入った日は破線の枠（要調整）／人員・グループの上限0や適切回数0のシフトが入った日は右上の角", TextWrapping = TextWrapping.Wrap });
            ViolationLegendHost.Children.Add(new TextBlock { MaxWidth = 360, Text = GridDisplayMarks.LegendShapeFamilies(LabelOf), TextWrapping = TextWrapping.Wrap });
            ViolationLegendHost.Children.Add(LegendItem(new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(Colors.HotPink) }, "桃ドット＝希望が未反映"));
            ViolationLegendHost.Children.Add(LegendItem(new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(Colors.SeaGreen) }, "緑ドット＝希望が反映済み"));
            ViolationLegendHost.Children.Add(LegendItem(new FontIcon { Glyph = "\uE72E", FontSize = 9 }, "左下の錠＝手動固定（自動では変更しません。周囲のセルを変えて解消できる場合はあります）"));
            // [Android 3.651.0/凡例の抜け] 拡張希望の×はセルにだけあり、凡例に無かった。
            ViolationLegendHost.Children.Add(LegendItem(ExtCrossSwatch(), "上端の×＝拡張希望（この日はこのシフト以外）。禁止のシフトが入ると違反の色になります。"));
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

    /// <summary>拡張希望の×の見本（セルのテンプレートと同じ線の×）。</summary>
    private static Microsoft.UI.Xaml.Shapes.Path ExtCrossSwatch()
    {
        var g = new GeometryGroup();
        g.Children.Add(new LineGeometry { StartPoint = new Windows.Foundation.Point(1, 1), EndPoint = new Windows.Foundation.Point(5, 5) });
        g.Children.Add(new LineGeometry { StartPoint = new Windows.Foundation.Point(5, 1), EndPoint = new Windows.Foundation.Point(1, 5) });
        return new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = g, Width = 6, Height = 6, StrokeThickness = 1.6,
            Stroke = (Brush)Application.Current.Resources["MagiOnSurfaceVariantBrush"],
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        };
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

    /// <summary>左端に見えている日から現在週を求める（自由スクロールにも追従）。右端まで来ていれば最終週。</summary>
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
        var atEnd = GridScroll.ScrollableWidth > 0 && !CanScrollForward();
        return ScheduleNav.CurrentWeekIndex(_weeks, d, atEnd);
    }

    private bool CanScrollForward() => GridScroll.HorizontalOffset < GridScroll.ScrollableWidth - 1;

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
        var hasVio = _vioDays.Count > 0 && !_hideVioNav;
        PrevVioButton.Visibility = hasVio ? Visibility.Visible : Visibility.Collapsed;
        NextVioButton.Visibility = hasVio ? Visibility.Visible : Visibility.Collapsed;
        UpdateNavLabel(ui);
        // グリッドは作り直した直後で幅が未確定なので、レイアウト後にもう一度ラベルを出す。
        DispatcherQueue.TryEnqueue(() => UpdateNavLabel(_vm.Ui));
    }

    private bool _hideVioNav;

    private void SetVioNavHidden(bool hide)
    {
        _hideVioNav = hide;
        var v = _vioDays.Count > 0 && !hide ? Visibility.Visible : Visibility.Collapsed;
        PrevVioButton.Visibility = v;
        NextVioButton.Visibility = v;
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
        var vioLabel = _vioDays.Count == 0 ? "" : $"違反 {(_navIdx < 0 ? "–" : (_navIdx + 1).ToString())}/{_vioDays.Count}";
        NavLabel.Text = string.Join(" ・ ", new[] { weekLabel, vioLabel }.Where(t => t.Length > 0));
        PrevWeekButton.IsEnabled = cur > 0;
        NextWeekButton.IsEnabled = cur < _weeks.Count - 1 && CanScrollForward();
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
            PrimaryButtonText = "確定する",
            CloseButtonText = "閉じる",
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
            var daysText = BulkDaysText(ui.StartDate, days, scopeCombo.SelectedIndex != 1);
            previewText.Text = cellCount > 0
                ? $"{eligible.Count}名 × {days.Count}日 = {cellCount}マス" + (daysText.Length > 0 ? $"（{daysText}）" : "") + $"へ一括設定します{skipNote}。既存の割当は上書きします。"
                : days.Count == 0 ? "この期間に対象の日がありません。別の曜日を選んでください。"
                : $"担当できる職員がいません{skipNote}。別のシフトを選んでください。";
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

    /// <summary>一括操作の対象日（期間全体は範囲「10/1〜10/31」、曜日は日付を並べる「10/6・10/13」）。Kotlin <c>bulkDaysText</c>。</summary>
    private static string BulkDaysText(string startDate, IReadOnlyList<int> days, bool wholePeriod) =>
        days.Count == 0 ? ""
        : wholePeriod ? DayText.Range(startDate, days[0], days[^1])
        : string.Join("・", days.Select(j => DayText.Short(startDate, j)));

    /// <summary>[Android 3.623.0] 必須違反の一覧（Kotlin HardListChip のシート）。行を押すと閉じて、巡回と同じ移動（FocusCell→そのセルのシート）。</summary>
    private async void OnHardListClick(object sender, RoutedEventArgs e)
    {
        var ui = _vm.Ui;
        var items = _vm.HardViolationItemsFor(LabelOf);
        var selfKeys = ui.WishSelfConflicts.SelectMany(g => g.WishKeys).ToHashSet();
        var panel = new StackPanel { Spacing = 8 };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = CellSheetLogic.HardListTitle(DisplaySnapshots.DisplayHardText(ui)),
            Content = new ScrollViewer { Content = panel, MaxHeight = 480 },
            CloseButtonText = "閉じる",
            DefaultButton = ContentDialogButton.Close,
        };
        var rows = CellSheetLogic.HardViolationRows(items, ui.StaffNames, selfKeys, LabelOf);
        if (rows.Count > 0)
        {
            var start = new Button { Content = CellSheetLogic.HardListStartButton, HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 48, Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
            var firstCell = items[rows[0].TourAt].Cell;
            start.Click += (_, _) => { dialog.Hide(); OpenCellSheet(firstCell.I, firstCell.J); };
            panel.Children.Add(start);
        }
        foreach (var r in rows)
        {
            var content = new Grid { ColumnSpacing = 8 };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            // [3.714.0] 1 件 1 行（族ごとの型、18 字まで）。
            content.Children.Add(new TextBlock { Text = r.Line ?? r.Heading, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });
            if (r.WishOnly)
            {
                var chip = AnalysisView.TagChip(NextActionGuide.WishOnlyTag, MagiAccent.Orange);
                Grid.SetColumn(chip, 1);
                content.Children.Add(chip);
            }
            var row = new Button { Content = content, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, MinHeight = 48 };
            var cell = items[r.TourAt].Cell;
            row.Click += (_, _) =>
            {
                dialog.Hide();
                OpenCellSheet(cell.I, cell.J);
            };
            panel.Children.Add(row);
        }
        if (CellSheetLogic.TourCovULine(ui.Breakdown.GetValueOrDefault("covU")) is { } covLine)
            panel.Children.Add(new TextBlock { Text = covLine, FontSize = 12, Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
        await dialog.ShowAsync();
    }

    private void Render()
    {
        var ui = _vm.Ui;
        StatusText.Text = ui.Loaded
            ? $"{ui.Message}（必須={ui.BestHard} 合計={ui.BestSoft}・{ui.Staff}名×{ui.Days}日×{ui.Shifts}シフト）"
            : ui.Message ?? "読込中…";
        // [まとめて割当] SetCell と同じ二重防御——EditBlockedNow が最終防御、ここは押せるのに拒否されるだけの
        // ボタンを見せないための表示上の抑止（EditView/HomeView と同じ方針）。
        BulkAssignButton.IsEnabled = ui.Loaded && ui.Schedule.Count > 0 && !ui.Running;
        // [Android 3.623.0] 「必須 N ▼」（Kotlin HardListChip）＝結果があり実行中でなく必須違反が残るときだけ。
        HardListButton.Visibility = !ui.Running && ui.HasResult && ui.BestHard > 0 ? Visibility.Visible : Visibility.Collapsed;
        HardListButton.Content = $"必須 {DisplaySnapshots.DisplayHardText(ui)} ▼";
        RenderFilterBar(ui);
        RenderStateLine(ui);
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
        // 希望の丸は実現できる希望（担当できるシフト）だけ＝チェッカーの pref と同じ（Android の勤務表と同じ）。
        var allowedByStaff = new Dictionary<int, int[]>();
        bool CanDo(int i, int k)
        {
            if (!allowedByStaff.TryGetValue(i, out var a)) allowedByStaff[i] = a = _vm.AllowedShiftsFor(i);
            return a.Contains(k);
        }
        DateOnly? startDay = DateOnly.TryParseExact(ui.StartDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var sd) ? sd : null;
        var today = DateOnly.FromDateTime(DateTime.Today);
        // [phase9 #10] 日別の不足人数「▼N」（covU 由来なので「人員」バケツ ON のときだけ）。
        // 人員の印はシフトを名指す（「休▲」）。旧「▼N」は人数だけでどのシフトかが読めなかった（Kotlin DayHeader と同じ）。
        _covMarks = GridDisplayMarks.CoverageHeaderMarks(ui, _vioEnabled, dayCount);
        _countBadges = GridDisplayMarks.CountBadges(ui, _vioEnabled);
        var c1Marks = GridDisplayMarks.C1DisplayMarks(ui);
        var c1Band = GridDisplayMarks.C1Band(ui, _vioEnabled, staffCount, dayCount);
        _c1Stuck = GridDisplayMarks.C1Stuck(ui);

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
            cell.SecondDotVisibility = Visibility.Collapsed;
            cell.BandVisibility = Visibility.Collapsed;
            cell.PinVisibility = Visibility.Collapsed;
            cell.ExtVisibility = Visibility.Collapsed;
            // 回数の族（下限・上限・適切回数・個人の合計）はセルを持たないので名前の横に小さく ▼/▲（Kotlin と同じ）。
            if (col == 0 && row > 0 && _countBadges.TryGetValue(row - 1, out var badge))
            {
                cell.Text = $"{text} {badge.Glyph}";
                cell.Tooltip = "回数・偏り: タップで内訳";
            }
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
                var marks = _covMarks[col - 1];
                if (marks.Count > 0)
                {
                    cell.Text += "\n" + GridDisplayMarks.CoverageHeaderLabel(ui, marks);
                    if (!isToday) cell.Foreground = marks[0].Under ? (Brush)Application.Current.Resources["MagiErrorBrush"] : new SolidColorBrush(DefaultSoftVioColor);
                    cell.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
                    cell.Tooltip = string.Join("\n", GridDisplayMarks.DayCoverageLines(ui, col - 1, marks, LabelOf)) + "\nタップで内訳";
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
            cell.DashVisibility = Visibility.Collapsed;
            cell.CornerVisibility = Visibility.Collapsed;
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

            if (ui.Wishes.TryGetValue($"{i},{j}", out var wishK) && CanDo(i, wishK))
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
            // 枠は必須違反(実線3dp)/フォーカス(主色・3dp)のときだけ出す。要調整は破線の枠か右上の角（G2-8）。
            Brush borderBrush = new SolidColorBrush(Colors.Transparent);
            var thickness = new Thickness(0);
            var displayClasses = GridDisplayMarks.DisplayCellClasses(ui, $"{i},{j}", c1Marks);
            cell.PinVisibility = ui.ManualPins.Contains($"{i},{j}") ? Visibility.Visible : Visibility.Collapsed;
            if (ui.ExtBanned.TryGetValue($"{i},{j}", out var extBans))
            {
                cell.ExtVisibility = Visibility.Visible;
                cell.ExtBrush = extBans.Contains(k) ? (Brush)Application.Current.Resources["MagiErrorBrush"] : (Brush)Application.Current.Resources["MagiOnSurfaceVariantBrush"];
            }
            else cell.ExtVisibility = Visibility.Collapsed;
            cell.BandVisibility = i < c1Band.Length && j < c1Band[i].Length && c1Band[i][j] ? Visibility.Visible : Visibility.Collapsed;
            var vioClass = displayClasses.FirstOrDefault(c => VioBuckets.VioVisible(c, _vioEnabled));
            var second = GridDisplayMarks.SecondVisibleClass(displayClasses, _vioEnabled);
            cell.SecondDotVisibility = second is null ? Visibility.Collapsed : Visibility.Visible;
            if (second is not null) cell.SecondDotColor = ResolveVioBrush(ui, second);
            var vioBrush = vioClass is null ? null : ResolveVioBrush(ui, vioClass);
            var heavySoft = vioClass is not null && !GridDisplayMarks.IsHardCellViolation(vioClass) && GridDisplayMarks.IsHeavySoftCellViolation(vioClass);
            var lightSoft = vioClass is not null && !GridDisplayMarks.IsHardCellViolation(vioClass) && !heavySoft;
            if (vioBrush is not null && GridDisplayMarks.IsHardCellViolation(vioClass))
            {
                borderBrush = vioBrush;
                thickness = new Thickness(3);
            }
            cell.DashVisibility = heavySoft ? Visibility.Visible : Visibility.Collapsed;
            cell.CornerVisibility = lightSoft ? Visibility.Visible : Visibility.Collapsed;
            if (vioBrush is not null) { cell.DashBrush = vioBrush; cell.CornerBrush = vioBrush; }
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
        if (sender is Border { DataContext: ScheduleCellVm { IsHeader: true } hv }) { ShowHeaderMarkDialog(hv); return; }
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
        TallyLegend.Text = CellSheetLogic.TallyLegendText;
        StaffTallyGridHost.Children.Clear();
        StaffTallyGridHost.RowDefinitions.Clear();
        StaffTallyGridHost.ColumnDefinitions.Clear();
        if (!ui.Loaded || ui.Schedule.Count == 0) return;

        var staffCount = ui.Schedule.Count;
        var shiftCount = ui.ShiftSymbols.Count;
        for (var r = 0; r <= staffCount + 1; r++) StaffTallyGridHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
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
                var vioClass = FirstVisible(ui.CountFamilies, ui.CountViolations, $"{i},{k}", _vioEnabled);
                var brush = VioBorderBrush(ui, vioClass, out var thickness);
                var (si, sk, sc, sv) = (i, k, count, vioClass);
                Action<FrameworkElement>? onClick = vioClass is null ? null : _ => ShowStaffTallyDetail(ui, si, sk, sc, sv!);
                AddTallyCell(StaffTallyGridHost, i + 1, k + 1, count.ToString(), header: false, borderBrush: brush, thickness: thickness, onClick: onClick);
            }
        }
        // 「計（期間）」: そのシフトに人員不足▼・過剰▲の日があれば日数を添え、タップで日の一覧（Kotlin TallyCard と同じ）。
        var totals = FixSearchText.ShiftCoverageTotals(GridDisplayMarks.CoverageHeaderMarks(ui, _vioEnabled, ui.Schedule[0].Count));
        AddTallyCell(StaffTallyGridHost, staffCount + 1, 0, "計（期間）", header: true);
        for (var k = 0; k < shiftCount; k++)
        {
            var total = ui.Schedule.Sum(r => r.Count(v => v == k));
            if (!totals.TryGetValue(k, out var ct))
            {
                AddTallyCell(StaffTallyGridHost, staffCount + 1, k + 1, total.ToString(), header: false);
                continue;
            }
            var kk = k;
            var brush = VioBorderBrush(ui, ct.UnderDays.Count > 0 ? "vio-covU" : "vio-covO", out var thickness);
            AddTallyCell(StaffTallyGridHost, staffCount + 1, k + 1, $"{total} {ct.Glyph}", header: false, borderBrush: brush, thickness: thickness,
                onClick: _ => ShowShiftTotalDialog(kk, ct));
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
                var vioClass = FirstVisible(ui.NeedFamilies, ui.NeedViolations, $"{k},{j}", _vioEnabled);
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
        // 先頭に要点（基準までの差 → 現在と基準 → 集計期間）。氏名とシフトは題にある。
        var lines = CellSheetLogic.CountFixTarget(new Dictionary<string, string> { [$"{i},{k}"] = vio }, i, k, (lo, hi, apt)) is { } t
            ? CellSheetLogic.FixTargetHead(t, count).ToList()
            : new List<string> { vio == "vio-c2" ? $"{AnalysisView.BreakdownLabels["c2"]}の目標に足りません（現在 {count}回）" : $"現在 {count}回" };
        if (CellSheetLogic.PeriodLine(ui.StartDate, ui.Days) is { } period) lines.Add(period);
        _ = ShowTallyDetailAsync($"{name}さんの {sym}", lines, focusStaff: i, shift: k, day: null, pinned: Array.Empty<int>());
    }

    /// <summary>[phase9 #11 → Android 3.515.2 同期] 日別セル(k,j)の内訳（Kotlin原本 <c>dayViolDetail</c>、過剰のとき）:
    /// 現在人数と適正の差、その枠の在勤者全員（希望で固定している人は注記）。旧実装は希望で固定している在勤者
    /// 「だけ」を名指ししていた（実機報告「誰と誰がA4の設定になっているか表示されない」）。</summary>
    private void ShowDayTallyDetail(UiState ui, int k, int j, int count)
    {
        var sym = k < ui.ShiftSymbols.Count ? ui.ShiftSymbols[k] : k.ToString();
        // 先頭に要点（基準との差 → 配置と基準）。日は題にある。開くのは人員過剰の枠だけ（不足は候補のフライアウト）。
        var lines = CellSheetLogic.NeedFixTarget(new Dictionary<string, IReadOnlyList<string>> { [$"{k},{j}"] = new[] { "vio-covO" } }, k, j, _vm.NeedCellLimits(k, j)) is { } t
            ? CellSheetLogic.FixTargetHead(t, count).ToList()
            : new List<string> { $"{sym} 現在 {count}人" };
        // 在勤者＝現在その枠に配置されている全員。希望で固定＝配置済みかつ希望一致（CoverageDiag と同じ判定）。
        var assigned = new List<int>();
        for (var i = 0; i < ui.Schedule.Count; i++)
            if (j < ui.Schedule[i].Count && ui.Schedule[i][j] == k) assigned.Add(i);
        var pinned = assigned.Where(i => ui.Wishes.TryGetValue($"{i},{j}", out var w) && w == k).ToList();
        if (assigned.Count > 0)
        {
            lines.Add($"{sym}の担当: " + string.Join("・", assigned.Select(i =>
            {
                var nm = i < ui.StaffNames.Count ? ui.StaffNames[i] : $"#{i}";
                return pinned.Contains(i) ? $"{nm}さん（本人の希望）" : $"{nm}さん";
            })));
        }
        if (pinned.Count > 0)
        {
            // 希望で固定した人数が適正人数を超えるときだけ「希望を調整しない限り残る」と言う（以下なら固定していない人を動かせば減る）。
            var overByWish = _vm.NeedCellLimits(k, j) is { } lim2 && pinned.Count > lim2.Hi;
            lines.Add("本人の希望: " + string.Join("・", pinned.Select(i => $"{(i < ui.StaffNames.Count ? ui.StaffNames[i] : $"#{i}")}さん")) +
                (overByWish ? "（必須の希望どうしが同じ日に重なっています。本人と相談してどちらかの希望を調整しない限り、過剰は残ります。調整すると希望は外れます。元に戻すで戻せます）" : ""));
        }
        _ = ShowTallyDetailAsync($"{DayText.Full(ui.StartDate, j)}・{sym}", lines, focusStaff: null, shift: k, day: j, pinned: pinned, assigned: assigned);
    }

    private async Task ShowTallyDetailAsync(string title, IReadOnlyList<string> lines, int? focusStaff, int shift, int? day, IReadOnlyList<int> pinned, IReadOnlyList<int>? assigned = null)
    {
        var ui = _vm.Ui;
        var panel = new StackPanel { Spacing = 4 };
        foreach (var l in lines) panel.Children.Add(new TextBlock { Text = l, TextWrapping = TextWrapping.Wrap });
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = title, Content = panel,
            CloseButtonText = "閉じる", DefaultButton = ContentDialogButton.Close,
        };
        // 開いた時点でこの職員×シフト（日別は日×シフト）の変更案を探し、同じダイアログに並べる（Kotlin FixSearchPanel）。
        //   日別で職員を選んだら、同じ日・同じ勤務の問題のまま、その職員に絞って探す（日付を落とさない。別タブへ行かない）。
        int? person = null;
        var detach = new List<Action>();
        var fixHost = new StackPanel { Spacing = 4 };
        var people = new StackPanel { Spacing = 4 };
        void Detach() { foreach (var a in detach.ToList()) a(); detach.Clear(); }
        dialog.Closed += (_, _) => Detach();
        void Mount()
        {
            Detach();
            fixHost.Children.Clear();
            if (person is { } p)
                fixHost.Children.Add(new TextBlock { Text = $"{(p < _vm.Ui.StaffNames.Count ? _vm.Ui.StaffNames[p] : $"#{p}")}さんに絞って探しています", FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            fixHost.Children.Add(AttachFixSearch(dialog.Hide, a => detach.Add(a), new FixFocus(person ?? focusStaff, shift, day)));
        }
        panel.Children.Add(fixHost);
        Mount();
        if (day is { } dj)
        {
            foreach (var i in pinned)
            {
                var name = i < ui.StaffNames.Count ? ui.StaffNames[i] : $"#{i}";
                var cancel = new Button
                {
                    Content = new TextBlock { Text = $"{name}さんの希望を外す", MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis }, HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 48,
                    Foreground = (Brush)Application.Current.Resources["MagiErrorBrush"], IsEnabled = !ui.Running,
                };
                var staff = i;
                cancel.Click += (_, _) => { dialog.Hide(); _vm.RemoveWish(staff, dj); };
                panel.Children.Add(cancel);
            }
            // [Android 3.515.2 同期] 希望で固定していない在勤者にも1人にしぼった「勤務変更案を探す」への導線を足す。
            //   探している間は個人ごとのボタンを並べない（何を調べているかだけを出す＝2026-10-14 利用者レビュー）。
            void RenderPeople()
            {
                people.Children.Clear();
                foreach (var i in (_vm.Ui.FixSearching ? Array.Empty<int>() : assigned ?? Array.Empty<int>()).Where(i => !pinned.Contains(i)))
                {
                    var name = i < _vm.Ui.StaffNames.Count ? _vm.Ui.StaffNames[i] : $"#{i}";
                    if (person == i) { people.Children.Add(new TextBlock { Text = $"{name}さん（選択中）", FontSize = 13 }); continue; }
                    var fix = new Button
                    {
                        Content = new TextBlock { Text = $"{name}さんの変更案を探す", MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis }, HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 48,
                        IsEnabled = !_vm.Ui.Running,
                    };
                    var staff = i;
                    fix.Click += (_, _) => { person = staff; Mount(); RenderPeople(); };
                    people.Children.Add(fix);
                }
            }
            RenderPeople();
            panel.Children.Add(people);
            PropertyChangedEventHandler searching = (_, e) => { if (e.PropertyName is nameof(UiState.FixSearching)) DispatcherQueue.TryEnqueue(RenderPeople); };
            _vm.Ui.PropertyChanged += searching;
            dialog.Closed += (_, _) => _vm.Ui.PropertyChanged -= searching;
        }
        await dialog.ShowAsync();
    }

    /// <summary>
    /// ダイアログを開いた時点で対象の勤務変更案を探し、進み具合 → 案（見ている問題の変化を先頭に。［変更内容を確認］で確認の画面）→ 無ければ確かめた理由と次の一歩を出す。
    /// 盤面が変われば探し直し、閉じれば探索を取り消す（古い結果を書き戻さない）。Kotlin <c>FixSearchPanel</c> の移植。
    /// </summary>
    private StackPanel AttachFixSearch(ContentDialog dialog, FixFocus focus) =>
        AttachFixSearch(dialog.Hide, onClosed => dialog.Closed += (_, _) => onClosed(), focus);

    /// <param name="settingsLabel">手が無いときの設定への行き先の名（null＝節ごとの名 <see cref="SettingsLabel.For"/>）。</param>
    /// <param name="whenNoFix">手が 0 件のときに理由の下へ足す要素（板挟みの「同じ違反のもう一方のセル」へのボタン）。</param>
    /// <param name="quick">セルのシート＝締切 3 秒（手は 1 件でよい。Kotlin <c>compact</c>）。日×シフト・職員の集計のダイアログは 8 秒。</param>
    /// <param name="cell">セルのシートから来たとき、編集タブの「元の確認へ戻る」の戻り先。</param>
    /// <param name="primaryElsewhere">[3.716.0] 同じシートに塗りの主ボタンがある（案なしの「問題一覧」を文字ボタンにする＝INV-09 塗りは 1 つ）。</param>
    /// <param name="problems">セルに重なった問題の族。2 件以上なら、どの問題の直し方を見るかを選ばせ、選んだ族を減らす案だけを出す（Kotlin 3.665.0）。</param>
    private StackPanel AttachFixSearch(Action hide, Action<Action> registerClosed, FixFocus requested, string? settingsLabel = null,
        Func<IEnumerable<UIElement>>? whenNoFix = null, bool quick = false, (int I, int J)? cell = null, IReadOnlyList<string>? problems = null,
        (int I, int J)? resolveAt = null, bool reuse = false, string? initialPicked = null, bool primaryElsewhere = false)
    {
        // 開いた時点の職員を ID で覚え、並べ替え・削除のあとも同じ人を探す（統合仕様 5.1）。消えていれば探さない。
        var ids0 = _vm.Ui.StaffIds;
        int? IdAt(int? i) => i is { } v && v >= 0 && v < ids0.Count ? ids0[v] : null;
        var (pinnedStaff, pinnedExcept) = (IdAt(requested.Staff), IdAt(requested.ExceptStaff));
        FixFocus? Resolve() => CellSheetLogic.ResolveFixFocus(requested, pinnedStaff, pinnedExcept, _vm.Ui.StaffIds);
        var focus = Resolve() ?? requested;
        var picked = initialPicked;
        var reuseOnce = reuse;
        AppliedFix? foldedApplied = null;   // 「続けて … の案を見る」でたたんだ反映の帯（別の手を当てたら また出す）
        bool othersOpen = false, moveOnlyOpen = false, scopeOpen = false, candidatesOpen = false, appliedChangesOpen = false;   // 畳んだ欄の開閉（Refresh で作り直しても保つ）
        var host = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
        // 日は「他の人で補う」・人員の印・日別の職員に絞った探索で渡す（セルのシートは勤務が無い＝日は理由の読み取りにだけ使う）。
        // [3.716.0] 開いたとき・盤面が変わったときの探索は自動（auto＝記録は「案を探す（自動）」、ためらいの回数と続けて 0 件に入れない）。もう一度探すは手の操作。
        void Find(bool auto = false)
        {
            if (Resolve() is not { } f) return;
            focus = f;
            _vm.FindFixSuggestions(f.Staff, f.Shift, f.Key, f.ExceptStaff,
                f.ExceptStaff is not null || f.Staff is null || f.Shift is not null ? f.Day : null, quick, f.AllDays, auto: auto);
        }
        void Start()
        {
            if (_vm.Ui.Running) return;
            // 確認の画面から［別案を見る］で戻ったときは、閉じる前に探した案をそのまま出す（盤面は変わっていない）。
            if (reuseOnce && _vm.Ui.FixDoneKey == focus.Key && !_vm.Ui.FixSearching) { reuseOnce = false; return; }
            reuseOnce = false;
            Find(auto: true);
        }
        EditReturn? Return() => cell is { } c ? CellReturn(c.I, c.J) : null;
        void Refresh()
        {
            var ui = _vm.Ui;
            host.Children.Clear();
            if (Resolve() is not { } resolved)
            {
                host.Children.Add(new TextBlock { Text = CellSheetLogic.FixStaffGoneNote, FontSize = 13, Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
                return;
            }
            focus = resolved;
            host.Children.Add(new TextBlock { Text = "勤務変更案", Opacity = 0.8 });
            if (problems is not null && CellSheetLogic.ProblemChoiceHeading(problems.Count) is { } heading)
            {
                host.Children.Add(new TextBlock { Text = heading, TextWrapping = TextWrapping.Wrap });
                foreach (var f in problems)
                {
                    var fam = f;
                    var b = new ToggleButton { Content = LabelOf(fam), IsChecked = picked == fam, MinHeight = 48, HorizontalAlignment = HorizontalAlignment.Stretch };
                    b.Click += (_, _) => { picked = picked == fam ? null : fam; Refresh(); };
                    host.Children.Add(b);
                }
            }
            // 問題が 2 件以上なら、選ぶまで案を出さない（どの問題を直す案かを先頭の問題で埋めない＝統合仕様 INV-01）。
            if (problems is { Count: >= 2 } && picked is null)
            {
                host.Children.Add(new TextBlock { Text = CellSheetLogic.PickProblemFirstNote, FontSize = 13, Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
                return;
            }
            var state = CellSheetLogic.PanelState(ui.Running, ui.FixSearching, ui.FixDoneKey, ui.FixFailedKey, focus.Key, ui.FixCancelledKey);
            if (state == FixPanelState.WaitCheck) { host.Children.Add(new TextBlock { Text = "計算・チェックが終わると探します。", TextWrapping = TextWrapping.Wrap }); return; }
            if (state == FixPanelState.NotStarted) { host.Children.Add(new TextBlock { Text = "まだ探していません。", TextWrapping = TextWrapping.Wrap }); return; }
            if (state == FixPanelState.Failed)
            {
                host.Children.Add(new TextBlock { Text = "勤務変更案を探す途中でうまくいきませんでした。もう一度試せます。", TextWrapping = TextWrapping.Wrap });
                var again = new Button { Content = CellSheetLogic.FixSearchRetryButton, MinHeight = 48 };
                again.Click += (_, _) => Find();
                host.Children.Add(again);
                return;
            }
            if (state == FixPanelState.Cancelled)
            {
                host.Children.Add(new TextBlock { Text = CellSheetLogic.FixSearchCancelled, TextWrapping = TextWrapping.Wrap });
                var retry = new Button { Content = CellSheetLogic.FixSearchRetryButton, MinHeight = 48 };
                retry.Click += (_, _) => Find();
                host.Children.Add(retry);
                return;
            }
            if (state == FixPanelState.Running)
            {
                var row = new Grid { ColumnSpacing = 8 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new ProgressRing { IsActive = true, Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center });
                // 見ている問題があれば、何を減らす案を探しているかを言う（個人ごとの検索ボタンは並べない）。
                var waiting = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                if (CellSheetLogic.PanelTarget(ui, focus, _vm.StaffCellLimits, _vm.NeedCellLimits) is { } st)
                    foreach (var l in CellSheetLogic.TargetSearchingLines(st, CellSheetLogic.FixTargetLabel(st, st.Shift >= 0 && st.Shift < ui.ShiftSymbols.Count ? ui.ShiftSymbols[st.Shift] : "", ui.StartDate)))
                        waiting.Children.Add(new TextBlock { Text = l, FontSize = 13, TextWrapping = TextWrapping.Wrap });
                waiting.Children.Add(new TextBlock { Text = CellSheetLogic.FixSearchingText(quick), TextWrapping = TextWrapping.Wrap, Opacity = 0.8 });
                Grid.SetColumn(waiting, 1);
                row.Children.Add(waiting);
                var stop = new HyperlinkButton { Content = CellSheetLogic.CancelFixSearchButton, MinHeight = 48 };
                stop.Click += (_, _) => _vm.CancelFixSearch();
                Grid.SetColumn(stop, 2);
                row.Children.Add(stop);
                host.Children.Add(row);
                return;
            }
            // 見ている問題（回数＝職員×シフト、人員＝シフト×日）。案ごとに盤面へ当てて数え直し、その問題を変える案を主一覧に、
            //   変えない案は「別の問題を改善する案」へ畳む（Kotlin FixSearchPanel、2026-10-14 利用者レビュー）。
            var target = CellSheetLogic.PanelTarget(ui, focus, _vm.StaffCellLimits, _vm.NeedCellLimits);
            var sym = focus.Shift is { } sk && sk >= 0 && sk < ui.ShiftSymbols.Count ? ui.ShiftSymbols[sk] : "";
            var label = target is not null ? CellSheetLogic.FixTargetLabel(target, sym, ui.StartDate) : sym;
            var snap = ui.Schedule.Select(r => r.ToArray()).ToArray();
            // セルのシートは、選んだ問題（無ければこのセルの問題のどれか）がこのセルで実際に消える案だけを主にする（族の全体の件数では判定しない）。
            var resolver = resolveAt is { } ra ? _vm.CellResolver(ra.I, ra.J, picked) : null;
            Func<FixSuggestion, bool>? pred = resolver ?? (picked is not null ? x => CellSheetLogic.FixAddresses(x, picked) : null);
            var addressLabel = resolveAt is not null ? "このセルの" + (picked is not null ? LabelOf(picked) : "問題") : null;
            // 日から開いた一覧はその日のセルに触れる案だけ（INV-01）。
            var shown = CellSheetLogic.FixListDay(focus) is { } listDay ? ui.FixSuggestions.Where(x => CellSheetLogic.ListableFix(null, ui.Schedule, x.Ops, listDay)).ToList() : ui.FixSuggestions.ToList();
            var fixing0 = target is not null ? shown.Where(x => target.Effect(ui.Schedule, x.Ops).Fixes).ToList() : pred is not null ? shown.Where(pred).ToList() : shown;
            // 日の人員の対象は、同じ勤務の期間全体の過不足が減る案を先に（見ている日だけ直って別の日へ移る案は後ろ。隠さない）。
            var fixing = fixing0;
            if (target is { Day: not null, Multi: false } && CellSheetLogic.ShiftPeriodGap(ui.Schedule, target.Shift, target.Under, _vm.NeedCellLimits) is { } g0)
                fixing = fixing0.OrderBy(x => (CellSheetLogic.ShiftPeriodGap(CellSheetLogic.BoardAfter(ui.Schedule, x.Ops), target.Shift, target.Under, _vm.NeedCellLimits) ?? g0) < g0 ? 0 : 1).ToList();
            else if (target?.Staff is not null)
                fixing = fixing0.OrderBy(x => CellSheetLogic.CountMovesOnly(x, target) ? 1 : 0).ToList();
            var fixingSet = new HashSet<FixSuggestion>(fixing, ReferenceEqualityComparer.Instance);
            var others = target is not null || pred is not null ? shown.Where(x => !fixingSet.Contains(x)).ToList() : new List<FixSuggestion>();
            var purpose = target is not null
                ? CellSheetLogic.FixTargetPurpose(target, focus.Staff is { } fi && fi >= 0 && fi < ui.StaffNames.Count ? ui.StaffNames[fi] : null, label)
                : picked is not null ? $"選んだ問題（{LabelOf(picked)}）を減らす" : null;
            var tv = ui.FixTiers is { } tv0 && tv0.Key == focus.Key && target is { Multi: false } ? tv0 : null;
            UIElement Card(int n, FixSuggestion s, FixExplanation? ex = null, string? purposeOverride = null)
            {
                var e = target?.Effect(ui.Schedule, s.Ops);
                var res = Application.Current.Resources;
                var fg = (Brush)res["MagiOnSecondaryContainerBrush"];
                var col = new StackPanel { Spacing = 4 };
                TextBlock Small(string t) => new() { Text = t, FontSize = 13, Foreground = fg, TextWrapping = TextWrapping.Wrap };
                TextBlock Caption(string t) => new() { Text = t, FontSize = 12, Opacity = 0.8 };
                // 先頭＝いま見ている問題がどう変わるか。変更する勤務、ほかの問題への影響、操作の順。
                // 日の人員の対象は、同じ勤務の期間全体の過不足も前後で言う（見ている日だけ直って別の日へ移る案を、解消と読ませない）。
                IReadOnlyList<string> remain = Array.Empty<string>();
                if (target is { Day: not null, Multi: false }
                    && CellSheetLogic.ShiftPeriodGap(ui.Schedule, target.Shift, target.Under, _vm.NeedCellLimits) is { } b0
                    && CellSheetLogic.ShiftPeriodGap(CellSheetLogic.BoardAfter(ui.Schedule, s.Ops), target.Shift, target.Under, _vm.NeedCellLimits) is { } b1)
                    remain = new[] { CellSheetLogic.PeriodGapLine(target.Shift >= 0 && target.Shift < ui.ShiftSymbols.Count ? ui.ShiftSymbols[target.Shift] : "", target.Under, b0, b1) };
                // 先頭は必須違反の変化（必須を減らす案は、その効き目が判断の第一）。
                if (CellSheetLogic.HardChangeLine(s.Diff, LabelOf) is { } hl)
                    col.Children.Add(new TextBlock { Text = hl, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = fg, TextWrapping = TextWrapping.Wrap });
                foreach (var l in FixExplain.HardUnchangedLines(s, ui.BestHard > 0)) col.Children.Add(Small(l));
                if (e is not null && target is not null)
                {
                    col.Children.Add(new TextBlock { Text = $"案{n}：{CellSheetLogic.SurplusMoveTitle(target, ui.Schedule, s.Ops, ui.StaffNames, ui.ShiftSymbols) ?? e.Title(label)}", FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = fg, TextWrapping = TextWrapping.Wrap });
                    col.Children.Add(new TextBlock { Text = $"{label}：{e.CountLine()}　{e.GapLine()}" + (CellSheetLogic.CountMovesOnly(s, target) ? CellSheetLogic.MovesToOtherStaffNote : ""), Foreground = fg, TextWrapping = TextWrapping.Wrap });
                    col.Children.Add(new TextBlock { Text = e.AfterNote(target.Basis), Foreground = fg, TextWrapping = TextWrapping.Wrap });
                    foreach (var r in remain) col.Children.Add(new TextBlock { Text = r, Foreground = fg, TextWrapping = TextWrapping.Wrap });
                    col.Children.Add(Small(CellSheetLogic.ChangeSizeLine(s.Ops)));
                }
                else
                {
                    col.Children.Add(new TextBlock { Text = $"案{n}：{FixOutcomeText.FixDisplayLabel(s.Label)}", FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = fg, TextWrapping = TextWrapping.Wrap });
                    if (addressLabel is not null && resolver?.Invoke(s) == true)
                        col.Children.Add(new TextBlock { Text = $"{addressLabel}：解消します", Foreground = fg, TextWrapping = TextWrapping.Wrap });
                    if (picked is not null && CellSheetLogic.FamilyEffectLine(s, picked, LabelOf(picked)) is { } fl)
                        col.Children.Add(new TextBlock { Text = fl, Foreground = fg, TextWrapping = TextWrapping.Wrap });
                }
                if (!quick)
                {
                    col.Children.Add(Caption("変更する勤務"));
                    foreach (var c in ChainFixPreview.Of(s, snap, ui.StaffNames, ui.ShiftSymbols, ui.StartDate).Changes) col.Children.Add(Small(c));
                    // [3.712.0] 日ごとの人数と回数の影響は確認の画面が言う＝案のカードでは「詳しく」の中だけ（Android と同じ）。
                    var impactMore = new StackPanel { Spacing = 2, Visibility = Visibility.Collapsed };
                    var impactToggle = new HyperlinkButton { Content = CellSheetLogic.DiagDetailsLabel, MinHeight = 44 };
                    impactToggle.Click += (_, _) =>
                    {
                        var opened = impactMore.Visibility != Visibility.Visible;
                        impactMore.Visibility = opened ? Visibility.Visible : Visibility.Collapsed;
                        impactToggle.Content = opened ? CellSheetLogic.DiagDetailsClose : CellSheetLogic.DiagDetailsLabel;
                    };
                    col.Children.Add(impactToggle);
                    col.Children.Add(impactMore);
                    // 見ている日のほかの勤務と、別の日へ移る影響を分ける（必要・適正は設定の値で言う）。
                    var (sameDay, otherDays) = CellSheetLogic.HeadcountImpact(ui.Schedule, s.Ops, ui.ShiftSymbols, ui.StartDate, _vm.NeedCellLimits,
                        target?.Day is { } td ? (target.Shift, td) : null);
                    if (sameDay.Count > 0)
                    {
                        impactMore.Children.Add(Caption("同じ日のほかの勤務"));
                        foreach (var l in sameDay) impactMore.Children.Add(Small("・" + l));
                    }
                    if (otherDays.Count > 0)
                    {
                        impactMore.Children.Add(Caption(target?.Day is not null ? "別の日への影響" : "日ごとの人数への影響"));
                        foreach (var l in otherDays) impactMore.Children.Add(Small("・" + l));
                    }
                    var notes = CellSheetLogic.StaffCountNotes(ui.Schedule, s.Ops, ui.StaffNames, ui.ShiftSymbols, _vm.StaffCellLimits, target?.Staff);
                    if (notes.Count > 0)
                    {
                        impactMore.Children.Add(Caption("この組み合わせで変わる回数"));
                        foreach (var l in notes) impactMore.Children.Add(Small("・" + l));
                    }
                    var skip = target is not null ? CellSheetLogic.FixTargetFamilies(target) : picked is not null ? new HashSet<string> { picked } : new HashSet<string>();
                    var others = CellSheetLogic.OtherEffectLines(s, skip, LabelOf, hardShown: CellSheetLogic.HardChangeLine(s.Diff, LabelOf) is not null).ToList();
                    if (target?.Staff is not null)
                        others.AddRange(CellSheetLogic.NewCountBreakLines(ui.Schedule, s.Ops, target.Staff, _vm.StaffCellLimits, ui.StaffNames, ui.ShiftSymbols, LabelOf));
                    if (others.Count > 0) col.Children.Add(Caption("ほかの問題への影響"));
                    foreach (var l in others)
                        col.Children.Add(new TextBlock { Text = "・" + l, FontSize = 13, Foreground = fg, TextWrapping = TextWrapping.Wrap });
                }
                else col.Children.Add(new TextBlock { Text = NextActionGuide.FixImpactLines(s, LabelOf).HardLine, FontSize = 13, Foreground = fg, TextWrapping = TextWrapping.Wrap });
                if (ex is not null) foreach (var l in FixExplain.ExtraReasonLines(ex, ui.Schedule, ui.StaffNames, ui.ShiftSymbols, ui.StartDate)) col.Children.Add(Small(l));
                var review = new Button { Content = CellSheetLogic.ReviewChangeButton, MinHeight = 48, HorizontalAlignment = HorizontalAlignment.Right, IsEnabled = !ui.Running };
                var chosen = s;
                // 「別の問題を改善する案」は見ている問題を直す案として確認・適用しない（目的と対象を渡さない＝統合仕様 INV-01/INV-03）。
                var fixesTarget = e?.Fixes == true;
                IReadOnlyList<string> eff = e is not null && target is not null && fixesTarget ? ChainFixPreview.PreviewEffectLines(label, e, target.Basis).Concat(remain).ToList() : Array.Empty<string>();
                var reviewPurpose = target is null || fixesTarget ? purposeOverride ?? purpose : null;
                var reviewTarget = fixesTarget ? target : null;
                // 確認の画面（MainWindow）はこのダイアログが閉じ切ってから出す（ContentDialog は同時に 1 つ）。
                review.Click += (_, _) =>
                {
                    _reviewOrigin = new FixPanelOrigin(focus, settingsLabel, quick, cell, problems, resolveAt, picked);
                    registerClosed(() => _vm.PreviewFix(chosen, reviewPurpose, eff, focus.Key, reviewTarget));
                    hide();
                };
                col.Children.Add(review);
                return new Border { Background = (Brush)res["MagiSecondaryContainerBrush"], CornerRadius = new CornerRadius(12), Padding = new Thickness(10), Child = col };
            }
            var limit = quick ? 1 : 3;
            // この依頼の画面で当てた手の結果（今の盤面がその手の直後のときだけ）。案は今の勤務表で探し直したもの＝古い案は使わない。
            if (!quick && ui.AppliedFix is { } applied && !ReferenceEquals(applied, foldedApplied) && applied.FocusKey == focus.Key && applied.IsCurrent(ui.Schedule))
            {
                var at = target ?? applied.Target;
                var atLabel = at is not null ? CellSheetLogic.FixTargetLabel(at, at.Shift >= 0 && at.Shift < ui.ShiftSymbols.Count ? ui.ShiftSymbols[at.Shift] : "", ui.StartDate) : label;
                var res = Application.Current.Resources;
                var fg = (Brush)res["MagiOnTertiaryContainerBrush"];
                var banner = new StackPanel { Spacing = 4 };
                banner.Children.Add(new TextBlock { Text = "変更を反映しました", FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = fg });
                foreach (var l in CellSheetLogic.AppliedLines(ui, applied, at, atLabel, _vm.NeedCellLimits, LabelOf))
                    banner.Children.Add(new TextBlock { Text = l, FontSize = 13, Foreground = fg, TextWrapping = TextWrapping.Wrap });
                if (appliedChangesOpen)
                {
                    var asFix = new FixSuggestion(FixKind.ChangeMulti, applied.Ops, applied.Label, 0, 0, Array.Empty<(string, int)>());
                    foreach (var c in ChainFixPreview.Of(asFix, applied.Before.Select(r => r.ToArray()).ToArray(), ui.StaffNames, ui.ShiftSymbols, ui.StartDate).Changes)
                        banner.Children.Add(new TextBlock { Text = "・" + c, FontSize = 13, Foreground = fg, TextWrapping = TextWrapping.Wrap });
                }
                // 保存に失敗している間は、追加の変更より保存のやり直しを先に（統合仕様 §11）。
                if (ui.SaveState == SaveState.Failed)
                {
                    // 失敗している間だけ塗り（帯の中の主はこれ 1 つ）。
                    var retry = new Button { Content = CellSheetLogic.SaveRetryButton, MinHeight = 48, Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
                    retry.Click += (_, _) => _vm.RetrySave();
                    banner.Children.Add(retry);
                }
                var acts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                // 次の一歩: 同じ問題が残れば帯をたたんで下の探し直した案へ、無ければ残っている必須違反を順に見る（巡回できなければ分析タブ）。
                var nextSym = at is not null && at.Shift >= 0 && at.Shift < ui.ShiftSymbols.Count ? ui.ShiftSymbols[at.Shift] : null;
                var sameRemains = nextSym is not null && CellSheetLogic.AppliedRemaining(ui, applied, at, atLabel, _vm.NeedCellLimits).Count > 0;
                // か所の数は上部の札と同じ巡回の項目数（写しが今の検査に追いついていればそれを、でなければ同じ式で数える）。
                var tourCount = ui.Display is { } disp && disp.FreshFor(ui) ? disp.TourItems.Count : _vm.HardViolationItemsFor(LabelOf).Count;
                if (CellSheetLogic.AppliedNextButton(sameRemains, nextSym, applied.HardAfter, tourCount) is { } nextLabel)
                {
                    var tourBtn = new HyperlinkButton { Content = nextLabel, MinHeight = 48 };
                    tourBtn.Click += (_, _) =>
                    {
                        if (sameRemains) { foldedApplied = applied; Refresh(); return; }
                        var first = _vm.HardViolationItemsFor(LabelOf).FirstOrDefault();
                        registerClosed(() => { if (first is not null) FocusCell(first.Cell.I, first.Cell.J); else _goAnalysis?.Invoke(); });
                        hide();
                    };
                    acts.Children.Add(tourBtn);
                }
                var changesBtn = new HyperlinkButton { Content = appliedChangesOpen ? "変更内容を閉じる" : "今回の変更内容を見る", MinHeight = 48 };
                changesBtn.Click += (_, _) => { appliedChangesOpen = !appliedChangesOpen; Refresh(); };
                acts.Children.Add(changesBtn);
                // 当てた直後だけ（今の盤面がその手の直後）に出る＝元に戻すの先頭はこの手。
                if (ui.CanUndo)
                {
                    var undo = new HyperlinkButton { Content = "今回の変更を元に戻す", MinHeight = 48 };
                    undo.Click += (_, _) => _vm.Undo();
                    acts.Children.Add(undo);
                }
                banner.Children.Add(new ScrollViewer { Content = acts, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
                host.Children.Add(new Border { Background = (Brush)res["MagiTertiaryContainerBrush"], CornerRadius = new CornerRadius(12), Padding = new Thickness(10), Child = banner });
            }
            if (pred is not null && target is null && shown.Count > 0 && fixing.Count == 0)
                host.Children.Add(new TextBlock { Text = CellSheetLogic.NoFixForProblemLine(addressLabel ?? (picked is not null ? LabelOf(picked) : "このセルの問題")), TextWrapping = TextWrapping.Wrap, Opacity = 0.8 });
            else if (tv is not null && target is not null)
            {
                // 段に分けた案: 範囲の中の 1 セル → 適用できない 1 セルの理由（1 行、ボタンなし）→ 崩れた分も整える案。
                if (!quick && CellSheetLogic.OtherDaysGapLine(ui.NeedFamilies, ui.Schedule, target, _vm.NeedCellLimits, ui.ShiftSymbols, ui.StartDate) is { } od)
                    host.Children.Add(new TextBlock { Text = od, FontSize = 13, Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
                var t1 = tv.Of(FixTier.T1); var t2 = tv.Of(FixTier.T2);
                // 争点カード（D03）: 主一覧の上位 2 件を推奨A／案B として先に。選ぶと各案と同じ変更の確認へ（一覧は下にそのまま）。
                if (!quick && CellSheetLogic.ContestCardOf(target, label, t1.Concat(t2).Select(x => x.Sug).ToList(), ui.Schedule, ui.StaffNames, ui.ShiftSymbols) is { } cc)
                {
                    var ct = target;
                    void Pick(FixSuggestion s)
                    {
                        var ce = ct.Effect(ui.Schedule, s.Ops);
                        IReadOnlyList<string> ceff = ChainFixPreview.PreviewEffectLines(label, ce, ct.Basis);
                        _reviewOrigin = new FixPanelOrigin(focus, settingsLabel, quick, cell, problems, resolveAt, picked);
                        registerClosed(() => _vm.PreviewFix(s, purpose, ceff, focus.Key, ct));
                        hide();
                    }
                    var ccol = new StackPanel { Spacing = 4 };
                    ccol.Children.Add(new TextBlock { Text = cc.Title, FontWeight = Microsoft.UI.Text.FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
                    ccol.Children.Add(new TextBlock { Text = cc.A.Label, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                    if (cc.A.Reason is { } rs) ccol.Children.Add(new TextBlock { Text = rs, FontSize = 13, Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
                    if (cc.B is { } cb) ccol.Children.Add(new TextBlock { Text = cb.Label, TextWrapping = TextWrapping.Wrap });
                    var aBtn = new Button { Content = CellSheetLogic.ContestAButton, MinHeight = 48, HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = !ui.Running,
                        Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
                    aBtn.Click += (_, _) => Pick(cc.A.Suggestion);
                    ccol.Children.Add(aBtn);
                    if (cc.B is { } cb2)
                    {
                        var bBtn = new Button { Content = CellSheetLogic.ContestBButton, MinHeight = 48, HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = !ui.Running };
                        bBtn.Click += (_, _) => Pick(cb2.Suggestion);
                        ccol.Children.Add(bBtn);
                    }
                    host.Children.Add(new Border { Background = (Brush)Application.Current.Resources["MagiSurfaceVariantBrush"], CornerRadius = new CornerRadius(12), Padding = new Thickness(10), Child = ccol });
                }
                var n = 0;
                if (t1.Count > 0)
                {
                    if (!quick) host.Children.Add(new TextBlock { Text = FixTiers.Tier1Heading(target), Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
                    foreach (var x in t1.Take(limit)) host.Children.Add(Card(++n, x.Sug));
                }
                foreach (var b in tv.Blocked.Take(quick ? 1 : 3)) host.Children.Add(new TextBlock { Text = b, FontSize = 13, Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
                if (t2.Count > 0 && !(quick && t1.Count > 0))
                {
                    if (!quick) host.Children.Add(new TextBlock { Text = FixTiers.Tier2Heading, Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
                    n = Math.Min(t1.Count, limit);
                    foreach (var x in t2.Take(limit)) host.Children.Add(Card(++n, x.Sug, x.Explain));
                }
                if (!tv.HasMain) NoFixBlock(ui, target, label);
            }
            else if (fixing.Count > 0)
            {
                // 同じ勤務のほかの日の過不足はここに 1 回だけ（カードごとに繰り返すと判断に要る行が下へ押し出される）。
                if (target is not null && !quick && CellSheetLogic.OtherDaysGapLine(ui.NeedFamilies, ui.Schedule, target, _vm.NeedCellLimits, ui.ShiftSymbols, ui.StartDate) is { } od)
                    host.Children.Add(new TextBlock { Text = od, FontSize = 13, Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
                if (target is not null && !quick) host.Children.Add(new TextBlock { Text = CellSheetLogic.ThisFixHeading(label, target), Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
                var n = 0;
                foreach (var s in fixing.Take(limit)) host.Children.Add(Card(++n, s));
            }
            else NoFixBlock(ui, target, label);
            void Fold(string title, string note, IReadOnlyList<TieredFix> items, int startN, string? purposeOverride, bool open, Action<bool> setOpen)
            {
                var head = new StackPanel();
                head.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], TextWrapping = TextWrapping.Wrap });
                head.Children.Add(new TextBlock { Text = note, FontSize = 13, Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
                var list = new StackPanel { Spacing = 6 };
                var n = startN;
                foreach (var x in items.Take(limit)) list.Children.Add(Card(++n, x.Sug, x.Explain, purposeOverride));
                var exp = new Expander
                {
                    Header = head, Content = list, IsExpanded = open, MinHeight = 48,
                    HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                };
                exp.Expanding += (_, _) => setOpen(true);
                exp.Collapsed += (_, _) => setOpen(false);
                host.Children.Add(exp);
            }
            if (tv is not null && target is not null && !quick)
            {
                var main = Math.Min(tv.Of(FixTier.T1).Count, limit) + Math.Min(tv.Of(FixTier.T2).Count, limit);
                // 見ている問題を減らさない案は一覧に出さない（適用の門 ImprovesTarget と同じ判定＝INV-03）。
                var mv = tv.Of(FixTier.MoveOnly); var t3 = tv.Of(FixTier.T3).Where(x => CellSheetLogic.ListableFix(target, ui.Schedule, x.Sug.Ops, CellSheetLogic.FixListDay(focus))).ToList();
                if (mv.Count > 0) Fold(FixTiers.MoveOnlyHeading(sym, target.Under, mv.Count), FixTiers.MoveOnlyNote, mv, main, FixTiers.MoveOnlyPurpose(label, target), moveOnlyOpen, v => moveOnlyOpen = v);
                if (t3.Count > 0) Fold(CellSheetLogic.OtherFixHeading(t3.Count), CellSheetLogic.OtherFixNote(label, target), t3, main + Math.Min(mv.Count, limit), null, othersOpen, v => othersOpen = v);
            }
            else if (others.Count > 0 && !quick && target is null && pred is not null)
            {
                var head = new StackPanel();
                head.Children.Add(new TextBlock { Text = CellSheetLogic.OtherFixHeading(others.Count), Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
                head.Children.Add(new TextBlock { Text = target is not null ? CellSheetLogic.OtherFixNote(label, target) : $"{addressLabel ?? "選んだ問題"}は解消しません", FontSize = 13, Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
                var list = new StackPanel { Spacing = 6 };
                var n = Math.Min(fixing.Count, limit);
                foreach (var s in others.Take(limit)) list.Children.Add(Card(++n, s));
                var exp = new Expander
                {
                    Header = head, Content = list, IsExpanded = othersOpen, MinHeight = 48,
                    HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                };
                exp.Expanding += (_, _) => othersOpen = true;
                exp.Collapsed += (_, _) => othersOpen = false;
                host.Children.Add(exp);
            }
        }
        // 案が見つからないとき（Kotlin NoFixBlock）: 確かめた理由と次に確認すること、探した範囲（畳む）、行き先と相談。
        Button RetryButton()
        {
            var b = new Button { Content = CellSheetLogic.FixSearchRetryButton, MinHeight = 48 };
            b.Click += (_, _) => Find();
            return b;
        }
        void NoFixBlock(UiState ui, FixTarget? target, string label)
        {
            var why = FixSearchText.NoFixReasons(ui, focus, _vm.StaffCellLimits, _vm.NeedCellLimits);
            IReadOnlyList<string> lines = target is { Staff: null } ? new[] { CellSheetLogic.NoNeedFixLine(label, target, ui.FixTimedOut), FixSearchText.NoFixScope } : why.Lines;
            var body = lines.Where(l => l != FixSearchText.NoFixScope).ToList();
            // [3.713.0] 見出し「案はありません」と説明の 1 行（続けて空なら 3.711.0 の行）。
            host.Children.Add(new TextBlock { Text = AnalysisTriage.NoFixTitle, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            host.Children.Add(new TextBlock { Text = AnalysisTriage.NoFixLineOf(ui.FixEmptyRepeatKey == focus.Key, ui.FixTimedOut), TextWrapping = TextWrapping.Wrap });
            // [3.714.0] 主は「問題一覧」（巡回の先頭のセルへ）、閉じるは文字。セルのシートは自分の閉じるがあるので出さない。
            // [3.716.0] シートに塗りの主（足りない側の人探し）が別にあれば文字ボタン（塗りは 1 つ＝INV-09）。
            var nfTour = _vm.HardViolationItemsFor(LabelOf);
            if (nfTour.Count > 0)
            {
                ButtonBase nfList = primaryElsewhere
                    ? new HyperlinkButton { Content = AnalysisTriage.NoFixListButton, MinHeight = 48 }
                    : new Button { Content = AnalysisTriage.NoFixListButton, MinHeight = 48, Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
                var nfFirst = nfTour[0].Cell;
                nfList.Click += (_, _) => { hide(); OpenCellSheet(nfFirst.I, nfFirst.J); };
                host.Children.Add(nfList);
            }
            if (!quick)
            {
                var nfClose = new HyperlinkButton { Content = AnalysisTriage.NoFixCloseButton, MinHeight = 48 };
                nfClose.Click += (_, _) => hide();
                host.Children.Add(nfClose);
            }
            if (ui.FixEmptyRepeatKey == focus.Key)
            {
                // 全面の一覧は下に希望・設定への行き先を並べる＝ボタンはそれが無いセルのシートだけ。
                if (quick && AnalysisTriage.RepeatedEmptyActionOf(why.WishRelated, ui.SettingIssues.Count > 0) is { } rea)
                {
                    var reb = new Button { Content = AnalysisTriage.RepeatedEmptyActionLabel(rea), MinHeight = 48 };
                    reb.Click += (_, _) =>
                    {
                        hide();
                        _goEditLanding?.Invoke(rea == RepeatedEmptyAction.Wishes ? new EditLanding(0, null, WishStaff: focus.Staff, Day: focus.Day) : new EditLanding(2, why.SettingsSection), Return());
                    };
                    host.Children.Add(reb);
                }
            }
            if (quick)
            {
                var nfSingle = problems is { Count: 1 } ? LabelOf(problems[0]) : null;
                var (nfStaff, nfDay) = (focus.Staff, focus.Day);
                var nfPrev = nfStaff is not null && nfDay is { } nfD0 && nfSingle is not null ? CellSheetLogic.AdjacentDayLabel(ui.StartDate, ui.Days, nfD0 - 1) : null;
                var nfNext = nfStaff is not null && nfDay is { } nfD1 && nfSingle is not null ? CellSheetLogic.AdjacentDayLabel(ui.StartDate, ui.Days, nfD1 + 1) : null;
                host.Children.Add(new TextBlock { Text = CellSheetLogic.CompactNoFixLine(body, nfSingle, nfPrev, nfNext), TextWrapping = TextWrapping.Wrap, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 13 });
                if (nfStaff is { } nfI && nfDay is { } nfJ && (nfPrev is not null || nfNext is not null))
                {
                    var nfAdj = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    foreach (var (nfLabel, nfTo) in new[] { (nfPrev, nfJ - 1), (nfNext, nfJ + 1) })
                    {
                        if (nfLabel is null) continue;
                        var nfBtn = new Button { Content = $"{nfLabel}を開く", MinHeight = 48 };
                        nfBtn.Click += (_, _) => { hide(); OpenCellSheet(nfI, nfTo); };
                        nfAdj.Children.Add(nfBtn);
                    }
                    host.Children.Add(nfAdj);
                }
                if (ui.FixTimedOut) host.Children.Add(RetryButton());
                if (whenNoFix is not null) foreach (var el in whenNoFix()) host.Children.Add(el);
                return;
            }
            foreach (var l in body) host.Children.Add(new TextBlock { Text = l, TextWrapping = TextWrapping.Wrap });
            if (ui.FixTimedOut) host.Children.Add(RetryButton());
            if (why.Candidates.Count > 0 && target is { Staff: { } cs })
                host.Children.Add(CandidatesExpander(ui, target, cs, why.Candidates));
            if (lines.Contains(FixSearchText.NoFixScope))
            {
                var scope = new Expander
                {
                    Header = "探した範囲", IsExpanded = scopeOpen, MinHeight = 48,
                    HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Content = new TextBlock { Text = FixSearchText.SearchedScopeDetail, FontSize = 13, Opacity = 0.8, TextWrapping = TextWrapping.Wrap },
                };
                scope.Expanding += (_, _) => scopeOpen = true;
                scope.Collapsed += (_, _) => scopeOpen = false;
                host.Children.Add(scope);
            }
            host.Children.Add(new TextBlock { Text = "設定を確認する", FontSize = 12, Opacity = 0.8 });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            // 希望への行き先は 1 つ（日があればその日を選んだ状態で開く。職員が無くても日は渡す）。
            var needUnder = target is { Staff: null, Under: true };
            if (why.WishRelated || needUnder)
            {
                var w = new Button { Content = CellSheetLogic.WishDayButton(focus.Day is { } fd ? DayText.Short(ui.StartDate, fd) : null), MinHeight = 48 };
                w.Click += (_, _) => { hide(); _goEditLanding?.Invoke(new EditLanding(0, null, WishStaff: focus.Staff, Day: focus.Day), Return()); };
                buttons.Children.Add(w);
            }
            if (needUnder)
            {
                var ws1 = new Button { Content = "担当できる職員を見る", MinHeight = 48 };
                ws1.Click += (_, _) => { hide(); _goEditLanding?.Invoke(new EditLanding(2, "yr_ws1"), Return()); };
                buttons.Children.Add(ws1);
            }
            if (FixSearchText.CoverageFocus(ui, focus))
            {
                // 人員不足・過剰の印は、その日のそのシフトの必要人数（月次条件）へ。群のレンジは④のまま。
                var need = new Button { Content = SettingsLabel.For("yr_headcount"), MinHeight = 48 };
                need.Click += (_, _) => { hide(); _goEditLanding?.Invoke(new EditLanding(0, null, NeedShift: focus.Shift, Day: focus.Day), Return()); };
                buttons.Children.Add(need);
            }
            else
            {
                var st = new Button { Content = settingsLabel ?? SettingsLabel.For(why.SettingsSection), MinHeight = 48 };
                st.Click += (_, _) => { hide(); _goEditLanding?.Invoke(new EditLanding(2, why.SettingsSection), Return()); };
                buttons.Children.Add(st);
            }
            if (target is { Staff: { } ts })
            {
                var name = ts < ui.StaffNames.Count ? ui.StaffNames[ts] : $"#{ts}";
                var item = ConsultList.CountShortage(name, target.Shift < ui.ShiftSymbols.Count ? ui.ShiftSymbols[target.Shift] : "",
                    CellSheetLogic.FixTargetHead(target, target.CountOf(ui.Schedule))[0], ts);
                if (ConsultList.IsConsulted(ui.Consults, item)) buttons.Children.Add(AnalysisView.TagChip(ConsultList.Done, MagiAccent.Orange));
                else
                {
                    var consult = new Button { Content = ConsultList.Button, MinHeight = 48 };
                    consult.Click += (_, _) => { _vm.AddConsult(item); Refresh(); };
                    buttons.Children.Add(consult);
                }
            }
            host.Children.Add(new ScrollViewer { Content = buttons, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
            if (whenNoFix is not null) foreach (var el in whenNoFix()) host.Children.Add(el);
        }
        // ［追加候補日を確認］（Kotlin AddCandidatesDialog）。ContentDialog は同時に 1 つ＝入れ子のダイアログでなく、その場で開く。
        //   候補日ごとに今の勤務 → 対象の勤務、変更できない理由、解消するために確認すること。確かめられた理由のある日を先に。
        UIElement CandidatesExpander(UiState ui, FixTarget target, int staff, IReadOnlyList<AddCandidate> list)
        {
            var col = new StackPanel { Spacing = 10 };
            col.Children.Add(new TextBlock { Text = $"「{(target.Shift < ui.ShiftSymbols.Count ? ui.ShiftSymbols[target.Shift] : "?")}」を追加する候補日", Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
            foreach (var c in list.OrderBy(x => x.Reason is not null ? 0 : 1))
            {
                var item = new StackPanel { Spacing = 2 };
                item.Children.Add(new TextBlock { Text = FixSearchText.AddCandidateHead(c, ui, target.Shift), Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
                item.Children.Add(new TextBlock { Text = "変更できない理由：" + (c.Reason ?? "この画面では確かめられませんでした"), FontSize = 13, TextWrapping = TextWrapping.Wrap });
                item.Children.Add(new TextBlock { Text = $"解消するために確認すること：{c.Check}", FontSize = 13, Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                var see = new HyperlinkButton { Content = "該当する勤務を見る", MinHeight = 48 };
                var day = c.Day;
                see.Click += (_, _) => { registerClosed(() => FocusCell(staff, day)); hide(); };
                row.Children.Add(see);
                if (c.Go != AddCandidateGo.None)
                {
                    var go = new HyperlinkButton { Content = "対象の設定を見る", MinHeight = 48 };
                    var cc = c;
                    go.Click += (_, _) =>
                    {
                        hide();
                        var landing = cc.Go switch
                        {
                            AddCandidateGo.Wish => new EditLanding(0, null, WishStaff: staff, Day: cc.Day),
                            AddCandidateGo.Need => new EditLanding(0, null, NeedShift: cc.Current, Day: cc.Day),
                            _ => new EditLanding(2, "yr_count"),
                        };
                        _goEditLanding?.Invoke(landing, Return());
                    };
                    row.Children.Add(go);
                }
                item.Children.Add(row);
                col.Children.Add(item);
            }
            var exp = new Expander
            {
                Header = "追加候補日を確認", Content = new ScrollViewer { Content = col, MaxHeight = 420 }, IsExpanded = candidatesOpen, MinHeight = 48,
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            exp.Expanding += (_, _) => candidatesOpen = true;
            exp.Collapsed += (_, _) => candidatesOpen = false;
            return exp;
        }
        PropertyChangedEventHandler handler = (_, e) =>
        {
            // 盤面・希望が変わったときと、計算・チェックが終わった時点（Running→false）で探し直す。
            if (e.PropertyName is nameof(UiState.Schedule) or nameof(UiState.Wishes) or nameof(UiState.Running)) DispatcherQueue.TryEnqueue(() => { Start(); Refresh(); });
            else if (e.PropertyName is nameof(UiState.FixSearching) or nameof(UiState.FixDoneKey) or nameof(UiState.FixFailedKey) or nameof(UiState.FixCancelledKey) or nameof(UiState.FixSuggestions)
                     or nameof(UiState.AppliedFix) or nameof(UiState.SaveState) or nameof(UiState.StaffIds))
                DispatcherQueue.TryEnqueue(Refresh);
        };
        _vm.Ui.PropertyChanged += handler;
        registerClosed(() => { _vm.Ui.PropertyChanged -= handler; _vm.CancelFixSearch(); });
        Start();
        Refresh();
        return host;
    }

    /// <summary>手が無いときの whenNoFix は閉じたセルのフライアウトに結び付くので覚えない。</summary>
    private sealed record FixPanelOrigin(FixFocus Focus, string? SettingsLabel, bool Quick,
        (int I, int J)? Cell, IReadOnlyList<string>? Problems, (int I, int J)? ResolveAt, string? Picked);

    /// <summary>確認の画面を開いた変更案のパネル（ContentDialog は同時に 1 つ＝パネルは確認の前に閉じている）。</summary>
    private FixPanelOrigin? _reviewOrigin;

    /// <summary>セルのシートの探索（<c>quick</c>）は反映の欄を出さない（Kotlin <c>compact</c>）＝適用後には開き直さない。</summary>
    internal bool CanReopenFixPanel(string? focusKey, bool afterApply) =>
        focusKey is not null && _reviewOrigin is { } o && o.Focus.Key == focusKey && !(afterApply && o.Quick);

    /// <summary>
    /// 確認の画面を閉じたあと、同じ依頼の変更案のパネルを開き直す（Kotlin ではパネルが確認の画面の下に残る）。
    /// 適用後は反映の欄（「変更を反映しました」）と今の勤務表で探し直した案、［別案を見る］では閉じる前の案（<paramref name="reuse"/>）。
    /// </summary>
    internal async void ReopenFixPanel(XamlRoot root, string focusKey, string? purpose, bool reuse)
    {
        if (_reviewOrigin is not { } o || o.Focus.Key != focusKey) return;
        // セルのシートの探索は、同じセルのシートを開き直す（「他の人で補う」などの手が無いときのボタンはシートに結び付いている）。
        if (o.Quick && o.Cell is { } c) { OpenCellSheet(c.I, c.J); return; }
        var dialog = new ContentDialog { XamlRoot = root, Title = purpose ?? "勤務変更案", CloseButtonText = "閉じる" };
        // 探索の開始と購読はダイアログが開いてから（開けなかったときに裏で探し続けたり、開いている画面の探索を取り消したりしない）。
        dialog.Opened += (_, _) => dialog.Content = new ScrollViewer
        {
            Content = AttachFixSearch(dialog.Hide, onClosed => dialog.Closed += (_, _) => onClosed(), o.Focus, o.SettingsLabel, null,
                o.Quick, o.Cell, o.Problems, o.ResolveAt, reuse, o.Picked),
            MaxHeight = 520,
        };
        // 別の ContentDialog が開いていると ShowAsync は例外になる（同時に 1 つ）＝黙って消えないよう知らせる。
        try { await dialog.ShowAsync(); }
        catch (Exception) { _vm.Notify("変更案の画面を開けませんでした。開いている画面を閉じてから、もう一度開いてください", "W"); }
    }

    /// <summary>勤務表のそのセルへ送ってシートを開く（巡回の入口・必須違反の一覧と同じ）。</summary>
    internal void OpenCellSheet(int i, int j)
    {
        FocusCell(i, j);
        DispatcherQueue.TryEnqueue(() => ShowCellEditor((FrameworkElement?)_focusCellElement ?? HardListButton, i, j));
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
        var ui = _vm.Ui;
        var sym = shift >= 0 && shift < ui.ShiftSymbols.Count ? ui.ShiftSymbols[shift] : "";
        var dayLabel = DayText.Short(ui.StartDate, day);
        // 題・状態・候補の行はホームの人員不足の手伝いと同じ文言（H-6、Kotlin ShortageSlotHeader／guidedCandidateLabel）。
        if (_vm.NeedCellLimits(shift, day) is { } need)
        {
            var got = ui.Schedule.Count(r => day < r.Count && r[day] == shift);
            if (got < need.Lo)
            {
                flyout.Items.Add(new MenuFlyoutItem { Text = FixOutcomeText.GuidedFixTitle(GuidedStage.Candidates, dayLabel, sym), IsEnabled = false });
                foreach (var point in FixOutcomeText.GuidedStatusPoints(need.Lo, got, candidates.Count))
                    flyout.Items.Add(new MenuFlyoutItem { Text = "・" + point, IsEnabled = false });
                flyout.Items.Add(new MenuFlyoutSeparator());
            }
        }
        MenuFlyoutItem CandidateItem(FixCandidate c)
        {
            var from = c.StaffIndex < ui.Schedule.Count && day < ui.Schedule[c.StaffIndex].Count ? ui.Schedule[c.StaffIndex][day] : -1;
            var item = new MenuFlyoutItem { Text = FixOutcomeText.GuidedCandidateLabel(c.Name, sym) };
            var i = c.StaffIndex;
            var label = $"{c.Name}さんを{dayLabel}の「{sym}」に入れる";
            item.Click += (_, _) => _vm.PreviewShortageCandidate(i, day, shift, label);   // 確認の画面を経由する
            return item;
        }
        foreach (var c in candidates.Take(GuidedFixRules.GuidedFixPreview)) flyout.Items.Add(CandidateItem(c));
        if (candidates.Count > GuidedFixRules.GuidedFixPreview)
        {
            var more = new MenuFlyoutSubItem { Text = $"すべて表示（ほか{candidates.Count - GuidedFixRules.GuidedFixPreview}人）" };
            foreach (var c in candidates.Skip(GuidedFixRules.GuidedFixPreview)) more.Items.Add(CandidateItem(c));
            flyout.Items.Add(more);
        }
        if (candidates.Count == 0)
            flyout.Items.Add(new MenuFlyoutItem { Text = "動かせる候補がいません（担当できて、希望で固定されていない人がいません）", IsEnabled = false });
        else
        {
            flyout.Items.Add(new MenuFlyoutSeparator());
            flyout.Items.Add(new MenuFlyoutItem { Text = "押すと、当てる前に変更と影響を確認できます", IsEnabled = false });
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

    private IReadOnlyList<IReadOnlyList<CoverageMark>> _covMarks = Array.Empty<IReadOnlyList<CoverageMark>>();
    private IReadOnlyDictionary<int, CountBadge> _countBadges = new Dictionary<int, CountBadge>();

    private static string LabelOf(string family) => AnalysisView.BreakdownLabels.TryGetValue(family, out var jp) ? jp : family;

    /// <summary>行末の回数の印／日ヘッダの人員の印の内訳（Kotlin <c>GridMarkDialog</c>）。印の無い見出しは何もしない。</summary>
    private void ShowHeaderMarkDialog(ScheduleCellVm hv)
    {
        var ui = _vm.Ui;
        string title; IReadOnlyList<string> lines; FixFocus focus;
        if (hv.Row == 0 && hv.Col > 0 && hv.Col - 1 < _covMarks.Count && _covMarks[hv.Col - 1].Count > 0)
        {
            var j = hv.Col - 1;
            if (_covMarks[j].Count >= 2) { ShowDayPickDialog(j, _covMarks[j]); return; }
            title = $"{DayText.Full(ui.StartDate, j)}の人員";
            lines = GridDisplayMarks.DayCoverageLines(ui, j, _covMarks[j], LabelOf, _vm.NeedCellLimits);
            focus = new FixFocus(null, _covMarks[j][0].Shift, j);
        }
        else if (hv.Col == 0 && hv.Row > 0 && (_countBadges.ContainsKey(hv.Row - 1) || _c1Stuck.Contains(hv.Row - 1)))
        {
            var i = hv.Row - 1;
            title = $"{(i < ui.StaffNames.Count ? ui.StaffNames[i] : $"#{i}")}さんの勤務状況";
            ShowStaffCountDialog(title, GridDisplayMarks.StaffCountSheetOf(ui, i, LabelOf, _vm.StaffCellLimits), new FixFocus(i, null), _c1Stuck.Contains(i));
            return;
        }
        else return;
        ShowMarkDialog(title, lines, focus);
    }

    /// <summary>
    /// 日ヘッダの人員の印で、2 つ以上の勤務に過不足があるとき（Kotlin MagiFlatGrid の daySheet）: どれを直すかを選んでから案を探す
    /// （2026-10-14 利用者レビュー: 先頭の勤務だけで探し、どの不足を直す案か分からなかった）。
    /// </summary>
    private void ShowDayPickDialog(int j, IReadOnlyList<CoverageMark> marks)
    {
        var ui = _vm.Ui;
        var panel = new StackPanel { Spacing = 4 };
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = $"{DayText.Full(ui.StartDate, j)}の人員", Content = new ScrollViewer { Content = panel }, CloseButtonText = "閉じる" };
        int? picked = null;
        var detach = new List<Action>();
        var rows = new StackPanel { Spacing = 2 };
        var fixHost = new StackPanel();
        void Detach() { foreach (var a in detach.ToList()) a(); detach.Clear(); }
        dialog.Closed += (_, _) => Detach();
        void RenderRows()
        {
            rows.Children.Clear();
            foreach (var m in marks)
            {
                var row = new Grid { ColumnSpacing = 8 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock
                {
                    Text = CellSheetLogic.DayPickRow(_vm.Ui, j, m, _vm.NeedCellLimits(m.Shift, j)), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = picked == m.Shift ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
                });
                FrameworkElement act;
                if (picked == m.Shift) act = new TextBlock { Text = "選択中", FontSize = 13, Foreground = (Brush)Application.Current.Resources["MagiPrimaryBrush"], VerticalAlignment = VerticalAlignment.Center };
                else
                {
                    var see = new HyperlinkButton { Content = CellSheetLogic.ViewFixesButton, MinHeight = 48 };
                    var k = m.Shift;
                    see.Click += (_, _) =>
                    {
                        picked = k;
                        RenderRows();
                        Detach();
                        fixHost.Children.Clear();
                        fixHost.Children.Add(AttachFixSearch(dialog.Hide, a => detach.Add(a), new FixFocus(null, k, j)));
                    };
                    act = see;
                }
                Grid.SetColumn(act, 1);
                row.Children.Add(act);
                rows.Children.Add(row);
            }
        }
        panel.Children.Add(new TextBlock { Text = CellSheetLogic.DayPickHeading(marks), TextWrapping = TextWrapping.Wrap });
        RenderRows();
        panel.Children.Add(rows);
        var dayButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        // その日の勤務を見る＝勤務表をその日の列へ送る（FocusCell の職員 -1 は日の列だけを送る）。
        var seeDay = new Button { Content = $"{DayText.Short(ui.StartDate, j)}の勤務を見る", MinHeight = 48 };
        seeDay.Click += (_, _) => { dialog.Hide(); FocusCell(-1, j); };
        dayButtons.Children.Add(seeDay);
        var needSetting = new Button { Content = SettingsLabel.For("yr_headcount"), MinHeight = 48 };
        needSetting.Click += (_, _) => { dialog.Hide(); _goEditLanding?.Invoke(new EditLanding(0, null, NeedShift: picked ?? marks[0].Shift, Day: j), null); };
        dayButtons.Children.Add(needSetting);
        panel.Children.Add(dayButtons);
        panel.Children.Add(fixHost);
        _ = dialog.ShowAsync();
    }

    /// <summary>
    /// 集計の「計（期間）」の勤務の人員（Kotlin TallyCard の totalSheet）。過不足のある日が 2 日以上なら、どの日を直すかを選んでから探す
    /// （2026-10-14 利用者レビュー: 先頭の日だけで探し、どの日の不足を直す案か分からなかった）。休の過剰は先に設定の意味を確かめてもらう。
    /// </summary>
    private void ShowShiftTotalDialog(int kk, ShiftCoverageTotal ct)
    {
        var ui = _vm.Ui;
        var days = ct.Days;
        var sym = kk < ui.ShiftSymbols.Count ? ui.ShiftSymbols[kk] : $"{kk}";
        var rest = kk == _vm.RestIdx && ct.OverDays.Count > 0;
        var panel = new StackPanel { Spacing = 4 };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = rest ? "休みの人数を確認" : $"「{sym}」の人員" + (ct.OverDays.Count == 0 ? "不足" : ""),
            Content = new ScrollViewer { Content = panel }, CloseButtonText = "閉じる",
        };
        int? picked = days.Count == 1 ? days[0] : null;
        var multi = false;   // 不足の日が 2 日以上なら、それらの日の不足の合計で探す（日ごとの案を組み合わせない）
        var multiSlot = new StackPanel();
        var detach = new List<Action>();
        var rows = new StackPanel { Spacing = 2 };
        var fixHost = new StackPanel();
        void Detach() { foreach (var a in detach.ToList()) a(); detach.Clear(); }
        dialog.Closed += (_, _) => Detach();
        void Mount()
        {
            Detach();
            fixHost.Children.Clear();
            if (multi) fixHost.Children.Add(AttachFixSearch(dialog.Hide, a => detach.Add(a), new FixFocus(null, kk, null, AllDays: true)));
            else if (picked is { } j) fixHost.Children.Add(AttachFixSearch(dialog.Hide, a => detach.Add(a), new FixFocus(null, kk, j)));
        }
        void RenderRows()
        {
            rows.Children.Clear();
            foreach (var j in days)
            {
                var under = ct.UnderDays.Contains(j);
                var text = rest && !under ? CellSheetLogic.RestOverRow(_vm.Ui, kk, j, _vm.NeedCellLimits(kk, j)) : CellSheetLogic.ShiftDayPickRow(_vm.Ui, kk, j, under, _vm.NeedCellLimits(kk, j));
                var row = new Grid { ColumnSpacing = 8 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock
                {
                    Text = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = picked == j ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
                });
                if (days.Count >= 2)
                {
                    FrameworkElement act;
                    if (picked == j && !multi) act = new TextBlock { Text = "選択中", FontSize = 13, Foreground = (Brush)Application.Current.Resources["MagiPrimaryBrush"], VerticalAlignment = VerticalAlignment.Center };
                    else
                    {
                        var see = new HyperlinkButton { Content = "変更案を見る", MinHeight = 48 };
                        var day = j;
                        see.Click += (_, _) => { picked = day; multi = false; RenderRows(); RenderMulti(); Mount(); };
                        act = see;
                    }
                    Grid.SetColumn(act, 1);
                    row.Children.Add(act);
                }
                rows.Children.Add(row);
            }
        }
        if (days.Count == 0 && !rest) panel.Children.Add(new TextBlock { Text = "人員の過不足はありません。" });
        if (rest)
        {
            foreach (var l in CellSheetLogic.RestOverIntro(ct.OverDays.All(j => _vm.NeedCellLimits(kk, j)?.Hi == 0)))
                panel.Children.Add(new TextBlock { Text = l, TextWrapping = TextWrapping.Wrap });
            var setting = new Button { Content = "人数の設定を確認", MinHeight = 48 };
            setting.Click += (_, _) => { dialog.Hide(); _goEditLanding?.Invoke(new EditLanding(0, null, NeedShift: kk, Day: ct.OverDays[0]), null); };
            panel.Children.Add(setting);
        }
        if (days.Count >= 2) panel.Children.Add(new TextBlock { Text = CellSheetLogic.ShiftDayPickHeading(), TextWrapping = TextWrapping.Wrap });
        RenderRows();
        panel.Children.Add(rows);
        // 日ごとの差の合計が減る案だけを主にする（ある日の過剰で別の日の不足を相殺しない）。
        void RenderMulti()
        {
            multiSlot.Children.Clear();
            if (ct.UnderDays.Count < 2) return;
            var name = CellSheetLogic.MultiFixButton(ct.UnderDays.Count);
            if (multi) multiSlot.Children.Add(new TextBlock { Text = "選択中：" + (name.EndsWith("案を探す", StringComparison.Ordinal) ? name[..^4] : name), FontSize = 13, Foreground = (Brush)Application.Current.Resources["MagiPrimaryBrush"], TextWrapping = TextWrapping.Wrap });
            else
            {
                var all = new Button { Content = new TextBlock { Text = name, TextWrapping = TextWrapping.Wrap }, MinHeight = 48 };
                all.Click += (_, _) => { multi = true; picked = null; RenderRows(); RenderMulti(); Mount(); };
                multiSlot.Children.Add(all);
            }
        }
        RenderMulti();
        panel.Children.Add(multiSlot);
        panel.Children.Add(fixHost);
        Mount();
        _ = dialog.ShowAsync();
    }

    private IReadOnlySet<int> _c1Stuck = new HashSet<int>();

    /// <summary>期間の制約を勤務表だけでは満たせないときの次の一歩（希望を見る／並び・期間の制約の設定を開く）。<paramref name="cell"/>＝セルのシートから来たとき（その職員・その日を選んで開き、戻り先にする）。</summary>
    private StackPanel C1StuckButtons(Action hide, (int I, int J)? cell = null)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var ret = cell is { } c ? CellReturn(c.I, c.J) : null;
        var w = new Button { Content = CellSheetLogic.WishDayButton(cell is { } wc ? DayText.Short(_vm.Ui.StartDate, wc.J) : null), MinHeight = 48 };
        w.Click += (_, _) => { hide(); _goEditLanding?.Invoke(new EditLanding(0, null, WishStaff: cell?.I, Day: cell?.J), ret); };
        var st = new Button { Content = SettingsLabel.For("yr_cons"), MinHeight = 48 };
        st.Click += (_, _) => { hide(); _goEditLanding?.Invoke(new EditLanding(2, "yr_cons"), ret); };
        buttons.Children.Add(w); buttons.Children.Add(st);
        return buttons;
    }

    /// <summary>セルのシートから編集タブへ行ったときの戻り先（Kotlin <c>cellReturn</c>、3.646.0 L03）。</summary>
    private EditReturn CellReturn(int i, int j)
    {
        var ui = _vm.Ui;
        var name = i < ui.StaffNames.Count ? ui.StaffNames[i] : $"#{i}";
        return new EditReturn($"{name} {DayText.Short(ui.StartDate, j)} のセル", EditReturn.CellOrigin, (i, j));
    }

    /// <summary>回数の過不足の色（背景, 文字のブラシ名）。不足＝赤系・超過＝橙系（シフト集計の ▼▲ と同じ色言語）。差し替えはここだけ。</summary>
    private static (string Bg, string Fg) CountChipBrushes(bool under) =>
        under ? ("MagiErrorContainerBrush", "MagiOnErrorContainerBrush") : ("MagiWarnContainerBrush", "MagiOnWarnContainerBrush");

    /// <summary>行末の印のシート（Kotlin <c>StaffCountDialog</c>）: 回数の過不足（全文の表。行から案へ進む）・曜日の偏り（既定は閉じる）・公平化・勤務変更案。</summary>
    private void ShowStaffCountDialog(string title, StaffCountSheet sheet, FixFocus focus, bool c1Stuck)
    {
        var res = Application.Current.Resources;
        var ui = _vm.Ui;
        var panel = new StackPanel { Spacing = 8 };
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = new ScrollViewer { Content = panel }, CloseButtonText = "閉じる" };
        if (sheet.IsEmpty) panel.Children.Add(new TextBlock { Text = "回数・偏りの違反はありません。" });
        foreach (var l in sheet.C1) panel.Children.Add(new TextBlock { Text = l, TextWrapping = TextWrapping.Wrap });
        if (CellSheetLogic.PeriodLine(ui.StartDate, ui.Days) is { } period) panel.Children.Add(new TextBlock { Text = period, FontSize = 13, Opacity = 0.8 });
        // 選んだ勤務の回数の問題を同じダイアログで探す。切り替えたら前の探索の購読を外してから付け直す（閉じたときも外す）。
        // 行を選ぶまで案は探さない。選べる行が 1 つだけなら最初から選んでおく（Android 3.683.0）。
        int? pick = focus.Staff is not null ? CellSheetLogic.StaffSheetPreselect(sheet.Chips.Select(c => c.ShiftIdx)) : null;
        var general = false;
        var detach = new List<Action>();
        var fixHost = new StackPanel { Spacing = 6 };
        var rows = new StackPanel { Spacing = 4 };
        void Detach() { foreach (var a in detach.ToList()) a(); detach.Clear(); }
        dialog.Closed += (_, _) => Detach();
        void Mount()
        {
            Detach();
            fixHost.Children.Clear();
            if (pick is { } k && focus.Staff is { } i)
            {
                // 選んだ勤務の回数の問題: 見出し（あと N 回）と、その回数を変える案。
                if (CellSheetLogic.CountFixTarget(_vm.Ui.CountViolations, i, k, _vm.StaffCellLimits(i, k)) is { } t)
                    foreach (var h in CellSheetLogic.FixTargetHead(t, t.CountOf(_vm.Ui.Schedule)))
                        fixHost.Children.Add(new TextBlock { Text = h, FontWeight = Microsoft.UI.Text.FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
                fixHost.Children.Add(AttachFixSearch(dialog.Hide, a => detach.Add(a), new FixFocus(i, k)));
            }
            else
            {
                if (c1Stuck) fixHost.Children.Add(C1StuckButtons(() => dialog.Hide()));
                var fold = new HyperlinkButton { Content = general ? "この職員のほかの変更案を閉じる" : CellSheetLogic.StaffOtherFixesLabel, MinHeight = 48 };
                fold.Click += (_, _) => { general = !general; Mount(); };
                fixHost.Children.Add(fold);
                if (general) fixHost.Children.Add(AttachFixSearch(dialog.Hide, a => detach.Add(a), focus));
            }
        }
        void RenderRows()
        {
            rows.Children.Clear();
            // 2026-10-14 利用者レビュー: 札「Dﾃ +2回 (6/4)」を、現在・基準・過不足を全文で読める表にし、行から案へ進む。
            foreach (var c in sheet.Chips)
            {
                var (bg, fg) = CountChipBrushes(c.Under);
                var row = new Grid { ColumnSpacing = 8 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock
                {
                    Text = c.RowText, Foreground = (Brush)res[fg], TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = pick == c.ShiftIdx ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
                });
                if (c.ShiftIdx >= 0)
                {
                    FrameworkElement act;
                    if (pick == c.ShiftIdx) act = new TextBlock { Text = "選択中", Foreground = (Brush)res[fg], FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
                    else
                    {
                        var see = new HyperlinkButton { Content = "見る", MinHeight = 48 };
                        var k = c.ShiftIdx;
                        see.Click += (_, _) => { pick = k; RenderRows(); Mount(); };
                        act = see;
                    }
                    Grid.SetColumn(act, 1);
                    row.Children.Add(act);
                }
                rows.Children.Add(new Border { Background = (Brush)res[bg], CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 0, 8, 0), MinHeight = 36, Child = row });
            }
        }
        if (sheet.Chips.Count > 0)
        {
            panel.Children.Add(new TextBlock { Text = "回数の過不足", Style = (Style)res["BodyStrongTextBlockStyle"] });
            panel.Children.Add(new TextBlock { Text = "どの勤務回数を調整しますか？", TextWrapping = TextWrapping.Wrap });
            RenderRows();
            panel.Children.Add(rows);
        }
        if (sheet.Weekly.Count > 0)
        {
            var list = new StackPanel { Spacing = 2 };
            foreach (var w in sheet.Weekly) list.Children.Add(new TextBlock { Text = "・" + w, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new Expander
            {
                Header = $"{LabelOf("weekly")}（{sheet.Weekly.Count}件）", IsExpanded = false, MinHeight = 48,
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = new ScrollViewer { MaxHeight = 160, Content = list },
            });
        }
        if (sheet.Fair.Count > 0)
        {
            panel.Children.Add(new TextBlock { Text = LabelOf("fair"), Style = (Style)res["BodyStrongTextBlockStyle"] });
            foreach (var f in sheet.Fair) panel.Children.Add(new TextBlock { Text = f, TextWrapping = TextWrapping.Wrap });
        }
        panel.Children.Add(fixHost);
        Mount();
        _ = dialog.ShowAsync();
    }

    private void ShowMarkDialog(string title, IReadOnlyList<string> lines, FixFocus? focus, bool c1Stuck = false)
    {
        var panel = new StackPanel { Spacing = 4 };
        foreach (var l in lines) panel.Children.Add(new TextBlock { Text = l, TextWrapping = TextWrapping.Wrap });
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = title, Content = new ScrollViewer { Content = panel }, CloseButtonText = "閉じる" };
        if (c1Stuck) panel.Children.Add(C1StuckButtons(() => dialog.Hide()));
        if (focus is not null) panel.Children.Add(AttachFixSearch(dialog, focus));
        _ = dialog.ShowAsync();
    }

    /// <summary>違反のあるセル: 違反の一覧・この職員の回数・偏り・開いた時点で始める直し方探し（Kotlin セルシート）と、
    /// その下にいつでも使える手動の割当ボタン。閉じると探索を取り消す。</summary>
    private CancellationTokenSource? _marksCts;
    /// <summary>セル編集の全体表示（既定＝ちら見。閉じると戻す）。</summary>
    private bool _cellSheetExpanded;
    // ちら見の「変更案を探す」は大きく開いて、このセルの変更案の一覧を開いた状態から始める。
    private bool _searchOnOpen;

    /// <summary>
    /// セル編集（Android <c>CellEditSheet</c> の移植、2026-09-25。3.670.0 で 3 段階）: ① 問題を確認（今の勤務・問題の一文・並びの日々・
    /// どのように調整するか・今月の勤務回数・手動固定・前日/翌日）→ ② 自分で勤務を選ぶ（固定配置のシフトボタン。担当外は「外」、利き手で左右反転、
    /// 右上の角に印）→ ③ 影響を確認して適用。選んだだけでは反映しない。希望どおりのセルに違反があれば「他の人で補う（推奨）」を先に出す。
    /// </summary>
    /// <param name="step">割当（mode 0）の段: 0＝問題を確認、1＝自分で勤務を選ぶ、2＝影響を確認して適用（<paramref name="pickTo"/> へ変える）。</param>
    private void ShowCellEditor(FrameworkElement anchor, int i, int j, int mode = 0, int step = 0, int? pickTo = null)
    {
        if (_vm.EditBlockedNow()) return;
        var ui = _vm.Ui;
        if (i < 0 || i >= ui.Schedule.Count || j < 0 || j >= ui.Days) return;
        var canDo = CellSheetLogic.SheetCanDo(_vm.AllowedByStaff(), i, ui.ShiftSymbols.Count);
        var cur = ui.Schedule[i][j];
        int? wish = ui.Wishes.TryGetValue($"{i},{j}", out var w) ? w : null;
        string Sym(int? k) => k is { } kk && kk >= 0 && kk < ui.ShiftSymbols.Count ? ui.ShiftSymbols[kk] : "—";
        var name = i < ui.StaffNames.Count ? ui.StaffNames[i] : $"#{i}";
        var status = _vm.CellStatusFor(i, j, LabelOf);
        var dilemma = CellSheetLogic.IsWishDilemma(wish, cur, status.Severity);
        var relax = _vm.RelaxTrialFor();
        var handoff = CellSheetLogic.RelaxHandoffOf(relax?.Result, ui.RelaxSearching, _vm.RelaxNoWall(), i, j, _vm.RelaxStopped(), _vm.RelaxUnavailable(), _vm.RelaxFailed());
        var panel = new StackPanel { Spacing = 6, MaxWidth = 400 };
        var flyout = new Flyout { XamlRoot = anchor.XamlRoot, Content = new ScrollViewer { Content = panel, MaxHeight = 620 } };
        var reopening = false;
        void Reopen(int ni, int nj, int m, int st = 0, int? to = null) { reopening = true; flyout.Hide(); ShowCellEditor(anchor, ni, nj, m, st, to); }
        flyout.Closed += (_, _) => { if (!reopening) _cellSheetExpanded = false; };

        // 見出し行の［↶ 元に戻す］（Kotlin CellEditSheet の SheetUndoButton）。有効条件は下部バーと同じ（CanUndo かつ実行中でない）。
        //   戻すと盤面が変わるのでシートを開き直す。
        Button MakeUndoButton(int column)
        {
            var u = new Button { Content = "↶", MinHeight = 48, MinWidth = 48, IsEnabled = ui.CanUndo && !ui.Running };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(u, "元に戻す");
            ToolTipService.SetToolTip(u, "元に戻す");
            u.Click += (_, _) => { _vm.Undo(); Reopen(i, j, mode); };
            Grid.SetColumn(u, column);
            return u;
        }
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(new TextBlock { Text = CellSheetLogic.CellSheetTitle, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        // 巡回は違反単位（Kotlin hardViolationItems）: このセルが属する件を「必須違反 k / N ・ 見出し」で示し、「次の違反 ▶」は次の件の起点セルへ。
        //   このセルがどの件にも属さなければ先頭の件へ（別の起点ボタンは無い＝旧 ViolationTour（セル単位）は据え置き）。
        var tourItems = _vm.HardViolationItemsFor(LabelOf);
        var tourAt = tourItems.ToList().FindIndex(t => t.Staff == i && t.Days.Contains(j));
        // 対象の行と札（S-2）: 今の勤務はここで言う。巡回中の必須は「必須 2/5」。
        var target = CellSheetLogic.CellSheetTarget(name, ScheduleUtil.FormatDay(ui.StartDate, j), cur >= 0 ? Sym(cur) : null);
        var badge = CellSheetLogic.CellSheetBadge(status.Severity, tourAt >= 0 ? tourAt : null, tourItems.Count);
        FrameworkElement? BadgeChip() => badge is null ? null : AnalysisView.TagChip(badge, status.Severity == CellSeverity.Hard ? MagiAccent.Red : MagiAccent.Orange);
        // 巡回中にシートを開いている間は、下部バーの違反ナビを隠す（シートの［◀ 前］［次 ▶］と重複）。
        SetVioNavHidden(tourAt >= 0);
        flyout.Closed += (_, _) => { if (!reopening) SetVioNavHidden(false); };
        var tourNext = tourItems.Count == 0 ? null : tourItems[tourAt < 0 ? 0 : (tourAt + 1) % tourItems.Count];
        if (tourNext is not null && (tourItems.Count > 1 || tourAt < 0) && step == 0)
        {
            var tour = new Button { Content = "次の違反 ▶", MinHeight = 48 };
            var nx = tourNext.Cell;
            tour.Click += (_, _) => { FocusCell(nx.I, nx.J); Reopen(nx.I, nx.J, 0); };
            Grid.SetColumn(tour, 1);
            head.Children.Add(tour);
        }
        head.Children.Add(MakeUndoButton(2));
        panel.Children.Add(head);
        var targetRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (BadgeChip() is { } headChip) targetRow.Children.Add(headChip);
        targetRow.Children.Add(new TextBlock { Text = target, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        panel.Children.Add(targetRow);
        if (step == 0 && CellSheetLogic.TourHeading(tourItems, tourAt) is not null)
        {
            if (CellSheetLogic.TourCovULine(ui.Breakdown.GetValueOrDefault("covU")) is { } covLine)
                panel.Children.Add(new TextBlock { Text = covLine, FontSize = 13, Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
        }

        if (!_cellSheetExpanded)
        {
            ShowPeek();
            return;
        }
        var collapse = new Button { Content = "小さく表示 ▾", MinHeight = 48, HorizontalAlignment = ui.LeftHand ? HorizontalAlignment.Left : HorizontalAlignment.Right };
        collapse.Click += (_, _) => { _cellSheetExpanded = false; Reopen(i, j, mode); };
        panel.Children.Insert(0, collapse);

        // ちら見（Kotlin PeekBody）: ①違反の 1 行＋✕ ②対象＋前/次 ③希望を外す・S6 の既存の推奨・上位 4 シフト・詳しく ▸。
        void ShowPeek()
        {
            var peek = new StackPanel { Spacing = 4, MaxWidth = 400 };
            var r1 = new Grid();
            r1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            r1.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            r1.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            r1.Children.Add(MakeUndoButton(1));
            r1.Children.Add(new TextBlock
            {
                Text = target, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
            });
            var x = new Button { Content = "✕", MinHeight = 48, MinWidth = 48 };
            ToolTipService.SetToolTip(x, "閉じる");
            x.Click += (_, _) => flyout.Hide();
            Grid.SetColumn(x, 2);
            r1.Children.Add(x);
            peek.Children.Add(r1);
            var r2 = new Grid();
            r2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            r2.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            // 2 行目＝札＋原因（S-2）。
            var causeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            if (BadgeChip() is { } peekChip) { peekChip.VerticalAlignment = VerticalAlignment.Center; causeRow.Children.Add(peekChip); }
            causeRow.Children.Add(new TextBlock
            {
                Text = badge is null ? "このセルに違反はありません" : status.Cause, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
                Foreground = status.Severity == CellSeverity.Hard ? new SolidColorBrush(ColorHex.Parse(MagiAccent.Red, Colors.Red)) : null,
            });
            r2.Children.Add(causeRow);
            if (tourAt >= 0 && tourItems.Count > 1)
            {
                var nav = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                var pv = tourItems[(tourAt - 1 + tourItems.Count) % tourItems.Count].Cell;
                var nx = tourItems[(tourAt + 1) % tourItems.Count].Cell;
                var pb = new Button { Content = "◀ 前", MinHeight = 48 };
                var nb = new Button { Content = "次 ▶", MinHeight = 48 };
                pb.Click += (_, _) => { FocusCell(pv.I, pv.J); Reopen(pv.I, pv.J, 0); };
                nb.Click += (_, _) => { FocusCell(nx.I, nx.J); Reopen(nx.I, nx.J, 0); };
                nav.Children.Add(pb);
                nav.Children.Add(nb);
                Grid.SetColumn(nav, 1);
                r2.Children.Add(nav);
            }
            peek.Children.Add(r2);
            // 希望を外す・緩める候補は幅が足りないので上の行へ折り返す（横スクロールしない）。
            var extra = new StackPanel { Orientation = Orientation.Horizontal };
            var acts = new StackPanel { Orientation = Orientation.Horizontal };
            // 希望を外すのは希望の登録中だけ（割当のちら見では、シフトの押し違いで希望を消さない）。
            if (wish is not null && mode == 1)
            {
                var rw = new Button { Content = CellSheetLogic.RemoveWishButton, MinHeight = 48, Margin = new Thickness(0, 0, 6, 0) };
                rw.Click += (_, _) => { _vm.RemoveWish(i, j); Reopen(i, j, mode); };
                extra.Children.Add(rw);
            }
            _searchOnOpen = false;
            int? peekRecommend = mode == 0 && CellSheetLogic.PeekRecommendation(relax?.Result, i, j) is { } pr && pr != cur && canDo.Contains(pr) ? pr : null;
            if (peekRecommend is { } rec)
            {
                // このセルだけを変えず、上限の緩和と手順をまとめて確定するダイアログを開く。
                var rb = new Button { Content = NextActionGuide.RelaxPeekLabel(Sym(rec)), MinHeight = 48, Margin = new Thickness(0, 0, 6, 0) };
                rb.Click += (_, _) => { flyout.Hide(); _showRelax?.Invoke((i, j)); };
                extra.Children.Add(rb);
            }
            var picks = CellSheetLogic.PeekShifts(_vm.SheetShifts(), canDo, cur, wish).Take(CellSheetLogic.PeekPickCount((int)peek.MaxWidth - 32)).ToList();
            var zc = _vm.ZeroCapShiftsFor(i);
            var marks = new Dictionary<int, TextBlock>();
            foreach (var k in ui.LeftHand ? Enumerable.Reverse(picks) : picks)
            {
                var sel = mode == 0 ? k == cur : k == wish;
                var sbg = k < ui.ShiftColorHex.Count ? ParseHexColor(ui.ShiftColorHex[k], Colors.LightGray) : Colors.Gainsboro;
                var sfg = k < ui.ShiftTextHex.Count ? ParseHexColor(ui.ShiftTextHex[k], Colors.Black) : Colors.DimGray;
                var g = new Grid();
                var lab = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                lab.Children.Add(new TextBlock { Text = (sel ? "✓ " : "") + Sym(k), Foreground = new SolidColorBrush(sfg), FontWeight = Microsoft.UI.Text.FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center });
                if (zc.Contains(k)) lab.Children.Add(new TextBlock { Text = "上限0", FontSize = 11, Foreground = new SolidColorBrush(sfg), HorizontalAlignment = HorizontalAlignment.Center });
                g.Children.Add(lab);
                var mk = new TextBlock { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
                g.Children.Add(mk);
                marks[k] = mk;
                var b = new Button { Content = g, MinHeight = 48, Width = 56, Margin = new Thickness(0, 0, 6, 0), HorizontalContentAlignment = HorizontalAlignment.Stretch, Background = new SolidColorBrush(sbg), BorderThickness = new Thickness(sel ? 4 : 1) };
                var kk = k;
                b.Click += (_, _) =>
                {
                    // 割当では選んだだけで反映しない＝全体表示の ③ 影響の確認へ（Kotlin 3.670.0）。
                    if (mode == 0) { if (kk != cur) { _cellSheetExpanded = true; Reopen(i, j, 0, 2, kk); } }
                    else { _vm.SetWish(i, j, kk); Reopen(i, j, 1); }
                };
                acts.Children.Add(b);
            }
            var more = new Button { Content = "詳しく ▸", MinHeight = 48, MinWidth = 64 };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(more, "問題の説明と直し方を表示");
            more.Click += (_, _) => { _cellSheetExpanded = true; Reopen(i, j, mode); };
            acts.Children.Add(more);
            peek.Children.Add(extra);
            peek.Children.Add(acts);
            // 主ボタンはシフト行の下に全幅で 1 つ（S-3・INV-09）。押すとこのセルの変更案の一覧を開いた状態で広げる（INV-01）。
            if (CellSheetLogic.PeekSearchAction(mode, status.Severity) is { } searchLabel)
            {
                var sb = new Button { Content = searchLabel, MinHeight = 48, HorizontalAlignment = HorizontalAlignment.Stretch, Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
                sb.Click += (_, _) => { _cellSheetExpanded = true; _searchOnOpen = true; Reopen(i, j, 0); };
                peek.Children.Add(sb);
            }
            var riskNote = new TextBlock { Text = CellSheetLogic.PeekHardRiskNote, FontSize = 12, Opacity = 0.8, Visibility = Visibility.Collapsed };
            peek.Children.Add(riskNote);
            flyout.Content = peek;
            _marksCts?.Cancel();
            var pc = new CancellationTokenSource();
            _marksCts = pc;
            flyout.Closed += (_, _) => pc.Cancel();
            if (mode == 0) _ = FillPeekMarksAsync();
            async Task FillPeekMarksAsync()
            {
                try
                {
                    var m = await _vm.ShiftMarksForAsync(i, j, status.Severity, pc.Token);
                    if (pc.IsCancellationRequested) return;
                    foreach (var (k, tb) in marks)
                    {
                        if (m.HardRisk.Contains(k)) { tb.Text = "⚠"; tb.Foreground = new SolidColorBrush(ColorHex.Parse(MagiAccent.Red, Colors.Red)); }
                        else if (m.Recommended.Contains(k)) { tb.Text = "●"; tb.Foreground = new SolidColorBrush(ColorHex.Parse(MagiAccent.Green, Colors.Green)); }
                    }
                    if (marks.Keys.Any(m.HardRisk.Contains)) riskNote.Visibility = Visibility.Visible;
                }
                catch (OperationCanceledException) { }
            }
            flyout.ShowAt(anchor);
        }

        // 3 段階（Kotlin CellEditSheet、2026-10-14 利用者レビュー）: step 0＝問題を確認、1＝自分で勤務を選ぶ、2＝影響を確認して適用。選んだだけでは反映しない。
        //   段の切替はシートを開き直す（Reopen）＝ほかの切替（割当/希望・前日/翌日）と同じ形。
        // このセルが足りない側（当日の自分の勤務の不足・自分の回数の不足）の族は、このセルの問題として赤で出さない。
        var shortSide = _vm.CellShortSideFamilies(i, j);
        var fams = _vm.CellProblemFamilies(i, j).Select(f => f.StartsWith("vio-", StringComparison.Ordinal) ? f[4..] : f).Where(f => !shortSide.Contains(f)).ToList();
        var pinned = ui.ManualPins.Contains($"{i},{j}");
        var left = ui.LeftHand;
        var zeroCaps = _vm.ZeroCapShiftsFor(i);
        var marksByShift = new Dictionary<int, TextBlock>();
        var dilemmaChoice = mode == 2 ? 2 : 0;
        if (mode == 2) mode = 0;
        TextBlock Caption(string t) => new() { Text = t, FontSize = 12, Opacity = 0.8, Margin = new Thickness(0, 4, 0, 0) };
        TextBlock Body(string t, bool bold = false, double size = 14) => new()
            { Text = t, FontSize = size, TextWrapping = TextWrapping.Wrap, FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal };
        Button Wide(string text, bool accent = false)
        {
            var b = new Button { Content = text, MinHeight = 48, HorizontalAlignment = HorizontalAlignment.Stretch };
            if (accent) b.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
            return b;
        }

        // シフトのボタン（② 自分で勤務を選ぶ・希望の画面）。割当では選ぶと ③ 影響の確認へ（この時点では反映しない）。
        void AddShiftGrid()
        {
            var grid = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
            for (var c = 0; c < CellSheetLogic.Columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var slots = CellSheetLogic.Slots(_vm.SheetShifts(), canDo, left);
            for (var r = 0; r < slots.Count; r++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                for (var c = 0; c < CellSheetLogic.Columns; c++)
                {
                    if (slots[r][c] is not { } sl) continue;
                    var k = sl.Shift;
                    var sel = mode == 0 ? k == cur : k == wish;
                    var sbg = sl.CanDo && k < ui.ShiftColorHex.Count ? ParseHexColor(ui.ShiftColorHex[k], Colors.LightGray) : Colors.Gainsboro;
                    var sfg = sl.CanDo && k < ui.ShiftTextHex.Count ? ParseHexColor(ui.ShiftTextHex[k], Colors.Black) : Colors.DimGray;
                    var content = new Grid();
                    var label = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    label.Children.Add(new TextBlock { Text = (sel ? "✓ " : "") + Sym(k) + (sl.CanDo ? "" : " 外"), Foreground = new SolidColorBrush(sfg), FontWeight = Microsoft.UI.Text.FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center });
                    if (zeroCaps.Contains(k)) label.Children.Add(new TextBlock { Text = "上限0", FontSize = 11, Foreground = new SolidColorBrush(sfg), HorizontalAlignment = HorizontalAlignment.Center });
                    content.Children.Add(label);
                    var mark = new TextBlock { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
                    content.Children.Add(mark);
                    marksByShift[k] = mark;
                    var btn = new Button
                    {
                        Content = content, MinHeight = 52, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                        Background = new SolidColorBrush(sbg), BorderThickness = new Thickness(sel ? 4 : 1), IsEnabled = mode == 1 || sl.CanDo,
                    };
                    var kk = k;
                    btn.Click += (_, _) =>
                    {
                        if (mode == 0) { if (kk != cur) Reopen(i, j, 0, 2, kk); }
                        else { _vm.SetWish(i, j, kk); Reopen(i, j, 1); }
                    };
                    Grid.SetRow(btn, r);
                    Grid.SetColumn(btn, c);
                    grid.Children.Add(btn);
                }
            }
            panel.Children.Add(grid);
        }

        var countsPanel = new StackPanel { Spacing = 2 };

        var moreOps = new StackPanel { Spacing = 8, Visibility = Visibility.Collapsed };
        var problemPanel = new StackPanel { Spacing = 4 };
        var singleNote = new TextBlock { Text = CellSheetLogic.SingleCellNote, TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Visibility = Visibility.Collapsed };
        var riskReason = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Visibility = Visibility.Collapsed };
        var ctxText = new TextBlock { Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 260 };
        string CtxLine()
        {
            var u = _vm.Ui;
            int? wNow = u.Wishes.TryGetValue($"{i},{j}", out var wv) ? wv : null;
            return CellSheetLogic.WishTabLine(wNow, cur, u.ExtBanned.TryGetValue($"{i},{j}", out var eb) ? eb : null, k => Sym(k), u.ManualPins.Contains($"{i},{j}"));
        }
        // ① の問題の一文・並びの日々と今月の勤務回数（検査の完了で同じ盤面から作り直す）。
        void FillProblem()
        {
            problemPanel.Children.Clear();
            // [Android 3.683.0] 1 行目＝状態（重さの色）、2 行目＝一般の説明、今の勤務。
            if (status.Severity != CellSeverity.None)
            {
                var stLine = Body(status.Text.StartsWith("⚠ ", StringComparison.Ordinal) ? status.Text[2..] : status.Text, bold: true);
                if (status.Severity == CellSeverity.Hard) stLine.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["MagiErrorBrush"];
                problemPanel.Children.Add(stLine);
            }
            if (fams.Count > 0) problemPanel.Children.Add(Body(CellSheetLogic.CellRuleLine(fams[0], LabelOf), size: 13));
            if (dilemma) problemPanel.Children.Add(Body(CellSheetLogic.WishKeptLine(Sym(wish)), size: 13));
            var shortCaution = _vm.ShortSideCautionFor(i, j, name);
            if (shortCaution is not null) problemPanel.Children.Add(Body(shortCaution));
            if (fams.Count == 0 && shortCaution is null) problemPanel.Children.Add(Body("このセルに違反はありません。"));
            var partnersNow = status.Severity == CellSeverity.Hard ? _vm.ViolationPartnerDaysFor(i, j) : Array.Empty<int>();
            if (CellSheetLogic.SequenceLine(_vm.Ui.StartDate, _vm.Ui.Schedule[i], partnersNow.Append(j).Distinct().ToList(), _vm.Ui.ShiftSymbols) is { } seq)
                problemPanel.Children.Add(Body(seq));
        }
        void FillCounts()
        {
            countsPanel.Children.Clear();
            // 回数の目標未満を、勤務を減らす前に見える位置に残す。
            var counts = CellSheetLogic.CellCountLines(_vm.Ui, i, cur, _vm.StaffCellLimits);
            if (!CellSheetLogic.ShowCountSection(_vm.Ui.CountFamilies.Keys.Concat(_vm.Ui.CountViolations.Keys), i, counts)) return;
            countsPanel.Children.Add(Caption(CellSheetLogic.CountSectionTitle));
            foreach (var l in counts) countsPanel.Children.Add(Body(l, size: 13));
        }

        if (mode == 0 && step == 2 && pickTo is { } to && _vm.CellChangeReviewFor(i, j, to, LabelOf) is { } review)
        {
            // ③ 影響を確認して適用（値はすべて正式な検査で評価した結果）。
            panel.Children.Add(Body(ChainFixPreview.PreviewTitle, bold: true));
            panel.Children.Add(Body(review.Change, bold: true));
            if (review.Verdict is { } verdict)
            {
                var vb = Body(verdict, bold: true);
                if (review.VerdictBad) vb.Foreground = new SolidColorBrush(ColorHex.Parse(MagiAccent.Red, Colors.Red));
                panel.Children.Add(vb);
            }
            void Section(string title, IReadOnlyList<string> lines)
            {
                if (lines.Count == 0) return;
                panel.Children.Add(Caption(title));
                foreach (var l in lines) panel.Children.Add(Body(l, size: 13));
            }
            Section("前後の勤務", new[] { review.Neighbors });
            Section("今回の問題", review.ThisCell.Concat(review.OthersResolved.Select(x => $"解消: {x}")).ToList());
            Section(CellSheetLogic.ReviewAppearedHeading, review.Appeared.Count > 0 ? review.Appeared : new[] { "ありません" });
            panel.Children.Add(Body(review.HardLine));
            Section("人数と回数への影響", review.Headcount.Concat(review.Counts).ToList());
            Section("希望・固定への影響", review.WishPin);
            // 適用と戻る（比較の部分はシートごと縦に送る）。
            var apply = Wide(ChainFixPreview.ApplyChangeButton, accent: true);
            apply.IsEnabled = !ui.Running;
            apply.Click += (_, _) => { _vm.SetCell(i, j, to); Reopen(i, j, 0); };
            panel.Children.Add(apply);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var item = ConsultList.CellChange(review, i, j, name, ConsultList.IsoDate(ui.StartDate, j));
            if (ConsultList.IsConsulted(ui.Consults, item)) row.Children.Add(AnalysisView.TagChip(ConsultList.Done, MagiAccent.Orange));
            else
            {
                var consult = new Button { Content = ConsultList.Button, MinHeight = 48 };
                consult.Click += (_, _) => { _vm.AddConsult(item); Reopen(i, j, 0, 2, to); };
                row.Children.Add(consult);
            }
            var other = new Button { Content = "別の勤務を選ぶ", MinHeight = 48 };
            other.Click += (_, _) => Reopen(i, j, 0, 1);
            row.Children.Add(other);
            panel.Children.Add(row);
            var keep = new HyperlinkButton { Content = CellSheetLogic.KeepUnchangedButton, MinHeight = 48, HorizontalAlignment = HorizontalAlignment.Stretch };
            keep.Click += (_, _) => Reopen(i, j, 0);
            panel.Children.Add(keep);
        }
        else if (mode == 0 && step >= 1)
        {
            // ② 自分で勤務を選ぶ: 選ぶと影響の確認へ（この時点では反映しない）。
            panel.Children.Add(Body($"{DayText.Full(ui.StartDate, j)}の変更先を選択", bold: true));
            panel.Children.Add(Body($"対象：{name}さん　現在：{Sym(cur)}"));
            panel.Children.Add(Body("選ぶと変更後の影響を確認できます。この時点では反映しません。", size: 13));
            if (_vm.SheetShifts().Any(k => !canDo.Contains(k))) panel.Children.Add(Body(CellSheetLogic.NotCanDoNote, size: 13));
            AddShiftGrid();
            var back = Wide("元の画面に戻る");
            back.Click += (_, _) => Reopen(i, j, 0);
            panel.Children.Add(back);
        }
        else
        {
            if (mode == 0)
            {
                // ① 問題を確認: 今の勤務・問題の一文・並びの日々、ほかの問題は畳んで全部見られるように。
                FillProblem();
                panel.Children.Add(problemPanel);
            }
            else
            {
                var statusBox = new StackPanel { Padding = new Thickness(10, 8, 10, 8), CornerRadius = new CornerRadius(12), Background = (Brush)Application.Current.Resources["MagiSurfaceVariantBrush"] };
                statusBox.Children.Add(new TextBlock { Text = status.Text, TextWrapping = TextWrapping.Wrap, MaxLines = 2 });
                panel.Children.Add(statusBox);
            }
            var partners = status.Severity == CellSeverity.Hard ? _vm.ViolationPartnerDaysFor(i, j) : Array.Empty<int>();
            // [3.714.0] 問題が 1 つでも説明の文は「詳しく見る」の中（主は「変更案を探す」＝これは文字ボタン）。
            if (mode == 0 && fams.Count >= 1)
            {
                var details = new StackPanel { Spacing = 2, Visibility = Visibility.Collapsed };
                details.Children.Add(Body(CellSheetLogic.CellProblemSentence(fams[0], LabelOf), size: 13));
                foreach (var f in fams.Skip(1)) details.Children.Add(Body("・" + CellSheetLogic.CellProblemSentence(f, LabelOf), size: 13));
                foreach (var l in _vm.CellDetailLinesFor(i, j, LabelOf)) details.Children.Add(Body(l, size: 13));
                if (_vm.RelatedCellsLineFor(i, j, partners) is { } related) details.Children.Add(new TextBlock { Text = related, FontSize = 13, Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
                var openLabel = fams.Count >= 2 ? $"関連する問題をすべて見る：ほか{fams.Count - 1}件" : CellSheetLogic.CellDetailButton;
                var closeLabel = fams.Count >= 2 ? "関連する問題を閉じる" : "閉じる";
                var toggle = new HyperlinkButton { Content = openLabel, MinHeight = 48 };
                toggle.Click += (_, _) =>
                {
                    var open = details.Visibility != Visibility.Visible;
                    details.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                    toggle.Content = open ? closeLabel : openLabel;
                };
                panel.Children.Add(toggle);
                panel.Children.Add(details);
            }
            panel.Children.Add(singleNote);
            panel.Children.Add(riskReason);
            if (mode == 0 && _vm.C1ShortageAt(i, j) is { Stuck: true }) panel.Children.Add(C1StuckButtons(() => flyout.Hide(), (i, j)));
            if (mode == 0 && dilemma && dilemmaChoice == 0)
            {
                // 板挟み: 他の人で補う（推奨）／希望は残して別のシフトにする。
                var others = Wide("他の人で補う（推奨）", accent: true);
                var breakWish = new Button { Content = new TextBlock { Text = "希望は残して別のシフトにする", TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center }, MinHeight = 48, HorizontalAlignment = HorizontalAlignment.Stretch };
                panel.Children.Add(others);
                panel.Children.Add(breakWish);
                var slot = new StackPanel();
                panel.Children.Add(slot);
                // 他の人の手が無いとき、同じ違反のもう一方のセルへ（希望は触らない）。
                IEnumerable<UIElement> PartnerButtons()
                {
                    foreach (var d in partners)
                    {
                        var b = new Button { Content = new TextBlock { Text = CellSheetLogic.PartnerCellLabel(_vm.Ui.StartDate, d, partners.Count == 1), TextWrapping = TextWrapping.Wrap }, MinHeight = 48 };
                        var dd = d;
                        b.Click += (_, _) => Reopen(i, dd, 0);
                        yield return b;
                    }
                }
                others.Click += (_, _) => { slot.Children.Clear(); slot.Children.Add(AttachFixSearch(flyout.Hide, onClosed => flyout.Closed += (_, _) => onClosed(), new FixFocus(null, null, j, i), null, PartnerButtons, quick: true, cell: (i, j))); };
                breakWish.Click += (_, _) => Reopen(i, j, 2);
            }
            else if (mode == 0)
            {
                panel.Children.Add(Body("どのように調整しますか？", bold: true));
                // 足りない側のセルは、このセルを変えるより当日のその勤務に入れる人を探すのが先。
                // 直し方の一覧は一度に 1 つだけ開く（片方を開くともう片方は閉じる）。
                Action closeShort = () => { }, closeCell = () => { };
                if (shortSide.Contains("covU"))
                {
                    var shortSlot = new StackPanel();
                    var shortSearch = Wide(CellSheetLogic.ShortSideSearchButton(DayText.Short(ui.StartDate, j), Sym(cur)), accent: CellSheetLogic.ShortSideFilled(fams));
                    closeShort = () => { shortSlot.Children.Clear(); shortSearch.Visibility = Visibility.Visible; };
                    shortSearch.Click += (_, _) =>
                    {
                        closeCell();
                        shortSearch.Visibility = Visibility.Collapsed;
                        shortSlot.Children.Add(AttachFixSearch(flyout.Hide, onClosed => flyout.Closed += (_, _) => onClosed(), new FixFocus(null, cur, j), quick: true));
                    };
                    panel.Children.Add(shortSearch);
                    panel.Children.Add(shortSlot);
                }
                if (status.Severity != CellSeverity.None)
                {
                    // 変更案は押してから探す（開いただけでは探さない）。
                    var searchSlot = new StackPanel();
                    // 主は 1 つ（INV-09）: 人員不足だけのセルは足りない側の人探しが主。
                    var search = Wide(CellSheetLogic.PeekSearchButton, accent: !(shortSide.Contains("covU") && CellSheetLogic.ShortSideFilled(fams)));
                    var searchNote = new TextBlock { Text = "この日・前後の日・関連する勤務の変更案を比較します。", FontSize = 13, Opacity = 0.8, TextWrapping = TextWrapping.Wrap };
                    closeCell = () => { searchSlot.Children.Clear(); search.Visibility = Visibility.Visible; searchNote.Visibility = Visibility.Visible; };
                    void OpenCellSearch()
                    {
                        closeShort();
                        search.Visibility = Visibility.Collapsed;
                        searchNote.Visibility = Visibility.Collapsed;
                        searchSlot.Children.Add(AttachFixSearch(flyout.Hide, onClosed => flyout.Closed += (_, _) => onClosed(), new FixFocus(i, null, j), quick: true, cell: (i, j),
                            problems: _vm.CellProblemFamilies(i, j), resolveAt: (i, j),
                            primaryElsewhere: shortSide.Contains("covU") && CellSheetLogic.ShortSideFilled(fams)));   // 足りない側の塗りのボタンが上に出ている
                    }
                    search.Click += (_, _) => OpenCellSearch();
                    panel.Children.Add(search);
                    panel.Children.Add(searchNote);
                    panel.Children.Add(searchSlot);
                    if (_searchOnOpen) { _searchOnOpen = false; OpenCellSearch(); }
                }
                var pick = new HyperlinkButton { Content = "自分で勤務を選ぶ", MinHeight = 48, HorizontalAlignment = HorizontalAlignment.Stretch };
                pick.Click += (_, _) => Reopen(i, j, 0, 1);
                panel.Children.Add(pick);
                var wishBtn = Wide("希望を確認・登録する");
                wishBtn.Click += (_, _) => Reopen(i, j, 1);
                moreOps.Children.Add(wishBtn);
            }
            if (mode == 1 && wish is not null)
            {
                if (_vm.WishTabInvolvedLineFor(i, j) is { } involved) panel.Children.Add(Body(involved, size: 13));
                if (zeroCaps.Contains(wish.Value)) panel.Children.Add(Body(CellSheetLogic.WishZeroCapLine(Sym(wish)), size: 13));
                panel.Children.Add(new TextBlock { Text = pinned ? CellSheetLogic.WishTabPinnedNote : NextActionGuide.WishTabKeepNote, FontSize = 13, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 });
            }
            if (mode == 0 && status.Severity == CellSeverity.Hard)
            {
                if (handoff == RelaxHandoff.Offer && relax is not null)
                {
                    panel.Children.Add(new TextBlock { Text = CellSheetLogic.RelaxHandoffLine(relax.Result, ui, LabelOf), TextWrapping = TextWrapping.Wrap });
                    var showRelax = new Button { Content = "緩める候補を見る", MinHeight = 48 };
                    showRelax.Click += (_, _) => { flyout.Hide(); _showRelax?.Invoke((i, j)); };
                    panel.Children.Add(showRelax);
                }
                else if (handoff == RelaxHandoff.Searching)
                    panel.Children.Add(new TextBlock { Text = NextActionGuide.RelaxSearchingText, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 });
                else if (handoff == RelaxHandoff.NoWall)
                    panel.Children.Add(new TextBlock { Text = NextActionGuide.RelaxNoWallText, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 });
                else if (handoff == RelaxHandoff.Unavailable)
                    panel.Children.Add(new TextBlock { Text = CellSheetLogic.RelaxUnavailableText(_vm.RelaxUnavailable() ?? ""), TextWrapping = TextWrapping.Wrap, Opacity = 0.8 });
                else if (handoff is RelaxHandoff.Stopped or RelaxHandoff.Failed)
                {
                    var row = new Grid { ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto } } };
                    row.Children.Add(new TextBlock { Text = handoff == RelaxHandoff.Failed ? NextActionGuide.RelaxFailedText : NextActionGuide.RelaxStoppedText, TextWrapping = TextWrapping.Wrap, Opacity = 0.8, VerticalAlignment = VerticalAlignment.Center });
                    var retry = new HyperlinkButton { Content = NextActionGuide.RelaxRetryLabel, MinHeight = 48 };
                    retry.Click += (_, _) => { _vm.RetryRelaxTrial(); Reopen(i, j, mode); };
                    Grid.SetColumn(retry, 1);
                    row.Children.Add(retry);
                    panel.Children.Add(row);
                }
            }
            if (mode == 0)
            {
                FillCounts();
                panel.Children.Add(countsPanel);
                // 手動固定は通常の修正操作と分け、何をするか・しないかを添える。
                if (cur >= 0)
                {
                    moreOps.Children.Add(Caption(CellSheetLogic.PinTitle));
                    moreOps.Children.Add(new TextBlock { Text = CellSheetLogic.PinNote, FontSize = 13, Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
                    var pin = new Button { Content = pinned ? "手動固定を外す" : "手動固定する", MinHeight = 48 };
                    pin.Click += (_, _) => { _vm.TogglePin(i, j); Reopen(i, j, 0); };
                    moreOps.Children.Add(pin);
                }
                if (status.Severity != CellSeverity.None)
                {
                    var memo = new HyperlinkButton { Content = "⚑ ルールの見直し候補に入れる" };
                    memo.Click += (_, _) => { flyout.Hide(); _vm.AddReviewMemo($"{name} {DayText.Full(ui.StartDate, j)} {Sym(cur)}：{status.Cause}"); };
                    moreOps.Children.Add(memo);
                }
                // [Android 3.683.0] 希望・手動固定・ルールの見直しは「ほかの操作」に畳む（主の操作を先に見せる）。
                if (moreOps.Children.Count > 0)
                {
                    var moreToggle = new HyperlinkButton { Content = CellSheetLogic.MoreOpsLabel, MinHeight = 48 };
                    moreToggle.Click += (_, _) =>
                    {
                        var nowOpen = moreOps.Visibility != Visibility.Visible;
                        moreOps.Visibility = nowOpen ? Visibility.Visible : Visibility.Collapsed;
                        moreToggle.Content = nowOpen ? "ほかの操作を閉じる" : CellSheetLogic.MoreOpsLabel;
                    };
                    panel.Children.Add(moreToggle);
                    panel.Children.Add(moreOps);
                }
                panel.Children.Add(new TextBlock { Text = CellSheetLogic.PickMarksLegend, FontSize = 13, Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
            }
            if (mode == 1)
            {
                // 希望の画面: ［割当に戻る］と希望の中身、シフトのボタン、希望を取り消す。
                var ctx = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                var backToAssign = new Button { Content = "割当に戻る", MinHeight = 48 };
                backToAssign.Click += (_, _) => Reopen(i, j, 0);
                ctx.Children.Add(backToAssign);
                ctxText.Text = CtxLine();
                ctx.Children.Add(ctxText);
                panel.Children.Add(ctx);
                AddShiftGrid();
                if (wish is not null)
                {
                    var remove = Wide("希望を取り消す");
                    remove.Click += (_, _) => { _vm.RemoveWish(i, j); Reopen(i, j, 1); };
                    panel.Children.Add(remove);
                }
            }
            else
            {
                // 利き手の側に寄せる（右手＝右寄せ、左手＝左寄せ。前日・翌日の順は変えない）。
                var days = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right };
                var prevL = CellSheetLogic.AdjacentDayLabel(ui.StartDate, ui.Days, j - 1);
                var nextL = CellSheetLogic.AdjacentDayLabel(ui.StartDate, ui.Days, j + 1);
                var prevB = new HyperlinkButton { Content = $"{prevL ?? "前日"}へ", MinHeight = 48, IsEnabled = prevL is not null };
                var nextB = new HyperlinkButton { Content = $"{nextL ?? "翌日"}へ", MinHeight = 48, IsEnabled = nextL is not null };
                prevB.Click += (_, _) => Reopen(i, j - 1, 0);
                nextB.Click += (_, _) => Reopen(i, j + 1, 0);
                days.Children.Add(prevB);
                days.Children.Add(nextB);
                panel.Children.Add(days);
            }
            var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right };
            var close = new HyperlinkButton { Content = "閉じる", MinHeight = 48, MinWidth = 160 };
            close.Click += (_, _) => flyout.Hide();
            bottom.Children.Add(close);
            panel.Children.Add(bottom);
        }

        _marksCts?.Cancel();
        var cts = new CancellationTokenSource();
        _marksCts = cts;
        flyout.Closed += (_, _) => cts.Cancel();
        var showMarks = mode == 0 && step != 2;
        if (showMarks) _ = FillMarksAsync();
        // 問題の一文・回数・印・希望の中身は、検査の完了（CheckRev）と希望の変化で同じ盤面から作り直す（元に戻す/やり直すも含む）。
        var refreshMode = mode;
        var refreshStep = step;
        void RefreshEvaluated()
        {
            if (cts.IsCancellationRequested) return;
            status = _vm.CellStatusFor(i, j, LabelOf);
            fams = _vm.CellProblemFamilies(i, j).Select(f => f.StartsWith("vio-", StringComparison.Ordinal) ? f[4..] : f).ToList();
            if (refreshMode == 0 && refreshStep == 0) { FillProblem(); FillCounts(); }
            if (refreshMode == 1) ctxText.Text = CtxLine();
            if (showMarks) _ = FillMarksAsync();
        }
        PropertyChangedEventHandler evalHandler = (_, e) =>
        {
            if (e.PropertyName is nameof(UiState.CheckRev) or nameof(UiState.Wishes) or nameof(UiState.ManualPins)) DispatcherQueue.TryEnqueue(RefreshEvaluated);
        };
        _vm.Ui.PropertyChanged += evalHandler;
        flyout.Closed += (_, _) => _vm.Ui.PropertyChanged -= evalHandler;
        async Task FillMarksAsync()
        {
            try
            {
                var m = await _vm.ShiftMarksForAsync(i, j, status.Severity, cts.Token);
                if (cts.IsCancellationRequested) return;
                var hopeless = mode == 0 && step == 0 && status.Severity != CellSeverity.None && CellSheetLogic.SingleCellHopeless(m, canDo, cur);
                singleNote.Visibility = hopeless ? Visibility.Visible : Visibility.Collapsed;
                riskReason.Text = hopeless ? _vm.AllRiskReasonFor(i, j, m) ?? "" : "";
                riskReason.Visibility = riskReason.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                foreach (var (k, tb) in marksByShift)
                {
                    tb.Text = "";
                    if (m.HardRisk.Contains(k)) { tb.Text = "⚠"; tb.Foreground = new SolidColorBrush(ColorHex.Parse(MagiAccent.Red, Colors.Red)); }
                    else if (m.Recommended.Contains(k)) { tb.Text = "●"; tb.Foreground = new SolidColorBrush(ColorHex.Parse(MagiAccent.Green, Colors.Green)); }
                }
            }
            catch (OperationCanceledException) { }
        }
        flyout.ShowAt(anchor);
    }
}
