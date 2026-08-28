using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [フェーズ7ピース7] <c>V6FinalPortTest.kt</c>の全17件を移植（<see cref="V6FinalPort.
/// WatchdogStagnationFired"/>/<see cref="V6FinalPort.EffectiveStallMs"/>/<see cref="V6FinalPort.
/// NormalStallMs"/>は移植時点で C# 側に既存のテストが1件も無かった）。
///
/// [3.230.0/停滞ウォッチドッグの分離] ドッグフーディングで発見: 旧実装は
/// <c>max(lastBestImproveMs, lastPhaseChangeMs)</c> を単一の stallMs(270s相当) と比較しており、
/// 20〜90秒間隔で頻発するフェーズ遷移（RSI各ラウンド・ALNS各restart等）のたびにタイマが
/// リセットされ続け、実質的に一度も発火し得なかった（実機ログでPhase1完了直後から270秒以上
/// 一切改善が無いまま予算を使い切る事例を確認）。分離後は「現フェーズ自身の短い個別猶予
/// (phaseGraceMs)」と「真の頭打ち検知(lastBestImproveMs単独)」を独立した AND 条件にする。
/// </summary>
public class V6FinalPortWatchdogTest
{
    private const long MinRunMs = 45_000L;
    private const long PhaseGraceMs = 7_500L;
    private const long EffStall = 270_000L;

    [Fact]
    public void DoesNotFireBeforeMinRunElapses()
    {
        // 起動直後（改善無し・フェーズ変化無しでも）は最初の猶予(minRunMs)内なら発火しない。
        Assert.False(V6FinalPort.WatchdogStagnationFired(
            now: 40_000L, startMs: 0L, minRunMs: MinRunMs,
            lastPhaseChangeMs: 0L, phaseGraceMs: PhaseGraceMs,
            lastBestImproveMs: 0L, effStall: EffStall));
    }

    [Fact]
    public void DoesNotFireWhenCurrentPhaseJustStarted()
    {
        // 現フェーズが始まったばかり(phaseGraceMs未満)なら、改善が大昔でも即座には打ち切らない
        // （新フェーズが何も試していない瞬間の誤検知防止）。
        Assert.False(V6FinalPort.WatchdogStagnationFired(
            now: 300_000L, startMs: 0L, minRunMs: MinRunMs,
            lastPhaseChangeMs: 299_000L, phaseGraceMs: PhaseGraceMs, // 現フェーズは1秒前に開始
            lastBestImproveMs: 10_000L, effStall: EffStall));
    }

    // [核心/バグ再現] フェーズが頻繁に切り替わり続けていても(=lastPhaseChangeMsは常に「最近」)、
    // 実際の最終改善(lastBestImproveMs)からは effStall を超えて経過していれば発火すること。
    // 旧実装(max()合成)ではlastPhaseChangeMsが常に新しいため以下のケースは一生発火しなかった。
    [Fact]
    public void FiresOnTrueStagnationDespiteFrequentPhaseTransitions()
    {
        const long now = 300_000L;
        const long lastBestImproveMs = 10_000L; // 実際の改善はt=10sで止まっている
        const long lastPhaseChangeMs = 290_000L; // フェーズはt=290sにも切り替わった（=直前）

        // 現フェーズ自身は10秒経過＝phaseGraceMs(7.5s)を超えている。
        Assert.True(now - lastPhaseChangeMs > PhaseGraceMs);
        // 旧ロジック相当(max()合成)ではここが effStall を超えないため発火しなかったはずの検証:
        var oldStyleGate = now - Math.Max(lastBestImproveMs, lastPhaseChangeMs);
        Assert.False(oldStyleGate > EffStall, "旧ロジックはこの状況で発火し得なかったことの確認");

        Assert.True(
            V6FinalPort.WatchdogStagnationFired(
                now: now, startMs: 0L, minRunMs: MinRunMs,
                lastPhaseChangeMs: lastPhaseChangeMs, phaseGraceMs: PhaseGraceMs,
                lastBestImproveMs: lastBestImproveMs, effStall: EffStall),
            "フェーズが切り替わり続けていても、真の無改善時間がeffStallを超えれば発火すること");
    }

