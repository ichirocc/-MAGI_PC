package com.magi.app.v6

import com.magi.app.model.C1Row
import com.magi.app.model.C3Row
import com.magi.app.model.Group
import com.magi.app.model.MagiState
import com.magi.app.model.Range
import com.magi.app.model.Shift
import com.magi.app.model.Staff
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * [C1研磨の再設計] applyC1WindowPolish に追加した回数保存移設プリミティブ（手R1=鏡像長方形・手R2=自己2日swap）
 * の検証。golden_state の残差解剖（Python実測、CLAUDE.md参照）で判明した「回数固定職員には追加系の手A/手Bが
 * 必ず棄却され、移設だけが唯一の改善手」という局面を、手計算で検証済みの最小盤面で再現する。
 * 2シフト・4日の最小構成（D=2窓・N=1回以上）を使い、各テストは「手A(同日スワップ)は不採用/不可能」
 * 「新設の手だけが採用される」ことを前提条件として固定する（乱数非依存・決定的）。
 */
class C1RelocationPolishTest {

    private fun shifts() = listOf(
        Shift("Y", "Y", "", ""),   // index0 = 汎用シフト（need無し）
        Shift("X", "X", "", ""),  // index1 = cons1 対象シフト（need無し・c1のみで縛る）
    )

    /**
     * 手R1（鏡像長方形）検証用の盤面。D=2,N=1窓・T=4日。
     * i(職員0)=[X,X,Y,Y]: 手計算済み内訳 — 窓[0,1]=2X(余剰,day0安全) / 窓[1,2]=1X(day1は窓[1,2]がz=1<=n=1で危険=非donor)
     *   / 窓[2,3]=0X(不足=deficient)。donors={day0}のみ。
     * i2(職員1)=[Y,Y,X,X]: 窓[0,1]=0X(deficient) / 窓[1,2]=1X(ok) / 窓[2,3]=2X(ok)。
     * 手A(同日j=2のみ交換)を手計算すると: i 1→0fire(-1)・i2 1→2fire(+1)=総和±0で不採用（isBetterが拒否）。
     * 手R1(day0とday2を同時に交換)は: i 1→0fire(-1)・i2 1→1fire(±0, 窓[0,1]解消/窓[1,2]新規で相殺)=総和-1で採用。
     * 両職員とも同一グループ・両シフト担当可、needもwishもcons3nも無し＝covU/HARD不変。
     * [3.287.0 keep-best統一で強化] docstring どおりの「回数固定職員」を staffRange の厳密ピン(X=2固定)で
     * 実際に表現する。旧盤面はピン未設定で、weighted優先化後は「c1(15)を weekly(1)等と交換する count-changing 手」
     * が正当な改善として追加採用され、回数保存アサーションが破れた（挙動は正しい）。ピンを立てることで
     * count-changing 手は low/high(90/45)+exactPinRegression で拒否され、本テストの意図（移設だけが唯一の
     * 改善手である局面で R1 が機能する）が新旧どちらの比較器でも成立する。
     */
    private fun mirrorState(): MagiState {
        val groups = listOf(Group("G0", "G0"))
        val staff = listOf(Staff("i", 0), Staff("i2", 0))
        val schedule = listOf(
            listOf(1, 1, 0, 0),   // i:  X,X,Y,Y
            listOf(0, 0, 1, 1),   // i2: Y,Y,X,X
        )
        return MagiState(
            startDate = "2026-01-01", endDate = "2026-01-04",
            shifts = shifts(), groups = groups, staff = staff, use2Patterns = false,
            groupShift = listOf(listOf(1, 1)),
            groupShiftApt = List(1) { List(2) { "" } },
            schedule = schedule,
            wishes = emptyMap(),
            staffRange = mapOf("0,1" to Range("2", "2"), "1,1" to Range("2", "2")),   // X回数を厳密ピン（両職員とも現状=2）
            needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = listOf(C1Row(day1 = "2", shiftKigou = "X", day2 = "1")),
            cons2 = emptyList(), cons3 = emptyList(),
            cons3n = emptyList(), cons3m = emptyList(), cons3mn = emptyList(),
            cons41 = emptyList(), cons42 = emptyList(),
        )
    }

