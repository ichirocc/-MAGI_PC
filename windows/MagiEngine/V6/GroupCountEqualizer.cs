using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// グループ内の回数を、採点の fair と同じ物差しでそろえる（Kotlin <c>GroupCountEqualizer</c>、Android 3.727.115・3.727.125。希望の反映と同じく利用者が押して上書きする操作）。
/// 手は同じ日の 2 人のシフトの入れ替え＝日ごとのシフト別人数は変わらない。1 組ずつの段は同じグループの 2 人だけ、焼きなましは別のグループの人とも入れ替える。
/// ずれは採点の fair と同じ値（<see cref="ScheduleUtil.FairDevOfBucket"/>）。最も減る入れ替えから当てる。希望・手動固定のマス、置けないシフト、
/// 個人の範囲から外れる手、必須違反が増える手は当てない。1 組ずつ当てて止まったら、月全体（2 人・3 人・前後 2 日）の入れ替えを焼きなましで探す
/// （<see cref="Anneal"/>、Android 3.727.118）。回数を固定して組み直す（CP-SAT、Android 3.727.124）は C# に CP-SAT が無いので移植しない。
/// </summary>
public static class GroupCountEqualizer
{
    public sealed record Result(int[][] Schedule, int Swaps, long SpreadBefore, long SpreadAfter);

    private const int AnnealIters = 400_000;
    private const int AnnealRuns = 3;

