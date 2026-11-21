using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// グループ内の回数を、採点に関係なくそろえる（Kotlin <c>GroupCountEqualizer</c>、Android 3.727.115。希望の反映と同じく利用者が押して上書きする操作）。
/// 手は同じ日・同じ勤務グループの 2 人のシフトの入れ替えだけ。各人の目標（置ける人の平均を個人の下限・上限へ収めた値）からのずれの 2 乗和が
/// 最も減る入れ替えから当てる。希望・手動固定のマス、置けないシフト、個人の範囲から外れる手、必須違反が増える手は当てない。
/// 1 組ずつ当てて止まったら、月全体（2 人・3 人・前後 2 日）の入れ替えを焼きなましで探す（<see cref="Anneal"/>、Android 3.727.118）。
/// </summary>
public static class GroupCountEqualizer
{
    public sealed record Result(int[][] Schedule, int Swaps, long SpreadBefore, long SpreadAfter);

    private static double[][] Targets(Problem p, int[][] cnt, IReadOnlySet<int>? shifts)
    {
        var t = Enumerable.Range(0, p.S).Select(_ => Enumerable.Repeat(double.NaN, p.K).ToArray()).ToArray();
        foreach (var m in p.GroupMembers)
        {
            if (m.Length < 2) continue;
            for (var k = 0; k < p.K; k++)
            {
                if (shifts is not null && !shifts.Contains(k)) continue;
                var el = m.Where(i => p.MayPlace(i, k)).ToArray();
                if (el.Length < 2) continue;
                var mean = el.Sum(i => cnt[i][k]) / (double)el.Length;
                foreach (var i in el)
                {
                    double lo = p.RangeLo[i][k] == int.MinValue ? 0 : p.RangeLo[i][k];
                    double hi = p.RangeHi[i][k] == int.MaxValue ? double.MaxValue : p.RangeHi[i][k];
                    t[i][k] = Math.Clamp(mean, lo, Math.Max(lo, hi));
                }
            }
        }
        return t;
    }

    private const int AnnealIters = 400_000;
    private const int AnnealRuns = 3;

