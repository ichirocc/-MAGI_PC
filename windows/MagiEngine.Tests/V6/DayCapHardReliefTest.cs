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

    /// <summary>救済で必須を減らした盤面を作り直しても、必須の多い結果へ置き換えない（入口で上限 0 の日のセルを外した基準だけと比べていた、Android 3.727.130 R1）。</summary>
    [Fact]
    public async Task RerunFromAReliefBoardNeverRaisesTheHardCount()
    {
        var st0 = Load("sample_state_v6.json");
        var p0 = new Problem(st0);
        var rest = p0.RestIdx!.Value;
        var board = st0.Schedule.ToIntArray2D();
        var baseRep = UnifiedViolationChecker.Check(st0, board);
        foreach (var cell in baseRep.Violations.Where(kv => kv.Value == "vio-c3n").Select(kv => kv.Key))
        {
            var ij = cell.Split(',').Select(int.Parse).ToArray();
            int i = ij[0], j = ij[1];
            if (p0.WishLocked(i, j) || p0.Pinned(i, j) || board[i][j] == rest || !p0.MayPlace(i, rest)) continue;
            var st = CapRest(st0, rest, j);
            var r = DayCapHardRelief.Apply(st, board, UnifiedViolationChecker.Check(st, board), long.MaxValue);
            if (r.Placed.Count == 0) continue;
            var (cleared, n) = V6NativeOptimizer.ClearCappedCells(st, r.Schedule);
            Assert.True(n > 0);
            var clearedReport = UnifiedViolationChecker.Check(st, cleared);
            Assert.True(clearedReport.Hard > r.Report.Hard, "外した基準のほうが必須が多い前提");
            Assert.Equal(r.Report.Hard, V6NativeOptimizer.DayCapKeptBaseline(st, r.Schedule, clearedReport)!.Value.Report.Hard);
            var outRes = await V6FinalPort.HandleOptimize(st, secondsRaw: 1, schedule: r.Schedule.Copy2D(), workers: 1,
                requestedAlgorithm: V6Algorithm.V5, allowImpossible: true, seed: 7L);
            Assert.True(outRes.Report.Hard <= r.Report.Hard, $"必須 {r.Report.Hard} → {outRes.Report.Hard}");
            return;
        }
        Assert.Fail("救済の起きる盤面が無い");
    }

    /// <summary>入力以外の段が上限 0 の日に置いて必須を減らしていたら、そのセルを残した版も候補に足す（Android 3.727.131、sept2026）。入力は足さない。</summary>
    [Fact]
    public void DayCapKeptStagesAddsTheKeptBoardOnlyWhenItHasFewerHard()
    {
        var st0 = Load("sample_state_v6.json");
        var p0 = new Problem(st0);
        var rest = p0.RestIdx!.Value;
        var board = st0.Schedule.ToIntArray2D();
        var baseRep = UnifiedViolationChecker.Check(st0, board);
        foreach (var cell in baseRep.Violations.Where(kv => kv.Value == "vio-c3n").Select(kv => kv.Key))
        {
            var ij = cell.Split(',').Select(int.Parse).ToArray();
            int i = ij[0], j = ij[1];
            if (p0.WishLocked(i, j) || p0.Pinned(i, j) || board[i][j] == rest || !p0.MayPlace(i, rest)) continue;
            var st = CapRest(st0, rest, j);
            var p = new Problem(st);
            var r = DayCapHardRelief.Apply(st, board, UnifiedViolationChecker.Check(st, board), long.MaxValue);
            if (r.Placed.Count == 0) continue;
            var stages = new List<V6FinalPort.StageCandidate>
            {
                new("入力", r.Schedule, r.Report),
                new("探索", board, UnifiedViolationChecker.Check(st, board)),
                new("後処理", r.Schedule, r.Report),
            };
            var added = V6FinalPort.DayCapKeptStages(st, p, stages, false);
            Assert.DoesNotContain(added, a => a.Label.StartsWith("入力"));
            var kept = Assert.Single(added, a => a.Label == "後処理（上限0の日を保つ）");
            Assert.Equal(r.Report.Hard, kept.Report.Hard);
            var cleared = V6FinalPort.ClearedCapZeroStages(st, p, stages, false);
            foreach (var a in added)
            {
                var c = Assert.Single(cleared, x => x.Label == a.Label.Replace("上限0の日を保つ", "上限0を外す"));
                Assert.True(a.Report.Hard < c.Report.Hard, $"{a.Label}: 外した版より必須が少ないときだけ足す");
            }
            return;
        }
        Assert.Fail("救済の起きる盤面が無い");
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