    // [3.408.0/実機ログ 2026-08-19] 並列ワーカーが1本のフェーズ文字列を共有するため
    //   `lastPhaseChangeMs` が絶えず更新され、フェーズ猶予が**恒久的な拒否権**になっていた
    //   （実機: 停滞274s・閾値37s・発火なし・未発火の理由「現フェーズ猶予未達(実測0s/7s)」）。
    //   猶予は遅延であって検知を止める根拠は無い＝閾値の2倍で必ず発火する。
    [Fact]
    public void PhaseGraceDelaysButCanNeverVetoForever()
    {
        const long now = 300_000L;
        const long shortStall = 37_500L;
        // フェーズは常に「たった今」始まった状態（並列ワーカーの共有フェーズ名を再現）。
        const long alwaysFreshPhase = now - 1_000L;

        // 閾値は超えたが2倍には達していない＝猶予が効いて**まだ**発火しない。
        Assert.False(
            V6FinalPort.WatchdogStagnationFired(
                now: now, startMs: 0L, minRunMs: MinRunMs,
                lastPhaseChangeMs: alwaysFreshPhase, phaseGraceMs: PhaseGraceMs,
                lastBestImproveMs: now - shortStall - 1_000L, effStall: shortStall),
            "閾値超〜2倍未満のあいだは、フェーズ猶予が発火を遅らせる");

        // 2倍を超えたら、フェーズがいくら更新され続けていても発火する。
        Assert.True(
            V6FinalPort.WatchdogStagnationFired(
                now: now, startMs: 0L, minRunMs: MinRunMs,
                lastPhaseChangeMs: alwaysFreshPhase, phaseGraceMs: PhaseGraceMs,
                lastBestImproveMs: now - shortStall * V6FinalPort.StallOverrideFactor - 1_000L,
                effStall: shortStall),
            "フェーズ猶予は拒否権ではない＝閾値の2倍で必ず発火する");
    }

    // 実機ログそのものの再現: 停滞274s・実効閾値37s・フェーズは常に直近更新。
    [Fact]
    public void RealDeviceLogCaseNowFires()
    {
        const long now = 275_000L;
        Assert.True(V6FinalPort.WatchdogStagnationFired(
            now: now, startMs: 0L, minRunMs: MinRunMs,
            lastPhaseChangeMs: now, phaseGraceMs: 7_000L, // 実測0s/7s
            lastBestImproveMs: now - 274_000L, effStall: 37_000L));
    }

    [Fact]
    public void DoesNotFireWhileImprovementsAreRecent()
    {
        // 最終改善が effStall 以内なら（フェーズも十分経過していても）発火しない＝品質不変の担保。
        Assert.False(V6FinalPort.WatchdogStagnationFired(
            now: 300_000L, startMs: 0L, minRunMs: MinRunMs,
            lastPhaseChangeMs: 100_000L, phaseGraceMs: PhaseGraceMs,
            lastBestImproveMs: 290_000L, effStall: EffStall)); // 10秒前に改善
    }

    // ==== [3.281.0/停滞レビューA] effectiveStallMs＝c3n構造壁(証明つき)の plateau 移行 ====

    private const long StallHard = 37_500L;
    private const long StallLong = 270_000L;

    [Fact]
    public void EffectiveStallUsesShortStallForBasePlateau()
    {
        // 従来どおり: bestHard<=hardFloor かつ 非covU HARD=0 → 短い閾値（挙動不変の回帰）。
        Assert.Equal(StallHard, V6FinalPort.EffectiveStallMs(0, 0, 0, false, false, StallHard, StallLong));
        Assert.Equal(StallHard, V6FinalPort.EffectiveStallMs(2, 2, 0, false, false, StallHard, StallLong));
    }

    [Fact]
    public void EffectiveStallUsesShortStallWhenC3nWallProven()
    {
        // 実機ログ再現: hardFloor=0・c3n=1のみ残存・ForbiddenDiagが壁を証明 → 短い閾値へ移行
        //   （旧実装は常に270s＝300s予算では構造的に発火不能だった）。
        Assert.Equal(StallHard, V6FinalPort.EffectiveStallMs(1, 0, 1, true, true, StallHard, StallLong));
    }

    [Fact]
    public void EffectiveStallKeepsLongStallWhenWallUnproven()
    {
        // 証明が無い（診断未実行/崩す手が実在する）間は従来どおり長い閾値で粘る＝品質側に倒す。
        Assert.Equal(StallLong, V6FinalPort.EffectiveStallMs(1, 0, 1, true, false, StallHard, StallLong));
    }

    [Fact]
    public void EffectiveStallKeepsLongStallWhenOtherNonCovUHardRemains()
    {
        // groupViol/pref が混在（nonCovUAllC3n=false）なら、たとえ壁が証明されていても長い閾値のまま
        //   ＝解ける可能性のある HARD を早々に諦めない。
        Assert.Equal(StallLong, V6FinalPort.EffectiveStallMs(2, 0, 2, false, true, StallHard, StallLong));
    }