    @Test
    fun c1PolishAppliesMirrorRectangleWhenSameDaySwapIsRejected() {
        val st = mirrorState()
        val sched = st.schedule.toIntArray2D()
        val before = UnifiedViolationChecker.check(st, sched)
        assertEquals("初期 HARD=0（covU/c3n無し）", 0, before.hard)
        assertTrue("初期 c1>0", (before.breakdown["c1"] ?: 0) > 0)

        val res = V6HotfixPasses.applyC1WindowPolish(st, sched, maxPasses = 1)
        val after = UnifiedViolationChecker.check(st, res.newSchedule)

        assertTrue("鏡像長方形が採用されたこと（手計算どおり同日スワップは総和±0で不採用のはず）", res.applied > 0)
        assertTrue("c1 が減少したこと", (after.breakdown["c1"] ?: 0) < (before.breakdown["c1"] ?: 0))
        assertEquals("HARD 不変(=0)", 0, after.hard)
        assertTrue("鏡像交換のログが記録されていること", res.logs.any { it.message.contains("鏡像:1") || it.message.contains("鏡像:2") })
        // 鏡像交換は両職員の総シフト回数（多重集合）を保存する（i, i2 とも X/Y の総数が不変）。
        fun countsOf(sc: Array<IntArray>, i: Int): Pair<Int, Int> {
            val cx = sc[i].count { it == 1 }; val cy = sc[i].count { it == 0 }
            return cx to cy
        }
        val (bx0, by0) = countsOf(sched, 0); val (ax0, ay0) = countsOf(res.newSchedule, 0)
        val (bx1, by1) = countsOf(sched, 1); val (ax1, ay1) = countsOf(res.newSchedule, 1)
        assertEquals("職員0の X 回数保存", bx0, ax0); assertEquals("職員0の Y 回数保存", by0, ay0)
        assertEquals("職員1の X 回数保存", bx1, ax1); assertEquals("職員1の Y 回数保存", by1, ay1)
    }

    /**
     * 手R2（自己2日swap）検証用の盤面。D=2,N=1窓・T=4日。
     * i(職員0)=[X,X,Y,Y]（mirrorStateと同一行）: donors={day0}・deficient window=[2,3](day2)。
     * i2(職員1)は別グループでXを担当不可（groupShift=[1,0]）＝全日Yに固定＝手A/手R1の相手候補が構造上ゼロ
     *   （i2は常にX不保持のため work[i2][j]==X が成立しない）。よって手R2（自己内の付け替え）だけが唯一の解。
     * 手計算: i 単独で day0(X)→Y・day2(Y)→X の入替えで 1fire→0fire（窓[2,3]解消・窓[0,1]は2X→1Xで依然ok）。
     */
    private fun selfSwapState(): MagiState {
        val groups = listOf(Group("G0", "G0"), Group("G1", "G1"))
        val staff = listOf(Staff("i", 0), Staff("bystander", 1))
        val schedule = listOf(
            listOf(1, 1, 0, 0),   // i:         X,X,Y,Y
            listOf(0, 0, 0, 0),   // bystander: Y,Y,Y,Y（Xを担当不可＝手A/R1の相手になり得ない）
        )
        return MagiState(
            startDate = "2026-01-01", endDate = "2026-01-04",
            shifts = shifts(), groups = groups, staff = staff, use2Patterns = false,
            groupShift = listOf(listOf(1, 1), listOf(1, 0)),
            groupShiftApt = List(2) { List(2) { "" } },
            schedule = schedule,
            wishes = emptyMap(), staffRange = emptyMap(),
            needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = listOf(C1Row(day1 = "2", shiftKigou = "X", day2 = "1")),
            cons2 = emptyList(), cons3 = emptyList(),
            cons3n = emptyList(), cons3m = emptyList(), cons3mn = emptyList(),
            cons41 = emptyList(), cons42 = emptyList(),
        )
    }

    @Test
    fun c1PolishAppliesSelfSwapWhenNoOtherStaffCanTakeTheShift() {
        val st = selfSwapState()
        val sched = st.schedule.toIntArray2D()
        val before = UnifiedViolationChecker.check(st, sched)
        assertEquals("初期 HARD=0", 0, before.hard)
        assertTrue("初期 c1>0", (before.breakdown["c1"] ?: 0) > 0)

        val res = V6HotfixPasses.applyC1WindowPolish(st, sched, maxPasses = 1)
        val after = UnifiedViolationChecker.check(st, res.newSchedule)

        assertTrue("自己swapが採用されたこと（相方候補が存在しないため手A/R1は不可能）", res.applied > 0)
        assertEquals("c1 が解消されたこと（0まで）", 0, after.breakdown["c1"] ?: 0)
        assertEquals("HARD 不変(=0)", 0, after.hard)
        assertTrue("自己swapのログが記録されていること", res.logs.any { it.message.contains("自己:1") })
        // 自己swapは職員0自身のX/Y総回数を保存する。
        val cx0Before = sched[0].count { it == 1 }; val cx0After = res.newSchedule[0].count { it == 1 }
        assertEquals("職員0の X 回数保存", cx0Before, cx0After)
    }

