package com.magi.app

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.consumeWindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Shapes
import androidx.compose.material3.Surface
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.runtime.Composable
import androidx.compose.runtime.SideEffect
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.core.view.WindowCompat
import com.magi.app.ui.MagiApp

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        enableEdgeToEdge()
        super.onCreate(savedInstanceState)
        setContent {
            // [D8/UD固定] 外観はユーザー判断で UD（高コントラスト・白地）固定。自動/明/暗の選択は撤去。
            //   白地＝ステータスバー/ナビバーは暗アイコン。
            SideEffect {
                val controller = WindowCompat.getInsetsController(window, window.decorView)
                controller.isAppearanceLightStatusBars = true
                controller.isAppearanceLightNavigationBars = true
            }
            MagiTheme(3) {
                Surface(
                    Modifier
                        .fillMaxSize()
                        .windowInsetsPadding(WindowInsets.safeDrawing)
                        .consumeWindowInsets(WindowInsets.safeDrawing),
                    color = MaterialTheme.colorScheme.background,
                ) {
                    MagiApp()
                }
            }
        }
    }
}

/**
 * [3.386.0] `private` → `internal`。会話バブルの展開先（`BubbleActivity`）が素の `MaterialTheme` を
 * 使っており、D8（3.121.0＝外観は UD 高コントラスト固定）から**バブルだけ外れて** M3 既定の
 * mauve/pink が出ていた。同じテーマを共有するため可視性を上げる（呼出は本アプリ内の2箇所のみ）。
 */
