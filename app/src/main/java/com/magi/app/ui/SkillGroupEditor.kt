package com.magi.app.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.KeyboardArrowDown
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Card
import androidx.compose.material3.Divider
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Icon
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
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

/**
 * [スキルグループ新設・年次マスター] ユニットグループとは別の第2分類を編集する。担当可否には影響せず、
 * スキル別の回数(cons41s)/組み合わせ禁止(cons42s) だけが参照する。1人1スキル。
 */
@Composable
fun SkillGroupCard(ui: UiState, vm: MagiViewModel) {
    if (!ui.loaded) return
    val cs = MaterialTheme.colorScheme
    val skills = vm.skillGroups()
    val staff = vm.ws1()?.staff ?: emptyList()
    var dialog by remember { mutableStateOf<SkillDlg?>(null) }
    // [破壊操作ガード] スキル群削除は職員の skillIdx を再割当てする高影響操作。確認を挟む。
    var confirmDelete by remember { mutableStateOf<Int?>(null) }

    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Text("担当シフトには影響しない別の分類。スキル別の回数/禁止ペアだけが参照します（1人1スキル）。",
                fontSize = 12.sp, color = cs.onSurfaceVariant)

            skills.forEachIndexed { g, sg ->
                Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                    Text("${sg.kigou}  ${sg.name}", fontSize = 12.sp, modifier = Modifier.weight(1f))
                    EditRowButton(onClick = { dialog = SkillDlg.Edit(g, sg.name, sg.kigou) }, enabled = !ui.running)
                    Spacer(Modifier.width(6.dp))
                    DeleteRowButton(onClick = { confirmDelete = g }, enabled = !ui.running)
                }
            }
            AddRowButton("スキルグループ追加", onClick = { dialog = SkillDlg.Add }, enabled = !ui.running)

            if (skills.isNotEmpty()) {
                Divider()
                // [B6] fontSize=13sp が titleSmall(16sp・+1sp スケール)を打ち消していた。override を外し scale に従わせる。
                Text("職員のスキル割当", style = MaterialTheme.typography.titleSmall)
                staff.forEachIndexed { i, st ->
                    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                        Text(st.name, fontSize = 12.sp, modifier = Modifier.weight(1f))
                        var open by remember { mutableStateOf(false) }
                        Box {
                            OutlinedButton(onClick = { open = true }, enabled = !ui.running, modifier = Modifier.heightIn(min = 48.dp)) {
                                Text(skills.getOrNull(st.skillIdx)?.kigou ?: "(なし)")
                                // [校正] ドロップダウンと分かるよう下向き矢印アフォーダンスを付与。
                                Icon(
                                    Icons.Filled.KeyboardArrowDown,
                                    contentDescription = "スキルを選ぶ",
                                    modifier = Modifier.padding(start = 2.dp),
                                )
                            }
                            DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
                                // [スキル解除] どのスキルにも所属させない選択肢(skillIdx=-1)。engine は ssk==groupIdx で常に偽=未所属。
                                DropdownMenuItem(text = { Text("(なし)") }, onClick = { vm.setStaffSkill(i, -1); open = false })
                                skills.forEachIndexed { gi, sg ->
                                    DropdownMenuItem(text = { Text("${sg.kigou}  ${sg.name}") }, onClick = { vm.setStaffSkill(i, gi); open = false })
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    when (val d = dialog) {
        SkillDlg.Add -> SkillGroupDialog("スキルグループ追加", "", "", onOk = { n, k -> vm.addSkillGroup(n, k); dialog = null }, onClose = { dialog = null })
        is SkillDlg.Edit -> SkillGroupDialog("スキルグループ編集", d.name, d.kigou, onOk = { n, k -> vm.editSkillGroup(d.g, n, k); dialog = null }, onClose = { dialog = null })
        null -> {}
    }

    confirmDelete?.let { g ->
        val name = skills.getOrNull(g)?.let { "${it.kigou} ${it.name}" } ?: "このスキルグループ"
        AlertDialog(
            onDismissRequest = { confirmDelete = null },
            title = { Text("スキルグループを削除しますか？") },
            text = { Text("「$name」を削除します。所属していた職員のスキル割当は自動で付け替わります。元に戻すで取り消せます。") },
            confirmButton = { DialogDangerButton("削除する", onClick = { vm.removeSkillGroup(g); confirmDelete = null }) },
            dismissButton = { DialogDismissButton(onClick = { confirmDelete = null }) },
        )
    }
}

private sealed interface SkillDlg {
    object Add : SkillDlg
    data class Edit(val g: Int, val name: String, val kigou: String) : SkillDlg
}

@Composable
private fun SkillGroupDialog(title: String, name0: String, kigou0: String, onOk: (String, String) -> Unit, onClose: () -> Unit) {
    var name by remember { mutableStateOf(name0) }
    var kigou by remember { mutableStateOf(kigou0) }
    AlertDialog(
        onDismissRequest = onClose,
        confirmButton = { DialogConfirmButton("OK", enabled = kigou.isNotBlank(), onClick = { if (kigou.isNotBlank()) onOk(name, kigou) }) },
        dismissButton = { DialogDismissButton(onClick = onClose) },
        title = { DialogHeader(title, onClose) },
        text = {
            Column(modifier = Modifier.verticalScroll(rememberScrollState()), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                OutlinedTextField(value = kigou, onValueChange = { if (it.length <= 4) kigou = it }, label = { Text("記号（例: N）") }, singleLine = true)
                OutlinedTextField(value = name, onValueChange = { name = it }, label = { Text("名前（例: 看護）") }, singleLine = true)
            }
        },
    )
}
