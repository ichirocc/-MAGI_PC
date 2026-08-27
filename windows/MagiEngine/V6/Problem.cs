using System.Globalization;
using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>Resolved constraint rows in index form (kigou -&gt; indices).</summary>
public sealed class C1
{
    public readonly int Day1;
    public readonly int ShiftIdx;
    public readonly int Day2;
    public C1(int day1, int shiftIdx, int day2) { Day1 = day1; ShiftIdx = shiftIdx; Day2 = day2; }
}

public sealed class C2
{
    public readonly int ShiftIdx;
    public readonly int Count;
    public C2(int shiftIdx, int count) { ShiftIdx = shiftIdx; Count = count; }
}

public sealed class C3
{
    public readonly int[] Seq;
    public C3(int[] seq) { Seq = seq; }
}

public sealed class C41
{
    public readonly int GroupIdx;
    public readonly int ShiftIdx;
    public readonly int L;
    public readonly int U;
    public C41(int groupIdx, int shiftIdx, int l, int u) { GroupIdx = groupIdx; ShiftIdx = shiftIdx; L = l; U = u; }
}

public sealed class C42
{
    public readonly int G1;
    public readonly int S1;
    public readonly int G2;
    public readonly int S2;
    public C42(int g1, int s1, int g2, int s2) { G1 = g1; S1 = s1; G2 = g2; S2 = s2; }
}

/// <summary>
/// Immutable, index-resolved view of a <see cref="MagiState"/> ready for fast evaluation.
/// Faithfully mirrors the Kotlin/Android app's <c>Problem.kt</c> (which itself mirrors the Web
/// worker's prelude + resolveConstraints()) field-for-field and line-for-line, including its
/// diagnostic side-channels (out-of-range group staff, over-length windows/patterns, unresolved
/// constraint rows) that downstream code (phase 7's V6SanityPort port) will surface to the user.
///
/// This is a plain class (not a record), exactly mirroring Kotlin's <c>class Problem</c> (not
/// <c>data class</c>) — reference equality is intentional and correct here, matching the source.
/// </summary>
public sealed class Problem
{
    public MagiState State { get; }

    public int S { get; }
    public int T { get; }
    public int K { get; }
    public int G { get; }
    public bool Use2 { get; }

    // [3.410.0/P-06 移植元] groupIdx が群の範囲外だった職員 index。先頭群(0)へ寄せたうえで記録する
    // （黙って寄せると別の群のルールが静かに掛かるため、必ず読み取れるようにする）。
    private readonly List<int> _outOfRangeGroupStaff = new();
    public IReadOnlyList<int> OutOfRangeGroupStaff => _outOfRangeGroupStaff;

    public int[] Sgrp { get; }

    /// <summary>休シフトの index（記号"休"解決、無ければ0）。曜日平準化(weekly)で「勤務日か休か」を判定。</summary>
    public int RestIdx { get; }

    /// <summary>
    /// startDate の曜日オフセット（%7）。weekday(j)=(dow0+j)%7。曜日平準化(weekly)の曜日バケットに使う。
    /// 絶対曜日ラベルは重要でなく、day j と day j+7 が同一バケットに落ちることのみが必要。
    /// .NET の <see cref="DayOfWeek"/> の数値（Sunday=0..Saturday=6）は、移植元(Kotlin)が計算する
    /// <c>java.time.DayOfWeek.value % 7</c>（Monday=1..Saturday=6,Sunday=0）と数値として完全一致する
    /// （両者とも Sunday=0）ため、変換テーブル無しでそのままキャストできる。
    /// </summary>
    public int Dow0 { get; }

    /// <summary>Allowed shift indices per group (groupShift[g][k]==1).</summary>
    public int[][] Bucket { get; }

    /// <summary>groupMembers[g] = 群gに属する staff index。グループ内公平化(fair)で群メンバー間の回数偏差を均すのに使う。</summary>
    public int[][] GroupMembers { get; }

    /// <summary>Staff indices that may take a given shift (used by block-fill moves).</summary>
    public int[][] StaffForShift { get; }

    /// <summary>wish[i][j] = desired shift index, or -1.</summary>
    public int[][] Wish { get; }

    /// <summary>need1[k][j] / need2[k][j] = required count, or -1 (= no requirement).</summary>
    public int[][] Need1 { get; }
    public int[][] Need2 { get; }

