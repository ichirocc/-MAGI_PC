using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// Kotlin <c>ObjectiveWeightsTest</c> の 1 対 1 移植: 既定（Weights）は今の重みそのもの、優先順位（Priority）でも
/// チェッカーの WeightedScore・評価器・差分評価が同じ重みで一致すること。方式は静的な値なので並列にしない。
/// </summary>
[Collection("PolishGateGlobal")]
public class ObjectiveWeightsTest : IDisposable
{
    public void Dispose() => ObjectiveWeights.Method = ObjectiveMethod.Weights;

    [Fact]
    public void DefaultMethodIsTheCurrentWeights()
    {
        Assert.Equal(ObjectiveMethod.Weights, ObjectiveWeights.Method);
        Assert.Equal(MirrorKeys.Weights, ObjectiveWeights.Table());
    }

    [Fact]
    public void PriorityTableKeepsHardAndUsesTierWeights()
    {
        var t = ObjectiveWeights.Table(ObjectiveMethod.Priority).ToDictionary(kv => kv.Key, kv => kv.Weight);
        foreach (var f in MirrorKeys.Hard) Assert.Equal(MirrorKeys.WeightOf(f), t[f]);
        foreach (var f in TierObjective.Tiers[1]) Assert.Equal(200.0, t[f]);
        foreach (var f in TierObjective.Tiers[2]) Assert.Equal(20.0, t[f]);
        foreach (var f in TierObjective.Tiers[3]) Assert.Equal(1.0, t[f]);
        Assert.Equal(MirrorKeys.Weights.Select(kv => kv.Key), ObjectiveWeights.Table(ObjectiveMethod.Priority).Select(kv => kv.Key));
    }

    [Fact]
    public void CheckerEvaluatorAndDeltaAgreeUnderBothMethods()
    {
        foreach (var m in new[] { ObjectiveMethod.Weights, ObjectiveMethod.Priority })
        {
            ObjectiveWeights.Method = m;
            foreach (var name in CheckerSnapshotTest.Fixtures)
            {
                var st = CheckerSnapshotTest.Load(name);
                var p = new Problem(st);
                var ev = new Evaluator(p);
                var rng = new Random(11);
                var board = st.Schedule.ToIntArray2D();
                var de = new DeltaEvaluator(p);
                de.Reset(board.Copy2D());
                for (var step = 0; step < 400; step++)
                {
                    int i = rng.Next(p.S), j = rng.Next(p.T), k = rng.Next(p.K);
                    de.Apply(i, j, k); board[i][j] = k;
                    if (step % 40 != 0) continue;
                    var full = ev.FullEval(board);
                    Assert.True(full == de.Score(), $"{m} {name} #{step} Δ と全体");
                    var rep = UnifiedViolationChecker.Check(st, board);
                    var hardW = MirrorKeys.Hard.Sum(f => rep.Breakdown.GetValueOrDefault(f, 0) * ObjectiveWeights.Of(f));
                    Assert.True(Math.Abs(full % Evaluator.SCORE_HARD_UNIT - (rep.WeightedScore - hardW)) < 1e-6, $"{m} {name} #{step} 評価器の soft とチェッカー");
                }
            }
        }
    }
}
