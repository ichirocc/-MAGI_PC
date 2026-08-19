package com.magi.app.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.Close
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.InputChip
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp

/**
 * ws3 移植: 希望シフト wishes["i,j"]=シフトindex（スタッフ i が j 日目に希望するシフト）。
 * 採点は pref（hard1 のソフト寄り）。モデル(wishes)・エンジン(pref)は既存のため不変、UI のみ。
 * 注意: これは「希望」であり、勤務表セルの「割当」変更とも、cons3系（連勤の並び）とも別概念。
 *
 * [必要人数設定(3.186.0)と同じ「4つの情報」原則を適用] ①どの職員か（見出し行のドロップダウン）
 * ②各日の登録済み希望（カレンダーのシフト色チップ）③どの日を選んでいるか（枠＋✓）④選択日にどのシフトを
 * 適用するか（下部のインライン一括パネル、モーダルではない）。「設定日数N日・シフト別内訳」の常時表示・
 * 「1日1個のみ」等の説明文は撤去。「全職員を見る」（確認・削除専用の一覧）は既定非表示のトグルのまま温存
 * （必要人数設定の「標準N人タップ」「未設定に戻す」と同様、常時は出さないが到達可能な副次機能として残す）。
 * 「1日1個のみ」は wishes["i,j"] が単一値の Map である既存モデルで自動保証。表示のみ・スコア不変。
 */
@OptIn(ExperimentalLayoutApi::class, ExperimentalMaterial3Api::class)
@Composable
fun WishCard(ui: UiState, vm: MagiViewModel, initialStaff: Int? = null, onInitialConsumed: () -> Unit = {}) {
    val staff = ui.staffNames
    val shifts = vm.shiftKigouList()
    if (staff.isEmpty() || shifts.isEmpty()) return
    var i by remember { mutableStateOf(initialStaff?.takeIf { it in staff.indices } ?: 0) }
    if (i !in staff.indices) i = 0
    var daysSel by remember(i) { mutableStateOf(emptySet<Int>()) }
    // [下流→上流ディープリンク] 要確認一覧の pref 項目「設定で直す」から該当職員を事前選択して開く。
    LaunchedEffect(initialStaff) {
        if (initialStaff != null) {
            if (initialStaff in staff.indices) i = initialStaff
            onInitialConsumed()
        }
    }
    var staffMenu by remember { mutableStateOf(false) }
    var showAllStaff by remember { mutableStateOf(false) }
    val cs = MaterialTheme.colorScheme
    val allowed = vm.allowedShiftsFor(i).toHashSet()
    val rows = vm.wishOverrides()
    val myRows = rows.filter { it.i == i }
    val marked = myRows.associate { it.day to it.k }

    val onToggleDay: (Int) -> Unit = { d -> daysSel = if (d in daysSel) daysSel - d else daysSel + d }

    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            Text("希望シフト登録", style = MaterialTheme.typography.titleMedium)
            // [1行に統合] 職員▼のみ常時表示。「全職員を見る」は小さな文字リンクで副次的に残す。
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                Box(Modifier.weight(1f)) {
                    SelectorField(label = "職員", value = staff.getOrElse(i) { "" }, onClick = { staffMenu = true })
                    DropdownMenu(expanded = staffMenu, onDismissRequest = { staffMenu = false }) {
                        staff.forEachIndexed { idx, n ->
                            DropdownMenuItem(text = { Text(n) }, onClick = { i = idx; daysSel = emptySet(); staffMenu = false })
                        }
                    }
                }
                Text(
                    if (showAllStaff) "一覧を隠す" else "全職員を見る",
                    style = MaterialTheme.typography.labelMedium, color = cs.primary,
                    modifier = Modifier.clickable { showAllStaff = !showAllStaff }.padding(vertical = 4.dp),
                )
            }
            MonthHeaderStatic(ui.startDate)
            WishMonthGrid(
                startDate = ui.startDate, maxDay = ui.days, marked = marked,
                shiftKigou = shifts, shiftColorHex = ui.shiftColorHex, shiftTextHex = ui.shiftTextHex,
                selectedDays = daysSel, onToggle = onToggleDay,
            )
            // [4点目] 1日以上選択したときだけ、下部にインライン一括パネルを表示（モーダルシートは撤去）。
            if (daysSel.isNotEmpty()) {
                WishApplyPanel(ui, vm, i, daysSel, shifts, allowed, onCancel = { daysSel = emptySet() }, onDone = { daysSel = emptySet() })
            } else {
            }
            // [全職員横断の一覧] カレンダーは1職員ずつしか見えない弱点を補う確認・削除専用ビュー（既定非表示）。
            if (showAllStaff && rows.isNotEmpty()) {
                Text("登録済み希望（全職員）", style = MaterialTheme.typography.labelLarge, fontWeight = FontWeight.Bold)
                rows.groupBy { it.i }.forEach { (_, list) ->
                    Text(list.first().staffName, style = MaterialTheme.typography.labelMedium, color = cs.onSurfaceVariant)
                    FlowRow(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                        list.forEach { r ->
                            InputChip(
                                selected = false,
                                enabled = !ui.running,
                                onClick = { i = r.i },
                                label = { Text("${r.day}日 ${r.kigou}") },
                                trailingIcon = {
                                    Icon(Icons.Filled.Close, contentDescription = "削除",
                                        modifier = Modifier.size(32.dp).clickable(enabled = !ui.running) { vm.removeWish(r.i, r.j) }.padding(7.dp))
                                },
                            )
                        }
                    }
                }
            }
        }
    }
}

