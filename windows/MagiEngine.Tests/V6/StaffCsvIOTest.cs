using MagiEngine.Model;
using MagiEngine.V6;
// System.Range (built-in C# 8+ slice type, brought into scope by the SDK's implicit
// `global using global::System;`) collides by simple name with MagiEngine.Model.Range.
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [フェーズ7ピース11] <see cref="StaffCsvIO"/> の移植元テストの抽出。
///
///  - <c>ReviewFixes3442Test.kt</c>（76行・2テスト）を丸ごと移植（H3: CSVで追加した職員の空き日を、
///    休を担当できない群でも「その群が実際に担当できるシフト」で埋める。旧実装は休の記号解決だけで
///    担当可否を見ておらず、追加行の全日が groupViol(HARD 10000) になっていた）。
///  - <c>ReviewFixes3410Test.kt</c> の <c>unknownGroupAndSkillSymbolsInStaffCsvAreRecorded</c>
///    （I-07: 未知グループ/スキル記号を件数付きで記録する）のみを抽出。同ファイルの他のテストは
///    <see cref="ScheduleCsvBridge"/>（<c>ScheduleCsvBridgeTest.cs</c> で移植済み）や本フェーズ対象外
///    （<c>Ws1Ops</c>/<c>MagiViewModel</c> 依存＝ Android 層）のため対象外。
/// </summary>
public class StaffCsvIOTest
{
    /// <summary>群 G0 は X しか担当できない（休は担当外）。休は index1＝旧実装との差が観測できる。</summary>
    private static MagiState StRestNotAllowed() => new(
        StartDate: "2026-01-01", EndDate: "2026-01-03",
        Shifts: new List<Shift> { new("X", "X", "", ""), new("休", "休", "", "") },
        Groups: new List<Group> { new("G0", "G0") },
        StaffList: new List<Staff> { new("s0", 0) },
        Use2Patterns: false,
        GroupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 0 } },   // X=可 / 休=不可
        GroupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" } },
        Schedule: new List<IReadOnlyList<int>> { new List<int> { 0, 0, 0 } },
        Wishes: new Dictionary<string, int>(), StaffRange: new Dictionary<string, Range>(),
        NeedDay1: new Dictionary<string, string>(), NeedDay2: new Dictionary<string, string>(),
        Cons1: new List<C1Row>(), Cons2: new List<C2Row>(),
        Cons3: new List<C3Row>(), Cons3n: new List<C3Row>(), Cons3m: new List<C3Row>(), Cons3mn: new List<C3Row>(),
        Cons41: new List<C41Row>(), Cons42: new List<C42Row>(),
        SkillGroups: new List<Group>(), Cons41s: new List<C41Row>(), Cons42s: new List<C42Row>(),
        ShiftColors: new Dictionary<string, string>(), Extras: new Dictionary<string, System.Text.Json.JsonElement>());

    /// <summary>
    /// H3: CSV で追加した職員の空き日は「その群が担当できるシフト」で埋める。
    ///
    /// 旧実装は休の記号解決だけで担当可否を見ておらず、休を担当可否から外した群（UI の担当可否チップで
    /// 実際にできる操作）へ CSV で職員を足すと、追加行の全日が groupViol(HARD 10000) になっていた。
    /// 31日なら1回の取込で必須違反31件。3.418.0 が Ws1Ops の3経路で直した穴の、CSV 側の取り残し。
    /// </summary>
    [Fact]
    public void CsvUpsertFillsNewStaffRowWithAnAllowedShift()
    {
        var st = StRestNotAllowed();
        var sched = new[] { new[] { 0, 0, 0 } };
        var r = StaffCsvIO.ParseUpsert("氏名,グループ\n新人,G0\n", st, sched)!;
        Assert.Equal(1, r.Added);   // 1名が追加される

        var row = r.Schedule[1];
        Assert.Equal(3, row.Length);   // 期間ぶんの行ができる
        var rest = ScheduleUtil.RestShiftIndex(st);
        Assert.Equal(1, rest);   // 休は index1（旧実装との差が観測できる構成）
        foreach (var cell in row)
            Assert.Equal(0, cell);   // 空き日は担当できる X で埋まる（旧実装は休＝担当外だった）

        // 実際に必須違反が出ないことまで見る（この修正の目的そのもの）。
        var rep = UnifiedViolationChecker.Check(r.State, r.Schedule);
        Assert.Equal(0, rep.Breakdown.GetValueOrDefault("groupViol", 0));   // 追加行が担当外シフトで埋まっていない
    }

    /// <summary>休を担当できる群では従来どおり休で埋まる（3.418.0 の意味論を後退させない）。</summary>
    [Fact]
    public void CsvUpsertStillPrefersRestWhenTheGroupCanTakeIt()
    {
        var st = StRestNotAllowed() with { GroupShift = new List<IReadOnlyList<int>> { new List<int> { 1, 1 } } };
        var sched = new[] { new[] { 0, 0, 0 } };
        var r = StaffCsvIO.ParseUpsert("氏名,グループ\n新人,G0\n", st, sched)!;
        var row = r.Schedule[1];
        var rest = ScheduleUtil.RestShiftIndex(st);
        Assert.True(row.All(cell => cell == rest));   // 全日が休
    }

    /// <summary>
    /// [3.413.0/I-07 移植元] 職員一覧CSV の未知グループ/スキル記号を記録する。
    ///
    /// 旧: 新規は先頭グループ・既存は現状維持へ黙って落ちており、空欄（指定なし）と誤記が見分けられ
    /// なかった。所属グループは担当できるシフトを決めるので、誤記が通ると「なぜこの人がこの勤務に
    /// 入るのか」が説明できない盤面になる。
    /// </summary>
    [Fact]
    public void UnknownGroupAndSkillSymbolsInStaffCsvAreRecorded()
    {
        var st = new MagiState(
            StartDate: "2026-06-01", EndDate: "2026-06-02",
            Shifts: new List<Shift> { new("休", "休", "", ""), new("A", "A", "", "") },
            Groups: new List<Group> { new("G1", "G1") },
            StaffList: new List<Staff> { new("既存", 0, 0) },
            Use2Patterns: false,
            GroupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
            GroupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" } },
            Schedule: new List<IReadOnlyList<int>> { new List<int> { 0, 0 } },
            Wishes: new Dictionary<string, int>(), StaffRange: new Dictionary<string, Range>(),
            NeedDay1: new Dictionary<string, string>(), NeedDay2: new Dictionary<string, string>(),
            Cons1: new List<C1Row>(), Cons2: new List<C2Row>(),
            Cons3: new List<C3Row>(), Cons3n: new List<C3Row>(), Cons3m: new List<C3Row>(), Cons3mn: new List<C3Row>(),
            Cons41: new List<C41Row>(), Cons42: new List<C42Row>(),
            SkillGroups: new List<Group> { new("S1", "S1") }, Cons41s: new List<C41Row>(), Cons42s: new List<C42Row>(),
            ShiftColors: new Dictionary<string, string>(), Extras: new Dictionary<string, System.Text.Json.JsonElement>());
        var sched = new[] { new[] { 0, 0 } };
        var csv = "氏名,グループ,スキル\n新人,ZZ,QQ\n既存,ZZ,S1\n別人,G1,\n";
        var r = StaffCsvIO.ParseUpsert(csv, st, sched);
        Assert.NotNull(r);   // 取込自体は成功する
        Assert.Equal(2, r!.UnknownGroups["ZZ"]);   // 未知グループ ZZ は2件（新規1＋既存1）
        Assert.Equal(1, r.UnknownSkills["QQ"]);    // 未知スキル QQ は1件
        Assert.False(r.UnknownGroups.ContainsKey("G1"));   // 既知の G1 は未知に数えない
        Assert.False(r.UnknownSkills.ContainsKey(""));     // 空欄は未知に数えない（指定なしと誤記を区別する）
        // 挙動そのものは不変: 新規は先頭グループ、既存は元のまま。
        Assert.Equal(0, r.State.StaffList.First(s => s.Name == "新人").GroupIdx);   // 新規は先頭グループ
        Assert.Equal(0, r.State.StaffList.First(s => s.Name == "既存").GroupIdx);   // 既存の所属は維持
    }
}
