package com.magi.app.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.wrapContentHeight
import androidx.compose.ui.draw.clip
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Card
import androidx.compose.material3.Divider
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

/**
 * ws3-5 constraint editor, rendered as a card in the single-scroll layout. Adding or
 * removing a constraint rebuilds state and re-runs the unified violation check
 * (see MagiViewModel.mutateConstraints -> refreshCheck), so the breakdown/score/v6
 * panels above update automatically. Edited constraints are written back on JSON save.
 */
@Composable
fun ConstraintsCard(
    ui: UiState,
    vm: MagiViewModel,
    title: String = "ルールの編集（勤務の並び・回数）",
    keys: Set<String>? = null,
) {
    var addFamily by remember { mutableStateOf<String?>(null) }
    // [制約編集] 行タップで既存行を変更（追加ダイアログのプリフィル版）。実機指摘「登録した制約の変更ができない」。
    var editTarget by remember { mutableStateOf<Pair<String, Int>?>(null) }
    // [発見性] keys 指定時はその family だけ描画。群の C41/C42 を専用節に分けて見つけやすくするため。
    val families = vm.constraintFamilies().let { all -> if (keys == null) all else all.filter { it.key in keys } }

    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp)) {
            if (title.isNotBlank()) Text(title, style = MaterialTheme.typography.titleMedium)
            families.forEachIndexed { fi, fam ->
                if (fi > 0) Spacer(Modifier.height(6.dp))
                Spacer(Modifier.height(8.dp))
                Text(fam.title, fontSize = 14.sp, fontWeight = FontWeight.Bold)
                if (fam.rows.isEmpty()) {
                    Text("(なし)", fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                } else {
                    fam.rows.forEachIndexed { idx, row ->
                        ConstraintRow(row,
                            onEdit = { editTarget = fam.key to idx },
                            onDelete = { vm.removeConstraint(fam.key, idx) })
                    }
                }
                AddRowButton("追加", onClick = { addFamily = fam.key })
                Divider()
            }
        }
    }

    val fam = addFamily
    if (fam != null) ConstraintDialog(fam, vm, onClose = { addFamily = null })
    editTarget?.let { (k, i) -> ConstraintDialog(k, vm, editIndex = i, onClose = { editTarget = null }) }
}

/**
 * 制約1行: 本文タップ=変更、右端に「編集」「削除」。
 *
 * [3.396.0] 旧実装は本文が 12sp の素のテキストで、KDoc も「タップ可能に見えるよう最小高44dpを確保」と
 * 書いていた——だが **44dp は触れる大きさであって見た目の手がかりではない**。だからカードの上に
 * 「行をタップすると変更できます」という貼り紙が要り、それでも実機で「登録した制約の変更ができない」と
 * 報告された（3.130.0）。職員一覧（`StaffManageCard`）は同じ「行を編集する」操作を**編集ボタン**で
 * 表しているので、同じ操作は同じ形にする。貼り紙は剥がした。
 */
@Composable
private fun ConstraintRow(row: String, onEdit: () -> Unit, onDelete: () -> Unit) {
    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
        Text(row, fontSize = 12.sp, modifier = Modifier
            .weight(1f)
            .clip(MaterialTheme.shapes.small)
            .clickable(onClick = onEdit)
            .heightIn(min = 48.dp)
            .wrapContentHeight(Alignment.CenterVertically)
            .padding(horizontal = 4.dp))
        EditRowButton(onClick = onEdit)
        Spacer(Modifier.width(6.dp))
        DeleteRowButton(onClick = onDelete)
    }
}

/** [校正] スキルグループの C41/C42（cons41s/cons42s）をスキルグループ定義の直下に co-locate。
 *  汎用ルール（ユニット群）と混ざって埋もれていた問題を解消し、見つけやすくする。 */
@Composable
fun SkillConstraintsCard(ui: UiState, vm: MagiViewModel) {
    var addFamily by remember { mutableStateOf<String?>(null) }
    var editTarget by remember { mutableStateOf<Pair<String, Int>?>(null) }
    val families = vm.skillConstraintFamilies()
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp)) {
            Text("上の「スキルグループ」に対する専用ルールです。スキル群のレンジ（1日の人数）と、スキル群ペア禁止（同じ日に不可）を設定します。",
                fontSize = 12.sp, color = MaterialTheme.colorScheme.primary)
            if (vm.skillGroupKigouList().isEmpty()) {
                Spacer(Modifier.height(8.dp))
                Text("先に上で「スキルグループ」を追加すると設定できます。",
                    fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
            } else {
                families.forEachIndexed { fi, fam ->
                    if (fi > 0) Spacer(Modifier.height(6.dp))
                    Spacer(Modifier.height(8.dp))
                    Text(fam.title, fontSize = 14.sp, fontWeight = FontWeight.Bold)
                    if (fam.rows.isEmpty()) {
                        Text("(なし)", fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
                    } else {
                        fam.rows.forEachIndexed { idx, row ->
                            ConstraintRow(row,
                                onEdit = { editTarget = fam.key to idx },
                                onDelete = { vm.removeConstraint(fam.key, idx) })
                        }
                    }
                    AddRowButton("追加", onClick = { addFamily = fam.key })
                    Divider()
                }
            }
        }
    }
    val fam = addFamily
    if (fam != null) ConstraintDialog(fam, vm, onClose = { addFamily = null })
    editTarget?.let { (k, i) -> ConstraintDialog(k, vm, editIndex = i, onClose = { editTarget = null }) }
}

