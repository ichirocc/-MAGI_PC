package com.magi.app.ui

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.selection.selectableGroup
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.compositeOver
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp

/**
 * MAGI 共通コンポーネント（docs/magi_design_system.md §4）。
 * Material3 を一次ソースとし、添付テイスト（角丸・余白・大数値・セグメント）を素直に表現する。
 * 指1本操作前提: タップ標的は最小 48dp、セグメントは最小 44dp 高。
 */

/** §4.5 セグメントコントロール（外観モード / 制約レベル / カレンダー表示切替）。 */
@Composable
fun MagiSegmentedControl(
    options: List<String>,
    selected: Int,
    onSelect: (Int) -> Unit,
    modifier: Modifier = Modifier,
) {
    val cs = MaterialTheme.colorScheme
    Surface(
        modifier = modifier.fillMaxWidth(),
        color = cs.surfaceVariant,
        shape = MaterialTheme.shapes.large,
    ) {
        Row(
            Modifier.padding(4.dp).selectableGroup(),
            horizontalArrangement = Arrangement.spacedBy(4.dp),
        ) {
            options.forEachIndexed { i, label ->
                val active = i == selected
                Surface(
                    onClick = { onSelect(i) },
                    // [a11y/touch] Surface(onClick) は 48dp 自動補完が無いため明示。選択状態を読み上げに公開。
                    modifier = Modifier.weight(1f).heightIn(min = 48.dp).semantics { this.selected = active },
                    // [Planner テイスト] 選択中はやわらかい色付きピル（白の段差でなく穏やかな塗り）。
                    color = if (active) cs.primaryContainer else Color.Transparent,
                    contentColor = if (active) cs.onPrimaryContainer else cs.onSurfaceVariant,
                    shape = MaterialTheme.shapes.medium,
                    tonalElevation = 0.dp,
                ) {
                    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.Center, verticalAlignment = Alignment.CenterVertically) {
                        Text(
                            label,
                            // [レイアウト整合] 狭幅(320dp)/フォント拡大でも制御ラベルが硬クリップせず省略表示へ退避。左右に微小padding。
                            modifier = Modifier.padding(horizontal = 4.dp, vertical = 10.dp),
                            style = MaterialTheme.typography.labelLarge,
                            fontWeight = if (active) FontWeight.SemiBold else FontWeight.Normal,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                        )
                    }
                }
            }
        }
    }
}

/** §4.6 中央大数値ゲージ（満足度 / 総合スコア）。Canvas を使わず大数値＋線で軽量に表現。 */
@Composable
fun MagiScoreGauge(
    score: Int,
    max: Int = 100,
    label: String,
    sub: String? = null,
    accent: Color? = null,
    modifier: Modifier = Modifier,
) {
    val cs = MaterialTheme.colorScheme
    val tint = accent ?: cs.primary
    val ratio = if (max > 0) (score.toFloat() / max).coerceIn(0f, 1f) else 0f
    Column(
        modifier.fillMaxWidth(),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        Row(verticalAlignment = Alignment.Bottom) {
            Text("$score", style = MaterialTheme.typography.displaySmall, color = tint, fontWeight = FontWeight.Bold)
            Text(" / $max", style = MaterialTheme.typography.titleMedium, color = cs.onSurfaceVariant)
        }
        LinearProgressIndicator(
            progress = { ratio },
            modifier = Modifier.fillMaxWidth().heightIn(min = 8.dp),
            color = tint,
            trackColor = cs.surfaceVariant,
        )
        Text(label, style = MaterialTheme.typography.titleSmall, color = cs.onSurface, textAlign = TextAlign.Center)
        sub?.let { Text(it, style = MaterialTheme.typography.bodySmall, color = cs.onSurfaceVariant, textAlign = TextAlign.Center) }
    }
}

/** §4.8 カテゴリタグ（軽い色付きピル）。色は意味色（MagiAccent）やシフト色を渡す。 */
@Composable
fun MagiTagChip(
    text: String,
    color: Color,
    modifier: Modifier = Modifier,
    leadingIcon: ImageVector? = null,
) {
    // [UD監査] 淡い塗り(α0.14)上の生アクセント文字は 橙2.7:1/緑3.4:1 で不足。実効背景（白地に合成後）に
    //   対して WCAG を保証（不足時は自動で濃色へ）。
    val chipBg = color.copy(alpha = 0.14f).compositeOver(MaterialTheme.colorScheme.surface)
    Surface(
        modifier = modifier,
        // [Planner テイスト] やわらかい塗り＋細い同系ボーダーで、クリーム地でも輪郭がやさしく締まるピル。
        color = color.copy(alpha = 0.14f),
        contentColor = ensureReadable(chipBg, color),
        shape = RoundedCornerShape(50),
        border = BorderStroke(1.dp, color.copy(alpha = 0.30f)),
    ) {
        Row(
            Modifier.padding(horizontal = 11.dp, vertical = 6.dp),
            horizontalArrangement = Arrangement.spacedBy(4.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            leadingIcon?.let { Icon(it, contentDescription = null, modifier = Modifier.heightIn(min = 18.dp)) }
            // [判読性] タグチップ文字を 13sp→15sp(labelLarge/SemiBold)へ。
            Text(text, style = MaterialTheme.typography.labelLarge, fontWeight = FontWeight.SemiBold, maxLines = 1)
        }
    }
}
