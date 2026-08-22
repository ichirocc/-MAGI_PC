package com.magi.app.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.ui.draw.clip
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Card
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp

/** "#rrggbb"/"#rgb" -> Compose Color。同パッケージ(ScheduleGrid)と共有。不正値はグレー。 */
internal fun hexToColor(hex: String): Color {
    val h = hex.trim().removePrefix("#")
    val full = when (h.length) {
        3 -> buildString { h.forEach { append(it); append(it) } }
        6 -> h
        else -> "888888"
    }
    val v = full.toIntOrNull(16) ?: 0x888888
    return Color((v shr 16) and 0xFF, (v shr 8) and 0xFF, v and 0xFF)
}

// [実機指摘 3.129系] 12色(5+5+2)では最終行2個が weight で巨大化＋既定色(必須の赤/要調整の橙)が
//   パレットに無く「現在の色が画面の中にない・他の色も選択できない」だった。20色=5×4の完全グリッドにし、
//   既定色(#BA1A1A 必須 / #E08A1E 要調整)と MagiAccent 系(赤/橙/緑/青/紫/桃/灰)を含める。
private val COLOR_PALETTE = listOf(
    "#ba1a1a", "#d23b34", "#c0563f", "#e08a1e", "#d7a13b",
    "#6fa56b", "#2e9e62", "#4fa89c", "#556b2f", "#8a979b",
    "#5fb3d4", "#3b6fd4", "#3f6fc0", "#34558b", "#8195a8",
    "#8a5cd1", "#7a4fd0", "#a96bff", "#d24d89", "#6e6354",
)

/**
 * colors 移植: シフトの表示色設定。
 * shiftColors[kigou] の上書きを編集。既定は一覧上の位置で決まる色（resolveShiftColor）。
 * 表示専用のため採点・エンジンに影響しない。勤務表グリッドに反映される。
 */
@OptIn(ExperimentalLayoutApi::class)
@Composable
fun ShiftColorCard(ui: UiState, vm: MagiViewModel) {
    var target by remember { mutableStateOf<String?>(null) }
    val shifts = vm.shiftColorList()
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Text("シフトの表示色", style = MaterialTheme.typography.titleMedium)
            Text(
                "勤務表に表示される各シフトの色（既定はシフト種別ごとの色）。",
                style = MaterialTheme.typography.labelMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            if (shifts.isEmpty()) {
                Text(
                    "（データ未読込）",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            } else {
                // [校正] 縦長の一覧をやめ、スウォッチ＋記号のコンパクトなチップを折り返しグリッドに。
                //   カスタム色は枠色（primary）で「指定」を表現（テキスト列を削減＝冗長解消）。
                FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    shifts.forEach { sc ->
                        ColorChip(hex = sc.hex, label = sc.kigou, custom = sc.custom, enabled = !ui.running) { target = sc.kigou }
                    }
                }
            }
            // [IA重複解消 3.132系] 旧「違反の色（必須違反）」節（__vio__ のみの入口）は撤去。違反の色は
            //   直下の ColorSettingsView（違反種別の色＝基準色2種＋族別）に一本化（詳細設定から移動）。
        }
    }
    target?.let { kg ->
        val current = shifts.firstOrNull { it.kigou == kg }
        ColorPickerDialog(
            kigou = kg,
            currentHex = current?.hex ?: "",
            onPick = { hex -> vm.setShiftColor(kg, hex); target = null },
            onReset = { vm.resetShiftColor(kg); target = null },
            onClose = { target = null },
        )
    }
}

/**
 * 色を変える操作の共通の形。**枠＋色見本＋ラベル**で「押すと色が変わる」を形そのものが示す
 * （説明文で補わない）。同じ操作は同じ形にする＝シフトの表示色と違反種別の色が同一の見た目になり、
 * 片方だけ形が変わって取り残される事故も起きない。
 * custom=true（利用者が明示指定した色）は枠を太く primary にして「指定済み」を示す。
 */
