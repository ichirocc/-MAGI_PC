using Microsoft.UI.Xaml;

namespace MagiApp.WinUI;

/// <summary>
/// フェーズ0の Hello World アプリケーションエントリポイント。
/// フェーズ9で DI コンテナ・ViewModel 配線・背景実行の起動などをここへ足す
/// （現状は MainWindow を1つ開くだけ）。
/// </summary>
public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
