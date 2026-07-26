package com.magi.app.v6

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * [3.230.0/停滞ウォッチドッグの分離] ドッグフーディングで発見: 旧実装は
 * `max(lastBestImproveMs, lastPhaseChangeMs)` を単一の stallMs(270s相当) と比較しており、
 * 20〜90秒間隔で頻発するフェーズ遷移（RSI各ラウンド・ALNS各restart等）のたびにタイマが
 * リセットされ続け、実質的に一度も発火し得なかった（実機ログでPhase1完了直後から270秒以上
 * 一切改善が無いまま予算を使い切る事例を確認）。分離後は「現フェーズ自身の短い個別猶予
 * (phaseGraceMs)」と「真の頭打ち検知(lastBestImproveMs単独)」を独立した AND 条件にする。
 */
class V6FinalPortTest {
    private val minRunMs = 45_000L
    private val phaseGraceMs = 7_500L
    private val effStall = 270_000L

    @Test fun doesNotFireBeforeMinRunElapses() {
        // 起動直後（改善無し・フェーズ変化無しでも）は最初の猶予(minRunMs)内なら発火しない。
        assertFalse(
            V6FinalPort.watchdogStagnationFired(
                now = 40_000L, startMs = 0L, minRunMs = minRunMs,
                lastPhaseChangeMs = 0L, phaseGraceMs = phaseGraceMs,
                lastBestImproveMs = 0L, effStall = effStall,
            ),
        )
    }

    @Test fun doesNotFireWhenCurrentPhaseJustStarted() {
        // 現フェーズが始まったばかり(phaseGraceMs未満)なら、改善が大昔でも即座には打ち切らない
        // （新フェーズが何も試していない瞬間の誤検知防止）。
        assertFalse(
            V6FinalPort.watchdogStagnationFired(
                now = 300_000L, startMs = 0L, minRunMs = minRunMs,
                lastPhaseChangeMs = 299_000L, phaseGraceMs = phaseGraceMs,   // 現フェーズは1秒前に開始
                lastBestImproveMs = 10_000L, effStall = effStall,
            ),
        )
    }

    // [核心/バグ再現] フェーズが頻繁に切り替わり続けていても(=lastPhaseChangeMsは常に「最近」)、
    // 実際の最終改善(lastBestImproveMs)からは effStall を超えて経過していれば発火すること。
    // 旧実装(max()合成)ではlastPhaseChangeMsが常に新しいため以下のケースは一生発火しなかった。
    @Test fun firesOnTrueStagnationDespiteFrequentPhaseTransitions() {
        val now = 300_000L
        val lastBestImproveMs = 10_000L      // 実際の改善はt=10sで止まっている
        val lastPhaseChangeMs = 290_000L      // フェーズはt=290sにも切り替わった（=直前）

        // 現フェーズ自身は10秒経過＝phaseGraceMs(7.5s)を超えている。
        assertTrue(now - lastPhaseChangeMs > phaseGraceMs)
        // 旧ロジック相当(max()合成)ではここが effStall を超えないため発火しなかったはずの検証:
        val oldStyleGate = now - maxOf(lastBestImproveMs, lastPhaseChangeMs)
        assertFalse("旧ロジックはこの状況で発火し得なかったことの確認", oldStyleGate > effStall)

        assertTrue(
            "フェーズが切り替わり続けていても、真の無改善時間がeffStallを超えれば発火すること",
            V6FinalPort.watchdogStagnationFired(
                now = now, startMs = 0L, minRunMs = minRunMs,
                lastPhaseChangeMs = lastPhaseChangeMs, phaseGraceMs = phaseGraceMs,
                lastBestImproveMs = lastBestImproveMs, effStall = effStall,
            ),
        )
    }

    @Test fun doesNotFireWhileImprovementsAreRecent() {
        // 最終改善が effStall 以内なら（フェーズも十分経過していても）発火しない＝品質不変の担保。
        assertFalse(
            V6FinalPort.watchdogStagnationFired(
                now = 300_000L, startMs = 0L, minRunMs = minRunMs,
                lastPhaseChangeMs = 100_000L, phaseGraceMs = phaseGraceMs,
                lastBestImproveMs = 290_000L, effStall = effStall,   // 10秒前に改善
            ),
        )
    }

    // ==== [3.281.0/停滞レビューA] effectiveStallMs＝c3n構造壁(証明つき)の plateau 移行 ====

    private val stallHard = 37_500L
    private val stallLong = 270_000L

    @Test fun effectiveStallUsesShortStallForBasePlateau() {
        // 従来どおり: bestHard<=hardFloor かつ 非covU HARD=0 → 短い閾値（挙動不変の回帰）。
        assertEquals(stallHard, V6FinalPort.effectiveStallMs(0, 0, 0, false, false, stallHard, stallLong))
        assertEquals(stallHard, V6FinalPort.effectiveStallMs(2, 2, 0, false, false, stallHard, stallLong))
    }

    @Test fun effectiveStallUsesShortStallWhenC3nWallProven() {
        // 実機ログ再現: hardFloor=0・c3n=1のみ残存・ForbiddenDiagが壁を証明 → 短い閾値へ移行
        //   （旧実装は常に270s＝300s予算では構造的に発火不能だった）。
        assertEquals(stallHard, V6FinalPort.effectiveStallMs(1, 0, 1, true, true, stallHard, stallLong))
    }

    @Test fun effectiveStallKeepsLongStallWhenWallUnproven() {
        // 証明が無い（診断未実行/崩す手が実在する）間は従来どおり長い閾値で粘る＝品質側に倒す。
        assertEquals(stallLong, V6FinalPort.effectiveStallMs(1, 0, 1, true, false, stallHard, stallLong))
    }

    @Test fun effectiveStallKeepsLongStallWhenOtherNonCovUHardRemains() {
        // groupViol/pref が混在（nonCovUAllC3n=false）なら、たとえ壁が証明されていても長い閾値のまま
        //   ＝解ける可能性のある HARD を早々に諦めない。
        assertEquals(stallLong, V6FinalPort.effectiveStallMs(2, 0, 2, false, true, stallHard, stallLong))
    }

    @Test fun effectiveStallKeepsLongStallWhenCovUAboveFloor() {
        // covU が構造床より高い（まだ下げられる）間は c3n 壁が証明済みでも長い閾値で粘る。
        //   bestHard(3) > hardFloor(0)+nonCovU(1) ＝ covU 部分が床超過。
        assertEquals(stallLong, V6FinalPort.effectiveStallMs(3, 0, 1, true, true, stallHard, stallLong))
    }
}