    /// <summary>
    /// 1 組ずつの入れ替えでは範囲や禁止の並びにかかって進めないとき用の焼きなまし（月全体、同じグループの 2 人の入れ替え・3 人の回し・前後 2 日をまとめた入れ替え）。
    /// 途中は個人の範囲を外れても必須が増えてもよい（罰として数える）が、採るのは「各 (職員,シフト) の範囲からの外れ量が元以下・
    /// 必須が元以下」の盤面のうち、目標からのずれがいちばん小さいものだけ。よりそろった盤面が見つかれば <paramref name="work"/> を書き換えて true。
    /// </summary>
    private static bool Anneal(Problem p, int[][] work, int[][] cnt0, double[][] tgt, int iters, long seed)
    {
        // 手は 1 手ごとに無作為に作る（全部を先に並べると 30 人・31 日で数十万件になる）: 同じグループの 2 人の同じ日の入れ替え・
        // 3 人の回し・同じ 2 人の前後 2 日をまとめた入れ替えを等しい割合で。希望・手動固定のマスに当たる手は捨てる。日ごとの人数は変わらない。
        var groups = p.GroupMembers.Where(m => m.Length >= 2).ToArray();
        if (groups.Length == 0 || p.T == 0) return false;
        var pairW = groups.Select(m => m.Length * (m.Length - 1) / 2).ToArray();
        var pairTotal = pairW.Sum();
        var cnt = cnt0.Select(r => (int[])r.Clone()).ToArray();
        int Lo(int i, int k) => p.RangeLo[i][k] == int.MinValue ? 0 : p.RangeLo[i][k];
        int Out(int i, int k, int n) => Math.Max(0, Lo(i, k) - n) + (p.RangeHi[i][k] == int.MaxValue ? 0 : Math.Max(0, n - p.RangeHi[i][k]));
        var out0 = Enumerable.Range(0, p.S).Select(i => Enumerable.Range(0, p.K).Select(k => Out(i, k, cnt[i][k])).ToArray()).ToArray();
        double Sq(int i, int k, int n) { var t = tgt[i][k]; return double.IsNaN(t) ? 0.0 : (n - t) * (n - t); }
        int Excess(int i, int k, int n) => Math.Max(0, Out(i, k, n) - out0[i][k]);   // 範囲の外れ量のうち元を超えた分
        var devCur = 0.0;
        for (var i = 0; i < p.S; i++) for (var k = 0; k < p.K; k++) devCur += Sq(i, k, cnt[i][k]);
        var exCur = 0;
        var hardCur = 0;   // 元からの必須の増減
        var cur = work.Copy2D(); var trial = work.Copy2D();
        var bestDev = devCur;
        int[][]? best = null;
        var rnd = new JavaRandom(seed);
        const double t0 = 1.0, t1 = 0.02, wEx = 2.0, wHard = 20.0;
        int[] ci2 = new int[12], ck = new int[12], cd = new int[12];   // この手での (職員,シフト) ごとの回数の増減
        for (var step = 0; step < iters; step++)
        {
            var gi = rnd.NextInt(pairTotal); var g = 0;
            while (gi >= pairW[g]) { gi -= pairW[g]; g++; }
            var m = groups[g];
            var kind = rnd.NextInt(3);
            var n = kind == 1 && m.Length >= 3 ? 3 : 2;
            var who = new int[n];
            who[0] = m[rnd.NextInt(m.Length)];
            do { who[1] = m[rnd.NextInt(m.Length)]; } while (who[1] == who[0]);
            if (n == 3) do { who[2] = m[rnd.NextInt(m.Length)]; } while (who[2] == who[0] || who[2] == who[1]);
            var d1 = rnd.NextInt(p.T);
            int[] days;
            if (kind == 2 && p.T >= 2)
            {
                int d2;
                do { d2 = rnd.NextInt(p.T); } while (d2 == d1);
                days = new[] { d1, d2 };
            }
            else days = new[] { d1 };
            if (days.Any(d => who.Any(i => p.WishLocked(i, d)))) continue;
            bool ok = true, moved = false; var nc = 0;
            void Add(int i, int k, int dd)
            {
                for (var e = 0; e < nc; e++) if (ci2[e] == i && ck[e] == k) { cd[e] += dd; return; }
                ci2[nc] = i; ck[nc] = k; cd[nc] = dd; nc++;
            }
            foreach (var d in days)
            {
                for (var t = 0; t < n; t++)
                {
                    int from = cur[who[t]][d], to = cur[who[(t + 1) % n]][d];
                    if (from < 0 || from >= p.K || to < 0 || to >= p.K) { ok = false; break; }
                    if (from == to) continue;
                    if (!p.MayPlaceAt(who[t], d, to)) { ok = false; break; }
                    Add(who[t], from, -1); Add(who[t], to, 1); moved = true;
                }
                if (!ok) break;
            }
            if (!ok || !moved) continue;
            var dDev = 0.0; var dEx = 0;
            for (var e = 0; e < nc; e++)
            {
                int i = ci2[e], k = ck[e], dd = cd[e];
                if (dd == 0) continue;
                dDev += Sq(i, k, cnt[i][k] + dd) - Sq(i, k, cnt[i][k]);
                dEx += Excess(i, k, cnt[i][k] + dd) - Excess(i, k, cnt[i][k]);
            }
            foreach (var d in days) for (var t = 0; t < n; t++) trial[who[t]][d] = cur[who[(t + 1) % n]][d];
            var dHard = HardDelta.Delta(p, cur, trial);
            var df = dDev + wEx * dEx + wHard * dHard;
            var temp = t0 * Math.Pow(t1 / t0, (double)step / iters);
            if (df <= 0 || rnd.NextDouble() < Math.Exp(-df / temp))
            {
                foreach (var d in days) for (var t = 0; t < n; t++) cur[who[t]][d] = trial[who[t]][d];
                for (var e = 0; e < nc; e++) cnt[ci2[e]][ck[e]] += cd[e];
                devCur += dDev; exCur += dEx; hardCur += dHard;
                if (exCur == 0 && hardCur <= 0 && devCur < bestDev - 1e-9)
                {
                    bestDev = devCur;
                    best ??= Enumerable.Range(0, p.S).Select(_ => new int[p.T]).ToArray();
                    for (var i = 0; i < p.S; i++) Array.Copy(cur[i], best[i], p.T);
                }
            }
            else foreach (var d in days) for (var t = 0; t < n; t++) trial[who[t]][d] = cur[who[t]][d];
        }
        if (best is null) return false;
        for (var i = 0; i < p.S; i++) Array.Copy(best[i], work[i], p.T);
        return true;
    }

