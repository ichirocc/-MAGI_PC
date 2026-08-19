package com.magi.app.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Card
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.magi.app.v6.MirrorKeys
import com.magi.app.v6.ShiftAppearance

// [3.86.0 デッドコード撤去] 未描画の合成画面 `V6RemainingScreens` と、そこからのみ実呼出だった
//   HeaderBar / RingGauge / OverviewDashboard / FlagsView / OperatorLogView / BottomNav を撤去
//   （外部参照0を確認。並列監査 3.84.0 の報告に基づく）。
// [3.286.0 冗長性D] CheckSummaryView（分析タブの必須違反数1行）も撤去＝ホームのカード・要確認一覧見出しとの
//   3重表示を解消（呼出0となったため定義ごと削除）。残るのは live な ColorSettingsView（設定タブ）と
//   それが使う SectionSegment のみ。表示のみ・スコアリング不変。

@Composable
fun SectionSegment(title: String, subtitle: String? = null, content: @Composable () -> Unit) {
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp)) {
            Text(title, fontWeight = FontWeight.Bold)
            if (subtitle != null) Text(subtitle, fontSize = 12.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
            Spacer(Modifier.height(10.dp))
            content()
        }
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
fun ColorSettingsView(ui: UiState, vm: MagiViewModel) {
    // [色変更/スクショ指摘] 旧版は read-only の凡例で、チップを押しても何も起きず「色を変更出来ない」と誤解を
    //   招いていた（カード題も「違反種別の色」）。チップタップでその重大度の色を変更できるように:
    //   必須=既存トークン __vio__（外観の「違反の色」と同一）/ 要調整=新トークン __vioSoft__。灰=情報は固定。
    //   変更はグリッド枠・角マーク・日ヘッダ下線・凡例へ即反映。表示のみ・スコアリング不変。
    // [3.122.0 族別色] チップタップで「その種別（族）の色」を個別に変更（実機指摘「個別に設定できない」）。
    //   未設定の族は重大度色（必須=__vio__/要調整=__vioSoft__）で表示＝従来互換。族色はグリッドの枠・角マーク・
    //   カレンダー・編集シートの理由テキストへ即反映（resolvedVioColor が族→重大度の順で解決）。
    var pickFam by remember { mutableStateOf<String?>(null) }   // 族キー(c1/c3n/…)
    // [3.397.0 形が語る] 旧版は「背景を丸ごと色で塗った白文字の四角」＝これは凡例(badge)の形であって
    //   操作できる形ではなかった。だから「チップをタップで…変更できます」という貼り紙が要り、それでも
    //   実機で「個別に設定できない」と報告された。**同じ操作は同じ形**にする＝シフトの表示色と同一の
    //   ColorChip（枠＋色見本＋ラベル）へ揃え、貼り紙を剥がす。副次的にユーザー指定色の上へ白文字を
    //   載せる必要が無くなり、コントラスト担保(ensureReadable)の綱渡りも消える。
    SectionSegment("違反種別の色") {
        val cs = MaterialTheme.colorScheme
        // 基準色の実効値（未設定なら既定＝必須は UD 赤 / 要調整はアンバー。ピッカーの defaultHex と同値）。
        val baseHard = ui.violationColorHex.ifBlank { "#BA1A1A" }
        val baseSoft = ui.violationSoftColorHex.ifBlank { "#E08A1E" }
        FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            MirrorKeys.all.forEach { key ->
                val sev = ShiftAppearance.severityFromVioKey(key)
                // [色覚/日本語化] 色に依らない文字バックアップ。英語enum(CRITICAL/…)ではなく日本語の重大度語で示す。
                val sevJp = when (sev) { "CRITICAL" -> "必須"; "HIGH", "WARN" -> "要調整"; else -> "情報" }
                val famHex = ui.violationFamilyColorHex[key]?.takeIf { it.isNotBlank() }
                val hex = famHex ?: when (sev) {
                    "CRITICAL" -> baseHard
                    "HIGH", "WARN" -> baseSoft
                    else -> "#8A979B"   // 情報（族色を設定すればそれが優先）
                }
                val count = ui.breakdown[key] ?: 0
                val label = "${breakdownLabels[key] ?: key}（$sevJp）" + (if (count > 0) " $count件" else "")
                ColorChip(hex = hex, label = label, custom = famHex != null, enabled = !ui.running) { pickFam = key }
            }
        }
        Spacer(Modifier.height(12.dp))
        Text("未設定の種別に効く基準色", fontSize = 12.sp, color = cs.onSurfaceVariant)
        Spacer(Modifier.height(6.dp))
        FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            ColorChip(hex = baseHard, label = "必須", custom = ui.violationColorHex.isNotBlank(), enabled = !ui.running) { pickFam = "__hard__" }
            ColorChip(hex = baseSoft, label = "要調整", custom = ui.violationSoftColorHex.isNotBlank(), enabled = !ui.running) { pickFam = "__soft__" }
        }
    }
    pickFam?.let { pf ->
        // [実機指摘] 未設定時にグレーの偽「現在の色」を出さないよう、実効の既定色を defaultHex で渡す
        //   （必須=UD赤 / 要調整=橙 / 族=その重大度の基準色）。ColorPickerDialog 側で✓も一致表示。
        val hardHex = ui.violationColorHex.ifBlank { "#BA1A1A" }
        val softHex = ui.violationSoftColorHex.ifBlank { "#E08A1E" }
        when (pf) {
            "__hard__" -> ColorPickerDialog(
                kigou = "必須違反（基準色）",
                currentHex = ui.violationColorHex,
                defaultHex = "#BA1A1A",
                onPick = { hex -> vm.setViolationColor(hex); pickFam = null },
                onReset = { vm.resetViolationColor(); pickFam = null },
                onClose = { pickFam = null },
            )
            "__soft__" -> ColorPickerDialog(
                kigou = "要調整（基準色）",
                currentHex = ui.violationSoftColorHex,
                defaultHex = "#E08A1E",
                onPick = { hex -> vm.setViolationSoftColor(hex); pickFam = null },
                onReset = { vm.resetViolationSoftColor(); pickFam = null },
                onClose = { pickFam = null },
            )
            else -> ColorPickerDialog(
                kigou = breakdownLabels[pf] ?: pf,
                currentHex = ui.violationFamilyColorHex[pf] ?: "",
                defaultHex = when (ShiftAppearance.severityFromVioKey(pf)) {
                    "CRITICAL" -> hardHex
                    "HIGH", "WARN" -> softHex
                    else -> "#8A979B"   // INFO(灰)
                },
                onPick = { hex -> vm.setViolationFamilyColor(pf, hex); pickFam = null },
                onReset = { vm.resetViolationFamilyColor(pf); pickFam = null },
                onClose = { pickFam = null },
            )
        }
    }
}
