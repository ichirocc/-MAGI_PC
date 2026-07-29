package com.magi.app.ui

import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.TextButton
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.PathEffect
import androidx.compose.ui.graphics.drawscope.Stroke
import java.time.LocalDate
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import com.magi.app.v6.V6PortReport
import com.magi.app.v6.V6Algorithm
import com.magi.app.v6.CoverageVerdict
import com.magi.app.v6.MirrorKeys
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Assessment
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.DateRange
import androidx.compose.material.icons.filled.Edit
import androidx.compose.material.icons.filled.Home
import androidx.compose.material.icons.filled.KeyboardArrowDown
import androidx.compose.material.icons.filled.KeyboardArrowUp
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material.icons.filled.Stop
import androidx.compose.material.icons.filled.Warning
import androidx.compose.material3.Icon
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.foundation.gestures.detectHorizontalDragGestures
import androidx.compose.ui.input.pointer.pointerInput

/**
 * CSVのバイト列を文字列へ復号する。妥当な UTF-8 ならそれを採用し、そうでなければ日本の Excel CSV で
 * 一般的な CP932(Shift-JIS) とみなす。先頭の BOM は除去する。これにより Shift-JIS の勤務表CSVが
 * 文字化けせず取り込める（UTF-8 として bytes を読むと壊れていた）。
 */

@Composable
internal fun GuidedFixDialog(ui: UiState, vm: MagiViewModel, onDismiss: () -> Unit, onRerun: () -> Unit) {
    val cs = MaterialTheme.colorScheme
    val shortfalls = ui.coverageDiag?.shortfalls ?: emptyList()
    val target = shortfalls.firstOrNull { it.verdict == CoverageVerdict.FIXABLE && it.miss > 0 }
    val infeasible = shortfalls.filter { it.verdict == CoverageVerdict.INFEASIBLE }
    val allDone = target == null && infeasible.isEmpty()

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(if (allDone) "直し終わりました！" else "なおすのを手伝います") },
        text = {
            Column(
                Modifier.heightIn(max = 380.dp).verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                when {
                    target != null -> {
                        Text("${target.dayLabel} の「${target.shiftSymbol}」が ${target.miss}人 足りません。",
                            fontWeight = FontWeight.Bold)
                        Text("この日に動かせる人がいます。だれかを「${target.shiftSymbol}」に入れますか？",
                            style = MaterialTheme.typography.bodyMedium, color = cs.onSurfaceVariant)
                        val cands = remember(target.dayIndex, target.shiftIndex, ui.coverageDiag) {
                            vm.shortageFixCandidates(target.dayIndex, target.shiftIndex)
                        }
                        if (cands.isEmpty()) {
                            Text("いま動かせる人がいません。別の日を見直すか、データを確認してください。", color = cs.error)
                        } else {
                            cands.take(8).forEach { c ->
                                Button(
                                    onClick = { vm.setCell(c.staffIndex, target.dayIndex, target.shiftIndex) },
                                    modifier = Modifier.fillMaxWidth().heightIn(min = 52.dp).padding(vertical = 2.dp),
                                ) {
                                    val tail = if (c.fromRest) "（休み）" else ""
                                    // 長い氏名でも切れないよう2行まで折り返し（文字欠け防止）。
                                    Text("${c.name}$tail を「${target.shiftSymbol}」に入れる",
                                        textAlign = TextAlign.Center, maxLines = 2, overflow = TextOverflow.Ellipsis)
                                }
                            }
                            Text("入れたら「元に戻す」でいつでも取り消せます。", fontSize = 12.sp, color = cs.onSurfaceVariant)
                        }
                    }
                    infeasible.isNotEmpty() -> {
                        Text("これ以上は自動で埋められません。", fontWeight = FontWeight.Bold)
                        infeasible.take(4).forEach {
                            Text("・${it.dayLabel}「${it.shiftSymbol}」：${it.reason}",
                                style = MaterialTheme.typography.bodySmall, color = cs.onSurfaceVariant)
                        }
                        Text("人を増やすか、担当できるシフトや希望を見直すと直せます。", fontSize = 12.sp, color = cs.onSurfaceVariant)
                    }
                    else -> {
                        Text("人手が足りない日はなくなりました。仕上げにもう一度つくると全体が整います。")
                    }
                }
            }
        },
        confirmButton = {
            if (allDone) DialogConfirmButton("もう一度つくる", onClick = onRerun)
            else DialogDismissButton(onClick = onDismiss, text = "閉じる")
        },
        // [dogfooding] 修正中は「閉じる」だけ（やめる＝同じ動作の重複ボタンを排除）。完了時のみ第2ボタンを出す。
        dismissButton = { if (allDone) DialogDismissButton(onClick = onDismiss, text = "閉じる") },
    )
}

/** [operator_ux §3] 思考誘導カードの1状態分のプラン（文言・色・大ボタン・補助）。 */

internal class OpNextPlan(
    val container: Color, val fg: Color, val headline: String,
    val bigLabel: String, val bigAction: () -> Unit, val bigEnabled: Boolean,
    val helperLabel: String?, val helperAction: () -> Unit,
)

/**
 * [operator_ux §3] 思考誘導ホームの「次にやること」カード。
 * IT中学生レベルのオペレーター向け：専門用語ゼロ・大ボタン1つ・色で意味（緑=できた/黄=もう少し/赤=気をつけて）。
 * いまの状態（未作成／組立中／配れる／もう少し／埋められない）で文言と主ボタンが自動で変わる。
 */

@Composable
internal fun OperatorNextActionCard(
    ui: UiState,
    onMake: () -> Unit,      // 勤務表をつくる（最適化）
    // [3.126.0 撤去] onDraft（下書きをつくる=簡易作成）はユーザー判断で撤去。主導線は「勤務表をつくる」1本。
    onSmartInitial: () -> Unit,  // [新設] 初期解を作る（希望→C1優先の下書き。本最適化は続けない）
    onStop: () -> Unit,      // やめる（停止）
    onExport: () -> Unit,    // 印刷・書き出し / そのまま配る（CSV書き出し）
    onSchedule: () -> Unit,  // 中身を見る（勤務表へ）
    onFix: () -> Unit,       // なおすのを手伝って（勤務表で手直し）
    onSetup: () -> Unit,     // データを見直す（編集へ）
) {
    val cs = MaterialTheme.colorScheme
    val infeasible = ui.coverageDiag?.allInfeasible == true
    val shortDays = ui.coverageDiag?.shortfalls?.map { it.dayIndex }?.distinct()?.size ?: 0
    val worstDay = ui.coverageDiag?.shortfalls?.firstOrNull()?.dayLabel

    // [M3] 成功=tertiary / 注意=error / 主操作=primary はテーマロール。警告のみ独自トークンに集約。
    val (amber, onAmber) = magiWarnColors()

    val plan = when {
        ui.running -> {
            // [校正] 「やめる」は下部コマンドバーに常設済み。カード側の補助ボタンは重複のため出さない。
            // [スクショ指摘/撤去] 見出し文（コンピューターが組んでいます…あと約N分…）は下の進捗行
            //   （残り時間/改善率/回数 = progressSummary）と重複のため出さない（ユーザー赤囲い指示）。
            OpNextPlan(cs.primaryContainer, cs.onPrimaryContainer, "", "", {}, false, null, onStop)
        }
        !ui.hasResult -> OpNextPlan(cs.primaryContainer, cs.onPrimaryContainer,
            "② ボタンひとつで、勤務表を作ります。",
            "勤務表をつくる", onMake, true, "初期解を作る（希望・C1を優先した下書き）", onSmartInitial)
        ui.bestHard == 0L -> OpNextPlan(cs.tertiaryContainer, cs.onTertiaryContainer,
            "③ できました！ そのまま配れます。",
            "印刷・書き出し", onExport, true, "中身を見る", onSchedule)
        infeasible -> OpNextPlan(cs.errorContainer, cs.onErrorContainer,
            "このデータでは、ここは埋められません。" + (worstDay?.let { "（例：$it）" } ?: ""),
            "データを見直す", onSetup, true, "未充足のまま書き出す", onExport)
        else -> OpNextPlan(amber, onAmber,
            // [監査#1] 人手不足ゼロでも必須違反(希望/禁止連続/群)で此処に来る。不足が無いのに
            //   「人手が足りない」と告げる誤診断を排し、実態（必須違反の残数）を言う。
            "もう少しです。" + (worstDay?.let { "$it が人手不足です。" }
                ?: "必須違反が ${ui.bestHard}件 残っています。"),
            "なおすのを手伝って", onFix, true, "もう一度つくる", onMake)
    }

    Card(Modifier.fillMaxWidth(), colors = CardDefaults.cardColors(containerColor = plan.container)) {
        Column(Modifier.padding(20.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
            // [HUD段2] フェーズ名バッジ（探索/完成/狩猟）。既存の状態分岐に名前を与えるだけ。
            //   未最適化→探索 / HARD=0→完成 / HARD>0(infeasible含む)→狩猟。実行中は非表示（カードが別表示）。
            if (!ui.running) {
                val (phName, phColor) = when {
                    !ui.hasResult -> "探索" to MagiAccent.blue
                    ui.bestHard == 0L -> "完成" to MagiAccent.green
                    else -> "狩猟" to MagiAccent.orange
                }
                Box(Modifier.background(phColor, CircleShape).padding(horizontal = 10.dp, vertical = 3.dp)) {
                    // [コントラスト] 白文字は淡い原色(緑/橙)で2.2:1と不足するため WCAG 保証（不足時のみ黒へ）。
                    Text(phName, style = MaterialTheme.typography.labelMedium, color = ensureReadable(phColor, Color.White), fontWeight = FontWeight.Bold)
                }
            }
            if (plan.headline.isNotBlank()) Text(plan.headline, style = MaterialTheme.typography.titleLarge, color = plan.fg, fontWeight = FontWeight.Bold)
            // 数字は必ず言葉つきで意味を添える（operator_ux §6）。
            Text(
                "人手が足りない日：${shortDays}日 ・ できあがり度：${ui.satisfaction}%",
                style = MaterialTheme.typography.bodyMedium, color = plan.fg,
            )
            // [判断設計監査 #1/#2] 数字の根拠（できあがり度の意味）と結果採用の意味（承認ステップの
            //   不在を補う注記: 反映済み・取消可・確定は書き出し時）を1行で明示。
            if (!ui.running && ui.hasResult) {
                Text(
                    "※できあがり度＝最初からの違反の減り具合（必須違反が残る間は最大55%）。" +
                        "結果は下書きに反映済み・「元に戻す」で取消可・確定は書き出し時です。",
                    style = MaterialTheme.typography.bodySmall, color = plan.fg.copy(alpha = 0.8f),
                )
            }
            if (ui.running) {
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                    CircularProgressIndicator(Modifier.size(22.dp), color = plan.fg)
                    // [進捗の見える化] 「組み立て中…」だけでなく 改善率/残り時間/探索数 を出す。
                    Text(progressSummary(ui), color = plan.fg, style = MaterialTheme.typography.bodyMedium, modifier = Modifier.weight(1f))
                }
            }
            if (plan.bigEnabled) {
                Button(onClick = plan.bigAction, modifier = Modifier.fillMaxWidth().heightIn(min = 56.dp)) {
                    Text(plan.bigLabel, style = MaterialTheme.typography.titleMedium)
                }
            }
            plan.helperLabel?.let { hl ->
                // [校正] 補助操作もテキストリンクではなく外枠ボタンに（カード地色でも見えるよう枠色=前景色）。
                OutlinedButton(
                    onClick = plan.helperAction,
                    modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp),
                    colors = ButtonDefaults.outlinedButtonColors(contentColor = plan.fg),
                    border = BorderStroke(1.dp, plan.fg.copy(alpha = 0.5f)),
                ) { Text(hl) }
            }
            // [3.261.0, ユーザー指摘「初期解生成が何度も出来ない」] !ui.hasResult状態の補助ボタンは
            // hasResult=true化後（=初期解生成の完了直後を含む全ての完成/狩猟/未充足状態）に消えるため、
            // 一度使うと二度と初期解生成へ戻れなかった。小さな常設リンクとして独立させ、実行中以外は
            // 常に再生成できるようにする（元に戻すで取消可能・破壊的でない）。
            if (!ui.running && ui.hasResult) {
                TextButton(onClick = onSmartInitial, modifier = Modifier.fillMaxWidth()) {
                    Text("初期解を作り直す（希望・C1優先の下書きに戻す）")
                }
            }
        }
    }
}

