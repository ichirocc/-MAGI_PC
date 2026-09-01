using MagiApp.ViewModels;
using MagiApp.WinUI.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MagiApp.WinUI;

/// <summary>
/// [フェーズ9→Phase 10] アプリのルートウィンドウ。5タブ（ホーム/勤務表/編集/分析/設定）の
/// ナビゲーション殻を持つ。
///
/// [起動時の初期化] <see cref="MagiViewModel.RestoreOnStartup"/>（前回の自動保存からの復元。
/// 2026-09-01 以前はプロセスkill時の中断マーカー・バックグラウンド完了結果からの復元も担っていたが、
/// ユーザー明示判断「クラッシュからの復旧はそこまで重視しない」により撤去した——詳細は
/// <c>windows/README.md</c> フェーズ10節参照）をまず待ち、それでも何も復元されなければ
/// （<c>Ui.Running</c>/<c>Ui.Loaded</c> がどちらも false のまま＝真にデータの無い初回起動）フェーズ8由来の
/// 同梱フィクスチャへフォールバックする（フェーズ9で「データを開く」導線に置き換わるまでの暫定入口
/// ——<see cref="LoadFixtureAsync"/> 参照）。両者を無条件に両方走らせると、復元が読み込む前に
/// フィクスチャが先に <c>state</c> を埋めてしまい実データを握り潰しうる（<c>RestoreOnStartup</c> 自身の
/// 復元判定は「<c>state</c> がまだ null か」を見るため）——このガードがその競合を防ぐ。
///
/// [プロセス生存戦略の決定（Phase 10 の残課題への回答）] Kotlin原本は WorkManager の前景サービスにより
/// アプリがバックグラウンドへ回っても最適化を続行できるが、Windows デスクトップに直接対応する機構
/// （タスクトレイ常駐等）は無い。フルのトレイアイコン実装（Win32相互運用または追加パッケージ・
/// このサンドボックスでは実機検証不可）を新規に持ち込むリスクを避け、**ウィンドウを閉じたら
/// プロセスも終了する**（標準的なデスクトップアプリの既定動作のまま）と明示的に決定する。
/// その代わり、閉じようとした時点でバックグラウンド計算が動いている場合は**無言で捨てない**——
/// <see cref="OnAppWindowClosing"/> が確認ダイアログを挟み、実行中と知らずに計算を失う事故を防ぐ
/// （「生かし続ける」の代わりに「知らずに失わせない」で決着）。
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly MagiViewModel _vm;
    private readonly Dictionary<string, UIElement> _tabCache = new();
    private bool _closeConfirmed;

    public MainWindow(MagiViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        Title = "MAGI ShiftOptimizer";
        Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First();
        AppWindow.Closing += OnAppWindowClosing;
        ShowTab("home");
        _ = InitializeAsync();
    }

    /// <summary>
    /// 実行中（前景/背景いずれか）に閉じようとしたら確認する（クラスKDoc「プロセス生存戦略の決定」参照）。
    /// 確認後に自分自身が呼ぶ <see cref="Window.Close"/> で無限ループしないよう <see cref="_closeConfirmed"/> で防ぐ。
    /// </summary>
    private async void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closeConfirmed || !_vm.Ui.Running) return;
        args.Cancel = true;
        var dialog = new ContentDialog
        {
            XamlRoot = Nav.XamlRoot,
            Title = "計算が実行中です",
            Content = "閉じると、実行中の計算（バックグラウンドを含む）が中断されます。終了しますか？",
            PrimaryButtonText = "終了する",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        _closeConfirmed = true;
        _vm.Stop();
        Close();
    }

    private async Task InitializeAsync()
    {
        await _vm.RestoreOnStartup();
        // Ui.Running=true は「復元すべきものが見つかり LoadAsync が進行中」（LoadAsync 自身が
        // 読込開始時に同期的に立てる）、Ui.Loaded=true は「既に読み込み完了済み」。どちらも false なら
        // 復元対象が本当に無かった（自動保存も途中結果も無い、真の初回起動）と判断してよい。
        if (_vm.Ui.Running || _vm.Ui.Loaded) return;
        await LoadFixtureAsync();
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
                "edit" => new EditView(_vm),
                "analysis" => new AnalysisView(_vm),
                "settings" => new SettingsView(_vm, this),
                _ => PlaceholderTab(tag),
            };
            _tabCache[tag] = content;
        }
        HostContent.Content = content;
    }

    /// <summary>
    /// [フェーズ9] 未知のタグに対する防御（5タグはすべて実装済みのため通常は到達しない）。
    /// switch 式の網羅性を静的に証明できない以上、黙って空を出すよりは理由を画面に出す。
    /// </summary>
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
