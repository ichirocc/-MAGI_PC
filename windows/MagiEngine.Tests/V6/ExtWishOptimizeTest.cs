using System.Reflection;
using MagiEngine.Model;
using MagiEngine.Tests.Fixtures;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// 拡張希望 第 8 節（Kotlin <c>ExtWishOptimizeTest</c> の 1 対 1 移植）: 最適化器は禁止のシフトを新しく置かない。
/// 敵対的な禁止＝禁止なしで最適化器が実際に置いたセルと値をそのまま禁止にし、入力の値を禁止にした既存の違反も混ぜる。
/// 最終番兵が段を戻した（候補生成の漏れ）ことも、後処理の段が禁止を置いたことも失敗として名指しする。
/// </summary>
[Collection("PolishGateGlobal")]
public class ExtWishOptimizeTest
{
    private static MagiState Load(string name) => StateJsonSerializer.Parse(FixtureLoader.ReadRaw(name));

    private static MagiState Adversarial(MagiState st, int[][] input, int[][] out0)
    {
        var p = new Problem(st);
        var start = DateOnly.Parse(st.StartDate);
        var kigou = st.Shifts.Select(s => s.Kigou).ToList();
        var cur = st;
        var n = 0;
        for (var i = 0; i < p.S; i++)
            for (var j = 0; j < p.T; j++)
            {
                if (p.Wish[i][j] >= 0 || p.Pinned(i, j)) continue;
                int ban;
                if (out0[i][j] != input[i][j] && (i + j) % 2 == 0) ban = out0[i][j];
                else if ((i * 7 + j) % 11 == 0) ban = input[i][j];
                else continue;
                if (ban < 0 || ban >= p.K) continue;
                var r = ExtWishRules.Sanitize(cur, new ExtWish(i, new[] { start.AddDays(j).ToString("yyyy-MM-dd") }, new[] { kigou[ban] }));
                if (r.Saved is not null) { cur = cur with { ExtWishes = (cur.ExtWishes ?? Array.Empty<ExtWish>()).Append(r.Saved).ToList() }; n++; }
            }
        Assert.True(n > 10, "禁止が作れていない");
        return cur;
    }

    private static Dictionary<string, int> WithProbe(int[][] baseSched, Problem p, Action body)
    {
        var offenders = new Dictionary<string, int>();
        int[][]? prev = null;
        V6HotfixPasses.PostChain.StageProbe = (key, work) =>
        {
            var c = p.ExtBanNewCells(prev ?? baseSched, work).Count;
            if (c > 0) offenders[key] = offenders.GetValueOrDefault(key) + c;
            prev = work.Copy2D();
        };
        try { body(); } finally { V6HotfixPasses.PostChain.StageProbe = null; }
        return offenders;
    }

    [Fact]
    public async Task OptimizerNeverPlacesABannedShiftAcrossAlgorithms()
    {
        foreach (var file in new[] { "sample_state_v6.json", "sept2026_state.json" })
        {
            var st0 = Load(file);
            var input = st0.Schedule.ToIntArray2D();
            var out0 = (await V6FinalPort.HandleOptimize(st0, secondsRaw: 2, schedule: input.Copy2D(), workers: 1,
                requestedAlgorithm: V6Algorithm.V5, allowImpossible: true, seed: 3L)).Schedule;
            var st = Adversarial(st0, input, out0);
            var p = new Problem(st);
            var baseSched = V6NativeOptimizer.ClearCappedCells(st, p.WithManualPins(ScheduleUtil.NormalizeSchedule(input, p))).Schedule;
            foreach (var algo in new[] { V6Algorithm.V5, V6Algorithm.Alns, V6Algorithm.Rsi, V6Algorithm.RsiPlus, V6Algorithm.Portfolio })
            {
                V6FinalPort.ActionResult? res = null;
                var offenders = WithProbe(baseSched, p, () =>
                    res = V6FinalPort.HandleOptimize(st, secondsRaw: 2, schedule: input.Copy2D(), workers: 2,
                        requestedAlgorithm: algo, allowImpossible: true, seed: 5L).GetAwaiter().GetResult());
                var fresh = p.ExtBanNewCells(baseSched, res!.Schedule);
                var sentinel = res.Logs.Where(l => l.Message.Contains("拡張希望の禁止が")).Select(l => l.Message).ToList();
                Assert.True(fresh.Count == 0, $"{file} {algo}: 最終盤面に禁止の新規配置 {fresh.Count}");
                Assert.True(sentinel.Count == 0, $"{file} {algo}: 最終番兵が段を戻した＝候補生成の漏れ {string.Join(" / ", sentinel)} 後処理の段={string.Join(",", offenders.Select(kv => $"{kv.Key}={kv.Value}"))}");
                Assert.True(offenders.Count == 0, $"{file} {algo}: 後処理の段が禁止を新しく置いた {string.Join(",", offenders.Select(kv => $"{kv.Key}={kv.Value}"))}");
            }
        }
    }

