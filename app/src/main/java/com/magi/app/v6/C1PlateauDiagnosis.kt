package com.magi.app.v6

/**
 * [C1 頭打ちの構造化診断 / 3.322.0] 「窓の要件(c1)が最後まで残った理由」を、ログ文字列でなく
 * 構造化データとして UI まで運ぶ。
 *
 * ## なぜ観測ベースなのか（静的証明にしなかった理由）
 * 「休の回数が固定（lo==hi）だから窓不足を直せない」は**証明できない**。窓の不足は
 * 「窓の外にある同じシフトを窓の中へ移す」（[V6HotfixPasses.applyC1WindowPolish] の手R1/R2/R3＝
 * 回数保存の再配置）でも解消しうるため、回数が動かせないことは即「直せない」を意味しない。
 * よってこの診断は **実際に研磨が候補を作って却下した記録**（観測）だけを根拠にする。
 * 「構造的に不能」とは言わない — 言えるのは「いまの設定のもとで、**試した**手が却下された」までで、
 * 試していない手の存在を否定はしない。
 *
 * ## 証明つきの壁との住み分け
 * `C1RepairAnalysis.provenWalls`（coverage 入替でどう並べても焦点を解消できないことを厳密に
 * 証明する A4 診断）は別物で、こちらは一切変更しない。本診断はその手前の
 * 「探索は動いたが採用に至らなかった」層を説明する。
 */
enum class C1PlateauCause {
    /** 厳密ピン(lo==hi)を目標から遠ざけるため却下された手が最多。回数固定を緩めれば通る可能性がある。 */
    PIN_CONSTRAINED,

    /** 候補は作れたが、他の族が悪化するため総合的に却下された手が最多。 */
    SCORE_TRADEOFF,

    /** 入れ替え相手・再配置先が1つも見つからなかった（候補が生成できていない）。 */
    NO_CANDIDATE,
}

/** 根拠の強さ。「証明」は名乗らない（上記の理由）。 */
enum class C1PlateauEvidence {
    /** 実際に候補を作って却下された記録がある。 */
    OBSERVED,

    /** 候補が1件も作れず、なぜ作れないかまでは分かっていない。 */
    UNKNOWN,
}

/**
 * 残った窓の要件についての内訳。**粒度は職員×シフト**で、同じ職員・同じシフトに複数の
 * 期間の決まり（cons1 の規則・窓開始日）があってもまとめて1件に集計する
 * （3.324.0/外部レビューで明示。別の窓で却下された理由が、残った別の窓の理由として並ぶことがある）。
 */
data class C1PlateauEntry(
    val staff: Int,
    val shift: Int,
    val staffName: String,
    val shiftKigou: String,
    val cause: C1PlateauCause,
    val evidence: C1PlateauEvidence,
    /** 厳密ピンを崩すため却下された候補の数。 */
    val rejectedByPin: Int,
    /** 目的関数（必須／重み／件数）で却下された候補の数。 */
    val rejectedByScore: Int,
    /** 候補そのものが作れなかった回数。 */
    val noCandidate: Int,
    /** 目的関数で却下された候補が最も悪化させた族（重み付き・多い順）。 */
    val topScoreCulprits: List<Pair<String, Int>>,
) {
    /** 却下の総数（原因の判定に使った母数）。 */
    val observations: Int get() = rejectedByPin + rejectedByScore + noCandidate

    val label: String get() = "$staffName $shiftKigou"

    /**
     * 利用者が次に取れる手。文言はここ1か所に置き、族名の日本語化だけ呼出側から受ける
     * （族名の対応表は UI 層が持っている＝エンジンに複製すると必ずドリフトする）。
     *
     * @param labelOf 族キー→表示名。ログからは素のキーのまま渡してよい。
     */
    fun recommendedAction(labelOf: (String) -> String = { it }): String = when (cause) {
        C1PlateauCause.PIN_CONSTRAINED ->
            // [3.324.0/外部レビュー] 「すべて」「1回ぶん」は断定しすぎ。観測できたのは
            //   「試した手のうち多くが回数固定で却下された」ことまでで、全空間の主張はできない。
            //   緩め幅の優劣は実測でデータによって逆転したので幅を決め打ちしない（HF77 と整合）。
            "試した直し方の多くが、回数を固定している（下限＝上限）ために却下されています。" +
                "この職員の回数の幅を見直すか、窓の要件を下げると通る可能性があります。"
        C1PlateauCause.SCORE_TRADEOFF -> {
            val fam = topScoreCulprits.firstOrNull()?.first
            val famTxt = if (fam == null) "他の条件" else "「${labelOf(fam)}」"
            "直し方は見つかりましたが、${famTxt}が悪化するため採用されていません。" +
                "${famTxt}の設定を緩めるか、窓の要件を下げてください。"
        }
        C1PlateauCause.NO_CANDIDATE ->
            "入れ替えられる相手が見つかりません。このシフトを担当できる職員を増やすか、窓の要件を下げてください。"
    }
}

/**
 * 最後の研磨で残った窓の要件の内訳。`entries` が空でも `remainingC1 > 0` はありうる
 * （研磨が起点に取れなかった＝観測が無い場合）。
 */