/** [対象月の選択] 勤務表を作る月を前月/翌月/今月で選ぶ。変更でその月の日数に合わせて表を作り直す。 */

@Composable
internal fun CopilotCard(ui: UiState, onGoEdit: () -> Unit, onSoftPolish: () -> Unit = {}) {
    // [冗長性削減] できあがり度・進捗は OperatorNextActionCard が表示するため、ここは助言/警告だけに専念。
    val cs = MaterialTheme.colorScheme
    val show = ui.impossibleWishCount > 0 || ui.copilotHint != null || (ui.polishExhausted && !ui.running)
    if (!show) return
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            // 担当外など実現不能な希望の警告（Web版の担当外希望警告に相当）
            if (ui.impossibleWishCount > 0) {
                Surface(color = cs.errorContainer, shape = MaterialTheme.shapes.medium) {
                    Column(Modifier.fillMaxWidth().padding(12.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                        Text("⚠ 実現できない希望が ${ui.impossibleWishCount} 件（担当外シフトなど）。配布前に見直しを。",
                            color = cs.onErrorContainer, style = MaterialTheme.typography.bodyMedium)
                        OutlinedButton(onClick = onGoEdit, modifier = Modifier.heightIn(min = 48.dp)) { Text("希望シフトを編集") }
                    }
                }
            }
            // ガチャ操作の助言＋修正導線（NextActionBar相当）
            ui.copilotHint?.let {
                Surface(color = cs.secondaryContainer, shape = MaterialTheme.shapes.medium) {
                    Column(Modifier.fillMaxWidth().padding(12.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                        Text("💡 $it", color = cs.onSecondaryContainer, style = MaterialTheme.typography.bodyMedium)
                        OutlinedButton(onClick = onGoEdit, modifier = Modifier.heightIn(min = 48.dp)) { Text("編集タブで見直す") }
                    }
                }
            }
            if (ui.polishExhausted && !ui.running) {
                Surface(color = cs.tertiaryContainer, shape = MaterialTheme.shapes.medium) {
                    Column(Modifier.fillMaxWidth().padding(12.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        Text("✓ 必須は満たしています。残りの調整は自動で減らせます（必須は壊しません）。",
                            color = cs.onTertiaryContainer, style = MaterialTheme.typography.bodyMedium)
                        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            Button(onClick = onSoftPolish, modifier = Modifier.weight(1f).heightIn(min = 48.dp)) { Text("自動で整える") }
                            OutlinedButton(onClick = onGoEdit, modifier = Modifier.weight(1f).heightIn(min = 48.dp)) { Text("手修正") }
                        }
                    }
                }
            }
        }
    }
}

/**
 * 人員不足(covU)が残るときだけ表示する原因診断カード。
 * 各不足枠を「充足不可（データ上どう割り当てても埋まらない）」か
 * 「充足可能（枠は足りる＝最適化が未到達）」に切り分けて、配布前の判断材料にする。
 */

@Composable
internal fun CoverageDiagnosisCard(ui: UiState) {
    val diag = ui.coverageDiag ?: return
    if (!diag.hasShortage && !diag.hasSurplus) return
    val cs = MaterialTheme.colorScheme
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            if (diag.hasShortage) {
                Text("人員不足の原因", style = MaterialTheme.typography.titleMedium)
                val headline = when {
                    diag.allInfeasible -> "不足 ${diag.totalShortfall} 人は全て充足不可。今のデータでは満たせません（想定内）。"
                    diag.infeasibleSlots == 0 -> "不足 ${diag.totalShortfall} 人は枠が足りています。再実行や設定の見直しで解消し得ます。"
                    else -> "不足 ${diag.totalShortfall} 人 — 充足不可 ${diag.infeasibleSlots} 枠 / 充足可能 ${diag.fixableSlots} 枠。"
                }
                Text(headline, style = MaterialTheme.typography.bodyMedium, color = cs.onSurfaceVariant)
                for (s in diag.shortfalls.take(6)) {
                    val infeasible = s.verdict == CoverageVerdict.INFEASIBLE
                    val container = if (infeasible) cs.errorContainer else cs.secondaryContainer
                    val onContainer = if (infeasible) cs.onErrorContainer else cs.onSecondaryContainer
                    Surface(color = container, shape = MaterialTheme.shapes.medium) {
                        Column(Modifier.fillMaxWidth().padding(12.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                Text("${s.dayLabel}  ${s.shiftSymbol}  必要${s.need}/現状${s.got}（不足${s.miss}）",
                                    color = onContainer, style = MaterialTheme.typography.titleSmall, modifier = Modifier.weight(1f))
                                MagiTagChip(
                                    text = if (infeasible) "充足不可" else "充足可能",
                                    color = if (infeasible) MagiAccent.red else MagiAccent.blue,
                                )
                            }
                            Text(s.reason, color = onContainer, style = MaterialTheme.typography.bodySmall)
                        }
                    }
                }
                if (diag.shortfalls.size > 6) {
                    Text("ほか ${diag.shortfalls.size - 6} 枠（詳細はログ出力を参照）",
                        style = MaterialTheme.typography.bodySmall, color = cs.onSurfaceVariant)
                }
                if (diag.relaxations.isNotEmpty()) {
                    Surface(color = cs.tertiaryContainer, shape = MaterialTheme.shapes.medium) {
                        Column(Modifier.fillMaxWidth().padding(12.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                            Text("解けるようにするには（担当追加の案）", color = cs.onTertiaryContainer, style = MaterialTheme.typography.titleSmall)
                            for (r in diag.relaxations.take(4)) {
                                Text("・$r", color = cs.onTertiaryContainer, style = MaterialTheme.typography.bodySmall)
                            }
                            Text("※ 担当追加の提案です。設定変更は行いません（採否はご判断ください）。",
                                color = cs.onTertiaryContainer.copy(alpha = 0.8f), style = MaterialTheme.typography.labelSmall)
                        }
                    }
                }
            }
            if (diag.hasSurplus) {
                Text("人員過剰がなぜ減らないか", style = MaterialTheme.typography.titleMedium)
                Text("過剰 ${diag.totalSurplus} 人 — 在勤者を他シフトへ動かせば消えるはずが、動かない理由を枠ごとに示します。",
                    style = MaterialTheme.typography.bodyMedium, color = cs.onSurfaceVariant)
                for (s in diag.surpluses.take(6)) {
                    Surface(color = cs.secondaryContainer, shape = MaterialTheme.shapes.medium) {
                        Column(Modifier.fillMaxWidth().padding(12.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                            Text("${s.dayLabel}  ${s.shiftSymbol}  必要${s.need}/現状${s.got}（過剰${s.excess}）",
                                color = cs.onSecondaryContainer, style = MaterialTheme.typography.titleSmall)
                            Text(s.reason, color = cs.onSecondaryContainer, style = MaterialTheme.typography.bodySmall)
                        }
                    }
                }
                if (diag.surpluses.size > 6) {
                    Text("ほか ${diag.surpluses.size - 6} 枠（詳細はログ出力を参照）",
                        style = MaterialTheme.typography.bodySmall, color = cs.onSurfaceVariant)
                }
            }
        }
    }
}


