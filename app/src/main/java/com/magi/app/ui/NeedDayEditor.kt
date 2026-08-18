package com.magi.app.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Check
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.Modifier
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp

// ============ 希望/必要人数カレンダーで共有するレイアウト部品（スクショ準拠） ============

/** 開始日(ISO)から「YYYY年M月」。パース失敗は空。 */
internal fun monthLabel(startDate: String): String = try {
    val d = java.time.LocalDate.parse(startDate); "${d.year}年${d.monthValue}月"
} catch (_: Exception) { "" }

/** 開始日(ISO)＋期間内の1始まり日番号 → 「M/D(曜)」。月跨ぎも正しく算出。 */
internal fun dayChipLabel(startDate: String, day1: Int): String = try {
    val d = java.time.LocalDate.parse(startDate).plusDays((day1 - 1).toLong())
    "${d.monthValue}/${d.dayOfMonth}(${listOf("月", "火", "水", "木", "金", "土", "日")[d.dayOfWeek.value - 1]})"
} catch (_: Exception) { "${day1}日" }

/** [静的な月見出し] 「‹ 2025年6月 ›」。矢印は淡色の飾り＝月送りは無し（D6決定=1state=1か月）。 */
@Composable
internal fun MonthHeaderStatic(startDate: String) {
    val cs = MaterialTheme.colorScheme
    val label = monthLabel(startDate)
    if (label.isBlank()) return
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.Center, verticalAlignment = Alignment.CenterVertically) {
        Text("‹", style = MaterialTheme.typography.titleMedium, color = cs.outlineVariant, modifier = Modifier.padding(horizontal = 12.dp))
        Text(label, style = MaterialTheme.typography.titleMedium, color = cs.primary, fontWeight = FontWeight.Bold)
        Text("›", style = MaterialTheme.typography.titleMedium, color = cs.outlineVariant, modifier = Modifier.padding(horizontal = 12.dp))
    }
}

/** ドロップダウンの見た目のアンカー（ラベル＋値＋▼）。DropdownMenu を兄弟に置いて開閉する。 */
@Composable
internal fun SelectorField(label: String, value: String, onClick: () -> Unit) {
    val cs = MaterialTheme.colorScheme
    Column(
        Modifier.fillMaxWidth().border(1.dp, cs.outline, RoundedCornerShape(8.dp))
            .clickable(onClick = onClick).padding(horizontal = 12.dp, vertical = 8.dp),
    ) {
        Text(label, style = MaterialTheme.typography.labelSmall, color = cs.onSurfaceVariant)
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(value, style = MaterialTheme.typography.bodyLarge, modifier = Modifier.weight(1f), maxLines = 1)
            Text("▼", style = MaterialTheme.typography.labelMedium, color = cs.onSurfaceVariant)
        }
    }
}

/** 「N日選択中」ピル。 */
@Composable
internal fun CountPill(text: String) {
    Surface(color = MaterialTheme.colorScheme.primary, shape = RoundedCornerShape(50)) {
        Text(text, style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.onPrimary,
            modifier = Modifier.padding(horizontal = 12.dp, vertical = 4.dp))
    }
}

