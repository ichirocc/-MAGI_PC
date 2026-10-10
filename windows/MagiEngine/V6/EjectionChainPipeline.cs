using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>[Kotlin 原本 <c>EjectionChainPipeline</c>] 残差があるときだけ安い予察（深さ 2→4）を行い、採用ゲートを通る手順が見つかった起点にだけ深い探索の予算を
/// 回す（当たりが無ければ深く探さない）。予察・深い探索は盤面を変えずに手順を集め、採用はいまの盤面で取り直して採用ゲートを通るものだけ（段の数値と測定は Android の docs/history 3.656.0）。</summary>
internal static class EjectionChainPipeline
{
    public enum Focus { OFF, C1, SOFT, BOTH }

    /// <summary>測定用（受け入れ条件 A2/A3 の比較腕）: 中段と深い探索を走らせず、浅い予察の当たりだけを採る。本番は false。</summary>
    internal static volatile bool ShallowOnly = false;

    public sealed record Config(
        Focus Focus,
        /// <summary>時間でなく評価回数で止める（ベンチと再現性の検証用）。</summary>
        bool Deterministic = false,
        long ShallowEvaluations = 3_000L,
        long ShallowMillis = 120L,
        long MidEvaluations = 8_000L,
        long MidMillis = 200L,
        /// <summary>中段を走らせる残差の下限。</summary>
        int MidResidual = 3,
        /// <summary>深い探索の時間の上限（焦点の合計）。null＝玉突きの上限秒（<see cref="PolishGate.EjectionChainMaxMillis"/>）。</summary>
        long? DeepMaxMillis = null,
        /// <summary>決定的モードの深い探索の評価回数（焦点の合計）。</summary>
        long DeepEvaluations = 360_000L,
        /// <summary>入れ替えも 1 手に使う。null＝<see cref="PolishGate.EjectionChainSwapMoves"/>。</summary>
        bool? SwapMoves = null);

    /// <summary>焦点 1 つぶんの記録。DeepRan が真なら必ず当たり（浅＋中）が 1 件以上ある。</summary>
    public sealed class Telemetry
    {
        public Telemetry(string focus) { FocusName = focus; }
        public string FocusName { get; }
        public int Residual;
        public string Skip = "";
        public int Seeds;
        public long ShallowEvaluations; public long ShallowMs; public int ShallowHits;
        public bool MidRan; public long MidEvaluations; public long MidMs; public int MidHits;
        public bool DeepRan; public long DeepBudgetMs; public long DeepBudgetEvaluations;
        public long DeepEvaluations; public long DeepMs; public int DeepCandidates;
        public int Committed;
        public string EndReason = "";
        public ViolationReport? Before;
        public ViolationReport? After;

        public string Line()
        {
            var b = Before; var a = After ?? Before;
            var mid = MidRan ? $"評価{MidEvaluations}/{MidMs}ms/当たり{MidHits}" : "なし";
            var deep = DeepRan ? $"予算{(DeepBudgetEvaluations > 0 ? $"{DeepBudgetEvaluations}評価" : $"{DeepBudgetMs}ms")}/評価{DeepEvaluations}/{DeepMs}ms/候補{DeepCandidates}" : "なし";
            return $"玉突きパイプライン[焦点={FocusName} 残差={Residual} 起点={Seeds} 浅=評価{ShallowEvaluations}/{ShallowMs}ms/当たり{ShallowHits} 中={mid} 深={deep} " +
                $"採用={Committed}件 終了={EndReason}]" +
                (b != null && a != null ? $": HARD {b.Hard}->{a.Hard} 合計 {b.Total}->{a.Total} 重み {(long)b.WeightedScore}->{(long)a.WeightedScore}" : "");
        }
    }

    private sealed record LegOut(int[][] Work, ViolationReport Report, int CommittedCells, long DeepMs, long DeepEvaluations);

