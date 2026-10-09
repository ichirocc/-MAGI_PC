using MagiApp.ViewModels.Tests.TestSupport;
using MagiEngine.Model;

namespace MagiApp.ViewModels.Tests;

/// <summary>[Android 3.643.0 同期] 月を移す前に、引き継ぐもの（日番号で残る）と消えるもの（日付で持つ拡張希望・期間外）を Ws1Ops.ResizeDays と同じ規則で数える（Kotlin <c>MonthMovePlanTest</c> と 1 対 1）。</summary>
public class MonthMovePlanTest
{
    private static MagiState State() => MinimalState.Build(
            startDate: "2026-10-01", endDate: "2026-10-31",
            shifts: new List<Shift> { new("休", "休", "", "", ShiftRole.Rest), new("日勤", "A", "1", "") },
            schedule: Enumerable.Range(0, 2).Select(_ => (IReadOnlyList<int>)Enumerable.Repeat(0, 31).ToList()).ToList(),
            wishes: new Dictionary<string, int> { ["0,4"] = 0, ["1,30"] = 1 },
            needDay1: new Dictionary<string, string> { ["1,2"] = "2", ["1,30"] = "1" })
        with
        {
            ManualPins = new List<ManualPin> { new(0, 1, 1), new(1, 30, 0) },
            ExtWishes = new List<ExtWish> { new(0, new[] { "2026-10-05", "2026-11-05" }, new[] { "A" }), new(1, new[] { "2026-10-20" }, new[] { "A" }) },
        };

    [Fact]
    public void MovingToAThirtyDayMonthDropsDateKeyedAndOutOfRangeItems()
    {
        var p = MonthMovePlan.Of(State(), 2026, 11);   // 11 月＝30 日
        Assert.Equal(30, p.Days);
        Assert.Equal(1, p.CarriedWishes);
        Assert.Equal(1, p.CarriedNeedExceptions); Assert.Equal(1, p.DroppedNeedExceptions);
        Assert.Equal(1, p.CarriedPins); Assert.Equal(1, p.DroppedPins);
        Assert.Equal(2, p.DroppedExtWishDays);
        Assert.Equal(1, p.DroppedExtWishes);
        Assert.True(p.NeedsConfirm);
        var lines = p.Lines();
        Assert.StartsWith("引き継ぐ: 勤務表の中身（同じ日番号に残ります）・通常希望 1 件", lines[0]);
        Assert.StartsWith("消える: 期間の外の拡張希望 2 日分", lines[1]);
    }

    [Fact]
    public void EmptyStateNeedsNoConfirmation()
    {
        var st = State() with { Wishes = new Dictionary<string, int>(), NeedDay1 = new Dictionary<string, string>(), ManualPins = Array.Empty<ManualPin>(), ExtWishes = Array.Empty<ExtWish>() };
        var p = MonthMovePlan.Of(st, 2026, 11);
        Assert.False(p.NeedsConfirm);
        Assert.Equal(new[] { "引き継ぐ: 勤務表の中身（同じ日番号に残ります）" }, p.Lines());
    }
}
