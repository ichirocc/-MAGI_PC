using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [3.385.0] <c>PublishLiveBest</c> が publish する「評価」と「盤面」が食い違わないことを固定する。
///
/// 旧実装は report を CAS してから<b>その外で</b>盤面を代入していたため、CAS に勝った直後に
/// プリエンプトされたスレッドが、より良い盤面を書いた後続スレッドを<b>劣る盤面で上書き</b>できた
/// （<c>liveBestReport</c> は最良なのに <c>LiveBest</c> は劣る盤面＝doc が謳う単調性が破れる）。
///
/// 競合そのものは確率的にしか再現できないので、ここでは<b>盤面に自分の評価値を埋め込み</b>、
/// 「最後に残った盤面が、publish された最良の評価に対応していること」を検査する。
/// 盤面コピーが O(職員×日) かかることが窓を作るので、実データ規模（30×31）で回す。
/// </summary>
public class LiveBestPublishTest
{
    private static ViolationReport Rep(int hard) => new(
        Violations: new Dictionary<string, string>(),
        NeedViolations: new Dictionary<string, string>(),
        CountViolations: new Dictionary<string, string>(),
        Breakdown: new Dictionary<string, int>(),
        Total: hard,
        Hard: hard,
        Soft: 0,
        WeightedScore: hard);

    /// <summary>全セルに <c>mark</c> を敷いた盤面（どのセルを読んでも由来が分かる＝部分的な上書きも検出できる）。</summary>
    private static int[][] Board(int mark, int staff = 30, int days = 31) =>
        Enumerable.Range(0, staff).Select(_ => Enumerable.Repeat(mark, days).ToArray()).ToArray();

    /// <summary>置き換えで打ち切られた旧実行の盤面（職員数・日数が違う）は、新しい実行の途中最良として返さない。</summary>
    [Fact]
    public void ShapedLiveBestRejectsAnotherRunsBoard()
    {
        V6NativeOptimizer.ResetLiveBestForTest();
        V6NativeOptimizer.PublishLiveBest(Rep(1), Board(1, staff: 10, days: 31));
        Assert.Null(V6NativeOptimizer.LiveBestShaped(Board(0, staff: 11, days: 31)));
        Assert.Null(V6NativeOptimizer.LiveBestShaped(Board(0, staff: 10, days: 30)));
        Assert.Equal(10, V6NativeOptimizer.LiveBestShaped(Board(0, staff: 10, days: 31))?.Count);
        V6NativeOptimizer.ResetLiveBestForTest();
    }

    [Fact]
    public void PublishedBoardAlwaysMatchesThePublishedBest()
    {
        const int threads = 8;
        const int perThread = 400;
        // 評価は小さいほど良い（hard が第1キー）。全スレッドを通した最小値が最終的な勝者。
        const int best = 1;
        const int worst = threads * perThread;

        for (var attempt = 0; attempt < 6; attempt++)
        {
            V6NativeOptimizer.ResetLiveBestForTest();
            Assert.Null(V6NativeOptimizer.LiveBest); // 各試行はクリアな状態から始める

            using var barrier = new Barrier(threads);
            Exception? failure = null;
            var ts = Enumerable.Range(0, threads).Select(t => new Thread(() =>
            {
                try
                {
                    barrier.SignalAndWait();
                    // 各スレッドが降順に publish する＝常に「勝てる」候補を出し続けて競合を最大化。
                    for (var i = 0; i < perThread; i++)
                    {
                        var h = worst - (i * threads + t);
                        V6NativeOptimizer.PublishLiveBest(Rep(h), Board(h));
                    }
                }
                catch (Exception e)
                {
                    Interlocked.CompareExchange(ref failure, e, null);
                }
            })).ToList();
            ts.ForEach(th => th.Start());
            ts.ForEach(th => th.Join());
            if (failure is not null) throw new Exception($"publish 中に例外 (attempt={attempt})", failure);

            var b = V6NativeOptimizer.LiveBest ?? throw new Exception($"publish したのに LiveBest が null (attempt={attempt})");
            // 盤面に埋め込んだ評価値が最良と一致していなければ、劣るスレッドが後から上書きしている。
            Assert.True(best == b[0][0], $"最終盤面が最良の評価に対応していない (attempt={attempt})");
            foreach (var row in b)
                foreach (var v in row)
                    Assert.True(best == v, $"盤面の一部だけ別の publish 由来になっている (attempt={attempt})");
        }
    }

    /// <summary>劣る評価では publish されない（既存の単調性）。</summary>
    [Fact]
    public void WorseOrEqualPublishIsIgnored()
    {
        V6NativeOptimizer.ResetLiveBestForTest();
        V6NativeOptimizer.PublishLiveBest(Rep(5), Board(5));
        Assert.Equal(5, V6NativeOptimizer.LiveBest![0][0]);

        V6NativeOptimizer.PublishLiveBest(Rep(9), Board(9)); // 劣る
        Assert.True(5 == V6NativeOptimizer.LiveBest![0][0], "劣る publish で退行してはいけない");

        V6NativeOptimizer.PublishLiveBest(Rep(5), Board(7)); // 同値
        Assert.True(5 == V6NativeOptimizer.LiveBest![0][0], "同値の publish でも差し替えない");

        V6NativeOptimizer.PublishLiveBest(Rep(2), Board(2)); // 真に改善
        Assert.Equal(2, V6NativeOptimizer.LiveBest![0][0]);
    }

    /// <summary>publish 後に呼出側が自分の盤面を書き換えても、publish 済みスナップショットは動かない。</summary>
    [Fact]
    public void PublishedSnapshotIsIndependentOfTheCallersArray()
    {
        V6NativeOptimizer.ResetLiveBestForTest();
        var mine = Board(3);
        V6NativeOptimizer.PublishLiveBest(Rep(3), mine);
        mine[0][0] = 99;
        Assert.True(3 == V6NativeOptimizer.LiveBest![0][0], "publish はコピーであって参照共有ではない");
    }
}
