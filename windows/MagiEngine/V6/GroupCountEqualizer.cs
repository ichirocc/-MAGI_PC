using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// グループ内の回数を、採点に関係なくそろえる（Kotlin <c>GroupCountEqualizer</c>、Android 3.727.115。希望の反映と同じく利用者が押して上書きする操作）。
/// 手は同じ日・同じ勤務グループの 2 人のシフトの入れ替えだけ。各人の目標（置ける人の平均を個人の下限・上限へ収めた値）からのずれの 2 乗和が
/// 最も減る入れ替えから当てる。希望・手動固定のマス、置けないシフト、個人の範囲から外れる手、必須違反が増える手は当てない。
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

    private static int[][] Counts(Problem p, int[][] s) =>
        Enumerable.Range(0, p.S).Select(i => { var c = new int[p.K]; foreach (var k in s[i]) if (k >= 0 && k < p.K) c[k]++; return c; }).ToArray();

    private static long Dev(double[][] t, int[][] cnt)
    {
        var d = 0.0;
        for (var i = 0; i < t.Length; i++) for (var k = 0; k < t[i].Length; k++) if (!double.IsNaN(t[i][k])) { var e = cnt[i][k] - t[i][k]; d += e * e; }
        return (long)Math.Round(d * 100, MidpointRounding.AwayFromZero);
    }

    /// <param name="shifts">そろえるシフト（null＝全部、Android 3.727.116）。ほかのシフトの回数は入れ替えで動き得るが、個人の下限・上限は守る。</param>
    public static Result Apply(MagiState state, int[][] schedule, IReadOnlySet<int>? shifts = null, int maxSwaps = 2_000)
    {
        var p = new Problem(state);
        var work = ScheduleUtil.NormalizeSchedule(schedule, p);
        var cnt = Counts(p, work);
        var tgt = Targets(p, cnt, shifts);
        var before = Dev(tgt, cnt);
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
        return new Result(work, swaps, before, Dev(tgt, cnt));
    }
}
