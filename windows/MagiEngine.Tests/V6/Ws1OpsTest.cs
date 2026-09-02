using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;
// System.Range (built-in C# 8+ slice type) collides by simple name with MagiEngine.Model.Range —
// see the same alias pattern already established in TestSupport/MinimalState.cs.
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [フェーズ9] <c>Ws1Ops.cs</c>（<c>Ws1Ops.kt</c> の逐語移植）の回帰テスト。単一の <c>Ws1OpsTest.kt</c>
/// は Android 側に存在せず、3つの Kotlin テストファイルから対象を集約する:
///  - <c>Ws1OpsRefCountTest.kt</c>（参照カウント4件）
///  - <c>Ws1OpsAptTest.kt</c>（<c>SetGroupApt</c> 3件）
///  - <c>SessionRegressionTest.kt</c>（<c>Ws1Ops</c> 固有の8件）
///
/// うち <c>filledCellsAreAlwaysAShiftTheStaffMayActuallyWork</c> の4つ目の経路
/// （<c>Problem.InitialAssignment()</c> の範囲外/欠損セル穴埋め）は、その不変条件をフェーズ2の
/// <c>ProblemTest.cs</c>（<c>InitialAssignment_MissingCellFallsBackToBucketFirstWhenRestNotInBucket</c>
/// 等）が既により細かい粒度で網羅しているため、ここでは移植しない（<c>Ws1Ops</c> 固有の3経路のみ対象）。
/// </summary>
public class Ws1OpsTest
{
    // ---- Ws1OpsRefCountTest.kt: shiftRefCount/groupRefCount/skillGroupRefCount ----
    //
    // [3.429.0/R-03の由来をそのまま記録] 削除確認ダイアログへ渡す影響件数。
    // Problem.ShiftIdxOf/GroupIdxOf/SkillGroupIdxOf と同じ厳密一致(==)で数えることを固定する。