    /// <summary>
    /// 1 組ずつの入れ替えでは範囲や禁止の並びにかかって進めないとき用の焼きなまし（月全体、同じグループの 2 人の入れ替え・3 人の回し・前後 2 日をまとめた入れ替え）。
    /// 途中は個人の範囲を外れても必須が増えてもよい（罰として数える）が、採るのは「各 (職員,シフト) の範囲からの外れ量が元以下・
    /// 必須が元以下」の盤面のうち、目標からのずれがいちばん小さいものだけ。よりそろった盤面が見つかれば <paramref name="work"/> を書き換えて true。
    /// </summary>
    private static bool Anneal(Problem p, int[][] work, int[][] cnt0, int[]?[][] elig, int iters, long seed, bool fastCool)
    {
        // 手は 1 手ごとに無作為に作る（全部を先に並べると 30 人・31 日で数十万件になる）: 2 人の同じ日の入れ替え・3 人の回し・
        // 同じ 2 人の前後 2 日をまとめた入れ替えを等しい割合で。1 人目はグループから、相手は半分は同じグループ・半分は誰でも
        // （同じグループの中だけでは禁止の並びや希望で動けない回数を、別のグループの人との入れ替えで動かす、Android 3.727.122）。
        // 希望・手動固定のマスに当たる手は捨てる。日ごとの人数は変わらない。
        var groups = p.GroupMembers.Where(m => m.Length >= 2).ToArray();
        if (groups.Length == 0 || p.T == 0) return false;
        var pairW = groups.Select(m => m.Length * (m.Length - 1) / 2).ToArray();
        var pairTotal = pairW.Sum();
        var cnt = cnt0.Select(r => (int[])r.Clone()).ToArray();
        int Lo(int i, int k) => p.RangeLo[i][k] == int.MinValue ? 0 : p.RangeLo[i][k];
        int Out(int i, int k, int n) => Math.Max(0, Lo(i, k) - n) + (p.RangeHi[i][k] == int.MaxValue ? 0 : Math.Max(0, n - p.RangeHi[i][k]));
        var out0 = Enumerable.Range(0, p.S).Select(i => Enumerable.Range(0, p.K).Select(k => Out(i, k, cnt[i][k])).ToArray()).ToArray();
        var groupOf = new int[p.S]; Array.Fill(groupOf, -1);
        for (var g = 0; g < p.GroupMembers.Length; g++) foreach (var i in p.GroupMembers[g]) groupOf[i] = g;
        int Excess(int i, int k, int n) => Math.Max(0, Out(i, k, n) - out0[i][k]);   // 範囲の外れ量のうち元を超えた分
        var devCur = 0.0;
        for (var g = 0; g < elig.Length; g++) for (var k = 0; k < p.K; k++) if (elig[g][k] is not null) devCur += GroupDev(p, g, k, cnt);
        int[] tg = new int[24], tk = new int[24];   // この手で変わる (グループ,シフト)
        var exCur = 0;
        var hardCur = 0;   // 元からの必須の増減
        var cur = work.Copy2D();
        var bits = C3nBitScan.Usable(p);   // 64 日以内＝禁止の並びをビットで数える（30 人・31 日が業務の上限）
        var masks = bits ? Enumerable.Range(0, p.S).Select(i => C3nBitScan.BuildRowMask(p, cur[i])).ToArray() : Array.Empty<long[]>();
        var old2 = new[] { new int[2], new int[2] }; var old3 = new[] { new int[3], new int[3] };   // 日ごとの元の値（2 人・3 人）
        var locked = new bool[p.S * p.T];   // 希望・手動固定（毎手引くと重い）
        for (var c = 0; c < locked.Length; c++) locked[c] = p.WishLocked(c / p.T, c % p.T);
        int[] who2 = new int[2], who3 = new int[3], day1 = new int[1], day2 = new int[2];
        var bestDev = devCur;
        int[][]? best = null;
        var rnd = new JavaRandom(seed);
        const double t0 = 1.0, t1 = 0.02, wEx = 2.0, wHard = 20.0;
        var cool = Math.Pow(t1 / t0, 1.0 / iters); var temp = t0;
        int[] ci2 = new int[12], ck = new int[12], cd = new int[12];   // この手での (職員,シフト) ごとの回数の増減
        for (var step = 0; step < iters; step++)
        {
            temp *= cool;
            var gi = rnd.NextInt(pairTotal); var g = 0;
            while (gi >= pairW[g]) { gi -= pairW[g]; g++; }
            var m = groups[g];
            var kind = rnd.NextInt(3);
            var n = kind == 1 && m.Length >= 3 ? 3 : 2;
            var who = n == 2 ? who2 : who3;
            who[0] = m[rnd.NextInt(m.Length)];
            var wide = rnd.NextBoolean();   // 相手を誰からでも選ぶ
            int Pick() => wide ? rnd.NextInt(p.S) : m[rnd.NextInt(m.Length)];
            do { who[1] = Pick(); } while (who[1] == who[0]);
            if (n == 3) do { who[2] = Pick(); } while (who[2] == who[0] || who[2] == who[1]);
            var d1 = rnd.NextInt(p.T);
            int[] days;
            if (kind == 2 && p.T >= 2)
            {
                int d2;
                do { d2 = rnd.NextInt(p.T); } while (d2 == d1);
                day2[0] = d1; day2[1] = d2; days = day2;
            }
            else { day1[0] = d1; days = day1; }
            var lockHit = false;
            foreach (var d in days) for (var t = 0; t < n; t++) if (locked[who[t] * p.T + d]) lockHit = true;
            if (lockHit) continue;
            bool ok = true, moved = false; var nc = 0;
            foreach (var d in days)
            {
                for (var t = 0; t < n; t++)
                {
                    int from = cur[who[t]][d], to = cur[who[(t + 1) % n]][d];
                    if (from < 0 || from >= p.K || to < 0 || to >= p.K) { ok = false; break; }
                    if (from == to) continue;
                    if (!p.MayPlaceAt(who[t], d, to)) { ok = false; break; }
                    for (var q = 0; q < 2; q++)
                    {
                        int i = who[t], k = q == 0 ? from : to, dd = q == 0 ? -1 : 1;
                        var e = 0;
                        while (e < nc && !(ci2[e] == i && ck[e] == k)) e++;
                        if (e == nc) { ci2[nc] = i; ck[nc] = k; cd[nc] = 0; nc++; }
                        cd[e] += dd;
                    }
                    moved = true;
                }
                if (!ok) break;
            }
            if (!ok || !moved) continue;
            var dDev = 0.0; var dEx = 0;
            for (var e = 0; e < nc; e++)
            {
                int i = ci2[e], k = ck[e], dd = cd[e];
                if (dd == 0) continue;
                dEx += Excess(i, k, cnt[i][k] + dd) - Excess(i, k, cnt[i][k]);
            }
            var nt = 0;
            for (var e = 0; e < nc; e++)
            {
                int gg = groupOf[ci2[e]], k = ck[e];
                if (cd[e] == 0 || gg < 0 || elig[gg][k] is null) continue;
                var seen = false;
                for (var q = 0; q < nt; q++) if (tg[q] == gg && tk[q] == k) seen = true;
                if (!seen) { tg[nt] = gg; tk[nt] = k; nt++; }
            }
            if (nt > 0)
            {
                for (var q = 0; q < nt; q++) dDev -= GroupDev(p, tg[q], tk[q], cnt);
                for (var e = 0; e < nc; e++) cnt[ci2[e]][ck[e]] += cd[e];
                for (var q = 0; q < nt; q++) dDev += GroupDev(p, tg[q], tk[q], cnt);
                for (var e = 0; e < nc; e++) cnt[ci2[e]][ck[e]] -= cd[e];
            }
            // その場で当てて、必須の差は変わった日だけ数える（同じ日の並べ替え＝日の人数は不変。2 日は 1 日ずつ当てて足す）
            var dHard = 0;
            for (var di = 0; di < days.Length; di++)
            {
                var d = days[di]; var o = (n == 2 ? old2 : old3)[di];
                for (var t = 0; t < n; t++) o[t] = cur[who[t]][d];
                for (var t = 0; t < n; t++)
                {
                    cur[who[t]][d] = o[(t + 1) % n];
                    if (bits) C3nBitScan.Move(p, masks[who[t]], d, o[t], o[(t + 1) % n]);
                }
                dHard += bits ? HardDelta.SameDayPermutationDeltaBits(p, cur, masks, d, who, o) : HardDelta.SameDayPermutationDelta(p, cur, d, who, o);
            }
            var df = dDev + wEx * dEx + wHard * dHard;
            if (df <= 0 || rnd.NextDouble() < Math.Exp(-df / temp))
            {
                for (var e = 0; e < nc; e++) cnt[ci2[e]][ck[e]] += cd[e];
                devCur += dDev; exCur += dEx; hardCur += dHard;
                if (exCur == 0 && hardCur <= 0 && devCur < bestDev - 1e-9)
                {
                    bestDev = devCur;
                    best ??= Enumerable.Range(0, p.S).Select(_ => new int[p.T]).ToArray();
                    for (var i = 0; i < p.S; i++) Array.Copy(cur[i], best[i], p.T);
                }
            }
            else
                for (var di = days.Length - 1; di >= 0; di--)
                {
                    var d = days[di]; var o = (n == 2 ? old2 : old3)[di];
                    for (var t = 0; t < n; t++)
                    {
                        if (bits) C3nBitScan.Move(p, masks[who[t]], d, cur[who[t]][d], o[t]);
                        cur[who[t]][d] = o[t];
                    }
                }
            if (fastCool) temp *= cool;   // 速く冷やす＝有効な手で 2 回（Android 3.727.125）
        }
        if (best is null) return false;
        for (var i = 0; i < p.S; i++) Array.Copy(best[i], work[i], p.T);
        return true;
    }

