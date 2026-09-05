using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
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

    /// <summary>[通知バー] 直近 <see cref="GlobalMessageBar"/> に表示した <c>Ui.Message</c> の値
    /// （<see cref="OnMessageBarClosed"/> が <see cref="MagiViewModel.ClearMessage"/> へ compare-and-clear
    /// 用に渡す——別の新しい通知が表示中に上書きしていたら消さないため）。</summary>
    private string? _shownMessage;

    /// <summary>[通知バー] 自動消滅タイマー（Kotlin原本の Snackbar の自動消滅に対応。<c>DispatcherTimer</c>
    /// を使う既存規約は <c>ScheduleView.FocusCell</c> と同じ）。</summary>
    private DispatcherTimer? _messageTimer;

    public MainWindow(MagiViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        Title = "MAGI ShiftOptimizer";
        Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First();
        AppWindow.Closing += OnAppWindowClosing;
        _vm.Ui.PropertyChanged += OnUiChangedForMessageBar;
        ShowTab("home");
        UpdateMessageBar();
        _ = InitializeAsync();
    }

    /// <summary>
    /// [通知バー] Kotlin原本の Snackbar 相当。<c>Ui.Message</c> が変わるたびに開閉する
    /// （タブは <c>ShowTab</c> がタブ内容だけを差し替えるので、この購読はウィンドウ生存期間中ずっと有効
    /// ——<see cref="MainWindow"/> 自体がアプリの生存期間と一致するため明示的な解除は行わない）。
    /// </summary>
    private void OnUiChangedForMessageBar(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(UiState.Message) or nameof(UiState.MessageIsError)) UpdateMessageBar();
    }

    /// <summary>
    /// [通知バー] <c>Ui.Message</c> を <see cref="GlobalMessageBar"/> へ反映する。null なら閉じるだけ
    /// （<see cref="MagiViewModel.ClearMessage"/> は呼ばない——既に空なので比較対象が無い）。
    /// 同一メッセージが既に開いている間の再描画（他プロパティの変更に連動した <c>Render</c> 等）では
    /// タイマーを再起動しない（表示時間が伸び続けるのを防ぐ）。
    /// </summary>
    private void UpdateMessageBar()
    {
        var msg = _vm.Ui.Message;
        if (msg is null)
        {
            _messageTimer?.Stop();
            GlobalMessageBar.IsOpen = false;
            return;
        }
        if (msg == _shownMessage && GlobalMessageBar.IsOpen) return;

        _shownMessage = msg;
        GlobalMessageBar.Message = msg;
        GlobalMessageBar.Severity = _vm.Ui.MessageIsError ? InfoBarSeverity.Error : InfoBarSeverity.Informational;
        GlobalMessageBar.IsOpen = true;

        _messageTimer?.Stop();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_vm.Ui.MessageIsError ? 6 : 4) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            GlobalMessageBar.IsOpen = false;
        };
        _messageTimer = timer;
        timer.Start();
    }

    /// <summary>
    /// [通知バー] 自動消滅／×ボタンいずれで閉じても呼ばれる。<see cref="MagiViewModel.ClearMessage"/>
    /// へ compare-and-clear させる——表示していた間に別の新しい通知が <c>Ui.Message</c> を上書きして
    /// いたら（<c>_shownMessage</c> と一致しない）、その新しい通知を誤って消さない。
    /// </summary>
    private void OnMessageBarClosed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        _messageTimer?.Stop();
        _vm.ClearMessage(_shownMessage);
    }

    /// <summary>
    /// 実行中（前景/背景いずれか）に閉じようとしたら確認する（クラスKDoc「プロセス生存戦略の決定」参照）。
    /// 確認後に自分自身が呼ぶ <see cref="Window.Close"/> で無限ループしないよう <see cref="_closeConfirmed"/> で防ぐ。
    ///
    /// [2026-09-02, 配線] 実行中でない通常の終了経路では <see cref="MagiViewModel.SaveNow"/>
    /// （デバウンス無しの即時同期保存。フェーズ9で移植・テスト済み——KDoc「autoSaveの1200msデバウンス中に
    /// プロセスが破棄されても編集が失われないための保険」参照）が一度も呼ばれておらず、直近の編集から
    /// 1200ms以内にウィンドウを閉じると自動保存に間に合わず編集が消えうる欠陥があった。閉じる直前に
    /// 必ず呼ぶ（確認ダイアログで終了する場合も同様）。
    /// </summary>
    private async void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closeConfirmed) return;
        if (!_vm.Ui.Running)
        {
            _vm.SaveNow();
            return;
        }
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
        // [レビュー指摘 2026-09-04] 旧: 確認ダイアログ経由の終了では SaveNow() を呼ばず、2回目の Closing は
        //   _closeConfirmed で即 return するため、1200ms のデバウンス待ちだった編集が消えていた
        //   （上の KDoc「確認ダイアログで終了する場合も同様」と実装が一致していなかった）。
        _vm.SaveNow();
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
    private void ShowTab(string tag) => HostContent.Content = GetOrCreateTab(tag);

    private UIElement GetOrCreateTab(string tag)
    {
        if (!_tabCache.TryGetValue(tag, out var content))
        {
            content = tag switch
            {
                "home" => new HomeView(_vm, this),
                "schedule" => new ScheduleView(_vm),
                "edit" => new EditView(_vm),
                "analysis" => new AnalysisView(_vm, JumpToCell),
                "settings" => new SettingsView(_vm, this),
                _ => PlaceholderTab(tag),
            };
            _tabCache[tag] = content;
        }
        return content;
    }

    /// <summary>[phase9 #2] タブ切替（<c>NavigationView</c> の選択表示も同期）。ホームの処方箋カードの導線が使う。</summary>
    internal void SelectTab(string tag)
    {
        ShowTab(tag);
        var item = Nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(it => (string?)it.Tag == tag);
        if (item is not null) Nav.SelectedItem = item;
    }

    /// <summary>[phase9 #2] 勤務表CSVの書き出し。ピッカーの配線は設定タブに1つだけ置き、ここは委譲する。</summary>
    /// <summary>[phase9 #5] 「データを開く」（ホームの空状態カード）。ピッカーは設定タブのものへ委譲。</summary>
    internal Task OpenDataAsync() =>
        GetOrCreateTab("settings") is SettingsView sv ? sv.OpenDataAsync() : Task.CompletedTask;

    internal Task ExportScheduleCsvAsync() =>
        GetOrCreateTab("settings") is SettingsView sv ? sv.ExportCsvAsync() : Task.CompletedTask;

    /// <summary>
    /// [違反箇所へのジャンプ] <c>AnalysisView</c>「違反の場所」から呼ばれる。勤務表タブへ切替え
    /// （<c>NavigationView</c> の選択表示も同期させる）てから、対象セルへスクロール＋一時ハイライト
    /// する（<see cref="ScheduleView.FocusCell"/>）。<see cref="ShowTab"/> が呼ばれた後は
    /// <c>_tabCache["schedule"]</c> に <see cref="ScheduleView"/> が必ず存在する（初回訪問でも
    /// このタイミングで構築される）。
    /// </summary>
    private void JumpToCell(int i, int j)
    {
        SelectTab("schedule");
        if (_tabCache["schedule"] is ScheduleView sv) sv.FocusCell(i, j);
    }

    /// <summary>
    /// [フェーズ9] 未知のタグに対する防御（5タグはすべて実装済みのため通常は到達しない）。
    /// switch 式の網羅性を静的に証明できない以上、黙って空を出すよりは理由を画面に出す。
    /// </summary>
    private static UIElement PlaceholderTab(string label) => new TextBlock
    {
        Text = $"{label}タブは準備中です。",
        Margin = new Thickness(16),
        Style = (Style)Application.Current.Resources["MagiBodySmallTextStyle"],
        Opacity = 0.7,
    };

    /// <summary>
    /// フェーズ8の同梱フィクスチャ（Assets/sample_state_v6.json）を読み込む。フェーズ9で
    /// 「データを開く」導線（ファイルピッカー）に置き換わるまでの暫定入口。
    /// </summary>
    internal async Task LoadFixtureAsync()
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
