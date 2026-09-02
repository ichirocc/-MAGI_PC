using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using MagiApp.ViewModels;
using MagiEngine.V6;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;

namespace MagiApp.WinUI.Views;

/// <summary>
/// [フェーズ9→データ入出力] 「設定」タブ。既存の <see cref="MagiViewModel"/> セッター（<c>SetWorkers</c>/
/// <c>SetBudget</c>/<c>SetV6Algorithm</c>/<c>SetSoftPolish</c>/<c>SetBlockSwapC3nFilter</c>/
/// <c>SetWideC3nBreak</c>、いずれもフェーズ9ピース5で移植済み・テスト済み）への薄い配線。
///
/// [双方向反映のガード] コントロールの初期値を <see cref="Render"/> でプログラム的に設定すると、
/// その代入自体が各コントロールの <c>ValueChanged</c>/<c>Toggled</c>/<c>SelectionChanged</c> を
/// 発火させ、セッターへ無意味な再代入ループが起きうる。<see cref="_syncingFromModel"/> を立てている
/// 間はイベントハンドラを no-op にすることで防ぐ（WinUI3の定石パターン）。
///
/// [データ入出力] 「データを開く/保存」（JSON、<see cref="MagiViewModel.LoadAsync"/>/
/// <see cref="MagiViewModel.ExportJson"/>）と「勤務表CSVの取込/書出」（<see cref="MagiViewModel.ImportCsvSmart"/>/
/// <see cref="MagiViewModel.ExportCsv"/>）を <see cref="FileOpenPicker"/>/<see cref="FileSavePicker"/> へ配線する
/// ——Kotlin原本ではこれらは Compose 側のファイル選択コード（<c>ui/MagiApp.kt</c>、Android の
/// <c>ActivityResultContracts</c>）が担っており <c>MagiViewModel.kt</c> の一部ではなかった。この移植でも
/// 同じ役割分担を保ち、UI層（このファイル）に置く。CSV取込のバイト列デコード（Shift-JIS/CP932 自動判定）
/// は <see cref="CsvEncoding.DecodeCsvBytes"/>（Kotlin原本 <c>decodeCsvBytes</c> の移植）。
///
/// [アンパッケージ実行でのピッカー] <see cref="FileOpenPicker"/>/<see cref="FileSavePicker"/> は
/// デスクトップアプリでは所有ウィンドウのハンドルが要る（<see cref="InitializeWithWindow"/>）ため、
/// <see cref="MainWindow"/> 自身を <paramref name="window"/> として受け取る。
/// </summary>
public sealed partial class SettingsView : UserControl
{
    private readonly MagiViewModel _vm;
    private readonly Window _window;
    private bool _syncingFromModel;

    public SettingsView(MagiViewModel vm, Window window)
    {
        _vm = vm;
        _window = window;
        InitializeComponent();
        _vm.Ui.PropertyChanged += OnUiChanged;
        Unloaded += (_, _) => _vm.Ui.PropertyChanged -= OnUiChanged;
        Render();
    }

    private void OnUiChanged(object? sender, PropertyChangedEventArgs e) => Render();

