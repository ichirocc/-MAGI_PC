using System.ComponentModel;
using MagiApp.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace MagiApp.WinUI.Views;

/// <summary>[フェーズ9] 「ホーム」タブ。クラスKDoc（HomeView.xaml参照）。</summary>
public sealed partial class HomeView : UserControl
{
    private readonly MagiViewModel _vm;

    public HomeView(MagiViewModel vm)
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
        var ui = _vm.Ui;
        MessageText.Text = ui.Message ?? (ui.Loaded ? "" : "データを読み込んでいます…");
        SummaryText.Text = ui.Loaded
            ? $"満足度 {ui.Satisfaction} ・ 必須違反 {ui.BestHard} ・ 合計 {ui.BestSoft}"
            : "";
        MakeButton.IsEnabled = ui.Loaded && !ui.Running;
        StopButton.IsEnabled = ui.Running;
    }

    private void OnMakeClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => _vm.RunV6FullOptimize();

    private void OnStopClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => _vm.Stop();
}
