package com.magi.app.ui

import com.magi.app.toHankakuKigou
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Card
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

/**
 * [入口4分割/職員管理] 入退職・改名・所属グループ・資格スキルを職員単位で随時変更する1枚。
 * 年間マスター(Ws1Card)のスタッフ節と同じ ViewModel API（ws1EditStaff/ws1AddStaff/ws1RemoveStaff/
 * setStaffSkill）を使う職員中心の別ビュー（併存＝安全）。個人の回数上下限は年間マスター③「回数（1人あたり）」
 * の CountsCard（StaffRangeSection）が担当。
 */
@Composable
fun StaffManageCard(ui: UiState, vm: MagiViewModel) {
    val v = vm.ws1() ?: return
    val skills = vm.skillGroups()
    val cs = MaterialTheme.colorScheme
    var edit by remember { mutableStateOf<Triple<Int, String, Int>?>(null) }   // (i, name, groupIdx)
    var addOpen by remember { mutableStateOf(false) }
    var confirmDelete by remember { mutableStateOf<Int?>(null) }
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            Text("職員一覧（${v.staff.size}名）", style = MaterialTheme.typography.titleMedium)
            v.staff.forEachIndexed { i, st ->
                val gk = v.groups.getOrNull(st.groupIdx)?.kigou?.let { toHankakuKigou(it) } ?: "?"
                Row(
                    Modifier.fillMaxWidth().heightIn(min = 48.dp)
                        .clickable(enabled = !ui.running) { edit = Triple(i, st.name, st.groupIdx) },
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Column(Modifier.weight(1f)) {
                        Text(st.name, style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.SemiBold)
                        Text("グループ $gk", style = MaterialTheme.typography.labelSmall, color = cs.onSurfaceVariant)
                    }
                    if (skills.isNotEmpty()) {
                        var open by remember { mutableStateOf(false) }
                        Box {
                            OutlinedButton(onClick = { open = true }, enabled = !ui.running, modifier = Modifier.heightIn(min = 48.dp)) {
                                Text(skills.getOrNull(st.skillIdx)?.kigou ?: "(なし)")
                            }
                            DropdownMenu(expanded = open, onDismissRequest = { open = false }) {
                                DropdownMenuItem(text = { Text("(なし)") }, onClick = { vm.setStaffSkill(i, -1); open = false })
                                skills.forEachIndexed { gi, sg ->
                                    DropdownMenuItem(text = { Text("${sg.kigou}  ${sg.name}") }, onClick = { vm.setStaffSkill(i, gi); open = false })
                                }
                            }
                        }
                        Spacer(Modifier.width(6.dp))
                    }
                    EditRowButton(onClick = { edit = Triple(i, st.name, st.groupIdx) }, enabled = !ui.running)
                    if (v.staff.size > 1) {
                        Spacer(Modifier.width(6.dp))
                        DeleteRowButton(onClick = { confirmDelete = i }, enabled = !ui.running)
                    }
                }
            }
            // [3.409.11] 残り1名のとき削除ボタンが理由なく消える（年間マスター側と同じ対象漏れ）。
            if (v.staff.size <= 1) {
                Text("最後の1名は削除できません（勤務表の行が無くなるため）。", fontSize = 12.sp, color = MaterialTheme.colorScheme.error)
            }
            AddRowButton("入職（職員追加）", onClick = { addOpen = true }, enabled = ui.loaded && !ui.running)
        }
    }
    edit?.let { (i, nm, gi0) ->
        StaffDialog("職員の編集（改名・所属）", nm, gi0, v.groups.map { toHankakuKigou(it.kigou) },
            { n, gi -> vm.ws1EditStaff(i, n, gi); edit = null }, { edit = null })
    }
    if (addOpen) {
        StaffDialog("入職（職員追加）", "", 0, v.groups.map { toHankakuKigou(it.kigou) },
            { n, gi -> vm.ws1AddStaff(n, gi); addOpen = false }, { addOpen = false })
    }
    confirmDelete?.let { i ->
        AlertDialog(
            onDismissRequest = { confirmDelete = null },
            confirmButton = { DialogDangerButton("削除（退職）", onClick = { vm.ws1RemoveStaff(i); confirmDelete = null }) },
            dismissButton = { DialogDismissButton(onClick = { confirmDelete = null }) },
            title = { DialogHeader("退職・削除の確認", { confirmDelete = null }) },
            text = { Text("${v.staff.getOrNull(i)?.name ?: ""} を削除します。この職員の勤務・希望も消えます。") },
        )
    }
}
