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
        Assert.Equal("10/3のA不足を解消する", CellSheetLogic.FixTargetPurpose(t, null, "10/3のA"));
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
        // [3.678.0] カードの先頭に必須の変化を出したら繰り返さない。
        Assert.Equal(new[] { "期間の制約：1件増える", "曜日の偏り：2pt増える" },
            CellSheetLogic.OtherEffectLines(s, new HashSet<string> { "low", "high", "apt" }, Label, hardShown: true));
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
        Assert.Equal("必須違反 0・配布前の確認", CellSheetLogic.ReadyBadgeLabel(0));
        Assert.Equal("要調整 3件・配布前の確認", CellSheetLogic.ReadyBadgeLabel(3));
        Assert.DoesNotContain("元に戻せます", ChainFixPreview.ApplyChangeButton);
        Assert.Equal("対象期間：10/1〜10/31", CellSheetLogic.PeriodLine("2026-10-01", 31));
    }

    /// <summary>「どの日の不足を直す案か」と「別の日への影響」を分ける（2026-10-14 利用者レビュー 2 回目）。</summary>
    [Fact]
    public void OtherDayImpactIsSeparatedFromTheTargetDay()
    {
        // 職員0: 10/1=A 10/2=休 10/3=休 10/4=B、職員1: 10/1=休 10/2=A 10/3=A 10/4=休。
        var t = CellSheetLogic.NeedFixTarget(new Dictionary<string, IReadOnlyList<string>> { ["1,2"] = new[] { "vio-covU" } }, 1, 2, (2, 2))!;
        var ops = new[] { new FixCell(0, 2, 1), new FixCell(0, 3, 0) };   // 10/3 休→A、10/4 B→休
        (int Lo, int Hi)? Need(int k, int j) => (k, j) switch { (1, 2) => (2, 2), (2, 3) => (1, 1), _ => null };
        var (same, other) = CellSheetLogic.HeadcountImpact(Schedule, ops, new[] { "休", "A", "B" }, "2026-10-01", Need, (1, 2));
        Assert.Equal(new[] { "10/3の休：1人 → 0人" }, same);
        Assert.Equal(new[] { "10/4の休：1人 → 2人", "10/4のB：1人 → 0人（必要 1人を下回ります）" }, other);
        Assert.Equal("必要 2人を満たします", t.Effect(Schedule, ops).AfterNote(t.Basis));
        Assert.Equal(new[] { "モニカさんの「休」の回数は変わりません（2回のまま）", "モニカさんの「A」：1回 → 2回", "モニカさんの「B」：1回 → 0回" },
            CellSheetLogic.StaffCountNotes(Schedule, ops, new[] { "モニカ", "ノア" }, new[] { "休", "A", "B" }));
        Assert.Empty(CellSheetLogic.StaffCountNotes(Schedule, ops.Take(1).ToList(), new[] { "モニカ" }, new[] { "休", "A" }));
        // 同じ勤務のほかの日に残る不足（案を当てた後の盤面で数える）。
        var fams = new Dictionary<string, IReadOnlyList<string>> { ["1,2"] = new[] { "vio-covU" }, ["1,3"] = new[] { "vio-covU" } };
        (int Lo, int Hi)? NeedA(int k, int j) => k == 1 ? (2, 2) : null;
        Assert.Equal(new[] { "10/4のAは、2人不足のままです" },
            CellSheetLogic.RemainingSameShift(fams, CellSheetLogic.BoardAfter(Schedule, ops), t, NeedA, new[] { "休", "A", "B" }, "2026-10-01"));
    }

    [Fact]
    public void ShiftDayPickAndRestRowsSayTheSettingFirst()
    {
        var u = new UiState { StartDate = "2026-10-01", ShiftSymbols = new[] { "休", "A", "B" }, Schedule = Schedule };
        Assert.Equal("どの日を調整しますか？", CellSheetLogic.ShiftDayPickHeading());
        Assert.Equal("10/4(日)　配置 0人／必要 1人　1人不足", CellSheetLogic.ShiftDayPickRow(u, 1, 3, true, (1, 1)));
        Assert.Equal("10/2(金)　休み 1人／適正 0人", CellSheetLogic.RestOverRow(u, 0, 1, (0, 0)));
        Assert.Equal("10/2(金)　休み 1人／適正 未設定", CellSheetLogic.RestOverRow(u, 0, 1, null));
        Assert.Equal("各日の適正人数が0人と表示されています。意図した設定か、確認できます。", CellSheetLogic.RestOverIntro(true)[0]);
        Assert.Equal("保存：完了", CellSheetLogic.SaveStateLine(SaveState.Saved));
    }

    [Fact]
    public void PreviewRowsCarryTheDayHeadcountAndCountNotes()
    {
        var s = new FixSuggestion(FixKind.SwapXDay, new[] { new FixCell(0, 1, 1), new FixCell(0, 0, 0) }, "x", 0, 0, Array.Empty<(string, int)>());
        var p = ChainFixPreview.Of(s, new[] { new[] { 1, 0, 0, 2 }, new[] { 0, 1, 1, 0 } }, new[] { "福澤" }, new[] { "休", "A", "B" }, "2026-10-01");
        Assert.Equal("休 1人 → 0人、A 1人 → 2人", p.Rows[0].Impact);   // 10/2 休→A
        Assert.Equal("A 1人 → 0人、休 1人 → 2人", p.Rows[1].Impact);   // 10/1 A→休
        Assert.Equal(new[] { "福澤さんの「休」の回数は変わりません（2回のまま）", "福澤さんの「A」の回数は変わりません（1回のまま）" }, p.CountNotes);
    }

    [Fact]
    public void AppliedLinesReportTheTargetSaveAndWhatRemains()
    {
        var t = CellSheetLogic.NeedFixTarget(new Dictionary<string, IReadOnlyList<string>> { ["1,2"] = new[] { "vio-covU" } }, 1, 2, (2, 2))!;
        var ops = new[] { new FixCell(0, 2, 1) };
        var after = CellSheetLogic.BoardAfter(Schedule, ops);
        var u = new UiState
        {
            StartDate = "2026-10-01", ShiftSymbols = new[] { "休", "A", "B" }, Schedule = after, SaveState = SaveState.Saving,
            NeedFamilies = new Dictionary<string, IReadOnlyList<string>> { ["1,3"] = new[] { "vio-covU" } },
        };
        var a = new AppliedFix("k", "x", ops, Schedule, after, t, 3, 2, 10, 11);
        (int Lo, int Hi)? Need(int k, int j) => k == 1 ? (2, 2) : null;
        var lines = CellSheetLogic.AppliedLines(u, a, t, "10/3のA", Need);
        Assert.Equal("今回の問題：解消（10/3のA：1人 → 2人、必要 2人を満たします）", lines[0]);
        Assert.Equal("Aの不足（期間全体）：5人 → 4人", lines[1]);
        Assert.Equal("必須違反（勤務表全体）：3件 → 2件", lines[2]);
        Assert.Equal("10/3の休：1人 → 0人", lines[3]);
        Assert.Equal("保存：保存中", lines[4]);
        Assert.Equal("次に確認すること：10/4のAは、2人不足のままです。", lines[5]);
        Assert.StartsWith("勤務表が変わったため", lines[^1]);
        // 保存済みなら保存の行は出さない。続けて同じ勤務の案へ進むボタン。
        u.SaveState = SaveState.Saved;
        Assert.DoesNotContain(CellSheetLogic.AppliedLines(u, a, t, "10/3のA", Need), l => l.StartsWith("保存：", StringComparison.Ordinal));
        Assert.Equal("続けて A の案を見る", CellSheetLogic.AppliedNextButton(true, "A", 2, true));
        Assert.Equal("残っている必須違反を順に見る（2か所）", CellSheetLogic.AppliedNextButton(false, "A", 2, true));
        Assert.Null(CellSheetLogic.AppliedNextButton(false, "A", 0, true));
        Assert.Equal("新たに生じた問題：禁止の並び +1件", CellSheetLogic.AppearedFamiliesLine(new Dictionary<string, int> { ["c3n"] = 0 }, new Dictionary<string, int> { ["c3n"] = 1, ["c1"] = 0 }, f => f == "c3n" ? "禁止の並び" : f));
        Assert.True(a.IsCurrent(after));
    }

    /// <summary>日別の詳細で職員に絞っても、その日のその勤務の問題のまま探す（日付を落とさない）。</summary>
    [Fact]
    public void PersonFocusOnADayKeepsTheNeedTarget()
    {
        var u = new UiState { Schedule = Schedule, NeedFamilies = new Dictionary<string, IReadOnlyList<string>> { ["1,2"] = new[] { "vio-covU" } } };
        (int? Lo, int? Hi, int? Apt) Staff(int i, int k) => (null, null, null);
        (int Lo, int Hi)? Need(int k, int j) => k == 1 ? (2, 2) : null;
        Assert.Equal(new FixTarget(null, 1, 2, 2, true, "必要人数"), CellSheetLogic.PanelTarget(u, new FixFocus(0, 1, 2), Staff, Need));
        Assert.Equal(new FixTarget(null, 1, 2, 2, true, "必要人数"), CellSheetLogic.PanelTarget(u, new FixFocus(null, 1, 2), Staff, Need));
        Assert.Null(CellSheetLogic.PanelTarget(u, new FixFocus(0, null, 2), Staff, Need));   // セルのシート（勤務なし）は対象なし
        Assert.Null(CellSheetLogic.PanelTarget(u, new FixFocus(null, 1, 2, ExceptStaff: 0), Staff, Need));
    }

    /// <summary>自動と担当者の分担（2026-10-14 利用者レビュー）: 作成前に自動で行うこと・保持するもの、作成後は「完了＝配布可」と言わない。</summary>
    [Fact]
    public void AutoAndOperatorSplitTexts()
    {
        Assert.Equal("10/1〜10/31・12人・探索 1分（＋仕上げ 20秒）。開始後は一手ごとの確認はしません", CellSheetLogic.PreRunPlanLine("2026-10-01", 31, 12, 60, 20));
        Assert.Equal("登録条件：つくる前に直す項目はありません／手動固定 3件は保持します", CellSheetLogic.PreRunConditionLine(false, 0, 3));
        Assert.Equal("登録条件：確認が必要な項目があります（下の一覧・希望が上限を超える 2件）", CellSheetLogic.PreRunConditionLine(false, 2, 0));
        static string L(string f) => f switch { "c1" => "期間の制約", "weekly" => "曜日の偏り", _ => f };
        Assert.Equal("期間の制約 2件・曜日の偏り 12pt", CellSheetLogic.SoftSummary(new Dictionary<string, int> { ["c1"] = 2, ["weekly"] = 12, ["c3n"] = 1 }, L));
        Assert.Equal("なし", CellSheetLogic.SoftSummary(new Dictionary<string, int> { ["covU"] = 1 }, L));
        Assert.Equal("解消", CellSheetLogic.AppliedStatusWord(new CountEffect(0, 1, 1, true, "人")));
        Assert.Equal("減少", CellSheetLogic.AppliedStatusWord(new CountEffect(7, 9, 15, true)));
        Assert.Equal("残存", CellSheetLogic.AppliedStatusWord(new CountEffect(7, 7, 15, true)));
        Assert.Equal("別案を見る", CellSheetLogic.BackToFixesButton);
    }

    /// <summary>同じ勤務の不足の日々をまとめて見る（日ごとの不足の合計。ある日の過剰で別の日の不足を相殺しない）。</summary>
    [Fact]
    public void MultiDayTargetSumsPerDayShortages()
    {
        // A の必要: 10/3＝2人（今 1人）、10/4＝1人（今 0人）。
        var fams = new Dictionary<string, IReadOnlyList<string>> { ["1,2"] = new[] { "vio-covU" }, ["1,3"] = new[] { "vio-covU" } };
        (int Lo, int Hi)? Need(int k, int j) => k == 1 ? (j == 2 ? (2, 2) : (1, 1)) : null;
        var t = CellSheetLogic.MultiNeedTarget(fams, 1, Need)!;
        Assert.Equal(new[] { 2, 3 }, t.Days); Assert.Equal(new[] { 2, 1 }, t.Needs);
        Assert.Equal(2, t.GapOf(Schedule));
        Assert.Null(CellSheetLogic.MultiNeedTarget(new Dictionary<string, IReadOnlyList<string>> { ["1,2"] = new[] { "vio-covU" } }, 1, Need));   // 1 日だけならまとめない
        // 10/3 だけ埋める案＝合計 2 → 1、両日を埋める案＝2 → 0。
        var one = t.Effect(Schedule, new[] { new FixCell(0, 2, 1) });
        Assert.Equal((2, 1), one.Gaps); Assert.True(one.Fixes);
        Assert.Equal("適用後も、合計 1人不足", one.AfterNote(t.Basis));
        var both = t.Effect(Schedule, new[] { new FixCell(0, 2, 1), new FixCell(1, 3, 1) });
        Assert.Equal("対象の日はすべて必要人数を満たします", both.AfterNote(t.Basis));
        Assert.Equal("不足 2人 → 0人", both.GapLine());
        // 10/4 の A を足して 10/3 の A を抜く案は、合計が減らない（相殺しない）。
        var swap = t.Effect(Schedule, new[] { new FixCell(1, 2, 0), new FixCell(1, 3, 1) });
        Assert.False(swap.Fixes);
        Assert.Equal("A（2日）", CellSheetLogic.FixTargetLabel(t, "A", "2026-10-01"));
        Assert.Equal("A（2日）の不足をまとめて解消する", CellSheetLogic.FixTargetPurpose(t, null, "A（2日）"));
        Assert.Equal(new[] { "2日で合計2人不足しています（日ごとの不足の合計）", "対象の日：10/3・10/4" }, CellSheetLogic.FixTargetHead(t, 0, Schedule, "2026-10-01"));
        Assert.Equal("両日の不足をまとめて調整する案を探す", CellSheetLogic.MultiFixButton(2));
        Assert.Equal("3日の不足をまとめて調整する案を探す", CellSheetLogic.MultiFixButton(3));
        var u = new UiState { Schedule = Schedule, NeedFamilies = fams };
        (int? Lo, int? Hi, int? Apt) Staff(int i, int k) => (null, null, null);
        Assert.Equal(t, CellSheetLogic.PanelTarget(u, new FixFocus(null, 1, null, AllDays: true), Staff, Need));
        Assert.Equal("-,1,-,all", new FixFocus(null, 1, null, AllDays: true).Key);
    }

    [Fact]
    public void ApplyRequiresTargetImprovementAndShowsUnevaluated()
    {
        var fams = new Dictionary<string, IReadOnlyList<string>> { ["1,2"] = new[] { "vio-covU" }, ["1,3"] = new[] { "vio-covU" } };
        (int Lo, int Hi)? Need(int k, int j) => k == 1 ? (j == 2 ? (2, 2) : (1, 1)) : null;
        var t = CellSheetLogic.MultiNeedTarget(fams, 1, Need)!;
        Assert.True(CellSheetLogic.ImprovesTarget(t, Schedule, new[] { new FixCell(0, 2, 1) }));
        // 相殺するだけの案は全体が良くなっても当てない（統合仕様 INV-03）。対象を持たない案は判定しない。
        Assert.False(CellSheetLogic.ImprovesTarget(t, Schedule, new[] { new FixCell(1, 2, 0), new FixCell(1, 3, 1) }));
        Assert.True(CellSheetLogic.ImprovesTarget(null, Schedule, new[] { new FixCell(1, 2, 0) }));
        Assert.Equal("新たな必須違反：未評価（適用の直前に検査します）", CellSheetLogic.NewHardLine(null));
        Assert.Equal("新たな必須違反：なし", CellSheetLogic.NewHardLine(Array.Empty<string>()));
        Assert.Equal("新たな必須違反：禁止の並び", CellSheetLogic.NewHardLine(new[] { "禁止の並び" }));
    }

    [Fact]
    public void TimeoutAndSaveFailureAreSaidSeparately()
    {
        var t = CellSheetLogic.NeedFixTarget(new Dictionary<string, IReadOnlyList<string>> { ["1,2"] = new[] { "vio-covU" } }, 1, 2, (2, 2))!;
        // 時間切れは「案が無い」と分ける（統合仕様 INV-08）。
        Assert.Equal("10/3のAの不足を減らす案は、制限時間内には見つかりませんでした。もう一度探すと続きを探せます。", CellSheetLogic.NoNeedFixLine("10/3のA", t, timedOut: true));
        Assert.Equal("保存：失敗（「もう一度保存する」で保存し直してください）", CellSheetLogic.SaveStateLine(SaveState.Failed));
    }

    [Fact]
    public void UnitsAreNotSummedAcrossKinds()
    {
        // 件・人・回・pt は 1 つの数に足さない（統合仕様 INV-07）。
        var b = new Dictionary<string, int> { ["c3n"] = 2, ["covU"] = 1, ["low"] = 6, ["apt"] = 3, ["c1"] = 4, ["covO"] = 2, ["fair"] = 30 };
        Assert.Equal("2件・1人", AnalysisTriage.UnitTotals(b, MirrorKeys.Hard));
        Assert.Equal("4件・2人・9回・30pt", AnalysisTriage.UnitTotals(b, MirrorKeys.Soft));
        Assert.Equal("4件・2人・9回", AnalysisTriage.UnitTotals(b, MirrorKeys.Soft, includePt: false));
        Assert.Equal("必須違反 2件・1人・要調整 4件・2人・9回・30pt", AnalysisTriage.ReportCountsText(b));
        static string L(string f) => f switch { "low" => "下限割れ", "c3n" => "禁止の並び", _ => f };
        Assert.Equal("下限割れ −6回・禁止の並び +1件", AnalysisTriage.FamilyDeltaList(new[] { ("low", -6), ("c3n", 1) }, L));
        Assert.Equal("要調整 4件・2人・9回・30pt・配布前の確認", CellSheetLogic.ReadyBadgeLabel(40, b));
        Assert.Equal("評価：必須違反 2件・1人", CellSheetLogic.BoardStatusLines(true, true, true, 3, 0, b).Second);
    }

    [Fact]
    public void FixFocusFollowsTheStaffIdAcrossReorderAndDeletion()
    {
        var f = new FixFocus(1, 2, 3, ExceptStaff: 0);
        // 職員 ID 20（番号 1）と 10（番号 0）で開き、並べ替えのあとは新しい番号で探す（統合仕様 5.1）。
        Assert.Equal(new FixFocus(0, 2, 3, ExceptStaff: 2), CellSheetLogic.ResolveFixFocus(f, 20, 10, new[] { 20, 30, 10 }));
        Assert.Equal(f, CellSheetLogic.ResolveFixFocus(f, 20, 10, new[] { 10, 20, 30 }));
        Assert.Null(CellSheetLogic.ResolveFixFocus(f, 20, 10, new[] { 10, 30 }));   // 削除＝別の人を探さない
        Assert.Equal(f, CellSheetLogic.ResolveFixFocus(f, null, 0, new[] { 5, 6 }));   // ID なし＝番号のまま
    }

    [Fact]
    public void movingTheExcessToAnotherDayIsSaidAsAMove()
    {
        // 2 人 × 3 日。勤務 1 の適正は毎日 1 人。10/1 に 2 人＝期間全体の過剰 1。
        IReadOnlyList<IReadOnlyList<int>> board = new IReadOnlyList<int>[] { new[] { 1, 0, 0 }, new[] { 1, 1, 0 } };
        Func<int, int, (int Lo, int Hi)?> lim = (k, _) => k == 1 ? (1, 1) : null;
        Assert.Equal(1, CellSheetLogic.ShiftPeriodGap(board, 1, false, lim));
        // 10/1 の 2 人目を休にし、代わりに 10/2 へ 1 人足す＝10/1 は直るが 10/2 が過剰＝移るだけ（2026-10-15 利用者「思考誘導が混乱する」）。
        var moved = CellSheetLogic.BoardAfter(board, new[] { new FixCell(1, 0, 0), new FixCell(0, 1, 1) });
        Assert.Equal(1, CellSheetLogic.ShiftPeriodGap(moved, 1, false, lim));
        Assert.Equal("Dﾃの過剰（期間全体）：1人 → 1人＝別の日へ移るだけです", CellSheetLogic.PeriodGapLine("Dﾃ", false, 1, 1));
        Assert.Equal("Dﾃの過剰（期間全体）：1人 → 0人", CellSheetLogic.PeriodGapLine("Dﾃ", false, 1, 0));
        static string L(string f) => f switch { "c3n" => "禁止の並び", _ => f };
        Assert.Equal("必須違反の変化：禁止の並び −1件", CellSheetLogic.HardChangeLine(new[] { ("c3n", -1), ("covO", 0), ("weekly", 2) }, L));
        Assert.Null(CellSheetLogic.HardChangeLine(new[] { ("weekly", 2) }, L));
        var fams = new Dictionary<string, IReadOnlyList<string>> { ["1,0"] = new[] { "vio-covO" }, ["1,1"] = new[] { "vio-covO" } };
        var t = CellSheetLogic.NeedFixTarget(fams, 1, 0, (1, 1))!;
        Assert.Equal("ほかにDﾃが多い日：10/2（1人）", CellSheetLogic.OtherDaysGapLine(fams, moved, t, lim, new[] { "休", "Dﾃ" }, "2026-10-01"));
    }
}
