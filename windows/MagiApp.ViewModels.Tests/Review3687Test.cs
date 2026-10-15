using MagiApp.ViewModels.Tests.TestSupport;
using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>Kotlin <c>Review3687Test</c> の 1 対 1 移植。3.687.0 のコードレビュー対応（R01〜R05・R08・R09・R11・R12）。</summary>
public class Review3687Test
{
    private static MagiState St1(IReadOnlyList<IReadOnlyList<int>> groupShift, IReadOnlyList<IReadOnlyList<int>> schedule, IReadOnlyList<Staff> staff,
        IReadOnlyDictionary<string, int>? wishes = null, IReadOnlyList<Shift>? shifts = null, IReadOnlyList<Group>? groups = null,
        IReadOnlyList<C41Row>? cons41 = null, int days = 1) => MinimalState.Build(
        startDate: "2026-08-01", endDate: $"2026-08-{days:00}", shifts: shifts, groups: groups, staffList: staff, use2Patterns: false,
        groupShift: groupShift, groupShiftApt: groupShift.Select(r => (IReadOnlyList<string>)r.Select(_ => "").ToList()).ToList(),
        schedule: schedule, wishes: wishes ?? new Dictionary<string, int>(), cons41: cons41 ?? new List<C41Row>());

    private static IReadOnlyList<IReadOnlyList<int>> G(params int[][] rows) => rows.Select(r => (IReadOnlyList<int>)r).ToList();

    // R08: 不足枠の候補は正式な前後の検査で採点してから確認の画面へ（0・空の差分を出さない）。
    [Fact]
    public void shortageCandidateIsEvaluatedBeforePreview()
    {
        var st = FixTiersTest.Board12x16(); var s = FixTiersTest.Sched(st);
        var op = new FixCell(1, 1, FixTiersTest.REST);
        var sug = V6PortAnalyzer.EvaluatedSuggestion(st, s, FixKind.Change, new[] { op }, "x");
        var before = UnifiedViolationChecker.Check(st, s);
        var w = FixTiersTest.Sched(st); w[1][1] = FixTiersTest.REST;
        var after = UnifiedViolationChecker.Check(st, w);
        Assert.Equal(after.Hard - before.Hard, sug.DeltaHard);
        Assert.Equal(after.Total - before.Total, sug.DeltaTotal);
        Assert.True(sug.Diff.Count > 0 && sug.DeltaTotal != 0, "過剰が減る差分を持つ");
    }

    // R02: 希望の取消は診断の入力の指紋と職員 ID で照合する。
    private static MagiState WishState(int[] gs, IReadOnlyList<Staff> staff, IReadOnlyDictionary<string, int> wishes) => St1(
        G(gs, new[] { 1, 1 }), G(new[] { 0 }, new[] { 0 }), staff, wishes,
        new List<Shift> { new("休", "休", "", "", ShiftRole.Rest), new("A", "A", "", "") }, new List<Group> { new("G", "G"), new("H", "H") });

    [Fact]
    public void removeWishRefusesWhenInputsChangedAndFollowsStaffId()
    {
        var s0 = WishState(new[] { 1, 0 }, new List<Staff> { new("X", 0, Id: 7), new("Y", 1, Id: 8) }, new Dictionary<string, int> { ["0,0"] = 1 });
        var issue = V6SanityPort.BuildGuidance(s0).Single(it => it.Action == SettingFixAction.RemoveWish);
        Assert.Equal(7, issue.WishStaffId);
        Assert.NotNull(issue.WishFingerprint);
        // 担当できるようになった＝診断が古い。取り消さない。
        var canDoNow = s0 with { GroupShift = G(new[] { 1, 1 }, new[] { 1, 1 }) };
        Assert.True(SettingFixLogic.WishDiagnosisStale(canDoNow, issue));
        Assert.Null(SettingFixLogic.Apply(canDoNow, issue));
        // 同じ入力なら取り消せる。
        Assert.Empty(SettingFixLogic.Apply(s0, issue)!.Wishes);
        // 並び替えたあとも ID で同じ人を指す（並び順のキーで別の人の希望を消さない）。
        var reordered = s0 with
        {
            StaffList = new List<Staff> { s0.StaffList[1] with { GroupIdx = 1 }, s0.StaffList[0] },
            Wishes = new Dictionary<string, int> { ["1,0"] = 1, ["0,0"] = 1 },
            Schedule = G(new[] { 0 }, new[] { 0 }),
        };
        Assert.Equal("1,0", SettingFixLogic.CurrentWishKey(reordered, issue));
        // 職員がいなくなった＝古い。
        Assert.True(SettingFixLogic.WishDiagnosisStale(s0 with
        {
            StaffList = new List<Staff> { new("Y", 1, Id: 8) }, Schedule = G(new[] { 0 }), Wishes = new Dictionary<string, int>(),
        }, issue));
    }