@Composable
internal fun ColorChip(
    hex: String,
    label: String,
    custom: Boolean,
    enabled: Boolean = true,
    onClick: () -> Unit,
) {
    val cs = MaterialTheme.colorScheme
    Row(
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(8.dp),
        modifier = Modifier
            .heightIn(min = 48.dp)
            .clip(MaterialTheme.shapes.medium)
            .border(if (custom) 2.dp else 1.dp, if (custom) cs.primary else cs.outline, MaterialTheme.shapes.medium)
            .clickable(enabled = enabled, onClick = onClick)
            .padding(horizontal = 12.dp, vertical = 8.dp),
    ) {
        Swatch(hex, 24.dp)
        Text(label, style = MaterialTheme.typography.bodyMedium)
    }
}

@Composable
private fun Swatch(hex: String, sizeDp: androidx.compose.ui.unit.Dp) {
    Box(
        Modifier
            .size(sizeDp)
            .background(hexToColor(hex), MaterialTheme.shapes.small)
            .border(1.dp, MaterialTheme.colorScheme.outline.copy(alpha = 0.5f), MaterialTheme.shapes.small),
    )
}

@Composable
internal fun ColorPickerDialog(
    kigou: String,
    currentHex: String,
    onPick: (String) -> Unit,
    onReset: () -> Unit,
    onClose: () -> Unit,
    defaultHex: String = "",
) {
    // [実機指摘「現在の設定している色が画面の中にない」] 未設定(空)のときグレーの偽色を出していた →
    //   実効色(既定色)を表示し、パレット上の一致スウォッチにも✓を付ける。
    val effectiveHex = currentHex.ifBlank { defaultHex }
    AlertDialog(
        onDismissRequest = onClose,
        confirmButton = { DialogConfirmButton("閉じる", onClick = onClose) },
        dismissButton = { DialogDismissButton(onClick = onReset, text = "既定に戻す") },
        title = { DialogHeader("「$kigou」の色", onClose) },
        text = {
            Column(modifier = Modifier.verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Swatch(effectiveHex, 28.dp)
                    Text(if (currentHex.isBlank()) "  現在の色（既定）" else "  現在の色",
                        style = MaterialTheme.typography.bodyMedium)
                }
                Text("色を選ぶ", style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
                val perRow = 5
                COLOR_PALETTE.chunked(perRow).forEach { rowColors ->
                    // [不具合修正×2] 固定40dp×6は幅超過で6個目が切れ、weight等分は端数行(2個)が巨大化していた。
                    //   幅いっぱいを等分(weight)＋正方形(aspectRatio)＋端数行は空 Spacer で埋めて全行同サイズに。
                    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        rowColors.forEach { hex ->
                            val selected = hex.equals(effectiveHex, ignoreCase = true)
                            Box(
                                Modifier
                                    .weight(1f)
                                    .aspectRatio(1f)
                                    .background(hexToColor(hex), MaterialTheme.shapes.extraSmall)
                                    .border(
                                        if (selected) 3.dp else 1.dp,
                                        if (selected) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.outline,
                                        MaterialTheme.shapes.extraSmall,
                                    )
                                    .clickable { onPick(hex) }
                                    // [a11y] 色のみの選択肢に読み上げ名を付与。
                                    .semantics { contentDescription = "色 $hex" + (if (selected) "・選択中" else "") },
                                contentAlignment = Alignment.Center,
                            ) {
                                if (selected) Text("✓", color = hexToColor(pickFg(hex)), textAlign = TextAlign.Center)
                            }
                        }
                        repeat(perRow - rowColors.size) { Spacer(Modifier.weight(1f).aspectRatio(1f)) }
                    }
                }
                Spacer(Modifier.height(2.dp))
            }
        },
    )
}

/** 簡易: 明度から文字色を選ぶ（チェック印用）。 */
private fun pickFg(bgHex: String): String {
    val h = bgHex.trim().removePrefix("#")
    val v = (if (h.length == 6) h else "888888").toIntOrNull(16) ?: 0x888888
    val r = (v shr 16) and 0xFF; val g = (v shr 8) and 0xFF; val b = v and 0xFF
    val lum = (0.299 * r + 0.587 * g + 0.114 * b)
    return if (lum > 140) "#14110d" else "#fbf4e8"
}

