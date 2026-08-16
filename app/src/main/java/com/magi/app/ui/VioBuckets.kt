package com.magi.app.ui

/**
 * [E7] 違反 種別フィルタの分類表（勤務表タブ全面で共有・コース6分類）。
 *
 * 主軸=制約種別。**場所を持つ族**を作成者の語彙で6バケツに束ね、勤務表タブの全面（グリッドセル/日ヘッダ/
 * Tally職員・日/カレンダー）を1つの共有フィルタで絞る。複数トグル・初期全ON（見たくないノイズを引き算）。
 * 件数付きチップで「まずどの種類を潰すか」の種別トリアージを兼ねる。
 * **表示のみ・スコアリング不変**（どの違反が存在するかは不変、表示するかだけを制御）。
 *
 * [3.382.0] `MagiScheduleViews.kt` から**この分類だけ**を切り出した（Compose に一切依存しないため）。
 * 目的は下の不変条件を**ユニットテストで固定できるようにする**こと＝以前は UI ファイルにあり
 * ホストでも CI の JVM テストでも安全に触れなかった。ロジックは1文字も変えていない。
 */
internal data class VioBucket(val key: String, val label: String, val families: Set<String>)

internal val vioBuckets: List<VioBucket> = listOf(
    VioBucket("need", "人員", setOf("covU", "covO")),
    VioBucket("pref", "希望", setOf("pref")),
    VioBucket("seq", "連勤", setOf("c3", "c3n", "c3m", "c3mn")),
    VioBucket("count", "回数", setOf("low", "high", "apt", "c2")),
    VioBucket("group", "群ルール", setOf("groupViol", "c41", "c42", "c41s", "c42s")),
    VioBucket("window", "窓", setOf("c1")),
)

/**
 * バケツに入れない族＝**セル/日の場所マップを持たない**ため絞り込みの対象にできないもの。
 *
 * 公平(fair)は「群×シフトの平均からの偏り」、曜日(weekly)は「職員×シフトの曜日偏り」で、どちらも
 * 特定のセルに紐づかない（3.149.0 で内訳パネル用の `distLocations` は持たせたが、グリッドには出さない）。
 *
 * **ここと `vioBuckets` の和が `MirrorKeys.all` と一致していなければならない**。族を追加して
 * どちらにも入れ忘れると `bucketOfFamily` が null を返し、E7 フィルタで**常に表示**へ静かに落ちる
 * （例外も警告も出ない）。`VioBucketsTest` がこの一致を両方向で固定する。
 */
internal val vioBucketlessFamilies: Set<String> = setOf("fair", "weekly")

/** vio-class（"vio-covU"/"vio-aptLow" 等）→ 族キー。aptLow/aptHigh は apt に畳む。 */
internal fun familyOfVioClass(cls: String): String =
    when (val f = cls.removePrefix("vio-")) { "aptLow", "aptHigh" -> "apt"; else -> f }

/** 族キー → バケツキー（対象外＝null）。 */
internal fun bucketOfFamily(fam: String): String? = vioBuckets.firstOrNull { fam in it.families }?.key

/** この違反クラスが現在のフィルタ(enabled=表示中バケツ集合)で表示されるか。バケツ対象外の族は常に表示。 */
internal fun vioVisible(cls: String?, enabled: Set<String>): Boolean {
    if (cls == null) return false
    val b = bucketOfFamily(familyOfVioClass(cls)) ?: return true
    return b in enabled
}

internal val allVioBucketKeys: Set<String> = vioBuckets.map { it.key }.toSet()
