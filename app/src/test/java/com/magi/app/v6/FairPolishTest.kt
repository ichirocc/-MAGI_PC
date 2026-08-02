package com.magi.app.v6

import com.magi.app.model.Group
import com.magi.app.model.MagiState
import com.magi.app.model.Shift
import com.magi.app.model.Staff
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * [FairPolish] ユーザー指示「c42/c42s以外にも『動かせるか』専用オペレータの欠如が無いか棚卸しする」で
 * 発見（AptPolish=3.223.0と同型の穴）。fairは群×担当ONシフトのround(平均)からのL1偏差＝目標が現在の
 * 配置に応じて動く点がaptの固定目標と異なるため、①自己振替 ②同一グループ相互交換 ③玉突きチェーン、
 * の3段構成を検証する。
 */
class FairPolishTest {

    // 手①: 4人グループ(A,B,C,D)・休は誰も使わず常に中立。A だけが X で過多・Y で過少（B/C/Dは両方とも
    // ちょうど目標どおり=中立）。自己振替(A: X→Y 1日)だけで両シフトとも厳密に0まで解消する。
    private fun selfSwapState(): MagiState {
        val shifts = listOf(Shift("休", "休", "", ""), Shift("X", "X", "", ""), Shift("Y", "Y", "", ""))
        val groups = listOf(Group("G0", "G0"))
        val groupShift = listOf(listOf(1, 1, 1))
        val staff = listOf(Staff("A", 0), Staff("B", 0), Staff("C", 0), Staff("D", 0))
        val schedule = listOf(
            listOf(1, 1, 1, 2), // A: X,X,X,Y (X=3 過多 / Y=1 過少)
            listOf(1, 1, 2, 2), // B: X,X,Y,Y (X=2 / Y=2 ＝目標どおり)
            listOf(1, 1, 2, 2), // C: 同上
            listOf(1, 1, 2, 2), // D: 同上
        )
        return MagiState(
            startDate = "2026-08-01", endDate = "2026-08-04",
            shifts = shifts, groups = groups, staff = staff, use2Patterns = false,
            groupShift = groupShift, groupShiftApt = listOf(listOf("", "", "")),
            schedule = schedule, wishes = emptyMap(), staffRange = emptyMap(),
            needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(),
            cons3n = emptyList(), cons3m = emptyList(), cons3mn = emptyList(),
            cons41 = emptyList(), cons42 = emptyList(),
        )
    }

    @Test
    fun fairPolishResolvesViaSelfSwapWhenSameStaffHasOppositeImbalance() {
        val st = selfSwapState()
        val sched = st.schedule.toIntArray2D()
        val before = UnifiedViolationChecker.check(st, sched)
        assertTrue("初期はfair違反があること", (before.breakdown["fair"] ?: 0) > 0)
        assertEquals("初期HARD=0", 0, before.hard)

        val result = V6HotfixPasses.applyFairPolish(st, sched)
        val after = UnifiedViolationChecker.check(st, result.newSchedule)

        assertEquals("自己振替後はfair=0", 0, after.breakdown["fair"] ?: -1)
        assertEquals("HARDは悪化しない", 0, after.hard)
        assertTrue("実際に手が採用されている", result.applied > 0)
    }

    // 手①(複数人): 2人グループ(A,B)・休とXのみ。AがXで過多(休で過少)・BがXで過少(休で過多)という
    // 互いに鏡像の不均衡を持つ最小盤面。各自が自分自身の中で解消できる(自己振替×2)ため、相手を
    // 必要とせず両者とも独立に解消し、最終的にfair=0まで到達することを確認する。
    private fun twoStaffState(): MagiState {
        val shifts = listOf(Shift("休", "休", "", ""), Shift("X", "X", "", ""))
        val groups = listOf(Group("G0", "G0"))
        val groupShift = listOf(listOf(1, 1))
        val staff = listOf(Staff("A", 0), Staff("B", 0))
        val schedule = listOf(
            listOf(1, 1), // A: X,X（Xへ過多・休へ過少）
            listOf(0, 0), // B: 休,休（休へ過多・Xへ過少）
        )
        return MagiState(
            startDate = "2026-08-01", endDate = "2026-08-02",
            shifts = shifts, groups = groups, staff = staff, use2Patterns = false,
            groupShift = groupShift, groupShiftApt = listOf(listOf("", "")),
            schedule = schedule, wishes = emptyMap(), staffRange = emptyMap(),
            needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(),
            cons3n = emptyList(), cons3m = emptyList(), cons3mn = emptyList(),
            cons41 = emptyList(), cons42 = emptyList(),
        )
    }

