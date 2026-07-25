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
}