    public static V6HotfixPasses.CyclicSwapResult Apply(
        MagiState state, int[][] schedule, Config config,
        bool previousImproved = false, long deadlineMs = long.MaxValue,
        Func<bool>? shouldStop = null, bool quantitativeRangeEval = false,
        List<Telemetry>? telemetry = null)
    {
        var stop = shouldStop ?? (() => false);
        var p = ScheduleUtil.CachedProblem(state, quantitativeRangeEval);
        var work = ScheduleUtil.NormalizeSchedule(schedule, p);
        var rep0 = UnifiedViolationChecker.Check(state, work, quantitativeRangeEval);
        var rep = rep0;
        var legs = config.Focus switch
        {
            Focus.C1 => new[] { (C1EjectionChainPolish.Origin.C1, false) },
            Focus.SOFT => new[] { (C1EjectionChainPolish.Origin.SOFT, false) },
            Focus.BOTH => new[] { (C1EjectionChainPolish.Origin.C1, false), (C1EjectionChainPolish.Origin.SOFT, true) },
            _ => Array.Empty<(C1EjectionChainPolish.Origin, bool)>(),
        };
        var pinBlocks = new PinBlockAttribution();
        var deepMsLeft = config.DeepMaxMillis ?? PolishGate.EjectionChainMaxMillis;
        var deepEvaluationsLeft = config.DeepEvaluations;
        var applied = 0;
        var logs = new List<MirrorLog>();
        for (var n = 0; n < legs.Length; n++)
        {
            var (origin, skipC1) = legs[n];
            var share = (long)(legs.Length - n);
            var tel = new Telemetry(origin == C1EjectionChainPolish.Origin.C1 ? "C1" : "SOFT");
            var outLeg = RunLeg(state, p, work, rep, origin, skipC1, config, previousImproved, deadlineMs, stop,
                quantitativeRangeEval, pinBlocks, deepMsLeft / share, deepEvaluationsLeft / share, tel);
            work = outLeg.Work; rep = outLeg.Report; applied += outLeg.CommittedCells;
            deepMsLeft -= outLeg.DeepMs; deepEvaluationsLeft -= outLeg.DeepEvaluations;
            telemetry?.Add(tel);
            logs.Add(new MirrorLog(tag: "EjectionPipeline", message: tel.Line()));
        }
        return new V6HotfixPasses.CyclicSwapResult(work, rep0.Total, rep.Total, applied, logs, PinBlocks: pinBlocks, Report: rep);
    }

    private static int ResidualOf(ViolationReport rep, C1EjectionChainPolish.Origin origin, bool skipC1)
    {
        var c1 = rep.Breakdown.TryGetValue("c1", out var v) ? v : 0;
        return origin == C1EjectionChainPolish.Origin.C1 ? c1 : rep.Total - rep.Hard - (skipC1 ? c1 : 0);
    }