    /**
     * [実バグ修正/anchorStaff の重み優先シャドーイング] 旧実装は anchorStaff の判定に rep0.violations
     * （1セル=最重1クラスのみ）を使っていた。i(職員0)の唯一のc1マーク位置(day2)に、より重いc3n(HARD)も
     * 同時に発火すると、violations["0,2"]は"vio-c3n"に上書きされ"vio-c1"は消える。iの他の日には
     * c1マークが無いため、iはanchorStaffから完全に漏れ、本来採用可能な手A(同日スワップ)すら一度も
     * 試されず c1=1のまま採用0回になっていた（cellFamilies=1セルの全クラス保持マップへ切替えて解消）。
     * i(職員0)=[X,X,Y,Y]・cons3n=[Y,Y]（day2,3が禁止連続の完全一致で c3n 発火・c1のマーク位置と一致）。
     * i2(職員1)=[X,X,X,X]（day2にXを持つ唯一の交換相手）。手計算: 同日day2スワップで i の窓[2,3]が
     * 0X→1Xで解消(c1 1→0)・i2は窓が全てz=2→z=1でも>=1のため不変。かつc3nもi側day2がXになり消滅(1→0)。
     * HARDが1→0に改善するため isBetter は自明に採用する（旧実装ではそもそも試行されなかった手）。
     */
    private fun shadowedAnchorState(): MagiState {
        val groups = listOf(Group("G0", "G0"))
        val staff = listOf(Staff("i", 0), Staff("i2", 0))
        val schedule = listOf(
            listOf(1, 1, 0, 0),   // i:  X,X,Y,Y
            listOf(1, 1, 1, 1),   // i2: X,X,X,X
        )
        return MagiState(
            startDate = "2026-01-01", endDate = "2026-01-04",
            shifts = shifts(), groups = groups, staff = staff, use2Patterns = false,
            groupShift = listOf(listOf(1, 1)),
            groupShiftApt = List(1) { List(2) { "" } },
            schedule = schedule,
            wishes = emptyMap(), staffRange = emptyMap(),
            needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = listOf(C1Row(day1 = "2", shiftKigou = "X", day2 = "1")),
            cons2 = emptyList(), cons3 = emptyList(),
            cons3n = listOf(C3Row(pattern = listOf("Y", "Y"))),
            cons3m = emptyList(), cons3mn = emptyList(),
            cons41 = emptyList(), cons42 = emptyList(),
        )
    }

    @Test
    fun c1PolishFindsAnchorEvenWhenC1MarkIsShadowedByHeavierViolationAtSameCell() {
        val st = shadowedAnchorState()
        val sched = st.schedule.toIntArray2D()
        val before = UnifiedViolationChecker.check(st, sched)
        assertEquals("初期 HARD=1（c3n 1件）", 1, before.hard)
        assertEquals("初期 c1=1（day2窓のみ不足）", 1, before.breakdown["c1"] ?: 0)
        // [前提確認] c1のマーク位置がc3nに上書きされ、旧実装ではiがanchorStaffから漏れていたことの確認。
        assertTrue("職員0の day2 セルは vio-c1 を含まない（c3nに上書き済み）", before.violations["0,2"] != "vio-c1")
        assertTrue("しかし cellFamilies には vio-c1 も残っている", "vio-c1" in (before.cellFamilies["0,2"] ?: emptyList()))

        val res = V6HotfixPasses.applyC1WindowPolish(st, sched, maxPasses = 1)
        val after = UnifiedViolationChecker.check(st, res.newSchedule)

        assertTrue("cellFamilies切替えにより職員0がanchorに入り、同日スワップが試行・採用されること", res.applied > 0)
        assertEquals("c1 が解消されたこと", 0, after.breakdown["c1"] ?: 0)
        assertEquals("HARD も解消されたこと（c3n 1->0）", 0, after.hard)
    }

