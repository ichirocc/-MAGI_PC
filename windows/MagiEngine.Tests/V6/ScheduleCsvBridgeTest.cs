using MagiEngine.Model;
using MagiEngine.V6;
// System.Range (built-in C# 8+ slice type, brought into scope by the SDK's implicit
// `global using global::System;`) collides by simple name with MagiEngine.Model.Range.
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [フェーズ7ピース10] Kotlin原本 <c>ReviewFixes3410Test.kt</c> のうち <see cref="ScheduleCsvBridge"/>
/// のみを対象にする <c>unclosedQuoteInScheduleCsvIsFlaggedInsteadOfSilentlyTruncated</c> の移植。
///
/// 同ファイルの他のテスト（<c>StaffCsvIO.parseUpsert</c> 対象など）はフェーズ7ピース11以降のスコープの
/// ため今回は対象外（<c>ScheduleCsvBridge</c>/<c>RosterCsvImport</c>/<c>FlatRosterCsvImport</c> を
/// 参照するのはこのテストのみ、と grep で確認済み）。
///
/// [3.413.0/I-08 移植元] 引用符が閉じないまま入力が終わっても検出せず、開いた引用符以降の全文が
/// 1セルへ吸い込まれ残りの行が丸ごと消えるのに、呼出側からは「短いCSV／氏名不一致」と区別が付かなかった。
/// </summary>
public class ScheduleCsvBridgeTest
{
    private static MagiState BuildState() => new(
        StartDate: "2026-06-01", EndDate: "2026-06-02",
        Shifts: new List<Shift> { new("休", "休", "", ""), new("A", "A", "", "") },
        Groups: new List<Group> { new("G1", "G1") },
        StaffList: new List<Staff> { new("職員A", 0), new("職員B", 0) },
        Use2Patterns: false,
        GroupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
        GroupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" } },
        Schedule: new List<IReadOnlyList<int>> { new List<int> { 0, 0 }, new List<int> { 0, 0 } },
        Wishes: new Dictionary<string, int>(), StaffRange: new Dictionary<string, Range>(),
        NeedDay1: new Dictionary<string, string>(), NeedDay2: new Dictionary<string, string>(),
        Cons1: new List<C1Row>(), Cons2: new List<C2Row>(),
        Cons3: new List<C3Row>(), Cons3n: new List<C3Row>(), Cons3m: new List<C3Row>(), Cons3mn: new List<C3Row>(),
        Cons41: new List<C41Row>(), Cons42: new List<C42Row>(),
        SkillGroups: new List<Group>(), Cons41s: new List<C41Row>(), Cons42s: new List<C42Row>(),
        ShiftColors: new Dictionary<string, string>(), Extras: new Dictionary<string, System.Text.Json.JsonElement>());

    [Fact]
    public void UnclosedQuoteInScheduleCsvIsFlaggedInsteadOfSilentlyTruncated()
    {
        var st = BuildState();
        var baseSchedule = new[] { new[] { 0, 0 }, new[] { 0, 0 } };
        var good = "スタッフ \\ 日付,1,2\n職員A,A,休\n職員B,休,A\n";
        var ok = ScheduleCsvBridge.Parse(good, st, baseSchedule);
        Assert.Equal(2, ok.Matched);
        Assert.False(ok.UnclosedQuote, "正常なCSVで旗は立たない");
        Assert.Equal(1, ok.Schedule[0][0]);

        // 職員A の行で引用符を開いたまま閉じない → 以降（職員B の行を含む）が1セルへ吸い込まれる。
        var bad = "スタッフ \\ 日付,1,2\n職員A,\"A,休\n職員B,休,A\n";
        var ng = ScheduleCsvBridge.Parse(bad, st, baseSchedule);
        Assert.True(ng.UnclosedQuote, "引用符が閉じないことを検出する");
        Assert.True(ng.Matched < 2, "実際に行が失われている（一致が減る）");
    }
}
