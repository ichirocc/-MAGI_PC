using System.ComponentModel;
using System.Linq;
using MagiApp.ViewModels;
using MagiEngine.V6;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

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
/// </summary>
public sealed partial class ScheduleView : UserControl
{
    private readonly MagiViewModel _vm;

    /// <summary>[違反箇所へのジャンプ] 分析タブから飛んできた注目セル。<see cref="FocusCell"/> 参照。</summary>
    private (int I, int J)? _focusCell;

    /// <summary>直近の <see cref="RenderSchedule"/> が作った注目セルの要素（無ければ null）。
    /// <see cref="FocusCell"/> が <c>StartBringIntoView</c> を呼ぶために使う。</summary>
    private Border? _focusCellElement;

    private DispatcherTimer? _focusTimer;

    public ScheduleView(MagiViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        _vm.Ui.PropertyChanged += OnUiChanged;
        Unloaded += (_, _) => _vm.Ui.PropertyChanged -= OnUiChanged;
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
        _focusCellElement?.StartBringIntoView();

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

    private void OnUndoClick(object sender, RoutedEventArgs e) => _vm.Undo();
    private void OnRedoClick(object sender, RoutedEventArgs e) => _vm.Redo();

    private void Render()
    {
        var ui = _vm.Ui;
        StatusText.Text = ui.Loaded
            ? $"{ui.Message}（必須={ui.BestHard} 合計={ui.BestSoft}・{ui.Staff}名×{ui.Days}日×{ui.Shifts}シフト）"
            : ui.Message ?? "読込中…";
        UndoButton.IsEnabled = ui.CanUndo && !ui.Running;
        RedoButton.IsEnabled = ui.CanRedo && !ui.Running;
        RenderSchedule(ui);
        RenderStaffTally(ui);
        RenderDayTally(ui);
    }

    private void RenderSchedule(UiState ui)
    {
        ScheduleGridHost.Children.Clear();
        ScheduleGridHost.RowDefinitions.Clear();
        ScheduleGridHost.ColumnDefinitions.Clear();
        _focusCellElement = null;
        if (!ui.Loaded || ui.Schedule.Count == 0) return;

        var staffCount = ui.Schedule.Count;
        var dayCount = ui.Schedule.Count > 0 ? ui.Schedule[0].Count : 0;
        // +1 列/行 = 職員名ヘッダー列・日番号ヘッダー行。
        for (var r = 0; r <= staffCount; r++) ScheduleGridHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var c = 0; c <= dayCount; c++) ScheduleGridHost.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        void AddCell(int row, int col, string text, bool header)
        {
            var block = new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = header ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                Padding = new Thickness(6, 4, 6, 4),
                MinWidth = header && col == 0 ? 96 : 32,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var border = new Border
            {
                Child = block,
                BorderBrush = new SolidColorBrush(Colors.LightGray),
                BorderThickness = new Thickness(0, 0, 1, 1),
            };
            Grid.SetRow(border, row);
            Grid.SetColumn(border, col);
            ScheduleGridHost.Children.Add(border);
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
                Padding = new Thickness(6, 4, 6, 4),
                MinWidth = 32,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Background = new SolidColorBrush(bg),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                IsEnabled = !ui.Running,
            };
            button.Click += (sender, _) => ShowCellEditor((FrameworkElement)sender, i, j);

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
                    Margin = new Thickness(0, 0, 2, 2),
                });
            }

            Brush borderBrush = new SolidColorBrush(Colors.LightGray);
            var thickness = new Thickness(0, 0, 1, 1);
            if (ui.ViolationCells.TryGetValue($"{i},{j}", out var vioClass))
            {
                borderBrush = ResolveVioBrush(ui, vioClass);
                thickness = new Thickness(2);
            }
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

        AddCell(0, 0, "", header: true);
        for (var j = 0; j < dayCount; j++) AddCell(0, j + 1, $"{j + 1}", header: true);

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
                var brush = VioBorderBrush(ui, vioClass, out var thickness);
                AddTallyCell(DayTallyGridHost, k + 1, j + 1, count.ToString(), header: false, borderBrush: brush, thickness: thickness);
            }
        }
    }

    /// <summary>集計グリッド共通のセル描画（<see cref="RenderStaffTally"/>/<see cref="RenderDayTally"/> 共用）。</summary>
    private static void AddTallyCell(Grid host, int row, int col, string text, bool header, Brush? borderBrush = null, Thickness? thickness = null)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = header ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            Padding = new Thickness(6, 4, 6, 4),
            MinWidth = header && col == 0 ? 96 : 32,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var border = new Border
        {
            Child = block,
            BorderBrush = borderBrush ?? new SolidColorBrush(Colors.LightGray),
            BorderThickness = thickness ?? new Thickness(0, 0, 1, 1),
        };
        Grid.SetRow(border, row);
        Grid.SetColumn(border, col);
        host.Children.Add(border);
    }

    /// <summary>違反クラス文字列("vio-xxx"等)から集計セルの枠色/太さを決める。null=違反なし=既定枠。</summary>
    private static Brush? VioBorderBrush(UiState ui, string? vioClass, out Thickness? thickness)
    {
        if (vioClass is null) { thickness = null; return null; }
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
        if (_vm.Ui.Running) return; // ボタン無効化と二重の防御（Render 直後の取りこぼし対策）。
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
        flyout.ShowAt(anchor);
    }
}
