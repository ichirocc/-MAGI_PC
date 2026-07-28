package com.magi.app.v6

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class HypothesisEpochPolicyTest {
    @Test
    fun w0RemainsPermanentSafetyFloor() {
        // [3.278.0] safetyFloor フィールドはデッドコードとして撤去（設計意図は role 分岐自体が担保）。
        for (r in 0..20) {
            val a = AdaptiveHypothesisEpochPolicy.assignmentFor(0, r)
            assertEquals(HypothesisEpochRole.BASELINE_REFINE, a.role)
            assertFalse(AdaptiveHypothesisEpochPolicy.shouldReassign(0, false, 99, 0))
        }
    }

    @Test
    fun w4TurnsIntoEliteRelinkingAfterFirstPlateau() {
        assertEquals(
            HypothesisEpochRole.BASELINE_REFINE,
            AdaptiveHypothesisEpochPolicy.assignmentFor(4, 0).role,
        )
        assertEquals(
            HypothesisEpochRole.ELITE_RELINK,
            AdaptiveHypothesisEpochPolicy.assignmentFor(4, 1).role,
        )
    }

    @Test
    fun sixEscapeWorkersRotateAcrossAllEscapeRoles() {
        val expected = setOf(
            HypothesisEpochRole.DAY_BLOCK_ALNS,
            HypothesisEpochRole.HARD_FAMILY_RSI,
            HypothesisEpochRole.HARD_DEBT_RSI_PLUS,
            HypothesisEpochRole.LARGE_DESTROY_ALNS,
            HypothesisEpochRole.PERSONAL_RSI,
            HypothesisEpochRole.MAX_DISTANCE_RSI_PLUS,
        )
        for (slot in listOf(1, 2, 3, 5, 6, 7)) {
            val roles = (0 until 6).map {
                AdaptiveHypothesisEpochPolicy.assignmentFor(slot, it).role
            }.toSet()
            assertEquals("W$slot must visit every escape role", expected, roles)
        }
    }

    @Test
    fun duplicateBasinForcesReassignmentEvenAfterScalarImprovement() {
        assertTrue(
            AdaptiveHypothesisEpochPolicy.shouldReassign(
                index = 2,
                improvedThisEpoch = true,
                stagnantEpochs = 0,
                nearestOtherDistance = AdaptiveHypothesisEpochPolicy.DUPLICATE_DISTANCE_CELLS,
            ),
        )
    }

    @Test
    fun plateauReassignsButProgressKeepsCurrentRole() {
        assertTrue(
            AdaptiveHypothesisEpochPolicy.shouldReassign(
                index = 3,
                improvedThisEpoch = false,
                stagnantEpochs = 1,
                nearestOtherDistance = 50,
            ),
        )
        assertFalse(
            AdaptiveHypothesisEpochPolicy.shouldReassign(
                index = 3,
                improvedThisEpoch = true,
                stagnantEpochs = 0,
                nearestOtherDistance = 50,
            ),
        )
    }

    @Test
    fun improvingRoleGetsLongerButDeadlineClampedQuantum() {
        val alns = AdaptiveHypothesisEpochPolicy.assignmentFor(1, 0)
        val plus = AdaptiveHypothesisEpochPolicy.assignmentFor(0, 0)
        assertEquals(5, AdaptiveHypothesisEpochPolicy.quantumSeconds(alns, false, 100))
        assertEquals(8, AdaptiveHypothesisEpochPolicy.quantumSeconds(alns, true, 100))
        assertEquals(35, AdaptiveHypothesisEpochPolicy.quantumSeconds(plus, false, 100))
        assertEquals(45, AdaptiveHypothesisEpochPolicy.quantumSeconds(plus, true, 100))
        assertEquals(3, AdaptiveHypothesisEpochPolicy.quantumSeconds(plus, true, 3))
        assertEquals(0, AdaptiveHypothesisEpochPolicy.quantumSeconds(alns, false, 0))
    }

    @Test
    fun epochAndReassignmentChangeSeed() {
        val a = AdaptiveHypothesisEpochPolicy.epochSeed(42L, 1, 0, 0)
        val b = AdaptiveHypothesisEpochPolicy.epochSeed(42L, 1, 1, 0)
        val c = AdaptiveHypothesisEpochPolicy.epochSeed(42L, 1, 1, 1)
        assertNotEquals(a, b)
        assertNotEquals(b, c)
    }

    // ---------------------------------------------------------------------------------------
    // [3.308.0] 改善直後の長い量子を引き継ぐ条件（両経路が共有する契約）
    // ---------------------------------------------------------------------------------------

    @Test
    fun improvingQuantumIsNotInheritedAcrossARoleChange() {
        // 前の役割が改善しても、役割が変わったら新しい役割は基準量子から始める。
        assertFalse(AdaptiveHypothesisEpochPolicy.carriesImprovingQuantum(true, roleChanged = true))
        // 役割が続くなら改善はそのまま次の量子へ効く。
        assertTrue(AdaptiveHypothesisEpochPolicy.carriesImprovingQuantum(true, roleChanged = false))
        // 改善していなければどちらでも基準量子。
        assertFalse(AdaptiveHypothesisEpochPolicy.carriesImprovingQuantum(false, roleChanged = false))
        assertFalse(AdaptiveHypothesisEpochPolicy.carriesImprovingQuantum(false, roleChanged = true))
    }

    @Test
    fun roleChangeCostsTheRsiPlusImprovingBonusInSeconds() {
        // 契約が実際に秒へ効くことを quantumSeconds まで通して固定する。
        val rsiPlus = AdaptiveHypothesisEpochPolicy.assignmentFor(
            HypothesisEpochRole.HARD_DEBT_RSI_PLUS, escapeDepth = 0,
        )
        val kept = AdaptiveHypothesisEpochPolicy.carriesImprovingQuantum(true, roleChanged = false)
        val changed = AdaptiveHypothesisEpochPolicy.carriesImprovingQuantum(true, roleChanged = true)
        assertEquals(
            AdaptiveHypothesisEpochPolicy.RSI_PLUS_IMPROVING_QUANTUM_SEC,
            AdaptiveHypothesisEpochPolicy.quantumSeconds(rsiPlus, kept, 999),
        )
        assertEquals(
            AdaptiveHypothesisEpochPolicy.RSI_PLUS_BASE_QUANTUM_SEC,
            AdaptiveHypothesisEpochPolicy.quantumSeconds(rsiPlus, changed, 999),
        )
    }

    @Test
    fun intensityGrowthClampsNegativeBasisToZero() {
        // 負の停滞深さは呼出側の想定外。基準強度へ丸め、例外にも負値にもしない。
        for (role in HypothesisEpochRole.values()) {
            assertEquals(
                AdaptiveHypothesisEpochPolicy.intensityFor(role, 0),
                AdaptiveHypothesisEpochPolicy.intensityFor(role, -5),
            )
        }
    }

    @Test
    fun defaultPathReassignmentDoesNotAlwaysChangeTheRole() {
        // [3.308.1/敵対検証] 「再配属＝必ず役割が変わる」は偽。W4 は2回目以降 ELITE_RELINK のまま。
        // roleChanged=true を渡しているのは旧挙動（常に基準量子へ戻す）の保存が目的であって、
        // 役割変更の主張ではない。この事実を固定しておかないと同じ誤解を再び書く。
        val w4 = (0..4).map { AdaptiveHypothesisEpochPolicy.assignmentFor(4, it).role }
        assertEquals(HypothesisEpochRole.BASELINE_REFINE, w4[0])
        for (r in 1..4) assertEquals(HypothesisEpochRole.ELITE_RELINK, w4[r])

        // 脱出役6本を回すワーカーは index が1つ進むので毎回変わる。
        for (slot in listOf(1, 2, 3, 5, 6, 7)) {
            val seq = (0..6).map { AdaptiveHypothesisEpochPolicy.assignmentFor(slot, it).role }
            for (r in 1..6) assertNotEquals(seq[r - 1], seq[r])
        }
    }
}