    // R01: 証明つき（担当できる人数 < 必要数）だけを「充足不可」と言い、限定探索の不発は「試した入れ替えでは埋まらなかった」。
    private static MagiState Cascade(bool cWished) => St1(
        G(new[] { 1, 1, 1 }, new[] { 1, 0, 1 }), G(new[] { 2 }, new[] { 0 }), new List<Staff> { new("A", 0), new("C", 1) },
        cWished ? new Dictionary<string, int> { ["1,0"] = 0 } : new Dictionary<string, int>(),
        new List<Shift> { new("休", "休", "", "", ShiftRole.Rest), new("X", "X", "1", ""), new("Y", "Y", "1", "") },
        new List<Group> { new("GA", "GA"), new("GC", "GC") });

    [Fact]
    public void searchMissIsNotCalledInfeasible()
    {
        var d = V6PortAnalyzer.DiagnoseCoverage(Cascade(cWished: true));
        var sf = d.Shortfalls.Single(it => it.ShiftIndex == 1);
        Assert.True(sf.BlockedNow && sf.SearchMiss);
        Assert.Equal(1, d.SearchMissSlots); Assert.Equal(0, d.ProvenBlockedSlots);
        var log = d.LogLines();
        var all = string.Join("\n", log);
        Assert.True(log[0].Contains("1枠は 試した入れ替えでは埋まらなかった") && !log[0].Contains("不能"), all);
        Assert.True(log.Any(it => it.Contains("— 試した入れ替えでは埋まらなかった:")), all);
        Assert.False(log[0].Contains("この希望・担当のままでは人員不足は減りません"), all);
        var proven = V6PortAnalyzer.DiagnoseCoverage(St1(G(new[] { 1, 0 }), G(new[] { 0 }), new List<Staff> { new("A", 0) },
            shifts: new List<Shift> { new("休", "休", "", "", ShiftRole.Rest), new("X", "X", "1", "") }, groups: new List<Group> { new("G", "G") }));
        Assert.Equal(CoverageVerdict.Infeasible, proven.Shortfalls.Single().Verdict);
        Assert.Contains(proven.LogLines(), it => it.Contains("— 充足不可:"));
    }

    // R03: 範囲の内外は族の名前でなく鍵の場所で決める。前からある別の違反を直すだけの付随は必要な変更に数えない。
    [Fact]
    public void frameIsJudgedByKeyLocation()
    {
        var day = FixTiersTest.Over;
        Assert.True(FixExplain.KeyInFrame(day, new ViolationKey("c3n", ViolationKey.Kind.Cell, 2, 1)));
        Assert.False(FixExplain.KeyInFrame(day, new ViolationKey("covU", ViolationKey.Kind.Need, 1, 3)));
        Assert.False(FixExplain.KeyInFrame(day, new ViolationKey("low", ViolationKey.Kind.Count, 0, 1)));
        var staff = new FixTarget(0, 1, null, 2, true, "下限");
        Assert.True(FixExplain.KeyInFrame(staff, new ViolationKey("c3n", ViolationKey.Kind.Cell, 0, 3)));
        Assert.False(FixExplain.KeyInFrame(staff, new ViolationKey("c3n", ViolationKey.Kind.Cell, 1, 3)));
        var e = new FixExplanation(Array.Empty<FixCell>(), new[] { new ExtraReason(new[] { new FixCell(0, 0, 0) }, "ついで", Incidental: true) }, Array.Empty<string>());
        Assert.False(e.AllExplained, "ついでに直るだけの付随は理由つきの埋め合わせではない");
    }

    [Fact]
    public void compensatingExtraStaysExplainedAndTier2()
    {
        var st = FixTiersTest.Board12x16(); var s = FixTiersTest.Sched(st);
        var swap = new FixSuggestion(FixKind.SwapXDay, new[] { new FixCell(0, 1, FixTiersTest.REST), new FixCell(0, 3, FixTiersTest.D) }, "swap", -1, -1, Array.Empty<(string, int)>());
        var e = FixExplain.ExplainExtras(st, s, FixTiersTest.Over, swap);
        Assert.True(e.AllExplained && e.Extras.All(it => !it.Incidental), string.Join(" / ", e.Extras));
    }