    private static int[][] Counts(Problem p, int[][] s) =>
        Enumerable.Range(0, p.S).Select(i => { var c = new int[p.K]; foreach (var k in s[i]) if (k >= 0 && k < p.K) c[k]++; return c; }).ToArray();

    /// <summary>グループ×シフトごとの対象（置ける人・2 人以上）。シフトを選ばない・置ける人が 1 人以下なら null（Kotlin <c>eligible</c>）。</summary>
    private static int[]?[][] Eligible(Problem p, IReadOnlySet<int>? shifts) =>
        Enumerable.Range(0, p.GroupMembers.Length).Select(g =>
        {
            var m = p.GroupMembers[g];
            return Enumerable.Range(0, p.K).Select(k =>
            {
                if (m.Length < 2 || (shifts is not null && !shifts.Contains(k)) || !p.Bucket[g].Contains(k)) return null;
                var el = m.Where(i => p.MayPlace(i, k)).ToArray();
                return el.Length >= 2 ? el : null;
            }).ToArray();
        }).ToArray();

    /// <summary>グループ g・シフト k のずれ＝採点の fair と同じ値（Kotlin <c>groupDev</c>、Android 3.727.125）。</summary>
    private static double GroupDev(Problem p, int g, int k, int[][] cnt) =>
        p.FairDevOfBucket(g, k, x => cnt[x][k], members: false).Total;

    private static long TotalDev(Problem p, int[]?[][] elig, int[][] cnt)
    {
        var d = 0.0;
        for (var g = 0; g < elig.Length; g++) for (var k = 0; k < p.K; k++) if (elig[g][k] is not null) d += GroupDev(p, g, k, cnt);
        return (long)Math.Round(d, MidpointRounding.AwayFromZero);
    }

