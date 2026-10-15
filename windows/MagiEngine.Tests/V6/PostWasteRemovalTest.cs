using MagiEngine.Model;
using MagiEngine.Tests.Fixtures;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>Kotlin <c>PostWasteRemovalTest</c> の 1 対 1 移植: 出力不変の無駄取り 2 件（<see cref="PolishGate.RotationDeltaPrescreen"/>・
/// <see cref="PolishGate.RoundPassMemo"/>）。ON/OFF で盤面が同じこと。</summary>
public class PostWasteRemovalTest
{
    private static MagiState Load(string name) => StateJsonSerializer.Parse(FixtureLoader.ReadRaw(name));

    private static T WithGates<T>(bool prescreen, bool memo, Func<T> block)
    {
        var a = PolishGate.RotationDeltaPrescreen; var b = PolishGate.RoundPassMemo;
        PolishGate.RotationDeltaPrescreen = prescreen; PolishGate.RoundPassMemo = memo;
        try { return block(); } finally { PolishGate.RotationDeltaPrescreen = a; PolishGate.RoundPassMemo = b; }
    }

    /// <summary>3 者回転を盤面に当てたあとの差分キーが、正式評価の (hard, weightedScore, total) と毎手一致する（群をまたぐ組も含む）。</summary>
    [Fact]
    public void deltaKeyAgreesWithCheckerOnRotations()
    {
        foreach (var q in new[] { false, true })
        {
            var st = Load("sept2026_state.json");
            var p = new Problem(st, q);
            var work = ScheduleUtil.NormalizeSchedule(st.Schedule.ToIntArray2D(), p);
            var de = new DeltaEvaluator(p); de.Reset(work);
            var rng = new JavaRandom(23L);
            var cross = 0;
            for (var it = 0; it < 150; it++)
            {
                var w = 2 + rng.NextInt(2); var j = rng.NextInt(p.T - w + 1);
                var abc = Enumerable.Range(0, p.S).Shuffled(rng).Take(3).ToArray();
                int a = abc[0], b = abc[1], c = abc[2];
                if (Enumerable.Range(0, w).Any(t => p.WishLocked(a, j + t) || p.WishLocked(b, j + t) || p.WishLocked(c, j + t))) continue;
                if (p.Sgrp[a] != p.Sgrp[b] || p.Sgrp[b] != p.Sgrp[c]) cross++;
                for (var t = 0; t < w; t++)
                {
                    int sa = work[a][j + t], sb = work[b][j + t], sc = work[c][j + t];
                    work[a][j + t] = sb; work[b][j + t] = sc; work[c][j + t] = sa;
                    de.Apply(a, j + t, sb); de.Apply(b, j + t, sc); de.Apply(c, j + t, sa);
                }
                var rep = UnifiedViolationChecker.Check(st, work, quantitativeRangeEval: q);
                Assert.True(V6HotfixPasses.DeltaKeyMatches(de, rep), $"q={q}");
                Assert.Equal(new[] { (long)rep.Hard, (long)rep.WeightedScore, (long)rep.Total }, de.ReportKey());
                Assert.False(V6HotfixPasses.DeltaKeyBetter(de.ReportKey(), rep));
            }
            Assert.True(cross > 0, "群をまたぐ組を含む");
        }
    }

    [Fact]
    public void rotationPrescreenKeepsTheSameBoard()
    {
        foreach (var f in new[] { "golden_state.json", "sample_state_v6.json" })
        {
            var st = Load(f);
            var s0 = st.Schedule.ToIntArray2D();
            var anchors = new HashSet<string> { "vio-c3", "vio-c3m", "vio-c3mn", "vio-c1" };
            var off = WithGates(false, true, () => V6HotfixPasses.ApplyBlockRotationPolish(st, s0.Copy2D(), anchors, "t", maxPasses: 1));
            var on = WithGates(true, true, () => V6HotfixPasses.ApplyBlockRotationPolish(st, s0.Copy2D(), anchors, "t", maxPasses: 1));
            Assert.True(ScheduleUtil.ContentDeepEquals(off.NewSchedule, on.NewSchedule), f);
            Assert.Equal(off.Applied, on.Applied);
            Assert.Equal(off.Report?.WeightedScore, on.Report?.WeightedScore);
        }
    }

