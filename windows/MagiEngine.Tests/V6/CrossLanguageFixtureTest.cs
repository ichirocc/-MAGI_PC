using MagiEngine.Model;
using MagiEngine.Tests.Fixtures;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [Android 3.509.1/教訓#53] Kotlin・C++・C# の 3 実装を同じ fixture と同じ期待値ファイルで固定する契約テスト。
/// 期待値ファイル（<c>*_eval_expected.txt</c>: hard= / soft=）は Android の <c>app/src/test/resources</c> と同一の複製。
/// Kotlin 側は <c>NativeParityFixtureTest</c>、C++ 側は CI の parity harness が同じファイルを読む。
/// 評価器や <c>Problem</c> の展開（apt/range 等）を片側だけ変えると、ここが落ちる。
/// </summary>
public class CrossLanguageFixtureTest
{
    [Theory]
    [InlineData("golden_state.json", "golden_eval_expected.txt")]
    [InlineData("sample_state_v6.json", "sample_v6_eval_expected.txt")]
    [InlineData("blocked_covu_state.json", "blocked_covu_eval_expected.txt")]
    public void EvaluatorMatchesTheSharedCrossLanguageExpectation(string stateFile, string expectFile)
    {
        var st = StateJsonSerializer.Parse(FixtureLoader.ReadRaw(stateFile));
        var expected = FixtureLoader.ReadRaw(expectFile).Split('\n')
            .Select(l => l.Trim()).Where(l => l.StartsWith("hard=") || l.StartsWith("soft="))
            .ToDictionary(l => l.Substring(0, 4), l => long.Parse(l.Substring(5)));
        Assert.Equal(new[] { "hard", "soft" }, expected.Keys.OrderBy(k => k));
        var p = new Problem(st);
        var sched = st.Schedule.Select(r => r.ToArray()).ToArray();
        var ev = new Evaluator(p);
        var (hard, soft) = ev.Split(ev.FullEval(sched));
        Assert.Equal(expected["hard"], hard);
        Assert.Equal(expected["soft"], soft);
    }
}
