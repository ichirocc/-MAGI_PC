using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// [フェーズ7ピース16・最終ピース] <c>V6SanityPort.kt</c> の残り全て（1-80行の
/// <see cref="ShiftCountDiagnostic"/>/<see cref="V6SanityReport"/>、81-137行の
/// <see cref="V6SanityPort.Build"/> 統括関数、1354-1443行の schedule 依存プライベートヘルパー群
/// <c>buildLoadDataBitSummary</c>/<c>buildLoadDataBitDetails</c>/<c>buildShiftCountDiagnostic</c>/
/// <c>invalidAssignmentCells</c>/<c>badStaffRanges</c>/<c>impossibleDemandDays</c>）を移植。
/// これで <c>V6SanityPort.kt</c> は全面移植完了（<c>V6SanityPort.cs</c>=piece 4／
/// <c>V6SanityPort.Core.cs</c>=piece 2／<c>V6SanityPort.ViolationDebug.cs</c>=piece 12／
/// <c>ConstraintMus.cs</c>=piece 13／<c>V6SanityPort.Guidance.cs</c>=piece 14/15／本ファイル=piece 16）。
///
/// <see cref="Build"/> は他の全てのピースを1つの <see cref="V6SanityReport"/> へ集約する統括関数
/// （依存: piece 2・12・13・14/15）。プライベートヘルパー6件は本ファイルにのみ属し、この関数からしか
/// 呼ばれないため <c>private</c> のまま（Kotlin原本と可視性を一致させる）。
///
/// 1点、意図的にKotlinのコメントをそのまま残した箇所がある: <c>impossibleDemandDays</c> は
/// <see cref="V6SanityPort.EffectiveDemand"/>（<c>CovUCell(k,j,0)</c> 委譲、piece 2で
/// need2単独定義セルの見落としを解消済み）を使うため、この関数自体はその穴を持たない
/// （Kotlin側コメント「検査3と同じ穴」は歴史的な注記であり、既に該当関数へ委譲済みという事実は不変）。
/// </summary>
public sealed record ShiftCountDiagnostic(
    int StaffIndex,
    string StaffName,
    string ShiftSymbol,
    int Count,
    int? Lo,
    int? Hi,
    string Status);

public sealed record V6SanityReport(
    bool Ok,
    IReadOnlyList<string> Warns,
    IReadOnlyList<string> Notes,
    string LoadDataBitSummary,
    IReadOnlyList<string> LoadDataBitDetails,
    IReadOnlyList<ShiftCountDiagnostic> ShiftCountDiagnostics,
    IReadOnlyList<ImpossibleWish> ImpossibleWishes,
    IReadOnlyList<string> DuplicateSeqConstraints,
    IReadOnlyList<SettingIssue> Guidance);

public static partial class V6SanityPort
{
    /// <summary>
    /// [Kotlin原本 build()、V6SanityPort.kt:81-137] 診断の統括入口。<paramref name="schedule"/> を
    /// 省略すると <c>state.Schedule.ToIntArray2D()</c> を使う（Kotlinのデフォルト引数と同じ意味論）。
    /// </summary>
    public static V6SanityReport Build(MagiState state, int[][]? schedule = null)
    {
        schedule ??= state.Schedule.ToIntArray2D();
        var p = new Problem(state);
        var s = ScheduleUtil.NormalizeSchedule(schedule, p);
        var warns = new List<string>();
        var notes = new List<string>();

        var invalidAssignments = InvalidAssignmentCells(state, p, s);
        if (invalidAssignments.Count > 0)
            warns.Add($"担当不可または範囲外の配置が {invalidAssignments.Count} セルあります");

        var impossible = DetectImpossibleWishes(state, p);
        if (impossible.Count > 0)
            warns.Add($"実現不能な希望シフトが {impossible.Count} 件あります");

        var dup = FindDuplicateSeqConstraints(state);
        if (dup.Count > 0) warns.Add($"連続パターン制約の重複が {dup.Count} 件あります");

        var badRanges = BadStaffRanges(state, p);
        if (badRanges > 0) warns.Add($"staffRange の範囲外キーまたは lo>hi が {badRanges} 件あります");

        var impossibleDemand = ImpossibleDemandDays(state, p);
        if (impossibleDemand.Count > 0)
        {
            var head = new List<string>();
            var lim = Math.Min(4, impossibleDemand.Count);
            for (var idx = 0; idx < lim; idx++) head.Add(impossibleDemand[idx]);
            var suffix = impossibleDemand.Count > 4 ? " …" : "";
            warns.Add($"担当可能人数を超える需要があります: {string.Join(" / ", head)}{suffix}");
        }

        var aptSet = 0;
        foreach (var row in state.GroupShiftApt)
            foreach (var cell in row)
                if (cell.Trim().Length > 0) aptSet++;
        notes.Add($"groupShiftApt 適切回数: {aptSet} 件");
        notes.Add($"shifts={p.K} groups={p.G} staff={p.S} days={p.T}");
        notes.Add(state.Use2Patterns ? "2世代需要(セル毎OR/AND: #4b)が有効" : "需要はP1のみ");

        return new V6SanityReport(
            Ok: warns.Count == 0,
            Warns: warns,
            Notes: notes,
            LoadDataBitSummary: BuildLoadDataBitSummary(state, p, s),
            LoadDataBitDetails: BuildLoadDataBitDetails(state, p),
            ShiftCountDiagnostics: BuildShiftCountDiagnostic(state, p, s),
            ImpossibleWishes: impossible,
            DuplicateSeqConstraints: dup,
            Guidance: BuildGuidance(state, p));
    }