    /**
     * [同根の実バグ修正/applyC3SequencePolish] c1と同じ anchorStaff の重み優先シャドーイングが
     * c3/c3m/c3mn研磨にも存在した。c3m違反(職員0のday1)がc3n(HARD)に同一セルで上書きされ、
     * 職員0の唯一のc3m/c3n発生源がシャドーイングを受けるため、旧実装では anchorStaff が完全に
     * 空になり(職員1は元々c3系違反なし)、2者ブロック交換が一度も試されなかった。
     * ブロック交換はシフト値の完全な入替のため、c3m/c3n自体は「解消」でなく「移動」だが、
     * 同時に staffRange(low/high) の実改善が乗るため総合スコアは真に改善する（手計算で確認）。
     * i(職員0)=[Y,X,Z]・i2(職員1)=[Z,Z,Y]（3日, T=3）。cons3m=[X,Y]（Want）・cons3n=[X,Z]（禁止）。
     * staffRange: 職員0はZ下限2(現状1=不足1)／職員1はZ上限0(現状2=超過2)。
     * 手計算(w=2,j=0の2日ブロック交換): 職員0→[Z,Z,Z]（Z=3,下限2達成・c3m/c3n消滅）、
     * 職員1→[Y,X,Y]（Z=0,上限0達成・c3mも「Xの次がY」で充足=不発・c3nも不発）。
     * 全違反が解消(HARD 1->0, total 5->0)。
     */
    private fun shadowedC3AnchorState(): MagiState {
        val shifts3 = listOf(
            Shift("Y", "Y", "", ""),  // index0
            Shift("X", "X", "", ""),  // index1
            Shift("Z", "Z", "", ""),  // index2
        )
        val groups = listOf(Group("G0", "G0"))
        val staff = listOf(Staff("i", 0), Staff("i2", 0))
        val schedule = listOf(
            listOf(0, 1, 2),   // i:  Y,X,Z
            listOf(2, 2, 0),   // i2: Z,Z,Y
        )
        return MagiState(
            startDate = "2026-01-01", endDate = "2026-01-03",
            shifts = shifts3, groups = groups, staff = staff, use2Patterns = false,
            groupShift = listOf(listOf(1, 1, 1)),
            groupShiftApt = List(1) { List(3) { "" } },
            schedule = schedule,
            wishes = emptyMap(),
            staffRange = mapOf(
                "0,2" to Range(lo = "2", hi = ""),
                "1,2" to Range(lo = "", hi = "0"),
            ),
            needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(),
            cons3n = listOf(C3Row(pattern = listOf("X", "Z"))),
            cons3m = listOf(C3Row(pattern = listOf("X", "Y"))),
            cons3mn = emptyList(),
            cons41 = emptyList(), cons42 = emptyList(),
        )
    }

    @Test
    fun c3PolishFindsAnchorEvenWhenC3mMarkIsShadowedByHeavierViolationAtSameCell() {
        val st = shadowedC3AnchorState()
        val sched = st.schedule.toIntArray2D()
        val before = UnifiedViolationChecker.check(st, sched)
        assertEquals("初期 HARD=1（c3n 1件）", 1, before.hard)
        assertEquals("初期 c3m=1", 1, before.breakdown["c3m"] ?: 0)
        assertTrue("職員0の day1 セルは vio-c3m を含まない（c3nに上書き済み）", before.violations["0,1"] != "vio-c3m")
        assertTrue("しかし cellFamilies には vio-c3m も残っている", "vio-c3m" in (before.cellFamilies["0,1"] ?: emptyList()))

        val res = V6HotfixPasses.applyC3SequencePolish(st, sched, maxPasses = 1)
        val after = UnifiedViolationChecker.check(st, res.newSchedule)

        assertTrue("cellFamilies切替えにより職員0がanchorに入り、ブロック交換が試行・採用されること", res.applied > 0)
        assertTrue("総合スコアが改善したこと", after.total < before.total)
        assertTrue("HARDが悪化しないこと(keep-best)", after.hard <= before.hard)
    }

