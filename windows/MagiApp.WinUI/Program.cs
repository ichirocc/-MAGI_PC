using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace MagiApp.WinUI;

/// <summary>
/// [2026-09-04 実機報告「Windows11版が起動出来ない。画面でない」] 起動失敗を必ず見えるようにするための
/// 手書き Main（csproj の DISABLE_XAML_GENERATED_MAIN で生成 Main を止め、同じ手順を try/catch で包む）。
/// 旧: 生成 Main は例外を捕まえず、unpackaged 実行で必須ファイル（resources.pri・VC++ ランタイム）が無いと
/// ウィンドウを出す前にプロセスが無言で終了していた。ここでは <see cref="StartupDiagnostics"/> がログへ書き、
/// XAML に依存しない Win32 MessageBox で原因とログの場所を表示する。
/// </summary>
public static class Program
{
    [DllImport("Microsoft.ui.xaml.dll")]
    private static extern void XamlCheckProcessRequirements();

    [STAThread]
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            StartupDiagnostics.Report("AppDomain.UnhandledException", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            StartupDiagnostics.Log("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
        try
        {
            StartupDiagnostics.Log("起動", null);
            XamlCheckProcessRequirements();
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(_ =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                _ = new App();
            });
            return 0;
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Report("Main", ex);
            return 1;
        }
    }
}

/// <summary>起動時の診断。XAML が使えない状況でも動くよう、ファイル書込みと Win32 MessageBox だけに依存する。</summary>
public static class StartupDiagnostics
{
    public static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Magi", "startup_error.log");

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    public static void Log(string where, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append(" [").Append(where).Append("] ");
            sb.Append("exe=").Append(AppContext.BaseDirectory).Append(" os=").Append(Environment.OSVersion.VersionString)
              .Append(" x64=").Append(Environment.Is64BitProcess);
            if (ex is not null) sb.AppendLine().Append(ex);
            sb.AppendLine();
            File.AppendAllText(LogPath, sb.ToString());
        }
        catch { /* 診断自体の失敗で起動を止めない */ }
    }

    /// <summary>致命的な起動失敗: ログへ書き、原因の要約とログの場所を Win32 MessageBox で示す。</summary>
    public static void Report(string where, Exception? ex)
    {
        Log(where, ex);
        try
        {
            var root = ex; while (root?.InnerException is not null) root = root.InnerException;
            var hint = root switch
            {
                DllNotFoundException or BadImageFormatException => "必要なランタイム DLL が見つかりません（Visual C++ ランタイム／WindowsAppSDK）。インストーラを入れ直してください。",
                FileNotFoundException f when (f.FileName ?? "").EndsWith(".pri", StringComparison.OrdinalIgnoreCase) => "resources.pri が見つかりません。インストーラを入れ直してください。",
                _ => "",
            };
            var msg = "MAGI ShiftOptimizer を起動できませんでした。\n\n" +
                      (hint.Length > 0 ? hint + "\n\n" : "") +
                      (root?.GetType().Name ?? "Exception") + ": " + (root?.Message ?? "") + "\n\n" +
                      "詳細ログ: " + LogPath;
            MessageBoxW(IntPtr.Zero, msg, "MAGI ShiftOptimizer", 0x10 /* MB_ICONERROR */);
        }
        catch { }
    }
}