    private static MagiState RefCountState() => new MagiState(
        StartDate: "2026-07-01", EndDate: "2026-07-02",
        Shifts: new List<Shift> { new("日勤", "日", "", ""), new("休み", "休", "", ""), new("夜勤", "夜", "", "") },
        Groups: new List<Group> { new("A", "A"), new("B", "B") },
        StaffList: new List<Staff> { new("s1", 0) },
        Use2Patterns: false,
        GroupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 }, new List<int> { 1, 1, 1 } },
        GroupShiftApt: new List<IReadOnlyList<string>>(),
        Schedule: new List<IReadOnlyList<int>> { new List<int> { 0, 1 } },
        Wishes: new Dictionary<string, int>(),
        StaffRange: new Dictionary<string, Range>(),
        NeedDay1: new Dictionary<string, string>(),
        NeedDay2: new Dictionary<string, string>(),
        Cons1: new List<C1Row> { new("4", "日", "1") },
        Cons2: new List<C2Row> { new("日", "3"), new("休", "2") },
        Cons3: new List<C3Row> { new(new List<string> { "日", "夜" }) },
        Cons3n: new List<C3Row>(),
        Cons3m: new List<C3Row> { new(new List<string> { "休", "" }) },
        Cons3mn: new List<C3Row>(),
        Cons41: new List<C41Row> { new("A", "日", "1", "2"), new("B", "夜", "0", "1") },
        Cons42: new List<C42Row> { new("A", "B", "日", "夜") },
        SkillGroups: new List<Group> { new("リーダー", "L"), new("新人", "N") },
        Cons41s: new List<C41Row> { new("L", "日", "1", "1") },
        Cons42s: new List<C42Row> { new("L", "N", "夜", "休") },
        ShiftColors: new Dictionary<string, string>(),
        Extras: MinimalState.NoExtras
    );

    [Fact]
    public void ShiftRefCountSumsAllReferencingFamilies()
    {
        var s = RefCountState();
        // 「日」: cons1(1) + cons2(1) + cons3 pattern(1) + cons41(1) + cons42 s1(1) + cons41s(1) = 6
        Assert.Equal(6, Ws1Ops.ShiftRefCount(s, "日"));
        // 「夜」: cons3 pattern(1) + cons41(1) + cons42 s2(1) + cons42s s1(1) = 4
        Assert.Equal(4, Ws1Ops.ShiftRefCount(s, "夜"));
        // 「休」: cons2(1) + cons3m pattern(1) + cons42s s2(1) = 3
        Assert.Equal(3, Ws1Ops.ShiftRefCount(s, "休"));
    }

    [Fact]
    public void ShiftRefCountIsZeroForUnreferencedOrUnknownSymbol()
    {
        var b = RefCountState();
        var s = b with { Shifts = b.Shifts.Append(new Shift("A4", "A4", "", "")).ToList() };
        Assert.Equal(0, Ws1Ops.ShiftRefCount(s, "A4"));
        Assert.Equal(0, Ws1Ops.ShiftRefCount(s, "存在しない記号"));
    }

    [Fact]
    public void GroupRefCountSumsCons41And42Only()
    {
        var s = RefCountState();
        Assert.Equal(2, Ws1Ops.GroupRefCount(s, "A")); // cons41(1) + cons42 g1(1)
        Assert.Equal(2, Ws1Ops.GroupRefCount(s, "B")); // cons41(1) + cons42 g2(1)
        // スキル群 "L" は勤務グループの参照カウントには入らない（別分類）
        Assert.Equal(0, Ws1Ops.GroupRefCount(s, "L"));
    }

    [Fact]
    public void SkillGroupRefCountSumsCons41sAnd42sOnly()
    {
        var s = RefCountState();
        Assert.Equal(2, Ws1Ops.SkillGroupRefCount(s, "L")); // cons41s(1) + cons42s g1(1)
        Assert.Equal(1, Ws1Ops.SkillGroupRefCount(s, "N")); // cons42s g2(1)
        // 勤務グループ "A" はスキル群の参照カウントには入らない
        Assert.Equal(0, Ws1Ops.SkillGroupRefCount(s, "A"));
    }

    [Fact]
    public void ExactMatchDoesNotTrim()
    {
        // ShiftIdxOf は完全一致(==)なので trim しない。前後空白の別記号とは一致させない。
        var s = RefCountState();
        Assert.Equal(0, Ws1Ops.ShiftRefCount(s, " 日"));
        Assert.Equal(0, Ws1Ops.ShiftRefCount(s, "日 "));
    }

    // ---- Ws1OpsAptTest.kt: SetGroupApt ----

    private static MagiState AptState(IReadOnlyList<IReadOnlyList<string>>? apt = null) => new MagiState(
        StartDate: "2026-07-01", EndDate: "2026-07-02",
        Shifts: new List<Shift> { new("日勤", "日", "", ""), new("休み", "休", "", "") },
        Groups: new List<Group> { new("A", "A"), new("B", "B") },
        StaffList: new List<Staff> { new("s1", 0) },
        Use2Patterns: false,
        GroupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 }, new List<int> { 1, 1 } },
        GroupShiftApt: apt ?? new List<IReadOnlyList<string>>(),
        Schedule: new List<IReadOnlyList<int>> { new List<int> { 0, 1 } },
        Wishes: new Dictionary<string, int>(),
        StaffRange: new Dictionary<string, Range>(),
        NeedDay1: new Dictionary<string, string>(),
        NeedDay2: new Dictionary<string, string>(),
        Cons1: new List<C1Row>(),
        Cons2: new List<C2Row>(),
        Cons3: new List<C3Row>(),
        Cons3n: new List<C3Row>(),
        Cons3m: new List<C3Row>(),
        Cons3mn: new List<C3Row>(),
        Cons41: new List<C41Row>(),
        Cons42: new List<C42Row>(),
        SkillGroups: new List<Group>(),
        Cons41s: new List<C41Row>(),
        Cons42s: new List<C42Row>(),
        ShiftColors: new Dictionary<string, string>(),
        Extras: MinimalState.NoExtras
    );

    [Fact]
    public void NormalizesEmptyAptGridAndSetsCell()
    {
        var s = Ws1Ops.SetGroupApt(AptState(), 0, 0, "10");
        // 未初期化(空)でも G×K に正規化される
        Assert.Equal(2, s.GroupShiftApt.Count);
        Assert.Equal(2, s.GroupShiftApt[0].Count);
        Assert.Equal("10", s.GroupShiftApt[0][0]);
        Assert.Equal("", s.GroupShiftApt[0][1]);
        Assert.Equal("", s.GroupShiftApt[1][0]);
    }

    [Fact]
    public void TrimsValueAndIsOutOfRangeSafe()
    {
        var s = Ws1Ops.SetGroupApt(AptState(), 1, 1, "  3 ");
        Assert.Equal("3", s.GroupShiftApt[1][1]);
        // 範囲外は無変更。MagiState は record だが list/dict フィールドは参照等価のみ
        // （構造的一致は取れない＝MagiState.cs のドキュメントコメント参照）なので、
        // Kotlin の assertEquals(orig, result) に相当する検証は参照同一性で行う
        // （Ws1Ops.SetGroupApt の範囲外分岐は早期 return state; で同一参照を返す）。
        var orig = AptState();
        Assert.Same(orig, Ws1Ops.SetGroupApt(orig, 9, 0, "5"));
        Assert.Same(orig, Ws1Ops.SetGroupApt(orig, 0, 9, "5"));
    }

    [Fact]
    public void ClearingWithBlankKeepsGridShape()
    {
        var seeded = AptState(new List<IReadOnlyList<string>>
        {
            new List<string> { "10", "2" },
            new List<string> { "4", "" },
        });
        var s = Ws1Ops.SetGroupApt(seeded, 0, 0, "");
        Assert.Equal("", s.GroupShiftApt[0][0]);
        Assert.Equal("2", s.GroupShiftApt[0][1]);
        Assert.Equal("4", s.GroupShiftApt[1][0]);
    }

    // ---- SessionRegressionTest.kt: Ws1Ops-specific coverage --------------------
    //
    // [レビュー指摘P1(3.106.0)＋方針転換(3.416.0)] 休シフトも通常の編集規則。

    private static MagiState ThreeShiftState() => new MagiState(
        StartDate: "2026-06-01", EndDate: "2026-06-03",
        // 休が index0 でない配置（旧実装のハードコード0が露呈するケース）
        Shifts: new List<Shift> { new("A", "A", "1", ""), new("休", "休", "", ""), new("B", "B", "1", "") },
        Groups: new List<Group> { new("G", "G") },
        StaffList: new List<Staff> { new("s0", 0, 2) }, // skillIdx=2
        Use2Patterns: false,
        GroupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } },
        GroupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "", "" } },
        Schedule: new List<IReadOnlyList<int>> { new List<int> { 0, 1, 2 } },
        Wishes: new Dictionary<string, int>(),
        StaffRange: new Dictionary<string, Range>(),
        NeedDay1: new Dictionary<string, string>(),
        NeedDay2: new Dictionary<string, string>(),
        Cons1: new List<C1Row>(),
        Cons2: new List<C2Row>(),
        Cons3: new List<C3Row>(),
        Cons3n: new List<C3Row>(),
        Cons3m: new List<C3Row>(),
        Cons3mn: new List<C3Row>(),
        Cons41: new List<C41Row>(),
        Cons42: new List<C42Row>(),
        SkillGroups: new List<Group>(),
        Cons41s: new List<C41Row>(),
        Cons42s: new List<C42Row>(),
        ShiftColors: new Dictionary<string, string>(),
        Extras: MinimalState.NoExtras
    );

    [Fact]
    public void RemoveShiftMapsDeletedCellsToRest()
    {
        var st = ThreeShiftState();
        var sched = new[] { new[] { 0, 1, 2 } };
        // A(idx0) を削除: A のセルは休(削除後 idx0)へ、休(1)→0、B(2)→1 に追従（3.106.0 の本体＝
        // ハードコード0で勤務シフトへ化けるバグの回帰）
        var r = Ws1Ops.RemoveShift(st, sched, 0);
        Assert.Equal("休", r.State.Shifts[0].Kigou);
        Assert.Equal(new[] { 0, 0, 1 }, r.Schedule[0]);
    }

    /// <summary>
    /// [3.416.0/方針「休は通常のシフト定義」] 休シフト自体も他シフトと同じ規則で削除できる。
    /// 削除セルは削除後の一覧の既定シフト（「休」があればそれ、無ければ先頭）へ。
    /// 旧実装（3.106.0）はここを no-op で禁止していた＝この2件は方針転換の回帰ガード。
    /// </summary>
    [Fact]
    public void RemoveShiftAllowsDeletingTheRestShiftItself()
    {
        var st = ThreeShiftState(); // shifts = [A, 休, B]
        var sched = new[] { new[] { 0, 1, 2 } };
        var r = Ws1Ops.RemoveShift(st, sched, 1); // 休(idx1) を削除
        Assert.Equal(2, r.State.Shifts.Count);
        Assert.Equal(new[] { "A", "B" }, r.State.Shifts.Select(sh => sh.Kigou));
        // 削除後の一覧に「休」が無い＝既定は先頭(A=0)。休セル(1)→0、B(2)→1 へ追従。範囲外や-1は出ない。
        Assert.Equal(new[] { 0, 0, 1 }, r.Schedule[0]);
        Assert.All(r.Schedule[0], v => Assert.InRange(v, 0, 1));
    }

    /// <summary>
    /// 休が末尾indexのとき削除しても範囲外セルを作らない（旧式 <c>rest&gt;k ? rest-1 : rest</c> は
    /// k==rest の末尾削除で削除済みindexを指し、正規化で -1 センチネル＝必須違反化していた形）。
    /// </summary>
    [Fact]
    public void RemoveShiftDeletingTrailingRestStaysInBounds()
    {
        var st = ThreeShiftState() with
        {
            Shifts = new List<Shift> { new("A", "A", "1", ""), new("B", "B", "1", ""), new("休", "休", "", "") },
            Schedule = new List<IReadOnlyList<int>> { new List<int> { 0, 1, 2 } },
        };
        var sched = new[] { new[] { 0, 1, 2 } };
        var r = Ws1Ops.RemoveShift(st, sched, 2); // 末尾の休を削除
        Assert.Equal(new[] { "A", "B" }, r.State.Shifts.Select(sh => sh.Kigou));
        Assert.Equal(new[] { 0, 1, 0 }, r.Schedule[0]); // 休セルは先頭(A)へ
        Assert.All(r.Schedule[0], v => Assert.InRange(v, 0, 1));
    }

    /// <summary>
    /// [3.416.0] 休シフトの改名も通常経路＝制約参照（記号の文字列）が RenameShiftInConstraints で
    /// 追従し、「休」記号が消えた場合の既定シフト解決は先頭へ倒れる（検査2g が案内する既定挙動）。
    /// </summary>
    [Fact]
    public void EditShiftRenamingRestFollowsConstraintsLikeAnyShift()
    {
        var st = ThreeShiftState() with { Cons1 = new List<C1Row> { new("5", "休", "2") } };
        var r = Ws1Ops.EditShift(st, 1, "公休", "公", "", "");
        Assert.Equal("公", r.Shifts[1].Kigou);
        Assert.Equal("公", r.Cons1[0].ShiftKigou); // 窓ルールが改名へ追従＝同じシフトを指し続ける
        Assert.Equal(0, ScheduleUtil.RestShiftIndex(r)); // 「休」記号は消えた＝既定解決は先頭へ
    }

    [Fact]
    public void EditStaffPreservesSkillIdx()
    {
        var st = ThreeShiftState();
        var ns = Ws1Ops.EditStaff(st, 0, "改名した", 0);
        Assert.Equal("改名した", ns.StaffList[0].Name);
        Assert.Equal(2, ns.StaffList[0].SkillIdx); // 旧実装は 0 に化けていた
    }

    [Fact]
    public void AddStaffAndResizeFillWithResolvedRestShift()
    {
        // H-01: 休が index 0 でないデータ（先頭が勤務シフト）。新しい職員の行・伸ばした日は
        // index 0 ではなく休で埋まること。
        var st = new MagiState(
            StartDate: "2026-08-01", EndDate: "2026-08-02",
            Shifts: new List<Shift> { new("A", "A", "0", ""), new("B", "B", "0", ""), new("休", "休", "0", "") },
            Groups: new List<Group> { new("G", "G") },
            StaffList: new List<Staff> { new("s0", 0) },
            Use2Patterns: false,
            GroupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } },
            GroupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "", "" } },
            Schedule: new List<IReadOnlyList<int>> { new List<int> { 0, 1 } },
            Wishes: new Dictionary<string, int>(),
            StaffRange: new Dictionary<string, Range>(),
            NeedDay1: new Dictionary<string, string>(),
            NeedDay2: new Dictionary<string, string>(),
            Cons1: new List<C1Row>(), Cons2: new List<C2Row>(), Cons3: new List<C3Row>(), Cons3n: new List<C3Row>(),
            Cons3m: new List<C3Row>(), Cons3mn: new List<C3Row>(), Cons41: new List<C41Row>(), Cons42: new List<C42Row>(),
            SkillGroups: new List<Group>(), Cons41s: new List<C41Row>(), Cons42s: new List<C42Row>(),
            ShiftColors: new Dictionary<string, string>(), Extras: MinimalState.NoExtras
        );
        var rest = ScheduleUtil.RestShiftIndex(st);
        Assert.Equal(2, rest); // 前提: 休は index 2

        var added = Ws1Ops.AddStaff(st, st.Schedule.ToIntArray2D(), "s1", 0);
        Assert.All(added.Schedule[1], v => Assert.Equal(rest, v)); // 新しい職員の全日が休

        var grown = Ws1Ops.ResizeDays(st, st.Schedule.ToIntArray2D(), 4);
        Assert.Equal(rest, grown.Schedule[0][2]); // 伸ばした日は休
        Assert.Equal(1, grown.Schedule[0][1]); // 元の日は不変
    }

    /// <summary>
    /// [3.418.0] 空きマスを埋めるとき、その職員が担当できないシフトを置かない。旧実装は担当可否を
    /// 見ずに一律「休」で埋めていたため、担当可否から休を外した群（UIの担当可否チップで実際にできる
    /// 操作）に職員を足す／期間を伸ばすと、その全日が groupViol(HARD 重み10000) になった。埋めた
    /// 瞬間に必須違反が並ぶ。
    ///
    /// [注記] Kotlin原本の同名テストは4つ目の経路として <c>Problem.InitialAssignment()</c> の
    /// out-of-range/短い行の穴埋めも検証するが、その不変条件は既に <c>ProblemTest.cs</c>
    /// （フェーズ2、<c>InitialAssignment_MissingCellFallsBackToBucketFirstWhenRestNotInBucket</c> 等）
    /// がより細かい粒度で網羅済みのため、ここでは Ws1Ops 固有の3経路のみを対象にする。
    /// </summary>
    [Fact]
    public void FilledCellsAreAlwaysAShiftTheStaffMayActuallyWork()
    {
        var st = new MagiState(
            StartDate: "2026-08-01", EndDate: "2026-08-02",
            Shifts: new List<Shift> { new("A", "A", "0", ""), new("B", "B", "0", ""), new("休", "休", "0", "") },
            Groups: new List<Group> { new("G", "G") },
            StaffList: new List<Staff> { new("s0", 0) },
            Use2Patterns: false,
            GroupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 0 } }, // この群は休を担当できない
            GroupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "", "" } },
            Schedule: new List<IReadOnlyList<int>> { new List<int> { 0, 1 } },
            Wishes: new Dictionary<string, int>(),
            StaffRange: new Dictionary<string, Range>(),
            NeedDay1: new Dictionary<string, string>(),
            NeedDay2: new Dictionary<string, string>(),
            Cons1: new List<C1Row>(), Cons2: new List<C2Row>(), Cons3: new List<C3Row>(), Cons3n: new List<C3Row>(),
            Cons3m: new List<C3Row>(), Cons3mn: new List<C3Row>(), Cons41: new List<C41Row>(), Cons42: new List<C42Row>(),
            SkillGroups: new List<Group>(), Cons41s: new List<C41Row>(), Cons42s: new List<C42Row>(),
            ShiftColors: new Dictionary<string, string>(), Extras: MinimalState.NoExtras
        );
        Assert.Equal(2, ScheduleUtil.RestShiftIndex(st)); // 前提: 休は index 2 で、この群は担当できない
        Assert.False(new Problem(st).CanDo(0, 2)); // 前提: 群0 は休を担当できない

        var added = Ws1Ops.AddStaff(st, st.Schedule.ToIntArray2D(), "s1", 0);
        var pAdd = new Problem(added.State);
        Assert.All(added.Schedule[1], v => Assert.True(pAdd.CanDo(1, v))); // 追加した職員の全日が担当可能なシフト
        Assert.Equal(0, UnifiedViolationChecker.Check(added.State, added.Schedule).Breakdown["groupViol"]); // 担当外シフトを置いていない

        var grown = Ws1Ops.ResizeDays(st, st.Schedule.ToIntArray2D(), 4);
        var pGrow = new Problem(grown.State);
        Assert.All(grown.Schedule[0], v => Assert.True(pGrow.CanDo(0, v))); // 伸ばした日も担当可能なシフト
        Assert.Equal(1, grown.Schedule[0][1]); // 元の日は不変

        // 3つ目の埋め込み経路: シフト削除で空いたマス（s0 は day0 に A が入っている）。
        var removed = Ws1Ops.RemoveShift(st, st.Schedule.ToIntArray2D(), 0);
        var pRem = new Problem(removed.State);
        Assert.All(removed.Schedule[0], v => Assert.True(pRem.CanDo(0, v))); // 消したシフトのマスも担当可能なシフト
        Assert.Equal(0, UnifiedViolationChecker.Check(removed.State, removed.Schedule).Breakdown["groupViol"]); // 担当外シフトを置いていない
    }

    [Fact]
    public void RemovingSkillGroupLeavesMembersUnassignedNotInTheFirstGroup()
    {
        // [3.330.0/外部レビュー M-01] 削除した群の所属者を 0 へ寄せると、①無関係な先頭の群の制約が
        // 黙って掛かる ②最後の1群を消すと全員 0 になり、あとで群を足すと全員がそこに所属した扱い。
        var st = new MagiState(
            StartDate: "2026-08-01", EndDate: "2026-08-02",
            Shifts: new List<Shift> { new("休", "休", "0", ""), new("A", "A", "0", "") },
            Groups: new List<Group> { new("G", "G") },
            StaffList: new List<Staff>
            {
                new("s0", 0, 0), new("s1", 0, 1), new("s2", 0, 2), new("s3", 0, -1),
            },
            Use2Patterns: false,
            GroupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
            GroupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" } },
            Schedule: Enumerable.Range(0, 4).Select(_ => (IReadOnlyList<int>)new List<int> { 0, 0 }).ToList(),
            Wishes: new Dictionary<string, int>(),
            StaffRange: new Dictionary<string, Range>(),
            NeedDay1: new Dictionary<string, string>(),
            NeedDay2: new Dictionary<string, string>(),
            Cons1: new List<C1Row>(), Cons2: new List<C2Row>(), Cons3: new List<C3Row>(), Cons3n: new List<C3Row>(),
            Cons3m: new List<C3Row>(), Cons3mn: new List<C3Row>(), Cons41: new List<C41Row>(), Cons42: new List<C42Row>(),
            SkillGroups: new List<Group> { new("S0", "S0"), new("S1", "S1"), new("S2", "S2") },
            Cons41s: new List<C41Row>(), Cons42s: new List<C42Row>(),
            ShiftColors: new Dictionary<string, string>(), Extras: MinimalState.NoExtras
        );
        var after = Ws1Ops.RemoveSkillGroup(st, 1);
        Assert.Equal(2, after.SkillGroups.Count); // 群が1つ減る
        Assert.Equal(0, after.StaffList[0].SkillIdx); // 前の群は不変
        Assert.Equal(-1, after.StaffList[1].SkillIdx); // 削除された群の所属者は未所属(-1)
        Assert.Equal(1, after.StaffList[2].SkillIdx); // 後ろの群は1つ詰まる
        Assert.Equal(-1, after.StaffList[3].SkillIdx); // 元から未所属は不変

        // 最後の1群を消しても、あとで群を足したときに全員が所属した扱いにならないこと。
        var s2 = st;
        for (int g = st.SkillGroups.Count - 1; g >= 0; g--) s2 = Ws1Ops.RemoveSkillGroup(s2, g);
        Assert.All(s2.StaffList, p => Assert.Equal(-1, p.SkillIdx)); // 全員が未所属
        // 群の追加は skillGroups に1件足すだけ（MagiViewModel.AddSkillGroup と同じ操作）。
        var readded = s2 with { SkillGroups = s2.SkillGroups.Append(new Group("S9", "S9")).ToList() };
        Assert.All(readded.StaffList, p => Assert.Equal(-1, p.SkillIdx)); // 群を足しても誰も所属しない

        Assert.Same(st, Ws1Ops.RemoveSkillGroup(st, 9)); // 範囲外は何もしない
    }

    // ---- [マトリックス一括] SetGroupShiftRow / SetGroupShiftColumn（群×シフトの行/列ヘッダのタップ） ----

    private static MagiState MatrixState() => MinimalState.Build(
        shifts: new List<Shift> { new("休", "休", "", ""), new("A", "A", "", ""), new("B", "B", "", "") },
        groups: new List<Group> { new("G0", "G0"), new("G1", "G1") },
        groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 0, 1 }, new List<int> { 1, 1, 0 } });

    [Fact]
    public void SetGroupShiftRow_TurnsWholeRowOn_AndKeepsRestWhenTurningOff()
    {
        var st = MatrixState();
        var on = Ws1Ops.SetGroupShiftRow(st, 0, true);
        Assert.Equal(new[] { 1, 1, 1 }, on.GroupShift[0]);
        Assert.Equal(new[] { 1, 1, 0 }, on.GroupShift[1]); // 他の群は不変

        // OFF でも休(index0)は残る＝担当可能シフトの無い群を作らない（validate が拒否する状態を作らない）。
        var off = Ws1Ops.SetGroupShiftRow(on, 0, false);
        Assert.Equal(new[] { 1, 0, 0 }, off.GroupShift[0]);
        Assert.Same(st, Ws1Ops.SetGroupShiftRow(st, 5, true)); // 範囲外は何もしない
    }

    [Fact]
    public void SetGroupShiftColumn_AppliesToAllGroups_AndRefusesTurningRestOff()
    {
        var st = MatrixState();
        var on = Ws1Ops.SetGroupShiftColumn(st, 2, true);
        Assert.Equal(1, on.GroupShift[0][2]);
        Assert.Equal(1, on.GroupShift[1][2]);
        var off = Ws1Ops.SetGroupShiftColumn(on, 1, false);
        Assert.Equal(0, off.GroupShift[0][1]);
        Assert.Equal(0, off.GroupShift[1][1]);

        // 休の列を OFF にする操作は無変更（ReferenceEquals で拒否を検知できる）。
        Assert.Same(st, Ws1Ops.SetGroupShiftColumn(st, 0, false));
        Assert.Same(st, Ws1Ops.SetGroupShiftColumn(st, 9, true)); // 範囲外は何もしない
    }
}