    /**
     * [同根の実バグ修正/applyBlockRotationPolish] C1Rotate/C3Rotate が共有するこの関数も同じ
     * シャドーイングを受ける。3者回転が必要なため職員2名(mirrorStateと同一の ai=[X,X,Y,Y] とドナー
     * bi=[X,X,X,X])に無関係な第3の職員 ci=[X,X,X,X] を加え、ciには Y の下限(staffRange)を設定する。
     * 手計算: 回転 ai<-bi, bi<-ci, ci<-ai により ai=[X,X,X,X](c1/c3n解消)・bi=[X,X,X,X](無変化)・
     * ci=[X,X,Y,Y](aiの旧パターンを継承=c1/c3n再発)。c1/c3n自体は「移動」だが、ciのY下限(2)が
     * ちょうど満たされる(Y=0→2)ため総合スコアは真に改善する（staffPacked前フィルタも通過することを
     * 手計算で確認済み）。
     */
    private fun shadowedC1RotationState(): MagiState {
        val groups = listOf(Group("G0", "G0"))
        val staff = listOf(Staff("ai", 0), Staff("bi", 0), Staff("ci", 0))
        val schedule = listOf(
            listOf(1, 1, 0, 0),   // ai: X,X,Y,Y
            listOf(1, 1, 1, 1),   // bi: X,X,X,X
            listOf(1, 1, 1, 1),   // ci: X,X,X,X
        )
        return MagiState(
            startDate = "2026-01-01", endDate = "2026-01-04",
            shifts = shifts(), groups = groups, staff = staff, use2Patterns = false,
            groupShift = listOf(listOf(1, 1)),
            groupShiftApt = List(1) { List(2) { "" } },
            schedule = schedule,
            wishes = emptyMap(),
            staffRange = mapOf("2,0" to Range(lo = "2", hi = "")),   // ci: Y(index0)の下限2
            needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = listOf(C1Row(day1 = "2", shiftKigou = "X", day2 = "1")),
            cons2 = emptyList(), cons3 = emptyList(),
            cons3n = listOf(C3Row(pattern = listOf("Y", "Y"))),
            cons3m = emptyList(), cons3mn = emptyList(),
            cons41 = emptyList(), cons42 = emptyList(),
        )
    }

    @Test
    fun blockRotationPolishFindsAnchorEvenWhenC1MarkIsShadowedByHeavierViolationAtSameCell() {
        val st = shadowedC1RotationState()
        val sched = st.schedule.toIntArray2D()
        val before = UnifiedViolationChecker.check(st, sched)
        assertEquals("初期 HARD=1（c3n 1件）", 1, before.hard)
        assertEquals("初期 c1=1", 1, before.breakdown["c1"] ?: 0)
        assertTrue("職員aiの day2 セルは vio-c1 を含まない（c3nに上書き済み）", before.violations["0,2"] != "vio-c1")
        assertTrue("しかし cellFamilies には vio-c1 も残っている", "vio-c1" in (before.cellFamilies["0,2"] ?: emptyList()))

        val res = V6HotfixPasses.applyBlockRotationPolish(st, sched, setOf("vio-c1"), "C1Rotate", maxPasses = 1)
        val after = UnifiedViolationChecker.check(st, res.newSchedule)

        assertTrue("cellFamilies切替えによりaiがanchorに入り、3者回転が試行・採用されること", res.applied > 0)
        assertTrue("総合スコアが改善したこと", after.total < before.total)
        assertTrue("HARDが悪化しないこと(keep-best)", after.hard <= before.hard)
    }

    @Test
    fun c1PolishIsNoOpWhenAlreadySatisfied() {
        // 全窓が既に充足済み（X,X,X,X）なら anchorStaff が空になり即終了・採用0。
        val groups = listOf(Group("G0", "G0"))
        val staff = listOf(Staff("i", 0))
        val schedule = listOf(listOf(1, 1, 1, 1))
        val st = MagiState(
            startDate = "2026-01-01", endDate = "2026-01-04",
            shifts = shifts(), groups = groups, staff = staff, use2Patterns = false,
            groupShift = listOf(listOf(1, 1)),
            groupShiftApt = List(1) { List(2) { "" } },
            schedule = schedule,
            wishes = emptyMap(), staffRange = emptyMap(),
            needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = listOf(C1Row(day1 = "2", shiftKigou = "X", day2 = "1")),
            cons2 = emptyList(), cons3 = emptyList(),
            cons3n = emptyList(), cons3m = emptyList(), cons3mn = emptyList(),
            cons41 = emptyList(), cons42 = emptyList(),
        )
        val sched = st.schedule.toIntArray2D()
        val res = V6HotfixPasses.applyC1WindowPolish(st, sched)
        assertEquals("既に充足済みでは採用0(no-op)", 0, res.applied)
    }

