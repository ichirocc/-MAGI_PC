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

    /// <summary>[3.596.0, Kotlin原本] SolveConstructionDpの状態数上限（ユーザー指示の数値。実測の根拠は
    /// docs/history）。時間でなく状態数で切るのは、同じ入力なら必ず同じ初期解になる決定性を保つため。</summary>
    private const int MaxDpStates = 200_000;

    /// <summary>③〜⑤で、置くとシフトの間隔（gap）の不足が 1 日増えるごとに足す罰（Kotlin <c>GAP_PENALTY</c>、Android 3.727.110）。</summary>
    private const int GapPenalty = 10;

    /// <summary>(i,j) に k を置いたときに増える gap の不足日数（空きセル -1 は k でない扱い）。</summary>
    private static int GapIncrease(Problem p, int[] row, int i, int j, int k)
    {
        var inc = 0;
        foreach (var c in p.ConsGap)
        {
            if (c.ShiftIdx != k || !p.C1Applies(i, k)) continue;
            var before = GapRule.Deficit(row, k, c.Days);
            var old = row[j]; row[j] = k;
            inc += GapRule.Deficit(row, k, c.Days) - before;
            row[j] = old;
        }
        return inc;
    }

    /// <summary>DP内部状態のレコード（Kotlinの<c>solveConstructionDp</c>ローカル<c>data class Rec</c>
    /// に相当。C#はメソッド内でのローカル型宣言を許さないためクラス直下のprivateネスト型に置く —
    /// スコープの違いのみで意味は同一、この型を使うのは <see cref="SolveConstructionDp"/> だけ）。</summary>
    private readonly record struct Rec(long Cost, long Bits);

    public static ScheduleRunResult Generate(MagiState state, bool quantitativeRangeEval = false)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var p = new Problem(state, quantitativeRangeEval);
        if (p.T <= 0 || p.S <= 0 || p.K <= 0)
            throw new ArgumentException("期間/職員/シフトが不足しています");
        // [backlog#24] 休シフト未設定はここも入口＝ブロックする。
        int restK = ScheduleUtil.RestShiftIndex(state) ?? throw new ArgumentException("休みシフトが設定されていません");
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
                if (p.Pinned(i, j)) { schedule[i][j] = p.Pin[i][j]; continue; }   // [#41] 手動固定が先（希望より強い）。以降の段は空きセルだけ埋める
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
        int c1Skipped = 0;
        foreach (int x in rulesByShift.Keys.OrderBy(k => k))
        {
            var rules = rulesByShift[x];
            for (int i = 0; i < p.S; i++)
            {
                if (!p.MayPlace(i, x)) continue;
                var forced = new int[p.T];
                // 拡張希望で x が禁止の空き日は選べない日（0）として渡す
                for (int j = 0; j < p.T; j++)
                    forced[j] = schedule[i][j] == -1 ? (p.ExtBanned(i, j, x) ? 0 : -1) : schedule[i][j] == x ? 1 : 0;
                int cap = p.RangeHi[i][x];
                var targetDays = SolveConstructionDp(p.T, rules, forced, cap);
                if (targetDays is null) { c1Skipped++; continue; }
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
                        if (schedule[i][j] >= 0 || !p.MayPlace(i, k) || p.ExtBanned(i, j, k)) continue;
                        int hi = p.RangeHi[i][k];
                        bool over = hi != int.MaxValue && counts[i][k] >= hi;
                        // 間隔の不足は同点のときだけの決め手（人員の穴埋めの選び方を変えると後の日の必須が増える）
                        int penalty = ((over ? 1000 : 0) + counts[i][k] * 2) * 100 + GapIncrease(p, schedule[i], i, j, k) +
                            (p.MakesForbiddenRun(schedule, i, j, k) ? 50 : 0);
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
                    // 拡張希望の禁止の日は飛ばす（禁止が無ければ q == pos で従来どおり）
                    int q = pos;
                    while (q < free.Count && p.ExtBanned(i, free[q], k)) q++;
                    if (q >= free.Count) break;
                    // 間隔の不足が増えない日（禁止の並びも作らない日）があればそちらを先に
                    if (p.ConsGap.Count > 0 && GapIncrease(p, schedule[i], i, free[q], k) > 0)
                    {
                        int r = q + 1;
                        while (r < free.Count && (p.ExtBanned(i, free[r], k) || GapIncrease(p, schedule[i], i, free[r], k) > 0 ||
                                p.MakesForbiddenRun(schedule, i, free[r], k))) r++;
                        if (r < free.Count) q = r;
                    }
                    if (q != pos) { int tq = free[q]; free[q] = free[pos]; free[pos] = tq; }
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
                int bestK = restK;
                int bestPenalty = int.MaxValue;
                foreach (int k in allowed)
                {
                    if (p.ExtBanned(i, j, k)) continue;   // 拡張希望の禁止へは置かない（全部禁止なら FallbackShift）
                    int hi = p.RangeHi[i][k];
                    bool over = hi != int.MaxValue && counts[i][k] >= hi;
                    int covNow = 0;
                    for (int ii = 0; ii < p.S; ii++) if (schedule[ii][j] == k) covNow++;
                    // [need2単独定義セル見落とし修正] 上のstep③と同根。need1のみでなくcovUCell(OR)で判定。
                    int demandBonus = p.CovUCell(k, j, covNow) > 0 ? -100 : 0;
                    // [3.345.0] 休は通常のシフト種の一つ＝残り埋めで優先しない（旧: 休だけ -10 のボーナス）。
                    //   実データ3件で hard/covO/covU/low/high/c1 が全て同一＝この優先は実質不活性だった。
                    // 間隔の不足を避けた先が禁止の並びにならないよう、禁止の並びを作る候補は間隔より重く避ける
                    int penalty = (over ? 1000 : 0) + counts[i][k] + demandBonus + GapPenalty * GapIncrease(p, schedule[i], i, j, k) +
                        (p.MakesForbiddenRun(schedule, i, j, k) ? 500 : 0);
                    if (penalty < bestPenalty) { bestPenalty = penalty; bestK = k; }
                }
                if (bestPenalty == int.MaxValue) bestK = FallbackShift(p, i, j, restK);
                schedule[i][j] = bestK;
                counts[i][bestK]++;
            }
        }

        var report = UnifiedViolationChecker.Check(state, schedule, quantitativeRangeEval);
        long elapsedMs = sw.ElapsedMilliseconds;
        var log = new MirrorLog(
            tag: "SmartInitial",
            message: $"初期解生成: HARD={report.Hard} total={report.Total} " +
                $"希望seed={wishIn}件/担当外={wishOut}件 C1充足セル={c1Filled}件" +
                (c1Skipped > 0 ? $" C1配置を省いた職員×シフト={c1Skipped}件（状態数上限・希望で上限超過など）" : "") + $" ({elapsedMs}ms)");
        var logs = new List<MirrorLog> { log };
        logs.AddRange(report.Logs);
        return new ScheduleRunResult(schedule, report with { Logs = logs });
    }

    /// <summary>
    /// [3.262.0/V6SanityPort検査2b-3(個人内壁検知)向け] 個人上限(rangeHi)を無視した場合に、指定の
    /// 窓ルール群(同一シフトの複数規則も可)を**同時に**完全充足するには最低何日必要かを求める。
    /// 窓を右端の昇順に見て、不足している窓へ**右端に近い空き日から**足す貪欲。窓制約の係数行列は
    /// consecutive-ones＝全単模なので、この貪欲が厳密な最小値を与える（交換論法: 右端へ置く方が
    /// 以降の窓を必ず同等以上に覆う）。0違反が原理的に不可能（窓長より多い回数を要求する等）なら null。
    /// 3.596.0 で指数的なDPから置換（経緯と実測は docs/history、厳密性は総当たりオラクル照合で担保）。
    /// </summary>
    public static int? MinDaysForFullCompliance(int t, IReadOnlyList<(int Days, int Minimum)> rules)
    {
        // 旧DPのビットマスク由来の上限を踏襲＝対象外の長期間で診断の挙動を変えない（業務上限は31日）。
        if (t <= 0 || t > 62) return null;
        var valid = rules.Where(r => r.Days is >= 1 && r.Days <= t && r.Minimum > 0).ToList();
        if (valid.Count == 0) return null;
        var free = new int[t];
        Array.Fill(free, -1);
        return MinDaysWithFixed(t, valid, free);
    }

    /// <summary>
    /// <see cref="MinDaysForFullCompliance"/> に日ごとの固定を足したもの（Android 3.727.128）。forced: 1＝すでに x（回数に数える）・0＝x にできない・-1＝自由。
    /// 同じ貪欲で厳密（総当たり照合）。固定の日のせいで満たせない窓があれば null。
    /// </summary>
    public static int? MinDaysWithFixed(int t, IReadOnlyList<(int Days, int Minimum)> rules, int[] forced)
    {
        var chosen = new bool[t];
        var used = 0;
        for (var j = 0; j < t; j++) if (forced[j] == 1) { chosen[j] = true; used++; }
        for (var end = 0; end < t; end++)
        {
            foreach (var (days, minimum) in rules)
            {
                if (days < 1 || days > t || minimum <= 0 || end + 1 < days) continue;
                var from = end - days + 1;
                var cnt = 0;
                for (var j = from; j <= end; j++) if (chosen[j]) cnt++;
                var j2 = end;
                while (cnt < minimum)
                {
                    while (j2 >= from && (chosen[j2] || forced[j2] == 0)) j2--;
                    if (j2 < from) return null;
                    chosen[j2] = true;
                    used++;
                    cnt++;
                }
            }
        }
        return used;
    }

    /// <summary>
    /// 単一シフトxのC1規則群を満たす「対象日か否か」の月内配置を、ゼロからビットマスクDPで直接求める
    /// （<c>C1TemporalDp</c>と異なり回数保存・移設数上限は課さない＝構築専用）。
    /// </summary>
    /// <param name="forced">forced[day]: 1=希望等で既にx確定・0=希望等で既に他シフト確定(選べない)・-1=自由。</param>
    /// <param name="maxCount">対象日数の上限（staffRangeの個人上限=rangeHi。未設定はint.MaxValue）。
    /// 初期構築では個人上限(rangeHi)を厳密に守る設計方針＝C1充足のためだけに超えて割り当てない
    /// （[2026-09-10] high の重みは25でc1(30)より軽いが、初期構築はあくまで出発点であり、本採否は
    /// 後段の isBetter/keep-best が担うため、この保守的な方針自体は変更しない）。
    /// forced済み(希望由来)の対象日もこの上限に含める＝希望だけで既に上限超過なら
    /// (=既存の別問題)これ以上は増やさず null で安全側に諦める。</param>
    /// <returns>目的= まず窓ごとの不足量の和（c1 の件数と同じ）を最小化、次に対象日数を最小化（他制約(③④⑤)への自由度を残す）、
    /// 同点は対象日のビット列の小さい方で一意に選ぶ（決定的）。</returns>
    private static bool[]? SolveConstructionDp(
        int t, IReadOnlyList<C1Rule> rules, int[] forced, int maxCount = int.MaxValue)
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
                        fireInc += Math.Max(0, rule.Minimum - BitOperations.PopCount((ulong)(full & rm)));
                    }
                    long cost = rec.Cost + (long)fireInc * 1_000_000L + (long)bit * 1_000L;
                    long newMask = full & keepMask;
                    long bits = bit == 1 ? rec.Bits | (1L << day) : rec.Bits;
                    var nk = (newMask, newCnt);
                    if (!next.TryGetValue(nk, out var old) || cost < old.Cost ||
                        (cost == old.Cost && (ulong)bits < (ulong)old.Bits))
                    {
                        next[nk] = new Rec(cost, bits);
                        if (next.Count > MaxDpStates) return null;
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

    /// <summary>
    /// 置けるシフトがどれも禁止（拡張希望・日の上限 0）か、置けるシフトが無いマスに置くシフト（Kotlin <c>fallbackShift</c>、Android 3.727.119）。
    /// 空欄にはできないので違反のいちばん軽いものを選ぶ: 担当できて拡張希望にも日の上限 0 にも当たらない → 日の上限 0 だけに当たる →
    /// 置けるシフトのうち拡張希望に当たるもの → 担当できるシフト → 休。
    /// </summary>
    internal static int FallbackShift(Problem p, int i, int j, int restK)
    {
        var can = Enumerable.Range(0, p.K).Where(k => p.CanDo(i, k)).ToArray();
        // 拡張希望に当たらないもののうち、個人上限 0（上限超過）と日の上限 0（人員過剰）は採点の重みの軽いほうを先に（Android 3.727.125）
        double Cost(int k) => (!p.MayPlace(i, k) ? ObjectiveWeights.Of("high") : 0.0) + (p.DayCapBanned(i, j, k) ? ObjectiveWeights.Of("covO") : 0.0);
        int? pick = null; var pickCost = double.MaxValue;
        foreach (var k in can)
        {
            if (p.ExtWishBanned(i, j, k)) continue;
            var c = Cost(k);
            if (c < pickCost) { pickCost = c; pick = k; }
        }
        if (pick is { } pk) return pk;
        var allowed = p.AllowedShiftsForStaff(i);
        if (allowed.Length > 0) return allowed[0];
        return can.Length > 0 ? can[0] : restK;
    }
}
