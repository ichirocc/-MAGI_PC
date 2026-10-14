using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>Kotlin <c>CountShortageTextTest</c> の 1 対 1 移植。見ている問題の変化を最上段に（2026-10-14 利用者レビュー）:
/// 回数・人員の対象、案ごとの変化、不足の向きの説明、確認の画面、札。</summary>
public class CountShortageTextTest
{
    private static readonly IReadOnlyList<IReadOnlyList<int>> Schedule = new IReadOnlyList<int>[] { new[] { 1, 0, 0, 2 }, new[] { 0, 1, 1, 0 } };

    private static string Label(string f) => f switch { "c1" => "期間の制約", "weekly" => "曜日の偏り", _ => f };

    private static UiState Board() => new()
    {
        StartDate = "2026-10-01", Staff = 2, Days = 4, Shifts = 3, ShiftSymbols = new[] { "休", "A", "B" },
        StaffNames = new[] { "モニカ", "ノア" }, Schedule = Schedule,
        Wishes = new Dictionary<string, int> { ["0,1"] = 0, ["0,0"] = 1 },
        CountViolations = new Dictionary<string, string> { ["0,1"] = "vio-low" },
    };

    [Fact]
    public void CountTargetRecountsAfterTheOps()
    {
        var t = CellSheetLogic.CountFixTarget(new Dictionary<string, string> { ["0,1"] = "vio-low" }, 0, 1, (3, null, null))!;
        Assert.Equal(new FixTarget(0, 1, null, 3, true, "下限"), t);
        var e = t.Effect(Schedule, new[] { new FixCell(0, 1, 1) });
        Assert.Equal(new CountEffect(1, 2, 3, true), e);
        Assert.True(e.Fixes);
        Assert.Equal("Aを1回増やす", e.Title("A"));
        Assert.Equal("1回 → 2回", e.CountLine());
        Assert.Equal("不足 2回 → 1回", e.GapLine());
        Assert.Equal("適用後も、下限まで1回不足", e.AfterNote(t.Basis));
        Assert.Equal(new[] { "下限まで、あと2回です", "現在 1回 ／ 下限 3回" }, CellSheetLogic.FixTargetHead(t, 1));
        Assert.Equal("モニカさんのA不足を減らす", CellSheetLogic.FixTargetPurpose(t, "モニカ", "A"));
        // 別の職員だけを動かす案＝対象の回数は変わらない（「別の問題を改善する案」へ畳む）。
        var other = t.Effect(Schedule, new[] { new FixCell(1, 0, 1), new FixCell(1, 1, 0) });
        Assert.False(other.Fixes);
        Assert.Equal("Aは変わりません", other.Title("A"));
        Assert.Equal("Aの回数は増えません", CellSheetLogic.OtherFixNote("A", t));
        Assert.Equal("Aを増やす案", CellSheetLogic.ThisFixHeading("A", t));
        // 超過の向きと、届いたときの言い方。
        var over = CellSheetLogic.CountFixTarget(new Dictionary<string, string> { ["1,1"] = "vio-high" }, 1, 1, (null, 1, null))!;
        var oe = over.Effect(Schedule, new[] { new FixCell(1, 2, 0) });
        Assert.Equal("超過 1回 → 0回", oe.GapLine());
        Assert.Equal("適用後は上限以内になります", oe.AfterNote(over.Basis));
        Assert.Equal(new[] { "上限を1回超えています", "現在 2回 ／ 上限 1回" }, CellSheetLogic.FixTargetHead(over, 2));
        Assert.Null(CellSheetLogic.CountFixTarget(new Dictionary<string, string> { ["0,1"] = "vio-c2" }, 0, 1, (3, 5, 4)));
    }