    /// <summary>rangeLo/Hi[i][k] = LimMin/LimMax, or int.MinValue/MaxValue when unset.</summary>
    public int[][] RangeLo { get; }
    public int[][] RangeHi { get; }

    /// <summary>
    /// apt[i][k] = 適切回数（群単位の双方向目標 groupShiftApt[群][シフト]）, or -1 when unset.
    /// 担当可能(canDo=bucket)なシフトのみ展開し、解消不能な幻のapt偏差を作らない（c1 と同じ方針）。
    /// </summary>
    public int[][] Apt { get; }

    public IReadOnlyList<C1> Cons1 { get; }
    public IReadOnlyList<C2> Cons2 { get; }
    public IReadOnlyList<C3> Cons3 { get; }
    public IReadOnlyList<C3> Cons3n { get; }
    public IReadOnlyList<C3> Cons3m { get; }
    public IReadOnlyList<C3> Cons3mn { get; }

    // [監査#9 移植元] 期間より長い連続パターンはパース段階で除外し、(族, パターン表示) をここに記録する。
    private readonly List<(string Family, string Text)> _c3OverT = new();
    public IReadOnlyList<(string Family, string Text)> C3OverT => _c3OverT;

    // [3.309.0 移植元] 存在しないシフト記号を含む連続パターン行。(族, 未定義記号を〈〉で囲んだ行) を記録する。
    private readonly List<(string Family, string Text)> _c3UnknownShift = new();
    public IReadOnlyList<(string Family, string Text)> C3UnknownShift => _c3UnknownShift;

    // [3.412.0/P-04 移植元] 期間より長い窓の要件(cons1)。行は残す（評価の挙動は不変）が理由を記録する。
    private readonly List<string> _c1OverT = new();
    public IReadOnlyList<string> C1OverT => _c1OverT;

    // [3.320.0 移植元] cons1/cons2/cons41/cons42/cons41s/cons42s のうち記号が解決できない・数値が
    // 不正な行。(族ラベル, 行の表示) を記録する。
    private readonly List<(string Family, string Text)> _unresolvedRows = new();
    public IReadOnlyList<(string Family, string Text)> UnresolvedRows => _unresolvedRows;

    public IReadOnlyList<C41> Cons41 { get; }
    public IReadOnlyList<C42> Cons42 { get; }

    // [スキルグループ新設] スキル群の C41/C42 相当（ssk = staff のスキル群index。既存 sgrp とは独立）。
    public int SkillG { get; }
    public int[] Ssk { get; }
    public IReadOnlyList<C41> Cons41s { get; }
    public IReadOnlyList<C42> Cons42s { get; }

