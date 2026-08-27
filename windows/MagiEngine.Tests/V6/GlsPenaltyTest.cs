using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>1:1 port of Kotlin's <c>GlsPenaltyTest.kt</c>: extension cost, max-util reinforcement, range safety, decay.</summary>
public class GlsPenaltyTest
{
    private static readonly int[][] Sched = { new[] { 0, 1, 2 }, new[] { 3, 0, 1 } }; // 2人×3日, K=4

    [Fact]
    public void AugmentIsZeroUntilPenalized()
    {
        var gls = new GlsPenalty(2, 3, 4, lambda: 10.0);
        Assert.Equal(0.0, gls.Augment(Sched), precision: 9);
    }

    [Fact]
    public void PenalizesLeastPenalizedViolatingCellThenRotates()
    {
        var gls = new GlsPenalty(2, 3, 4, lambda: 10.0);
        var cells = new (int I, int J)[] { (0, 1), (1, 0) }; // 割当 k=1 と k=3
        // 1回目: 両方 penalty=0 → util同点で先頭(0,1)を強化
        Assert.True(gls.PenalizeWorst(Sched, cells));
        Assert.Equal(1, gls.PenaltyOf(0, 1, 1));
        Assert.Equal(10.0, gls.Augment(Sched), precision: 9); // lambda*1
        // 2回目: (0,1)は penalty1→util0.5, (1,0)は0→util1.0 → (1,0)を強化（util最大へローテート）
        Assert.True(gls.PenalizeWorst(Sched, cells));
        Assert.Equal(1, gls.PenaltyOf(1, 0, 3));
        Assert.Equal(20.0, gls.Augment(Sched), precision: 9); // lambda*(1+1)
        Assert.Equal(2, gls.KickCount());
    }

    [Fact]
    public void NoCandidateReturnsFalseAndIsRangeSafe()
    {
        var gls = new GlsPenalty(2, 3, 4, lambda: 10.0);
        Assert.False(gls.PenalizeWorst(Sched, Array.Empty<(int, int)>()));
        Assert.False(gls.PenalizeWorst(Sched, new (int, int)[] { (5, 5), (9, 0) })); // 範囲外
        Assert.Equal(0, gls.KickCount());
    }

    [Fact]
    public void SeverityBiasesSelection()
    {
        var gls = new GlsPenalty(2, 3, 4, lambda: 1.0);
        // (1,0) の severity を高く → penalty同点でも (1,0) が選ばれる
        gls.PenalizeWorst(Sched, new (int I, int J)[] { (0, 1), (1, 0) }, (i, _) => i == 1 ? 5.0 : 1.0);
        Assert.Equal(1, gls.PenaltyOf(1, 0, 3));
        Assert.Equal(0, gls.PenaltyOf(0, 1, 1));
    }

    [Fact]
    public void DecayShrinksPenaltyAndAugment()
    {
        var gls = new GlsPenalty(2, 3, 4, lambda: 10.0);
        var cell = new (int I, int J)[] { (0, 1) };
        for (int n = 0; n < 10; n++) gls.PenalizeWorst(Sched, cell); // (0,1)割当 k=1 を penalty=10 まで強化
        Assert.Equal(10, gls.PenaltyOf(0, 1, 1));
        Assert.Equal(100.0, gls.Augment(Sched), precision: 9); // lambda*10
        gls.Decay(80); // 10*80/100 = 8（整数床）
        Assert.Equal(8, gls.PenaltyOf(0, 1, 1));
        Assert.Equal(80.0, gls.Augment(Sched), precision: 9); // lambda*8
    }

    [Fact]
    public void DecayRemovesEntriesReachingZero()
    {
        var gls = new GlsPenalty(2, 3, 4, lambda: 10.0);
        gls.PenalizeWorst(Sched, new (int I, int J)[] { (0, 1) }); // penalty=1
        Assert.Equal(1, gls.PenaltyOf(0, 1, 1));
        Assert.Equal(0, gls.Decay(50)); // 1*50/100=0 → 除去 → 残り0項目
        Assert.Equal(0, gls.PenaltyOf(0, 1, 1));
        Assert.Equal(0.0, gls.Augment(Sched), precision: 9);
    }

    // [レビュー#8 3.213.0] decay の値域契約（100超=増幅・負値は無意味）を固定。
    [Fact]
    public void DecayRejectsOutOfRangeKeepPercent()
    {
        var gls = new GlsPenalty(2, 3, 4, lambda: 10.0);
        Assert.Throws<ArgumentException>(() => gls.Decay(101));
        Assert.Throws<ArgumentException>(() => gls.Decay(-1));
        Assert.Equal(0, gls.Decay(80)); // 有効値は従来どおり（空 penalty → 0 件）
    }
}