/** [選択日の一括設定] カレンダー下部にインライン表示（1日以上選択時のみ）。モーダルで隠さないので
 *  カレンダーを見ながら追加選択・適用できる。担当可能シフトを主ボタン＋「その他」で全シフト、
 *  「選択した日を未設定に戻す」で希望を一括クリア。 */
@Composable
private fun WishApplyPanel(
    ui: UiState,
    vm: MagiViewModel,
    staffIdx: Int,
    days: Set<Int>,
    shifts: List<String>,
    allowed: HashSet<Int>,
    onCancel: () -> Unit,
    onDone: () -> Unit,
) {
    val cs = MaterialTheme.colorScheme
    val primary = shifts.indices.filter { it in allowed }
    val others = shifts.indices.filter { it !in allowed }
    var selK by remember(staffIdx) { mutableStateOf(primary.firstOrNull() ?: 0) }
    var showOther by remember { mutableStateOf(false) }
    val sorted = days.sorted()
    // 選択日が多い場合は「6/3、6/8、6/17、ほか2日」と省略。
    val datesLabel = if (sorted.size <= 4) sorted.joinToString("、") { dayChipLabel(ui.startDate, it) }
    else sorted.take(3).joinToString("、") { dayChipLabel(ui.startDate, it) } + "、ほか${sorted.size - 3}日"
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        HorizontalDivider()
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            CountPill("${days.size}日選択中")
            Text(datesLabel, style = MaterialTheme.typography.labelMedium, color = cs.onSurfaceVariant, maxLines = 2, modifier = Modifier.weight(1f))
        }
        ShiftButtonGrid(shifts, primary, selK, ui.shiftColorHex, ui.shiftTextHex) { selK = it }
        if (showOther && others.isNotEmpty()) ShiftButtonGrid(shifts, others, selK, ui.shiftColorHex, ui.shiftTextHex, warnSet = others.toHashSet()) { selK = it }
        if (others.isNotEmpty()) {
            TextButton(onClick = { showOther = !showOther }) { Text(if (showOther) "その他を閉じる" else "その他（担当外シフト）") }
        }
        if (selK !in allowed) {
            Text("⚠「${shifts.getOrNull(selK)}」はこの職員の担当外です。希望は登録できますが、配置すると違反になります。",
                style = MaterialTheme.typography.labelSmall, color = cs.error)
        }
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            OutlinedButton(onClick = onCancel, enabled = !ui.running, modifier = Modifier.weight(1f).heightIn(min = 48.dp)) { Text("キャンセル") }
            Button(
                onClick = { vm.setWishesForDays(staffIdx, days.map { it - 1 }, selK); onDone() },
                enabled = !ui.running,
                modifier = Modifier.weight(1f).heightIn(min = 48.dp),
            ) { Text("${days.size}日に適用") }
        }
        TextButton(onClick = { vm.clearWishesForDays(staffIdx, days.map { it - 1 }); onDone() }, enabled = !ui.running,
            modifier = Modifier.fillMaxWidth()) { Text("選択した日を未設定に戻す") }
    }
}