/**
 * [3.280.0] 禁止連続(c3n)の「なぜ崩せないか」診断カード。CoverageDiagnosisCard（人員不足/過剰の原因）の
 * c3n 版＝同じ作り。必須違反が残っているのに探索が進まないとき、「構造的に不能（希望調整が必要）」か
 * 「多段手が必要（再実行で解消し得る）」かを違反 run ごとに示す。読取専用・スコア不変。
 */
@Composable
internal fun ForbiddenRunDiagnosisCard(ui: UiState, onRelaxRule: (String) -> Unit = {}) {
    val diag = ui.forbiddenDiag ?: return
    if (!diag.hasRuns) return
    val cs = MaterialTheme.colorScheme
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            Text("禁止の並びがなぜ崩せないか", style = MaterialTheme.typography.titleMedium)
            val headline = if (diag.allBlocked) {
                "残り ${diag.totalRuns} 件は全て塞がっています。今の希望・担当のままでは崩せません。"
            } else {
                "残り ${diag.totalRuns} 件 — 崩す手が残っている並びがあります。"
            }
            Text(headline, style = MaterialTheme.typography.bodyMedium, color = cs.onSurfaceVariant)
            for (r in diag.runs.take(6)) {
                val blocked = !r.escapable
                val container = if (blocked) cs.errorContainer else cs.secondaryContainer
                val onContainer = if (blocked) cs.onErrorContainer else cs.onSecondaryContainer
                Surface(color = container, shape = MaterialTheme.shapes.medium) {
                    Column(Modifier.fillMaxWidth().padding(12.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            Text("${r.staffName}  ${r.seqLabel}",
                                color = onContainer, style = MaterialTheme.typography.titleSmall, modifier = Modifier.weight(1f))
                            MagiTagChip(
                                text = if (blocked) "崩せない" else "崩す手あり",
                                color = if (blocked) MagiAccent.red else MagiAccent.blue,
                            )
                        }
                        val cellsTxt = r.cells.joinToString(" ・ ") { c ->
                            val tag = when (c.escape) {
                                com.magi.app.v6.ForbiddenCellEscape.FREE -> "崩せる"
                                com.magi.app.v6.ForbiddenCellEscape.CHAIN -> "玉突きで崩せる"
                                com.magi.app.v6.ForbiddenCellEscape.ADJACENT -> "隣接日調整で崩せる"
                                com.magi.app.v6.ForbiddenCellEscape.PINNED -> "希望固定"
                                com.magi.app.v6.ForbiddenCellEscape.BLOCKED -> "塞がり"
                            }
                            "${c.dayLabel} ${c.shiftSymbol}=$tag"
                        }
                        Text(cellsTxt, color = onContainer, style = MaterialTheme.typography.bodySmall)
                        Text(r.hint, color = onContainer, style = MaterialTheme.typography.bodySmall)
                    }
                }
            }
            if (diag.runs.size > 6) {
                Text("ほか ${diag.runs.size - 6} 件（詳細はログ出力を参照）",
                    style = MaterialTheme.typography.bodySmall, color = cs.onSurfaceVariant)
            }

            // [壁の名指しと緩和] 「崩せない」判定の run を並び（ルール）ごとに集約し、そのルールを
            //   その場で削除できるようにする。制約画面へ行って行を探す往復を省く導線。
            //   崩す手が残っている並びは探索で解けうるので出さない（先にルールを消させない）。
            val walls = diag.runs.filter { !it.escapable }
                .groupBy { it.seqLabel }
                .toList()
                .sortedByDescending { it.second.size }
            if (walls.isNotEmpty()) {
                HorizontalDivider()
                Text("崩せない原因の並び", style = MaterialTheme.typography.titleSmall)
                Text("この並びを禁止しているかぎり、必須違反は残ります。ルールを外すとその場で再チェックします（元に戻せます）。",
                    style = MaterialTheme.typography.bodySmall, color = cs.onSurfaceVariant)
                for ((seqLabel, rows) in walls) {
                    val who = rows.map { it.staffName }.distinct()
                    val whoTxt = who.take(3).joinToString("・") + if (who.size > 3) " ほか${who.size - 3}名" else ""
                    Row(
                        Modifier.fillMaxWidth(),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(8.dp),
                    ) {
                        Column(Modifier.weight(1f)) {
                            Text(seqLabel, style = MaterialTheme.typography.titleSmall)
                            Text("${rows.size}件（$whoTxt）", style = MaterialTheme.typography.bodySmall, color = cs.onSurfaceVariant)
                        }
                        TextButton(onClick = { onRelaxRule(seqLabel) }, enabled = !ui.running) {
                            Text("この並びの禁止をやめる")
                        }
                    }
                }
            }
        }
    }
}


/**
 * [窓の要件が直せなかった理由 / 3.322.0] 直近の最適化で c1 研磨が候補を却下した記録を、
 * 職員×シフトごとに理由つきで見せる。CoverageDiag（人員不足）・ForbiddenDiag（禁止連続）に続く3枚目。
 *
 * この2枚と違い**盤面から再計算できない**（根拠が「研磨が実際に候補を作って却下した」観測のため）。
 * したがって「構造的に不能」とは言わない — 言えるのは「いまの設定で、試した手はすべて却下された」まで。
 * 回数固定を緩めれば通る可能性が残る、という含みを文言で明示する。
 */
@Composable
internal fun C1PlateauCard(ui: UiState, onGoEdit: () -> Unit = {}) {
    val diag = ui.c1Plateau ?: return
    if (!diag.hasEntries) return
    // [3.323.0] 回数固定だけが止めた手の数。この手は目的関数が採用を認めていて、固定のガードだけが
    //   却下している＝「固定を緩めれば通ったはずの手」の実測値。0 なら緩めても何も変わらない。
    val pinBlocked = ui.pinBlocked
    val cs = MaterialTheme.colorScheme
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            Text("窓の要件がなぜ直せなかったか", style = MaterialTheme.typography.titleMedium)
            val pinTxt = if (diag.pinConstrained > 0) "うち ${diag.pinConstrained} 件は回数の固定が壁になっています。" else ""
            Text("残り ${diag.remainingC1} 件。$pinTxt 前回つくったときに試した直し方の記録です。",
                style = MaterialTheme.typography.bodyMedium, color = cs.onSurfaceVariant)
            for (e in diag.entries.take(6)) {
                val pin = e.cause == com.magi.app.v6.C1PlateauCause.PIN_CONSTRAINED
                val container = if (pin) cs.errorContainer else cs.secondaryContainer
                val onContainer = if (pin) cs.onErrorContainer else cs.onSecondaryContainer
                Surface(color = container, shape = MaterialTheme.shapes.medium) {
                    Column(Modifier.fillMaxWidth().padding(12.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            Text(e.label, color = onContainer,
                                style = MaterialTheme.typography.titleSmall, modifier = Modifier.weight(1f))
                            MagiTagChip(
                                text = when (e.cause) {
                                    com.magi.app.v6.C1PlateauCause.PIN_CONSTRAINED -> "回数固定が壁"
                                    com.magi.app.v6.C1PlateauCause.SCORE_TRADEOFF -> "他とのトレードオフ"
                                    com.magi.app.v6.C1PlateauCause.NO_CANDIDATE -> "候補なし"
                                },
                                color = if (pin) MagiAccent.red else MagiAccent.blue,
                            )
                        }
                        // 根拠の内訳。「試した手が何件あって、何で落ちたか」を数で示す（推測でなく観測）。
                        val parts = ArrayList<String>()
                        if (e.rejectedByPin > 0) parts.add("回数固定で却下 ${e.rejectedByPin}件")
                        if (e.rejectedByScore > 0) parts.add("総合評価で却下 ${e.rejectedByScore}件")
                        if (e.noCandidate > 0) parts.add("候補なし ${e.noCandidate}件")
                        Text(parts.joinToString(" ・ "), color = onContainer, style = MaterialTheme.typography.bodySmall)
                        Text(e.recommendedAction { fam -> breakdownLabels[fam] ?: fam },
                            color = onContainer, style = MaterialTheme.typography.bodySmall)
                    }
                }
            }
            if (diag.entries.size > 6) {
                Text("ほか ${diag.entries.size - 6} 件（詳細はログ出力を参照）",
                    style = MaterialTheme.typography.bodySmall, color = cs.onSurfaceVariant)
            }
            if (pinBlocked > 0) {
                HorizontalDivider()
                Text("回数の固定について", style = MaterialTheme.typography.titleSmall)
                Text("今回の計算では、$pinBlocked 件の直し方が「回数を固定している」という理由だけで見送られました。" +
                    "これらは他の条件では採用できる手でした。固定（下限＝上限）を1回ぶん緩めると通る可能性があります。",
                    style = MaterialTheme.typography.bodySmall, color = cs.onSurfaceVariant)
                TextButton(onClick = onGoEdit, enabled = !ui.running) { Text("個人の回数を見直す") }
            } else if (diag.pinConstrained > 0) {
                TextButton(onClick = onGoEdit, enabled = !ui.running) { Text("個人の回数を見直す") }
            }
        }
    }
}


