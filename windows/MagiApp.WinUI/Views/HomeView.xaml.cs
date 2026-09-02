using System.ComponentModel;
using MagiApp.ViewModels;
using Microsoft.UI.Xaml;
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
        var editable = ui.Loaded && !ui.Running;
        SmartInitialButton.IsEnabled = editable;
        MakeButton.IsEnabled = editable;
        SoftPolishButton.IsEnabled = editable;
        BackgroundButton.IsEnabled = editable;
        StopButton.IsEnabled = ui.Running;

        RenderAlternatives(ui, editable);
    }

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
        for (var i = 0; i < ui.Alternatives.Count; i++)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new TextBlock
            {
                Text = ui.Alternatives[i], FontSize = 13, VerticalAlignment = VerticalAlignment.Center,
            });
            var apply = new Button { Content = "適用", FontSize = 12, IsEnabled = editable };
            var idx = i;
            apply.Click += (_, _) => _vm.ApplyAlternative(idx);
            row.Children.Add(apply);
            AlternativesListHost.Children.Add(row);
        }
    }
}
