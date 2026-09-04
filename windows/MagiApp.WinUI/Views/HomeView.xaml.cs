using System.ComponentModel;
using MagiApp.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MagiApp.WinUI.Views;

/// <summary>[フェーズ9] 「ホーム」タブ。クラスKDoc（HomeView.xaml参照）。</summary>
public sealed partial class HomeView : UserControl
{
    private readonly MagiViewModel _vm;
    private readonly UiSubscription _uiSub;

    public HomeView(MagiViewModel vm)
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
        // [トークン非適用, ファイル共通] FontSize=13(TextBlock)/12(Button)はMagiThemeタイポ
        // スケール(14始まり)より小さい一覧行用の調整値で厳密一致せず、Button.FontSizeはStyle
        // (TargetType=TextBlock)を型的に受け付けられないため、いずれもトークン化の対象外。
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
