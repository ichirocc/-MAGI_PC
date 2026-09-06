using System.Numerics;
using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// Faithful port of Kotlin's <c>SmartInitialScheduler</c> object (初期解生成・賢い版).
///
/// Builds an initial draft schedule in the order: ①希望シフト（実現可能のみ）→②C1(窓の要件、ゼロから
/// ビットマスクDPで直接構築)→③日別必要人数(covUCell=need1/need2のORが source of truth)→④個人下限
/// (rangeLo)→⑤残りの空きセルをペナルティ最小で埋める。The generated schedule is a draft only — the
/// caller decides separately whether to continue with full optimization (SA/ALNS).
///
/// [3.261.0 の教訓 — 必ず踏襲する] 常にゼロから（<c>Array(p.S){IntArray(p.T){-1}}</c>）組み立てる。
/// 旧世代の <see cref="GreedyMirrorScheduler"/> が持つ「入力スケジュールの充足率で既存表ベース/空表
/// ベースを切り替える」トグルはここには**意図的に無い**（実機報告のバグ修正：充足済み入力に対する
/// 2回目の生成が完全な no-op になっていた）。この2ファイルの違いは仕様であり、統一しない。
/// </summary>
public static class SmartInitialScheduler
{
    private readonly record struct C1Rule(int Days, int Minimum);

    /// <summary>DP内部状態のレコード（Kotlinの<c>solveConstructionDp</c>ローカル<c>data class Rec</c>
    /// に相当。C#はメソッド内でのローカル型宣言を許さないためクラス直下のprivateネスト型に置く —
    /// スコープの違いのみで意味は同一、この型を使うのは <see cref="SolveConstructionDp"/> だけ）。</summary>
    private readonly record struct Rec(long Cost, long Bits);

    public static ScheduleRunResult Generate(MagiState state, long seed = 0x517A2L)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var p = new Problem(state);
        if (p.T <= 0 || p.S <= 0 || p.K <= 0)
            throw new ArgumentException("期間/職員/シフトが不足しています");
        int restK = ScheduleUtil.RestShiftIndex(state);
        var schedule = new int[p.S][];
        for (int i = 0; i < p.S; i++)
        {
            schedule[i] = new int[p.T];
            for (int j = 0; j < p.T; j++) schedule[i][j] = -1;
        }

        // ① 希望シフト（担当可能な希望のみ直接適用。担当外は据え置き＝後段の診断で案内される）。
        int wishIn = 0, wishOut = 0;
        for (int i = 0; i < p.S; i++)
        {
            for (int j = 0; j < p.T; j++)
            {
                if (schedule[i][j] >= 0) continue;
                int w = p.Wish[i][j];
                if (w < 0 || w >= p.K) continue;
                if (p.CanDo(i, w)) { schedule[i][j] = w; wishIn++; } else wishOut++;
            }
        }

        // ② C1(窓の要件)。対象シフトごとに規則をまとめ、シフトindex順（決定的）に処理する。
        var rulesByShift = new Dictionary<int, List<C1Rule>>();
        foreach (var c in p.Cons1)
        {
            if (c.ShiftIdx < 0 || c.ShiftIdx >= p.K || c.Day1 <= 0 || c.Day2 <= 0) continue;
            if (!rulesByShift.TryGetValue(c.ShiftIdx, out var list))
                rulesByShift[c.ShiftIdx] = list = new List<C1Rule>();
            list.Add(new C1Rule(c.Day1, c.Day2));
        }
        int c1Filled = 0;
        var rng = new JavaRandom(seed);
        foreach (int x in rulesByShift.Keys.OrderBy(k => k))
        {
            var rules = rulesByShift[x];
            for (int i = 0; i < p.S; i++)
            {
                if (!p.MayPlace(i, x)) continue;
                var forced = new int[p.T];
                for (int j = 0; j < p.T; j++)
                    forced[j] = schedule[i][j] == -1 ? -1 : schedule[i][j] == x ? 1 : 0;
                int cap = p.RangeHi[i][x];
                var targetDays = SolveConstructionDp(p.T, rules, forced, rng.NextLong(), cap);
                if (targetDays is null) continue;
                for (int j = 0; j < p.T; j++)
                {
                    if (targetDays[j] && schedule[i][j] < 0) { schedule[i][j] = x; c1Filled++; }
                }
            }
        }