/** 追加・変更を兼ねる制約ダイアログ。editIndex 指定時は既存行の値をプリフィルし、確定で同じ位置を置換。 */
@Composable
private fun ConstraintDialog(family: String, vm: MagiViewModel, editIndex: Int? = null, onClose: () -> Unit) {
    val shifts = vm.shiftKigouList()
    val groups = vm.groupKigouList()
    val skills = vm.skillGroupKigouList()
    val shiftsOpt = listOf("") + shifts
    // 値の並びは vm.constraintRowValues と同一（追加ダイアログの入力順）。
    val init = remember(family, editIndex) { editIndex?.let { vm.constraintRowValues(family, it) } }
    val mode = if (editIndex != null) "を変更" else "を追加"
    val okLabel = if (editIndex != null) "変更" else "追加"
    fun commit(values: List<String>, add: () -> Unit) {
        if (editIndex != null) vm.updateConstraint(family, editIndex, values) else add()
        onClose()
    }

    when (family) {
        "cons1" -> {
            var d1 by remember { mutableStateOf(init?.getOrNull(0) ?: "") }
            var sk by remember { mutableStateOf(init?.getOrNull(1) ?: shifts.firstOrNull() ?: "") }
            var d2 by remember { mutableStateOf(init?.getOrNull(2) ?: "") }
            Shell("窓の要件$mode", okLabel, onClose, { commit(listOf(d1, sk, d2)) { vm.addCons1(d1, sk, d2) } },
                d1.isNotBlank() && sk.isNotBlank() && d2.isNotBlank()) {
                NumField("何日間", d1) { d1 = it }
                Picker("シフト", shifts, sk) { sk = it }
                NumField("必要数(以上)", d2) { d2 = it }
            }
        }
        "cons2" -> {
            var sk by remember { mutableStateOf(init?.getOrNull(0) ?: shifts.firstOrNull() ?: "") }
            var c by remember { mutableStateOf(init?.getOrNull(1) ?: "") }
            Shell("個人の合計$mode", okLabel, onClose, { commit(listOf(sk, c)) { vm.addCons2(sk, c) } },
                sk.isNotBlank() && c.isNotBlank()) {
                Picker("シフト", shifts, sk) { sk = it }
                NumField("合計(以上)", c) { c = it }
            }
        }
        "cons41" -> {
            var gk by remember { mutableStateOf(init?.getOrNull(0) ?: groups.firstOrNull() ?: "") }
            var sk by remember { mutableStateOf(init?.getOrNull(1) ?: shifts.firstOrNull() ?: "") }
            var l by remember { mutableStateOf(init?.getOrNull(2) ?: "") }
            var u by remember { mutableStateOf(init?.getOrNull(3) ?: "") }
            Shell("群のレンジ（1日の人数）$mode", okLabel, onClose, { commit(listOf(gk, sk, l, u)) { vm.addCons41(gk, sk, l, u) } },
                gk.isNotBlank() && sk.isNotBlank()) {
                Picker("グループ", groups, gk) { gk = it }
                Picker("シフト", shifts, sk) { sk = it }
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    NumField("下限(空=0)", l, Modifier.weight(1f)) { l = it }
                    NumField("上限(空=無制限)", u, Modifier.weight(1f)) { u = it }
                }
            }
        }
        "cons42" -> {
            var g1 by remember { mutableStateOf(init?.getOrNull(0) ?: groups.firstOrNull() ?: "") }
            var s1 by remember { mutableStateOf(init?.getOrNull(1) ?: shifts.firstOrNull() ?: "") }
            var g2 by remember { mutableStateOf(init?.getOrNull(2) ?: groups.firstOrNull() ?: "") }
            var s2 by remember { mutableStateOf(init?.getOrNull(3) ?: shifts.firstOrNull() ?: "") }
            Shell("群ペア禁止$mode", okLabel, onClose, { commit(listOf(g1, s1, g2, s2)) { vm.addCons42(g1, g2, s1, s2) } },
                g1.isNotBlank() && s1.isNotBlank() && g2.isNotBlank() && s2.isNotBlank()) {
                Picker("グループ1", groups, g1) { g1 = it }
                Picker("シフト1", shifts, s1) { s1 = it }
                Picker("グループ2", groups, g2) { g2 = it }
                Picker("シフト2", shifts, s2) { s2 = it }
            }
        }
        "cons41s" -> {
            var gk by remember { mutableStateOf(init?.getOrNull(0) ?: skills.firstOrNull() ?: "") }
            var sk by remember { mutableStateOf(init?.getOrNull(1) ?: shifts.firstOrNull() ?: "") }
            var l by remember { mutableStateOf(init?.getOrNull(2) ?: "") }
            var u by remember { mutableStateOf(init?.getOrNull(3) ?: "") }
            Shell("スキル群のレンジ（1日の人数）$mode", okLabel, onClose, { commit(listOf(gk, sk, l, u)) { vm.addCons41s(gk, sk, l, u) } },
                gk.isNotBlank() && sk.isNotBlank()) {
                Picker("スキル", skills, gk) { gk = it }
                Picker("シフト", shifts, sk) { sk = it }
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    NumField("下限(空=0)", l, Modifier.weight(1f)) { l = it }
                    NumField("上限(空=無制限)", u, Modifier.weight(1f)) { u = it }
                }
            }
        }
        "cons42s" -> {
            var g1 by remember { mutableStateOf(init?.getOrNull(0) ?: skills.firstOrNull() ?: "") }
            var s1 by remember { mutableStateOf(init?.getOrNull(1) ?: shifts.firstOrNull() ?: "") }
            var g2 by remember { mutableStateOf(init?.getOrNull(2) ?: skills.firstOrNull() ?: "") }
            var s2 by remember { mutableStateOf(init?.getOrNull(3) ?: shifts.firstOrNull() ?: "") }
            Shell("スキル群ペア禁止$mode", okLabel, onClose, { commit(listOf(g1, s1, g2, s2)) { vm.addCons42s(g1, g2, s1, s2) } },
                g1.isNotBlank() && s1.isNotBlank() && g2.isNotBlank() && s2.isNotBlank()) {
                Picker("スキル1", skills, g1) { g1 = it }
                Picker("シフト1", shifts, s1) { s1 = it }
                Picker("スキル2", skills, g2) { g2 = it }
                Picker("シフト2", shifts, s2) { s2 = it }
            }
        }
        "cons3", "cons3n", "cons3m", "cons3mn" -> {
            var a by remember { mutableStateOf(init?.getOrNull(0) ?: shifts.firstOrNull() ?: "") }
            var b by remember { mutableStateOf(init?.getOrNull(1) ?: "") }
            var c by remember { mutableStateOf(init?.getOrNull(2) ?: "") }
            var d by remember { mutableStateOf(init?.getOrNull(3) ?: "") }
            var e by remember { mutableStateOf(init?.getOrNull(4) ?: "") }
            val kind = when (family) {
                "cons3n" -> "禁止の並び"
                "cons3m" -> "推奨の並び"
                "cons3mn" -> "回避の並び"
                else -> "必須の並び"
            }
            Shell(kind + mode, okLabel, onClose, { commit(listOf(a, b, c, d, e)) { vm.addCons3(family, listOf(a, b, c, d, e)) } }, a.isNotBlank()) {
                Text("並び (上から順・最大5連日 / 空=ここで終了)", fontSize = 12.sp,
                    color = MaterialTheme.colorScheme.onSurfaceVariant)
                Picker("1番目", shifts, a) { a = it }
                Picker("2番目", shiftsOpt, b) { b = it }
                Picker("3番目", shiftsOpt, c) { c = it }
                Picker("4番目", shiftsOpt, d) { d = it }
                Picker("5番目", shiftsOpt, e) { e = it }
            }
        }
        else -> onClose()
    }
}

