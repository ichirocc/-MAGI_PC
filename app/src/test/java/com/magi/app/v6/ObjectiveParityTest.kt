package com.magi.app.v6

import com.magi.app.model.C1Row
import com.magi.app.model.C2Row
import com.magi.app.model.C3Row
import com.magi.app.model.C41Row
import com.magi.app.model.C42Row
import com.magi.app.model.Group
import com.magi.app.model.MagiState
import com.magi.app.model.Range
import com.magi.app.model.Shift
import com.magi.app.model.Staff
import com.magi.app.model.StateParser
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.util.Random

/**
 * [3.337.0/敵対レビュー A1] **目的関数の意味が2箇所に書かれている**ことへの回帰網。
 *
 * - `UnifiedViolationChecker` … 表示・最終採否の正（重みは `MirrorKeys.weights`）
 * - `Evaluator.fullEvalParts` … 探索用スカラー（重みは**リテラル**で書かれている）
 *
 * 同じ意味を2箇所に書いているので、**片方だけ変えると静かにずれる**。実際にずれた履歴がある
 * （covO 0.5 vs 1.0／c1・c3mn の HF77 変更）。C++ 側は `native-parity` CI が守っているが、
 * **Kotlin の Checker と Evaluator を突き合わせる試験は無かった**。ここで固定する。
 *
 * 検証する等式（`fullEval` の辞書式パックの定義そのもの）:
 *   Checker の weightedScore − HARD族の重み付き寄与 == Evaluator の soft
 *   Checker の hard（件数）                        == Evaluator の hard（件数）
 *
 * ランダム側は **19族すべてが少なくとも1回は非ゼロになる**ことも確認する。族が発火していない
 * 試験は「その族の重みがずれても緑」になるので、網羅を数字で見せないと守れていない。
 */
class ObjectiveParityTest {

    /** Checker の weightedScore のうち HARD 族が占める分。 */
    private fun hardWeighted(rep: ViolationReport): Double =
        MirrorKeys.hard.sumOf { k -> (rep.breakdown[k] ?: 0) * MirrorKeys.weightOf(k) }

    private fun assertParity(label: String, st: MagiState, sched: Array<IntArray>) {
        val p = Problem(st)
        val ev = Evaluator(p, c3RunMode = true)
        val rep = UnifiedViolationChecker.check(st, sched)
        val parts = ev.fullEvalParts(sched)
        assertEquals("$label: HARD 件数が Checker と Evaluator で違う", rep.hard.toLong(), parts[0])
        assertEquals(
            "$label: SOFT の重み付き寄与が Checker と Evaluator で違う（重みの二重管理がずれた）",
            parts[1].toDouble(), rep.weightedScore - hardWeighted(rep), 1e-9,
        )
        // Checker 自身の内部整合（breakdown を重みで組み直すと weightedScore になる）も見る。
        val recon = rep.breakdown.entries.sumOf { (k, c) -> c * MirrorKeys.weightOf(k) }
        assertEquals("$label: breakdown の重み付き再構成が weightedScore と違う",
            rep.weightedScore, recon, 1e-9)
    }

    @Test fun objectiveParityOnRealProductionState() {
        val json = javaClass.getResourceAsStream("/golden_state.json")
            ?.bufferedReader()?.use { it.readText() }
            ?: error("golden_state.json not found on the test classpath")
        val st = StateParser.parse(json)
        assertParity("golden_state", st, st.schedule.toIntArray2D())
    }