    public Problem(MagiState state)
    {
        State = state;
        S = state.StaffCount;
        T = state.DayCount;
        K = state.ShiftCount;
        G = state.GroupCount;
        Use2 = state.Use2Patterns;

        // ---- property-initializer section (mirrors Problem.kt's immediate `val` initializers) ----

        Sgrp = new int[S];
        for (int i = 0; i < S; i++)
        {
            int g = state.StaffList[i].GroupIdx;
            if (g >= 0 && g < G) Sgrp[i] = g;
            else { _outOfRangeGroupStaff.Add(i); Sgrp[i] = 0; }
        }

        RestIdx = ScheduleUtil.RestShiftIndex(state);

        Dow0 = DateOnly.TryParseExact(state.StartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var startDateParsed)
            ? (int)startDateParsed.DayOfWeek
            : 0;

        Bucket = new int[G][];
        for (int g = 0; g < G; g++)
        {
            var row = g < state.GroupShift.Count ? state.GroupShift[g] : null;
            var list = new List<int>();
            for (int k = 0; k < K; k++)
            {
                if (row is not null && k < row.Count && row[k] == 1) list.Add(k);
            }
            Bucket[g] = list.ToArray();
        }

        GroupMembers = new int[G][];
        for (int g = 0; g < G; g++)
        {
            var list = new List<int>();
            for (int i = 0; i < S; i++) if (Sgrp[i] == g) list.Add(i);
            GroupMembers[g] = list.ToArray();
        }

        StaffForShift = new int[K][];
        for (int k = 0; k < K; k++)
        {
            var list = new List<int>();
            for (int i = 0; i < S; i++) if (Array.IndexOf(Bucket[Sgrp[i]], k) >= 0) list.Add(i);
            StaffForShift[k] = list.ToArray();
        }

        Wish = new int[S][];
        for (int i = 0; i < S; i++)
        {
            Wish[i] = new int[T];
            for (int j = 0; j < T; j++) Wish[i][j] = -1;
        }

        SkillG = state.SkillGroupCount;
        Ssk = new int[S];
        for (int i = 0; i < S; i++) Ssk[i] = state.StaffList[i].SkillIdx;

        // ---- init-block section (mirrors Problem.kt's trailing `init { ... }` block) ----

        foreach (var (key, v) in state.Wishes)
        {
            var p = key.Split(',');
            var iOpt = p.Length > 0 ? KotlinInterop.ToIntOrNull(p[0]) : null;
            if (iOpt is not int i2) continue;
            var jOpt = p.Length > 1 ? KotlinInterop.ToIntOrNull(p[1]) : null;
            if (jOpt is not int j2) continue;
            if (i2 >= 0 && i2 < S && j2 >= 0 && j2 < T) Wish[i2][j2] = v;
        }

        Need1 = new int[K][];
        Need2 = new int[K][];
        for (int k = 0; k < K; k++)
        {
            Need1[k] = new int[T];
            Need2[k] = new int[T];
            for (int j = 0; j < T; j++)
            {
                Need1[k][j] = NeedAt(state, k, j, false);
                Need2[k][j] = NeedAt(state, k, j, true);
            }
        }

        RangeLo = new int[S][];
        RangeHi = new int[S][];
        for (int i = 0; i < S; i++)
        {
            RangeLo[i] = new int[K];
            RangeHi[i] = new int[K];
            for (int k = 0; k < K; k++) { RangeLo[i][k] = int.MinValue; RangeHi[i][k] = int.MaxValue; }
        }
        foreach (var (key, r) in state.StaffRange)
        {
            var p = key.Split(',');
            var iOpt = p.Length > 0 ? KotlinInterop.ToIntOrNull(p[0]) : null;
            if (iOpt is not int i2) continue;
            var kOpt = p.Length > 1 ? KotlinInterop.ToIntOrNull(p[1]) : null;
            if (kOpt is not int k2) continue;
            if (i2 >= 0 && i2 < S && k2 >= 0 && k2 < K)
            {
                var lo = KotlinInterop.ToIntOrNull(r.Lo.Trim());
                if (lo is int loV) RangeLo[i2][k2] = loV;
                var hi = KotlinInterop.ToIntOrNull(r.Hi.Trim());
                if (hi is int hiV) RangeHi[i2][k2] = hiV;
            }
        }

        // 適切回数（双方向目標）: state.groupShiftApt[群][シフト] を個人別 apt[i][k] へ展開（群単位＝同群全員に同一目標）。
        // 担当ONシフトのみ（bucket=canDo）有効化し、担当不可シフトの幻のapt偏差を除外する。
        Apt = new int[S][];
        for (int i = 0; i < S; i++)
        {
            Apt[i] = new int[K];
            for (int k = 0; k < K; k++) Apt[i][k] = -1;
        }
        for (int i = 0; i < S; i++)
        {
            int g = Sgrp[i];
            if (g < 0 || g >= state.GroupShiftApt.Count) continue;
            var row = state.GroupShiftApt[g];
            var canK = g >= 0 && g < Bucket.Length ? Bucket[g] : null;
            for (int k = 0; k < K; k++)
            {
                if (k >= row.Count) continue;
                var parsed = KotlinInterop.ToIntOrNull(row[k].Trim());
                if (parsed is not int t) continue;
                if (t < 0 || canK is null || Array.IndexOf(canK, k) < 0) continue;
                // [整合] 個人別回数(staffRange=LimMin/LimMax)の[lo,hi]外の群目標は到達不能。範囲端に
                // クランプし、staffRangeで固定/制限された職員に解消不能な幻のapt違反が出るのを防ぐ
                // （例: Dﾃを2-2固定の職員に群目標10）。
                int rlo = RangeLo[i][k], rhi = RangeHi[i][k];
                if (rlo != int.MinValue && t < rlo) t = rlo;
                if (rhi != int.MaxValue && t > rhi) t = rhi;
                Apt[i][k] = t;
            }
        }

        var cons1List = new List<C1>();
        foreach (var it in state.Cons1)
        {
            int d1 = KotlinInterop.ToIntOrNull(it.Day1) ?? 0;
            int si = ShiftIdxOf(it.ShiftKigou);
            int d2 = KotlinInterop.ToIntOrNull(it.Day2) ?? 0;
            if (d1 > 0 && si >= 0 && d2 > 0)
            {
                // [3.412.0/P-04] 行としては解決できるが窓が期間を超える＝チェッカーが無言で飛ばす。
                // 行は残す（評価の挙動は完全に不変）が、記録して Sanity が理由を案内する。
                if (d1 > T) _c1OverT.Add($"{it.ShiftKigou} を{d1}日で{d2}回以上");
                cons1List.Add(new C1(d1, si, d2));
            }
            else
            {
                _unresolvedRows.Add(("窓の要件", $"{Mark(it.ShiftKigou, si >= 0)} を{it.Day1}日で{it.Day2}回以上"));
            }
        }
        Cons1 = cons1List;

        var cons2List = new List<C2>();
        foreach (var it in state.Cons2)
        {
            int si = ShiftIdxOf(it.ShiftKigou);
            int c = KotlinInterop.ToIntOrNull(it.Count) ?? 0;
            if (si >= 0 && c > 0) cons2List.Add(new C2(si, c));
            else _unresolvedRows.Add(("個人の合計", $"{Mark(it.ShiftKigou, si >= 0)} を{it.Count}回以上"));
        }
        Cons2 = cons2List;

        Cons3 = ResolveC3(state.Cons3, "c3");
        Cons3n = ResolveC3(state.Cons3n, "c3n");
        Cons3m = ResolveC3(state.Cons3m, "c3m");
        Cons3mn = ResolveC3(state.Cons3mn, "c3mn");

        var cons41List = new List<C41>();
        foreach (var it in state.Cons41)
        {
            int gi = GroupIdxOf(it.GroupKigou);
            int si = ShiftIdxOf(it.ShiftKigou);
            bool hasLo = !string.IsNullOrWhiteSpace(it.L);
            bool hasHi = !string.IsNullOrWhiteSpace(it.U);
            int lo = hasLo ? (KotlinInterop.ToIntOrNull(it.L) ?? 0) : 0;
            int hi = hasHi ? (KotlinInterop.ToIntOrNull(it.U) ?? int.MaxValue) : int.MaxValue;
            if (gi >= 0 && si >= 0 && (hasLo || hasHi)) cons41List.Add(new C41(gi, si, lo, hi));
            else
            {
                _unresolvedRows.Add(("群のレンジ",
                    $"{Mark(it.GroupKigou, gi >= 0)} の {Mark(it.ShiftKigou, si >= 0)}（{it.L}〜{it.U}）"));
            }
        }
        Cons41 = cons41List;

        var cons42List = new List<C42>();
        foreach (var it in state.Cons42)
        {
            int g1 = GroupIdxOf(it.G1Kigou);
            int g2 = GroupIdxOf(it.G2Kigou);
            int s1 = ShiftIdxOf(it.S1Kigou);
            int s2 = ShiftIdxOf(it.S2Kigou);
            if (g1 >= 0 && g2 >= 0 && s1 >= 0 && s2 >= 0) cons42List.Add(new C42(g1, s1, g2, s2));
            else
            {
                _unresolvedRows.Add(("群ペア禁止",
                    $"{Mark(it.G1Kigou, g1 >= 0)}/{Mark(it.S1Kigou, s1 >= 0)} × {Mark(it.G2Kigou, g2 >= 0)}/{Mark(it.S2Kigou, s2 >= 0)}"));
            }
        }
        Cons42 = cons42List;

        var cons41sList = new List<C41>();
        foreach (var it in state.Cons41s)
        {
            int gi = SkillGroupIdxOf(it.GroupKigou);
            int si = ShiftIdxOf(it.ShiftKigou);
            bool hasLo = !string.IsNullOrWhiteSpace(it.L);
            bool hasHi = !string.IsNullOrWhiteSpace(it.U);
            int lo = hasLo ? (KotlinInterop.ToIntOrNull(it.L) ?? 0) : 0;
            int hi = hasHi ? (KotlinInterop.ToIntOrNull(it.U) ?? int.MaxValue) : int.MaxValue;
            if (gi >= 0 && si >= 0 && (hasLo || hasHi)) cons41sList.Add(new C41(gi, si, lo, hi));
            else
            {
                _unresolvedRows.Add(("スキル群のレンジ",
                    $"{Mark(it.GroupKigou, gi >= 0)} の {Mark(it.ShiftKigou, si >= 0)}（{it.L}〜{it.U}）"));
            }
        }
        Cons41s = cons41sList;

        var cons42sList = new List<C42>();
        foreach (var it in state.Cons42s)
        {
            int g1 = SkillGroupIdxOf(it.G1Kigou);
            int g2 = SkillGroupIdxOf(it.G2Kigou);
            int s1 = ShiftIdxOf(it.S1Kigou);
            int s2 = ShiftIdxOf(it.S2Kigou);
            if (g1 >= 0 && g2 >= 0 && s1 >= 0 && s2 >= 0) cons42sList.Add(new C42(g1, s1, g2, s2));
            else
            {
                _unresolvedRows.Add(("スキル群ペア禁止",
                    $"{Mark(it.G1Kigou, g1 >= 0)}/{Mark(it.S1Kigou, s1 >= 0)} × {Mark(it.G2Kigou, g2 >= 0)}/{Mark(it.S2Kigou, s2 >= 0)}"));
            }
        }
        Cons42s = cons42sList;
    }