    /// <summary>11/3 の不足に別の日の案を「直し方」として出さない: 人員の対象はその日のそのシフトの人数で数える。</summary>
    [Fact]
    public void NeedTargetCountsOnlyThatDay()
    {
        var t = CellSheetLogic.NeedFixTarget(new Dictionary<string, IReadOnlyList<string>> { ["1,2"] = new[] { "vio-covU" } }, 1, 2, (2, 3))!;
        Assert.Equal(new FixTarget(null, 1, 2, 2, true, "必要人数"), t);
        Assert.Equal(1, t.CountOf(Schedule));
        Assert.True(t.Effect(Schedule, new[] { new FixCell(0, 2, 1) }).Fixes);
        Assert.False(t.Effect(Schedule, new[] { new FixCell(0, 1, 1) }).Fixes);   // 別の日の変更はこの日の人数を変えない
        Assert.Equal("10/3のA", CellSheetLogic.FixTargetLabel(t, "A", "2026-10-01"));
        Assert.Equal("10/3のAを1人増やす", t.Effect(Schedule, new[] { new FixCell(0, 2, 1) }).Title("10/3のA"));
        Assert.Equal(new[] { "必要人数より1人少ない状態です", "配置 1人 ／ 必要 2人" }, CellSheetLogic.FixTargetHead(t, 1));
        Assert.Equal("10/3のAを必要人数に近づける", CellSheetLogic.FixTargetPurpose(t, null, "10/3のA"));
        var o = CellSheetLogic.NeedFixTarget(new Dictionary<string, IReadOnlyList<string>> { ["1,2"] = new[] { "vio-covO" } }, 1, 2, (0, 0))!;
        Assert.Equal(new[] { "適正人数より1人多い状態です", "配置 1人 ／ 適正 0人" }, CellSheetLogic.FixTargetHead(o, 1));
        Assert.Equal("10/3のAの不足を減らす案は、今回の検索では見つかりませんでした。担当できる職員と、その日の希望・固定勤務をご確認ください。", CellSheetLogic.NoNeedFixLine("10/3のA", t));
        // 見ている問題を変える案を先に（同順位は元の順）。
        var far = new FixSuggestion(FixKind.Change, new[] { new FixCell(0, 1, 1) }, "far", -2, -2, Array.Empty<(string, int)>());
        var near = new FixSuggestion(FixKind.Change, new[] { new FixCell(0, 2, 1) }, "near", -1, -1, Array.Empty<(string, int)>());
        Assert.Equal(new[] { near, far }, CellSheetLogic.RankForTarget(new[] { far, near }, Schedule, t));
        Assert.Equal(new[] { far, near }, CellSheetLogic.RankForTarget(new[] { far, near }, Schedule, null));
    }

    [Fact]
    public void OtherEffectsLeadWithHardAndSkipTheTargetFamily()
    {
        var s = new FixSuggestion(FixKind.Change, new[] { new FixCell(0, 1, 1) }, "x", -1, 2, new[] { ("low", -1), ("c1", 1), ("weekly", 2) });
        Assert.Equal(new[] { "必須違反：1件減る", "期間の制約：1件増える", "曜日の偏り：2pt増える" },
            CellSheetLogic.OtherEffectLines(s, new HashSet<string> { "low", "high", "apt" }, Label));
        Assert.Equal("この2件の変更を適用", CellSheetLogic.ApplyCountButton(2));
        Assert.Equal(ChainFixPreview.ApplyChangeButton, CellSheetLogic.ApplyCountButton(1));
    }

    [Fact]
    public void CountGapLineNamesCurrentTargetAndDifference()
    {
        Assert.Equal("現在 4回／目標 5回 ・ 不足 1回", CellSheetLogic.CountGapLine("vio-aptLow", 4, (null, null, 5)));
        Assert.Equal("現在 7回／下限 15回 ・ 不足 8回", CellSheetLogic.CountGapLine("vio-low", 7, (15, null, null)));
        Assert.Equal("Dテ　3回／目標 5回　あと2回", new CountChip("Dテ", 3, 5, "", true, 1).RowText);
        Assert.Equal("B4　5回／上限 1回　4回多い", new CountChip("B4", 5, 1, "上限", false, 2).RowText);
    }

