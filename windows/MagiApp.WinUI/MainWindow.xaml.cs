using MagiApp.ViewModels;
using MagiApp.WinUI.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MagiApp.WinUI;

/// <summary>
/// [フェーズ9] アプリのルートウィンドウ。5タブ（ホーム/勤務表/編集/分析/設定）のナビゲーション殻を
/// 持ち、起動時にフェーズ8由来の同梱フィクスチャを読み込む（フェーズ9で「データを開く」導線に
/// 置き換わるまでの暫定入口——クラスKDocはXAML側参照）。
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly MagiViewModel _vm;
    private readonly Dictionary<string, UIElement> _tabCache = new();

    public MainWindow(MagiViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        Title = "MAGI ShiftOptimizer";
        Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First();
        ShowTab("home");
        _ = LoadFixtureAsync();
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag }) ShowTab(tag);
    }

    /// <summary>各タブの内容は初回選択時に構築してキャッシュする（クラスKDoc参照）。</summary>
    private void ShowTab(string tag)
    {
        if (!_tabCache.TryGetValue(tag, out var content))
        {
            content = tag switch
            {
                "home" => new HomeView(_vm),
                "schedule" => new ScheduleView(_vm),
                "settings" => new SettingsView(_vm),
                "edit" => PlaceholderTab("編集"),
                "analysis" => PlaceholderTab("分析"),
                _ => PlaceholderTab(tag),
            };
            _tabCache[tag] = content;
        }
        HostContent.Content = content;
    }

    /// <summary>[フェーズ9] 編集/分析タブは未実装。実装済みタブとの区別が付くよう明示する。</summary>
    private static UIElement PlaceholderTab(string label) => new TextBlock
    {
        Text = $"{label}タブは準備中です。",
        Margin = new Thickness(16),
        FontSize = 14,
        Opacity = 0.7,
    };

    /// <summary>
    /// フェーズ8の同梱フィクスチャ（Assets/sample_state_v6.json）を読み込む。フェーズ9で
    /// 「データを開く」導線（ファイルピッカー）に置き換わるまでの暫定入口。
    /// </summary>
    private async Task LoadFixtureAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "sample_state_v6.json");
        string json;
        try
        {
            json = await File.ReadAllTextAsync(path);
        }
        catch (Exception e)
        {
            HostContent.Content = new TextBlock
            {
                Text = $"フィクスチャを読み込めませんでした: {e.GetType().Name}: {e.Message}（{path}）",
                Margin = new Thickness(16),
            };
            return;
        }
        _vm.LoadAsync(json);
    }
}