    /// <summary>記号が解決できたかを `〈〉` で示す表示（cons3 系の記録と同じ書式）。</summary>
    private static string Mark(string kigou, bool resolved) => resolved ? kigou : $"〈{kigou}〉";

    /// <summary>getNeed(k,j,p2): per-day override, falling back to shift default; -1 == none.</summary>
    private static int NeedAt(MagiState state, int k, int j, bool p2)
    {
        var map = p2 ? state.NeedDay2 : state.NeedDay1;
        if (map.TryGetValue($"{k},{j}", out var v) && !string.IsNullOrWhiteSpace(v))
        {
            var parsed = KotlinInterop.ToIntOrNull(v.Trim());
            if (parsed is int pv) return pv;
        }
        var def = p2 ? state.Shifts[k].Need2 : state.Shifts[k].Need1;
        if (string.IsNullOrWhiteSpace(def)) return -1;
        return KotlinInterop.ToIntOrNull(def.Trim()) ?? -1;
    }

    private int ShiftIdxOf(string kigou)
    {
        for (int i = 0; i < State.Shifts.Count; i++) if (State.Shifts[i].Kigou == kigou) return i;
        return -1;
    }

    private int GroupIdxOf(string kigou)
    {
        for (int i = 0; i < State.Groups.Count; i++) if (State.Groups[i].Kigou == kigou) return i;
        return -1;
    }