    /// <param name="shifts">そろえるシフト（null＝全部、Android 3.727.116）。ほかのシフトの回数は入れ替えで動き得るが、個人の下限・上限は守る。</param>
    public static Result Apply(MagiState state, int[][] schedule, IReadOnlySet<int>? shifts = null, int maxSwaps = 2_000,
        int annealIters = AnnealIters, long seed = 1L)
    {
        var p = new Problem(state);
        var work = ScheduleUtil.NormalizeSchedule(schedule, p);
        var cnt = Counts(p, work);
        var elig = Eligible(p, shifts);
        var before = TotalDev(p, elig, cnt);
        if (elig.All(row => row.All(e => e is null))) return new Result(work, 0, before, before);   // そろえる (グループ,シフト) が無い
        bool Movable(int i, int j) => !p.WishLocked(i, j);   // 希望固定と手動固定
        bool RangeOk(int i, int k, int d)
        {
            var lo = p.RangeLo[i][k] == int.MinValue ? 0 : p.RangeLo[i][k];
            var hi = p.RangeHi[i][k];
            int Out(int n) => Math.Max(0, lo - n) + (hi == int.MaxValue ? 0 : Math.Max(0, n - hi));
            var n0 = cnt[i][k];
            return Out(n0 + d) <= Out(n0);
        }
        var groupOf = new int[p.S]; Array.Fill(groupOf, -1);
        for (var g = 0; g < p.GroupMembers.Length; g++) foreach (var i in p.GroupMembers[g]) groupOf[i] = g;
        var devNow = Enumerable.Range(0, elig.Length).Select(_ => new double[p.K]).ToArray();   // 1 巡ごとのグループ×シフトのずれ（入れ替え前）
        // a・b（同じグループ）の同じ日の入れ替え x↔y で、そのグループの x・y のずれ（fair）がどれだけ変わるか（負＝そろう）
        double SwapGain(int a, int b, int x, int y)
        {
            var g = groupOf[a];
            if (g < 0) return 0.0;
            var ks = new[] { x, y }.Where(k => elig[g][k] is not null).ToArray();
            if (ks.Length == 0) return 0.0;
            var bef = 0.0; foreach (var k in ks) bef += devNow[g][k];
            cnt[a][x]--; cnt[a][y]++; cnt[b][x]++; cnt[b][y]--;
            var aft = 0.0; foreach (var k in ks) aft += GroupDev(p, g, k, cnt);
            cnt[a][x]++; cnt[a][y]--; cnt[b][x]--; cnt[b][y]++;
            return aft - bef;
        }
        var swaps = 0;
        while (swaps < maxSwaps)
        {
            for (var g = 0; g < elig.Length; g++) for (var k = 0; k < p.K; k++) devNow[g][k] = elig[g][k] is not null ? GroupDev(p, g, k, cnt) : 0.0;
            var cands = new List<(double Gain, int A, int B, int D)>();
            foreach (var m in p.GroupMembers)
            {
                if (m.Length < 2) continue;
                for (var ai = 0; ai < m.Length; ai++) for (var bi = ai + 1; bi < m.Length; bi++)
                {
                    int a = m[ai], b = m[bi];
                    for (var d = 0; d < p.T; d++)
                    {
                        int x = work[a][d], y = work[b][d];
                        if (x == y || x < 0 || x >= p.K || y < 0 || y >= p.K || !Movable(a, d) || !Movable(b, d)) continue;
                        if (!p.MayPlaceAt(a, d, y) || !p.MayPlaceAt(b, d, x)) continue;
                        var gain = SwapGain(a, b, x, y);
                        if (gain > -1e-9) continue;
                        if (!RangeOk(a, x, -1) || !RangeOk(a, y, 1) || !RangeOk(b, x, 1) || !RangeOk(b, y, -1)) continue;
                        cands.Add((gain, a, b, d));
                    }
                }
            }
            if (cands.Count == 0) break;
            cands = cands.OrderBy(c => c.Gain).ThenBy(c => c.A).ThenBy(c => c.B).ThenBy(c => c.D).ToList();
            var done = false;
            foreach (var c in cands)
            {
                int x = work[c.A][c.D], y = work[c.B][c.D];
                var prev = work.Copy2D();
                work[c.A][c.D] = y; work[c.B][c.D] = x;
                if (HardDelta.Delta(p, prev, work) > 0) { work[c.A][c.D] = x; work[c.B][c.D] = y; continue; }
                cnt[c.A][x]--; cnt[c.A][y]++; cnt[c.B][x]++; cnt[c.B][y]--;
                swaps++; done = true;
                break;
            }
            if (!done) break;
        }
        // 焼きなましは乱数で行き着く先が揺れるので、種を変えて AnnealRuns 回走らせ、いちばんそろった盤面を採る。冷やし方は 2 通り
        //   （速く冷やす＝有効な手で 2 回、ゆっくり＝1 手 1 回）を両方走らせる。速いほうを先に走らせ同点は先を残す（Android 3.727.125）。
        int[][]? bestRun = null; var bestRunDev = TotalDev(p, elig, cnt);
        if (annealIters > 0)
            for (var run = 0; run < AnnealRuns; run++)
            foreach (var fastCool in new[] { true, false })
            {
                var w = work.Copy2D();
                if (!Anneal(p, w, cnt, elig, annealIters, seed + run, fastCool)) continue;
                var dv = TotalDev(p, elig, Counts(p, w));
                if (dv < bestRunDev) { bestRunDev = dv; bestRun = w; }
            }
        if (bestRun is not null)
        {
            var changed = 0;
            for (var i = 0; i < p.S; i++) for (var j = 0; j < p.T; j++) if (bestRun[i][j] != work[i][j]) changed++;
            swaps += (changed + 1) / 2;   // 変わったマスの数から数えた入れ替えの組数（3 人の回しは 2 組と数える）
            for (var i = 0; i < p.S; i++) Array.Copy(bestRun[i], work[i], p.T);
            cnt = Counts(p, work);
        }
        return new Result(work, swaps, before, TotalDev(p, elig, cnt));
    }
}