    // [頭打ちの理由を可視化=RangePolish(3.222.0)と同型をC1Polishへ横展開] Aが「休 5日窓≥2」に恒常的に
    // 不足(休を一度も持たない)。手A(同日交換=誰も休を持たない)/手R1/R2(donors()=Aは休を保有しない為
    // 常に空)は全滅し、手B(直接移動+玉突き)だけが唯一の経路になるが、唯一の玉突き候補Bが全日希望固定
    // (Z)のため findCovUChain が候補を1人も見つけられず「候補なし」で頭打ちする。ログの残存表示に
    // その理由が出ることを固定する。
    @Test
    fun c1PolishLogsNoCandidateReasonWhenOnlyChainPartnerIsWishLocked() {
        val shifts = listOf(
            Shift("休", "休", "", ""),
            Shift("X", "X", "1", ""),
            Shift("Y", "Y", "", ""),
            Shift("Z", "Z", "", ""),
        )
        val groups = listOf(Group("GA", "GA"), Group("GB", "GB"))
        val groupShift = listOf(
            listOf(1, 1, 1, 0), // GA(A)=休,X,Y
            listOf(1, 1, 0, 1), // GB(B)=休,X,Z
        )
        val staff = listOf(Staff("A", 0), Staff("B", 1))
        val schedule = listOf(
            listOf(1, 1, 1, 1, 1), // A = X×5（休を一度も持たない＝5日窓で常に不足）
            listOf(3, 3, 3, 3, 3), // B = Z×5（需要なしだが全日希望固定＝玉突き候補として使えない）
        )
        val st = MagiState(
            startDate = "2026-08-01", endDate = "2026-08-05",
            shifts = shifts, groups = groups, staff = staff, use2Patterns = false,
            groupShift = groupShift,
            groupShiftApt = List(2) { List(4) { "" } },
            schedule = schedule,
            wishes = mapOf("1,0" to 3, "1,1" to 3, "1,2" to 3, "1,3" to 3, "1,4" to 3),
            staffRange = emptyMap(), needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = listOf(C1Row(day1 = "5", shiftKigou = "休", day2 = "2")),
            cons2 = emptyList(), cons3 = emptyList(),
            cons3n = emptyList(), cons3m = emptyList(), cons3mn = emptyList(),
            cons41 = emptyList(), cons42 = emptyList(),
        )
        val sched = st.schedule.toIntArray2D()
        val before = UnifiedViolationChecker.check(st, sched)
        assertTrue("初期はc1違反があること", (before.breakdown["c1"] ?: 0) > 0)

        val result = V6HotfixPasses.applyC1WindowPolish(st, sched, seed = 1L)
        assertEquals("唯一の玉突き候補が希望固定のため採用0回", 0, result.applied)
        val msg = result.logs.first().message
        assertTrue("残存表示に候補なしの理由が出ること: $msg", msg.contains("候補なし"))
        assertTrue("対象職員名(A)と休が出ること: $msg", msg.contains("A 休"))
    }

