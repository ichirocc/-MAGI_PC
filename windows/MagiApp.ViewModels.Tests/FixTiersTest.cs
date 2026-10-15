using MagiApp.ViewModels.Tests.TestSupport;
using MagiEngine.Model;
using MagiEngine.V6;
using Range = MagiEngine.Model.Range;

namespace MagiApp.ViewModels.Tests;

/// <summary>12/16 の Dﾃ 過剰と同じ形の盤面で、入り口の 1 セルの案（Tier 1）と段の分け方を確かめる（Kotlin <c>FixTiersTest</c>）。</summary>
public class FixTiersTest
{
    internal const int REST = 0, D = 1;

    /// <summary>金沢（休は下限＝上限 2 回、D の 2 連続は禁止）・山本・佐藤。2 日目（day1）の D が 2 人で適正 1 人を超える。</summary>
    internal static MagiState Board12x16() => MinimalState.Build(
        startDate: "2026-12-15", endDate: "2026-12-18",
        shifts: new List<Shift> { new("休", "休", "", "", ShiftRole.Rest), new("D", "D", "1", "1") },
        groups: new List<Group> { new("G0", "G0"), new("G1", "G1"), new("G2", "G2") },
        staffList: new List<Staff> { new("金沢", 0), new("山本", 1), new("佐藤", 2) }, use2Patterns: true,
        groupShift: new List<IReadOnlyList<int>> { new[] { 1, 1 }, new[] { 1, 1 }, new[] { 1, 1 } },
        groupShiftApt: new List<IReadOnlyList<string>> { new[] { "", "" }, new[] { "", "" }, new[] { "", "" } },
        schedule: new List<IReadOnlyList<int>> { new[] { D, D, REST, REST }, new[] { REST, D, REST, D }, new[] { REST, REST, D, REST } },
        staffRange: new Dictionary<string, Range> { ["0,0"] = new("2", "2") },
        cons3n: new List<C3Row> { new(new[] { "D", "D" }) });
    internal static int[][] Sched(MagiState st) => st.Schedule.Select(r => r.ToArray()).ToArray();
    internal static readonly FixTarget Over = new(null, D, 1, 1, false, "適正人数");
    internal static readonly Func<int, int, (int Lo, int Hi)?> Limits = (k, _) => k == D ? (1, 1) : null;

    [Fact]
    public void EntryMoveOfAnotherStaffIsTier1AndPinnedStaffIsBlockedWithReason()
    {
        var st = Board12x16(); var s = Sched(st);
        var moves = FixTiers.EntryMoves(st, s, Over, REST);
        var yamamoto = Assert.Single(moves, m => m.Sug.Ops.SequenceEqual(new[] { new FixCell(1, 1, REST) }));
        Assert.True(yamamoto.Applicable);
        var kanazawa = Assert.Single(moves, m => m.Sug.Ops.Single().Staff == 0);
        Assert.False(kanazawa.Applicable);
        Assert.NotNull(kanazawa.Reason);
        var v = FixTiers.ClassifyTiers(st, s, Over, moves, Array.Empty<FixSuggestion>(), Limits);
        Assert.Equal(new[] { new[] { new FixCell(1, 1, REST) } }, v.Of(FixTier.T1).Select(x => x.Sug.Ops.ToArray()).ToArray());
        Assert.DoesNotContain(v.Of(FixTier.T1), x => x.Sug.Ops.Single().Staff == 0);   // 適用できない 1 セルは案にしない（理由の行だけ）
        Assert.Single(v.Blocked);
        Assert.True(v.Blocked.Single().StartsWith("金沢") && v.Blocked.Single().Contains("適用できません："), v.Blocked.Single());
    }

    [Fact]
    public void SameStaffOtherDaySwapIsMoveOnly()
    {
        var st = Board12x16(); var s = Sched(st);
        var swap = new FixSuggestion(FixKind.SwapXDay, new[] { new FixCell(0, 1, REST), new FixCell(0, 3, D) }, "swap", -1, -1, Array.Empty<(string, int)>());
        var v = FixTiers.ClassifyTiers(st, s, Over, Array.Empty<EntryMove>(), new[] { swap }, Limits);
        var t = Assert.Single(v.Items);
        Assert.Equal(FixTier.MoveOnly, t.Tier);
        var reason = Assert.Single(t.Explain!.Extras).Reason;
        Assert.NotNull(reason);
        Assert.True(reason!.StartsWith("入れないと金沢さんの"), reason);
    }

    [Fact]
    public void OutOfFrameForbiddenRunOnlyFixIsTier3()
    {
        var st = Board12x16(); var s = Sched(st);
        var c3nOnly = new FixSuggestion(FixKind.Change, new[] { new FixCell(0, 0, REST) }, "c3n", -1, 0, new[] { ("c3n", -1) });
        var v = FixTiers.ClassifyTiers(st, s, Over, Array.Empty<EntryMove>(), new[] { c3nOnly }, Limits);
        Assert.Equal(FixTier.T3, Assert.Single(v.Items).Tier);
        Assert.False(v.HasMain);
    }

    [Fact]
    public void MoveOnlyHeadingSaysPeriodGapDoesNotShrink()
    {
        Assert.Equal("Dの過剰は減りません（別の日へ移すだけの案 1件）", FixTiers.MoveOnlyHeading("D", false, 1));
    }
}