/**
 * [設定ミスの誘導修正] 制約・希望シフトの入力間違いを「どこが・なぜ・どう直すか」で具体的に提示する。
 * CoverageDiagnosisCard（人員不足の原因）と同じ作りで、配布前に設定を直せるようにするのが目的。
 */
@Composable
internal fun SettingIssuesCard(ui: UiState, onFix: (com.magi.app.v6.SettingIssue) -> Unit, onGoEdit: () -> Unit) {
    val issues = ui.settingIssues
    if (issues.isEmpty()) return
    val cs = MaterialTheme.colorScheme
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            Text("設定の見直し（${issues.size}件）", style = MaterialTheme.typography.titleMedium)
            // [冗長性見直し] 見出し「設定の見直し（N件）」＋各行の具体表示と重複するため説明文は削除。
            for (s in issues.take(6)) {
                val label: String
                val tagColor: androidx.compose.ui.graphics.Color
                when (s.kind) {
                    com.magi.app.v6.IssueKind.WISH -> { label = "希望"; tagColor = MagiAccent.blue }
                    com.magi.app.v6.IssueKind.CONSTRAINT -> { label = "制約"; tagColor = MagiAccent.red }
                    com.magi.app.v6.IssueKind.DEMAND -> { label = "必要人数"; tagColor = MagiAccent.red }
                    com.magi.app.v6.IssueKind.RANGE -> { label = "回数"; tagColor = MagiAccent.orange }
                }
                Surface(color = cs.errorContainer, shape = MaterialTheme.shapes.medium) {
                    Column(Modifier.fillMaxWidth().padding(12.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            MagiTagChip(text = label, color = tagColor)
                            Text(s.where, color = cs.onErrorContainer, style = MaterialTheme.typography.titleSmall, modifier = Modifier.weight(1f))
                        }
                        Text(s.problem, color = cs.onErrorContainer, style = MaterialTheme.typography.bodySmall)
                        Text("→ ${s.fix}", color = cs.onErrorContainer, style = MaterialTheme.typography.bodyMedium)
                        if (s.actionLabel.isNotEmpty()) {
                            Button(onClick = { onFix(s) }, modifier = Modifier.align(Alignment.End).heightIn(min = 48.dp)) {
                                Text(s.actionLabel)
                            }
                        }
                    }
                }
            }
            if (issues.size > 6) {
                // [誘導] 重要な順に整列済み。届かない「ログ出力」ではなく、上から直せば解消する旨を案内。
                Text("ほか ${issues.size - 6} 件（重要な順に表示中。まず上から直してください）", style = MaterialTheme.typography.bodySmall, color = cs.onSurfaceVariant)
            }
            OutlinedButton(onClick = onGoEdit, modifier = Modifier.heightIn(min = 48.dp)) { Text("設定・希望を編集する") }
        }
    }
}

@Composable
internal fun V6DashboardCard(v6: V6PortReport?) {
    if (v6 == null) return
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp)) {
            Text("V6 1ヶ月俯瞰", fontWeight = FontWeight.Bold)
            Text(
                "人員の穴・負荷の偏り・入力ミスを勤務表から直接集計します。",
                fontSize = 12.sp,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            Spacer(Modifier.height(14.dp))
            // §5.4 上部: 充足率(coverage%)を大数値ゲージで（必要人数のうち満たせた割合）
            v6.coveragePct?.let { pct ->
                val tint = if (pct >= 100) MaterialTheme.colorScheme.tertiary else if (pct >= 90) MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.error
                MagiScoreGauge(
                    score = pct,
                    max = 100,
                    label = "人員充足率",
                    sub = "必要人数 ${v6.demand} のうち満たせた割合",
                    accent = tint,
                )
                // [D3-full案A] 「できあがり度(全体の完成度)」と「人員充足率(人員の一側面)」は別指標。
                //   役割の違いを明示し、片方を他方の内訳と誤認させない(架空分解を避ける)。
                Text(
                    "※全体の完成度は「できあがり度」（ホーム）で確認。ここは人員の充足のみ。",
                    style = MaterialTheme.typography.labelMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                Spacer(Modifier.height(14.dp))
            }
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp), modifier = Modifier.fillMaxWidth()) {
                BigStat("HARD Core", v6.hardCore.toString(), Modifier.weight(1f))
                BigStat("Guard", v6.hardGuard.toString(), Modifier.weight(1f))
                BigStat("充足", v6.coveragePct?.let { "$it%" } ?: "-", Modifier.weight(1f))
            }
            Spacer(Modifier.height(8.dp))
            Text(
                if (v6.topRiskShortage > 0) "最優先: ${v6.topRiskLabel} に不足 ${v6.topRiskShortage} 枠" else "最優先: 人員不足なし",
                color = if (v6.topRiskShortage > 0) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.primary,
                fontWeight = FontWeight.SemiBold,
            )
            // [3.286.0 冗長性C] 日別リスクチップ列（dayRisks）は AttentionCardsSection（日別リスト＝全件＋
            //   要確認のみトグル＋タップ修復）が上位互換のため撤去（3.195.0 で保留した次点候補の実施）。
            Spacer(Modifier.height(10.dp))
            Text(
                "Apt=${"%.2f".format(v6.aptPenalty)} / Equalize=${"%.2f".format(v6.equPenalty)} / Demand=${v6.demand} / covU=${v6.covU}",
                fontFamily = FontFamily.Monospace,
                fontSize = 12.sp,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            if (v6.sanityWarnings.isNotEmpty()) {
                Spacer(Modifier.height(8.dp))
                v6.sanityWarnings.take(3).forEach {
                    Text("⚠ $it", color = MaterialTheme.colorScheme.error, fontSize = 12.sp)
                }
            }
            // [3.286.0 冗長性C] 負荷プロフィール（staffProfiles top5）は AttentionCardsSection（人別リスト）が
            //   上位互換のため撤去。固有の生指標（充足率/HARD Core/Guard/Apt/Equalize/sanity警告）のみ残す。
        }
    }
}


/**
 * [N2/⛏11] 重み表カード。weightedScore の内部重み（MirrorKeys.weights）をそのまま描画し、
 * 「できあがり度/スコア」の根拠（どの違反が何倍効くか）を上級者が逆算できるようにする。
 * 重みは最適化器と同じマップを参照＝表示と最適化器が常に一致（統一思想の延長）。プロ表示時のみ。
 */
@Composable
internal fun WeightTableCard() {
    fun fmt(w: Double): String = if (w == w.toLong().toDouble()) w.toLong().toString() else w.toString()
    val sorted = MirrorKeys.weights.entries.sortedByDescending { it.value }
    val hard = sorted.filter { it.value >= 1000.0 }
    val soft = sorted.filter { it.value < 1000.0 }
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            Text("重み表（最適化器と一致）", fontWeight = FontWeight.Bold)
            Text("スコアの内部重み。大きいほど優先して直されます。", style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
            Text("絶対に守る（HARD）", style = MaterialTheme.typography.labelMedium, fontWeight = FontWeight.Bold)
            hard.forEach { (k, w) ->
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                    Text(breakdownLabels[k] ?: k, modifier = Modifier.weight(1f))
                    Text("×${fmt(w)}", fontFamily = FontFamily.Monospace, color = MaterialTheme.colorScheme.error)
                }
            }
            Text("できれば守る（SOFT）", style = MaterialTheme.typography.labelMedium, fontWeight = FontWeight.Bold)
            soft.forEach { (k, w) ->
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                    Text(breakdownLabels[k] ?: k, modifier = Modifier.weight(1f))
                    Text("×${fmt(w)}", fontFamily = FontFamily.Monospace)
                }
            }
        }
    }
}


// [3.286.0 冗長性C] RiskChip は dayRisks チップ列の撤去で呼出0となったため削除。


