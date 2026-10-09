using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>[3.644.0] つくる前の確認の、セルを持たない行の着地先（Kotlin <c>EditLandingTest</c> と 1 対 1）。</summary>
public class EditLandingTest
{
    [Fact]
    public void DayNeedLandsOnTheNeedCalendarOfThatShift()
    {
        var core = new ConstraintMus.Item[] { new ConstraintMus.DayNeed(11, 2, 3), new ConstraintMus.RangeCap(0, 2, 4) };
        var l = GuidedFixRules.LandingForProofCore(core, zeroCap: false);
        Assert.Equal(new EditLanding(0, null, NeedShift: 2, Day: 11), l);   // [3.646.0 L01] 日も運ぶ
        Assert.Equal("必要人数を見直す", GuidedFixRules.LandingButtonLabel(l));
    }

    /// <summary>[3.646.0 L01/L04] 人員不足の枠は日を運ぶ。原因が分からない枠も編集タブを開くだけにせず、その日のそのシフトの必要人数へ。</summary>
    [Fact]
    public void ShortageLandingsCarryTheDayAndNeverFallBackToTheBareTab()
    {
        static CoverageShortfall Sf(CoverageVerdict verdict, bool blockedNow = false, int forbid = 0, IReadOnlyList<int>? pinned = null) =>
            new(9, "10/10", 3, "夜", 1, 0, 1, 4, verdict, "r", BlockedNow: blockedNow, WishPinned: pinned ?? Array.Empty<int>(), ForbidCount: forbid);
        Assert.Equal(new EditLanding(0, null, WishStaff: 4, Day: 9), GuidedFixRules.LandingFor(Sf(CoverageVerdict.Fixable, pinned: new[] { 4 })));
        Assert.Equal(new EditLanding(2, "yr_ws1"), GuidedFixRules.LandingFor(Sf(CoverageVerdict.Infeasible)));
        Assert.Equal(new EditLanding(2, "yr_cons"), GuidedFixRules.LandingFor(Sf(CoverageVerdict.Fixable, blockedNow: true, forbid: 2)));
        Assert.Equal(new EditLanding(0, null, NeedShift: 3, Day: 9), GuidedFixRules.LandingFor(Sf(CoverageVerdict.Fixable)));
        Assert.Equal("必要人数を見直す", GuidedFixRules.LandingButtonLabel(GuidedFixRules.LandingFor(Sf(CoverageVerdict.Fixable))));
    }

    /// <summary>[3.646.0 L03] 編集タブの先頭の「元の確認へ戻る」の 1 行。</summary>
    [Fact]
    public void ReturnLineNamesTheOrigin()
    {
        Assert.Equal("「つくる前の確認」から来ました。直したら元の確認へ戻れます。", EditReturn.Line(new EditReturn("つくる前の確認", EditReturn.PreRun)));
        Assert.Equal(EditReturn.CellOrigin, new EditReturn("甲 10/3 のセル", EditReturn.CellOrigin, (0, 2)).Origin);
        Assert.Equal("元の確認へ戻る", EditReturn.ButtonText);
    }

    [Fact]
    public void ZeroCapWinsWhenTheProofIsTaggedZeroCap()
    {
        var core = new ConstraintMus.Item[] { new ConstraintMus.DayNeed(11, 2, 3), new ConstraintMus.RangeCap(5, 2, 0) };
        var l = GuidedFixRules.LandingForProofCore(core, zeroCap: true);
        Assert.Equal(new EditLanding(2, "yr_count", CountStaff: 5, CountShift: 2, Label: GuidedFixRules.LandingZeroCap), l);
        Assert.Equal(GuidedFixRules.LandingZeroCap, GuidedFixRules.LandingButtonLabel(l));
        Assert.Equal(1, GuidedFixRules.DoorFor(l!));
    }

    [Fact]
    public void RangeAndWindowRulesLandOnCountsAndConstraints()
    {
        Assert.Equal(new EditLanding(2, "yr_count", CountStaff: 3, CountShift: 1), GuidedFixRules.LandingForProofCore(new ConstraintMus.Item[] { new ConstraintMus.RangeFloor(3, 1, 2) }, zeroCap: false));
        Assert.Equal("回数の下限・上限を見直す", GuidedFixRules.LandingButtonLabel(new EditLanding(2, "yr_count", CountStaff: 3, CountShift: 1)));
        Assert.Equal(new EditLanding(2, "yr_cons", Label: GuidedFixRules.LandingWindow), GuidedFixRules.LandingForProofCore(new ConstraintMus.Item[] { new ConstraintMus.WindowRule(1, 7, 2) }, zeroCap: false));
        Assert.Null(GuidedFixRules.LandingForProofCore(new ConstraintMus.Item[] { new ConstraintMus.WishPin(0, 1, 1) }, zeroCap: false));
    }

