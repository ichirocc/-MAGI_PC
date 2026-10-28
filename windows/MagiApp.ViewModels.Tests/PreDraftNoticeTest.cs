using MagiApp.ViewModels.Tests.TestSupport;
using MagiApp.ViewModels.Work;
using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>Kotlin <c>PreDraftNoticeTest</c>（3.727.3）の 1 対 1 移植。置き換え前の勤務表との比較は必須→重み→合計の辞書式
/// （3.727.0 実機ログ: 元 43337 を下書きに置き換え、作成が 43563 で負けた）。</summary>
[Collection("OptimizationRepositoryState")]
public class PreDraftNoticeTest
{
    [Fact]
    public void LexicographicLikeReportComparator()
    {
        Assert.True(AnalysisTriage.PreDraftBetter(4, 43563.0, 378, 4, 43337.0, 411));    // 重みが軽い元の表のほうが良い（合計は多くても）
        Assert.False(AnalysisTriage.PreDraftBetter(4, 42965.0, 403, 4, 43337.0, 411));   // 結果が勝てば知らせない
        Assert.True(AnalysisTriage.PreDraftBetter(5, 100.0, 1, 4, 99999.0, 999));        // 必須が先
        Assert.False(AnalysisTriage.PreDraftBetter(4, 100.0, 10, 4, 100.0, 10));         // 同じなら良くない
        Assert.True(AnalysisTriage.PreDraftBetter(4, 100.0, 10, 4, 100.0, 9));
    }

    [Fact]
    public void NoticeText()
    {
        Assert.Equal("下書きに置き換える前の勤務表のほうが良い結果です（必須 4件・重み 43337）。", AnalysisTriage.PreDraftNoticeText("4件", 43337.0));
    }

    /// <summary>C# のみ（VM の筋）: 確認して下書きで置き換えた盤面を覚え、あとの作成の結果が負けたら知らせ、戻すと元の盤面へ（元に戻すで取り消せる）。</summary>
    [Fact]
    public async Task ReplacedBoardIsRememberedAndRestorable()
    {
        OptimizationRepository.SetRunning(false);
        OptimizationRepository.Clear();
        var original = MinimalState.BuildSchedule();
        original[0][0] = 1;
        var vm = new MagiViewModel { _state = MinimalState.Build(), _currentSchedule = original };
        vm.Ui.CheckRev = 1; vm.Ui.BestHard = 0; vm.Ui.WeightedScore = 5; vm.Ui.TotalViolations = 1;
        vm.Ui.DraftConfirm = "確認";
        vm.GenerateSmartInitial(confirmed: true);
        await vm.LastGenerateSmartInitialTask!;
        var worse = new ViolationReport(new Dictionary<string, string>(), new Dictionary<string, string>(), new Dictionary<string, string>(),
            new Dictionary<string, int> { ["covU"] = 1 }, 1, 1, 0, 10000.0);
        vm.NotePreDraftAfterRun(worse);
        Assert.Equal(AnalysisTriage.PreDraftNoticeText("0件", 5), vm.Ui.PreDraftNotice);
        vm.RestorePreDraft();
        Assert.Null(vm.Ui.PreDraftNotice);
        Assert.Equal(1, vm._currentSchedule![0][0]);
        Assert.True(vm.Ui.CanUndo);
    }
}