/** 内訳の家族キー → 日本語ラベル（BreakdownCard と FixSuggestionCard で共用）。 */
internal val breakdownLabels: Map<String, String> = mapOf(
    "groupViol" to "担当外シフト", "pref" to "希望違反", "covU" to "人員不足", "c3n" to "禁止の並び",
    "low" to "下限割れ", "high" to "上限超過", "apt" to "適切回数のズレ", "fair" to "公平化のズレ", "weekly" to "曜日の偏り",
    "c1" to "窓の要件", "c2" to "個人の合計", "c3" to "必須の並び", "c3m" to "推奨の並び",
    "c3mn" to "回避の並び", "c41" to "群のレンジ", "c42" to "群ペア",
    // [④用語統一] covO は covU「人員不足」/集計凡例「人員過剰」と対にする（旧「過剰な配置」は同じ違反の別名で紛らわしい）。
    "c41s" to "スキル群のレンジ", "c42s" to "スキル群ペア", "covO" to "人員過剰",
)

/**
 * [分析→場所] 内訳の家族キー(low/covO/c42 等)から、その違反の「場所」と関係スタッフindexを返す。
 *  - count系(low/high/c2)   : スタッフ名「シフト」 / staff=i        (countViolations: i,k)
 *  - 被覆系(covU/covO/c41/c41s): 日付「シフト」 / staff=null         (needViolations: k,j)
 *  - セル系(c1/c3/c3n/c3m/c3mn/c42/c42s/pref/groupViol): スタッフ名 日付=実シフト / staff=i (violationCells: i,j)
 * staff!=null の項目はタップで「そのスタッフが関わる交換」を探せる。
 */
internal fun breakdownLocations(famKey: String, ui: UiState): List<Pair<String, Int?>> {
    fun nm(i: Int) = ui.staffNames.getOrNull(i) ?: "#$i"
    fun sym(k: Int) = ui.shiftSymbols.getOrNull(k) ?: "$k"
    val want = "vio-$famKey"
    return when (famKey) {
        "low", "high", "c2" -> ui.countViolations.entries.filter { it.value == want }.mapNotNull {
            val p = it.key.split(","); val i = p.getOrNull(0)?.toIntOrNull(); val k = p.getOrNull(1)?.toIntOrNull()
            if (i == null || k == null) null else ("${nm(i)} 「${sym(k)}」" to i)
        }
        // 適切回数(apt) は不足=vio-aptLow / 超過=vio-aptHigh の2クラスで countViolations(i,k) に入る。
        "apt" -> ui.countViolations.entries.filter { it.value == "vio-aptLow" || it.value == "vio-aptHigh" }.mapNotNull {
            val p = it.key.split(","); val i = p.getOrNull(0)?.toIntOrNull(); val k = p.getOrNull(1)?.toIntOrNull()
            if (i == null || k == null) null else ("${nm(i)} 「${sym(k)}」" to i)
        }
        "covU", "covO", "c41", "c41s" -> ui.needViolations.entries.filter { it.value == want }.mapNotNull {
            val p = it.key.split(","); val k = p.getOrNull(0)?.toIntOrNull(); val j = p.getOrNull(1)?.toIntOrNull()
            if (k == null || j == null) null else ("${dayMD(ui.startDate, j)} 「${sym(k)}」" to null)
        }
        // [場所表示] fair/weekly はセル単位でなく職員(weekly)・職員×シフト(fair)単位の偏り。distLocations から整形。
        "weekly" -> (ui.distLocations["weekly"] ?: emptyList()).mapNotNull { e ->
            val i = e.getOrNull(0) ?: return@mapNotNull null; val dev = e.getOrNull(1) ?: 0
            "${nm(i)}（曜日の偏り ${dev}）" to i
        }
        "fair" -> (ui.distLocations["fair"] ?: emptyList()).mapNotNull { e ->
            val i = e.getOrNull(0) ?: return@mapNotNull null; val k = e.getOrNull(1) ?: return@mapNotNull null; val dev = e.getOrNull(2) ?: 0
            "${nm(i)} 「${sym(k)}」（偏り ${dev}）" to i
        }
        else -> ui.violationCells.entries.filter { it.value == want }.mapNotNull {
            val p = it.key.split(","); val i = p.getOrNull(0)?.toIntOrNull(); val j = p.getOrNull(1)?.toIntOrNull()
            if (i == null || j == null) null else {
                val cell = ui.schedule.getOrNull(i)?.getOrNull(j) ?: -1
                ("${nm(i)} ${dayMD(ui.startDate, j)}=${if (cell >= 0) sym(cell) else "—"}" to i)
            }
        }
    }
}

// [死にコード整理] BottleneckCard は 3.81.0 で AttentionCardsSection(全件＋トグル＋タップ修復)が上位互換となり
//   詳細タブから撤去済み。呼出0の composable 定義もここで撤去した（履歴は git にある）。

// [3.112.0 撤去] HeroMetricsRow（対象人数/対象期間の2タイル）はユーザー赤囲い指示で撤去。
//   読込ステータス行「読込完了: N名/N日/Nシフト」と重複する固定値で、トリアージに寄与しないため。

/** [★1/E1] 要確認一覧の1項目。個々の違反箇所を重大度マーク付きで表す（web「画面修正版」confirm ビュー移植）。 */
private data class ConfirmItem(
    val kind: Int,          // 0=不足/必須(bad) 1=過剰/調整(warn) 2=窓(c1)
    val mark: String,       // バッジ内グリフ（2文字以内）
    val title: String,      // 場所（職員名 / 日「シフト」）
    val sub: String,        // 族ラベル（人員不足・下限割れ 等）
    val staff: Int?,        // タップ修復のフォーカス職員（null=全体探索）
    val day: Int?,          // [ジャンプ] セル違反の日index（null=セルに紐付かない項目）
    val order: Int,         // 並び（kind→場所）
    // [下流→上流ディープリンク] pref違反→希望シフト登録(該当職員) / covU・covO→必要人数カレンダー(該当シフト)。
    //   null=そのカレンダーへの「設定で直す」導線を出さない。
    val wishStaff: Int? = null,
    val needShift: Int? = null,
)

/**
 * [★1/E1] 各違反マップ（needViolations/countViolations/violationCells）を個々の「要確認」項目に展開。
 * BreakdownCard が族単位の集計なのに対し、こちらは spec「画面修正版」confirm ビューと同型の箇所単位リスト。
 * 表示のみ・スコアリング不変（読取専用）。
 */
private fun confirmItems(ui: UiState): List<ConfirmItem> {
    fun nm(i: Int) = ui.staffNames.getOrNull(i) ?: "#$i"
    fun sym(k: Int) = ui.shiftSymbols.getOrNull(k) ?: "$k"
    val out = ArrayList<ConfirmItem>()
    // 被覆・群レンジ: needViolations "k,j" -> vio-covU/covO/c41/c41s（日×シフト）
    for ((key, cls) in ui.needViolations) {
        val p = key.split(","); val k = p.getOrNull(0)?.toIntOrNull() ?: continue; val j = p.getOrNull(1)?.toIntOrNull() ?: continue
        val fam = cls.removePrefix("vio-")
        // [④用語統一] 過剰マークは「過剰」（凡例/集計と同語）。[⑥日別ジャンプ] day=j で勤務表の該当日列へ飛べる。
        val (mark, kind) = when (fam) { "covU" -> "不足" to 0; "covO" -> "過剰" to 1; else -> "調整" to 1 }
        out += ConfirmItem(kind, mark, "${dayMD(ui.startDate, j)}「${sym(k)}」", breakdownLabels[fam] ?: fam, null, j, kind * 100000 + j * 100 + k,
            needShift = if (fam == "covU" || fam == "covO") k else null)
    }
    // 個人回数: countViolations "i,k" -> vio-low/high/c2/aptLow/aptHigh（職員×シフト）
    for ((key, cls) in ui.countViolations) {
        val p = key.split(","); val i = p.getOrNull(0)?.toIntOrNull() ?: continue; val k = p.getOrNull(1)?.toIntOrNull() ?: continue
        val fam = cls.removePrefix("vio-")
        // c2(個人の合計) は方向を持たない単一クラス vio-c2 → 「過」ではなく中立マーク「計」（誤って過剰と表示しない）。
        val (mark, kind) = when (fam) { "low", "aptLow" -> "不足" to 0; "c2" -> "計" to 1; else -> "過剰" to 1 }
        val labelFam = when (fam) { "aptLow", "aptHigh" -> "apt"; else -> fam }
        out += ConfirmItem(kind, mark, nm(i), "${sym(k)}・${breakdownLabels[labelFam] ?: labelFam}", i, null, kind * 100000 + 40000 + i * 100 + k)
    }
    // セル違反: violationCells "i,j" -> vio-pref/groupViol/c3n/c3/c3m/c3mn/c1/c42/c42s（職員×日=実シフト）
    for ((key, cls) in ui.violationCells) {
        val p = key.split(","); val i = p.getOrNull(0)?.toIntOrNull() ?: continue; val j = p.getOrNull(1)?.toIntOrNull() ?: continue
        val fam = cls.removePrefix("vio-")
        val cell = ui.schedule.getOrNull(i)?.getOrNull(j) ?: -1
        val cellSym = if (cell >= 0) sym(cell) else "—"
        val (mark, kind) = when (fam) {
            "c1" -> "窓" to 2
            "pref", "groupViol", "c3n" -> "必須" to 0
            else -> "調整" to 1
        }
        // [Set化] 同セルに重なった族は sub に全列挙（重み降順）。行数=箇所数は不変（見出し件数の意味を保つ）。
        val famsAll = (ui.violationCellFamilies[key] ?: listOf(cls)).map { it.removePrefix("vio-") }
        val sub = famsAll.joinToString("・") { breakdownLabels[it] ?: it }
        out += ConfirmItem(kind, mark, "${nm(i)} ${dayMD(ui.startDate, j)}=$cellSym", sub, i, j, kind * 100000 + 80000 + j * 100 + i,
            wishStaff = if ("pref" in famsAll) i else null)
    }
    return out.sortedWith(compareBy({ it.kind }, { it.order }))
}