    private static string BuildLoadDataBitSummary(MagiState state, Problem p, int[][] schedule)
    {
        var assigned = 0;
        foreach (var row in schedule)
            foreach (var v in row)
                if (v >= 0 && v < p.K) assigned++;
        var possible = p.S * p.T;
        var allowBits = 0;
        for (var g = 0; g < p.G; g++)
            allowBits += g >= 0 && g < p.Bucket.Length ? p.Bucket[g].Length : 0;
        var wishCount = state.Wishes.Count;
        var rangeCount = state.StaffRange.Count;
        return $"LoadDataBit: staffN={p.S} termT={p.T} shiftK={p.K} assigned={assigned}/{possible} allowBits={allowBits} wishes={wishCount} ranges={rangeCount}";
    }

    private static List<string> BuildLoadDataBitDetails(MagiState state, Problem p)
    {
        var outList = new List<string>();
        for (var g = 0; g < p.G; g++)
        {
            var allowedParts = new List<string>();
            foreach (var k in p.Bucket[g])
                allowedParts.Add(k >= 0 && k < state.Shifts.Count ? state.Shifts[k].Kigou : k.ToString());
            var allowed = string.Join(" ", allowedParts);
            var members = 0;
            foreach (var staff in state.StaffList)
                if (staff.GroupIdx == g) members++;
            var groupSym = g >= 0 && g < state.Groups.Count ? state.Groups[g].Kigou : g.ToString();
            outList.Add($"Group {groupSym}: members={members} allowed=[{allowed}]");
        }
        return outList;
    }

    private static List<ShiftCountDiagnostic> BuildShiftCountDiagnostic(MagiState state, Problem p, int[][] schedule)
    {
        var counts = ScheduleUtil.CountMatrix(p, schedule);
        var outList = new List<ShiftCountDiagnostic>();
        for (var i = 0; i < p.S; i++)
        for (var k = 0; k < p.K; k++)
        {
            int? lo = p.RangeLo[i][k] != int.MinValue ? p.RangeLo[i][k] : null;
            int? hi = p.RangeHi[i][k] != int.MaxValue ? p.RangeHi[i][k] : null;
            if (lo == null && hi == null) continue;
            var n = counts[i][k];
            var status = lo != null && n < lo ? "LOW" : hi != null && n > hi ? "HIGH" : "OK";
            var staffName = i >= 0 && i < state.StaffList.Count ? state.StaffList[i].Name : $"#{i}";
            var shiftSym = k >= 0 && k < state.Shifts.Count ? state.Shifts[k].Kigou : k.ToString();
            outList.Add(new ShiftCountDiagnostic(i, staffName, shiftSym, n, lo, hi, status));
        }
        // Kotlin: compareBy { it.status != "LOW" && it.status != "HIGH" }.thenBy { it.staffIndex }
        // — LOW/HIGH の行(bool=false)を先に、OK(bool=true)を後に、それぞれ職員index順で。
        return outList
            .OrderBy(d => !(d.Status == "LOW" || d.Status == "HIGH"))
            .ThenBy(d => d.StaffIndex)
            .ToList();
    }

    private static List<string> InvalidAssignmentCells(MagiState state, Problem p, int[][] schedule)
    {
        var outList = new List<string>();
        for (var i = 0; i < p.S; i++)
        for (var j = 0; j < p.T; j++)
        {
            var k = schedule[i][j];
            if (k < 0 || k >= p.K) outList.Add($"{i},{j}=範囲外({k})");
            else if (!p.CanDo(i, k))
                outList.Add($"{i},{j}={(k >= 0 && k < state.Shifts.Count ? state.Shifts[k].Kigou : k.ToString())}");
        }
        return outList;
    }

    private static int BadStaffRanges(MagiState state, Problem p)
    {
        var bad = 0;
        foreach (var (key, r) in state.StaffRange)
        {
            var parts = key.Split(',');
            var i = parts.Length > 0 ? KotlinInterop.ToIntOrNull(parts[0]) : null;
            var k = parts.Length > 1 ? KotlinInterop.ToIntOrNull(parts[1]) : null;
            var lo = KotlinInterop.ToIntOrNull(r.Lo.Trim());
            var hi = KotlinInterop.ToIntOrNull(r.Hi.Trim());
            // Kotlin原本は2つの独立した if（else if でない）＝両方に該当すれば2回加算される。
            if (i == null || k == null || i < 0 || i >= p.S || k < 0 || k >= p.K) bad++;
            if (lo != null && hi != null && lo > hi) bad++;
        }
        return bad;
    }

    private static List<string> ImpossibleDemandDays(MagiState state, Problem p)
    {
        var outList = new List<string>();
        for (var j = 0; j < p.T; j++)
        for (var k = 0; k < p.K; k++)
        {
            var need = EffectiveDemand(p, k, j);
            if (need <= 0) continue;
            var capable = 0;
            for (var i = 0; i < p.S; i++)
                if (p.CanDo(i, k)) capable++;
            if (need > capable)
            {
                var sym = k >= 0 && k < state.Shifts.Count ? state.Shifts[k].Kigou : k.ToString();
                outList.Add($"{SafeDayLabel(state.StartDate, j)} {sym}: need={need} capable={capable}");
            }
        }
        return outList;
    }
}