    /**
     * [手R3・局所探索の強化=ユーザー指示「賢く深く網羅的に」] 単独職員(相手なし＝手A/R1不可能)・
     * donors()が構造的に空(手R2不可能)・単独職員のためfindCovUChainも候補なし(手B「玉突き経由」不可能)
     * という、既存の手A/R1/R2/手Bの玉突き経路が全滅する局面で、手R3(アンカー限定なしの全ペア網羅)だけが
     * 解消できることを手計算(Pythonで独立検証済み)で確認する。d=3,n=1窓・T=6日・Xを2回(day0,day4、
     * 互いに独立な窓しかカバーしない配置)。
     * 手計算: 窓は4個(wStart=0..3)。day0は窓0のみをカバー(z=1,n=1でdonor対象外＝抜くと即NG)。
     * day4は窓2,3をカバー(いずれもz=1でdonor対象外)。窓1のみ無人でfires=1。
     * day4→day3への1回の交換で窓1,2,3を全てday3がカバーし(day0は窓0のまま)fires=0まで完全解消
     * できるが、この移動先(day3)は「現在違反しているセル(アンカー=窓1の先頭)」そのものではあるが、
     * donors()が空のため既存の手R2はそもそも一度も候補ペアを試せない（donorsループが0回転）。
     * [CI失敗で判明した見落とし修正] staffRangeでXの上限を保有回数(2)に固定しないと、手B(直接移動)が
     * 「アンカー日を無条件にXへ追加する」だけでX回数を3回に増やし(単独職員・需要無しでcovU制約が
     * 効かないため)fires=0を達成してしまい、意図した「回数保存の再配置(手R3)」でなく回数増加で
     * 解決してしまう（単独職員かつneed無しのためfindCovUChainの「玉突きが必要か」の判定自体が
     * 意味をなさず、直接追加がisBetterに素通りしていた）。X上限を現在の保有回数2に固定することで、
     * 手Bの回数増加による解決をhigh違反(重み90)として封じ、手R3(回数保存)のみが解となるようにする。
     */
    private fun isolatedRepackState(): MagiState {
        val groups = listOf(Group("G0", "G0"))
        val staff = listOf(Staff("solo", 0))
        val schedule = listOf(
            listOf(1, 0, 0, 0, 1, 0),   // X,Y,Y,Y,X,Y
        )
        return MagiState(
            startDate = "2026-01-01", endDate = "2026-01-06",
            shifts = shifts(), groups = groups, staff = staff, use2Patterns = false,
            groupShift = listOf(listOf(1, 1)),
            groupShiftApt = List(1) { List(2) { "" } },
            schedule = schedule,
            wishes = emptyMap(),
            staffRange = mapOf("0,1" to Range(lo = "", hi = "2")),   // X(index1)の上限を現在の保有数(2)に固定
            needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = listOf(C1Row(day1 = "3", shiftKigou = "X", day2 = "1")),
            cons2 = emptyList(), cons3 = emptyList(),
            cons3n = emptyList(), cons3m = emptyList(), cons3mn = emptyList(),
            cons41 = emptyList(), cons42 = emptyList(),
        )
    }

    @Test
    fun c1PolishResolvesViaExhaustiveRepackWhenNoPartnerOrDonorExists() {
        val st = isolatedRepackState()
        val sched = st.schedule.toIntArray2D()
        val before = UnifiedViolationChecker.check(st, sched)
        assertEquals("初期 c1=1（窓1が不足）", 1, before.breakdown["c1"] ?: 0)

        val res = V6HotfixPasses.applyC1WindowPolish(st, sched, maxPasses = 1)
        val after = UnifiedViolationChecker.check(st, res.newSchedule)

        assertTrue("手R3(全ペア再配置)が採用されたこと", res.applied > 0)
        assertEquals("c1 が完全解消されたこと（窓を全カバーする配置へ再構成）", 0, after.breakdown["c1"] ?: 0)
        assertEquals("HARD 不変(=0)", 0, after.hard)
        assertTrue("再配置のログが記録されていること", res.logs.any { it.message.contains("再配置:1") })
        // X の総回数は保存される（配置だけが変わる）。
        val cxBefore = sched[0].count { it == 1 }; val cxAfter = res.newSchedule[0].count { it == 1 }
        assertEquals("X 回数保存", cxBefore, cxAfter)
    }

    @Test
    fun c1PolishRepackIsNoOpWhenAlreadyOptimallyPlaced() {
        // 既に窓を全カバーする最適配置（day0, day3）なら fires=0＝手R3も何もしない。
        val groups = listOf(Group("G0", "G0"))
        val staff = listOf(Staff("solo", 0))
        val schedule = listOf(listOf(1, 0, 0, 1, 0, 0))   // X,Y,Y,X,Y,Y
        val st = MagiState(
            startDate = "2026-01-01", endDate = "2026-01-06",
            shifts = shifts(), groups = groups, staff = staff, use2Patterns = false,
            groupShift = listOf(listOf(1, 1)),
            groupShiftApt = List(1) { List(2) { "" } },
            schedule = schedule,
            wishes = emptyMap(), staffRange = emptyMap(),
            needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = listOf(C1Row(day1 = "3", shiftKigou = "X", day2 = "1")),
            cons2 = emptyList(), cons3 = emptyList(),
            cons3n = emptyList(), cons3m = emptyList(), cons3mn = emptyList(),
            cons41 = emptyList(), cons42 = emptyList(),
        )
        val sched = st.schedule.toIntArray2D()
        val before = UnifiedViolationChecker.check(st, sched)
        assertEquals("既に最適配置でc1=0", 0, before.breakdown["c1"] ?: 0)

        val res = V6HotfixPasses.applyC1WindowPolish(st, sched)
        assertEquals("既に最適なら採用0(no-op)", 0, res.applied)
    }
}
