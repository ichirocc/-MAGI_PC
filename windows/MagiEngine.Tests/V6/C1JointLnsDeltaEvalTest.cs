using MagiEngine.Model;
using MagiEngine.Tests.Fixtures;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// <see cref="C1JointLnsPolish.Config.DeltaChildEval"/> の検証。G1＝子の差分 report が正式 check と全族一致、
/// G2＝決定的モード（評価回数上限）で flag OFF/ON の最終盤面と report が一致。
/// </summary>
public class C1JointLnsDeltaEvalTest
{
    private static readonly string[] Fixtures = { "golden_state.json", "sample_state_v6.json", "blocked_covu_state.json", "sept2026_state.json" };

    private static MagiState Load(string name) => StateJsonSerializer.Parse(FixtureLoader.ReadRaw(name));

    private static int JavaStringHash(string s)
    {
        int h = 0;
        foreach (var ch in s) h = unchecked(31 * h + ch);
        return h;
    }

    private static MagiState Synthetic(long seed)
    {
        var rng = new JavaRandom(seed);
        var shifts = new List<Shift> { new("休", "休", "", ""), new("A", "A", "1", "2"), new("B", "B", "1", "1"), new("C", "C", "1", "") };
        var staff = Enumerable.Range(0, 7).Select(i => new Staff($"s{i}", i % 2, (i / 2) % 2)).ToList();
        const int t = 14;
        int[] odd = { 0, 2, 3 };
        var schedule = new List<IReadOnlyList<int>>();
        for (int i = 0; i < staff.Count; i++)
        {
            var row = new List<int>();
            for (int j = 0; j < t; j++) row.Add(i % 2 == 0 ? rng.NextInt(3) : odd[rng.NextInt(3)]);
            schedule.Add(row);
        }
        return MinimalState.Build(
            startDate: "2025-01-01", endDate: "2025-01-14",
            shifts: shifts, groups: new List<Group> { new("G0", "G0"), new("G1", "G1") }, staffList: staff, use2Patterns: true,
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1, 0 }, new List<int> { 1, 0, 1, 1 } },
            groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "4", "3", "" }, new List<string> { "", "", "3", "4" } },
            schedule: schedule, wishes: new Dictionary<string, int> { ["0,0"] = 0, ["1,4"] = 2, ["2,2"] = 1 },
            staffRange: new Dictionary<string, Range> { ["0,1"] = new("3", "5"), ["1,2"] = new("", "4"), ["3,3"] = new("2", "") },
            needDay1: new Dictionary<string, string> { ["1,0"] = "2" }, needDay2: new Dictionary<string, string> { ["2,5"] = "2" },
            cons1: new List<C1Row> { new("3", "A", "1"), new("5", "A", "2"), new("4", "休", "1"), new("6", "B", "2") },
            cons2: new List<C2Row> { new("B", "3") }, cons3: new List<C3Row> { new(new List<string> { "A", "B" }) },
            cons3n: new List<C3Row> { new(new List<string> { "C", "C" }) },
            cons3m: new List<C3Row> { new(new List<string> { "B", "A" }) }, cons3mn: new List<C3Row> { new(new List<string> { "A", "A" }) },
            cons41: new List<C41Row> { new("G0", "A", "1", "2") }, cons42: new List<C42Row> { new("G0", "G1", "A", "C") },
            skillGroups: new List<Group> { new("SK0", "SK0"), new("SK1", "SK1") },
            cons41s: new List<C41Row> { new("SK0", "A", "", "2") }, cons42s: new List<C42Row> { new("SK0", "SK1", "A", "C") })
            with { Cons3w = new List<C3wRow> { new("B", "C") } };
    }

    /// <summary>1〜3 セルの束。3 セル束の半分は同一職員の近接日（c1 窓が重なる）に置く。</summary>
    private static int[] RandomBundle(JavaRandom rng, int[][] board, int k)
    {
        int s = board.Length, t = board[0].Length;
        int size = 1 + rng.NextInt(3);
        var outList = new List<int>();
        var used = new HashSet<int>();
        int i0 = rng.NextInt(s), j0 = rng.NextInt(t);
        while (outList.Count < size * 3)
        {
            bool near = size == 3 && rng.NextBoolean();
            int i = near ? i0 : rng.NextInt(s);
            int j = near ? Math.Clamp(j0 + rng.NextInt(5) - 2, 0, t - 1) : rng.NextInt(t);
            if (!used.Add(i * t + j)) { if (used.Count >= s * t) break; continue; }
            int nw = rng.NextInt(k);
            if (nw == board[i][j]) nw = (nw + 1) % k;
            outList.Add(i); outList.Add(j); outList.Add(nw);
        }
        return outList.ToArray();
    }

    private static void AssertBundlesMatch(string label, MagiState st, int[][] board, bool q, int n, long seed)
    {
        var p = ScheduleUtil.CachedProblem(st, q);
        var rng = new JavaRandom(seed);
        var bundles = Enumerable.Range(0, n).Select(_ => RandomBundle(rng, board, p.K)).ToList();
        var got = new C1JointLnsPolish.DeltaPool(p).Evaluate(board, bundles);
        var baseRep = UnifiedViolationChecker.Check(st, board, quantitativeRangeEval: q);
        for (int idx = 0; idx < bundles.Count; idx++)
        {
            var cells = bundles[idx];
            var next = board.Copy2D();
            for (int c = 0; c < cells.Length; c += 3) next[cells[c]][cells[c + 1]] = cells[c + 2];
            var want = UnifiedViolationChecker.Check(st, next, quantitativeRangeEval: q);
            var tag = $"{label} q={q} bundle#{idx} [{string.Join(",", cells)}]";
            foreach (var fam in MirrorKeys.All)
                Assert.True(want.Breakdown.GetValueOrDefault(fam, 0) == got[idx].Breakdown.GetValueOrDefault(fam, 0), $"{tag} {fam}");
            Assert.True(want.Hard == got[idx].Hard, $"{tag} hard");
            Assert.True(want.Total == got[idx].Total, $"{tag} total");
            Assert.True(want.WeightedScore == got[idx].WeightedScore, $"{tag} weighted");
            Assert.True(V6SearchOperators.WorstWorsenedFamily(want, baseRep) == V6SearchOperators.WorstWorsenedFamily(got[idx], baseRep), $"{tag} worstWorsened");
        }
    }

    [Fact]
    public void DeltaChildReportsMatchCheckerOnRealFixtures()
    {
        foreach (var name in Fixtures)
        {
            var st = Load(name);
            var board = ScheduleUtil.NormalizeSchedule(st.Schedule.ToIntArray2D(), ScheduleUtil.CachedProblem(st));
            foreach (var q in new[] { false, true }) AssertBundlesMatch(name, st, board, q, 300, JavaStringHash(name));
        }
    }

    [Fact]
    public void DeltaChildReportsMatchCheckerOnSyntheticStates()
    {
        for (long seed = 1; seed <= 6; seed++)
        {
            var st = Synthetic(seed);
            var board = ScheduleUtil.NormalizeSchedule(st.Schedule.ToIntArray2D(), ScheduleUtil.CachedProblem(st));
            foreach (var q in new[] { false, true }) AssertBundlesMatch($"synthetic{seed}", st, board, q, 400, seed * 31);
        }
    }

    // C# の C1JointLnsPolish.Apply は quantitativeRangeEval を受けない（未同期）ため G2 は q=false だけ。
    private static (V6HotfixPasses.CyclicSwapResult Off, V6HotfixPasses.CyclicSwapResult On) RunBoth(MagiState st, int[][] board, int evals)
    {
        V6HotfixPasses.CyclicSwapResult Run(bool delta) => C1JointLnsPolish.Apply(
            st, board.Copy2D(),
            new C1JointLnsPolish.Config(MaxMillis: 60_000L, PatienceMs: 0L, MaxEvaluations: evals, DeltaChildEval: delta));
        return (Run(false), Run(true));
    }

    private static void AssertSameResult(string label, MagiState st, V6HotfixPasses.CyclicSwapResult off, V6HotfixPasses.CyclicSwapResult on)
    {
        Assert.True(off.NewSchedule.Length == on.NewSchedule.Length && off.NewSchedule.Zip(on.NewSchedule).All(z => z.First.SequenceEqual(z.Second)), $"{label} board");
        Assert.Equal(off.Applied, on.Applied);
        var ro = UnifiedViolationChecker.Check(st, off.NewSchedule);
        var rn = UnifiedViolationChecker.Check(st, on.NewSchedule);
        Assert.Equal(ro.Breakdown, rn.Breakdown);
        Assert.Equal(ro.WeightedScore, rn.WeightedScore);
        static string Strip(string m) { int at = m.IndexOf(" 停止=", StringComparison.Ordinal); return at < 0 ? m : m[..at]; }
        Assert.Equal(Strip(off.Logs[0].Message), Strip(on.Logs[0].Message));
    }

    [Fact]
    public void DeterministicModeGivesTheSameBoardWithAndWithoutDelta()
    {
        int applied = 0;
        foreach (var name in Fixtures)
        {
            var st = Load(name);
            var (off, on) = RunBoth(st, st.Schedule.ToIntArray2D(), 4000);
            AssertSameResult(name, st, off, on);
            applied += on.Applied;
        }
        for (long seed = 1; seed <= 4; seed++)
        {
            var st = Synthetic(seed);
            var (off, on) = RunBoth(st, st.Schedule.ToIntArray2D(), 1500);
            AssertSameResult($"synthetic{seed}", st, off, on);
            applied += on.Applied;
        }
        Assert.True(applied >= 4, "採用のある経路を通っている");
    }
}
