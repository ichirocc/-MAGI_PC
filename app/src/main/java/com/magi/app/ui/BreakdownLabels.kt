package com.magi.app.ui

/**
 * 違反の族キー（`MirrorKeys.all`）→ 画面に出す日本語名。
 *
 * 引くのは10箇所（内訳チップ・要確認一覧・重み表＝「直す優先順位」・人員診断の主因・C1頭打ちの助言など）で、
 * どれも `breakdownLabels[key] ?: key` の形。**引けなければ生の英字キーがそのまま画面に出る**＝
 * `docs/operator_ux.md` の「英字符号を画面に出さない」に正面から反する。例外も警告も出ないので、
 * 族を1つ足してここへ書き忘れると**誰かが実機で気づくまで分からない**。
 *
 * [3.409.7] `MagiDashboardCards.kt` から**この表だけ**を切り出した（Compose に一切依存しないため）。
 * 目的は下の不変条件を**ユニットテストで固定できるようにする**こと＝以前は Compose 依存の UI ファイルに
 * あり、ホストでも CI の JVM テストでも安全に触れなかった。中身は1文字も変えていない
 * （3.382.0 で `vioBuckets` を切り出したのと同じ理由・同じ形）。
 *
 * 不変条件: **`MirrorKeys.all` と過不足なく一致する**（`BreakdownLabelsTest`）。
 * 3.72.0 で weekly が19族目として実際に追加された経緯があり、同じことは今後も起こる。
 */
internal val breakdownLabels: Map<String, String> = mapOf(
    "groupViol" to "担当外シフト", "pref" to "希望違反", "covU" to "人員不足", "c3n" to "禁止の並び",
    "low" to "下限割れ", "high" to "上限超過", "apt" to "適切回数のズレ", "fair" to "公平化のズレ", "weekly" to "曜日の偏り",
    "c1" to "窓の要件", "c2" to "個人の合計", "c3" to "必須の並び", "c3m" to "推奨の並び",
    "c3mn" to "回避の並び", "c41" to "群のレンジ", "c42" to "群ペア",
    // [④用語統一] covO は covU「人員不足」/集計凡例「人員過剰」と対にする（旧「過剰な配置」は同じ違反の別名で紛らわしい）。
    "c41s" to "スキル群のレンジ", "c42s" to "スキル群ペア", "covO" to "人員過剰",
)
