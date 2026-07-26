package com.magi.app.v6

/**
 * [C1 Delta Prefilter / 3.275.0] C1修復候補を「全チェッカーへ渡す前」に安く選別する（図の C1 Delta Prefilter）。
 *
 * **accept非変更・スコア不変が絶対条件**: 本フィルタは「UnifiedViolationChecker + isBetter が**確実に却下する**
 * 候補」だけを早期に落とし、採用され得る候補は一切落とさない（＝退化不能）。最終採否は常に呼出側の
 * checker + keep-best のまま。
 *
 *  - hasActionableC1(index): 盤面に不足窓が1つも無い＝すべての c1修復手は c1中立。c1オペレータは c1違反
 *    セルにアンカーするため候補ゼロ＝no-op。よってクラスタ全体を1回のチェックで安全にスキップできる。
 *  - screenCell(...): **単一セル候補**の速い判定。適用すると HARD（groupViol/pref/c3n）が必ず増える、または
 *    盤面が変わらない候補は checker が辞書式(hard→total→weighted)で必ず却下するため HARD_REJECT を返す。
 *    それ以外は NEUTRAL（判定を checker に委ねる）。**単一セル専用**＝相手の隣接日に触れる bundle には使わない
 *    （その場合 makesForbiddenRun の per-cell 判定が陳腐化するため）。
 *  - c1Delta(...): (staff,day)→newShift の**その職員のc1 fire 正味増減**（gain−loss を厳密勘定）。
 *    **順位付け/診断専用**（accept を一切ゲートしない）。
 *
 * [設計判断] 各オペレータの内側ループ（first-improvement 順に依存）への per候補配線は探索順序を変え得るため
 *   本版では見送り（スコア不変を最優先）。screenCell/c1Delta は新規オペレータ・診断のための検証済み部品として
 *   提供し、既存オペレータは従来どおり自前の canDo/wishLocked/makesForbiddenRun 判定を保持する。
 */
object C1DeltaPrefilter {

    enum class Verdict {
        /** checker が確実に却下する（HARD増 or 無変化）＝安全に早期スキップ可。 */
        HARD_REJECT,

        /** 改善し得る＝checker+keep-best に判定を委ねる。 */
        NEUTRAL,
    }

    /** 不足窓が無ければ c1オペレータは一律 no-op。クラスタ全体を安全にスキップできる。 */
    fun hasActionableC1(index: C1RepairIndex.Index): Boolean = index.hasActionable

    /**
     * 単一セル候補 (staff,day)→newShift を安く選別する。HARD_REJECT は「適用しても isBetter が必ず false」を
     * 意味する（＝スキップしても採用結果は不変）。
     *
     * [3.279.0/外部レビューC1-01/02/12] 旧実装は per-family の存在判定（makesForbiddenRun=true／
     * wishLocked希望外）で無条件却下していたが、それでは**正味では HARD が悪化しない候補**まで落としていた:
     *  - C1-01: 新しい禁止連続を1件作りつつ既存の禁止連続を1件以上壊す手（c3n 正味0以下）＝checker は採用しうる。
     *  - C1-02: 既に希望違反中のセルを別の非希望シフトへ変える手（pref 1→1 不変）＝checker は採用しうる。
     * 反例をホストJVMで実証済み（screenCell=HARD_REJECT だが isBetter=true）。契約を sound にするため、
     * **単一セル変更の全 HARD 族（groupViol/pref/c3n/covU）の正味Δを厳密に計算し、Δ>0 のときだけ却下**する
     * （cand.hard = best.hard + Δ が厳密に成立＝Δ>0 なら isBetter は hard 比較で必ず false）。
     * per-family の相殺（c3n+1 を covU−2 が打ち消す等）も正しく通す。C1-12 の座標境界チェックも追加。
     */
    fun screenCell(p: Problem, schedule: Array<IntArray>, staff: Int, day: Int, newShift: Int): Verdict {
        if (staff !in 0 until p.S || day !in 0 until p.T) return Verdict.HARD_REJECT   // [C1-12] 不正座標は非手
        if (newShift !in 0 until p.K) return Verdict.HARD_REJECT
        // [3.279.1/レビューnit] 旧: normalizeSchedule で全盤面 O(S×T) をコピーしていたが、本判定が読むのは
        //   staff の1行と day の1列のみ。normalizeSchedule と**同一の意味論**（欠損セル→0=休へパディング・
        //   範囲外値→-1）を読み取り時に局所適用し、コピーを行1本 O(T) に削減（正規化済み盤面には恒等＝結果同一。
        //   S×T 未満の不揃い盤面でも normalizeSchedule 経由と同じ判定になり AIOOBE しない）。
        fun cell(i: Int, j: Int): Int {
            val v = schedule.getOrNull(i)?.getOrNull(j) ?: 0
            return if (v in 0 until p.K) v else -1
        }
        val old = cell(staff, day)
        if (old == newShift) return Verdict.HARD_REJECT            // 無変化＝isBetter は非改善で却下
        var delta = 0
        // groupViol: checker は範囲内かつ担当外のセルのみ計上（-1 セルは対象外）。
        if (!p.canDo(staff, newShift)) delta++
        if (old in 0 until p.K && !p.canDo(staff, old)) delta--
        // pref: 実現可能希望の未充足（checker と同一）。既に違反中なら別シフトへ変えても不変（C1-02）。
        if (p.wishLocked(staff, day)) {
            val w = p.wish[staff][day]
            delta += (if (newShift != w) 1 else 0) - (if (old != w) 1 else 0)
        }
        // c3n: 行内の禁止連続 fire 数の正味差分（生成と破壊の両方を勘定＝C1-01）。
        delta += run {
            val row = IntArray(p.T) { cell(staff, it) }
            val before = staffC3nFires(p, row)
            row[day] = newShift
            staffC3nFires(p, row) - before
        }
        // covU: **到着側のみ**勘定（≤0＝改善方向。c3n/pref の正味+1 を covU改善が相殺する候補を正しく通す）。
        //   離脱側の covU 悪化（≥0）は**意図的に含めない**: applyC1IndexChainRepair の branch(b) が
        //   「離脱で空いた covU 穴を玉突き連鎖で埋め直す」前提の候補であり、ここで落とすと連鎖経路が死ぬ
        //   （実テストで回帰確認済み）。正項の省略は Δ_computed ≤ Δ_true ＝ under-reject 方向のため
        //   「HARD_REJECT ⇒ checker が必ず却下」の契約は保たれる（離脱悪化の最終判定は checker）。
        val cntNew = (0 until p.S).count { cell(it, day) == newShift }
        delta += p.covUCell(newShift, day, cntNew + 1) - p.covUCell(newShift, day, cntNew)
        return if (delta > 0) Verdict.HARD_REJECT else Verdict.NEUTRAL
    }