        // ③ 日別必要人数(need1/need2=covUCellのOR)。不足の大きいシフトから、超過/回数の少ない職員を
        //   優先して埋める（GreedyMirrorSchedulerの充足フィルと同一ロジック）。
        var counts = ScheduleUtil.CountMatrix(p, schedule);
        var cov = ScheduleUtil.Coverage(p, schedule);
        for (int j = 0; j < p.T; j++)
        {
            var demandOrder = new List<(int Deficit, int K)>();
            for (int k = 0; k < p.K; k++)
            {
                int deficit = p.CovUCell(k, j, cov[j][k]);
                if (deficit > 0) demandOrder.Add((deficit, k));
            }
            demandOrder.Sort((a, b) =>
            {
                int d = b.Deficit.CompareTo(a.Deficit);
                return d != 0 ? d : a.K.CompareTo(b.K);
            });
            foreach (var (_, k) in demandOrder)
            {
                while (p.CovUCell(k, j, cov[j][k]) > 0)
                {
                    int bestI = -1, bestPenalty = int.MaxValue;
                    for (int i = 0; i < p.S; i++)
                    {
                        if (schedule[i][j] >= 0 || !p.MayPlace(i, k)) continue;
                        int hi = p.RangeHi[i][k];
                        bool over = hi != int.MaxValue && counts[i][k] >= hi;
                        int penalty = (over ? 1000 : 0) + counts[i][k] * 2;
                        if (penalty < bestPenalty) { bestPenalty = penalty; bestI = i; }
                    }
                    if (bestI < 0) break;
                    schedule[bestI][j] = k;
                    counts[bestI][k]++;
                    cov[j][k]++;
                }
            }
        }

        // ④ 個人下限(rangeLo)。
        counts = ScheduleUtil.CountMatrix(p, schedule);
        for (int i = 0; i < p.S; i++)
        {
            var allowed = p.AllowedShiftsForStaff(i);
            var free = new List<int>();
            for (int jj = 0; jj < p.T; jj++) if (schedule[i][jj] < 0) free.Add(jj);
            int pos = 0;
            foreach (int k in allowed)
            {
                int lo = p.RangeLo[i][k] != int.MinValue ? p.RangeLo[i][k] : 0;
                int need = Math.Max(0, lo - counts[i][k]);
                while (need > 0 && pos < free.Count)
                {
                    int j = free[pos++];
                    schedule[i][j] = k;
                    counts[i][k]++;
                    need--;
                }
            }
        }

        // ⑤ 残りの空きセルをペナルティ最小で埋める。
        counts = ScheduleUtil.CountMatrix(p, schedule);
        for (int i = 0; i < p.S; i++)
        {
            var allowed = p.AllowedShiftsForStaff(i);
            for (int j = 0; j < p.T; j++)
            {
                if (schedule[i][j] >= 0) continue;
                int bestK = allowed.Length > 0 ? allowed[0] : restK;
                int bestPenalty = int.MaxValue;
                foreach (int k in allowed)
                {
                    int hi = p.RangeHi[i][k];
                    bool over = hi != int.MaxValue && counts[i][k] >= hi;
                    int covNow = 0;
                    for (int ii = 0; ii < p.S; ii++) if (schedule[ii][j] == k) covNow++;
                    // [need2単独定義セル見落とし修正] 上のstep③と同根。need1のみでなくcovUCell(OR)で判定。
                    int demandBonus = p.CovUCell(k, j, covNow) > 0 ? -100 : 0;
                    // [3.345.0] 休は通常のシフト種の一つ＝残り埋めで優先しない（旧: 休だけ -10 のボーナス）。
                    //   実データ3件で hard/covO/covU/low/high/c1 が全て同一＝この優先は実質不活性だった。
                    int penalty = (over ? 1000 : 0) + counts[i][k] + demandBonus;
                    if (penalty < bestPenalty) { bestPenalty = penalty; bestK = k; }
                }
                schedule[i][j] = bestK;
                counts[i][bestK]++;
            }
        }