    private static int[][] Counts(Problem p, int[][] s) =>
        Enumerable.Range(0, p.S).Select(i => { var c = new int[p.K]; foreach (var k in s[i]) if (k >= 0 && k < p.K) c[k]++; return c; }).ToArray();

    private static long Dev(double[][] t, int[][] cnt)
    {
        var d = 0.0;
        for (var i = 0; i < t.Length; i++) for (var k = 0; k < t[i].Length; k++) if (!double.IsNaN(t[i][k])) { var e = cnt[i][k] - t[i][k]; d += e * e; }
        return (long)Math.Round(d * 100, MidpointRounding.AwayFromZero);
    }

    /// <param name="shifts">そろえるシフト（null＝全部、Android 3.727.116）。ほかのシフトの回数は入れ替えで動き得るが、個人の下限・上限は守る。</param>
    public static Result Apply(MagiState state, int[][] schedule, IReadOnlySet<int>? shifts = null, int maxSwaps = 2_000,
        int annealIters = AnnealIters, long seed = 1L)
    {
        var p = new Problem(state);
        var work = ScheduleUtil.NormalizeSchedule(schedule, p);
        var cnt = Counts(p, work);
        var tgt = Targets(p, cnt, shifts);
        var before = Dev(tgt, cnt);
        if (tgt.All(row => row.All(double.IsNaN))) return new Result(work, 0, before, before);   // そろえる (職員,シフト) が無い
        bool Movable(int i, int j) => !p.WishLocked(i, j);   // 希望固定と手動固定
        bool RangeOk(int i, int k, int d)
        {
            var lo = p.RangeLo[i][k] == int.MinValue ? 0 : p.RangeLo[i][k];
            var hi = p.RangeHi[i][k];
            int Out(int n) => Math.Max(0, lo - n) + (hi == int.MaxValue ? 0 : Math.Max(0, n - hi));
            var n0 = cnt[i][k];
            return Out(n0 + d) <= Out(n0);
        }
        double Dsq(int i, int k, int d)
        {
            var t = tgt[i][k];
            if (double.IsNaN(t)) return 0.0;
            var a = cnt[i][k] - t; var b = a + d;
            return b * b - a * a;
        }
        var swaps = 0;
        while (swaps < maxSwaps)
        {
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
                        var gain = Dsq(a, x, -1) + Dsq(a, y, 1) + Dsq(b, x, 1) + Dsq(b, y, -1);
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
        // 焼きなましは乱数で行き着く先が揺れるので、種を変えて AnnealRuns 回走らせ、いちばんそろった盤面を採る
        int[][]? bestRun = null; var bestRunDev = Dev(tgt, cnt);
        if (annealIters > 0)
            for (var run = 0; run < AnnealRuns; run++)
            {
                var w = work.Copy2D();
                if (!Anneal(p, w, cnt, tgt, annealIters, seed + run)) continue;
                var dv = Dev(tgt, Counts(p, w));
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
        return new Result(work, swaps, before, Dev(tgt, cnt));
    }
}