    @Test
    fun fairPolishResolvesMultipleStaffIndependently() {
        val st = twoStaffState()
        val sched = st.schedule.toIntArray2D()
        val before = UnifiedViolationChecker.check(st, sched)
        assertTrue("初期はfair違反(AとBが鏡像に偏っている)があること", (before.breakdown["fair"] ?: 0) > 0)

        val result = V6HotfixPasses.applyFairPolish(st, sched)
        val after = UnifiedViolationChecker.check(st, result.newSchedule)

        assertEquals("両者とも独立に解消しfair=0", 0, after.breakdown["fair"] ?: -1)
        assertEquals("HARDは悪化しない", 0, after.hard)
        assertTrue("実際に手が採用されている", result.applied > 0)
    }

    // 手③: GA={A,B2}(休,X,Y担当可)・GB={C}(休,X,Z担当可、m<2のためfair対象外＝玉突きの供給源専用)。
    // Aが唯一のX担当可能者でXを独占(need1=1)＝自己振替はcovU悪化で全日ブロック。B2は鏡像の低不均衡を
    // 持つ潜在的な相互交換相手だが両日とも希望固定(Y)で動かせない＝相互交換も不成立。残るのは
    // 玉突きチェーン(A→別シフトへ直接移動、Xの空き穴はCがZから玉突きで埋める)のみ。
    private fun chainState(): MagiState {
        val shifts = listOf(
            Shift("休", "休", "", ""),
            Shift("X", "X", "1", ""),
            Shift("Y", "Y", "", ""),
            Shift("Z", "Z", "", ""),
        )
        val groups = listOf(Group("GA", "GA"), Group("GB", "GB"))
        val groupShift = listOf(
            listOf(1, 1, 1, 0), // GA(A,B2) = 休,X,Y
            listOf(1, 1, 0, 1), // GB(C) = 休,X,Z
        )
        val staff = listOf(Staff("A", 0), Staff("B2", 0), Staff("C", 1))
        val schedule = listOf(
            listOf(1, 1), // A  = X,X（Xを独占＝過多、Yは0＝過少）
            listOf(2, 2), // B2 = Y,Y（Xは0＝過少、Yは過多＝Aと鏡像の相手だが希望固定で動かせない）
            listOf(3, 3), // C  = Z,Z（需要なし＝いつでも動かせる玉突きの供給源）
        )
        return MagiState(
            startDate = "2026-08-01", endDate = "2026-08-02",
            shifts = shifts, groups = groups, staff = staff, use2Patterns = false,
            groupShift = groupShift, groupShiftApt = listOf(listOf("", "", "", ""), listOf("", "", "", "")),
            schedule = schedule,
            wishes = mapOf("1,0" to 2, "1,1" to 2), // B2 両日とも Y に希望固定＝相互交換の相手として使えない
            staffRange = emptyMap(), needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(),
            cons3n = emptyList(), cons3m = emptyList(), cons3mn = emptyList(),
            cons41 = emptyList(), cons42 = emptyList(),
        )
    }

    @Test
    fun fairPolishResolvesViaChainWhenSelfAndMutualSwapAreBlocked() {
        val st = chainState()
        val sched = st.schedule.toIntArray2D()
        val before = UnifiedViolationChecker.check(st, sched)
        assertTrue("初期はfair違反があること", (before.breakdown["fair"] ?: 0) > 0)
        assertEquals("初期HARD=0(AがXを単独充足)", 0, before.hard)

        val result = V6HotfixPasses.applyFairPolish(st, sched, seed = 1L)
        val after = UnifiedViolationChecker.check(st, result.newSchedule)

        // [注記] 自己振替(covU悪化のため全日ブロック)・相互交換(B2希望固定のためブロック)のいずれも
        //   使えず、玉突きチェーン(直接移動＋Cによる穴埋め)だけが唯一の経路。目標が現在配置で動く
        //   fairの性質上、収束に複数パスを要し得るため厳密な0到達でなく「改善した・退化していない」を
        //   検証する（安全性=covU/HARD不変が本テストの核心）。
        assertTrue("fairが改善している(玉突きが実際に効いている)",
            (after.breakdown["fair"] ?: 0) < (before.breakdown["fair"] ?: 0))
        assertEquals("HARDは悪化しない", 0, after.hard)
        assertEquals("Xの被覆(covU)は玉突きで保たれる", 0, after.breakdown["covU"] ?: -1)
        assertTrue("実際に手が採用されている", result.applied > 0)
    }