    /// <summary>[3.653.0] 拡張希望の違反は必須（重み 8000＝希望と同じ）＝入力に既にある違反を探索が解消する（旧: 採点外で残った）。</summary>
    [Fact]
    public async Task ExistingViolationsAreRepairedNowThatTheyAreHard()
    {
        var st0 = Load("sept2026_state.json");
        var input = st0.Schedule.ToIntArray2D();
        var p0 = new Problem(st0);
        var start = DateOnly.Parse(st0.StartDate);
        var st = st0;
        for (var i = 0; i < p0.S; i++)
            for (var j = 0; j < p0.T; j++)
            {
                if (p0.Wish[i][j] >= 0 || p0.Pinned(i, j) || (i * 7 + j) % 11 != 0 || input[i][j] < 0 || input[i][j] >= p0.K) continue;
                var r = ExtWishRules.Sanitize(st, new ExtWish(i, new[] { start.AddDays(j).ToString("yyyy-MM-dd") }, new[] { st0.Shifts[input[i][j]].Kigou }));
                if (r.Saved is not null) st = st with { ExtWishes = (st.ExtWishes ?? Array.Empty<ExtWish>()).Append(r.Saved).ToList() };
            }
        var before = UnifiedViolationChecker.Check(st, input);
        var n0 = before.Breakdown.GetValueOrDefault("extWish", 0);
        Assert.True(n0 > 10, $"入力の違反が作れていない ({n0})");
        Assert.Equal(n0, before.ExtWishCells.Count);
        Assert.Equal(n0, before.Hard);   // sept2026 の入力盤面は必須 0
        var outSched = (await V6FinalPort.HandleOptimize(st, secondsRaw: 3, schedule: input.Copy2D(), workers: 2,
            requestedAlgorithm: V6Algorithm.V5, allowImpossible: true, seed: 7L)).Schedule;
        var after = UnifiedViolationChecker.Check(st, outSched);
        var n1 = after.Breakdown.GetValueOrDefault("extWish", 0);
        // 拡張希望の違反は先に解ける（Android 負荷なし 3 秒で 23→0）。玉突きで出る禁止の並びは時間で減る＝時間制なので緩く見る。
        Assert.True(n1 * 4 <= n0, $"拡張希望の違反が残りすぎ {n1}/{n0} {string.Join(",", after.ExtWishCells)}");
        Assert.True(after.Hard < before.Hard, $"必須が減っていない {before.Hard}→{after.Hard}");
    }

    /// <summary>既定 OFF の研磨も含めて PolishGate の真偽フラグを全部 ON にした決定的モードの後処理。</summary>
    [Fact]
    public void PostProcessingWithEveryFlagOnNeverPlacesABannedShift()
    {
        var flags = typeof(PolishGate).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(bool)).ToList();
        var saved = flags.ToDictionary(f => f, f => (bool)f.GetValue(null)!);
        try
        {
            foreach (var f in flags) f.SetValue(null, true);
            foreach (var file in new[] { "sample_state_v6.json", "sept2026_state.json" })
            {
                var st0 = Load(file);
                var input = st0.Schedule.ToIntArray2D();
                var prm = new V6HotfixPasses.PostOptimizationParams(Deterministic: true, C1LnsMaxEvaluations: 5_000, PersonalLnsMaxEvaluations: 5_000);
                var out0 = V6HotfixPasses.RunPostOptimization(st0, input.Copy2D(), "t", seed: 1L, deadlineMs: EngineClock.NowMs() + 3_600_000L, parameters: prm).Schedule;
                var st = Adversarial(st0, input, out0);
                var p = new Problem(st);
                var baseSched = p.WithManualPins(ScheduleUtil.NormalizeSchedule(input, p));
                int[][]? outB = null;
                var offenders = WithProbe(baseSched, p, () =>
                    outB = V6HotfixPasses.RunPostOptimization(st, input.Copy2D(), "t", seed: 1L, deadlineMs: EngineClock.NowMs() + 3_600_000L, parameters: prm).Schedule);
                Assert.True(offenders.Count == 0, $"{file}: 後処理の段が禁止を新しく置いた {string.Join(",", offenders.Select(kv => $"{kv.Key}={kv.Value}"))}");
                Assert.Empty(p.ExtBanNewCells(baseSched, outB!));
            }
        }
        finally { foreach (var (f, v) in saved) f.SetValue(null, v); }
    }
}

/// <summary>PolishGate の全フラグ・PostChain.StageProbe（どちらも静的）を書き換えるので、他のテストと並べて走らせない。</summary>
[CollectionDefinition("PolishGateGlobal", DisableParallelization = true)]
public class PolishGateGlobalCollection { }
