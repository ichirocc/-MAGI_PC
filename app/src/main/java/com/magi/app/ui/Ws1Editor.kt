package com.magi.app.ui

import com.magi.app.toHankakuKigou

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Check
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Card
import androidx.compose.material3.Divider
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Switch
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

/**
 * ws1 (初期設定) editor card. Edits the problem definition: period length (days),
 * use2 flag, shifts / groups / staff (rename + per-field edit, append-add), and the
 * group×shift bucket. Each change re-dimensions the working table consistently
 * (MagiViewModel.ws1* -> Ws1Ops) and re-runs the check; saving emits the full state.
 * Remove operations are deferred to a later increment.
 */
@OptIn(ExperimentalLayoutApi::class)
@Composable
fun Ws1Card(ui: UiState, vm: MagiViewModel) {
    val v = vm.ws1() ?: return
    var dialog by remember { mutableStateOf<Ws1Dialog?>(null) }
    var daysText by remember(v.days) { mutableStateOf(v.days.toString()) }

    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp)) {
            LoadoutHeader("LOADOUT", "装備BOX — マスター設定")
            Spacer(Modifier.height(6.dp))
            Text("変更すると表を作り直し、すぐ問題がないか調べ直します。", fontSize = 14.sp,
                color = MaterialTheme.colorScheme.primary)

            // --- period ---
            Spacer(Modifier.height(10.dp))
            LoadoutHeader("PERIOD", "期間／対象月")
            Text("${v.startDate} 〜 ${v.endDate}", fontSize = 14.sp,
                color = MaterialTheme.colorScheme.onSurfaceVariant)
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                W1Field("日数(1-31)", daysText, Modifier.width(130.dp)) { daysText = it }
                EditRowButton(onClick = { daysText.toIntOrNull()?.let { vm.ws1ResizeDays(it) } }, enabled = !ui.running, text = "変更")
            }

            // --- use2 ---
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                Text("必要人数の2パターン目を使う（特殊な月用・通常はOFF）", fontSize = 14.sp, modifier = Modifier.weight(1f))
                Switch(checked = v.use2, onCheckedChange = { vm.ws1SetUse2(it) })
            }
            Divider()

            // --- shifts ---
            Spacer(Modifier.height(8.dp))
            LoadoutHeader("ARSENAL", "装備／シフト種別 (${v.shifts.size})")
            Text("編集で記号・名前・必要人数を変更（勤務表と制約にも反映）。", style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            v.shifts.forEachIndexed { k, s ->
                // [不具合修正] 行に .clickable が無く、シフト行をタップしても選択/編集できなかった
                //   （小さな「編集」ボタンのみ反応）。行全体タップで編集ダイアログを開く。
                Row(Modifier.fillMaxWidth().heightIn(min = 48.dp).clickable(enabled = !ui.running) { dialog = Ws1Dialog.EditShift(k, s.name, s.kigou, s.need1, s.need2) },
                    verticalAlignment = Alignment.CenterVertically) {
                    Text("${toHankakuKigou(s.kigou)}  ${s.name}  (最低 ${s.need1.ifBlank { "-" }}人 / 上限 ${s.need2.ifBlank { "-" }}人)",
                        fontSize = 14.sp, modifier = Modifier.weight(1f))
                    EditRowButton(onClick = { dialog = Ws1Dialog.EditShift(k, s.name, s.kigou, s.need1, s.need2) }, enabled = !ui.running)
                    if (v.shifts.size > 1) {
                        Spacer(Modifier.width(6.dp))
                        DeleteRowButton(onClick = { dialog = Ws1Dialog.ConfirmDelete("shift", k, "シフト ${toHankakuKigou(s.kigou)}") }, enabled = !ui.running)
                    }
                }
            }
            // [3.409.11] 残り1シフトのとき削除ボタンが**理由の説明なく消える**（3.400.0 でグループには
            //   理由を付けたが、シフトと職員は対象漏れだった）。同じ形で理由を出す。
            if (v.shifts.size <= 1) {
                Text("最後の1シフトは削除できません（勤務表のセルが指す先が無くなるため）。", fontSize = 12.sp, color = MaterialTheme.colorScheme.error)
            }
            AddRowButton("シフト追加", onClick = { dialog = Ws1Dialog.AddShift }, enabled = !ui.running)
            AddRowButton("一括追加", onClick = { dialog = Ws1Dialog.BulkAddShift }, enabled = !ui.running)   // [⛏12]
            Divider()

            // --- groups ---
            Spacer(Modifier.height(8.dp))
            LoadoutHeader("SQUAD", "班／グループ (${v.groups.size})")
            Text("編集で改名。削除すると所属者は先頭グループへ移動。", style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            // [不具合報告「グループが削除出来ない」対応] 残り1グループの場合、削除ボタンが理由の説明なく
            //   消えるだけだった（担当可否の分類が無くなるため意図的に不可＝休シフトの削除不可と同型の
            //   ガードだが、そちらには理由メッセージがある一方こちらは無言で消えるだけだった）。理由を明示。
            if (v.groups.size <= 1) {
                Text("最後の1グループは削除できません（担当可否の分類が無くなるため）。", fontSize = 12.sp, color = MaterialTheme.colorScheme.error)
            }
            v.groups.forEachIndexed { g, gr ->
                // [押下明示O4] 行タップで編集（シフト行と統一・小さな編集ボタンだけに依存しない）。
                Row(Modifier.fillMaxWidth().heightIn(min = 48.dp).clickable(enabled = !ui.running) { dialog = Ws1Dialog.EditGroup(g, gr.name, gr.kigou) },
                    verticalAlignment = Alignment.CenterVertically) {
                    Text("${toHankakuKigou(gr.kigou)}  ${gr.name}", fontSize = 14.sp, modifier = Modifier.weight(1f))
                    EditRowButton(onClick = { dialog = Ws1Dialog.EditGroup(g, gr.name, gr.kigou) }, enabled = !ui.running)
                    if (vm.ws1CanRemoveGroup(g)) {
                        val members = vm.ws1GroupMemberCount(g)
                        Spacer(Modifier.width(6.dp))
                        DeleteRowButton(onClick = {
                            val label = "グループ ${toHankakuKigou(gr.kigou)}" + if (members > 0) "（所属${members}名→先頭グループへ移動）" else ""
                            dialog = Ws1Dialog.ConfirmDelete("group", g, label)
                        }, enabled = !ui.running)
                    }
                }
            }
            AddRowButton("グループ追加", onClick = { dialog = Ws1Dialog.AddGroup }, enabled = !ui.running)
            Divider()

            // --- staff ---
            Spacer(Modifier.height(8.dp))
            LoadoutHeader("PARTY", "仲間／職員 (${v.staff.size})")
            Text("編集で改名・所属グループの変更。", style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            v.staff.forEachIndexed { i, st ->
                val gk = v.groups.getOrNull(st.groupIdx)?.kigou?.let { toHankakuKigou(it) } ?: "?"
                // [押下明示O4] 行タップで編集（シフト/グループ行と統一）。
                Row(Modifier.fillMaxWidth().heightIn(min = 48.dp).clickable(enabled = !ui.running) { dialog = Ws1Dialog.EditStaff(i, st.name, st.groupIdx) },
                    verticalAlignment = Alignment.CenterVertically) {
                    Text("${st.name}  [グループ $gk]", fontSize = 14.sp, modifier = Modifier.weight(1f))
                    EditRowButton(onClick = { dialog = Ws1Dialog.EditStaff(i, st.name, st.groupIdx) }, enabled = !ui.running)
                    if (v.staff.size > 1) {
                        Spacer(Modifier.width(6.dp))
                        DeleteRowButton(onClick = { dialog = Ws1Dialog.ConfirmDelete("staff", i, st.name) }, enabled = !ui.running)
                    }
                }
            }
            // [3.409.11] 同上（残り1職員のときも無言で消えていた）。
            if (v.staff.size <= 1) {
                Text("最後の1名は削除できません（勤務表の行が無くなるため）。", fontSize = 12.sp, color = MaterialTheme.colorScheme.error)
            }
            AddRowButton("職員追加", onClick = { dialog = Ws1Dialog.AddStaff }, enabled = !ui.running)
            AddRowButton("一括追加", onClick = { dialog = Ws1Dialog.BulkAddStaff }, enabled = !ui.running)   // [⛏12]
            Divider()

            // --- groupShift bucket ---
            Spacer(Modifier.height(8.dp))
            LoadoutHeader("MATRIX", "担当可否（群 × シフト）")
            Text("チップで担当できるシフトを切替（✓＝担当可）。", style = MaterialTheme.typography.labelSmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            // [見やすさ] 横スクロール(12シフトで画面外)をやめ、群ごとに群名を行頭＋チップを FlowRow で折り返す。
            //   選択中＝塗り＋✓（色だけに依存しない手がかり）、未選択＝外枠。
            v.groups.forEachIndexed { g, gr ->
                Spacer(Modifier.height(4.dp))
                Text("${toHankakuKigou(gr.kigou)}  ${gr.name}", fontSize = 14.sp, fontWeight = FontWeight.Bold, fontFamily = FontFamily.Monospace)
                FlowRow(horizontalArrangement = Arrangement.spacedBy(6.dp), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                    v.shifts.forEachIndexed { k, s ->
                        val on = v.groupShift.getOrNull(g)?.getOrNull(k) == 1
                        FilterChip(
                            selected = on,
                            enabled = !ui.running,   // [3.409.13] 実行中は applyStructure が必ず拒否＝押せる形は嘘（3.405.0）
                            onClick = { vm.ws1SetGroupShift(g, k, !on) },
                            label = { Text(toHankakuKigou(s.kigou), fontFamily = FontFamily.Monospace) },
                            leadingIcon = if (on) {
                                { Icon(Icons.Filled.Check, contentDescription = null, modifier = Modifier.size(16.dp)) }
                            } else null,
                        )
                    }
                }
            }

            // [③回数へ移動] 適切回数(apt)gridは「回数（1人あたり）」節の AptCard へ分離した。

        }
    }

    when (val d = dialog) {
        is Ws1Dialog.EditShift -> ShiftDialog("シフト編集", d.name, d.kigou, d.need1, d.need2,
            { n, kg, n1, n2 -> vm.ws1EditShift(d.k, n, kg, n1, n2); dialog = null }, { dialog = null })
        Ws1Dialog.AddShift -> ShiftDialog("シフト追加", "", "", "", "",
            { n, kg, n1, n2 -> vm.ws1AddShift(n, kg, n1, n2); dialog = null }, { dialog = null })
        is Ws1Dialog.EditGroup -> GroupDialog("グループ編集", d.name, d.kigou,
            { n, kg -> vm.ws1EditGroup(d.g, n, kg); dialog = null }, { dialog = null })
        Ws1Dialog.AddGroup -> GroupDialog("グループ追加", "", "",
            { n, kg -> vm.ws1AddGroup(n, kg); dialog = null }, { dialog = null })
        is Ws1Dialog.EditStaff -> StaffDialog("職員編集", d.name, d.groupIdx, v.groups.map { toHankakuKigou(it.kigou) },
            { n, gi -> vm.ws1EditStaff(d.i, n, gi); dialog = null }, { dialog = null })
        Ws1Dialog.AddStaff -> StaffDialog("職員追加", "", 0, v.groups.map { toHankakuKigou(it.kigou) },
            { n, gi -> vm.ws1AddStaff(n, gi); dialog = null }, { dialog = null })
        Ws1Dialog.BulkAddShift -> BulkAddDialog("シフトを一括追加", "記号を改行で複数入力（例: 休 / Dﾃ / A4）。記号がそのまま名称になります。", null,
            { lines, _ -> lines.forEach { vm.ws1AddShift(it, it, "", "") }; dialog = null }, { dialog = null })
        Ws1Dialog.BulkAddStaff -> BulkAddDialog("職員を一括追加", "名前を改行で複数入力。全員を既定グループに追加します（後で個別変更可）。", v.groups.map { toHankakuKigou(it.kigou) },
            { lines, gi -> lines.forEach { vm.ws1AddStaff(it, gi) }; dialog = null }, { dialog = null })
        is Ws1Dialog.ConfirmDelete -> AlertDialog(
            onDismissRequest = { dialog = null },
            confirmButton = {
                DialogDangerButton("削除", onClick = {
                    when (d.kind) {
                        "shift" -> vm.ws1RemoveShift(d.index)
                        "group" -> vm.ws1RemoveGroup(d.index)
                        "staff" -> vm.ws1RemoveStaff(d.index)
                    }
                    dialog = null
                })
            },
            dismissButton = { DialogDismissButton(onClick = { dialog = null }) },
            title = { Text("削除の確認") },
            text = { Text("${d.label} を削除します。割当やインデックスが再構成されます。よろしいですか？") },
        )
        null -> Unit
    }
}

private sealed interface Ws1Dialog {
    data class EditShift(val k: Int, val name: String, val kigou: String, val need1: String, val need2: String) : Ws1Dialog
    object AddShift : Ws1Dialog
    data class EditGroup(val g: Int, val name: String, val kigou: String) : Ws1Dialog
    object AddGroup : Ws1Dialog
    data class EditStaff(val i: Int, val name: String, val groupIdx: Int) : Ws1Dialog
    object AddStaff : Ws1Dialog
    object BulkAddShift : Ws1Dialog
    object BulkAddStaff : Ws1Dialog
    data class ConfirmDelete(val kind: String, val index: Int, val label: String) : Ws1Dialog
}

@Composable
private fun ShiftDialog(
    title: String, name0: String, kigou0: String, need10: String, need20: String,
    onOk: (String, String, String, String) -> Unit, onClose: () -> Unit,
) {
    var name by remember { mutableStateOf(name0) }
    var kigou by remember { mutableStateOf(kigou0) }
    var need1 by remember { mutableStateOf(need10) }
    var need2 by remember { mutableStateOf(need20) }
    W1Shell(title, onClose, { onOk(name, kigou, need1, need2) }, kigou.isNotBlank()) {
        W1Text("記号 (kigou)", kigou) { kigou = it }
        W1Text("名称", name) { name = it }
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            W1Field("最低人数", need1, Modifier.weight(1f)) { need1 = it }
            W1Field("上限人数(2パターン時)", need2, Modifier.weight(1f)) { need2 = it }
        }
    }
}