    private void Render()
    {
        _syncingFromModel = true;
        try
        {
            var ui = _vm.Ui;
            WorkersSlider.Value = ui.Workers;
            WorkersLabel.Text = $"並列数 {ui.Workers}";
            BudgetSlider.Value = ui.BudgetSec;
            BudgetLabel.Text = FormatBudget(ui.BudgetSec);
            SoftPolishToggle.IsOn = ui.SoftPolish;
            NativeAccelToggle.IsOn = ui.NativeAccel;
            BlockSwapC3nFilterToggle.IsOn = ui.BlockSwapC3nFilter;
            WideC3nBreakToggle.IsOn = ui.WideC3nBreak;

            var tag = ui.V6Algorithm.ToString();
            var match = AlgorithmCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == tag);
            if (match is not null) AlgorithmCombo.SelectedItem = match;

            DataStatusText.Text = ui.Message ?? "";
            OpenDataButton.IsEnabled = !ui.Running;
            ImportCsvButton.IsEnabled = !ui.Running;
            SaveDataButton.IsEnabled = ui.Loaded && !ui.Running;
            ExportCsvButton.IsEnabled = ui.Loaded && !ui.Running;

            RenderShiftColors(ui);
            RenderViolationColors(ui);
        }
        finally
        {
            _syncingFromModel = false;
        }
    }

    // ===== 表示色（クラスKDoc参照。RenderのXAML静的化が難しい可変長リスト2種） =====

    /// <summary>シフト記号の表示色。行=シフト1件、既定パレット色との差＝<c>Custom</c> が「既定に戻す」の有効/無効。</summary>
    private void RenderShiftColors(UiState ui)
    {
        ShiftColorList.Children.Clear();
        if (!ui.Loaded) return;
        foreach (var sc in _vm.ShiftColorList())
        {
            var kigou = sc.Kigou;
            ShiftColorList.Children.Add(BuildColorRow(
                $"{sc.Name}（{sc.Kigou}）", sc.Hex, sc.Custom,
                hex => _vm.SetShiftColor(kigou, hex),
                () => _vm.ResetShiftColor(kigou)));
        }
    }

    /// <summary>違反の基準色（必須/要調整の2行）＋族別の個別色（19族、未設定は基準色へフォールバック）。</summary>
    private void RenderViolationColors(UiState ui)
    {
        ViolationBaseColorList.Children.Clear();
        ViolationFamilyColorList.Children.Clear();
        if (!ui.Loaded) return;

        var hardResolved = string.IsNullOrWhiteSpace(ui.ViolationColorHex) ? ColorHex.DefaultHardVioHex : ui.ViolationColorHex;
        var softResolved = string.IsNullOrWhiteSpace(ui.ViolationSoftColorHex) ? ColorHex.DefaultSoftVioHex : ui.ViolationSoftColorHex;

        ViolationBaseColorList.Children.Add(BuildColorRow(
            "必須違反の枠色", hardResolved, !string.IsNullOrWhiteSpace(ui.ViolationColorHex),
            hex => _vm.SetViolationColor(hex), () => _vm.ResetViolationColor()));
        ViolationBaseColorList.Children.Add(BuildColorRow(
            "要調整（ソフト違反）の枠色", softResolved, !string.IsNullOrWhiteSpace(ui.ViolationSoftColorHex),
            hex => _vm.SetViolationSoftColor(hex), () => _vm.ResetViolationSoftColor()));

        // [MirrorKeys.All=19族。ScheduleView.ResolveVioBrush と同じ優先順位（族別→基準色→既定色）で
        //  解決した色をそのままスウォッチに出す＝設定画面とグリッドの見え方が食い違わない。]
        foreach (var family in MirrorKeys.All)
        {
            var hard = MirrorKeys.Hard.Contains(family);
            var baseResolved = hard ? hardResolved : softResolved;
            ui.ViolationFamilyColorHex.TryGetValue(family, out var famHex);
            var custom = !string.IsNullOrWhiteSpace(famHex);
            var resolved = custom ? famHex! : baseResolved;
            var label = AnalysisView.BreakdownLabels.TryGetValue(family, out var jp) ? jp : family;
            ViolationFamilyColorList.Children.Add(BuildColorRow(
                label, resolved, custom,
                hex => _vm.SetViolationFamilyColor(family, hex),
                () => _vm.ResetViolationFamilyColor(family)));
        }
    }

    /// <summary>色設定1行＝スウォッチ＋ラベル＋「変更」（フライアウトの簡易カラーピッカー）＋「既定に戻す」。</summary>
    private FrameworkElement BuildColorRow(string label, string resolvedHex, bool custom, Action<string> onSet, Action onReset)
    {
        var swatch = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(ColorHex.Parse(resolvedHex, Colors.Gray)),
            BorderBrush = new SolidColorBrush(Colors.Gray),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var labelBlock = new TextBlock
        {
            Text = label,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 180,
            TextWrapping = TextWrapping.Wrap,
        };
        var changeButton = new Button { Content = "変更", Padding = new Thickness(8, 2, 8, 2) };
        changeButton.Flyout = BuildColorPickerFlyout(resolvedHex, onSet);
        var resetButton = new Button { Content = "既定に戻す", Padding = new Thickness(8, 2, 8, 2), IsEnabled = custom };
        resetButton.Click += (_, _) => onReset();

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(swatch);
        row.Children.Add(labelBlock);
        row.Children.Add(changeButton);
        row.Children.Add(resetButton);
        return row;
    }

    /// <summary>
    /// 「変更」ボタンのフライアウト＝<see cref="MagiAccent.All"/>（既存7色パレット、色設定の唯一の
    /// 一次ソース）のスウォッチをタップで即適用、または16進テキストで任意色を指定して「適用」。
    /// Kotlin原本 <c>ColorPickerDialog</c>（プリセットパレット＋現在色表示）の簡易移植——
    /// WinUI3 の <see cref="Microsoft.UI.Xaml.Controls.ColorPicker"/>（HSVホイール等の高機能版）は
    /// このC#移植の最初の段階では過剰と判断し、既存の7色パレット＋16進入力に留める。
    /// </summary>
    private Flyout BuildColorPickerFlyout(string currentHex, Action<string> onSet)
    {
        var flyout = new Flyout();
        var panel = new StackPanel { Spacing = 8, Padding = new Thickness(4) };

        var swatchGrid = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (var hex in MagiAccent.All)
        {
            var swatchButton = new Button
            {
                Width = 28,
                Height = 28,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(ColorHex.Parse(hex, Colors.Gray)),
            };
            swatchButton.Click += (_, _) => { onSet(hex); flyout.Hide(); };
            swatchGrid.Children.Add(swatchButton);
        }
        panel.Children.Add(swatchGrid);

        var hexBox = new TextBox { Text = currentHex, PlaceholderText = "#rrggbb" };
        var applyButton = new Button { Content = "適用", HorizontalAlignment = HorizontalAlignment.Stretch };
        applyButton.Click += (_, _) =>
        {
            var text = hexBox.Text?.Trim() ?? "";
            if (text.Length == 0) return;
            onSet(text.StartsWith("#", StringComparison.Ordinal) ? text : $"#{text}");
            flyout.Hide();
        };
        var hexRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        hexRow.Children.Add(hexBox);
        hexRow.Children.Add(applyButton);
        panel.Children.Add(hexRow);

        flyout.Content = panel;
        return flyout;
    }

    // ===== データ入出力（クラスKDoc参照） =====

    private async Task<StorageFile?> PickOpenFileAsync(string extension)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(extension);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(_window));
        return await picker.PickSingleFileAsync();
    }

    private async Task<StorageFile?> PickSaveFileAsync(string friendlyName, string extension, string suggestedName)
    {
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = suggestedName };
        picker.FileTypeChoices.Add(friendlyName, new List<string> { extension });
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(_window));
        return await picker.PickSaveFileAsync();
    }

    /// <summary>
    /// [2026-09-02, エラーハンドリングの欠落を解消] 旧実装は<c>FileOpenPicker</c>/<c>FileIO</c>の失敗
    /// （権限拒否・ディスク容量不足・ファイルロック等）を素通りさせていた。<c>async void</c>イベント
    /// ハンドラでの未処理例外はWinUI3ではアプリごとクラッシュしうるため、<see cref="MagiViewModel.NotifyOpenFailure"/>
    /// （用意済みだが呼び出し口が無かった）で受け止める。読込成功時は <see cref="MagiViewModel.LoadAsync"/>
    /// 自身が完了メッセージを出すため、ここで重ねて通知しない（クラスKDoc「成功時は呼ばない」参照）。
    /// </summary>
    private async void OnOpenDataClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var file = await PickOpenFileAsync(".json");
            if (file is null) return;
            var bytes = await FileIO.ReadBufferAsync(file);
            var text = CsvEncoding.DecodeCsvBytes(bytes.ToArray());
            _vm.LoadAsync(text);
        }
        catch (Exception ex)
        {
            _vm.NotifyOpenFailure(MagiViewModel.IoOutcome.Fail(ex), "データ");
        }
    }

    /// <summary>[2026-09-02] 保存は成功時も必ず通知する（<see cref="MagiViewModel.NotifySave"/> クラスKDoc参照
    /// ——ExportJson自体はI/Oを行わないため、成功の事実を伝えるのはここの責務）。</summary>
    private async void OnSaveDataClick(object sender, RoutedEventArgs e)
    {
        var json = _vm.ExportJson();
        if (json is null) return;
        try
        {
            var file = await PickSaveFileAsync("MAGI データ (JSON)", ".json", "magi_state");
            if (file is null) return;
            await FileIO.WriteTextAsync(file, json);
            _vm.NotifySave(MagiViewModel.IoOutcome.Ok(), "データ");
        }
        catch (Exception ex)
        {
            _vm.NotifySave(MagiViewModel.IoOutcome.Fail(ex), "データ");
        }
    }

    /// <summary>[2026-09-02] OnOpenDataClick と同じ理由。<see cref="MagiViewModel.ImportCsvSmart"/> 自身が
    /// 取込結果（成功/失敗いずれも）を通知するため、ここではファイルI/O自体の失敗だけを受け持つ。</summary>
    private async void OnImportCsvClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var file = await PickOpenFileAsync(".csv");
            if (file is null) return;
            var bytes = await FileIO.ReadBufferAsync(file);
            var text = CsvEncoding.DecodeCsvBytes(bytes.ToArray());
            _vm.ImportCsvSmart(text);
        }
        catch (Exception ex)
        {
            _vm.NotifyOpenFailure(MagiViewModel.IoOutcome.Fail(ex), "勤務表CSV");
        }
    }

    /// <summary>[2026-09-02] OnSaveDataClick と同じ理由。</summary>
    private async void OnExportCsvClick(object sender, RoutedEventArgs e)
    {
        var csv = _vm.ExportCsv();
        if (csv is null) return;
        try
        {
            var file = await PickSaveFileAsync("勤務表CSV", ".csv", "magi_schedule");
            if (file is null) return;
            await FileIO.WriteTextAsync(file, csv);
            _vm.NotifySave(MagiViewModel.IoOutcome.Ok(), "勤務表CSV");
        }
        catch (Exception ex)
        {
            _vm.NotifySave(MagiViewModel.IoOutcome.Fail(ex), "勤務表CSV");
        }
    }

    private static string FormatBudget(int sec)
    {
        var min = sec / 60;
        var remainder = sec % 60;
        return remainder == 0 ? $"計算の制限時間 {sec}秒（{min}分）" : $"計算の制限時間 {sec}秒";
    }

    private void OnWorkersChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncingFromModel) return;
        _vm.SetWorkers((int)e.NewValue);
    }

    private void OnBudgetChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncingFromModel) return;
        _vm.SetBudget((int)e.NewValue);
    }

    private void OnSoftPolishToggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_syncingFromModel) return;
        _vm.SetSoftPolish(SoftPolishToggle.IsOn);
    }

    private void OnBlockSwapC3nFilterToggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_syncingFromModel) return;
        _vm.SetBlockSwapC3nFilter(BlockSwapC3nFilterToggle.IsOn);
    }

    private void OnWideC3nBreakToggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_syncingFromModel) return;
        _vm.SetWideC3nBreak(WideC3nBreakToggle.IsOn);
    }

    private void OnAlgorithmChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingFromModel) return;
        var tag = (AlgorithmCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        if (tag is null || !Enum.TryParse<V6Algorithm>(tag, out var algo)) return;
        _vm.SetV6Algorithm(algo);
    }
}
