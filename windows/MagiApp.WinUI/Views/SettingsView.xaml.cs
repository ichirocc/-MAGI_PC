using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using MagiApp.ViewModels;
using MagiEngine.V6;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Storage;
using Windows.Storage.Pickers;
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
        }
        finally
        {
            _syncingFromModel = false;
        }
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

    private async void OnOpenDataClick(object sender, RoutedEventArgs e)
    {
        var file = await PickOpenFileAsync(".json");
        if (file is null) return;
        var bytes = await FileIO.ReadBufferAsync(file);
        var text = CsvEncoding.DecodeCsvBytes(bytes.ToArray());
        _vm.LoadAsync(text);
    }

    private async void OnSaveDataClick(object sender, RoutedEventArgs e)
    {
        var json = _vm.ExportJson();
        if (json is null) return;
        var file = await PickSaveFileAsync("MAGI データ (JSON)", ".json", "magi_state");
        if (file is null) return;
        await FileIO.WriteTextAsync(file, json);
    }

    private async void OnImportCsvClick(object sender, RoutedEventArgs e)
    {
        var file = await PickOpenFileAsync(".csv");
        if (file is null) return;
        var bytes = await FileIO.ReadBufferAsync(file);
        var text = CsvEncoding.DecodeCsvBytes(bytes.ToArray());
        _vm.ImportCsvSmart(text);
    }

    private async void OnExportCsvClick(object sender, RoutedEventArgs e)
    {
        var csv = _vm.ExportCsv();
        if (csv is null) return;
        var file = await PickSaveFileAsync("勤務表CSV", ".csv", "magi_schedule");
        if (file is null) return;
        await FileIO.WriteTextAsync(file, csv);
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