@Composable
private fun GroupDialog(
    title: String, name0: String, kigou0: String,
    onOk: (String, String) -> Unit, onClose: () -> Unit,
) {
    var name by remember { mutableStateOf(name0) }
    var kigou by remember { mutableStateOf(kigou0) }
    W1Shell(title, onClose, { onOk(name, kigou) }, kigou.isNotBlank()) {
        W1Text("記号 (kigou)", kigou) { kigou = it }
        W1Text("名称", name) { name = it }
    }
}

@Composable
internal fun StaffDialog(
    title: String, name0: String, group0: Int, groupKigou: List<String>,
    onOk: (String, Int) -> Unit, onClose: () -> Unit,
) {
    var name by remember { mutableStateOf(name0) }
    var gi by remember { mutableStateOf(group0.coerceIn(0, (groupKigou.size - 1).coerceAtLeast(0))) }
    W1Shell(title, onClose, { onOk(name, gi) }, name.isNotBlank() && groupKigou.isNotEmpty()) {
        W1Text("名称", name) { name = it }
        var open by remember { mutableStateOf(false) }
        Text("グループ", fontSize = 14.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
        if (groupKigou.isEmpty()) {
            // [A7] 鶏卵問題の誘導：グループが無いとスタッフの所属先が決められない（OKは無効）。
            Text("先に「グループ」を追加してください。職員はグループに所属します。",
                fontSize = 14.sp, color = MaterialTheme.colorScheme.error)
        } else {
            OutlinedButton(onClick = { open = true }) { Text(groupKigou.getOrNull(gi) ?: "(なし)") }
            DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
                groupKigou.forEachIndexed { idx, kg ->
                    DropdownMenuItem(text = { Text(kg, fontFamily = FontFamily.Monospace) },
                        onClick = { gi = idx; open = false })
                }
            }
        }
    }
}

