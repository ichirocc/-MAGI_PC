using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>Kotlin <c>SaWishLockTest</c>（3.334.0）の移植。SA/LAHC の近傍が実現可能な希望の入ったセルを触らないことを固定する。
/// <c>InitialAssignment</c> が実現可能な希望を先に盤面へ入れるので、近傍が触らなければ出力でも希望どおりのまま残る。</summary>
public class SaWishLockTest
{
    /// <summary>2職員×12日・シフト3種。希望は全部「担当できる」＝実現可能。</summary>
    private static MagiState State() => MinimalState.Build(
        startDate: "2026-08-01", endDate: "2026-08-12",
        shifts: new[] { new Shift("休", "休", "0", "", ShiftRole.Rest), new Shift("A", "A", "1", ""), new Shift("B", "B", "1", "") },
        groups: new[] { new Group("G", "G") },
        staffList: new[] { new Staff("s0", 0), new Staff("s1", 0) },
        use2Patterns: false,
        groupShift: new[] { (IReadOnlyList<int>)new[] { 1, 1, 1 } },
        groupShiftApt: new[] { (IReadOnlyList<string>)new[] { "", "", "" } },
        schedule: new[] { (IReadOnlyList<int>)new int[12], new int[12] },
        // 希望を散らす（連続していると opBlockFill の効果が見えない）。
        wishes: new Dictionary<string, int> { ["0,1"] = 1, ["0,4"] = 2, ["0,9"] = 1, ["1,2"] = 2, ["1,6"] = 1, ["1,11"] = 2 },
        // 窓の要件を入れて opBlockFill を実際に走らせる。
        cons1: new[] { new C1Row("5", "休", "1") },
        cons2: new List<C2Row>(), cons3: new List<C3Row>(), cons3n: new List<C3Row>(),
        cons3m: new List<C3Row>(), cons3mn: new List<C3Row>(), cons41: new List<C41Row>(), cons42: new List<C42Row>(),
        staffRange: new Dictionary<string, Range> { ["0,0"] = new("2", "6"), ["1,0"] = new("2", "6") },
        needDay1: new Dictionary<string, string>(), needDay2: new Dictionary<string, string>());

    [Fact(Skip = "strongPerturbFlat はネイティブ経路（magi_native.cpp）専用で C# には移植していない（SaOptimizer.cs の移植判断）")]
    public void StrongPerturbNeverMovesAFeasibleWish() { }

    [Fact]
    public async Task SearchNeverMovesACellThatHoldsAFeasibleWish()
    {
        var st = State();
        var p = new Problem(st);
        var ev = new Evaluator(p);

        var init = p.InitialAssignment();
        int locked = 0;
        for (int i = 0; i < p.S; i++) for (int j = 0; j < p.T; j++) if (p.WishLocked(i, j))
        {
            locked++;
            Assert.Equal(p.Wish[i][j], init[i][j]);
        }
        Assert.True(locked >= 6, "希望固定セルが無いと何も検証できない");

        for (long seed = 1L; seed <= 6L; seed++)
        {
            var r = await new SaOptimizer(p, ev).Run(new SaParams(BudgetMs: 400L, Workers: 1, Seed: seed));
            for (int i = 0; i < p.S; i++) for (int j = 0; j < p.T; j++) if (p.WishLocked(i, j))
                Assert.True(p.Wish[i][j] == r.Schedule[i][j], $"seed={seed} で職員{i} 日{j} の希望が動いた（近傍が固定セルを触っている）");
            var rep = UnifiedViolationChecker.Check(st, r.Schedule);
            Assert.Equal(0, rep.Breakdown.TryGetValue("pref", out var v) ? v : 0);
        }
    }
}
