using System.Text.Json;
using MagiEngine.Model;
// System.Range (built-in C# 8+ slice type, brought into scope by the SDK's implicit
// `global using global::System;`) collides by simple name with MagiEngine.Model.Range.
// Same alias as MagiEngine.Tests/TestSupport/MinimalState.cs for the same reason.
using Range = MagiEngine.Model.Range;

namespace MagiApp.ViewModels.Tests.TestSupport;

/// <summary>
/// [フェーズ9] <c>MagiEngine.Tests/TestSupport/MinimalState.cs</c> と同じ役割・同じ既定形状を持つ、
/// このテストプロジェクト専用の複製。テストプロジェクトどうしでプロジェクト参照を張るのは通常の
/// 構成でないため（かつ張っても <c>internal</c> 可視性は越えられない）、意図的に複製する
/// （小さく・変更頻度が低いビルダーなので複製コストは低い）。
///
/// 既定形状：シフト2種（index0="休"、index1="A"）・グループ1つ（両方担当可）・職員2名・
/// 7日間の休のみ初期盤面・制約/希望/回数レンジは全て空。
/// </summary>
internal static class MinimalState
{
    public static readonly IReadOnlyDictionary<string, JsonElement> NoExtras =
        new Dictionary<string, JsonElement>();

    public static MagiState Build(
        string startDate = "2025-12-01",
        string endDate = "2025-12-07",
        IReadOnlyList<Shift>? shifts = null,
        IReadOnlyList<Group>? groups = null,
        IReadOnlyList<Staff>? staffList = null,
        bool use2Patterns = false,
        IReadOnlyList<IReadOnlyList<int>>? groupShift = null,
        IReadOnlyList<IReadOnlyList<string>>? groupShiftApt = null,
        IReadOnlyList<IReadOnlyList<int>>? schedule = null,
        IReadOnlyDictionary<string, int>? wishes = null,
        IReadOnlyDictionary<string, Range>? staffRange = null,
        IReadOnlyDictionary<string, string>? needDay1 = null,
        IReadOnlyDictionary<string, string>? needDay2 = null,
        IReadOnlyList<C1Row>? cons1 = null,
        IReadOnlyList<C2Row>? cons2 = null,
        IReadOnlyList<C3Row>? cons3 = null,
        IReadOnlyList<C3Row>? cons3n = null,
        IReadOnlyList<C3Row>? cons3m = null,
        IReadOnlyList<C3Row>? cons3mn = null,
        IReadOnlyList<C41Row>? cons41 = null,
        IReadOnlyList<C42Row>? cons42 = null,
        IReadOnlyList<Group>? skillGroups = null,
        IReadOnlyList<C41Row>? cons41s = null,
        IReadOnlyList<C42Row>? cons42s = null,
        IReadOnlyDictionary<string, string>? shiftColors = null)
    {
        var shifts2 = shifts ?? new List<Shift>
        {
            new("休", "休", "", ""),
            new("A", "A", "", ""),
        };
        const int t = 7;
        var schedule2 = schedule ?? Enumerable.Range(0, 2)
            .Select(_ => (IReadOnlyList<int>)Enumerable.Repeat(0, t).ToList())
            .ToList();
        var groups2 = groups ?? new List<Group> { new("G0", "G0") };
        var groupShift2 = groupShift
            ?? groups2.Select(_ => (IReadOnlyList<int>)shifts2.Select(_ => 1).ToList()).ToList();
        var staffList2 = staffList ?? new List<Staff> { new("職員A", 0), new("職員B", 0) };

        return new MagiState(
            StartDate: startDate,
            EndDate: endDate,
            Shifts: shifts2,
            Groups: groups2,
            StaffList: staffList2,
            Use2Patterns: use2Patterns,
            GroupShift: groupShift2,
            GroupShiftApt: groupShiftApt
                ?? groups2.Select(_ => (IReadOnlyList<string>)shifts2.Select(_ => "").ToList()).ToList(),
            Schedule: schedule2,
            Wishes: wishes ?? new Dictionary<string, int>(),
            StaffRange: staffRange ?? new Dictionary<string, Range>(),
            NeedDay1: needDay1 ?? new Dictionary<string, string>(),
            NeedDay2: needDay2 ?? new Dictionary<string, string>(),
            Cons1: cons1 ?? new List<C1Row>(),
            Cons2: cons2 ?? new List<C2Row>(),
            Cons3: cons3 ?? new List<C3Row>(),
            Cons3n: cons3n ?? new List<C3Row>(),
            Cons3m: cons3m ?? new List<C3Row>(),
            Cons3mn: cons3mn ?? new List<C3Row>(),
            Cons41: cons41 ?? new List<C41Row>(),
            Cons42: cons42 ?? new List<C42Row>(),
            SkillGroups: skillGroups ?? new List<Group>(),
            Cons41s: cons41s ?? new List<C41Row>(),
            Cons42s: cons42s ?? new List<C42Row>(),
            ShiftColors: shiftColors ?? new Dictionary<string, string>(),
            Extras: NoExtras
        );
    }

    /// <summary>Build() の既定形状（2職員×7日、全セル休=index0）に一致する初期盤面。</summary>
    public static int[][] BuildSchedule() => new[]
    {
        new[] { 0, 0, 0, 0, 0, 0, 0 },
        new[] { 0, 0, 0, 0, 0, 0, 0 },
    };
}