    // [3.260.0, AptPolishと同型の穴] 手①(自己振替)は旧実装だと1パスにつき(i,k)ペア1回成功したら
    // 次のhighTargetsへ移っており、excess/deficitが複数単位ある職員は1パスで1単位しか解消できなかった。
    private fun multiUnitSelfSwapState(): MagiState {
        val shifts = listOf(Shift("休", "休", "", ""), Shift("X", "X", "", ""), Shift("Y", "Y", "", ""))
        val groups = listOf(Group("G0", "G0"))
        val groupShift = listOf(listOf(1, 1, 1))
        val staff = listOf(Staff("A", 0), Staff("B", 0), Staff("C", 0), Staff("D", 0))
        val schedule = listOf(
            listOf(1, 1, 1, 1, 1, 0, 0), // A: X*5, 休*2 (X過多・Yは0で過少)
            listOf(1, 1, 2, 2, 0, 0, 0), // B: X*2,Y*2,休*3
            listOf(1, 1, 2, 2, 0, 0, 0), // C: 同上
            listOf(1, 1, 2, 2, 0, 0, 0), // D: 同上
        )
        return MagiState(
            startDate = "2026-08-01", endDate = "2026-08-07",
            shifts = shifts, groups = groups, staff = staff, use2Patterns = false,
            groupShift = groupShift, groupShiftApt = listOf(listOf("", "", "")),
            schedule = schedule, wishes = emptyMap(), staffRange = emptyMap(),
            needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(),
            cons3n = emptyList(), cons3m = emptyList(), cons3mn = emptyList(),
            cons41 = emptyList(), cons42 = emptyList(),
        )
    }

    @Test
    fun fairPolishExhaustsSelfSwapWithinSinglePassForMultiUnitImbalance() {
        val st = multiUnitSelfSwapState()
        val sched = st.schedule.toIntArray2D()
        val before = UnifiedViolationChecker.check(st, sched)
        assertEquals("初期fair偏差=8", 8, before.breakdown["fair"] ?: -1)

        // maxPasses=1に固定し、1パス内での自己振替の反復可否そのものを検証する。
        val result = V6HotfixPasses.applyFairPolish(st, sched, maxPasses = 1)
        val after = UnifiedViolationChecker.check(st, result.newSchedule)

        // [3.345.0] weekly が「職員×シフト×曜日」になり同じ目的関数の中で自己振替と競合するため、
        //   1パスで fair=0 まで到達するとは限らなくなった（この盤面は T=7＝曜日が各1回で weekly が最も強く効く）。
        //   固定したいのは「1パス内で自己振替が反復される」ことなので、そこを直接見る。
        assertTrue("1パス内で複数回の手が採用されている(旧実装は1回で打ち切っていた)", result.applied > 1)
        assertTrue("fair は厳密に減る", (after.breakdown["fair"] ?: 99) < (before.breakdown["fair"] ?: 0))
        assertEquals("HARDは悪化しない", 0, after.hard)
        // 反復の継続で最終的には解消しきる（1パスの打ち切りが原因でないことの確認）。
        val settled = UnifiedViolationChecker.check(st, V6HotfixPasses.applyFairPolish(st, result.newSchedule, maxPasses = 4).newSchedule)
        assertEquals("パスを重ねれば fair=0 まで解消される", 0, settled.breakdown["fair"] ?: -1)
    }

    @Test
    fun fairPolishIsNoOpWhenAlreadyBalanced() {
        val st = selfSwapState().copy(
            schedule = listOf(
                listOf(1, 1, 2, 2),
                listOf(1, 1, 2, 2),
                listOf(1, 1, 2, 2),
                listOf(1, 1, 2, 2),
            ),
        )
        val sched = st.schedule.toIntArray2D()
        val before = UnifiedViolationChecker.check(st, sched)
        assertEquals("既に均等配置ならfair=0", 0, before.breakdown["fair"] ?: -1)

        val result = V6HotfixPasses.applyFairPolish(st, sched)
        assertEquals(0, result.applied)
    }
}
