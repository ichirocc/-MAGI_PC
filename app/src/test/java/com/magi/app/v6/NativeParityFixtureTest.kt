package com.magi.app.v6

import com.magi.app.model.StateParser
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Test

/**
 * [3.357.0] Kotlin と C++ の評価器を**1つの数字に固定する**。
 *
 * これまで CI が照合していたのは **C++ scalar vs C++ bit-op**（native-parity）と
 * **Checker vs Evaluator**（[ObjectiveParityTest]・どちらも Kotlin）だけで、
 * **Kotlin と C++ の間**を見るものが1つも無かった。実機には `NativeEval.parityCheck` の
 * 番兵があるが、発火するとネイティブが黙って無効化される＝**速度が落ちるだけで気づけない**。
 *
 * `golden_eval_expected.txt` は Kotlin の `Evaluator.fullEval`（実データ golden_state.json の
 * 入力盤面）が出す hard/soft。このテストが Kotlin 側を、native-parity ワークフローの
 * `--expect=` が C++ 側を、同じファイルへ固定する。片側だけを変えれば必ずどちらかが落ちる。
 *
 * **片側だけ変えたときに本当に落ちるかを実測して確認済み**: C++ の soft へ +113 を足す
 * （内部の scalar/bit 整合は崩れない＝旧 CI は 0 mismatch で通る）改変を入れると、
 * `--expect=` 側だけが MISMATCH で非ゼロ終了する。
 *
 * 期待値を意図的に変えるとき（重みの変更・族の定義変更など）は、**Kotlin と C++ の両方を直してから**
 * このファイルを更新する。片方だけ直して期待値を書き換えると、この仕組みは意味を失う。
 */
class NativeParityFixtureTest {
    @Test
    fun goldenEvaluatorValueMatchesTheSharedCrossLanguageFixture() {
        val json = javaClass.getResourceAsStream("/golden_state.json")?.bufferedReader()?.readText()
        assertNotNull("golden_state.json がテストリソースにありません", json)
        val expectText = javaClass.getResourceAsStream("/golden_eval_expected.txt")?.bufferedReader()?.readText()
        assertNotNull("golden_eval_expected.txt がテストリソースにありません", expectText)

        val expected = expectText!!.lineSequence()
            .mapNotNull { line ->
                val t = line.trim()
                when {
                    t.startsWith("hard=") -> "hard" to t.removePrefix("hard=").toLong()
                    t.startsWith("soft=") -> "soft" to t.removePrefix("soft=").toLong()
                    else -> null
                }
            }.toMap()
        assertEquals("期待値ファイルは hard= と soft= の2行", setOf("hard", "soft"), expected.keys)

        val st = StateParser.parse(json!!)!!
        val p = Problem(st)
        val sched = Array(st.schedule.size) { i -> st.schedule[i].toIntArray() }
        val ev = Evaluator(p, c3RunMode = true)
        val (hard, soft) = ev.split(ev.fullEval(sched))

        assertEquals(
            "Kotlin の hard が固定値と違う。C++ 側も同時に直したうえで golden_eval_expected.txt を更新すること",
            expected["hard"], hard,
        )
        assertEquals(
            "Kotlin の soft が固定値と違う。C++ 側も同時に直したうえで golden_eval_expected.txt を更新すること",
            expected["soft"], soft,
        )
    }
}