/** [★1] 要確認一覧のフィルタチップ（Surface ベース＝新規 import 不要）。 */
@Composable
private fun ConfirmFilterChip(label: String, count: Int, selected: Boolean, onClick: () -> Unit) {
    val cs = MaterialTheme.colorScheme
    val bg = if (selected) cs.secondaryContainer else cs.surfaceVariant
    val fg = if (selected) cs.onSecondaryContainer else cs.onSurfaceVariant
    Surface(
        color = bg, shape = MaterialTheme.shapes.small,
        modifier = Modifier.heightIn(min = 40.dp).clickable { onClick() },
    ) {
        Row(Modifier.padding(horizontal = 12.dp, vertical = 8.dp), verticalAlignment = Alignment.CenterVertically) {
            Text("$label $count", style = MaterialTheme.typography.labelLarge, color = fg,
                fontWeight = if (selected) FontWeight.Bold else FontWeight.Normal)
        }
    }
}

/** [★1] 要確認一覧の1行（spec confirmCard 同型：マークバッジ＋タイトル＋族＋メタ）。 */
@Composable
private fun ConfirmRow(
    item: ConfirmItem, onFocusStaff: (Int) -> Unit, onShowCell: (Int, Int) -> Unit, onShowDay: (Int) -> Unit,
    onFixWish: (Int) -> Unit = {}, onFixNeed: (Int) -> Unit = {},
) {
    val cs = MaterialTheme.colorScheme
    val (warnBg, warnFg) = magiWarnColors()
    val (bg, fg) = when (item.kind) {
        0 -> cs.errorContainer to cs.onErrorContainer
        1 -> warnBg to warnFg
        else -> cs.tertiaryContainer to cs.onTertiaryContainer
    }
    // [⑥日別ジャンプ] staff無し・day有り（人員/群レンジ=日×シフト）は勤務表の該当日列へ移動できる。
    val dayOnly = item.staff == null && item.day != null
    val clickable = item.staff != null || dayOnly
    var m = Modifier.fillMaxWidth().heightIn(min = 56.dp)
    if (clickable) m = m.clickable {
        val s = item.staff; val d = item.day
        when { s != null && d != null -> onShowCell(s, d); s != null -> onFocusStaff(s); d != null -> onShowDay(d) }
    }.semantics { contentDescription = "${item.title} ${item.sub}・" + (if (dayOnly) "タップで勤務表の該当日へ" else "タップで直し方を探す") }
    Surface(color = cs.surfaceVariant, shape = MaterialTheme.shapes.medium, modifier = m) {
        Row(Modifier.padding(10.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Surface(color = bg, shape = MaterialTheme.shapes.small) {
                Box(Modifier.size(46.dp), contentAlignment = Alignment.Center) {
                    Text(item.mark, color = fg, style = MaterialTheme.typography.labelLarge, fontWeight = FontWeight.Bold, maxLines = 1)
                }
            }
            Column(Modifier.weight(1f)) {
                Text(item.title, style = MaterialTheme.typography.bodyLarge, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text(item.sub, style = MaterialTheme.typography.labelMedium, color = cs.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
            // [下流→上流ディープリンク] 末尾に「設定で直す」（行本体タップの勤務表/直し方導線とは別アクション）。
            //   ローカル val に取り出してラムダ内スマートキャストを安全化。
            val ws = item.wishStaff
            val ns = item.needShift
            when {
                ws != null -> TextButton(onClick = { onFixWish(ws) }) { Text("設定で直す") }
                ns != null -> TextButton(onClick = { onFixNeed(ns) }) { Text("設定で直す") }
                clickable -> Text(if (dayOnly) "勤務表→" else "直し方→", style = MaterialTheme.typography.labelMedium,
                    color = ensureReadable(cs.surfaceVariant, MagiAccent.blue))
            }
        }
    }
}

/**
 * [★1/E1 要確認一覧] 散在していた診断を「箇所単位の重大度リスト」で1ハブに統合（web「画面修正版」confirm 移植）。
 * BreakdownCard（族集計）を補完し、covU/pref/c1/low/high/apt… の各違反箇所を 不足/過剰/窓 の重大度マーク付きで列挙。
 * 重大度フィルタ（全部/不足・必須/過剰・要調整/窓）で絞り、staff 紐付き項目はタップで修復フロー(onFocusStaff)へ。
 * 設定ミス(settingIssues)があれば先頭に件数導線を出す。表示のみ・スコアリング不変（読取専用）。
 */
@Composable
internal fun ConfirmListCard(
    ui: UiState,
    onFocusStaff: (Int) -> Unit,
    onGoEdit: () -> Unit,
    proMode: Boolean = false,
    // [ジャンプ/Web試作の移植] セル違反の項目タップで勤務表タブの該当セルへ移動＋ハイライト。
    onShowCell: (Int, Int) -> Unit = { i, _ -> onFocusStaff(i) },
    // [⑥日別ジャンプ] 日×シフト項目（人員/群レンジ）タップで勤務表タブの該当日列へ移動＋日ヘッダをハイライト。
    onShowDay: (Int) -> Unit = {},
    // [下流→上流ディープリンク] pref項目→希望シフト登録(該当職員)、covU/covO項目→必要人数カレンダー(該当シフト)へ。
    onFixWish: (Int) -> Unit = {},
    onFixNeed: (Int) -> Unit = {},
) {
    val items = remember(ui.violationCells, ui.violationCellFamilies, ui.needViolations, ui.countViolations, ui.schedule, ui.staffNames, ui.shiftSymbols, ui.startDate) { confirmItems(ui) }
    val issueCount = ui.settingIssues.size
    val cs = MaterialTheme.colorScheme
    if (items.isEmpty() && issueCount == 0) {
        // 達成表示（結果があり違反ゼロのときのみ）。データ未読込・実行中は何も出さない。
        if (ui.schedule.isNotEmpty() && !ui.running) {
            Card(Modifier.fillMaxWidth()) {
                Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                    Text("要確認一覧", style = MaterialTheme.typography.titleMedium)
                    Text("確認事項はありません（すべての条件を満たしています）。", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.tertiary)   // [監査修正] 成功テキストは AA を満たす tertiary(緑ロール)。MagiAccent.green は text で 3.3:1
                }
            }
        }
        return
    }
    var filter by rememberSaveable { mutableStateOf(-1) }   // -1=全部 / 0=不足・必須 / 1=過剰・要調整 / 2=窓
    val counts = intArrayOf(items.count { it.kind == 0 }, items.count { it.kind == 1 }, items.count { it.kind == 2 })
    // データ変化で選択中フィルタの件数が0になり得る（例: 窓を選択→再最適化で c1 消滅）。その時チップは消えるが
    // filter は残るため空リスト＋見出しの件数>0 という迷子状態になる。有効フィルタを「件数0なら全部へ戻す」で防ぐ。
    val effFilter = if (filter in 0..2 && counts[filter] == 0) -1 else filter
    val shown = items.filter { effFilter < 0 || it.kind == effFilter }
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            Text("要確認一覧（${items.size}件）", style = MaterialTheme.typography.titleMedium)
            if (!proMode) Text("タップで直し方を探します。",
                style = MaterialTheme.typography.bodySmall, color = cs.onSurfaceVariant)
            if (issueCount > 0) {
                Surface(color = cs.errorContainer, shape = MaterialTheme.shapes.medium) {
                    Row(Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 6.dp), verticalAlignment = Alignment.CenterVertically) {
                        Text("設定の見直し ${issueCount}件（実行前に直せます）", color = cs.onErrorContainer, style = MaterialTheme.typography.bodyMedium, modifier = Modifier.weight(1f))
                        TextButton(onClick = onGoEdit) { Text("設定へ") }
                    }
                }
            }
            Row(Modifier.horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                ConfirmFilterChip("全部", items.size, effFilter == -1) { filter = -1 }
                if (counts[0] > 0) ConfirmFilterChip("不足・必須", counts[0], effFilter == 0) { filter = 0 }
                // [④用語統一] 「調整」→「要調整」（グリッド凡例・編集シートの重大度語と揃える）。
                if (counts[1] > 0) ConfirmFilterChip("過剰・要調整", counts[1], effFilter == 1) { filter = 1 }
                if (counts[2] > 0) ConfirmFilterChip("窓", counts[2], effFilter == 2) { filter = 2 }
            }
            shown.take(40).forEach { ConfirmRow(it, onFocusStaff, onShowCell, onShowDay, onFixWish, onFixNeed) }
            if (shown.size > 40) Text("ほか ${shown.size - 40} 件（重大な順に表示中）", style = MaterialTheme.typography.labelSmall, color = cs.onSurfaceVariant)
            if (ui.running) Text("※実行中のため確定前の値です（確定後に最新化）", style = MaterialTheme.typography.labelSmall, color = cs.onSurfaceVariant)
        }
    }
}

