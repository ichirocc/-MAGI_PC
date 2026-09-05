using MagiApp.ViewModels;
using MagiApp.ViewModels.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace MagiApp.WinUI;

/// <summary>
/// [フェーズ8, 縦断スライス] フェーズ0の Hello World から一歩進め、DI コンテナで
/// <see cref="MagiViewModel"/> を組み立てて <see cref="MainWindow"/> へ渡す。
///
/// [DI コンテナの選定] Kotlin原本はAndroid ViewModel（フレームワーク自身がインスタンス管理）を使うため
/// 対応物が無い。この移植は <c>Microsoft.Extensions.DependencyInjection</c> を最小限（コンストラクタ
/// 注入のみ・ライフタイムはアプリ全体で単一インスタンス）で導入する——フェーズ9のUseCases/Services層
/// 拡張時にここへ登録を足していく想定。<see cref="MagiViewModel"/> は<c>単一の可変 <see cref="MagiViewModel.Ui"/>
/// を保持し続ける設計（クラスKDoc参照）のため Singleton が自然に対応する。
/// </summary>
public partial class App : Application
{
    private Window? _window;

    /// <summary>[テスト/将来のUseCases層からの参照用] アプリ全体で共有する DI コンテナ。</summary>
    public IServiceProvider Services { get; }

    public App()
    {
        InitializeComponent();
        // XAML ループ内の未処理例外もログへ残す（無言で落とさない）。
        UnhandledException += (_, e) =>
        {
            StartupDiagnostics.Log("Application.UnhandledException", e.Exception);
        };

        var services = new ServiceCollection();
        services.AddSingleton<IOptimizationService, EngineOptimizationService>();
        services.AddSingleton<MagiViewModel>();
        Services = services.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new MainWindow(Services.GetRequiredService<MagiViewModel>());
            _window.Activate();
            StartupDiagnostics.Log("ウィンドウ表示", null);
        }
        catch (Exception ex)
        {
            // メインウィンドウの生成失敗＝画面が一度も出ない。原因を見せてから終了する。
            StartupDiagnostics.Report("OnLaunched", ex);
            Exit();
        }
    }
}