    /** 19族すべてが発火しうる状態を作る（need/希望/担当不可/連続パターン/群・スキル群まで入れる）。 */
    private fun richState(rng: Random, s: Int, t: Int, k: Int, g: Int): MagiState {
        val groups = (0 until g).map { Group("G$it", "G$it") }
        val skills = (0 until 2).map { Group("K$it", "K$it") }
        val staff = (0 until s).map { Staff("S$it", rng.nextInt(g), rng.nextInt(3) - 1) }
        // 担当可否に 0 を混ぜる＝盤面が担当外を指すと groupViol が出る。
        val groupShift = (0 until g).map { (0 until k).map { if (rng.nextInt(6) == 0) 0 else 1 } }
        val groupShiftApt = (0 until g).map {
            (0 until k).map { if (rng.nextInt(3) == 0) rng.nextInt(t + 1).toString() else "" }
        }
        val shifts = listOf(Shift("休", "休", "", "")) + (1 until k).map {
            // need1/need2 を入れて covU/covO を出す。
            Shift("S$it", "S$it", if (rng.nextInt(2) == 0) rng.nextInt(3).toString() else "",
                if (rng.nextInt(2) == 0) (1 + rng.nextInt(3)).toString() else "")
        }
        val staffRange = HashMap<String, Range>()
        for (i in 0 until s) for (x in 0 until k) {
            if (rng.nextInt(2) != 0) continue
            val lo = if (rng.nextInt(2) == 0) rng.nextInt(t / 2 + 1).toString() else ""
            val hi = if (rng.nextInt(2) == 0) (rng.nextInt(t / 2 + 1)).toString() else ""
            if (lo.isNotEmpty() || hi.isNotEmpty()) staffRange["$i,$x"] = Range(lo, hi)
        }
        val wishes = HashMap<String, Int>()
        repeat(s * t / 6) { wishes["${rng.nextInt(s)},${rng.nextInt(t)}"] = rng.nextInt(k) }
        fun sym(x: Int) = if (x == 0) "休" else "S$x"
        fun pat(n: Int) = List(n) { sym(rng.nextInt(k)) }
        return MagiState(
            startDate = "2026-03-02", endDate = "2026-12-28",
            shifts = shifts, groups = groups, skillGroups = skills, staff = staff,
            use2Patterns = rng.nextInt(2) == 0,
            groupShift = groupShift, groupShiftApt = groupShiftApt,
            schedule = List(s) { List(t) { rng.nextInt(k) } },
            wishes = wishes, staffRange = staffRange,
            needDay1 = mapOf("1,0" to "1"), needDay2 = mapOf("1,0" to "2"),
            cons1 = listOf(C1Row((2 + rng.nextInt(3)).toString(), sym(rng.nextInt(k)), "1")),
            cons2 = listOf(C2Row(sym(rng.nextInt(k)), (1 + rng.nextInt(t)).toString())),
            cons3 = listOf(C3Row(pat(2))),
            cons3n = listOf(C3Row(pat(2))),
            cons3m = listOf(C3Row(pat(2))),
            cons3mn = listOf(C3Row(pat(2))),
            cons41 = listOf(C41Row("G0", sym(1), "1", "1")),
            cons42 = listOf(C42Row("G0", if (g > 1) "G1" else "G0", sym(1), sym(if (k > 2) 2 else 1))),
            cons41s = listOf(C41Row("K0", sym(1), "1", "1")),
            cons42s = listOf(C42Row("K0", "K1", sym(1), sym(if (k > 2) 2 else 1))),
        )
    }

    @Test fun objectiveParityAcrossRandomStatesCoveringEveryFamily() {
        val rng = Random(0x0B7A21L)
        val fired = HashSet<String>()
        repeat(60) { iter ->
            val st = richState(rng, s = 3 + rng.nextInt(5), t = 8 + rng.nextInt(14),
                k = 3 + rng.nextInt(3), g = 1 + rng.nextInt(2))
            val sched = st.schedule.toIntArray2D()
            assertParity("random#$iter", st, sched)
            UnifiedViolationChecker.check(st, sched).breakdown
                .forEach { (k2, v) -> if (v > 0) fired.add(k2) }
        }
        val missing = MirrorKeys.all.filterNot { it in fired }
        assertTrue("一度も発火しなかった族があるとその重みのずれを検出できない: $missing", missing.isEmpty())
    }
}