/**
 * [★3+4] 日別/人別 注意リスト＋「要確認のみ」トグル（web「画面修正版」day/staff カード＋alertOnly の融合移植）。
 *  - 人別＝countViolations(職員×シフト)＋violationCells(職員×日)を職員ごとに件数集計。行タップで修復フロー。
 *  - 日別＝needViolations(シフト×日)を日ごとに件数集計。各行に不足/過剰のシフトを併記。
 *  - 「要確認のみ」ON(既定)で違反0の行を隠す＝そのまま triage。既存 BottleneckCard(top5テキスト)の上位互換だが
 *    additive-safe で併存。表示のみ・スコアリング不変（読取専用）。
 */
@Composable
internal fun AttentionCardsSection(ui: UiState, onFocusStaff: (Int) -> Unit) {
    if (ui.schedule.isEmpty()) return
    fun nm(i: Int) = ui.staffNames.getOrNull(i) ?: "#$i"
    fun sym(k: Int) = ui.shiftSymbols.getOrNull(k) ?: "$k"
    // 人別: countViolations(i,k)=回数, violationCells(i,j)=セル
    val staffAlerts = HashMap<Int, Int>()
    val staffShifts = HashMap<Int, LinkedHashSet<String>>()
    for ((key, cls) in ui.countViolations) {
        val p = key.split(","); val i = p.getOrNull(0)?.toIntOrNull() ?: continue; val k = p.getOrNull(1)?.toIntOrNull() ?: continue
        staffAlerts[i] = (staffAlerts[i] ?: 0) + 1
        // c2(個人の合計) は方向を持たない → 「過」を付けず記号のみ（下限割れ/上限超過と混同させない）。
        val dir = when { cls == "vio-low" || cls == "vio-aptLow" -> "不足"; cls == "vio-c2" -> ""; else -> "過剰" }   // [④用語統一]
        staffShifts.getOrPut(i) { LinkedHashSet() }.add("${sym(k)}$dir")
    }
    for ((key, _) in ui.violationCells) {
        val i = key.substringBefore(",").toIntOrNull() ?: continue
        staffAlerts[i] = (staffAlerts[i] ?: 0) + 1
    }
    // 日別: needViolations(k,j)=被覆/群レンジ
    val dayAlerts = HashMap<Int, Int>()
    val dayShifts = HashMap<Int, LinkedHashSet<String>>()
    for ((key, cls) in ui.needViolations) {
        val p = key.split(","); val k = p.getOrNull(0)?.toIntOrNull() ?: continue; val j = p.getOrNull(1)?.toIntOrNull() ?: continue
        dayAlerts[j] = (dayAlerts[j] ?: 0) + 1
        val dir = when (cls) { "vio-covU" -> "不足"; "vio-covO" -> "過剰"; else -> "群のレンジ" }   // [④用語統一] breakdownLabels の c41系と同語

        dayShifts.getOrPut(j) { LinkedHashSet() }.add("${sym(k)}$dir")
    }
    val days = ui.schedule.firstOrNull()?.size ?: 0
    val staffCount = ui.staffNames.size
    var mode by rememberSaveable { mutableIntStateOf(0) }        // 0=日別 1=人別
    var alertOnly by rememberSaveable { mutableStateOf(true) }
    val cs = MaterialTheme.colorScheme
    val (warnBg, warnFg) = magiWarnColors()
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text("日別・人別の要確認", style = MaterialTheme.typography.titleMedium, modifier = Modifier.weight(1f))
                Text("要確認のみ", style = MaterialTheme.typography.bodySmall, color = cs.onSurfaceVariant)
                Spacer(Modifier.width(6.dp))
                Switch(checked = alertOnly, onCheckedChange = { alertOnly = it })
            }
            MagiSegmentedControl(options = listOf("日別", "人別"), selected = mode, onSelect = { mode = it })
            if (mode == 0) {
                val rows = (0 until days).filter { !alertOnly || (dayAlerts[it] ?: 0) > 0 }
                if (rows.isEmpty()) {
                    Text(if (alertOnly) "日別の要確認はありません。" else "日がありません。", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.tertiary)   // [監査修正] 成功テキストは AA を満たす tertiary(緑ロール)。MagiAccent.green は text で 3.3:1
                } else rows.forEach { j ->
                    val ac = dayAlerts[j] ?: 0
                    AttentionRow(title = dayMD(ui.startDate, j), sub = dayShifts[j]?.joinToString("・") ?: "", alerts = ac, warnBg = warnBg, warnFg = warnFg, onClick = null)
                }
            } else {
                val rows = (0 until staffCount).filter { !alertOnly || (staffAlerts[it] ?: 0) > 0 }
                if (rows.isEmpty()) {
                    Text(if (alertOnly) "人別の要確認はありません。" else "職員がいません。", style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.tertiary)   // [監査修正] 成功テキストは AA を満たす tertiary(緑ロール)。MagiAccent.green は text で 3.3:1
                } else rows.forEach { i ->
                    val ac = staffAlerts[i] ?: 0
                    AttentionRow(title = nm(i), sub = staffShifts[i]?.joinToString("・") ?: "", alerts = ac, warnBg = warnBg, warnFg = warnFg, onClick = if (ac > 0) ({ onFocusStaff(i) }) else null)
                }
            }
        }
    }
}

