using MagiApp.ViewModels.Work;
using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>
/// [フェーズ9 ピース5] <c>OptimizationRepository.kt</c> の移植（<see cref="OptimizationRepository"/>）の
/// 検証。Kotlin原本自体はAndroid/WorkManagerに依存しない純粋な <c>object</c> だが、専用テストは
/// 存在しない（呼び出し元の <c>MagiViewModel.kt</c>/<c>OptimizationWorker.kt</c> がAndroid依存で
/// ホストJVMでは検証できないため、間接的にしか運動していなかった）。この移植で初めて直接テストする。
///
/// プロセス全体で共有される static 状態を扱うため、<see cref="MagiViewModelTest"/> と同じ直列
/// コレクションに属する（<see cref="TestSupport.OptimizationRepositoryStateCollection"/> 参照）。
/// </summary>
[Collection("OptimizationRepositoryState")]
public class OptimizationRepositoryTest
{
    public OptimizationRepositoryTest()
    {
        OptimizationRepository.SetRunning(false);
        OptimizationRepository.Clear();
    }

    /// <summary>
    /// <see cref="ViolationReport"/> の8必須位置引数を空値で満たした、内容を問わない最小インスタンス。
    /// このテストは「値が正しく publish/読取されるか」だけを見ており、report の中身自体は無関係。
    /// </summary>
    private static ViolationReport EmptyReport() => new(
        Violations: new Dictionary<string, string>(),
        NeedViolations: new Dictionary<string, string>(),
        CountViolations: new Dictionary<string, string>(),
        Breakdown: new Dictionary<string, int>(),
        Total: 0,
        Hard: 0,
        Soft: 0,
        WeightedScore: 0.0);

    [Fact]
    public void SetRunningUpdatesTheValueAndRaisesTheEvent()
    {
        var raised = new List<bool>();
        Action<bool> handler = v => raised.Add(v);
        OptimizationRepository.RunningChanged += handler;
        try
        {
            OptimizationRepository.SetRunning(true);
            Assert.True(OptimizationRepository.Running);
            Assert.Equal(new[] { true }, raised);

            OptimizationRepository.SetRunning(false);
            Assert.False(OptimizationRepository.Running);
            Assert.Equal(new[] { true, false }, raised);
        }
        finally
        {
            OptimizationRepository.RunningChanged -= handler;
        }
    }

    [Fact]
    public void PublishProgressUpdatesTheValueAndRaisesTheEvent()
    {
        var raised = new List<OptimizationRepository.BgProgress>();
        Action<OptimizationRepository.BgProgress> handler = p => raised.Add(p);
        OptimizationRepository.ProgressPublished += handler;
        try
        {
            var progress = new OptimizationRepository.BgProgress("RSI", 3, 12, 15, 1_000_000L, 5_000L);
            OptimizationRepository.PublishProgress(progress);

            Assert.Equal(progress, OptimizationRepository.Progress);
            Assert.Single(raised);
            Assert.Equal(progress, raised[0]);
        }
        finally
        {
            OptimizationRepository.ProgressPublished -= handler;
        }
    }

    [Fact]
    public void PublishResultUpdatesTheValueAndRaisesTheEventIncludingNull()
    {
        var raised = new List<OptimizationRepository.BgResult?>();
        Action<OptimizationRepository.BgResult?> handler = r => raised.Add(r);
        OptimizationRepository.ResultPublished += handler;
        try
        {
            var report = EmptyReport();
            var result = new OptimizationRepository.BgResult(
                new[] { new[] { 0, 1 } }, report, "完了", RunId: 42L);
            OptimizationRepository.PublishResult(result);

            Assert.Equal(result, OptimizationRepository.Result);
            Assert.Single(raised);
            Assert.Equal(42L, raised[0]!.RunId);

            OptimizationRepository.PublishResult(null);
            Assert.Null(OptimizationRepository.Result);
            Assert.Equal(2, raised.Count);
            Assert.Null(raised[1]);
        }
        finally
        {
            OptimizationRepository.ResultPublished -= handler;
        }
    }

    [Fact]
    public void PublishNoteRaisesTheEventWithLevelAndMessage()
    {
        string? receivedLevel = null;
        string? receivedMsg = null;
        Action<string, string> handler = (level, msg) => { receivedLevel = level; receivedMsg = msg; };
        OptimizationRepository.NotePublished += handler;
        try
        {
            OptimizationRepository.PublishNote("W", "書込に失敗しました");
            Assert.Equal("W", receivedLevel);
            Assert.Equal("書込に失敗しました", receivedMsg);
        }
        finally
        {
            OptimizationRepository.NotePublished -= handler;
        }
    }

    [Fact]
    public void ClearResetsProgressAndResultButNotRunning()
    {
        OptimizationRepository.SetRunning(true);
        OptimizationRepository.PublishProgress(new OptimizationRepository.BgProgress("RSI", 0, 0, 0, 0L, 0L));
        OptimizationRepository.PublishResult(
            new OptimizationRepository.BgResult(Array.Empty<int[]>(), EmptyReport(), "完了"));

        OptimizationRepository.Clear();

        Assert.Null(OptimizationRepository.Progress);
        Assert.Null(OptimizationRepository.Result);
        // Clear() は Running には触れない（Kotlin原本 clear() の意味論のまま）。
        Assert.True(OptimizationRepository.Running);

        OptimizationRepository.SetRunning(false); // 後始末
    }
}
