using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using MagiEngine.Model;
using MagiEngine.Tests.Fixtures;
using MagiEngine.V6;
using Group = MagiEngine.Model.Group;

namespace MagiEngine.Tests.V6;

/// <summary>
/// <see cref="UnifiedViolationChecker.Check"/> と高速化前の写し <see cref="CheckerReferenceV0"/> の報告を全フィールド（マップは挿入順込み、
/// WeightedScore は生ビット、ログは時間の括弧を除く文面・水準・タグ）で突き合わせる。
/// 盤面は実データ fixture そのもの＋乱択の書き換え、規則は fixture のもの＋乱択で足した c41/c42/c41s/c42s（所属の範囲外・未所属・自己ペアを含む）。
/// </summary>
public class CheckerEquivalenceTest
{
    private static MagiState Load(string name) => StateJsonSerializer.Parse(FixtureLoader.ReadRaw(name));

    private static string Str(object? v) => v switch
    {
        null => "null",
        string s => s,
        IEnumerable e => "[" + string.Join(", ", e.Cast<object?>().Select(Str)) + "]",
        _ => v.ToString()!,
    };

    private static void Map<TV>(StringBuilder sb, string tag, IReadOnlyDictionary<string, TV> m)
    {
        sb.Append(tag).Append('{');
        foreach (var (k, v) in m) sb.Append(k).Append('=').Append(Str(v)).Append(';');
        sb.Append('}');
    }

    private static readonly Regex Elapsed = new(@" \(\d+ms\)$");

    private static string Canon(ViolationReport r)
    {
        var sb = new StringBuilder();
        Map(sb, "v", r.Violations); Map(sb, "n", r.NeedViolations); Map(sb, "c", r.CountViolations);
        Map(sb, "cf", r.CellFamilies); Map(sb, "kf", r.CountFamilies); Map(sb, "nf", r.NeedFamilies);
        Map(sb, "b", r.Breakdown); Map(sb, "d", r.DistLocations);
        sb.Append("t=").Append(r.Total).Append(";h=").Append(r.Hard).Append(";s=").Append(r.Soft);
        sb.Append(";w=").Append(BitConverter.DoubleToInt64Bits(r.WeightedScore)).Append(";r=").Append(Str(r.C1Runs));
        foreach (var l in r.Logs)
            sb.Append(";log=").Append(l.Iter).Append('/').Append(l.Level).Append('/').Append(l.Tag).Append('/')
                .Append(Elapsed.Replace(l.Message, ""));
        return sb.ToString();
    }

    private static void AssertSame(string label, MagiState st, int[][] s, bool q)
    {
        var want = Canon(CheckerReferenceV0.Check(st, s, quantitativeRangeEval: q));
        var got = Canon(UnifiedViolationChecker.Check(st, s, quantitativeRangeEval: q));
        Assert.True(want == got, label);
    }

    private static int[][] Perturb(MagiState st, JavaRandom rnd, int n)
    {
        var s = st.Schedule.ToIntArray2D();
        int k = st.Shifts.Count;
        int flips = n % 10 == 0 ? s.Length * (s.Length > 0 ? s[0].Length : 0) : 1 + rnd.NextInt(40);
        for (int f = 0; f < flips; f++)
        {
            int i = rnd.NextInt(s.Length);
            int j = rnd.NextInt(s[i].Length);
            s[i][j] = rnd.NextInt(k + 1) - 1;
        }
        return s;
    }

    /// <summary>規則と所属を乱択で足した状態。範囲の端（0・空欄）、同じ (グループ, シフト) の自己ペア、所属 -1・範囲外を混ぜる。</summary>
    private static MagiState Augmented(MagiState b, JavaRandom rnd)
    {
        var skills = b.SkillGroups.Count > 0 ? b.SkillGroups
            : new List<Group> { new("S甲", "S甲"), new("S乙", "S乙"), new("S丙", "S丙") };
        var shifts = b.Shifts.Select(x => x.Kigou).ToList();
        T Pick<T>(IReadOnlyList<T> xs) => xs[rnd.NextInt(xs.Count)];
        string Bound() => rnd.NextInt(4) == 0 ? "" : rnd.NextInt(4).ToString();
        List<C41Row> C41(IReadOnlyList<Group> groups) => Enumerable.Range(0, 1 + rnd.NextInt(5)).Select(_ =>
        {
            var l = Bound(); var u = Bound();
            return new C41Row(Pick(groups).Kigou, Pick(shifts), l, l.Length == 0 && u.Length == 0 ? "1" : u);
        }).ToList();
        List<C42Row> C42(IReadOnlyList<Group> groups) => Enumerable.Range(0, 1 + rnd.NextInt(5)).Select(_ =>
        {
            var g1 = Pick(groups).Kigou; var s1 = Pick(shifts);
            return rnd.NextInt(4) == 0 ? new C42Row(g1, g1, s1, s1) : new C42Row(g1, Pick(groups).Kigou, s1, Pick(shifts));
        }).ToList();
        return b with
        {
            SkillGroups = skills,
            StaffList = b.StaffList.Select(x => x with { SkillIdx = rnd.NextInt(skills.Count + 2) - 1 }).ToList(),
            Cons41 = b.Cons41.Concat(C41(b.Groups)).ToList(), Cons42 = b.Cons42.Concat(C42(b.Groups)).ToList(),
            Cons41s = b.Cons41s.Concat(C41(skills)).ToList(), Cons42s = b.Cons42s.Concat(C42(skills)).ToList(),
        };
    }

    [Fact]
    public void reportsMatchTheReferenceOnRealAndRandomBoards()
    {
        string[] names = { "oct2026_grid_state.json", "golden_state.json", "sept2026_state.json", "sample_state_v6.json",
            "blocked_covu_state.json" };
        var fixtures = names.Select(Load).ToArray();
        for (int f = 0; f < names.Length; f++)
            foreach (var q in new[] { false, true }) AssertSame($"{names[f]} q={q}", fixtures[f], fixtures[f].Schedule.ToIntArray2D(), q);
        var rnd = new JavaRandom(20261003);
        for (int n = 0; n < 4000; n++)
        {
            var st = fixtures[n % fixtures.Length];
            AssertSame($"random#{n} {names[n % names.Length]}", st, Perturb(st, rnd, n), n % 7 == 0);
        }
        for (int m = 0; m < 200; m++)
        {
            var st = Augmented(fixtures[m % fixtures.Length], rnd);
            for (int r = 0; r < 10; r++)
            {
                int n = m * 10 + r;
                var board = r == 0 ? st.Schedule.ToIntArray2D() : Perturb(st, rnd, n);
                AssertSame($"augmented#{m}/{r} {names[m % names.Length]}", st, board, n % 7 == 0);
            }
        }
    }
}