@Composable
internal fun MagiTheme(mode: Int = 0, content: @Composable () -> Unit) {
    // mode: 0=システム / 1=ライト / 2=ダーク / 3=高コントラスト(ユニバーサルデザイン)
    val dark = when (mode) { 1 -> false; 2 -> true; else -> isSystemInDarkTheme() }
    // [MAGI "Ward" 配色 — melta-ui 流トークン設計 / 詳細は docs/DESIGN.md]
    //   高コントラスト(UD, mode=3): 白地＋黒境界＋濃色ロールで最大可読。ブランドは deep teal に統一。
    val highContrast = lightColorScheme(
        primary = Color(0xFF00504A), onPrimary = Color(0xFFFFFFFF),
        primaryContainer = Color(0xFF003732), onPrimaryContainer = Color(0xFFFFFFFF),
        secondary = Color(0xFF123E38), onSecondary = Color(0xFFFFFFFF),
        secondaryContainer = Color(0xFFCDE8E3), onSecondaryContainer = Color(0xFF05201C),
        // [監査修正] UD で未指定だと M3 既定(mauve/pink/lavender)が混入していた tertiary/surfaceContainer*/outlineVariant を
        //   ブランド(teal/leaf)で明示。成功バッジ(tertiaryContainer)・下部バー(surfaceContainer)が正色で描画される。
        tertiary = Color(0xFF1C5030), onTertiary = Color(0xFFFFFFFF),
        tertiaryContainer = Color(0xFFBFF0AF), onTertiaryContainer = Color(0xFF002200),
        background = Color(0xFFFFFFFF), onBackground = Color(0xFF000000),
        surface = Color(0xFFFFFFFF), onSurface = Color(0xFF000000),
        surfaceVariant = Color(0xFFE3EDEA), onSurfaceVariant = Color(0xFF16221F),
        error = Color(0xFF8C0009), onError = Color(0xFFFFFFFF),
        errorContainer = Color(0xFFFFDAD4), onErrorContainer = Color(0xFF2D0001),
        surfaceTint = Color(0xFF00504A),
        surfaceContainerLowest = Color(0xFFFFFFFF), surfaceContainerLow = Color(0xFFF3F6F5),
        surfaceContainer = Color(0xFFEDF2F0), surfaceContainerHigh = Color(0xFFE6ECEA),
        surfaceContainerHighest = Color(0xFFDFE6E3),
        inverseSurface = Color(0xFF1A211F), inverseOnSurface = Color(0xFFFFFFFF), inversePrimary = Color(0xFF86D6C9),
        outline = Color(0xFF000000), outlineVariant = Color(0xFF45504D),
    )
    // [Material 3 トーナル配色 / MAGI "Ward" — 冷たいクリニカルペーパー＋ディープティール]
    //   種色＝ディープティール(主)・スレートティール(副)・リーフグリーン(三次=成功)、ニュートラルを寒色(ペーパー)へ。
    //   melta-ui 原則: 純黒本文を使わない(暖色寄りの濃インク)／重い影を使わない(境界＋surfaceトーンで階層)。
    //   全ロールを実測: 本文 onSurface/on* は 4.5:1 以上、UI(outline/container) は 3:1 以上を充足。
    val colors = if (mode == 3) highContrast else if (dark) darkColorScheme(
        primary = Color(0xFF86D6C9), onPrimary = Color(0xFF00382F),
        primaryContainer = Color(0xFF005046), onPrimaryContainer = Color(0xFFA7F2E6),
        secondary = Color(0xFFB1CCC7), onSecondary = Color(0xFF1C3531),
        secondaryContainer = Color(0xFF334B47), onSecondaryContainer = Color(0xFFCDE8E3),
        tertiary = Color(0xFFA3D397), onTertiary = Color(0xFF10380D),
        tertiaryContainer = Color(0xFF26501F), onTertiaryContainer = Color(0xFFBFF0AF),
        error = Color(0xFFFFB4AB), onError = Color(0xFF690005),
        errorContainer = Color(0xFF93000A), onErrorContainer = Color(0xFFFFDAD6),
        background = Color(0xFF0E1514), onBackground = Color(0xFFDDE4E1),
        surface = Color(0xFF0E1514), onSurface = Color(0xFFDDE4E1),
        surfaceVariant = Color(0xFF3F4947), onSurfaceVariant = Color(0xFFBEC9C6),
        surfaceTint = Color(0xFF86D6C9), scrim = Color(0xFF000000),
        surfaceBright = Color(0xFF343B39), surfaceDim = Color(0xFF0E1514),
        surfaceContainerLowest = Color(0xFF080F0E), surfaceContainerLow = Color(0xFF161D1B),
        surfaceContainer = Color(0xFF1A211F), surfaceContainerHigh = Color(0xFF252B2A),
        surfaceContainerHighest = Color(0xFF2F3634),
        inverseSurface = Color(0xFFDDE4E1), inverseOnSurface = Color(0xFF2B322F), inversePrimary = Color(0xFF0E6E63),
        outline = Color(0xFF899391), outlineVariant = Color(0xFF3F4947),
    ) else lightColorScheme(
        primary = Color(0xFF0E6E63), onPrimary = Color(0xFFFFFFFF),           // 主操作: ディープティール
        primaryContainer = Color(0xFFA7F2E6), onPrimaryContainer = Color(0xFF00201C),
        secondary = Color(0xFF4A6360), onSecondary = Color(0xFFFFFFFF),       // 補助: スレートティール
        secondaryContainer = Color(0xFFCDE8E3), onSecondaryContainer = Color(0xFF051F1C),
        tertiary = Color(0xFF3E6837), onTertiary = Color(0xFFFFFFFF),         // 成功: リーフグリーン
        tertiaryContainer = Color(0xFFBFF0AF), onTertiaryContainer = Color(0xFF012200),
        error = Color(0xFFB3261E), onError = Color(0xFFFFFFFF),               // 注意: 濃赤（本文4.5:1確保）
        errorContainer = Color(0xFFF9DEDC), onErrorContainer = Color(0xFF410E0B),
        background = Color(0xFFF4F7F7), onBackground = Color(0xFF171D1C),     // 冷たいペーパー＋暖色寄り濃インク（純黒不使用）
        surface = Color(0xFFFBFDFC), onSurface = Color(0xFF171D1C),
        surfaceVariant = Color(0xFFDAE5E2), onSurfaceVariant = Color(0xFF3F4947),
        surfaceTint = Color(0xFF0E6E63), scrim = Color(0xFF000000),
        surfaceBright = Color(0xFFFBFDFC), surfaceDim = Color(0xFFDBE5E2),
        surfaceContainerLowest = Color(0xFFFFFFFF), surfaceContainerLow = Color(0xFFF1F6F4),
        surfaceContainer = Color(0xFFEBF1EF), surfaceContainerHigh = Color(0xFFE5ECEA),
        surfaceContainerHighest = Color(0xFFE0E7E4),
        inverseSurface = Color(0xFF2B3231), inverseOnSurface = Color(0xFFECF2F0), inversePrimary = Color(0xFF86D6C9),
        outline = Color(0xFF6F7977), outlineVariant = Color(0xFFBEC9C6),
    )
    // 見出し大・本文静か・数値最大（添付テイスト）。spはシステム文字サイズ設定に追従。
    val t = Typography(
        displaySmall = TextStyle(fontSize = 34.sp, fontWeight = FontWeight.Bold, lineHeight = 40.sp),
        headlineSmall = TextStyle(fontSize = 24.sp, fontWeight = FontWeight.Bold, lineHeight = 30.sp),
        titleLarge = TextStyle(fontSize = 20.sp, fontWeight = FontWeight.SemiBold, lineHeight = 26.sp),
        titleMedium = TextStyle(fontSize = 17.sp, fontWeight = FontWeight.SemiBold, lineHeight = 23.sp),
        titleSmall = TextStyle(fontSize = 16.sp, fontWeight = FontWeight.SemiBold, lineHeight = 21.sp),
        // [判読性・全画面底上げ] 実機で「文字が小さい」との指摘を受け本文/ラベル層を +1sp 底上げ（最小tier=14sp）。
        //   密な表の折返しを避けるため見出し層(title Medium/Large・headline・display)は据え置き。1箇所で全画面横断。
        bodyLarge = TextStyle(fontSize = 17.sp, lineHeight = 25.sp),
        bodyMedium = TextStyle(fontSize = 16.sp, lineHeight = 23.sp),
        bodySmall = TextStyle(fontSize = 14.sp, lineHeight = 20.sp),
        labelLarge = TextStyle(fontSize = 16.sp, fontWeight = FontWeight.SemiBold),
        labelMedium = TextStyle(fontSize = 14.sp, fontWeight = FontWeight.Medium),
        // labelSmall はチップ/凡例/補足/違反内訳の下限。11sp(Material既定)→13→14sp へ継続底上げ。
        labelSmall = TextStyle(fontSize = 14.sp, lineHeight = 18.sp, fontWeight = FontWeight.Medium),
    )
    // [MAGI "Ward" 角丸] 精密な道具らしい引き締めた幾何（旧: 柔らかいプランナー調 12–28dp）。
    //   chip/入力=10dp、カード=14dp、タイル/シート=18dp、大面=24dp。melta-ui: 角丸は tier で一貫（任意値禁止）。
    val shapes = Shapes(
        extraSmall = RoundedCornerShape(10.dp),
        small = RoundedCornerShape(12.dp),
        medium = RoundedCornerShape(14.dp),
        large = RoundedCornerShape(18.dp),
        extraLarge = RoundedCornerShape(24.dp),
    )
    MaterialTheme(colorScheme = colors, typography = t, shapes = shapes, content = content)
}
