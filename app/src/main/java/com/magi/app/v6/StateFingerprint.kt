package com.magi.app.v6

import com.magi.app.model.MagiState

/**
 * [3.328.0/外部レビュー → 3.330.0 で ViewModel から移動] 勤務表の意味を決める入力すべての指紋。
 *
 * **何に使うか**（安全機構が2つ、この値の正しさに乗っている）:
 *  - 研磨診断（C1頭打ち・回数固定の却下記録）の鮮度判定。観測したときの入力と今の入力が違えば、
 *    その診断はもう「今の設定の話」ではないので出さない。
 *  - 背景で走らせた最適化の結果を当ててよいかの照合。実行中に別のデータを開く・取り込むと、
 *    結果が別の入力に対して計算されたものになる。
 *
 * **意図的に読まないフィールド**（`MagiState` の26個のうちこの3つだけ。残り23個は全部読む）:
 *  - `schedule`＝盤面。診断は「この盤面のもの」を盤面ハッシュで別に見ており、結果の適用では
 *    盤面が変わるのは当然。ここに混ぜると結果の照合が必ず不一致になって使えなくなる。
 *  - `shiftColors`＝表示色。エンジンに影響しない。
 *  - `extras`＝まだモデル化していない項目を往復のために持っているだけ。エンジンに影響しない。
 *
 * **書き忘れると安全機構が黙って効かなくなる**（変えたのに気づけず古い診断・古い結果が通る）ので、
 * `StateFingerprintTest` が入力の族ごとに「変えたら指紋も変わる」ことを固定している。
 * `MagiState` にフィールドを足したら、ここと そのテストの両方に足すこと。
 *
 * Android に依存しない純関数＝ホスト JVM でテストできる（ViewModel に置いていた間はできなかった）。
 */
object StateFingerprint {
    fun of(st: MagiState): Long {
        var h = -3750763034362895579L
        fun mix(v: Long) { h = h * 1099511628211L + v }
        fun txt(t: String?) { if (t == null) mix(0) else { for (c in t) mix(c.code.toLong()); mix(1) } }
        txt(st.startDate); txt(st.endDate); mix(if (st.use2Patterns) 1 else 0)
        for (sh in st.shifts) { txt(sh.name); txt(sh.kigou); txt(sh.need1); txt(sh.need2) }
        for (g in st.groups) { txt(g.name); txt(g.kigou) }
        for (g in st.skillGroups) { txt(g.name); txt(g.kigou) }
        for (p in st.staff) { txt(p.name); mix(p.groupIdx.toLong()); mix(p.skillIdx.toLong()) }
        for (row in st.groupShift) for (v in row) mix(v.toLong())
        for (row in st.groupShiftApt) for (v in row) txt(v)
        for ((k, v) in st.wishes.entries.sortedBy { it.key }) { txt(k); mix(v.toLong()) }
        for ((k, r) in st.staffRange.entries.sortedBy { it.key }) { txt(k); txt(r.lo); txt(r.hi) }
        for ((k, v) in st.needDay1.entries.sortedBy { it.key }) { txt(k); txt(v) }
        for ((k, v) in st.needDay2.entries.sortedBy { it.key }) { txt(k); txt(v) }
        for (c in st.cons1) { txt(c.day1); txt(c.shiftKigou); txt(c.day2) }
        for (c in st.cons2) { txt(c.shiftKigou); txt(c.count) }
        for (fam in listOf(st.cons3, st.cons3n, st.cons3m, st.cons3mn)) {
            mix(2); for (row in fam) for (t in row.pattern) txt(t)
        }
        for (fam in listOf(st.cons41, st.cons41s)) {
            mix(3); for (c in fam) { txt(c.groupKigou); txt(c.shiftKigou); txt(c.l); txt(c.u) }
        }
        for (fam in listOf(st.cons42, st.cons42s)) {
            mix(4); for (c in fam) { txt(c.g1Kigou); txt(c.g2Kigou); txt(c.s1Kigou); txt(c.s2Kigou) }
        }
        return h
    }
}
