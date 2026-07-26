package com.magi.app.v6

/**
 * [ネイティブ加速 Stage2] Problem → JNI 平坦配列の変換と、C++ 評価器の実行時パリティ照合。
 * 配列レイアウトは magi_native.cpp の nativeCreateProblem と厳密に一致させること。
 */
object NativeEval {

    /** Problem を平坦化してネイティブハンドルを作る。使用後は必ず NativeBridge.nativeDestroyProblem。 */
    fun createHandle(p: Problem): Long {
        if (!NativeBridge.available) return 0L
        val s = p.S; val t = p.T; val k = p.K; val g = p.G
        val meta = intArrayOf(s, t, k, g, p.restIdx, p.dow0, if (p.use2) 1 else 0)
        val staff = IntArray(2 * s) { if (it < s) p.sgrp[it] else p.ssk[it - s] }
        val canDo = IntArray(s * k)
        for (i in 0 until s) for (kk in 0 until k) canDo[i * k + kk] = if (p.canDo(i, kk)) 1 else 0
        val wish = IntArray(s * t)
        for (i in 0 until s) for (j in 0 until t) wish[i * t + j] = p.wish[i][j]
        val needs = IntArray(2 * k * t)
        for (kk in 0 until k) for (j in 0 until t) {
            needs[kk * t + j] = p.need1[kk][j]
            needs[k * t + kk * t + j] = p.need2[kk][j]
        }
        val ranges = IntArray(3 * s * k)
        for (i in 0 until s) for (kk in 0 until k) {
            ranges[i * k + kk] = p.rangeLo[i][kk]
            ranges[s * k + i * k + kk] = p.rangeHi[i][kk]
            ranges[2 * s * k + i * k + kk] = p.apt[i][kk]
        }
        val cons = ArrayList<Int>(64)
        cons.add(p.cons1.size); p.cons1.forEach { cons.add(it.day1); cons.add(it.shiftIdx); cons.add(it.day2) }
        cons.add(p.cons2.size); p.cons2.forEach { cons.add(it.shiftIdx); cons.add(it.count) }
        cons.add(p.cons41.size); p.cons41.forEach { cons.add(it.groupIdx); cons.add(it.shiftIdx); cons.add(it.l); cons.add(it.u) }
        cons.add(p.cons42.size); p.cons42.forEach { cons.add(it.g1); cons.add(it.s1); cons.add(it.g2); cons.add(it.s2) }
        cons.add(p.cons41s.size); p.cons41s.forEach { cons.add(it.groupIdx); cons.add(it.shiftIdx); cons.add(it.l); cons.add(it.u) }
        cons.add(p.cons42s.size); p.cons42s.forEach { cons.add(it.g1); cons.add(it.s1); cons.add(it.g2); cons.add(it.s2) }
        val c3 = ArrayList<Int>(64)
        for (fam in listOf(p.cons3, p.cons3n, p.cons3m, p.cons3mn)) {
            c3.add(fam.size)
            for (row in fam) { c3.add(row.seq.size); row.seq.forEach { c3.add(it) } }
        }
        val bucket = ArrayList<Int>(g * 4)
        for (gg in 0 until g) {
            val b = p.bucket.getOrNull(gg) ?: IntArray(0)
            bucket.add(b.size); b.forEach { bucket.add(it) }
        }
        return NativeBridge.nativeCreateProblem(
            meta, staff, canDo, wish, needs, ranges,
            cons.toIntArray(), c3.toIntArray(), bucket.toIntArray(),
        )
    }

    fun flatten(schedule: Array<IntArray>): IntArray {
        val s = schedule.size; val t = if (s > 0) schedule[0].size else 0
        val out = IntArray(s * t)
        for (i in 0 until s) System.arraycopy(schedule[i], 0, out, i * t, t)
        return out
    }

    fun unflatten(flat: IntArray, s: Int, t: Int): Array<IntArray> =
        Array(s) { i -> IntArray(t) { j -> flat[i * t + j] } }

    /**
     * 実行時パリティ照合: C++ fullEval と Kotlin Evaluator.fullEvalParts を同一盤面で比較。
     * 戻り値は診断ログ用の1行（null=ネイティブ不可）。不一致の場合も内容を返す（呼び出し側が
     * ネイティブ経路を無効化する判断に使う）。
     */
    fun parityCheck(p: Problem, schedule: Array<IntArray>): ParityResult? {
        if (!NativeBridge.available) return null
        val handle = runCatching { createHandle(p) }.getOrDefault(0L)
        if (handle == 0L) return null
        try {
            // [3.282.0/新領域ログ監査] flatten(Kotlin側の盤面コピー)は計測の外へ。旧: t0 が flatten より前に
            //   取られ、C++側の計測値に flatten＋JNI往復のコールド1回分が混入＝実機ログで「C++ 87µs vs
            //   Kotlin 47µs」とネイティブが遅く見える誤誘導を生んでいた（実スループットはホストベンチで
            //   C++ ×1.9〜2.2。単発値である旨のログ限定句は V6FinalPort 側で併記）。
            val flat = flatten(schedule)
            val t0 = System.nanoTime()
            val native = NativeBridge.nativeFullEval(handle, flat)
            val t1 = System.nanoTime()
            val kotlin = Evaluator(p).fullEvalParts(schedule)
            val t2 = System.nanoTime()
            return ParityResult(
                match = native.size == 2 && native[0] == kotlin[0] && native[1] == kotlin[1],
                nativeHard = native.getOrElse(0) { -1L }, nativeSoft = native.getOrElse(1) { -1L },
                kotlinHard = kotlin[0], kotlinSoft = kotlin[1],
                nativeUs = (t1 - t0) / 1000, kotlinUs = (t2 - t1) / 1000,
            )
        } finally {
            NativeBridge.nativeDestroyProblem(handle)
        }
    }

    data class ParityResult(
        val match: Boolean,
        val nativeHard: Long, val nativeSoft: Long,
        val kotlinHard: Long, val kotlinSoft: Long,
        val nativeUs: Long, val kotlinUs: Long,
    )
}