@Composable
private fun Shell(
    title: String,
    okLabel: String,
    onClose: () -> Unit,
    onAdd: () -> Unit,
    addEnabled: Boolean,
    content: @Composable ColumnScope.() -> Unit,
) {
    AlertDialog(
        onDismissRequest = onClose,
        confirmButton = { DialogConfirmButton(okLabel, enabled = addEnabled, onClick = onAdd) },
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
private fun NumField(label: String, value: String, modifier: Modifier = Modifier.width(150.dp), onChange: (String) -> Unit) {
    OutlinedTextField(
        value = value,
        onValueChange = { onChange(it.filter { c -> c.isDigit() }) },
        label = { Text(label, fontSize = 12.sp) },
        singleLine = true,
        // [レイアウト整合] 既定は width(150)。Row 内2連で並べる箇所は weight(1f) を渡してダイアログ幅からの溢れ(欠け)を防ぐ。
        modifier = modifier,
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
    )
}

@Composable
private fun Picker(label: String, options: List<String>, selected: String, onSelect: (String) -> Unit) {
    var open by remember { mutableStateOf(false) }
    Column {
        Text(label, fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
        Box {
            OutlinedButton(onClick = { open = true }) {
                Text(if (selected.isBlank()) "(なし)" else selected)
            }
            DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
                options.forEach { opt ->
                    DropdownMenuItem(
                        text = { Text(if (opt.isBlank()) "(なし)" else opt, fontFamily = FontFamily.Monospace) },
                        onClick = { onSelect(opt); open = false },
                    )
                }
            }
        }
    }
}