/**
 * [⛏12] 改行区切りで複数件をまとめて追加する汎用ダイアログ。1件ずつの追加(各4-5tap×N)を
 * 1回の入力に短縮し、ゼロ構築の操作量を削減する。groups!=null のときは既定グループを選ぶ
 * (スタッフ用)。groups==null はグループ選択なし(シフト用)。追加は呼び出し側で既存の
 * ws1AddStaff/ws1AddShift をループ呼びするだけ＝ロジックは不変。
 */
@Composable
private fun BulkAddDialog(
    title: String, hint: String, groups: List<String>?,
    onApply: (List<String>, Int) -> Unit, onClose: () -> Unit,
) {
    var text by remember { mutableStateOf("") }
    var gi by remember { mutableStateOf(0) }
    var open by remember { mutableStateOf(false) }
    val lines = text.split("\n").map { it.trim() }.filter { it.isNotEmpty() }
    val groupOk = groups == null || groups.isNotEmpty()
    W1Shell(title, onClose, { onApply(lines, gi) }, lines.isNotEmpty() && groupOk) {
        Text(hint, fontSize = 14.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
        OutlinedTextField(
            value = text, onValueChange = { text = it }, singleLine = false, minLines = 3,
            label = { Text("1行に1件", fontSize = 14.sp) }, modifier = Modifier.fillMaxWidth(),
        )
        if (groups != null) {
            if (groups.isEmpty()) {
                Text("先に「グループ」を追加してください。", fontSize = 14.sp, color = MaterialTheme.colorScheme.error)
            } else {
                Text("既定のグループ", fontSize = 14.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                OutlinedButton(onClick = { open = true }) { Text(groups.getOrNull(gi) ?: "(なし)") }
                DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
                    groups.forEachIndexed { idx, kg ->
                        DropdownMenuItem(text = { Text(kg, fontFamily = FontFamily.Monospace) },
                            onClick = { gi = idx; open = false })
                    }
                }
            }
        }
        if (lines.isNotEmpty()) Text("追加: ${lines.size}件", fontSize = 14.sp, color = MaterialTheme.colorScheme.primary)
    }
}

@Composable
private fun W1Shell(
    title: String, onClose: () -> Unit, onOk: () -> Unit, okEnabled: Boolean,
    content: @Composable ColumnScope.() -> Unit,
) {
    AlertDialog(
        onDismissRequest = onClose,
        confirmButton = { DialogConfirmButton("OK", enabled = okEnabled, onClick = onOk) },
        dismissButton = { DialogDismissButton(onClick = onClose) },
        title = { DialogHeader(title, onClose) },
        text = {
            Column(
                modifier = Modifier.verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(10.dp),
            ) { content() }
        },
    )
}

@Composable
private fun W1Text(label: String, value: String, onChange: (String) -> Unit) {
    OutlinedTextField(value = value, onValueChange = onChange, singleLine = true,
        label = { Text(label, fontSize = 14.sp) }, modifier = Modifier.fillMaxWidth())
}

@Composable
private fun W1Field(label: String, value: String, modifier: Modifier = Modifier, onChange: (String) -> Unit) {
    OutlinedTextField(
        value = value,
        onValueChange = { onChange(it.filter { c -> c.isDigit() }) },
        label = { Text(label, fontSize = 14.sp) }, singleLine = true, modifier = modifier,
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
    )
}

/** 適切回数のステッパー（記号 −[値]＋）。空欄＝目標なし。0も設定可（空欄→0→1…、0で−→空欄）。 */
@OptIn(ExperimentalLayoutApi::class)
@Composable
fun AptCard(ui: UiState, vm: MagiViewModel) {
    val v = vm.ws1() ?: return
    var confirmResetApt by remember { mutableStateOf(false) }
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp)) {
            Text("目標（1人あたり・やわらかい）", fontSize = 14.sp, fontWeight = FontWeight.Bold)
            Text("「この勤務に1か月で何回くらい入ってほしい」という目安です。最適化が各人をその回数に近づけようとします（必ず守るわけではありません。空欄＝目標なし）。担当ONの勤務にだけ設定できます。",
                fontSize = 14.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)

            // [目標の検算をその場で] 目標の合計がそれを受け止められる上限を超えていると、何をしても
            //   目標割れか過剰配置が必ず出る。判定は V6SanityPort.aptBalances（設定ミス診断の検査6-C と
            //   同じ単一ソース・盤面不要）。診断カードはホーム/分析タブにあり、目標を入力している最中は
            //   見えないため、ここで直接示して「入れた瞬間に気づける」ようにする。
            val overloaded = remember(ui.editRev, ui.structureEdited) { vm.aptBalances().filter { it.overloaded } }
            if (overloaded.isNotEmpty()) {
                Spacer(Modifier.height(8.dp))
                Surface(color = MaterialTheme.colorScheme.errorContainer, shape = MaterialTheme.shapes.medium) {
                    Column(Modifier.fillMaxWidth().padding(12.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                        Text("この目標は達成できません", fontSize = 14.sp, fontWeight = FontWeight.Bold,
                            color = MaterialTheme.colorScheme.onErrorContainer)
                        overloaded.forEach { b ->
                            Text(
                                "${toHankakuKigou(b.kigou)}：目標の合計 ${b.aptSum}回 に対し、" +
                                    (if (b.isRest) "休める日数の上限は ${b.capacity}日" else "必要人数の合計は ${b.capacity}回") +
                                    "。${b.shortfall}回ぶんは必ず届きません。",
                                fontSize = 14.sp, color = MaterialTheme.colorScheme.onErrorContainer,
                            )
                            Text(
                                if (b.isRest)
                                    "→ ${toHankakuKigou(b.kigou)}の目標を下げるか、ほかの勤務の回数の下限を見直してください。"
                                else
                                    "→ ${toHankakuKigou(b.kigou)}の目標を下げるか、必要人数を増やしてください。",
                                fontSize = 13.sp, color = MaterialTheme.colorScheme.onErrorContainer,
                            )
                        }
                    }
                }
            }
            if (v.groups.isNotEmpty()) {
                val aptSet = v.groupShiftApt.sumOf { row -> row.count { it.trim().isNotEmpty() } }
                Spacer(Modifier.height(4.dp))
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    DeleteRowButton(onClick = { confirmResetApt = true }, enabled = aptSet > 0, text = "目標を全リセット")
                    Text(if (aptSet > 0) "設定中 $aptSet 件" else "設定なし",
                        fontSize = 14.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
            v.groups.forEachIndexed { g, gr ->
                val onShifts = v.shifts.indices.filter { v.groupShift.getOrNull(g)?.getOrNull(it) == 1 }
                if (onShifts.isNotEmpty()) {
                    Spacer(Modifier.height(4.dp))
                    Text("${toHankakuKigou(gr.kigou)}  ${gr.name}", fontSize = 14.sp, fontWeight = FontWeight.Bold, fontFamily = FontFamily.Monospace)
                    FlowRow(horizontalArrangement = Arrangement.spacedBy(6.dp), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                        onShifts.forEach { k ->
                            val apt = v.groupShiftApt.getOrNull(g)?.getOrNull(k) ?: ""
                            AptStepper(label = toHankakuKigou(v.shifts[k].kigou), value = apt,
                                onChange = { vm.ws1SetGroupApt(g, k, it) })
                        }
                    }
                }
            }
        }
    }

    if (confirmResetApt) {
        AlertDialog(
            onDismissRequest = { confirmResetApt = false },
            title = { Text("目標を全リセット") },
            text = {
                Text(
                    "全グループ×全シフトの「目標」を空欄（目標なし）に戻します。\n" +
                        "・目標由来のやわらかい違反は消えます\n" +
                        "・担当ON/OFF・回数の下限上限・勤務表は変わりません\n" +
                        "・「元に戻す」で復帰できます\n実行しますか？",
                    fontSize = 14.sp,
                )
            },
            confirmButton = {
                DialogDangerButton("全リセット", onClick = { vm.ws1ResetGroupApt(); confirmResetApt = false })
            },
            dismissButton = { DialogDismissButton(onClick = { confirmResetApt = false }) },
        )
    }
}

@Composable
private fun AptStepper(label: String, value: String, onChange: (String) -> Unit) {
    Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(end = 6.dp)) {
        Text(label, fontSize = 14.sp, fontFamily = FontFamily.Monospace)
        TextButton(onClick = {
            // [ゼロ設定] 0を許可（従来は「1未満→空欄」で目標0が設定できなかった）。空欄→0 / 0→空欄 / N→N-1。
            val c = value.trim().toIntOrNull()
            onChange(when { c == null -> "0"; c <= 0 -> ""; else -> (c - 1).toString() })
        }, modifier = Modifier.semantics { contentDescription = "$label の適切回数を減らす" }) { Text("−", fontSize = 16.sp) }
        Text(value.ifBlank { "—" }, fontSize = 14.sp, fontFamily = FontFamily.Monospace,
            modifier = Modifier.width(22.dp), color = MaterialTheme.colorScheme.onSurface)
        TextButton(onClick = {
            val c = value.trim().toIntOrNull() ?: -1
            onChange((c + 1).coerceAtLeast(0).toString())
        }, modifier = Modifier.semantics { contentDescription = "$label の適切回数を増やす" }) { Text("＋", fontSize = 16.sp) }
    }
}

// ===== 装備BOX(LOADOUT) セクション識別見出し =====
// HUDコンセプトの二段見出し(英コードネーム — 日本語)を踏襲。マスター設定を1枚に統合した識別子。
@Composable
private fun LoadoutHeader(code: String, jp: String) {
    Row(verticalAlignment = Alignment.Bottom, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        Text(code, fontSize = 12.sp, fontWeight = FontWeight.Bold, letterSpacing = 1.sp, color = MaterialTheme.colorScheme.primary)
        Text(jp, fontSize = 14.sp, fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.onSurface)
    }
}