/**
 * [必要人数設定] 勤務作成者が必要とする4点だけに集約した画面（スクショ準拠のリデザイン）:
 *   ①どのシフトか（見出し行のドロップダウン）②各日の最低–最高（カレンダー）③どの日を選んでいるか（枠＋✓）
 *   ④選択日に何人を適用するか（下部のインライン一括パネル）。それ以外は常時表示しない
 *   （長い説明文・独立した「基本設定/複数日選択」カード・設定済/未設定の凡例・充足色ドット・全セルの「未設定」文字を撤去）。
 * 区別は色でなく形と文字で: 標準=通常文字 / 個別設定=太字＋小さな印 / 未設定=「—」 / 選択中=枠＋✓ / 入力エラー=赤枠。
 * 基本(標準)は見出し行に「標準 N人」で示し、タップで編集シート。月送りは無し（D6=1state=1か月）。表示のみ・スコアリング不変。
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun NeedCalendarCard(ui: UiState, vm: MagiViewModel, initialShift: Int? = null, onInitialConsumed: () -> Unit = {}) {
    val v = vm.ws1() ?: return
    if (v.shifts.isEmpty()) return
    var k by remember { mutableStateOf(initialShift?.takeIf { it in v.shifts.indices } ?: 0) }
    if (k !in v.shifts.indices) k = 0
    // [下流→上流ディープリンク] 要確認一覧の covU/covO 項目「設定で直す」から該当シフトを事前選択して開く。
    LaunchedEffect(initialShift) {
        if (initialShift != null) {
            if (initialShift in v.shifts.indices) k = initialShift
            onInitialConsumed()
        }
    }
    val shift = v.shifts[k]
    val cs = MaterialTheme.colorScheme
    var daysSel by remember(k) { mutableStateOf(emptySet<Int>()) }
    var shiftMenu by remember { mutableStateOf(false) }
    var baseSheet by remember { mutableStateOf(false) }
    val onToggleDay: (Int) -> Unit = { d -> daysSel = if (d in daysSel) daysSel - d else daysSel + d }
    val ranges = (0 until ui.days).map { j -> vm.needCellLimits(k, j) }
    // 個別設定＝日別例外が登録された日（0始まり）。カレンダーで太字＋小さな印にする。
    val individualDays = vm.needDayOverrides().filter { it.k == k }.map { it.j }.toSet()
    // 標準（基本設定）の表示ラベル。「N人」または「lo–hi人」、未設定は「未設定」。
    val baseLabel = run {
        val n1 = shift.need1.toIntOrNull(); val n2 = shift.need2.toIntOrNull()
        when {
            n1 == null && n2 == null -> "未設定"
            n2 == null || n2 == n1 -> "${n1 ?: n2}人"
            n1 == null -> "${n2}人"
            else -> "$n1–${n2}人"
        }
    }
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            // [1行に統合] タイトル＋「シフト▼ ・ 標準 N人」。説明文/設定済・未設定の凡例は撤去。
            Text("必要人数設定", style = MaterialTheme.typography.titleMedium)
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                Box {
                    Surface(color = cs.surfaceVariant, shape = RoundedCornerShape(8.dp), modifier = Modifier.clickable { shiftMenu = true }) {
                        Row(Modifier.padding(horizontal = 12.dp, vertical = 8.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                            Text(shift.kigou, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Bold)
                            Text("▼", style = MaterialTheme.typography.labelMedium, color = cs.onSurfaceVariant)
                        }
                    }
                    DropdownMenu(expanded = shiftMenu, onDismissRequest = { shiftMenu = false }) {
                        v.shifts.forEachIndexed { idx, s ->
                            DropdownMenuItem(text = { Text(s.kigou) }, onClick = { k = idx; daysSel = emptySet(); shiftMenu = false })
                        }
                    }
                }
                Text("標準 $baseLabel", style = MaterialTheme.typography.bodyLarge, color = cs.primary,
                    modifier = Modifier.clickable { baseSheet = true }.padding(vertical = 4.dp))
            }
            MonthHeaderStatic(ui.startDate)
            NeedMonthGrid(startDate = ui.startDate, ranges = ranges, individualDays = individualDays, selectedDays = daysSel, onToggle = onToggleDay)
            // [4点目] 1日以上選択したときだけ、下部にインライン一括パネルを表示（専用「複数日選択」カードは撤去）。
            if (daysSel.isNotEmpty()) {
                NeedApplyPanel(ui, vm, k, daysSel, shift.need1, shift.need2,
                    onCancel = { daysSel = emptySet() }, onDone = { daysSel = emptySet() })
            } else {
            }
        }
    }
    if (baseSheet) {
        BaseNeedSheet(shift.kigou, shift.need1, shift.need2, running = ui.running,
            onApply = { p1, p2 -> vm.setShiftNeed(k, p1, p2) }, onDismiss = { baseSheet = false })
    }
}

/**
 * [選択日の一括設定] カレンダー下部にインライン表示（1日以上選択時のみ）。モーダルで隠さないので
 * カレンダーを見ながら追加選択・適用できる。「未設定に戻す」で選択日の例外を削除＝既定へ。入力エラー(最低>最高)は赤枠＋注記。
 */
