using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// Faithful port of Kotlin's <c>V6SearchOperatorsTest</c>.
///
/// [/code-review, need2単独定義セル見落とし修正] <c>FindCovOFix</c> の検証。旧実装は
/// <c>p.Need1[k][j]</c> のみで過剰スキャンを行い、need1未設定・need2のみで上限が定義された
/// シフトの過剰配置を一切検出できなかった（3.173.0のCoverageDiagnosis修正・3.309.0の
/// V6LateOperators.isBalanceable修正と同根、covOCell/covUCellをsource of truthとして統一）。
/// </summary>
public class V6SearchOperatorsTest
{
    // shift 0="休"(needなし), shift 1="X"(need1未設定・need2=1のみで上限定義)
    private static MagiState State() => MinimalState.Build(
        startDate: "2026-01-01", endDate: "2026-01-01",
        shifts: new List<Shift> { new("休", "休", "", ""), new("X", "X", "", "1") },
        groups: new List<Group> { new("G", "G") },
        staffList: new List<Staff> { new("s0", 0), new("s1", 0) },
        use2Patterns: true,
        groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
        groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" } },
        // 両名ともXへ配置済み＝need2=1に対し2人在勤で過剰配置(covO=1)。
        schedule: new List<IReadOnlyList<int>> { new List<int> { 1 }, new List<int> { 1 } }
    );

    [Fact]
    public void FindCovOFixDetectsNeed2OnlyOverCoverage()
    {
        var p = new Problem(State());
        var eval = new DeltaEvaluator(p);
        Assert.Equal(1, p.CovOCell(1, 0, eval.CountOnDay(1, 0))); // 前提: X(day0)が2人で過剰配置(need2=1)

        var fix = V6SearchOperators.FindCovOFix(p, eval, new JavaRandom(1));
        Assert.NotNull(fix); // need2のみで定義された過剰配置も検出し修正候補を返すこと
        var (i, j, newK) = (fix![0], fix[1], fix[2]);
        Assert.Equal(0, j);
        Assert.Equal(1, eval.At(i, j)); // 元は在勤中のX(=1)を退避させる手であること
        Assert.Equal(0, newK); // 唯一の移動先候補=休(index0)
    }

    /// <summary>過剰配置が無ければ null（誤検知しないことの対照）。</summary>
    [Fact]
    public void FindCovOFixReturnsNullWhenNoOverCoverage()
    {
        var st = State() with
        {
            Schedule = new List<IReadOnlyList<int>> { new List<int> { 1 }, new List<int> { 0 } },
        }; // 1人だけXへ＝need2=1を満たすのみ
        var p = new Problem(st);
        var eval = new DeltaEvaluator(p);
        Assert.Equal(0, p.CovOCell(1, 0, eval.CountOnDay(1, 0)));
        Assert.Null(V6SearchOperators.FindCovOFix(p, eval, new JavaRandom(1)));
    }

    /// <summary>
    /// 候補族の巡回は先頭から 1 件ずつ交互で、尽きた列は飛ばす。列挙を途中で止めても残る enumerator を
    /// 必ず破棄する（希望島研磨の同日・窓・両翼の交互評価が依存する契約）。
    /// </summary>
    [Fact]
    public void RoundRobinInterleavesSourcesAndDisposesEnumeratorsOnEarlyExit()
    {
        var disposed = new List<string>();
        IEnumerable<string> Source(string name, int count)
        {
            try { for (var i = 0; i < count; i++) yield return $"{name}{i}"; }
            finally { disposed.Add(name); }
        }

        var all = V6SearchOperators.RoundRobin(Source("a", 3), Source("b", 1), Source("c", 2)).ToList();
        Assert.Equal(new[] { "a0", "b0", "c0", "a1", "c1", "a2" }, all);
        Assert.Equal(new[] { "b", "c", "a" }, disposed);

        disposed.Clear();
        var firstTwo = V6SearchOperators.RoundRobin(Source("a", 3), Source("b", 3)).Take(2).ToList();
        Assert.Equal(new[] { "a0", "b0" }, firstTwo);
        Assert.Equal(2, disposed.Count);
        Assert.Contains("a", disposed);
        Assert.Contains("b", disposed);
    }
}