    // R04: まとめた付随の変更を先頭の職員に寄せない。
    [Fact]
    public void groupedExtrasNameEveryStaff()
    {
        var names = new[] { "金沢", "山本", "佐藤" };
        Assert.Equal("金沢さんの", FixExplain.ExtraStaffWhere(new[] { new FixCell(0, 1, 0), new FixCell(0, 2, 0) }, names));
        Assert.Equal("山本さん・佐藤さんの合計 ", FixExplain.ExtraStaffWhere(new[] { new FixCell(1, 1, 0), new FixCell(2, 2, 0), new FixCell(1, 3, 0) }, names));
    }

    // R05: グループのレンジの負の値は空欄と同じ（制限なし）＝採点と設定の見直しの案内が一致する。
    [Fact]
    public void negativeGroupRangeBoundIsUnset()
    {
        static IReadOnlyList<Shift> Sh() => new List<Shift> { new("休", "休", "", "", ShiftRole.Rest), new("X", "X", "", "") };
        static IReadOnlyList<Group> Gr() => new List<Group> { new("G", "G") };
        C41 P(string l, string u) => new Problem(St1(G(new[] { 1, 1 }), G(new[] { 0 }), new List<Staff> { new("A", 0) },
            shifts: Sh(), groups: Gr(), cons41: new List<C41Row> { new("G", "X", l, u) })).Cons41.Single();
        var a = P("-1", "-2"); Assert.Equal(0, a.L); Assert.Equal(int.MaxValue, a.U);
        var b = P("1", "-1"); Assert.Equal(1, b.L); Assert.Equal(int.MaxValue, b.U);
        var c = P("", "0"); Assert.Equal(0, c.L); Assert.Equal(0, c.U);
        var d = P("abc", "2"); Assert.Equal(0, d.L); Assert.Equal(2, d.U);
        var issues = V6SanityPort.BuildGuidance(St1(G(new[] { 1, 1 }), G(new[] { 0 }), new List<Staff> { new("A", 0) },
            shifts: Sh(), groups: Gr(), cons41: new List<C41Row> { new("G", "X", "1", "-1") }));
        Assert.True(issues.Any(it => it.Where.Contains("グループのレンジ") && it.Problem.Contains("負の値") && it.Problem.Contains("制限なし")), string.Join(" / ", issues));
        // 負の上限が「どの人数でも違反」にならない。
        var st = St1(G(new[] { 1, 1 }), G(new[] { 0 }), new List<Staff> { new("A", 0) },
            shifts: Sh(), groups: Gr(), cons41: new List<C41Row> { new("G", "X", "", "-1") });
        Assert.Equal(0, UnifiedViolationChecker.Check(st, new[] { new[] { 0 } }).Breakdown.GetValueOrDefault("c41", 0));
    }

    // R09: 確認の画面の門の説明は「必須を増やす」変更を断る（要調整だけの改善は通る）。
    [Fact]
    public void gateNoteMatchesTheRealGate()
    {
        Assert.Contains("必須違反を増やす", ChainFixPreview.PreviewGateNote);
        Assert.DoesNotContain("減らない", ChainFixPreview.PreviewGateNote);
    }

    // R11: 色の札の件数は族の単位。
    [Fact]
    public void colorChipUsesFamilyUnit()
    {
        Assert.Equal("人", AnalysisTriage.UnitOf("covU")); Assert.Equal("回", AnalysisTriage.UnitOf("low"));
        Assert.Equal("pt", AnalysisTriage.UnitOf("fair")); Assert.Equal("件", AnalysisTriage.UnitOf("c3n"));
    }

    // R12: 同名の職員を ID で別の人として束ね、グループ記号を添える。
    [Fact]
    public void previewRowsGroupByStaffId()
    {
        var sug = new FixSuggestion(FixKind.ChangeMulti, new[] { new FixCell(0, 0, 1), new FixCell(1, 0, 1) }, "x", 0, 0, Array.Empty<(string, int)>());
        var p = ChainFixPreview.Of(sug, new[] { new[] { 0 }, new[] { 0 } }, new[] { "佐藤", "佐藤" }, new[] { "休", "A" }, "2026-08-01",
            staffIds: new[] { 11, 12 }, staffGroups: new[] { "G", "H" });
        Assert.Equal(new[] { 11, 12 }, p.Rows.Select(it => it.StaffKey));
        Assert.Equal(2, p.Rows.GroupBy(it => it.StaffKey).Count());
        Assert.Equal(new[] { "佐藤（G）", "佐藤（H）" }, p.Rows.Select(it => it.Staff));
        var single = ChainFixPreview.Of(sug, new[] { new[] { 0 }, new[] { 0 } }, new[] { "佐藤", "鈴木" }, new[] { "休", "A" }, "2026-08-01");
        Assert.Equal(new[] { "佐藤", "鈴木" }, single.Rows.Select(it => it.Staff));
    }
}
