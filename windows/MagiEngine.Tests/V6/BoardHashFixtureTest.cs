using System.Text;
using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// 全面改修 P0 の安全網（Android <c>docs/overhaul_design.md</c> §6.1 の 1）。6 fixture で、決定的モードの後処理を通した
/// 最終盤面を <c>StateFingerprint</c> でハッシュし、Android が生成した基準版の写しと照合する。
/// 探索本体は決定的でないため対象外。seed は決定的モードでは盤面に効かない（1〜3 で同一ハッシュ）ため 1 つだけ。
/// </summary>
public class BoardHashFixtureTest
{
    [Fact]
    public void DeterministicPostChainBoardMatchesTheBaselineHashForEveryFixtureAndSeed()
    {
        var sb = new StringBuilder();
        foreach (var name in CheckerSnapshotTest.Fixtures)
        {
            var st = CheckerSnapshotTest.Load(name);
            var input = st.Schedule.ToIntArray2D();
            foreach (var seed in new[] { 1L })
            {
                var outSched = V6HotfixPasses.RunPostOptimization(st, input.Copy2D(), "p0", seed: seed,
                    deadlineMs: EngineClock.NowMs() + 3_600_000L,
                    parameters: new V6HotfixPasses.PostOptimizationParams(Deterministic: true,
                        C1LnsMaxEvaluations: 5_000, PersonalLnsMaxEvaluations: 5_000)).Schedule;
                var hash = StateFingerprint.Of(st with { Schedule = outSched.Select(r => (IReadOnlyList<int>)r.ToList()).ToList() });
                sb.Append($"{name} seed={seed} hash={hash}\n");
            }
        }
        Assert.Equal(CheckerSnapshotTest.ReadSnapshot("board_hash.txt"), sb.ToString());
    }
}
