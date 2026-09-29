using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// [つくる前の確認] 本実行の前に「計算では消えない」「もう一度つくると外れる」を数える（Android <c>PreRunCheck.kt</c>、
/// <c>docs/business-logic.md</c> の診断の節）。表示と誘導だけ＝探索・評価・重みには触れない。希望も上限も自動で変えない（HF77）。
/// </summary>
public static class PreRunCheck
{
    /// <summary>手で置いてある上限 0 の勤務 1 セル（希望で固定したセルは除く）。本実行の入口の clear が外す。</summary>
    public sealed record HandPlacedCell(int Staff, int Day, int Shift);

    /// <summary>個人の上限（0回）の密度。TopStaff は組の数が最も多い職員（同数は番号の小さい方）。</summary>
    public sealed record WallHint(int Pairs, int StaffCount, int TopStaff, int TopPairs);

    /// <summary>本人の希望の件数が個人の上限を超える（BuildGuidance の 6e と同じ判定）。上限超過は要調整＝シートを出す条件に数えない。</summary>
    public sealed record WishOverCap(int Staff, int Shift, int Wished, int Hi);

    public sealed record Summary(
        IReadOnlyList<WishSelfConflict> WishConflicts,
        IReadOnlyList<ImpossibleWish> ImpossibleWishes,
        IReadOnlyList<ForcedCovU> ForcedShortfalls,
        IReadOnlyList<ConstraintMus.DayConflict> DayProofs,
        IReadOnlyList<ConstraintMus.StaffConflict> StaffProofs,
        IReadOnlyList<WishOverCap> WishOverCaps,
        IReadOnlyList<HandPlacedCell> RerunClears,
        WallHint? Wall,
        IReadOnlySet<int>? ZeroCapProofDays = null,
        IReadOnlySet<int>? ZeroCapShortShifts = null)
    {
        /// <summary>個人の上限0（入れない指定）を外すと成立する日の証明＝上限0が絡む（S6 で例外として緩めて確かめる）。</summary>
        public IReadOnlySet<int> ZeroCapDays => ZeroCapProofDays ?? new HashSet<int>();
        /// <summary>上限0を数えなければ不足が減るシフト。</summary>
        public IReadOnlySet<int> ZeroCapShorts => ZeroCapShortShifts ?? new HashSet<int>();
        public static bool ZeroCapStaffProof(ConstraintMus.StaffConflict c) => c.Core.Any(it => it is ConstraintMus.RangeCap { Hi: 0 });
        public int FloorCount => WishConflicts.Count + ImpossibleWishes.Count + ForcedShortfalls.Count +
            DayProofs.Count + StaffProofs.Count;
        /// <summary>利用者決定 2026-09-28: どちらかの節に 1 件でもあるときだけシートを出す。</summary>
        public bool NeedsSheet => FloorCount > 0 || RerunClears.Count > 0;
    }

    public static Summary Build(MagiState state, int[][] schedule)
    {
        var p = ScheduleUtil.CachedProblem(state);
        var s = ScheduleUtil.NormalizeSchedule(schedule, p);
        var dayProofs = ConstraintMus.AnalyzeDayConflicts(p).Where(c => HasWish(c.Core)).OrderBy(c => c.Day).ToList();
        var strict = ConstraintMus.DayProofsWithoutZeroCap(p).ToHashSet();
        var forced = V6SanityPort.ForcedCovU(state, p);
        return new Summary(
            V6SanityPort.WishSelfConflicts(p),
            V6SanityPort.DetectImpossibleWishes(state, p),
            forced,
            dayProofs,
            ConstraintMus.AnalyzeStaffConflicts(p).Where(c => HasWish(c.Core)).OrderBy(c => c.Staff).ToList(),
            WishOverCaps(p),
            HandPlacedCells(p, s),
            WallHintOf(RelaxTrial.UpperZeroWalls(state)),
            dayProofs.Select(c => c.Day).Where(d => !strict.Contains(d)).ToHashSet(),
            forced.Where(f => V6SanityPort.ZeroCapInShortfall(p, f)).Select(f => f.ShiftIndex).ToHashSet());
    }

    private static bool HasWish(IReadOnlyList<ConstraintMus.Item> core) => core.Any(it => it is ConstraintMus.WishPin);

    public static List<WishOverCap> WishOverCaps(Problem p)
    {
        var o = new List<WishOverCap>();
        for (var i = 0; i < p.S; i++)
            for (var k = 0; k < p.K; k++)
            {
                var hi = p.RangeHi[i][k];
                if (hi == int.MaxValue || !p.CanDo(i, k)) continue;
                var wished = 0;
                for (var j = 0; j < p.T; j++) if (p.WishFixed(i, j) && p.Wish[i][j] == k) wished++;
                if (wished > hi) o.Add(new WishOverCap(i, k, wished, hi));
            }
        return o;
    }

    /// <summary><see cref="V6SanityPort.HandPlacedUpperZeroIssue"/> と同じ判定のセル一覧（日→職員の順）。</summary>
    public static List<HandPlacedCell> HandPlacedCells(Problem p, int[][] s)
    {
        var o = new List<HandPlacedCell>();
        for (var j = 0; j < p.T; j++)
            for (var i = 0; i < Math.Min(p.S, s.Length); i++)
            {
                if (j >= s[i].Length) continue;
                var k = s[i][j];
                if (k < 0 || k >= p.K || k == p.RestIdx || !p.CanDo(i, k) || p.RangeHi[i][k] != 0) continue;
                if (p.WishFixed(i, j) && p.Wish[i][j] == k) continue;
                o.Add(new HandPlacedCell(i, j, k));
            }
        return o;
    }

    public static WallHint? WallHintOf(IReadOnlyList<(int Staff, int Shift)> walls)
    {
        if (walls.Count == 0) return null;
        var top = walls.GroupBy(w => w.Staff).Select(g => (Staff: g.Key, N: g.Count()))
            .OrderByDescending(x => x.N).ThenBy(x => x.Staff).ToList();
        return new WallHint(walls.Count, top.Count, top[0].Staff, top[0].N);
    }

    /// <summary>設定・希望・盤面の指紋。「このままつくる」の後は、これが変わるまで同じ理由で止めない。</summary>
    public static long Fingerprint(MagiState state, int[][] schedule)
    {
        var h = StateFingerprint.Of(state);
        unchecked
        {
            foreach (var row in schedule) foreach (var v in row) h = h * 31L + v;
        }
        return h;
    }
}
