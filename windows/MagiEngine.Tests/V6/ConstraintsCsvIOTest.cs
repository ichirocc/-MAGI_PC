using MagiEngine.Model;
using MagiEngine.V6;
// System.Range (built-in C# 8+ slice type, brought into scope by the SDK's implicit
// `global using global::System;`) collides by simple name with MagiEngine.Model.Range.
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [フェーズ7ピース11] <see cref="ConstraintsCsvIO"/> の移植元テストの抽出（Kotlin原本
/// <c>SessionRegressionTest.kt</c>）。同ファイルの他のテストは <see cref="V6FinalPort"/>/
/// <see cref="V6SanityPort"/>/<see cref="WishesCsvIO"/>/<c>Ws1Ops</c>/
/// <see cref="UnifiedViolationChecker"/>（重い族マーク）向けで、それぞれ別のテストファイルの
/// 対象かフェーズ対象外（Android 層）のためここでは対象外。<c>CsvState()</c> は
/// <c>WishesCsvIOTest.cs</c> にも同じ内容で重複させている（Kotlin原本の1ファイル複数
/// テスト対象を、対象クラスごとにC#テストファイルへ分ける本フェーズの方針＝
/// <c>ScheduleCsvBridgeTest.cs</c>/<c>RosterCsvImportTest.cs</c> と同型）。
/// </summary>
public class ConstraintsCsvIOTest
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
    public void HeaderlessConstraintsCsvKeepsFirstRow()
    {
        var st = CsvState();
        // ヘッダ無し: 先頭行も実データ（連勤）→ 2件とも取り込まれる
        var headerless = ConstraintsCsvIO.Parse("連勤,2,休,1\n回数下限,A,3", st);
        Assert.NotNull(headerless);
        Assert.Equal(2, headerless!.Accepted);
        Assert.Equal(0, headerless.Rejected);   // [3.329.0] 読めない行は無い
        Assert.Single(headerless.State.Cons1);
        Assert.Single(headerless.State.Cons2);
        // ヘッダ有り: 従来どおりヘッダは落ちる
        var withHeader = ConstraintsCsvIO.Parse("種別,a,b,c,d,e\n連勤,2,休,1", st);
        Assert.NotNull(withHeader);
        Assert.Equal(1, withHeader!.Accepted);
    }

    /// <summary>
    /// [3.333.0/外部レビュー Critical 移植元] 種別が既知なだけの行を無条件に受理していた。
    /// <c>連勤,,,</c> は C1Row("","","") として件数に数えられるが <see cref="Problem"/> は捨てる＝
    /// **評価されない行で既存の有効な制約を全置換**できた（実質「制約なし」で最適化される）。
    /// </summary>
    [Fact]
    public void ConstraintsCsvRejectsStructurallyUnusableRows()
    {
        var st = CsvState();
        var empty = ConstraintsCsvIO.Parse("連勤,2,休,1\n連勤,,,", st);
        Assert.NotNull(empty);
        Assert.Equal(2, empty!.Accepted);   // 読める行は従来どおり数える
        Assert.Equal(1, empty.Rejected);    // 評価されない行を数える

        // 群・スキル群も同じ（記号が今のデータに無い＝その行は一切効かない）。
        var unknownGroup = ConstraintsCsvIO.Parse("群回数,ZZ,A,0,1", st);
        Assert.Equal(1, unknownGroup!.Rejected);

        // 連続パターンの未解決記号は別リスト(C3UnknownShift)に入るので、そちらも見ていることの確認。
        var unknownShift = ConstraintsCsvIO.Parse("禁止連続,休,ZZ", st);
        Assert.Equal(1, unknownShift!.Rejected);

        // 正常な行しかなければ従来どおり 0。
        var clean = ConstraintsCsvIO.Parse("群回数,G,A,0,1\n禁止連続,休,A", st);
        Assert.Equal(2, clean!.Accepted);
        Assert.Equal(0, clean.Rejected);
    }

    /// <summary>
    /// [3.336.0/外部レビュー P2 移植元] <c>MUST連続,休,,A</c> は空セルで打ち切られ ["休"] になり、
    /// **A が黙って消えたまま accepted に数えられて**いた（3.333.0 の「評価されない行を受理しない」の
    /// 取り残し）。
    /// </summary>
    [Fact]
    public void ConstraintsCsvRejectsPatternWithAGap()
    {
        var st = CsvState();
        var gap = ConstraintsCsvIO.Parse("MUST連続,休,,A", st);
        Assert.Equal(0, gap!.Accepted);   // 穴あきの並びは取り込まない
        Assert.Equal(1, gap.Rejected);
        Assert.Empty(gap.State.Cons3);
        // 末尾が空なのは正常（並びは可変長）。
        var ok = ConstraintsCsvIO.Parse("MUST連続,休,A", st);
        Assert.Equal(1, ok!.Accepted);
        Assert.Equal(0, ok.Rejected);
        Assert.Equal(new List<string> { "休", "A" }, ok.State.Cons3[0].Pattern);
    }

    /// <summary>H-02: 種別の綴り違いで制約一式が消えるのを防ぐ。</summary>
    [Fact]
    public void ConstraintsImportRejectsUnknownKindInsteadOfWipingEverything()
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
            Wishes: new Dictionary<string, int>(), StaffRange: new Dictionary<string, Range>(),
            NeedDay1: new Dictionary<string, string>(), NeedDay2: new Dictionary<string, string>(),
            Cons1: new List<C1Row>(), Cons2: new List<C2Row>(),
            Cons3: new List<C3Row>(), Cons3n: new List<C3Row>(), Cons3m: new List<C3Row>(), Cons3mn: new List<C3Row>(),
            Cons41: new List<C41Row>(), Cons42: new List<C42Row>(),
            SkillGroups: new List<Group>(), Cons41s: new List<C41Row>(), Cons42s: new List<C42Row>(),
            ShiftColors: new Dictionary<string, string>(), Extras: new Dictionary<string, System.Text.Json.JsonElement>());
        var r = ConstraintsCsvIO.Parse("連勤,2,休,1\n連勤日数,2,休,1", st);
        Assert.Equal(1, r!.Accepted);
        Assert.Equal(1, r.Rejected);   // 未知の種別を数える
        // 氏名・記号が解決できない個人レンジも同じ扱い。
        var r2 = ConstraintsCsvIO.Parse("個人レンジ,太郎,A,1,2", st);
        Assert.Equal(0, r2!.Accepted);
        Assert.Equal(1, r2.Rejected);
    }
}
