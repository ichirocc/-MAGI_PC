using System.Linq;
using MagiEngine.Model;
using MagiEngine.Tests.Fixtures;
using MagiEngine.V6;
using Xunit;

namespace MagiEngine.Tests.V6;

public class RerunDraftTest
{
    private static MagiState State() => StateJsonSerializer.Parse(FixtureLoader.ReadRaw("golden_state.json"));

    [Fact]
    public void VariantsDifferFromTheBaseDraftAndFromEachOther()
    {
        var st = State();
        var baseSched = SmartInitialScheduler.Generate(st).Schedule;
        var v1 = V6FinalPort.RerunDraft(st, baseSched, 1);
        var v2 = V6FinalPort.RerunDraft(st, baseSched, 2);
        Assert.True(AdaptiveEliteArchive.ScheduleDistance(baseSched, v1) > 0, "変化 1 は元の下書きと違う");
        Assert.True(AdaptiveEliteArchive.ScheduleDistance(baseSched, v2) > 0, "変化 2 は元の下書きと違う");
        Assert.True(AdaptiveEliteArchive.ScheduleDistance(v1, v2) > 0, "回ごとに違う下書き");
        Assert.Equal(0, AdaptiveEliteArchive.ScheduleDistance(v1, V6FinalPort.RerunDraft(st, baseSched, 1)));
    }

    [Fact]
    public void VariantsKeepWishesAndExtBans()
    {
        var st = State();
        var p = new Problem(st);
        var baseSched = SmartInitialScheduler.Generate(st).Schedule;
        for (var v = 1; v <= 6; v++)
        {
            var d = V6FinalPort.RerunDraft(st, baseSched, v);
            for (var i = 0; i < p.S; i++) for (var j = 0; j < p.T; j++)
                if (p.WishLocked(i, j)) Assert.Equal(baseSched[i][j], d[i][j]);
            Assert.True(p.KeepsExtBan(baseSched, d), $"変化 {v} は拡張希望の禁止に置かない");
        }
    }
}
