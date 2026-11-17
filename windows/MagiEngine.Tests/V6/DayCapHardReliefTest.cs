using MagiEngine.Model;
using MagiEngine.Tests.Fixtures;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>上限 0 の日を守るせいで残る必須違反は、その日に置いて減らす（Kotlin <c>DayCapHardReliefTest</c> の 1 対 1 移植）。</summary>
public class DayCapHardReliefTest
{
    private static MagiState Load(string name) => StateJsonSerializer.Parse(FixtureLoader.ReadRaw(name));

    private static MagiState CapRest(MagiState st, int rest, int day)
    {
        var d1 = st.NeedDay1.ToDictionary(kv => kv.Key, kv => kv.Value);
        var d2 = st.NeedDay2.ToDictionary(kv => kv.Key, kv => kv.Value);
        d1[$"{rest},{day}"] = "0"; d2[$"{rest},{day}"] = "0";
        return st with { NeedDay1 = d1, NeedDay2 = d2 };
    }

    [Fact]
    public void PlacesOnACapZeroDayOnlyWhenTheHardCountDrops()
    {
        var st0 = Load("sample_state_v6.json");
        var p0 = new Problem(st0);
        var rest = p0.RestIdx!.Value;
        var board = st0.Schedule.ToIntArray2D();
        var baseRep = UnifiedViolationChecker.Check(st0, board);
        var tried = 0;
        foreach (var cell in baseRep.Violations.Where(kv => kv.Value == "vio-c3n").Select(kv => kv.Key))
        {
            var ij = cell.Split(',').Select(int.Parse).ToArray();
            int i = ij[0], j = ij[1];
            if (p0.WishLocked(i, j) || p0.Pinned(i, j) || board[i][j] == rest || !p0.MayPlace(i, rest)) continue;
            var st = CapRest(st0, rest, j);
            var before = UnifiedViolationChecker.Check(st, board);
            var r = DayCapHardRelief.Apply(st, board, before, long.MaxValue);
            tried++;
            Assert.True(r.Report.Hard <= before.Hard, "必須が増えた");
            if (r.Placed.Count > 0)
            {
                Assert.True(r.Report.Hard < before.Hard, "置いたのに必須が減っていない");
                Assert.Equal(r.Report.Hard, UnifiedViolationChecker.Check(st, r.Schedule).Hard);
                return;
            }
        }
        Assert.Fail($"禁止の並びの候補が無い (tried={tried})");
    }

    [Fact]
    public void LeavesTheBoardAloneWithoutCapZeroDays()
    {
        var st = Load("sample_state_v6.json");
        var board = st.Schedule.ToIntArray2D();
        var rep = UnifiedViolationChecker.Check(st, board);
        var r = DayCapHardRelief.Apply(st, board, rep, long.MaxValue);
        Assert.Empty(r.Placed);
        Assert.Equal(rep.Hard, r.Report.Hard);
    }
}