    private int SkillGroupIdxOf(string kigou)
    {
        for (int i = 0; i < State.SkillGroups.Count; i++) if (State.SkillGroups[i].Kigou == kigou) return i;
        return -1;
    }

    /// <summary>
    /// resolveC3: truncate the pattern at the first blank symbol; drop the whole row if an
    /// interior symbol is unresolvable (mirrors the Web doc7#4 fix — never compact).
    /// </summary>
    private IReadOnlyList<C3> ResolveC3(IReadOnlyList<C3Row> rows, string fam)
    {
        var result = new List<C3>();
        foreach (var row in rows)
        {
            var c3 = ResolveC3Row(row.Pattern, fam);
            if (c3 is not null) result.Add(c3);
        }
        return result;
    }

    private C3? ResolveC3Row(IReadOnlyList<string> pattern, string fam)
    {
        int end = -1;
        for (int idx = 0; idx < pattern.Count; idx++)
        {
            if (string.IsNullOrWhiteSpace(pattern[idx])) { end = idx; break; }
        }
        var body = end >= 0 ? pattern.Take(end).ToList() : pattern.ToList();
        if (body.Count == 0) return null;

        var seq = new int[body.Count];
        for (int idx = 0; idx < body.Count; idx++)
        {
            int si = ShiftIdxOf(body[idx]);
            if (si < 0)
            {
                // [3.309.0] 無言で捨てず記録する（Sanity が「この行は効いていない」と案内する）。
                var marked = string.Concat(body.Select(t => ShiftIdxOf(t) < 0 ? $"〈{t}〉" : t));
                _c3UnknownShift.Add((fam, marked));
                return null;
            }
            seq[idx] = si;
        }
        if (seq.Length > T)
        {
            // [監査#9] L>期間はどの族でも判定不能/無意味 → 除外して記録（Sanityが案内）
            _c3OverT.Add((fam, string.Concat(body)));
            return null;
        }
        return new C3(seq);
    }

