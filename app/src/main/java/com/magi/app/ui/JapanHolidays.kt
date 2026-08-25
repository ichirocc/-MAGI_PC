package com.magi.app.ui

import android.content.Context
import org.json.JSONObject
import java.time.LocalDate

/**
 * [レイアウト刷新/祝日色] 日本の祝日（国民の祝日に関する法律）の判定。
 * データは `tools/generate_japan_holidays.py` が祝日法の規則（固定日・ハッピーマンデー・
 * 春分秋分の近似式・振替休日・国民の休日）から生成した外部ファイル
 * `app/src/main/assets/japan_holidays.json`（"YYYY-MM-DD" -> 祝日名、2026〜2036年）を読む。
 * 特定の日付をコードへハードコードしない＝データを再生成すれば期間を延長できる。
 *
 * プロセス内キャッシュ（`@Volatile` の単一エントリ）。一度読めば以降は Map ルックアップのみで
 * I/O が発生しない＝グリッド描画のたびに呼んでもコストは無視できる。
 */
internal object JapanHolidays {
    @Volatile private var cache: Map<String, String>? = null

    private fun load(ctx: Context): Map<String, String> {
        cache?.let { return it }
        val loaded = runCatching {
            val text = ctx.assets.open("japan_holidays.json").use { it.readBytes().toString(Charsets.UTF_8) }
            val o = JSONObject(text)
            val m = HashMap<String, String>(o.length())
            val keys = o.keys()
            while (keys.hasNext()) { val k = keys.next(); m[k] = o.optString(k) }
            m as Map<String, String>
        }.getOrDefault(emptyMap())
        cache = loaded
        return loaded
    }

    /** 指定日が祝日なら祝日名（例「敬老の日」）を返す。祝日でなければ null。読込に失敗しても空扱い（安全側）。 */
    fun nameOf(ctx: Context, date: LocalDate): String? = load(ctx)[date.toString()]

    /** 指定日が祝日か。 */
    fun isHoliday(ctx: Context, date: LocalDate): Boolean = nameOf(ctx, date) != null
}