@Composable
private fun NeedApplyPanel(ui: UiState, vm: MagiViewModel, k: Int, days: Set<Int>, baseN1: String, baseN2: String, onCancel: () -> Unit, onDone: () -> Unit) {
    val cs = MaterialTheme.colorScheme
    var p1 by remember(k) { mutableStateOf(baseN1) }
    var p2 by remember(k) { mutableStateOf(baseN2) }
    val sorted = days.sorted()
    // 選択日が多い場合は「6/3、6/8、6/17、ほか2日」と省略。
    val datesLabel = if (sorted.size <= 4) sorted.joinToString("、") { dayChipLabel(ui.startDate, it) }
    else sorted.take(3).joinToString("、") { dayChipLabel(ui.startDate, it) } + "、ほか${sorted.size - 3}日"
    val n1 = p1.toIntOrNull(); val n2 = p2.toIntOrNull()
    val invalid = n1 != null && n2 != null && n1 > n2
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        HorizontalDivider()
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            CountPill("${days.size}日選択中")
            Text(datesLabel, style = MaterialTheme.typography.labelMedium, color = cs.onSurfaceVariant, maxLines = 2, modifier = Modifier.weight(1f))
        }
        Column(Modifier.border(1.dp, if (invalid) cs.error else Color.Transparent, RoundedCornerShape(8.dp))) {
            NumberStepper("最低人数", p1, { p1 = it }, min = 0, blankLabel = "既定")
            NumberStepper("上限人数", p2, { p2 = it }, min = 0, blankLabel = "既定")
        }
        if (invalid) Text("最低は最高以下にしてください", style = MaterialTheme.typography.labelMedium, color = cs.error)
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            OutlinedButton(onClick = onCancel, enabled = !ui.running, modifier = Modifier.weight(1f).heightIn(min = 48.dp)) { Text("キャンセル") }
            Button(
                onClick = { days.forEach { d -> vm.setNeedDay(k, d - 1, p1, p2) }; onDone() },
                enabled = !ui.running && (p1.isNotBlank() || p2.isNotBlank()) && !invalid,
                modifier = Modifier.weight(1f).heightIn(min = 48.dp),
            ) { Text("${days.size}日に適用") }
        }
        TextButton(onClick = { days.forEach { d -> vm.removeNeedDay(k, d - 1) }; onDone() }, enabled = !ui.running,
            modifier = Modifier.fillMaxWidth()) { Text("選択した日を未設定に戻す") }
    }
}

/** 基本の必要人数（シフト既定need1/need2）編集シート。 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun BaseNeedSheet(kigou: String, need1: String, need2: String, running: Boolean, onApply: (String, String) -> Unit, onDismiss: () -> Unit) {
    val sheetState = rememberModalBottomSheetState()
    var p1 by remember { mutableStateOf(need1) }
    var p2 by remember { mutableStateOf(need2) }
    ModalBottomSheet(onDismissRequest = onDismiss, sheetState = sheetState) {
        Column(Modifier.fillMaxWidth().padding(horizontal = 20.dp).padding(bottom = 28.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            Text("基本の必要人数（${kigou}の既定値）", style = MaterialTheme.typography.titleMedium)
            NumberStepper("最低人数", p1, { p1 = it }, min = 0, blankLabel = "未設定")
            NumberStepper("上限人数", p2, { p2 = it }, min = 0, blankLabel = "未設定")
            Button(onClick = { onApply(p1, p2); onDismiss() }, enabled = !running, modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp)) { Text("保存") }
        }
    }
}

/** [必要人数設定のカレンダー本体] 月内の日を曜日整列で並べ、タップで複数日選択できる。
 *  各日は実効need(lo–hi、`vm.needCellLimits`)を表示。区別は色でなく形と文字で:
 *  未設定=「—」(淡色) / 標準どおり=通常文字 / 個別設定(日別例外)=太字＋小さな印 / 選択中=枠＋✓。
 *  充足色ドット(covU/covO)は本画面(設定)では出さない＝勤務表グリッド/集計で確認する。 */
