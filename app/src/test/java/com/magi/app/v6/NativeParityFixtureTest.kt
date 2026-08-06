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
 * `<fixture>_eval_expected.txt` は Kotlin の `Evaluator.fullEval`（実データ state の
 * 入力盤面）が出す hard/soft。このテストが Kotlin 側を、native-parity ワークフローの
 * `--expect=` が C++ 側を、同じファイルへ固定する。片側だけを変えれば必ずどちらかが落ちる。
 *
 * **片側だけ変えたときに本当に落ちるかを実測して確認済み**: C++ の soft へ +113 を足す
 * （内部の scalar/bit 整合は崩れない＝旧 CI は 0 mismatch で通る）改変を入れると、
 * `--expect=` 側だけが MISMATCH で非ゼロ終了する。
 *
 * [3.361.0/backlog#6] 実データ形状の網羅を広げるため **2つ目のフィクスチャ sample_state_v6** を追加。
 * golden は `hard=0`（C++ の HARD 族パスを実データで一度も exercise しない）だが、sample_v6 の
 * 入力盤面は `hard=15`（groupViol/c3n/pref/covU が発火＝別形状）。C++ 側は 1回のベンチ実行のまま
 * `--expect` を flat と出現順で対応づけて両方を照合する（`host_parity_bench.cpp`）。
 *
 * 期待値を意図的に変えるとき（重みの変更・族の定義変更など）は、**Kotlin と C++ の両方を直してから**
 * 該当の期待値ファイルを更新する。片方だけ直して期待値を書き換えると、この仕組みは意味を失う。
 */
class NativeParityFixtureTest {
    @Test
    fun goldenEvaluatorValueMatchesTheSharedCrossLanguageFixture() {
        assertFixtureMatchesEvaluator("/golden_state.json", "/golden_eval_expected.txt")
    }

    /** [3.361.0] hard=15 の実データ形状（groupViol/c3n/pref/covU＋c1/c2/c42）を言語跨ぎで固定する。 */
    @Test
    fun sampleV6EvaluatorValueMatchesTheSharedCrossLanguageFixture() {
        assertFixtureMatchesEvaluator("/sample_state_v6.json", "/sample_v6_eval_expected.txt")
    }

    private fun assertFixtureMatchesEvaluator(stateResource: String, expectResource: String) {
        val json = javaClass.getResourceAsStream(stateResource)?.bufferedReader()?.readText()
        assertNotNull("$stateResource がテストリソースにありません", json)
        val expectText = javaClass.getResourceAsStream(expectResource)?.bufferedReader()?.readText()
        assertNotNull("$expectResource がテストリソースにありません", expectText)

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
            "Kotlin の hard が固定値と違う（$expectResource）。C++ 側も同時に直したうえで期待値ファイルを更新すること",
            expected["hard"], hard,
        )
        assertEquals(
            "Kotlin の soft が固定値と違う（$expectResource）。C++ 側も同時に直したうえで期待値ファイルを更新すること",
            expected["soft"], soft,
        )
    }
}