    [Fact]
    public void ExistingLabelsAndDoorsAreUnchanged()
    {
        Assert.Equal("希望を見直す", GuidedFixRules.LandingButtonLabel(new EditLanding(0, null, 3)));
        Assert.Equal("禁止の並びを見直す", GuidedFixRules.LandingButtonLabel(new EditLanding(2, "yr_cons")));
        Assert.Equal("担当を見直す", GuidedFixRules.LandingButtonLabel(new EditLanding(2, "yr_ws1")));
        Assert.Equal("データを見直す", GuidedFixRules.LandingButtonLabel(null));
        Assert.Equal(2, GuidedFixRules.DoorFor(new EditLanding(2, "yr_ws1")));
        Assert.Equal(0, GuidedFixRules.DoorFor(new EditLanding(0, null, 3)));
    }

    /// <summary>[3.650.0/外部レビュー] 入力途中の選択は対象を名前で追い、対象や期間が変わったら持ち越さない。</summary>
    [Fact]
    public void InputSelectionFollowsTheStaffAndDropsDaysWhenTheTargetOrPeriodChanges()
    {
        var p = EditPick.PeriodOf("2026-10-01", 31);
        var names = new[] { "甲", "乙", "丙" };
        var pick = EditPick.PickDays(EditPick.PickAt(EditPick.None, names, p, 2), names, p, Days(3, 4));
        Assert.Equal(new EditPick(2, "丙", Days(3, 4), p, EditPick.RosterOf(names)), pick);
        var moved = new[] { "丙", "甲", "乙" };
        Assert.Equal(new EditPick(0, "丙", Days(3, 4), p, EditPick.RosterOf(moved)), EditPick.Resolve(pick, moved, p));   // 並び替え: 名前で追う
        var cut = new[] { "甲", "乙" };
        Assert.Equal(new EditPick(0, "甲", EditPick.NoDays, p, EditPick.RosterOf(cut)), EditPick.Resolve(pick, cut, p));   // 削除: 先頭へ戻して日を消す
        var p11 = EditPick.PeriodOf("2026-11-01", 30);
        Assert.Equal(new EditPick(2, "丙", EditPick.NoDays, p11, EditPick.RosterOf(names)), EditPick.Resolve(pick, names, p11));   // 月の移動: 日を消す
        Assert.Equal(new EditPick(0, "甲", EditPick.NoDays, p, EditPick.RosterOf(names)), EditPick.Resolve(EditPick.None, names, p));   // まだ選んでいない
        Assert.Equal(EditPick.None with { Period = p }, EditPick.Resolve(pick, System.Array.Empty<string>(), p));
    }

    [Fact]
    public void SameNamedStaffAreFollowedOnlyWhileTheRosterIsUnchanged()
    {
        var p = EditPick.PeriodOf("2026-10-01", 31);
        var names = new[] { "佐藤", "佐藤", "鈴木" };
        var pick = EditPick.PickDays(EditPick.PickAt(EditPick.None, names, p, 1), names, p, Days(5));
        Assert.Equal(1, EditPick.Resolve(pick, names, p).Index);
        Assert.True(EditPick.Resolve(pick, names, p).Days.SetEquals(Days(5)));
        var reordered = new[] { "鈴木", "佐藤", "佐藤" };
        // 同じ名前で並びが変わったら区別できない＝先頭へ戻して日を消す
        Assert.Equal(new EditPick(0, "鈴木", EditPick.NoDays, p, EditPick.RosterOf(reordered)), EditPick.Resolve(pick, reordered, p));
    }

    /// <summary>一覧の指紋は Kotlin の <c>List&lt;String&gt;.hashCode()</c> と同じ値（相談の名簿の指紋と共用）。</summary>
    [Fact]
    public void RosterMatchesKotlinListHashCode()
    {
        Assert.Equal(29503473, EditPick.RosterOf(new[] { "甲", "乙", "丙" }));   // JVM の listOf("甲", "乙", "丙").hashCode()
        Assert.Equal(1, EditPick.RosterOf(System.Array.Empty<string>()));
    }

    private static IReadOnlySet<int> Days(params int[] d) => new HashSet<int>(d);
}