data class C1PlateauDiagnosis(
    val remainingC1: Int,
    val entries: List<C1PlateauEntry>,
) {
    val hasEntries: Boolean get() = entries.isNotEmpty()

    /**
     * c1 は残っているのに却下の観測が1件も無い＝**原因未確定**。
     * 研磨が起点を取れなかった／後続パスが別の窓を直して観測分だけ消えた、などで起こる。
     * このとき「直せない理由」を語ってはいけない（何も観測していない）。
     */
    val causeUnknown: Boolean get() = remainingC1 > 0 && entries.isEmpty()

    /** 回数固定による却下が最多だった件数。設定画面へ誘導するかの判断に使う。 */
    val pinConstrained: Int get() = entries.count { it.cause == C1PlateauCause.PIN_CONSTRAINED }

    /**
     * 後続の研磨パスが解消した箇所を落として最終盤面に合わせ直す。
     * 診断は C1 研磨の時点で作られるが、その後に別のパスが同じ窓を直すことがあるため
     * （残っていない箇所を「直せなかった」と見せない）。
     *
     * @param stillDeficient 最終盤面で当該窓がまだ不足しているか。
     */
    fun refreshedAgainst(remainingC1: Int, stillDeficient: (Int, Int) -> Boolean): C1PlateauDiagnosis =
        C1PlateauDiagnosis(remainingC1, entries.filter { stillDeficient(it.staff, it.shift) })

    fun logLines(): List<String> {
        if (causeUnknown) return listOf(
            "[W] C1Plateau: 窓の要件(c1) ${remainingC1}件が残存 — 却下の観測がなく原因未確定")
        if (!hasEntries) return emptyList()
        val out = ArrayList<String>()
        out.add("[W] C1Plateau: 窓の要件(c1) ${remainingC1}件が残存 — 直せなかった理由の内訳")
        for (e in entries.take(8)) {
            val causeTxt = when (e.cause) {
                C1PlateauCause.PIN_CONSTRAINED -> "回数固定で却下"
                C1PlateauCause.SCORE_TRADEOFF -> "他の条件とのトレードオフ"
                C1PlateauCause.NO_CANDIDATE -> "候補なし"
            }
            val culprits = e.topScoreCulprits.take(2).joinToString(" ") { "${it.first}:${it.second}" }
            out.add(
                "[W] C1Plateau: ${e.label} — $causeTxt" +
                    "(ピン破り:${e.rejectedByPin} スコア却下:${e.rejectedByScore} 候補なし:${e.noCandidate}" +
                    (if (culprits.isEmpty()) "" else " 主因 $culprits") + ")"
            )
        }
        if (entries.size > 8) out.add("[W] C1Plateau: ほか${entries.size - 8}件")
        return out
    }

    companion object {
        /** 却下理由の名前（[V6HotfixPasses.applyC1WindowPolish] の `recordBlock` が使う文字列と対応）。 */
        const val REASON_PIN = "ピン破り"
        const val REASON_SCORE = "不採用"
        const val REASON_NO_CANDIDATE = "候補なし"
        const val REASON_NO_REPACK = "再配置候補なし"

        /**
         * 研磨が記録した (職員,シフト)→理由別件数 から診断を組み立てる。
         *
         * @param blockStats 理由文字列→件数。上記 REASON_* のいずれか。
         * @param culpritStats スコア却下時に最も悪化した族→件数。
         * @param stillDeficient 最終盤面でなお当該窓が不足している (職員,シフト) だけを残すための述語。
         */
        fun build(
            remainingC1: Int,
            blockStats: Map<Pair<Int, Int>, Map<String, Int>>,
            culpritStats: Map<Pair<Int, Int>, Map<String, Int>>,
            staffName: (Int) -> String,
            shiftKigou: (Int) -> String,
            stillDeficient: (Int, Int) -> Boolean,
        ): C1PlateauDiagnosis {
            val entries = ArrayList<C1PlateauEntry>()
            for ((key, reasons) in blockStats) {
                val (i, x) = key
                if (!stillDeficient(i, x)) continue
                val pin = reasons[REASON_PIN] ?: 0
                val score = reasons[REASON_SCORE] ?: 0
                val none = (reasons[REASON_NO_CANDIDATE] ?: 0) + (reasons[REASON_NO_REPACK] ?: 0)
                if (pin + score + none == 0) continue
                // [分類規則] 「候補なし」は「入れ替え相手が見つかりません」という強い主張なので、
                //   候補が1件でも作れて却下されているならこれを名乗らない（件数で多数決すると、
                //   実データで「スコア却下8・候補なし10」→「相手が見つかりません」と案内してしまい、
                //   実際には相手が居て禁止連続で落ちていた、という取り違えが起きる）。
                //   候補が作れているときだけ、ピン破りとスコア却下を件数で比べる。
                val cause = when {
                    pin + score == 0 -> C1PlateauCause.NO_CANDIDATE
                    pin > score -> C1PlateauCause.PIN_CONSTRAINED
                    else -> C1PlateauCause.SCORE_TRADEOFF
                }
                entries.add(
                    C1PlateauEntry(
                        staff = i,
                        shift = x,
                        staffName = staffName(i),
                        shiftKigou = shiftKigou(x),
                        cause = cause,
                        evidence = if (pin + score > 0) C1PlateauEvidence.OBSERVED else C1PlateauEvidence.UNKNOWN,
                        rejectedByPin = pin,
                        rejectedByScore = score,
                        noCandidate = none,
                        topScoreCulprits = (culpritStats[key] ?: emptyMap()).entries
                            .sortedByDescending { it.value }.map { it.key to it.value },
                    )
                )
            }
            entries.sortByDescending { it.observations }
            return C1PlateauDiagnosis(remainingC1, entries)
        }
    }
}