@Composable
private fun NeedMonthGrid(
    startDate: String,
    ranges: List<Pair<Int, Int>?>,
    individualDays: Set<Int>,
    selectedDays: Set<Int>,
    onToggle: (Int) -> Unit,
) {
    val cs = MaterialTheme.colorScheme
    val sdow = (startDowMonFirst(startDate) + 1) % 7   // 月曜始まり(0=月)→日曜始まり(0=日)へ変換
    val weekJa = listOf("日", "月", "火", "水", "木", "金", "土")
    Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
        Row(horizontalArrangement = Arrangement.spacedBy(4.dp), modifier = Modifier.fillMaxWidth()) {
            weekJa.forEachIndexed { idx, w ->
                Box(Modifier.weight(1f), contentAlignment = Alignment.Center) {
                    Text(w, style = MaterialTheme.typography.labelSmall,
                        color = when (idx) { 0 -> MagiAccent.red; 6 -> MagiAccent.blue; else -> cs.onSurfaceVariant })
                }
            }
        }
        val dayCells: List<Int?> = List(sdow) { null } + ranges.indices.toList()
        dayCells.chunked(7).forEach { wk ->
            Row(horizontalArrangement = Arrangement.spacedBy(4.dp), modifier = Modifier.fillMaxWidth()) {
                wk.forEach { j ->
                    if (j == null) Box(Modifier.weight(1f).height(54.dp))
                    else {
                        val range = ranges[j]
                        val sel = (j + 1) in selectedDays
                        val individual = range != null && j in individualDays
                        val rangeLabel = when {
                            range == null -> "—"
                            range.first == range.second -> "${range.first}"
                            else -> "${range.first}–${range.second}"
                        }
                        Box(
                            Modifier.weight(1f).height(54.dp)
                                .background(if (sel) cs.primaryContainer else cs.surface, RoundedCornerShape(8.dp))
                                .border(if (sel) 2.dp else 1.dp, if (sel) cs.primary else cs.outlineVariant, RoundedCornerShape(8.dp))
                                .clickable { onToggle(j + 1) }
                                .semantics {
                                    contentDescription = "${j + 1}日を" + (if (sel) "選択解除" else "選択") +
                                        (range?.let { "・必要人数${it.first}〜${it.second}人" + (if (individual) "・個別設定" else "") } ?: "・未設定")
                                },
                            contentAlignment = Alignment.Center,
                        ) {
                            Column(horizontalAlignment = Alignment.CenterHorizontally) {
                                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(2.dp)) {
                                    Text("${j + 1}", style = MaterialTheme.typography.bodyMedium,
                                        color = if (sel) cs.onPrimaryContainer else cs.onSurface,
                                        fontWeight = if (sel) FontWeight.Bold else FontWeight.Normal)
                                    // 選択中=✓ / 個別設定=小さな印 / それ以外=印なし。
                                    if (sel) Icon(Icons.Filled.Check, contentDescription = null, tint = cs.primary, modifier = Modifier.size(12.dp))
                                    else if (individual) Box(Modifier.size(5.dp).background(cs.primary, CircleShape))
                                }
                                Text(
                                    rangeLabel,
                                    style = MaterialTheme.typography.labelSmall,
                                    color = when {
                                        sel -> cs.onPrimaryContainer
                                        range == null -> cs.outlineVariant
                                        else -> cs.onSurface
                                    },
                                    fontWeight = if (individual) FontWeight.Bold else FontWeight.Normal,
                                    maxLines = 1,
                                )
                            }
                        }
                    }
                }
                repeat(7 - wk.size) { Box(Modifier.weight(1f).height(54.dp)) }
            }
        }
    }
}

/**
 * ws2 移植: 日別の必要人数（例外）一覧。全シフト横断で例外を一覧確認・削除する専用ビュー
 * （カレンダーは1シフトずつしか見えない弱点を補う）。登録/変更は上の`NeedCalendarCard`へ一本化。
 */
@Composable
fun NeedDayCard(ui: UiState, vm: MagiViewModel) {
    val overrides = vm.needDayOverrides()
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Text("日別の必要人数（例外）一覧", style = MaterialTheme.typography.titleMedium)
            Text(
                "登録・変更は上の必要人数カレンダーから。ここは全シフト横断の一覧確認・削除用です。",
                style = MaterialTheme.typography.labelMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            if (overrides.isEmpty()) {
                Text(
                    "（例外なし — すべて既定値）",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            } else {
                overrides.forEach { o ->
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text(
                            "${o.kigou}  ${o.j + 1}日  最低 ${o.p1.ifBlank { "-" }}人 / 上限 ${o.p2.ifBlank { "-" }}人",
                            style = MaterialTheme.typography.bodyMedium,
                            modifier = Modifier.weight(1f),
                        )
                        DeleteRowButton(onClick = { vm.removeNeedDay(o.k, o.j) }, enabled = !ui.running)
                    }
                }
            }
        }
    }
}

/** 一本指: 数値は「＋ −」の大ボタンで増減（キーボード不要）。空＝既定/なし。同package(StaffRange)と共有。 */
@Composable
internal fun NumberStepper(label: String, value: String, onChange: (String) -> Unit, min: Int, blankLabel: String) {
    val n = value.toIntOrNull()
    Row(verticalAlignment = Alignment.CenterVertically) {
        Text(label, modifier = Modifier.weight(1f), style = MaterialTheme.typography.bodyMedium)
        OutlinedButton(
            // [ゼロ設定] 空欄(なし/既定)から「−」で下限値(=0)へ。0で更に「−」を押すと空欄(クリア)へ戻せる。
            onClick = { when { n == null -> onChange(min.toString()); n <= min -> onChange(""); else -> onChange((n - 1).toString()) } },
            modifier = Modifier.height(48.dp).semantics { contentDescription = "$label を減らす" },
        ) { Text("−", style = MaterialTheme.typography.titleLarge) }
        Box(Modifier.width(72.dp), contentAlignment = Alignment.Center) {
            Text(if (value.isBlank()) blankLabel else value, style = MaterialTheme.typography.titleMedium)
        }
        OutlinedButton(
            onClick = { onChange(((n ?: (min - 1)) + 1).coerceAtLeast(min).toString()) },
            modifier = Modifier.height(48.dp).semantics { contentDescription = "$label を増やす" },
        ) { Text("＋", style = MaterialTheme.typography.titleLarge) }
    }
}
