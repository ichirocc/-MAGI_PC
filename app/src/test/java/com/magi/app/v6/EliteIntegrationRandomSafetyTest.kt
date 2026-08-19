package com.magi.app.v6

import com.magi.app.model.Group
import com.magi.app.model.MagiState
import com.magi.app.model.Shift
import com.magi.app.model.Staff
import org.junit.Test
import java.util.Random

/**
 * EliteIntegrationPolish をランダムな盤面・エリート・次元で叩き、その上に載る keep-best が依存する
 * 3つの安全不変条件を固定する: 入口より悪くならない・返す report が公式の再検査と一致する・入力盤面を
 * 変更しない。
 *
 * [3.407.0/断捨離] 旧: `@Test` を持たない `object` ＋ `main()` の手動ハーネスだったが、**名前が Test で
 * 終わるため JUnit が拾って毎回 `initializationError` で失敗**していた（「500テスト中1件失敗、ただし
 * これは既知の誤検出」という注釈をセッションのたびに書く羽目になっていた）。しかも手で起動しない限り
 * **この3つの不変条件は一度も検証されていなかった**。実測4.4秒＝通常のテストとして十分速いので、
 * 誤検出を消すと同時に本当に CI で守られる形へ移した。
 */
class EliteIntegrationRandomSafetyTest {
    private fun randomState(target: Array<IntArray>): MagiState {
        val s = target.size
        val t = if (s > 0) target[0].size else 0
        return MagiState(
            startDate = "2026-08-01",
            endDate = java.time.LocalDate.parse("2026-08-01").plusDays((t - 1).coerceAtLeast(0).toLong()).toString(),
            shifts = listOf(
                Shift("休", "休", "", ""),
                Shift("A", "A", "", ""),
                Shift("B", "B", "", ""),
            ),
            groups = listOf(Group("G", "G")),
            staff = (0 until s).map { Staff("S$it", 0) },
            use2Patterns = false,
            groupShift = listOf(listOf(1, 1, 1)),
            groupShiftApt = listOf(listOf("", "", "")),
            schedule = target.map { row -> row.toList() },
            wishes = emptyMap(),
            staffRange = emptyMap(),
            needDay1 = emptyMap(),
            needDay2 = emptyMap(),
            cons1 = emptyList(),
            cons2 = emptyList(),
            cons3 = emptyList(),
            cons3n = emptyList(),
            cons3m = emptyList(),
            cons3mn = emptyList(),
            cons41 = emptyList(),
            cons42 = emptyList(),
        )
    }

    @Test
    fun randomBoardsNeverRegressAndNeverMutateTheInput() {
        val rng = Random(0xE11E7L)
        var improved = 0
        repeat(500) { case ->
            val s = 2 + rng.nextInt(4)
            val t = 2 + rng.nextInt(6)
            val target = Array(s) { IntArray(t) { rng.nextInt(3) } }
            val state = randomState(target)
            val root = Array(s) { IntArray(t) { rng.nextInt(3) } }
            val saved = root.copy2D()
            val rootRep = UnifiedViolationChecker.check(state, root)
            val elites = ArrayList<AdaptiveElite>()
            repeat(8) { idx ->
                val e = root.copy2D()
                repeat(1 + rng.nextInt(maxOf(1, s * t / 2))) {
                    e[rng.nextInt(s)][rng.nextInt(t)] = rng.nextInt(3)
                }
                val rep = UnifiedViolationChecker.check(state, e)
                elites.add(
                    AdaptiveElite(
                        e, rep,
                        HypothesisEpochRole.entries[idx % HypothesisEpochRole.entries.size],
                        idx, idx, bridge = rep.hard == rootRep.hard + 1,
                    ),
                )
            }
            val out = EliteIntegrationPolish.apply(
                state, root, elites, { false }, System.currentTimeMillis() + 500L,
                EliteIntegrationPolish.Config(maxPairs = 4, maxFusionGroups = 4, maxFusionCells = 8, beamWidth = 8),
            )
            val checked = UnifiedViolationChecker.check(state, out.schedule)
            check(!AdaptiveEliteArchive.better(rootRep, checked)) { "regression case=$case root=$rootRep out=$checked" }
            check(AdaptiveEliteArchive.sameObjective(checked, out.report)) { "report mismatch case=$case" }
            check(AdaptiveEliteArchive.sameSchedule(root, saved)) { "input mutated case=$case" }
            if (AdaptiveEliteArchive.better(checked, rootRep)) improved++
        }
        // 改善が一度も起きないなら「安全」は空虚（何もしないパスでも通る）＝この検証が意味を持つことの確認。
        check(improved > 0) { "500ケースで一度も改善しなかった＝この不変条件は空振りしている" }
    }
}
