package com.magi.app.ui

import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.Immutable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.luminance
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp

/**
 * MAGI デザインシステムのトークン補助層（docs/magi_design_system.md）。
 *
 * 一次ソースは Material3 の `MaterialTheme.colorScheme / typography / shapes`。
 * ここでは colorScheme に無い「意味色 / シフト色 / 余白(spacing)」だけを補い、二重管理を避ける。
 * Phase B の共通コンポーネント（MagiTagChip / MagiCalendarMonthView 等）が参照する。
 */
@Immutable
object MagiAccent {
    // [3.89.0 "Ward" 調和] ネオン Tailwind-500 系 → ディープティール地／冷たいペーパーに馴染む
    //   一段深い「診療チャート」調へ。7 色の色相位置(青/緑/橙/紫/桃/赤/灰)は据え置き＝認識性を保つ。
    //   保存済みのユーザー指定シフト色(shiftColors 等)は不変。ここは既定パレット＋直接使用アクセントのみ更新。
    val blue = Color(0xFF3B6FD4)    // 実行中 / 早番（スティールブルー）
    val green = Color(0xFF2E9E62)   // 成功 / 日勤（リーフ、ティール主色と弁別）
    val orange = Color(0xFFE08A1E)  // 警告 / 夜勤（アンバー）
    val purple = Color(0xFF8A5CD1)  // 遅番 / 個人属性（ミュートバイオレット）
    val pink = Color(0xFFD24D89)    // 希望 / 個人属性（ローズ）
    val red = Color(0xFFD23B34)     // 重大違反 / NG制約（明快なアラート赤）
    val gray = Color(0xFF8A979B)    // 休み / 無効（クールスレート、ペーパーに調和）

    /** 色ピッカー等で提示する既定パレット。 */
    val all: List<Color> = listOf(blue, green, orange, purple, pink, red, gray)
}

/**
 * 警告(warning)セマンティック色。M3 には warning ロールが無いため独自トークンとして集約。
 * 現在のテーマ（surface の明るさ）から明/暗を判定し、container/onContainer を返す（コントラスト確保）。
 * 成功=tertiary、注意=error、主操作=primary はテーマロールを直接使う。
 */
@Composable
fun magiWarnColors(): Pair<Color, Color> {
    val darkSurface = MaterialTheme.colorScheme.surface.luminance() < 0.5f
    return if (darkSurface) Color(0xFF5B4300) to Color(0xFFFBEAD0)   // 暗: アンバー容器＋淡い文字
    else Color(0xFFFBEAD0) to Color(0xFF6B4E00)                      // 明: 淡いアンバー＋濃い文字
}

// シフト記号→色の既定フォールバックは ShiftAppearance.resolveShiftColor が唯一の真実源。
// （以前ここに同等の shiftAccentFallback があったが未配線・二重管理のため削除）

/**
 * 前景（記号・文字）色を背景に対して必ず読めるようにする（WCAG コントラスト比）。
 * ユーザー指定色 [preferred] が背景 [bg] に対して [minRatio] 以上のコントラストを持てば
 * そのまま採用。不足する場合のみ 黒/白 のうち高コントラストな方へフォールバックする。
 * ＊描画時のみの補正で、保存済みの色データ（shiftTextHex 等）は一切書き換えない（HF77 セーフ）。
 * 記号は短い太字グリフのため既定しきい値は 4.5（通常テキスト基準）。
 */
fun ensureReadable(bg: Color, preferred: Color, minRatio: Float = 4.5f): Color {
    fun ratio(a: Color, b: Color): Float {
        val hi = maxOf(a.luminance(), b.luminance())
        val lo = minOf(a.luminance(), b.luminance())
        return (hi + 0.05f) / (lo + 0.05f)
    }
    if (ratio(bg, preferred) >= minRatio) return preferred
    return if (ratio(bg, Color.White) >= ratio(bg, Color.Black)) Color.White else Color.Black
}

/** 4dp グリッドの余白トークン（docs §2.2）。 */
@Immutable
object MagiSpacing {
    val xs: Dp = 4.dp
    val sm: Dp = 8.dp
    val md: Dp = 12.dp
    val lg: Dp = 16.dp
    val xl: Dp = 20.dp
    val section: Dp = 20.dp   // セクション（カード）間
    val screenH: Dp = 16.dp   // 画面左右パディング
}