    /// <summary>不足は「増やせない理由」を追加候補日で言う（旧: 「抜けると人員不足」＝減らす向きの理由で、向きが逆だった）。</summary>
    [Fact]
    public void ShortageReasonsListCandidateDaysNotRemovalRisks()
    {
        var u = Board();
        (int? Lo, int? Hi, int? Apt) Limits(int i, int k) => i == 0 && k == 1 ? (3, null, null) : (null, null, null);
        (int Lo, int Hi)? Need(int k, int j) => k == 2 ? (1, 1) : k == 1 ? (1, 2) : null;
        Assert.Equal(new CountShortage(1, 3, "下限"), FixSearchText.CountShortage(u, new FixFocus(0, 1), Limits));
        var why = FixSearchText.NoFixReasons(u, new FixFocus(0, 1), Limits, Need);
        Assert.Equal("今回の探索では、条件を守って 2 回増やせる案が見つかりませんでした。", why.Lines[0]);
        Assert.Equal("次に確認すること：「A」を追加する候補日と、変更を妨げる条件を確認します。", why.Lines[1]);
        Assert.Contains(why.Lines, l => l == "補足：「A」の 1 回のうち 1 回は本人の希望で固定されています。");
        Assert.DoesNotContain(why.Lines, l => l.Contains("抜けると"));
        Assert.Equal(FixSearchText.NoFixScope, why.Lines[^1]);
        Assert.Equal(new[] { 1, 2, 3 }, why.Candidates.Select(c => c.Day));
        var (wishDay, openDay, tightDay) = (why.Candidates[0], why.Candidates[1], why.Candidates[2]);
        Assert.Equal(AddCandidateGo.Wish, wishDay.Go);
        Assert.Equal("本人の希望（休）で固定されています", wishDay.Reason);
        Assert.Equal("モニカさんに、この日の希望を調整できるかご相談ください", wishDay.Check);
        Assert.Null(openDay.Reason);
        Assert.Equal(AddCandidateGo.None, openDay.Go);
        Assert.Equal("この日の「B」が必要人数ぎりぎりです（必要 1人）", tightDay.Reason);
        Assert.Equal(AddCandidateGo.Need, tightDay.Go);
        Assert.Equal("10/4(日)　B → A", FixSearchText.AddCandidateHead(tightDay, u, 1));
        Assert.True(why.WishRelated);
        var timedOut = Board();
        timedOut.FixTimedOut = true;
        Assert.Equal("制限時間内に、条件を守って 2 回増やせる案は見つかりませんでした。もう一度探すと続きを探せます。",
            FixSearchText.NoFixReasons(timedOut, new FixFocus(0, 1), Limits, Need).Lines[0]);
        Assert.Null(FixSearchText.CountShortage(u, new FixFocus(0, 1, 2), Limits));
        var high = Board();
        high.CountViolations = new Dictionary<string, string> { ["0,1"] = "vio-high" };
        Assert.Null(FixSearchText.CountShortage(high, new FixFocus(0, 1), Limits));
    }

    [Fact]
    public void DayPickNamesEachShiftWithItsGap()
    {
        var u = new UiState { ShiftSymbols = new[] { "休", "A", "B" }, Schedule = Schedule };
        Assert.Equal("どの勤務の不足を確認しますか？", CellSheetLogic.DayPickHeading(new[] { new CoverageMark(1, true), new CoverageMark(2, true) }));
        Assert.Equal("どの勤務の過不足を確認しますか？", CellSheetLogic.DayPickHeading(new[] { new CoverageMark(1, true), new CoverageMark(2, false) }));
        Assert.Equal("A　配置 0人／必要 1人　1人不足", CellSheetLogic.DayPickRow(u, 3, new CoverageMark(1, true), (1, 1)));
        Assert.Equal("B　配置 1人／適正 0人　1人多い", CellSheetLogic.DayPickRow(u, 3, new CoverageMark(2, false), (0, 0)));
    }

    [Fact]
    public void ReadyBadgeDoesNotSayReadyWhileAdjustmentsRemain()
    {
        Assert.Equal("配布可", CellSheetLogic.ReadyBadgeLabel(0));
        Assert.Equal("要調整 3件・配布前の確認", CellSheetLogic.ReadyBadgeLabel(3));
        Assert.DoesNotContain("元に戻せます", ChainFixPreview.ApplyChangeButton);
        Assert.Equal("対象期間：10/1〜10/31", CellSheetLogic.PeriodLine("2026-10-01", 31));
    }
}
