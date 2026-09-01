using System.ComponentModel;
using System.Linq;
using MagiApp.ViewModels;
using MagiEngine.V6;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace MagiApp.WinUI.Views;

/// <summary>
/// [フェーズ9] 「設定」タブ。既存の <see cref="MagiViewModel"/> セッター（<c>SetWorkers</c>/
/// <c>SetBudget</c>/<c>SetV6Algorithm</c>/<c>SetSoftPolish</c>/<c>SetBlockSwapC3nFilter</c>/
/// <c>SetWideC3nBreak</c>、いずれもフェーズ9ピース5で移植済み・テスト済み）への薄い配線。
///
/// [双方向反映のガード] コントロールの初期値を <see cref="Render"/> でプログラム的に設定すると、
/// その代入自体が各コントロールの <c>ValueChanged</c>/<c>Toggled</c>/<c>SelectionChanged</c> を
/// 発火させ、セッターへ無意味な再代入ループが起きうる。<see cref="_syncingFromModel"/> を立てている
/// 間はイベントハンドラを no-op にすることで防ぐ（WinUI3の定石パターン）。
/// </summary>
public sealed partial class SettingsView : UserControl
{
    private readonly MagiViewModel _vm;
    private bool _syncingFromModel;

    public SettingsView(MagiViewModel vm)
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
        }
        finally
        {
            _syncingFromModel = false;
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