    // [監査#4b 移植元] 被覆セル評価（per-cell OR/AND）— Evaluator/Δ/Checker の単一ソース
    // （VBA本家=Web HF574と三面統一）。
    //   U: 両定義=min(u1,u2)・片方定義=その値（P2単独定義セルも評価）。
    //   O: 両定義=両超過時のみmin(o1,o2)・片方定義=その超過。U>0とO>0は同一セルで両立しない。
    //   実データ(P1=P2)では u1=u2/o1=o2 のため旧・総量min式とビット単位で同値。
    public int CovUCell(int k, int j, int got)
    {
        int lo1 = Need1[k][j];
        int lo2 = Use2 ? Need2[k][j] : -1;
        int u1 = lo1 >= 0 ? Math.Max(lo1 - got, 0) : -1;
        int u2 = lo2 >= 0 ? Math.Max(lo2 - got, 0) : -1;
        if (u1 >= 0 && u2 >= 0) return Math.Min(u1, u2);
        if (u1 >= 0) return u1;
        if (u2 >= 0) return u2;
        return 0;
    }

    public int CovOCell(int k, int j, int got)
    {
        int lo1 = Need1[k][j];
        int lo2 = Use2 ? Need2[k][j] : -1;
        int o1 = lo1 >= 0 ? Math.Max(got - lo1, 0) : -1;
        int o2 = lo2 >= 0 ? Math.Max(got - lo2, 0) : -1;
        if (o1 >= 0 && o2 >= 0) return Math.Min(o1, o2);
        if (o1 >= 0) return o1;
        if (o2 >= 0) return o2;
        return 0;
    }

    /// <summary>
    /// [三連/五連など任意長への配慮] 職員 i の j 列を newK に変えたとき、cons3n（禁止連続, HARD）を
    /// 新たに作るかを判定する。任意長（三連・五連等）を表現できる cons3n ルールに対し、各ルール
    /// (長さd)について j をカバーする開始位置 s の窓を全て調べ、位置jだけ newK に差し替えて残りは
    /// 現在の schedule のまま完全一致(z==d)するかを見る。他セルは変えない=1手の影響範囲チェックとして
    /// 正しい。これは探索の枝刈り（成功率向上のヒント）用途で、最終的な正しさは常に
    /// UnifiedViolationChecker（source of truth、phase 3で移植）が担保する＝本関数の判定が仮に
    /// 見逃しても結果は不変（安全側）。
    /// </summary>
    public bool MakesForbiddenRun(int[][] schedule, int i, int j, int newK)
    {
        foreach (var c in Cons3n)
        {
            var seq = c.Seq;
            int d = seq.Length;
            if (d == 0 || d > T) continue;
            int sLo = Math.Max(j - d + 1, 0);
            int sHi = Math.Min(j, T - d);
            for (int s = sLo; s <= sHi; s++)
            {
                int z = 0;
                for (int l = 0; l < d; l++)
                {
                    int v = (s + l == j) ? newK : schedule[i][s + l];
                    if (v == seq[l]) z++;
                }
                if (z == d) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Initial assignment from state.schedule, overwriting a cell with its wish only when the
    /// wished shift is actually in the staff's allowed bucket (Web HF143 capability guard).
    /// </summary>
    public int[][] InitialAssignment()
    {
        var result = new int[S][];
        for (int i = 0; i < S; i++)
        {
            var b = Bucket[Sgrp[i]];
            var row = new int[T];
            for (int j = 0; j < T; j++)
            {
                int k = (i < State.Schedule.Count && j < State.Schedule[i].Count) ? State.Schedule[i][j] : -1;
                int w = Wish[i][j];
                if (w >= 0 && Array.IndexOf(b, w) >= 0) k = w;
                // [3.410.0/P-01, 3.419.0 移植元] 範囲外セルの寄せ先(restIdx)をこの職員が担当できるか
                // 確認せずに直接使うと、担当不可の群で意図しない groupViol(HARD) を作る。共通規則
                // FillShiftIndex へ委譲する（Ws1Ops 相当の3経路と同じ判断）。
                if (k < 0 || k >= K) k = ScheduleUtil.FillShiftIndex(b, RestIdx);
                row[j] = k;
            }
            result[i] = row;
        }
        return result;
    }
}
