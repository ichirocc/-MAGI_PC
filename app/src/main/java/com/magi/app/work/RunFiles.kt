package com.magi.app.work

import java.io.File

/**
 * [3.386.0] バックグラウンド最適化が使う**共有ファイルの所有権と原子的な書き込み**を、`Context` から
 * 切り離してテストできる場所へ移したもの。ロジックは `OptimizationWorker` から動かしただけで変えていない。
 *
 * **なぜ切り出すか**: このアプリで最も監査が濃いのは `OptimizationWorker`（3.327.0 High3・3.329.0 H-03・
 * 3.333.0・3.336.0 S3/P0残・3.385.0 の計11件）だが、`ctx.filesDir.resolve` を直接呼ぶため
 * **JVM 単体テストからは1行も触れず、再発防止がコメントだけ**という状態だった。守るべき性質のうち
 * 「所有権の判定」「後片付けの網羅」「原子置換」は `Context` でなく**ディレクトリ1つ**あれば表現できる。
 * 3.330.0 で `removeSkillGroup` を `Ws1Ops` へ移したのと同じ手＝**テストできる場所へ動かして初めて
 * 再発防止になる**。
 *
 * **ここでは守れないもの（正直に）**: `doWork()` の**並び**（耐久保存→公開の順序・失敗パスが所有権を
 * 閉じること・進捗公開の前に所有権を確認すること）は Worker のライフサイクルそのものなので、
 * Robolectric か instrumented test が要る。所有確認と置き換えの間の TOCTOU（3.385.0 で ms→μs へ
 * 縮めたが閉じてはいない）も同様に単体テストでは捕まらない。
 */
internal class RunFiles(private val dir: File) {

    /** kill 後の再起動でここから復元する入力。 */
    val input: File get() = File(dir, "magi_bg_input.json")

    /** 完了結果。UI 不在で完走しても次回起動で反映できるように残す。 */
    val result: File get() = File(dir, "magi_bg_result.json")

    /** 途中最良のスナップショット（8秒ごと退避＝実質の途中再開）。 */
    val snapshot: File get() = File(dir, "magi_bg_best.json")

    /**
     * いま所有権を持つ実行の ID。ファイル名は固定・`ExistingWorkPolicy.REPLACE` で入れ替わるため、
     * **どの実行が書いたファイルか**を区別する術がこれしかない（3.327.0）。
     */
    val runId: File get() = File(dir, "magi_bg_run.txt")

    /** enqueue の直前に呼び、この実行を所有者として記録する。 */
    /**
     * この実行の所有権マーカーを立てる。**書けたかどうかを返す**（3.406.0/B-01）。
     * 旧: 失敗を握り潰していたため、書けなくても Work が投入され、Worker 側は
     * `activeRunId()==0 ≠ 自分のid` で所有権なしと判定して**何もせず即 return**——
     * 画面だけ「開始しました」のまま実行中が永久に残る、という無言の失敗になっていた。
     */
    fun beginRun(id: Long): Boolean =
        runCatching { runId.writeText(id.toString()); runId.readText().trim() == id.toString() }.getOrDefault(false)

    /** 記録が無い・壊れているときは 0（＝誰も所有していない）。 */
    fun activeRunId(): Long =
        runCatching { runId.readText().trim().toLong() }.getOrDefault(0L)

    /**
     * この実行が共有ファイルの所有者か。
     * - `mine == 0L`：runId を持たない旧経路 → 従来どおり所有者として扱う（非破壊）。
     * - 置き換え（REPLACE）で新しい実行が [beginRun] を書くと旧実行はここで false になり、
     *   **書き込みも削除も一切しなくなる**。停止（[clear] で runId 消去）も同様。
     */
    fun owns(mine: Long): Boolean = mine == 0L || activeRunId() == mine

    /**
     * 4ファイルすべてを消す。**1つでも足すのを忘れると次回起動が古い状態を掴む**ので、
     * `RunFilesTest` が「clear 後に dir が空」で網羅を固定している。
     *
     * [3.410.0/B-06] **消し残った名前を返す**。旧: `delete()` の戻り値も例外も捨てており、消えなかった
     * ファイルがあっても呼出側に届かなかった＝**残ったファイルを次回起動が「中断されました・再開できます」
     * として掴む**。所有権マーカー(`runId`)が消え残ると、なお悪く新しい実行が所有者になれない。
     * 返り値が空でないときは呼出側が記録する（消せないこと自体はここでは直せない）。
     *
     * @param keepRunId 所有権マーカーを残す。[3.410.0/U-02] 「所有権を立ててから旧途中状態を掃除する」
     *   順序では、ここで runId まで消すと**自分で立てたばかりの所有権を自分で捨てる**ことになる。
     */
    fun clear(keepRunId: Boolean = false): List<String> {
        val stuck = ArrayList<String>()
        for (f in listOf(input, result, snapshot) + (if (keepRunId) emptyList() else listOf(runId))) {
            val ok = runCatching { if (f.exists()) f.delete() else true }.getOrDefault(false)
            if (!ok) stuck.add(f.name)
        }
        return stuck
    }