    private static LegOut RunLeg(
        MagiState state, Problem p, int[][] work0, ViolationReport rep0,
        C1EjectionChainPolish.Origin origin, bool skipC1, Config cfg, bool previousImproved,
        long deadlineMs, Func<bool> shouldStop, bool q, PinBlockAttribution pinBlocks,
        long deepCapMs, long deepCapEvaluations, Telemetry tel)
    {
        tel.Before = rep0;
        tel.Residual = ResidualOf(rep0, origin, skipC1);
        LegOut SkipLeg(string why) { tel.Skip = why; tel.EndReason = why; tel.After = rep0; return new LegOut(work0, rep0, 0, 0L, 0L); }
        if (tel.Residual == 0) return SkipLeg("no_residual");
        long TimeLeft() => EngineClock.RemainingMs(deadlineMs);
        if (!cfg.Deterministic && TimeLeft() < cfg.ShallowMillis) return SkipLeg("no_budget");
        Func<bool> stop = cfg.Deterministic ? shouldStop : () => shouldStop() || TimeLeft() <= 0L;
        var swap = cfg.SwapMoves ?? PolishGate.EjectionChainSwapMoves;

        IReadOnlyList<C1EjectionChainPolish.SeedKey> seeds = Array.Empty<C1EjectionChainPolish.SeedKey>();
        C1EjectionChainPolish.Apply(state, work0, new C1EjectionChainPolish.Config(Origin: origin, SkipC1Seeds: skipC1, SwapMoves: swap, MaxMillis: long.MaxValue),
            shouldStop: stop, quantitativeRangeEval: q, indexOnly: s => seeds = s);
        tel.Seeds = seeds.Count;
        var cands = new List<C1EjectionChainPolish.PathCandidate>();
        C1EjectionChainPolish.Stats Probe(IReadOnlyList<C1EjectionChainPolish.SeedKey> list, int depth, long evaluations, long millis)
        {
            var st = new C1EjectionChainPolish.Stats();
            if (list.Count == 0) return st;
            C1EjectionChainPolish.Apply(state, work0, new C1EjectionChainPolish.Config(Origin: origin, SkipC1Seeds: skipC1, SwapMoves: swap, SeedList: list,
                MaxDepth: depth, Branching: new[] { 2, 1 }, MaxEvaluations: evaluations, MaxMillis: millis,
                TimeWithEvaluations: !cfg.Deterministic, MaxRounds: 1), shouldStop: stop, quantitativeRangeEval: q, stats: st,
                collect: c => cands.Add(c));
            return st;
        }

        var t1 = EngineClock.NowMs();
        var shallow = Probe(seeds, 2, cfg.ShallowEvaluations, cfg.ShallowMillis);
        tel.ShallowEvaluations = shallow.Evaluations; tel.ShallowMs = EngineClock.NowMs() - t1; tel.ShallowHits = shallow.HitSeeds.Count;
        var hits = new List<C1EjectionChainPolish.SeedKey>(shallow.HitSeeds);
        var hitSet = new HashSet<C1EjectionChainPolish.SeedKey>(hits);

        var midTime = cfg.Deterministic || TimeLeft() >= cfg.MidMillis;
        if (!ShallowOnly && !stop() && midTime && (tel.Residual >= cfg.MidResidual || (hits.Count == 0 && previousImproved)))
        {
            var t2 = EngineClock.NowMs();
            var mid = Probe(seeds.Where(s => !hitSet.Contains(s)).ToList(), 4, cfg.MidEvaluations, cfg.MidMillis);
            tel.MidRan = true; tel.MidEvaluations = mid.Evaluations; tel.MidMs = EngineClock.NowMs() - t2; tel.MidHits = mid.HitSeeds.Count;
            foreach (var h in mid.HitSeeds) if (hitSet.Add(h)) hits.Add(h);
        }

        long deepMs = 0L, deepEvaluations = 0L;
        if (hits.Count == 0)
        {
            tel.EndReason = stop() ? "stopped" : "probe_miss";
            tel.After = rep0;
            return new LegOut(work0, rep0, 0, 0L, 0L);
        }
        var budgetMs = cfg.Deterministic ? 0L : Math.Min(deepCapMs, TimeLeft() / 4);
        var budgetEvaluations = cfg.Deterministic ? deepCapEvaluations : 0L;
        if (!ShallowOnly && !stop() && (budgetMs > 0L || budgetEvaluations > 0L))
        {
            tel.DeepRan = true; tel.DeepBudgetMs = budgetMs; tel.DeepBudgetEvaluations = budgetEvaluations;
            var beforeCount = cands.Count;
            var st = new C1EjectionChainPolish.Stats();
            var t3 = EngineClock.NowMs();
            C1EjectionChainPolish.Apply(state, work0, new C1EjectionChainPolish.Config(Origin: origin, SkipC1Seeds: skipC1, SwapMoves: swap,
                SeedList: seeds.Where(hitSet.Contains).ToList(), MaxRounds: 1,
                MaxMillis: cfg.Deterministic ? long.MaxValue : budgetMs, MaxEvaluations: budgetEvaluations),
                shouldStop: stop, quantitativeRangeEval: q, stats: st, collect: c => cands.Add(c));
            deepMs = EngineClock.NowMs() - t3; deepEvaluations = st.Evaluations;
            tel.DeepMs = deepMs; tel.DeepEvaluations = deepEvaluations; tel.DeepCandidates = cands.Count - beforeCount;
        }

        // 採用: 正式評価の良い順。いまの盤面で取り直して採用ゲートを通るものだけ。
        var ordered = cands.OrderBy(c => c.Report.Hard).ThenBy(c => c.Report.WeightedScore).ThenBy(c => c.Report.Total).ToList();
        var cur = work0;
        var curRep = rep0;
        var cells = 0;
        foreach (var c in ordered)
        {
            if (shouldStop()) break;
            if (c.Path.Any(m => cur[m[0]][m[1]] != m[2])) continue;
            var next = cur.Select(r => (int[])r.Clone()).ToArray();
            foreach (var m in c.Path) next[m[0]][m[1]] = m[3];
            var r2 = UnifiedViolationChecker.Check(state, next, q);
            if (!V6SearchOperators.AdoptionGate(p, cur, next, r2, curRep, pinBlocks).Accepted) continue;
            cur = next; curRep = r2; cells += c.Path.Count; tel.Committed++;
        }
        tel.After = curRep;
        tel.EndReason = tel.Committed > 0 ? "committed" : stop() ? "stopped" : "deep_done";
        return new LegOut(cur, curRep, cells, deepMs, cfg.Deterministic ? deepEvaluations : 0L);
    }
}
