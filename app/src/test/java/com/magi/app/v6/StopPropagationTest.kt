package com.magi.app.v6

import com.magi.app.model.StateParser
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * [3.409.9/P1] **停止信号は全アルゴリズムで安全確認点まで伝わる**——これを回帰として固定する。
 *
 * 締切の伝播漏れはこのリポジトリが繰り返し踏んできた欠陥クラス（2.65.0 HF66 / 3.161.0 研磨5パス /
 * 3.271.0 クラスタ締切 / 3.313.0 free repair 4関数）だが、**探索の入口(`optimize`)に対する回帰は
 * 一度も無かった**。伝播が切れると「やめる」を押しても予算いっぱい走り続ける＝利用者から見て
 * アプリが固まる。
 *
 * 実測（golden・予算60秒・workers=4・stop は最初から真）: V5 53ms / ALNS 33ms / RSI 14ms /
 * RSI++ 35ms / PORTFOLIO 30ms。ここでは 100倍の余裕を見て 5 秒を上限にする。
 * 伝播が切れれば予算（下の 30 秒）まで走ってから落ちる＝失敗は遅いが確実に検出される。
 *
 * あわせて **keep-best**（停止で返る盤面は入力より悪くない）と**次元の保存**も固定する。
 * ネイティブチャンクの停止確認は `.so` をロードできないホストJVMでは実行できないため対象外
 * （番兵・パリティは native-parity CI が別途守る）。
 */
class StopPropagationTest {

    private fun goldenState() =
        StateParser.parse(javaClass.classLoader!!.getResource("golden_state.json")!!.readText())!!

    @Test
    fun everyAlgorithmReturnsPromptlyWhenStopIsAlreadySignalled() = runBlocking {
        val st = goldenState()
        val initial = Array(st.schedule.size) { st.schedule[it].toIntArray() }
        val base = UnifiedViolationChecker.check(st, initial)
        val algorithms = listOf(
            V6Algorithm.V5, V6Algorithm.ALNS, V6Algorithm.RSI,
            V6Algorithm.RSI_PLUS, V6Algorithm.PORTFOLIO,
        )
        for (alg in algorithms) {
            val t0 = System.currentTimeMillis()
            val res = V6NativeOptimizer.optimize(
                st,
                Array(initial.size) { initial[it].clone() },
                V6OptimizerOptions(algorithm = alg, totalBudgetSec = 30, workers = 4, seed = 12345L),
                shouldStop = { true },
            )
            val elapsed = System.currentTimeMillis() - t0
            assertTrue("$alg が停止信号を無視した（${elapsed}ms）", elapsed < 5_000)

            // 次元の保存（返る盤面が入力と同じ形）
            assertEquals("$alg 職員数", initial.size, res.schedule.size)
            for (i in initial.indices) assertEquals("$alg 期間($i)", initial[i].size, res.schedule[i].size)

            // keep-best: 停止で返る盤面は入力より悪くない
            val after = UnifiedViolationChecker.check(st, res.schedule)
            assertTrue(
                "$alg が停止時に入力より悪い盤面を返した（in hard=${base.hard} total=${base.total} " +
                    "→ out hard=${after.hard} total=${after.total}）",
                !betterReport(base, after),
            )
        }
    }
}