    /**
     * 職員行 row の cons3n（禁止連続, HARD）fire 数。checker の forbidden 窓完全一致と同一意味論。
     * [3.280.0] c3n「なぜ崩せないか」診断（V6PortAnalyzer.diagnoseForbiddenRuns）が正味増減の判定に
     * 共用するため internal 化（screenCell と同じ row-local 差分計算を DRY に保つ）。
     */
    internal fun staffC3nFires(p: Problem, row: IntArray): Int {
        var fires = 0
        for (c in p.cons3n) {
            val seq = c.seq
            val d = seq.size
            if (d == 0 || d > p.T) continue
            var j = 0
            while (j <= p.T - d) {
                if (row[j] == seq[0]) {
                    var z = 0
                    for (l in 1 until d) if (row[j + l] == seq[l]) z++
                    if (z == d - 1) fires++
                }
                j++
            }
        }
        return fires
    }

    /**
     * (staff,day)→newShift としたときの、その職員の **c1 fire 数の正味増減**（負=改善）。
     * newShift追加で解消する窓の gain と、旧シフト除去で新たに割れる窓の loss の**両方**を厳密に勘定する
     * （index.expectedGain は gain のみの近似＝旧シフトが c1制約を持つと自己破壊を見落とす）。順位付け/診断専用
     * ＝accept を一切ゲートしない（採否は常に呼出側の checker+keep-best）。
     */
    fun c1Delta(p: Problem, schedule: Array<IntArray>, staff: Int, day: Int, newShift: Int): Int {
        if (staff !in 0 until p.S || day !in 0 until p.T || newShift !in 0 until p.K) return 0
        val row = schedule[staff].clone()   // 単一行のみ複製（c1 は per-staff）
        val old = row[day]
        if (old == newShift) return 0
        val oldFires = staffC1Fires(p, row, staff)
        row[day] = newShift
        val newFires = staffC1Fires(p, row, staff)
        return newFires - oldFires
    }

    /** 職員 staff の row における全 cons1 窓の不足 fire 数（checker の c1 走査と同一意味論）。 */
    private fun staffC1Fires(p: Problem, row: IntArray, staff: Int): Int {
        var fires = 0
        for (c in p.cons1) {
            val x = c.shiftIdx
            if (x !in 0 until p.K || c.day1 !in 1..p.T || c.day2 < 1) continue
            if (!p.canDo(staff, x)) continue
            var j = 0
            while (j <= p.T - c.day1) {
                var z = 0
                for (l in 0 until c.day1) if (row[j + l] == x) z++
                if (z < c.day2) fires++
                j++
            }
        }
        return fires
    }
}
