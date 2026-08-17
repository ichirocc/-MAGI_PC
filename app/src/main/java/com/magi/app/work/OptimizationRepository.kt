package com.magi.app.work

import com.magi.app.model.MagiState
import com.magi.app.v6.ViolationReport
import kotlinx.coroutines.channels.BufferOverflow
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.flow.asStateFlow

/**
 * In-process bridge between the UI/ViewModel and the background [OptimizationWorker]
 * (改善仕様書 §6.3). The MagiState is far larger than WorkManager's 10KB inputData limit,
 * so the input is handed over here by reference and progress/result are published as flows.
 *
 * While the process is alive (incl. backgrounded), this carries everything. If the process is
 * killed mid-run the in-memory [request] is lost, but the Worker persists its input/best-snapshot/
 * result to files and recovers from them (see OptimizationWorker.loadInputFromFile / inputFile /
 * snapshotFile / resultFile), so the run completes and the result is reflected on relaunch.
 */
object OptimizationRepository {
    data class BgProgress(
        val phase: String,
        val hard: Int,
        val soft: Int,
        val total: Int,
        val iters: Long,
        val elapsedMs: Long,
    )

    data class BgResult(
        val schedule: Array<IntArray>,
        val report: ViolationReport,
        val phase: String,
    )

    /** Input handed to the next worker run. */
    @Volatile var request: Pair<MagiState, Array<IntArray>>? = null
    @Volatile var seconds: Int = 60
    @Volatile var workers: Int = 4

    private val _running = MutableStateFlow(false)
    val running: StateFlow<Boolean> = _running.asStateFlow()

    private val _progress = MutableStateFlow<BgProgress?>(null)
    val progress: StateFlow<BgProgress?> = _progress.asStateFlow()

    private val _result = MutableStateFlow<BgResult?>(null)
    val result: StateFlow<BgResult?> = _result.asStateFlow()

    /**
     * [3.385.0/外部レビュー High3] Worker が **黙って落とした耐久保証の失敗**を、書き出せる操作ログへ届ける
     * ための唯一の経路。
     *
     * `OptimizationWorker` は入力・結果・途中最良の書き込みを全て `runCatching { }` で包んでおり、
     * 失敗しても痕跡が一切残らなかった。これは kill 耐性そのものを担う書き込みなので、失敗すると
     * 「5分回した実行がプロセス終了で丸ごと消えたのに、書き出したログには理由が1行も無い」状態になる
     * （3.381.0/3.382.0 で潰した「サイレント死」と同じクラス）。Worker は Android の Log しか持たず
     * このアプリの診断は全て書き出しログに集まるので、Repository を経由して `logOp` へ流す。
     *
     * `replay` を持たせるのは、Worker がプロセス再起動直後（ViewModel が購読する前）に走り得るため。
     */
    private val _notes = MutableSharedFlow<Pair<String, String>>(
        replay = 8,
        extraBufferCapacity = 16,
        onBufferOverflow = BufferOverflow.DROP_OLDEST,
    )
    val notes: SharedFlow<Pair<String, String>> = _notes.asSharedFlow()

    /**
     * 失敗の記録は本処理を止めてはいけないので、ブロックしない `tryEmit` を使う。
     *
     * [3.388.0/外部レビュー] `level` を持たせる。3.387.0 で Worker のライフサイクル全体を流すように
     * したのに消費側が `logOp("W", it)` 固定のままで、**正常に完了した背景実行まで警告として記録**して
     * いた。このリポジトリの診断は「まず [W] を拾う」読み方が定着している（SanityCheck・CoverageDiag・
     * 設定ミス・NativeBridge がすべて W）ので、正常系を混ぜるとその読み方が壊れる。
     */
    fun publishNote(level: String, msg: String) { _notes.tryEmit(level to msg) }

    fun setRunning(v: Boolean) { _running.value = v }
    fun publishProgress(p: BgProgress) { _progress.value = p }
    fun publishResult(r: BgResult?) { _result.value = r }
    fun clear() { _progress.value = null; _result.value = null }
}