/** [★3+4] 日別/人別 注意リストの1行。alerts>0 で警告バッジ、onClick!=null でタップ修復可。 */
@Composable
private fun AttentionRow(title: String, sub: String, alerts: Int, warnBg: Color, warnFg: Color, onClick: (() -> Unit)?) {
    val cs = MaterialTheme.colorScheme
    var m = Modifier.fillMaxWidth().heightIn(min = 52.dp)
    if (onClick != null) m = m.clickable { onClick() }
    Surface(color = cs.surfaceVariant, shape = MaterialTheme.shapes.medium, modifier = m) {
        Row(Modifier.padding(horizontal = 12.dp, vertical = 8.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Column(Modifier.weight(1f)) {
                Text(title, style = MaterialTheme.typography.bodyLarge, fontWeight = FontWeight.SemiBold, maxLines = 1, overflow = TextOverflow.Ellipsis)
                if (sub.isNotEmpty()) Text(sub, style = MaterialTheme.typography.labelMedium, color = cs.onSurfaceVariant, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
            if (alerts > 0) {
                Surface(color = warnBg, shape = MaterialTheme.shapes.small) {
                    Text("確認$alerts", color = warnFg, style = MaterialTheme.typography.labelLarge, fontWeight = FontWeight.Bold, modifier = Modifier.padding(horizontal = 10.dp, vertical = 5.dp))
                }
            }
            if (onClick != null) Text("直し方→", style = MaterialTheme.typography.labelMedium,
                color = ensureReadable(cs.surfaceVariant, MagiAccent.blue))
        }
    }
}

@Composable
internal fun BreakdownCard(ui: UiState, onFocusStaff: (Int) -> Unit = {}, proMode: Boolean = false) {
    val labels = breakdownLabels
    var criticalOnly by rememberSaveable { mutableStateOf(false) }
    var expanded by rememberSaveable { mutableStateOf<String?>(null) }
    val onTapChip: (String) -> Unit = { k -> expanded = if (expanded == k) null else k }
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(14.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text("違反の内訳", style = MaterialTheme.typography.titleMedium, modifier = Modifier.weight(1f))
                Text("重大のみ", style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                Spacer(Modifier.width(6.dp))
                Switch(checked = criticalOnly, onCheckedChange = { criticalOnly = it })
            }
            // [明確性I1] チップの数値は「ペナルティ量」。熟練者向けプロ表示では注記を省く。
            if (!proMode) {
                Text("数値は違反の大きさ。タップで場所を表示。",
                    style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            BreakdownGroup(if (proMode) "必須" else "必須（満たすべき）", listOf("groupViol", "pref", "covU", "c3n"), 2, ui, labels, expanded, onTapChip)
            if (!criticalOnly) {
                BreakdownGroup("人数の範囲", listOf("low", "high", "apt"), 1, ui, labels, expanded, onTapChip)
                BreakdownGroup(if (proMode) "任意" else "任意（できれば）", listOf("c1", "c2", "c3", "c3m", "c3mn", "c41", "c42", "c41s", "c42s", "covO", "fair", "weekly"), 0, ui, labels, expanded, onTapChip)
            }
            expanded?.let { key ->
                val cs = MaterialTheme.colorScheme
                val locs = breakdownLocations(key, ui)
                val name = labels[key] ?: key
                Surface(color = cs.secondaryContainer, shape = MaterialTheme.shapes.medium) {
                    Column(Modifier.fillMaxWidth().padding(12.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Text("$name の場所（${locs.size}箇所）", style = MaterialTheme.typography.titleSmall, color = cs.onSecondaryContainer, modifier = Modifier.weight(1f))
                            TextButton(onClick = { expanded = null }) { Text("閉じる") }
                        }
                        when {
                            locs.isEmpty() && ui.running -> Text("実行中です。確定後にここへ場所が表示されます。", style = MaterialTheme.typography.bodySmall, color = cs.onSecondaryContainer)
                            locs.isEmpty() -> Text("場所情報がありません。", style = MaterialTheme.typography.bodySmall, color = cs.onSecondaryContainer)
                            else -> {
                                locs.forEach { (txt, staff) ->
                                    if (staff != null) {
                                        Text("$txt　→直し方を探す", style = MaterialTheme.typography.bodyMedium, color = cs.onSecondaryContainer,
                                            modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp).clickable { onFocusStaff(staff) })
                                    } else {
                                        Text(txt, style = MaterialTheme.typography.bodyMedium, color = cs.onSecondaryContainer)
                                    }
                                }
                                if (ui.running) Text("※ 実行中のため確定前の値です（確定後に最新化）", style = MaterialTheme.typography.labelSmall, color = cs.onSecondaryContainer)
                            }
                        }
                    }
                }
            }
        }
    }
}


@Composable
internal fun BreakdownGroup(title: String, keys: List<String>, severity: Int, ui: UiState, labels: Map<String, String>, expanded: String?, onTap: (String) -> Unit) {
    Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Text(title, style = MaterialTheme.typography.labelLarge, color = MaterialTheme.colorScheme.onSurfaceVariant)
        keys.chunked(2).forEach { row ->
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                row.forEach { key -> SeverityChip(labels[key] ?: key, ui.breakdown[key] ?: 0, severity, key, expanded == key, onTap, Modifier.weight(1f)) }
                if (row.size == 1) Spacer(Modifier.weight(1f))
            }
        }
    }
}


@Composable
internal fun SeverityChip(label: String, count: Int, severity: Int, famKey: String, expanded: Boolean, onTap: (String) -> Unit, modifier: Modifier = Modifier) {
    val cs = MaterialTheme.colorScheme
    val active = count > 0
    val container: Color; val onContainer: Color
    when {
        !active -> { container = cs.surfaceVariant; onContainer = cs.onSurfaceVariant }
        severity >= 2 -> { container = cs.errorContainer; onContainer = cs.onErrorContainer }
        severity == 1 -> { container = cs.secondaryContainer; onContainer = cs.onSecondaryContainer }
        else -> { container = cs.primaryContainer; onContainer = cs.onPrimaryContainer }
    }
    val shape = MaterialTheme.shapes.small
    var m = modifier.heightIn(min = 48.dp)
    if (expanded) m = m.border(2.dp, onContainer.copy(alpha = 0.7f), shape)
    if (active) m = m.clickable { onTap(famKey) }
        .semantics { contentDescription = "$label $count 件" + (if (expanded) "・展開中" else "・タップで場所を表示") }
    Surface(color = container, shape = shape, modifier = m) {
        Row(Modifier.padding(horizontal = 12.dp, vertical = 10.dp), verticalAlignment = Alignment.CenterVertically) {
            Text(label, style = MaterialTheme.typography.bodyMedium, color = onContainer,
                modifier = Modifier.weight(1f), maxLines = 2, overflow = TextOverflow.Ellipsis)
            Text(count.toString(), style = MaterialTheme.typography.titleMedium, color = onContainer)
            if (active) {
                Spacer(Modifier.width(2.dp))
                Icon(if (expanded) Icons.Filled.KeyboardArrowUp else Icons.Filled.KeyboardArrowDown,
                    contentDescription = null, tint = onContainer, modifier = Modifier.size(18.dp))
            }
        }
    }
}

@Composable
internal fun BigStat(label: String, value: String, modifier: Modifier = Modifier) {
    Surface(color = MaterialTheme.colorScheme.surfaceVariant, shape = MaterialTheme.shapes.medium, modifier = modifier) {
        Column(
            Modifier.padding(vertical = 16.dp, horizontal = 12.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            // [B7] 5桁等の大きな値でも2行に折り返して崩れないよう1行固定＋省略（hardCore/Guard の大値は稀）。
            Text(value, style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.onSurface, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Spacer(Modifier.height(2.dp))
            Text(label, style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.onSurfaceVariant, textAlign = TextAlign.Center)
        }
    }
}


/** 改善手の種類 → (チップ文言, 色)。 */
private fun fixKindTag(k: com.magi.app.v6.FixKind): Pair<String, androidx.compose.ui.graphics.Color> = when (k) {
    com.magi.app.v6.FixKind.CHANGE -> "変更" to MagiAccent.green
    com.magi.app.v6.FixKind.CHANGE_MULTI -> "複数変更" to MagiAccent.green
    com.magi.app.v6.FixKind.SWAP -> "交換" to MagiAccent.blue
    com.magi.app.v6.FixKind.SWAP_XDAY -> "別日交換" to MagiAccent.blue
    com.magi.app.v6.FixKind.SWAP_MULTI -> "3人交換" to MagiAccent.purple
    com.magi.app.v6.FixKind.CHAIN -> "連鎖" to MagiAccent.red
    com.magi.app.v6.FixKind.WINDOW -> "再最適化" to MagiAccent.orange
}

@Composable
internal fun FixSuggestionCard(ui: UiState, onSearch: () -> Unit, onApply: (com.magi.app.v6.FixSuggestion) -> Unit, proMode: Boolean = false) {
    val cs = MaterialTheme.colorScheme
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                val title = "改善の提案" + if (ui.fixFocusName.isNotBlank()) "：${ui.fixFocusName} 関連" else ""
                Text(title, style = MaterialTheme.typography.titleMedium, modifier = Modifier.weight(1f))
                if (ui.fixSearching) {
                    Text("探索中…", style = MaterialTheme.typography.bodySmall, color = cs.onSurfaceVariant)
                } else {
                    TextButton(onClick = onSearch) { Text(if (ui.fixSuggestions.isEmpty()) "探す" else "全体で再探索") }
                }
            }
            if (!proMode) {
                Text("違反を減らす1手を効果順に提案します。",
                    style = MaterialTheme.typography.bodySmall, color = cs.onSurfaceVariant)
            }
            when {
                ui.fixSearching -> Text("候補を探しています。少しお待ちください。", style = MaterialTheme.typography.bodyMedium, color = cs.onSurfaceVariant)
                ui.fixSuggestions.isEmpty() -> Text("候補がありません。「探す」を押すか、上の違反の場所をタップしてください。\n※1手で直せない違反（下限が競合する等の構造的不足）は、設定の見直しが根本解です。",
                    style = MaterialTheme.typography.bodyMedium, color = cs.onSurfaceVariant)
                else -> ui.fixSuggestions.forEach { s ->
                    val (tag, tagColor) = fixKindTag(s.kind)
                    Surface(color = cs.secondaryContainer, shape = MaterialTheme.shapes.medium) {
                        Column(Modifier.fillMaxWidth().padding(12.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                MagiTagChip(text = tag, color = tagColor)
                                Text(s.label, style = MaterialTheme.typography.titleSmall, color = cs.onSecondaryContainer, modifier = Modifier.weight(1f))
                            }
                            val diffTxt = s.diff.joinToString("・") { (k, d) ->
                                "${breakdownLabels[k] ?: k} ${if (d < 0) "−${-d}" else "+$d"}"
                            }
                            val totalTxt = if (s.deltaTotal <= 0) "−${-s.deltaTotal}" else "+${s.deltaTotal}"
                            Text("違反 $totalTxt" + if (diffTxt.isNotBlank()) "（$diffTxt）" else "",
                                style = MaterialTheme.typography.bodyMedium, color = cs.onSecondaryContainer)
                            Button(onClick = { onApply(s) }, enabled = !ui.running, modifier = Modifier.align(Alignment.End).heightIn(min = 48.dp)) {
                                Text("この手を適用")
                            }
                        }
                    }
                }
            }
        }
    }
}

@Composable
internal fun AlternativesCard(ui: UiState, onApply: (Int) -> Unit) {
    if (ui.alternatives.isEmpty()) return
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Text("他の案（${ui.alternatives.size}）", style = MaterialTheme.typography.titleMedium)
            Text("採用案以外の候補です。", style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant)
            ui.alternatives.forEachIndexed { i, s ->
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                    Text(s, modifier = Modifier.weight(1f), style = MaterialTheme.typography.bodyMedium)
                    OutlinedButton(onClick = { onApply(i) }, enabled = !ui.running, modifier = Modifier.heightIn(min = 48.dp)) { Text("採用") }
                }
            }
        }
    }
}


@Composable
internal fun WishApplyCard(ui: UiState, onApply: () -> Unit) {
    if (!ui.loaded) return
    Card(Modifier.fillMaxWidth()) {
        Row(
            Modifier.padding(horizontal = 16.dp, vertical = 12.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Column(Modifier.weight(1f)) {
                Text("希望シフトを反映", fontWeight = FontWeight.Bold)
                Text("登録済みの希望を勤務表へ上書きします（元に戻せます）。",
                    style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            OutlinedButton(onClick = onApply, enabled = !ui.running, modifier = Modifier.heightIn(min = 48.dp)) { Text("希望を反映する") }
        }
    }
}

