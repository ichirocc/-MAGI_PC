using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [3.275.0/3.277.0/3.279.0 移植元] C1DeltaPrefilter の <b>accept非変更</b> 性質を固定する。
/// HardReject は「checker+isBetter が確実に却下する候補」＝早期スキップしても採用結果は不変、を意味する。
/// </summary>
public class C1DeltaPrefilterTest
{
    private static readonly List<Shift> Shifts = new()
    {
        new("休", "休", "", ""), new("X", "X", "", ""), new("Y", "Y", "", ""),
    };

    /// <summary>単一群（全シフト担当可）。</summary>
    private static MagiState Single(
        int days, int staff, IReadOnlyList<IReadOnlyList<int>> sched,
        IReadOnlyList<C1Row>? cons1 = null, IReadOnlyList<C3Row>? cons3n = null,
        IReadOnlyDictionary<string, int>? wishes = null) =>
        MinimalState.Build(
            startDate: "2026-01-01", endDate: "2026-01-" + days.ToString().PadLeft(2, '0'),
            shifts: Shifts, groups: new List<Group> { new("G", "G") },
            staffList: Enumerable.Range(0, staff).Select(i => new Staff($"s{i}", 0)).ToList(),
            use2Patterns: false,
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } },
            groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "", "" } },
            schedule: sched, wishes: wishes, cons1: cons1, cons3n: cons3n);

    [Fact]
    public void HasActionableReflectsDeficientWindows()
    {
        var deficient = Single(3, 1, new List<IReadOnlyList<int>> { new List<int> { 0, 0, 0 } }, cons1: new List<C1Row> { new("2", "X", "1") });
        Assert.True(C1DeltaPrefilter.HasActionableC1(C1RepairIndex.Build(new Problem(deficient), deficient.Schedule.ToIntArray2D())));
        var clean = Single(3, 1, new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } }, cons1: new List<C1Row> { new("2", "X", "1") });
        Assert.False(C1DeltaPrefilter.HasActionableC1(C1RepairIndex.Build(new Problem(clean), clean.Schedule.ToIntArray2D())));
    }

    [Fact]
    public void ScreenCellRejectsNoOp()
    {
        var s = Single(2, 1, new List<IReadOnlyList<int>> { new List<int> { 1, 0 } });
        var p = new Problem(s);
        var sc = s.Schedule.ToIntArray2D();
        Assert.Equal(C1DeltaPrefilter.Verdict.HardReject, C1DeltaPrefilter.ScreenCell(p, sc, 0, 0, 1)); // 既にX
    }

    [Fact]
    public void ScreenCellRejectsNonCanDo()
    {
        // 2群: g0={休,X}, g1={休,Y}。s0(g0)はYを担当不可。
        var s = MinimalState.Build(
            startDate: "2026-01-01", endDate: "2026-01-02",
            shifts: Shifts, groups: new List<Group> { new("G0", "G0"), new("G1", "G1") },
            staffList: new List<Staff> { new("s0", 0), new("s1", 1) }, use2Patterns: false,
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 0 }, new List<int> { 1, 0, 1 } },
            groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "", "" }, new List<string> { "", "", "" } },
            schedule: new List<IReadOnlyList<int>> { new List<int> { 1, 0 }, new List<int> { 2, 0 } });
        var p = new Problem(s);
        var sc = s.Schedule.ToIntArray2D();
        Assert.Equal(C1DeltaPrefilter.Verdict.HardReject, C1DeltaPrefilter.ScreenCell(p, sc, 0, 1, 2)); // s0→Y=担当外
    }

    [Fact]
    public void ScreenCellComparesNetPrefNotJustWishPresence()
    {
        // [3.279.0/外部レビューC1-02 移植元] (0,0) の希望=X。盤面は休(=既に pref 違反中)。
        //   別の非希望シフト Y へ変えても pref は 1→1 で不変＝checker は採用し得るので却下しない
        //   （旧: wishLocked && ≠wish の存在判定で無条件却下し、有効手を落としていた＝反例実証済み）。
        var s = Single(2, 1, new List<IReadOnlyList<int>> { new List<int> { 0, 0 } }, wishes: new Dictionary<string, int> { ["0,0"] = 1 });
        var p = new Problem(s);
        var sc = s.Schedule.ToIntArray2D();
        Assert.Equal(C1DeltaPrefilter.Verdict.Neutral, C1DeltaPrefilter.ScreenCell(p, sc, 0, 0, 2));
        // 希望X自体へ寄せる候補 → 却下しない（改善し得る＝checkerに委ねる）。
        Assert.Equal(C1DeltaPrefilter.Verdict.Neutral, C1DeltaPrefilter.ScreenCell(p, sc, 0, 0, 1));
        // 充足済みの希望を破る候補（pref 0→1 の正味悪化）は従来どおり却下。
        var sat = Single(2, 1, new List<IReadOnlyList<int>> { new List<int> { 1, 0 } }, wishes: new Dictionary<string, int> { ["0,0"] = 1 });
        var p2 = new Problem(sat);
        var sc2 = sat.Schedule.ToIntArray2D();
        Assert.Equal(C1DeltaPrefilter.Verdict.HardReject, C1DeltaPrefilter.ScreenCell(p2, sc2, 0, 0, 2));
    }

    [Fact]
    public void ScreenCellRejectsForbiddenRun()
    {
        // cons3n=[X,X]。(0,0)=X の隣 (0,1) を X にすると禁止連続（c3n 0→1 の正味悪化）。
        var s = Single(2, 1, new List<IReadOnlyList<int>> { new List<int> { 1, 0 } }, cons3n: new List<C3Row> { new(new List<string> { "X", "X" }) });
        var p = new Problem(s);
        var sc = s.Schedule.ToIntArray2D();
        Assert.Equal(C1DeltaPrefilter.Verdict.HardReject, C1DeltaPrefilter.ScreenCell(p, sc, 0, 1, 1));
    }

    [Fact]
    public void ScreenCellAllowsForbiddenRunWhenNetC3nDoesNotIncrease()
    {
        // [3.279.0/外部レビューC1-01 移植元] 盤面[Y,X,X]・cons3n={XX,YY}。day1→Y は [Y,Y] を1件作るが
        //   同時に [X,X] を1件壊す＝c3n 正味0。checker は他族(c1等)の改善で採用し得るので却下しない
        //   （旧: makesForbiddenRun=true の存在判定で無条件却下＝isBetter=true の手を落とす反例を実証済み）。
        var s = Single(
            3, 1, new List<IReadOnlyList<int>> { new List<int> { 2, 1, 1 } },
            cons3n: new List<C3Row> { new(new List<string> { "X", "X" }), new(new List<string> { "Y", "Y" }) });
        var p = new Problem(s);
        var sc = s.Schedule.ToIntArray2D();
        Assert.Equal(C1DeltaPrefilter.Verdict.Neutral, C1DeltaPrefilter.ScreenCell(p, sc, 0, 1, 2));
    }

    [Fact]
    public void ScreenCellRejectsOutOfRangeCoordinates()
    {
        // [3.279.0/外部レビューC1-12 移植元] 不正座標は例外でなく HardReject（防御的境界チェック）。
        var s = Single(2, 1, new List<IReadOnlyList<int>> { new List<int> { 0, 0 } });
        var p = new Problem(s);
        var sc = s.Schedule.ToIntArray2D();
        Assert.Equal(C1DeltaPrefilter.Verdict.HardReject, C1DeltaPrefilter.ScreenCell(p, sc, 5, 0, 1));
        Assert.Equal(C1DeltaPrefilter.Verdict.HardReject, C1DeltaPrefilter.ScreenCell(p, sc, 0, 9, 1));
    }

    [Fact]
    public void ScreenCellNeutralForSafeCandidate()
    {
        // 休→Y は無変化でない・担当可・希望なし・禁止連続なし → Neutral（checkerに委ねる）。
        var s = Single(2, 1, new List<IReadOnlyList<int>> { new List<int> { 0, 0 } });
        var p = new Problem(s);
        var sc = s.Schedule.ToIntArray2D();
        Assert.Equal(C1DeltaPrefilter.Verdict.Neutral, C1DeltaPrefilter.ScreenCell(p, sc, 0, 1, 2));
    }

    // ---- [3.277.0 移植元] exact net c1 delta ----

    [Fact]
    public void C1DeltaIsNegativeWhenMoveResolvesWindow()
    {
        // [Y,Y,Y] ルール「X 2日窓≥1」。day0→X で窓[0,1]を解消（[1,2]は残る）→ fires 2→1 = -1。
        var s = Single(3, 1, new List<IReadOnlyList<int>> { new List<int> { 2, 2, 2 } }, cons1: new List<C1Row> { new("2", "X", "1") });
        var p = new Problem(s);
        var sc = s.Schedule.ToIntArray2D();
        Assert.Equal(-1, C1DeltaPrefilter.C1Delta(p, sc, 0, 0, 1));
    }

    [Fact]
    public void C1DeltaIsPositiveWhenMoveBreaksOwnWindow()
    {
        // [X,X,Y] ルール「X 3日窓≥2」。窓[0,2]は z=2 で充足。day0→Y にすると z=1<2 → fires 0→1 = +1。
        //   （ExpectedGain=gainのみの近似ではこの自己破壊を見落とすが、C1Deltaは loss を勘定する）。
        var s = Single(3, 1, new List<IReadOnlyList<int>> { new List<int> { 1, 1, 2 } }, cons1: new List<C1Row> { new("3", "X", "2") });
        var p = new Problem(s);
        var sc = s.Schedule.ToIntArray2D();
        Assert.Equal(1, C1DeltaPrefilter.C1Delta(p, sc, 0, 0, 2));
    }

    [Fact]
    public void C1DeltaIsZeroForNoOp()
    {
        var s = Single(2, 1, new List<IReadOnlyList<int>> { new List<int> { 1, 0 } }, cons1: new List<C1Row> { new("2", "X", "1") });
        var p = new Problem(s);
        var sc = s.Schedule.ToIntArray2D();
        Assert.Equal(0, C1DeltaPrefilter.C1Delta(p, sc, 0, 0, 1)); // 既にX＝無変化
    }
}
