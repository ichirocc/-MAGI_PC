using MagiEngine.Model;
using MagiEngine.V6;
// System.Range (built-in C# 8+ slice type, brought into scope by the SDK's implicit
// `global using global::System;`) collides by simple name with MagiEngine.Model.Range.
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [フェーズ7ピース11] <see cref="WishesCsvIO"/> の移植元テストの抽出（Kotlin原本
/// <c>SessionRegressionTest.kt</c>）。同ファイルの他のテストは <see cref="V6FinalPort"/>/
/// <see cref="V6SanityPort"/>/<see cref="ConstraintsCsvIO"/>/<c>Ws1Ops</c>/
/// <see cref="UnifiedViolationChecker"/>（重い族マーク）向けで、それぞれ別のテストファイルの
/// 対象かフェーズ対象外（Android 層）のためここでは対象外。<c>CsvState()</c> は
/// <c>ConstraintsCsvIOTest.cs</c> にも同じ内容で重複させている（Kotlin原本の1ファイル複数
/// テスト対象を、対象クラスごとにC#テストファイルへ分ける本フェーズの方針＝
/// <c>ScheduleCsvBridgeTest.cs</c>/<c>RosterCsvImportTest.cs</c> と同型）。
/// </summary>
public class WishesCsvIOTest
{
    private static MagiState CsvState() => new(
        StartDate: "2026-06-01", EndDate: "2026-06-06",
        Shifts: new List<Shift> { new("休", "休", "", ""), new("A", "A", "1", "") },
        Groups: new List<Group> { new("G", "G") },
        StaffList: new List<Staff> { new("花子", 0) },
        Use2Patterns: false,
        GroupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
        GroupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" } },
        Schedule: new List<IReadOnlyList<int>> { new List<int> { 0, 0, 0, 0, 0, 0 } },
        Wishes: new Dictionary<string, int>(), StaffRange: new Dictionary<string, Range>(),
        NeedDay1: new Dictionary<string, string>(), NeedDay2: new Dictionary<string, string>(),
        Cons1: new List<C1Row>(), Cons2: new List<C2Row>(),
        Cons3: new List<C3Row>(), Cons3n: new List<C3Row>(), Cons3m: new List<C3Row>(), Cons3mn: new List<C3Row>(),
        Cons41: new List<C41Row>(), Cons42: new List<C42Row>(),
        SkillGroups: new List<Group>(), Cons41s: new List<C41Row>(), Cons42s: new List<C42Row>(),
        ShiftColors: new Dictionary<string, string>(), Extras: new Dictionary<string, System.Text.Json.JsonElement>());

    [Fact]
    public void HeaderlessWishesCsvKeepsFirstRow()
    {
        var st = CsvState();
        var headerless = WishesCsvIO.Parse("花子,1,A\n花子,2,休", st);
        Assert.NotNull(headerless);
        Assert.Equal(2, headerless!.Accepted);
        Assert.Equal(0, headerless.Rejected);   // [3.329.0] 読めない行は無い
        var withHeader = WishesCsvIO.Parse("氏名,日,希望シフト\n花子,1,A", st);
        Assert.NotNull(withHeader);
        Assert.Equal(1, withHeader!.Accepted);
    }

    /// <summary>H-02: 希望CSVは既存を全置換する。読めない行を黙って捨てると、その分の希望が消える。</summary>
    [Fact]
    public void ComponentImportReportsUnreadableRowsInsteadOfDroppingThem()
    {
        var st = new MagiState(
            StartDate: "2026-08-01", EndDate: "2026-08-03",
            Shifts: new List<Shift> { new("休", "休", "0", ""), new("A", "A", "0", "") },
            Groups: new List<Group> { new("G", "G") },
            StaffList: new List<Staff> { new("花子", 0) },
            Use2Patterns: false,
            GroupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
            GroupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" } },
            Schedule: new List<IReadOnlyList<int>> { new List<int> { 0, 0, 0 } },
            Wishes: new Dictionary<string, int> { ["0,0"] = 1, ["0,1"] = 0 }, StaffRange: new Dictionary<string, Range>(),
            NeedDay1: new Dictionary<string, string>(), NeedDay2: new Dictionary<string, string>(),
            Cons1: new List<C1Row>(), Cons2: new List<C2Row>(),
            Cons3: new List<C3Row>(), Cons3n: new List<C3Row>(), Cons3m: new List<C3Row>(), Cons3mn: new List<C3Row>(),
            Cons41: new List<C41Row>(), Cons42: new List<C42Row>(),
            SkillGroups: new List<Group>(), Cons41s: new List<C41Row>(), Cons42s: new List<C42Row>(),
            ShiftColors: new Dictionary<string, string>(), Extras: new Dictionary<string, System.Text.Json.JsonElement>());
        // 1行は有効、2行は誤記（未知の氏名・未知の記号）。
        var r = WishesCsvIO.Parse("花子,1,A\n太郎,1,A\n花子,2,Z", st);
        Assert.Equal(1, r!.Accepted);   // 有効行
        Assert.Equal(2, r.Rejected);    // 読めない行を数える
        Assert.NotEmpty(r.Sample);      // どこが悪いか示す
        // 全部読める場合は従来どおり置換できる。
        var ok = WishesCsvIO.Parse("花子,1,A\n花子,2,休", st);
        Assert.Equal(0, ok!.Rejected);
        Assert.Equal(2, ok.Accepted);
    }
}
