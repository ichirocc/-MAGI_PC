namespace MagiApp.ViewModels.Tests.TestSupport;

/// <summary>[Android 3.672.0] 作成はいつも「勤務表を作成する」シートを通る（同じ指紋で一度押すまで）。シートが出たら「勤務表を作成」を押して始める＝利用者の操作と同じ順。</summary>
internal static class PreRunTestExtensions
{
    public static void RunV6FullOptimizeThroughSheet(this MagiViewModel vm)
    {
        vm.RunV6FullOptimize();
        if (vm.Ui.PreRunCheck is not null) vm.ProceedPreRun();
    }
}
