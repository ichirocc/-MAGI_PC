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
 *  - c1Delta(...): index を使った c1解消数の見積り。**順位付け/診断専用**（accept を一切ゲートしない）。
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
     */
    fun screenCell(p: Problem, schedule: Array<IntArray>, staff: Int, day: Int, newShift: Int): Verdict {
        val s = normalizeSchedule(schedule, p)
        if (newShift !in 0 until p.K) return Verdict.HARD_REJECT
        if (s[staff][day] == newShift) return Verdict.HARD_REJECT            // 無変化＝isBetter は非改善で却下
        if (!p.canDo(staff, newShift)) return Verdict.HARD_REJECT            // groupViol(HARD 10000) を必ず作る
        if (p.wishLocked(staff, day) && p.wish[staff][day] != newShift) return Verdict.HARD_REJECT // pref(HARD) を破る
        if (p.makesForbiddenRun(s, staff, day, newShift)) return Verdict.HARD_REJECT               // c3n(HARD 7000)
        return Verdict.NEUTRAL
    }

    /** その日を newShift へ変えたとき解消する c1窓不足数（負=改善）。順位付け専用・accept 非ゲート。 */
    fun c1Delta(index: C1RepairIndex.Index, staff: Int, day: Int): Int = -index.expectedGain(staff, day)
}