        var report = UnifiedViolationChecker.Check(state, schedule);
        long elapsedMs = sw.ElapsedMilliseconds;
        var log = new MirrorLog(
            tag: "SmartInitial",
            message: $"初期解生成: HARD={report.Hard} total={report.Total} " +
                $"希望seed={wishIn}件/担当外={wishOut}件 C1充足セル={c1Filled}件 ({elapsedMs}ms)");
        var logs = new List<MirrorLog> { log };
        logs.AddRange(report.Logs);
        return new ScheduleRunResult(schedule, report with { Logs = logs });
    }

    /// <summary>
    /// [3.262.0/V6SanityPort検査2b-3(個人内壁検知)向け] 個人上限(rangeHi)を無視した場合に、指定の
    /// 窓ルール群(同一シフトの複数規則も可)を**同時に**完全充足するには最低何日必要かを、構築本体
    /// (<see cref="SolveConstructionDp"/>)自身で計算する。無制限cap・全日自由で呼び、0違反を達成する
    /// 解のうち対象日数最小のものを返す（DPの優先順位=違反数最優先→対象日数次点、と一致するため正確）。
    /// 0違反が原理的に不可能（規則の日数が期間を超える等）なら null を返す。
    /// </summary>
    public static int? MinDaysForFullCompliance(int t, IReadOnlyList<(int Days, int Minimum)> rules, long seed = 0x517A2L)
    {
        var c1Rules = rules.Select(r => new C1Rule(r.Days, r.Minimum)).ToList();
        var forced = new int[t];
        for (int i = 0; i < t; i++) forced[i] = -1;
        var targetDays = SolveConstructionDp(t, c1Rules, forced, seed, t);
        if (targetDays is null) return null;
        foreach (var rule in c1Rules)
        {
            for (int j0 = 0; j0 <= t - rule.Days; j0++)
            {
                int cnt = 0;
                for (int j = j0; j < j0 + rule.Days; j++) if (targetDays[j]) cnt++;
                if (cnt < rule.Minimum) return null;
            }
        }
        return targetDays.Count(v => v);
    }

    /// <summary>
    /// 単一シフトxのC1規則群を満たす「対象日か否か」の月内配置を、ゼロからビットマスクDPで直接求める
    /// （<c>C1TemporalDp</c>と異なり回数保存・移設数上限は課さない＝構築専用）。
    /// </summary>
    /// <param name="forced">forced[day]: 1=希望等で既にx確定・0=希望等で既に他シフト確定(選べない)・-1=自由。</param>
    /// <param name="maxCount">対象日数の上限（staffRangeの個人上限=rangeHi。未設定はint.MaxValue）。
    /// high違反(重み45)はc1(重み30)より重いため、C1充足のためだけに個人上限を超えて割り当てない。
    /// forced済み(希望由来)の対象日もこの上限に含める＝希望だけで既に上限超過なら
    /// (=既存の別問題)これ以上は増やさず null で安全側に諦める。</param>
    /// <returns>目的= まず違反窓数を最小化、次に対象日数を最小化（他制約(③④⑤)への自由度を残す）、
    /// 最後にseed由来の決定的tie-breakで一意に選ぶ。</returns>
    private static bool[]? SolveConstructionDp(
        int t, IReadOnlyList<C1Rule> rules, int[] forced, long seed, int maxCount = int.MaxValue)
    {
        if (t <= 0 || t > 62 || forced.Length != t) return null;
        var validRules = rules.Where(r => r.Days >= 1 && r.Days <= t && r.Minimum > 0).ToList();
        if (validRules.Count == 0) return null;
        int maxWindow = validRules.Max(r => r.Days);
        if (maxWindow >= 62) return null;
        int keepBits = Math.Max(maxWindow - 1, 0);
        long keepMask = keepBits == 0 ? 0L : (1L << keepBits) - 1L;
        // t以上のcapは実質無制限（対象日はt日を超えられない）＝既存の無制限挙動と完全に同値。
        int capBound = maxCount >= t ? t : Math.Max(maxCount, 0);

        long Tie(int day)
        {
            long z = seed ^ ((long)day * -0x61c8864680b583ebL);
            z ^= z >>> 33; z *= -0x00ae502812aa7333L; z ^= z >>> 29;
            return z & 511L;
        }

        // 状態キー=(直近maxWindow-1日分のビット列, 累積対象日数)。累積数がcapBoundを超える遷移は
        // 生成しない＝個人上限を構造的に超過できない。
        var dp = new Dictionary<(long Mask, int Cnt), Rec> { [(0L, 0)] = new Rec(0L, 0L) };
        for (int day = 0; day < t; day++)
        {
            var next = new Dictionary<(long Mask, int Cnt), Rec>(Math.Max(16, dp.Count * 2));
            int[] choices = forced[day] switch
            {
                1 => new[] { 1 },
                0 => new[] { 0 },
                _ => new[] { 0, 1 },
            };
            foreach (var ((mask, cnt), rec) in dp)
            {
                foreach (int bit in choices)
                {
                    int newCnt = cnt + bit;
                    if (newCnt > capBound) continue;
                    long full = (mask << 1) | (long)bit;
                    int fireInc = 0;
                    foreach (var rule in validRules)
                    {
                        if (day + 1 < rule.Days) continue;
                        long rm = (1L << rule.Days) - 1L;
                        if (BitOperations.PopCount((ulong)(full & rm)) < rule.Minimum) fireInc++;
                    }
                    long cost = rec.Cost + (long)fireInc * 1_000_000L + (long)bit * 1_000L + Tie(day);
                    long newMask = full & keepMask;
                    long bits = bit == 1 ? rec.Bits | (1L << day) : rec.Bits;
                    var nk = (newMask, newCnt);
                    if (!next.TryGetValue(nk, out var old) || cost < old.Cost ||
                        (cost == old.Cost && (ulong)bits < (ulong)old.Bits))
                    {
                        next[nk] = new Rec(cost, bits);
                    }
                }
            }
            if (next.Count == 0) return null;
            dp = next;
        }

        Rec? best = null;
        foreach (var rec in dp.Values)
        {
            if (best is null || rec.Cost < best.Value.Cost ||
                (rec.Cost == best.Value.Cost && (ulong)rec.Bits < (ulong)best.Value.Bits))
            {
                best = rec;
            }
        }
        if (best is null) return null;
        var chosen = best.Value;
        var result = new bool[t];
        for (int day = 0; day < t; day++) result[day] = ((chosen.Bits >>> day) & 1L) != 0L;
        return result;
    }
}