/** 希望シフト選択の大ボタン群（担当外は赤枠＋⚠、背景はシフト表示色）。 */
@Composable
private fun ShiftButtonGrid(
    shifts: List<String>, idxs: List<Int>, selK: Int,
    colorHex: List<String>, textHex: List<String>,
    warnSet: HashSet<Int> = HashSet(), onPick: (Int) -> Unit,
) {
    val cs = MaterialTheme.colorScheme
    idxs.chunked(4).forEach { rowIdxs ->
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
            rowIdxs.forEach { idx ->
                val sel = selK == idx
                val ng = idx in warnSet
                val bg = hexToColor(colorHex.getOrElse(idx) { "" })
                val fg = hexToColor(textHex.getOrElse(idx) { "" })
                val borderColor = when { ng -> MagiAccent.red; sel -> cs.primary; else -> cs.outline }
                Box(
                    Modifier.weight(1f).heightIn(min = 52.dp)
                        .background(bg, MaterialTheme.shapes.small)
                        .border(if (sel) 3.dp else 2.dp, borderColor, MaterialTheme.shapes.small)
                        .clickable { onPick(idx) }
                        .semantics { contentDescription = (shifts.getOrNull(idx) ?: "") + (if (ng) "・担当外" else "") + (if (sel) "・選択中" else "") }
                        .padding(4.dp),
                    contentAlignment = Alignment.Center,
                ) {
                    Column(horizontalAlignment = Alignment.CenterHorizontally) {
                        if (sel) Icon(Icons.Filled.Check, contentDescription = null, tint = fg, modifier = Modifier.size(16.dp))
                        Text((shifts.getOrNull(idx) ?: "") + if (ng) " ⚠" else "",
                            color = fg, textAlign = TextAlign.Center,
                            fontWeight = if (sel) FontWeight.Bold else FontWeight.Normal)
                    }
                }
            }
            repeat(4 - rowIdxs.size) { Spacer(Modifier.weight(1f)) }
        }
    }
}

/** [希望シフトカレンダーの本体グリッド] 月内の日を曜日整列で並べ、タップで複数日選択できる。
 *  各日は登録済みの希望（marked: 日→シフトindex）をシフトの表示色チップ（記号入り）で示す。 */
@Composable
private fun WishMonthGrid(
    startDate: String,
    maxDay: Int,
    marked: Map<Int, Int>,
    shiftKigou: List<String>,
    shiftColorHex: List<String>,
    shiftTextHex: List<String>,
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
        val dayCells: List<Int?> = List(sdow) { null } + (1..maxDay).toList()
        dayCells.chunked(7).forEach { wk ->
            Row(horizontalArrangement = Arrangement.spacedBy(4.dp), modifier = Modifier.fillMaxWidth()) {
                wk.forEach { d ->
                    if (d == null) Box(Modifier.weight(1f).height(56.dp))
                    else {
                        val sel = d in selectedDays
                        val k = marked[d]
                        Box(
                            Modifier.weight(1f).height(56.dp)
                                .background(if (sel) cs.primaryContainer else cs.surface, MaterialTheme.shapes.extraSmall)
                                .border(if (sel) 2.dp else 1.dp, if (sel) cs.primary else cs.outlineVariant, MaterialTheme.shapes.extraSmall)
                                .clickable { onToggle(d) }
                                .semantics {
                                    contentDescription = "${d}日を" + (if (sel) "選択解除" else "選択") +
                                        (k?.let { "・希望登録済み${shiftKigou.getOrNull(it) ?: ""}" } ?: "")
                                },
                            contentAlignment = Alignment.Center,
                        ) {
                            Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(2.dp)) {
                                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(2.dp)) {
                                    Text("$d", style = MaterialTheme.typography.bodyMedium,
                                        color = if (sel) cs.onPrimaryContainer else cs.onSurface,
                                        fontWeight = if (sel) FontWeight.Bold else FontWeight.Normal)
                                    if (sel) Icon(Icons.Filled.Check, contentDescription = null, tint = cs.primary, modifier = Modifier.size(12.dp))
                                }
                                if (k != null) {
                                    val chipBg = hexToColor(shiftColorHex.getOrElse(k) { "" })
                                    val chipFg = hexToColor(shiftTextHex.getOrElse(k) { "" })
                                    Box(Modifier.background(chipBg, RoundedCornerShape(4.dp)).padding(horizontal = 4.dp, vertical = 1.dp)) {
                                        Text(shiftKigou.getOrNull(k) ?: "", style = MaterialTheme.typography.labelSmall, color = chipFg, maxLines = 1)
                                    }
                                }
                            }
                        }
                    }
                }
                repeat(7 - wk.size) { Box(Modifier.weight(1f).height(56.dp)) }
            }
        }
    }
}