    /**
     * 一時ファイル経由の原子置換（3.336.0 S3）。素の `writeText` は非原子で、書き込み途中に落ちると
     * **壊れた JSON が残る**。起動時の復元は「結果が空でなければマーカーも入力も掃除してから読む」ため、
     * 壊れたファイルは「結果も再開手段も両方失う」経路になっていた。
     *
     * @param commitGuard 置き換えの**直前**に呼ぶ（3.385.0）。false なら一時ファイルだけ捨て、
     *   `target` には一切触れない。所有権の再確認をここへ置くと、直列化と一時ファイル書き込みのぶん
     *   （ms 級）が TOCTOU の窓から外れる。**窓が消えるわけではない。**
     * @param onNonAtomic rename が使えず**原子性を諦めて直接書いた**ときに呼ぶ（3.428.0/#7）。
     *   旧: 黙ってフォールバックしていたため、壊れたファイルが残る可能性のある書き方をしたことが
     *   どこにも残らなかった。原子性は諦めても、諦めたことは残す。
     *   **`commitGuard` より前に置くこと**：本番の呼出2箇所（途中最良・完了結果）は末尾ラムダ記法で
     *   `{ ownsFiles() }` を `commitGuard` へ渡している。末尾ラムダは**最後の引数**に束縛されるので、
     *   これを末尾にすると 3.385.0 の所有権再確認が黙って既定の `{ true }` に落ちる
     *   （実際にこの順序で書いて `RunFilesTest` の4件が落ち、出荷前に捕まえた）。
     * @return `target` に `text` が入ったなら true。
     */
    fun writeAtomically(
        target: File,
        text: String,
        onNonAtomic: () -> Unit = {},
        commitGuard: () -> Boolean = { true },
    ): Boolean = writeFileAtomically(target, text, onNonAtomic, commitGuard)
}

/**
 * 一時ファイル経由の原子置換。[RunFiles.writeAtomically] の実体で、**run ファイル以外**（自動保存など）
 * からも使えるようにファイルレベルへ出してある（同じ処理を写すと必ず片方が取り残される）。
 *
 * [3.410.0/B-05] 旧: 一時ファイル名が「対象名 + .tmp」の**固定名**で、置き換え(REPLACE)の前後に2つの
 * writer が重なると同じ一時ファイルを奪い合った（片方の書き込み途中に他方が rename/delete する＝
 * **壊れた内容が target に入る**）。所有権チェックは rename の直前しか見ないのでこの競合は素通りする。
 * 呼出ごとに一意な名前にすれば構造的に交差しない（WorkManager の Worker は同一プロセスで走るので
 * プロセス内カウンタで足りる）。
 */
internal fun writeFileAtomically(
    target: File,
    text: String,
    onNonAtomic: () -> Unit = {},
    commitGuard: () -> Boolean = { true },
): Boolean {
    val tmp = File(target.parentFile, target.name + ".t" + atomicWriteSeq.incrementAndGet() + ".tmp")
    try {
        tmp.writeText(text)
        if (!commitGuard()) return false
        // rename が使えない環境（別ファイルシステム跨ぎ等）では原子性を諦めて直接書く＝最善努力。
        // [3.428.0/#7] 諦めたことは呼出側へ知らせる。この経路で書いている間に落ちると壊れたファイルが
        //   残り、起動時の復元が「結果も再開手段も両方失う」形になりうる（原子置換を入れた動機そのもの）。
        if (!tmp.renameTo(target)) { onNonAtomic(); target.writeText(text) }
        return true
    } finally {
        // 成功時の rename 後は既に消えている。失敗・ガード偽・例外のいずれでも残骸を残さない。
        runCatching { if (tmp.exists()) tmp.delete() }
    }
}

private val atomicWriteSeq = java.util.concurrent.atomic.AtomicLong(0)