    [Fact]
    public void roundPassMemoKeepsTheSameBoard()
    {
        foreach (var f in new[] { "golden_state.json", "sample_state_v6.json" })
        {
            var st = Load(f);
            var parameters = new V6HotfixPasses.PostOptimizationParams(Deterministic: true, C1LnsMaxEvaluations: 2_000, PersonalLnsMaxEvaluations: 2_000);
            V6HotfixPasses.V6PostOptimizationResult Run(bool memo) => WithGates(true, memo, () =>
                V6HotfixPasses.RunPostOptimization(st, st.Schedule.ToIntArray2D(), "t", seed: 3L, parameters: parameters));
            var off = Run(false); var on = Run(true);
            Assert.True(ScheduleUtil.ContentDeepEquals(off.Schedule, on.Schedule), f);
            Assert.Equal(off.Report.WeightedScore, on.Report.WeightedScore);
            Assert.Equal(off.StageRecords!.Select(r => (r.Key, r.Applied)), on.StageRecords!.Select(r => (r.Key, r.Applied)));
        }
    }

    /// <summary>[3.686.0] 基準で 0 の回数の族（下限割れ）が回転で現れる盤面でも、差分評価の前判定は正式評価と同じ盤面を返す。</summary>
    [Fact]
    public void rotationPrescreenKeepsTheSameBoardWhenARangeFamilyAppears()
    {
        var st0 = Load("sept2026_state.json");
        var s0 = st0.Schedule.ToIntArray2D();
        // 全員の各シフトの回数をそのまま下限にする＝基準では low 0、回転で 1 つでも減れば現れる。
        var ranges = new Dictionary<string, MagiEngine.Model.Range>(st0.StaffRange);
        for (var i = 0; i < s0.Length; i++)
            for (var k = 0; k < st0.Shifts.Count; k++)
            {
                var n = s0[i].Count(v => v == k);
                if (n > 0 && !ranges.ContainsKey($"{i},{k}")) ranges[$"{i},{k}"] = new MagiEngine.Model.Range($"{n}", "");
            }
        var st = st0 with { StaffRange = ranges };
        foreach (var q in new[] { false, true })
        {
            Assert.Equal(0, UnifiedViolationChecker.Check(st, ScheduleUtil.NormalizeSchedule(s0, new Problem(st, q)), quantitativeRangeEval: q).Breakdown.GetValueOrDefault("low", 0));
            var anchors = new HashSet<string> { "vio-c3", "vio-c3m", "vio-c3mn", "vio-c1" };
            var off = WithGates(false, true, () => V6HotfixPasses.ApplyBlockRotationPolish(st, s0.Copy2D(), anchors, "t", maxPasses: 1, quantitativeRangeEval: q));
            var on = WithGates(true, true, () => V6HotfixPasses.ApplyBlockRotationPolish(st, s0.Copy2D(), anchors, "t", maxPasses: 1, quantitativeRangeEval: q));
            Assert.True(ScheduleUtil.ContentDeepEquals(off.NewSchedule, on.NewSchedule), $"q={q}");
            Assert.Equal(off.Applied, on.Applied);
            Assert.True(on.Logs.All(l => !l.Message.Contains("食い違い")), $"q={q} 食い違いで止めていない");
        }
    }

    [Fact]
    public void prescreenVerifiesCloseCallsAndEveryNth()
    {
        var st = Load("golden_state.json");
        var rep = UnifiedViolationChecker.Check(st, st.Schedule.ToIntArray2D());
        var w = (long)rep.WeightedScore;
        Assert.True(V6HotfixPasses.PrescreenNeedsVerify(new[] { (long)rep.Hard, w, 0L }, rep, 1));
        Assert.True(V6HotfixPasses.PrescreenNeedsVerify(new[] { (long)rep.Hard + 1, w + 100, 0L }, rep, 0));
        Assert.False(V6HotfixPasses.PrescreenNeedsVerify(new[] { (long)rep.Hard + 1, w + 100, 0L }, rep, 1));
    }
}