    [Fact]
    public void EffectiveStallKeepsLongStallWhenCovUAboveFloor()
    {
        // covU が構造床より高い（まだ下げられる）間は c3n 壁が証明済みでも長い閾値で粘る。
        //   bestHard(3) > hardFloor(0)+nonCovU(1) ＝ covU 部分が床超過。
        Assert.Equal(StallLong, V6FinalPort.EffectiveStallMs(3, 0, 1, true, true, StallHard, StallLong));
    }

    // ==== [3.422.0/ユーザー報告「停滞の早期終了が実質効いていない」・Part B / 3.424.0で基準是正]
    //   normalStallMs＝「通常」分岐の停滞閾値算出（PolishGate.normalStallFraction で外部化）。
    //   意味論=予算×割合、予算基準の値が探索区間内で発火し得ない帯だけ探索区間×割合へフォールバック ====

    [Fact]
    public void NormalStallMsPreservesLegacyBudgetBasisWhenReachable()
    {
        // [3.424.0/code-review是正の核心] 予算基準の値が探索区間内なら旧来の budgetMs*9/10 と
        //   ビット単位で同一（3.422.0 初版はここを 247,500 へ無計測で厳格化していた＝退行）。
        Assert.Equal(270_000L, V6FinalPort.NormalStallMs(300_000L, 275_000L, fraction: 0.9));
        Assert.Equal(216_000L, V6FinalPort.NormalStallMs(240_000L, 220_000L, fraction: 0.9));
        Assert.Equal(90_000L, V6FinalPort.NormalStallMs(100_000L, 91_666L, fraction: 0.9));
    }

    [Fact]
    public void NormalStallMsFallsBackToWindowFractionWhenBudgetBasisCannotFire()
    {
        // 60秒予算の実測帯（Part A の動機）: 予算×0.9=54s >= 探索区間52s＝発火不能 → 区間×0.9=46.8s。
        Assert.Equal(46_800L, V6FinalPort.NormalStallMs(60_000L, 52_000L, fraction: 0.9));
        // 境界: raw == window は「stalled > effStall が探索終了まで真になれない」＝発火不能側に分類。
        Assert.Equal(64_800L, V6FinalPort.NormalStallMs(80_000L, 72_000L, fraction: 0.9));
    }

    [Fact]
    public void NormalStallMsHonorsTwentySecondFloor()
    {
        // 小さな予算では両経路とも下限20秒でクランプ（旧来どおり）。
        Assert.Equal(20_000L, V6FinalPort.NormalStallMs(10_000L, 8_000L, fraction: 0.9));
        Assert.Equal(20_000L, V6FinalPort.NormalStallMs(20_000L, 12_000L, fraction: 0.5));
    }

    [Fact]
    public void NormalStallMsScalesWithFraction()
    {
        // fraction を下げるほど閾値は比例して下がり、大予算帯（見直しの条件の再測定対象＝
        //   blocked_covu 型の実機ログは 300s PORTFOLIO）でもノブが実際に効く。
        Assert.Equal(150_000L, V6FinalPort.NormalStallMs(300_000L, 275_000L, fraction: 0.5));
        Assert.Equal(30_000L, V6FinalPort.NormalStallMs(100_000L, 92_000L, fraction: 0.3));
    }

    [Fact]
    public void NormalStallMsReadsPolishGateByDefault()
    {
        // 引数を省略すると PolishGate.NormalStallFraction を読む（FilterC3nIncrease と同じ
        //   「デフォルト引数は呼び出し時評価」の配線パターン）。テスト後は必ず既定値へ復元する。
        var saved = PolishGate.NormalStallFraction;
        try
        {
            PolishGate.NormalStallFraction = 0.5;
            Assert.Equal(150_000L, V6FinalPort.NormalStallMs(300_000L, 275_000L));
            PolishGate.NormalStallFraction = 0.9;
            Assert.Equal(270_000L, V6FinalPort.NormalStallMs(300_000L, 275_000L));
        }
        finally
        {
            PolishGate.NormalStallFraction = saved;
        }
    }

    [Fact]
    public void NormalStallMsRejectsFractionsThatWouldDisableTheWatchdog()
    {
        // [3.424.0/code-review指摘] fraction>=1.0 は「閾値>=探索区間」＝Part A が直した到達不能バグの
        //   再現、NaN は toLong()=0→20秒床への暗黙の崩落＝最凶の早期終了。どちらも丸めず落とす。
        foreach (var bad in new[] { 1.0, 1.5, 0.0, -0.5, double.NaN, double.PositiveInfinity })
        {
            Assert.Throws<ArgumentException>(() => V6FinalPort.NormalStallMs(300_000L, 275_000L, fraction: bad));
        }
        // 0.999 は有効（フォールバック側でも閾値 < 探索区間が保たれる）。
        Assert.Equal(274_725L, V6FinalPort.NormalStallMs(300_000L, 275_000L, fraction: 0.999));
    }
}
