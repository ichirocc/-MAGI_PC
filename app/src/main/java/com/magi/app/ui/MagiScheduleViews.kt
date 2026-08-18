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
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.ScrollState
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.TextButton
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.derivedStateOf
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
import androidx.compose.foundation.Canvas
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.drawText
import androidx.compose.ui.text.rememberTextMeasurer
import androidx.compose.ui.graphics.Path
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
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.runtime.LaunchedEffect
import kotlin.math.roundToInt

/**
 * CSVのバイト列を文字列へ復号する。妥当な UTF-8 ならそれを採用し、そうでなければ日本の Excel CSV で
 * 一般的な CP932(Shift-JIS) とみなす。先頭の BOM は除去する。これにより Shift-JIS の勤務表CSVが
 * 文字化けせず取り込める（UTF-8 として bytes を読むと壊れていた）。
 */

/** 実行中の進捗サマリ文字列: 改善率(初期soft→現best) ・ 残り時間 ・ 探索数。読取専用。 */
internal fun progressSummary(ui: UiState): String {
    val parts = ArrayList<String>(4)
    parts += when {
        // [3.393.0] 必須違反が残っている間は「改善◯%」の枝に入らないため、旧実装は進み具合を
        //   まったく示していなかった（⚠3 とだけ出る）。開始時の必須件数(initHard)を併記して
        //   「69件から3件まで来ている」ことを見せる。initHard は満足度の計算で既に使っており、
        //   ここが最後の未配線だった。
        ui.bestHard > 0L ->
            if (ui.initHard > ui.bestHard) "未解決 ⚠${ui.bestHard}（最初は${ui.initHard}）" else "未解決 ⚠${ui.bestHard}"
        ui.initSoft > 0L -> {
            val pct = ((ui.initSoft - ui.bestSoft) * 100L / ui.initSoft).coerceAtLeast(0L)
            "改善 ${pct}% (${ui.initSoft}→${ui.bestSoft})"
        }
        else -> "改善 –"
    }
    // 必須が残っている間は「改善◯%」が出ないので、ソフトを含む今の合計を出す（減っていく数字が1つは要る）。
    //   必須=0 のときは上の枝が (初期→現在) を出しているので重複させない。
    if (ui.bestHard > 0L && ui.totalViolations > 0) parts += "合計${ui.totalViolations}"
    val secLeft = ((ui.budgetSec * 1000L - ui.elapsedMs).coerceAtLeast(0L) / 1000L)
    parts += "残り %d:%02d".format(secLeft / 60, secLeft % 60)
    val it = ui.iters
    val iterStr = when {
        it >= 1_000_000L -> "%.1fM回".format(it / 1_000_000.0)
        it >= 1_000L -> "${it / 1_000L}K回"
        else -> "${it}回"
    }
    // [3.393.0] 毎秒の反復数。エンジンは毎回の進捗で計算していたのに表示先が無かった。
    //   「回数は増えているが遅い（ネイティブ加速が効いていない等）」を画面だけで見分けられるようにする。
    parts += if (ui.itersPerSec >= 1_000L) "$iterStr（毎秒${ui.itersPerSec / 1_000L}K）" else iterStr
    return parts.joinToString("  ・  ")
}

@Composable
internal fun LiveScheduleCard(ui: UiState) {
    if (!ui.running || ui.liveSchedule.isEmpty()) return
    var show by rememberSaveable { mutableStateOf(false) }
    val cs = MaterialTheme.colorScheme
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
            // [冗長性見直し] progressSummary(ui) は直上の OperatorNextActionCard の実行中表示（進捗行）に
            //   既出のため、ここでの再表示は削除（同一文字列が直列2回並んでいた）。
            val cur = ui.liveSchedule
            // 変化セル検出: 前回スナップショットとの差分。holder(非state)で保持し再合成ループを避ける。
            val prevHolder = remember { arrayOfNulls<List<List<Int>>>(1) }
            val changed = remember(cur) {
                val set = HashSet<Int>()
                val p = prevHolder[0]
                if (p != null && p.size == cur.size) {
                    for (i in cur.indices) {
                        val a = p[i]; val b = cur[i]
                        if (a.size == b.size) for (j in b.indices) if (a[j] != b[j]) set.add(i * 100000 + j)
                    }
                }
                prevHolder[0] = cur
                set
            }
            TextButton(onClick = { show = !show }, modifier = Modifier.heightIn(min = 48.dp)) {
                Icon(if (show) Icons.Filled.KeyboardArrowUp else Icons.Filled.KeyboardArrowDown,
                    contentDescription = null, modifier = Modifier.padding(end = 4.dp))
                Text(if (show) "途中経過を隠す" else "途中経過を見る")
            }
            if (show) {
                Text("状態遷移  赤枠＝今回変化 (${changed.size})", style = MaterialTheme.typography.labelSmall, color = cs.onSurfaceVariant)
                Column(
                    Modifier.horizontalScroll(rememberScrollState()),
                    verticalArrangement = Arrangement.spacedBy(1.dp),
                ) {
                    cur.forEachIndexed { i, row ->
                        Row(horizontalArrangement = Arrangement.spacedBy(1.dp)) {
                            row.forEachIndexed { j, k ->
                                val color = if (k < 0) cs.surfaceVariant else hexToColor(ui.shiftColorHex.getOrNull(k) ?: "")
                                val isChanged = changed.contains(i * 100000 + j)
                                Box(
                                    Modifier
                                        .size(11.dp)
                                        .background(color, RoundedCornerShape(2.dp))
                                        .then(if (isChanged) Modifier.border(2.dp, cs.error, RoundedCornerShape(2.dp)) else Modifier),
                                )
                            }
                        }
                    }
                }
            }
        }
    }
}

/**
 * [operator_ux §5] 「なおすのを手伝って」対話。人手不足を1タップで埋める誘導フロー。
 * いまの診断(coverageDiag)から「充足可能」な不足枠を1つ取り上げ、入れられる職員を大ボタンで提示。
 * タップ→反映(setCell, Undo可)→診断が更新され次の枠へ自動で進む。埋められない枠は理由つきで提示。
 */

@OptIn(ExperimentalMaterial3Api::class)
@Composable
internal fun ShiftPickerSheet(
    ui: UiState,
    vm: MagiViewModel,
    cell: Pair<Int, Int>,
    onPick: (Int) -> Unit,
    onDismiss: () -> Unit,
) {
    val (i, j) = cell
    val cs = MaterialTheme.colorScheme
    val haptic = LocalHapticFeedback.current   // [一貫性G2] 希望操作にも触覚を付ける（割当と対称）
    val sheetState = rememberModalBottomSheetState()
    val allowed = remember(cell) { vm.allowedShiftsFor(i).toList() }
    val current = ui.schedule.getOrNull(i)?.getOrNull(j) ?: -1
    val wish = ui.wishes["$i,$j"]
    var mode by remember(cell) { mutableIntStateOf(0) } // 0=割当, 1=希望
    val name = ui.staffNames.getOrNull(i) ?: i.toString()
    fun sym(k: Int?): String = k?.let { ui.shiftSymbols.getOrNull(it) } ?: "—"
    ModalBottomSheet(onDismissRequest = onDismiss, sheetState = sheetState) {
        Column(
            Modifier.fillMaxWidth().padding(horizontal = 20.dp).padding(bottom = 28.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            DialogHeader("$name ・ ${j + 1}日", onDismiss)
            // 凝縮ステータス: 現在の割当 + 希望 + 違反理由
            Surface(color = cs.surfaceVariant, shape = MaterialTheme.shapes.small) {
                Column(Modifier.fillMaxWidth().padding(12.dp)) {
                    // [セルタップで違反理由/認知ウォークスルー最優先] このセルの違反族を提示。
                    //   従来は枠の意味(なぜ違反か)が要確認一覧/診断ログへ往復しないと分からなかった。
                    //   [Set化] 重なった違反は全列挙（重み降順＝必須が先頭）。表示のみ・スコア不変。
                    // [見直しF1] 重大度色はユーザートークン(__vio__/__vioSoft__)から解決（グリッド/凡例と同色）。
                    val vioHardC = ui.violationColorHex.takeIf { it.isNotBlank() }?.let { hexToColor(it) } ?: cs.error
                    val vioSoftC = ui.violationSoftColorHex.takeIf { it.isNotBlank() }?.let { hexToColor(it) } ?: MagiAccent.orange
                    cellVioClasses(ui, "$i,$j").forEach { vioCls ->
                        val fam = vioCls.removePrefix("vio-")
                        val hard = isHardCellViolation(vioCls)
                        Text((if (hard) "⚠ 必須違反: " else "△ 要調整: ") + (breakdownLabels[fam] ?: fam),
                            style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.Bold,
                            color = resolvedVioColor(ui, vioCls, vioHardC, vioSoftC))
                    }
                    Text("現在の割当  ${sym(current)}", style = MaterialTheme.typography.bodyMedium)
                    val wt = if (wish == null) "希望  未登録"
                        else "希望  ${sym(wish)}" + (if (wish == current) "（反映済）" else "（未反映）")
                    // [UD監査] 桃(4.07:1)は通常文字でAA不足→太字(14sp bold=大テキスト扱い・3:1基準)で担保。
                    Text(wt, style = MaterialTheme.typography.bodyMedium,
                        fontWeight = if (wish != null && wish != current) FontWeight.Bold else FontWeight.Normal,
                        color = if (wish != null && wish != current) MagiAccent.pink else cs.onSurfaceVariant)
                    // [見直し候補] 割当変更（今回だけ）と土台ルールの直し（年間マスター）を混同させない第3の出口。
                    //   違反セルのみ表示。メモは年間マスターの先頭（ReviewMemoCard）に積まれる。
                    val vioFams = cellVioClasses(ui, "$i,$j")
                    if (vioFams.isNotEmpty()) {
                        TextButton(onClick = {
                            val famsJp = vioFams.joinToString("・") { breakdownLabels[it.removePrefix("vio-")] ?: it }
                            vm.addReviewMemo("$name ${j + 1}日=${sym(current)}：$famsJp")
                        }) { Text("基本ルールの見直し候補にする") }
                    }
                }
            }
            // 希望どおりにする（割当モード・未反映・担当可のときだけ）= 最頻操作を1タップ
            if (mode == 0 && wish != null && wish != current && wish in allowed) {
                Button(onClick = { onPick(wish) }, modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp)) {
                    Text("希望どおり ${sym(wish)} にする")
                }
            }
            // 割当/希望 トグル
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                listOf("割当を変更", "希望を変更").forEachIndexed { idx, label ->
                    val selSeg = mode == idx
                    Box(
                        Modifier.weight(1f).heightIn(min = 48.dp)
                            .background(if (selSeg) cs.primaryContainer else cs.surfaceVariant, MaterialTheme.shapes.small)
                            .clickable { mode = idx },
                        contentAlignment = Alignment.Center,
                    ) {
                        Text(label, color = if (selSeg) cs.onPrimaryContainer else cs.onSurfaceVariant,
                            fontWeight = if (selSeg) FontWeight.Bold else FontWeight.Normal)
                    }
                }
            }
            Text(
                if (mode == 0) "タップで割当を即変更。" else "タップで希望を登録/変更（即確定）。「外」=担当外（登録可・配置で違反）。",
                style = MaterialTheme.typography.labelMedium, color = cs.onSurfaceVariant,
            )
            val opts = if (mode == 0) (if (allowed.isNotEmpty()) allowed else ui.shiftSymbols.indices.toList())
                       else ui.shiftSymbols.indices.toList()
            opts.chunked(4).forEach { rowKeys ->
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp), modifier = Modifier.fillMaxWidth()) {
                    rowKeys.forEach { k ->
                        val symbol = ui.shiftSymbols.getOrNull(k) ?: k.toString()
                        val sel = if (mode == 0) k == current else k == wish
                        val ng = mode == 1 && k !in allowed
                        val bg = if (sel) cs.primary else hexToColor(ui.shiftColorHex.getOrNull(k) ?: "")
                        val fg = if (sel) cs.onPrimary else ensureReadable(bg, hexToColor(ui.shiftTextHex.getOrNull(k) ?: ""))
                        // [結果プレビュー] 割当モードのみ: 現在/希望/不足解消/超過 を注記（needViolations から確実に判定）。
                        val noteParts = ArrayList<String>()
                        var noteWarn = false
                        if (mode == 0) {
                            if (k == current) noteParts.add("現在") else if (k == wish) noteParts.add("希望")
                            when (ui.needViolations["$k,$j"]) {
                                "vio-covU" -> noteParts.add("不足解消")
                                "vio-covO" -> { noteParts.add("超過"); noteWarn = true }
                            }
                        }
                        val note = noteParts.joinToString("・")
                        Box(
                            Modifier
                                .weight(1f)
                                .heightIn(min = 56.dp)
                                .background(bg, RoundedCornerShape(16.dp))
                                .then(if (ng) Modifier.border(2.dp, cs.error, RoundedCornerShape(16.dp)) else Modifier)
                                .clickable {
                                    if (mode == 0) onPick(k) else { haptic.performHapticFeedback(HapticFeedbackType.LongPress); vm.setWish(i, j, k); onDismiss() }
                                }
                                .padding(4.dp),
                            contentAlignment = Alignment.Center,
                        ) {
                            Column(horizontalAlignment = Alignment.CenterHorizontally) {
                                Text(symbol + (if (ng) " 外" else ""), color = if (ng) cs.error else fg, fontWeight = FontWeight.Bold)
                                if (note.isNotEmpty()) {
                                    // [UD監査] 任意のシフト色上の注記は WCAG 保証（橙/緑の生アクセントは淡色上で不足）。
                                    Text(note, style = MaterialTheme.typography.labelSmall,
                                        color = if (sel) cs.onPrimary else ensureReadable(bg, if (noteWarn) MagiAccent.orange else MagiAccent.green))
                                }
                            }
                        }
                    }
                    repeat(4 - rowKeys.size) { Spacer(Modifier.weight(1f)) }
                }
            }
            // 希望を削除（希望モード・登録済みのみ）
            if (mode == 1 && wish != null) {
                OutlinedButton(onClick = { haptic.performHapticFeedback(HapticFeedbackType.LongPress); vm.removeWish(i, j); onDismiss() }, modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp)) {
                    Text("希望を削除（希望なし）", color = cs.error)
                }
            }
        }
    }
}


// [3.193.0 シンプル化] StaffCalendarCard/CalendarCell（職員別カレンダー）を撤去。全職員グリッド
//   （ScheduleGrid）と同じ盤面の二重表示＝タブの密度/冗長の主因だった（旧コメントが自認済み）。呼出0を確認済み。

// [D7撤去] ScheduleModeCard（結果=読取/下書き=編集の切替）はユーザー判断で撤去。勤務表は常に直接編集の1本。
//   [3.393.0] UiState 側の結果スナップショット（resultSchedule / result 専用違反マップ6種 / hasResultSnapshot）は
//   D7 から一度も読み手が現れなかったので撤去した。ViewModel の resultSchedule（最後に生成した結果の控え）は
//   「開く前のデータに戻す」等が使う生きた状態なので残っている。


// ===== [E7] 違反 種別フィルタ =====
// [3.382.0] 分類表（VioBucket/vioBuckets/vioBucketlessFamilies/familyOfVioClass/bucketOfFamily/
//   vioVisible/allVioBucketKeys）は Compose 非依存なので `VioBuckets.kt` へ切り出した
//   （族の追加漏れを `VioBucketsTest` で機械的に固定するため）。ロジックは不変。

/** [Set化] セル("i,j")の全違反クラス（重み降順）。families 未充填の経路では最重1クラスへフォールバック。 */
internal fun cellVioClasses(ui: UiState, key: String): List<String> =
    ui.violationCellFamilies[key] ?: listOfNotNull(ui.violationCells[key])
/** [Set化×E7] フィルタを通過する最重の違反クラス。旧: 最重1クラスのみ判定＝最重族のバケツをOFFにすると
 *  表示中の族が同セルに残っていても枠ごと消えていた（フィルタと表示の不整合）。 */
internal fun visibleCellVio(ui: UiState, key: String, enabled: Set<String>): String? =
    cellVioClasses(ui, key).firstOrNull { vioVisible(it, enabled) }

/** [違反色/族別] 違反クラスの表示色を解決: 族別色（__vioFam_*）→ 重大度色（必須/要調整）の順でフォールバック。 */
internal fun resolvedVioColor(ui: UiState, cls: String?, hardC: Color, softC: Color): Color {
    if (cls == null) return hardC
    ui.violationFamilyColorHex[familyOfVioClass(cls)]?.takeIf { it.isNotBlank() }?.let { return hexToColor(it) }
    return if (isHardCellViolation(cls)) hardC else softC
}

/** [E7] 各バケットの「違反ロケーション数」(=セル/エントリ件数、見出し『要確認 N件』と同単位)。
 *  breakdown の量/#fire ではなく箇所数で集計＝チップ間・見出しと比較可能なトリアージ指標にする。 */
internal fun vioBucketLocCounts(ui: UiState): Map<String, Int> {
    val out = HashMap<String, Int>()
    fun tally(cls: String) { bucketOfFamily(familyOfVioClass(cls))?.let { out[it] = (out[it] ?: 0) + 1 } }
    // [Set化] セルは重なった全族のバケツへ計上（同セル同バケツは1回）＝バケツOFF/ONの見え方と件数が一致。
    ui.violationCells.keys.forEach { key ->
        cellVioClasses(ui, key).mapNotNull { bucketOfFamily(familyOfVioClass(it)) }.toSet()
            .forEach { b -> out[b] = (out[b] ?: 0) + 1 }
    }
    ui.needViolations.values.forEach(::tally)
    ui.countViolations.values.forEach(::tally)
    return out
}

/** [週ページング] 月曜始まりで日を週に分割（各週=その週に属する日index）。最初の週は部分週になり得る。
 *  週送りで横スクロールを解消するのに使う（画面修正版の「週」ビュー）。startDate 不正でも 7日ずつに退避。 */
internal fun mondayWeeks(startDate: String, days: Int): List<List<Int>> {
    val sdow = startDowMonFirst(startDate)   // 0=月
    val weeks = ArrayList<MutableList<Int>>()
    for (d in 0 until days) {
        val wd = (sdow + d) % 7
        if (weeks.isEmpty() || wd == 0) weeks.add(ArrayList())
        weeks.last().add(d)
    }
    return weeks
}

/** [E7] 6バケツの件数付きフィルタチップ行（勤務表タブ共有）。件数は breakdown から族合計。0件は淡色。 */
@OptIn(ExperimentalLayoutApi::class)
@Composable
internal fun ViolationFilterBar(bucketCounts: Map<String, Int>, enabled: Set<String>, onToggle: (String) -> Unit, locCount: Int = -1,
    focusMode: Boolean = false, onFocusMode: (Boolean) -> Unit = {}) {
    val cs = MaterialTheme.colorScheme
    // [監査修正] チップ件数は「違反ロケーション数(箇所)」＝見出し「要確認 N件」と同単位。旧: breakdown の量(low/high は
    //   不足量計・c1 は #fire)を混在合算しており、単位不一致で「回数20 vs 要確認1件」の誤トリアージを招いていた。
    val counts = bucketCounts
    val anyViol = counts.values.any { it > 0 }
    if (!anyViol) return   // 違反ゼロなら出さない（ノイズ削減）
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(horizontal = 16.dp, vertical = 12.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                // [画面修正版 ③] 「要確認 N件」= 違反ロケーション数（族fire数でなく作成者が見るべきセル数）。
                Text(if (locCount >= 0) "違反フィルタ（種別）・要確認 ${locCount}件" else "違反フィルタ（種別）",
                    style = MaterialTheme.typography.labelLarge, color = cs.onSurfaceVariant, modifier = Modifier.weight(1f))
                if (enabled != allVioBucketKeys) {
                    TextButton(onClick = { vioBuckets.forEach { if (it.key !in enabled) onToggle(it.key) } }) {
                        Text("すべて表示", style = MaterialTheme.typography.labelLarge)
                    }
                }
                // [集中モード/Web試作③] 違反・未反映希望のセルだけを浮かせ、他を淡色に沈めるトグル（表示のみ）。
                FilterChip(selected = focusMode, onClick = { onFocusMode(!focusMode) },
                    label = { Text("集中", style = MaterialTheme.typography.titleSmall) })
            }
            Spacer(Modifier.height(4.dp))
            FlowRow(horizontalArrangement = Arrangement.spacedBy(6.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                vioBuckets.forEach { b ->
                    val n = counts[b.key] ?: 0
                    val on = b.key in enabled
                    FilterChip(
                        selected = on,
                        onClick = { onToggle(b.key) },
                        label = {
                            Text("${b.label} $n",
                                style = MaterialTheme.typography.titleSmall,
                                // 0件は淡色（存在しない種別＝トリアージ上ノイズ）。トグル自体は可能。
                                color = if (n == 0) cs.onSurfaceVariant.copy(alpha = 0.5f) else Color.Unspecified)
                        },
                    )
                }
            }
            // [P7/実務者向け短文化] コーチング文（多い種類から潰す…）は削除。チップの件数が優先順を語る。
            // [冗長性見直し] 操作説明はチップのトグル自体が示すため削除。
        }
    }
}

/** [画面修正版 ②] 検索・凡例の統合折りたたみ（既定=閉）。E7種別フィルタは含めない＝可視のまま別バー(ユーザー指示)。
 *  検索=職員名で該当グリッド行を強調(行は隠さず被覆の文脈保持)。凡例=シフト色＋違反(実線=必須/破線=要調整)。 */
@Composable
internal fun SearchLegendBar(ui: UiState, query: String, onQuery: (String) -> Unit) {
    val cs = MaterialTheme.colorScheme
    var open by rememberSaveable { mutableStateOf(false) }
    val vioColor = ui.violationColorHex.takeIf { it.isNotBlank() }?.let { hexToColor(it) } ?: cs.error
    val vioSoftColor = ui.violationSoftColorHex.takeIf { it.isNotBlank() }?.let { hexToColor(it) } ?: MagiAccent.orange
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(horizontal = 16.dp, vertical = 12.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.fillMaxWidth().clickable { open = !open }) {
                Text("検索・凡例" + (if (!open && query.isNotBlank()) "（検索中: $query）" else ""),
                    style = MaterialTheme.typography.labelLarge, color = cs.onSurfaceVariant, modifier = Modifier.weight(1f))
                Text(if (open) "閉じる ▾" else "開く ▸", style = MaterialTheme.typography.labelLarge, color = cs.onSurfaceVariant)
            }
            if (open) {
                Spacer(Modifier.height(8.dp))
                OutlinedTextField(
                    value = query, onValueChange = onQuery,
                    label = { Text("職員名で検索（該当行を強調）") },
                    singleLine = true, modifier = Modifier.fillMaxWidth(),
                    trailingIcon = { if (query.isNotEmpty()) TextButton(onClick = { onQuery("") }) { Text("消す") } },
                )
                Spacer(Modifier.height(10.dp))
                if (ui.violationCells.isNotEmpty()) { ViolationLegend(vioColor, vioSoftColor); Spacer(Modifier.height(6.dp)) }
                ShiftColorLegend(ui.shiftSymbols, ui.shiftColorHex, ui.shiftTextHex)
            }
        }
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
internal fun ScheduleGrid(
    ui: UiState, onCellClick: (Int, Int) -> Unit, proMode: Boolean = false, nameQuery: String = "",
    vioEnabled: Set<String> = allVioBucketKeys,
    onBulkSet: (Collection<Pair<Int, Int>>, Int) -> Unit = { _, _ -> },
    // [ジャンプ/Web試作の移植] 要確認一覧から渡される注目セル(i,j)。該当日へ自動スクロール＋一時ハイライト。
    focusCell: Pair<Int, Int>? = null,
    onFocusShown: () -> Unit = {},
    focusRange: Triple<Int, Int, Int>? = null,   // [窓ハイライト③] (職員i, 開始日, 終了日)
    focusMode: Boolean = false,                  // [集中モード] 違反・未反映希望以外のセルを淡色化
    canDo: (Int, Int) -> Boolean = { _, _ -> true },   // [矛盾なく選択] 一括割当の担当可否（(職員i, シフトk)）
) {
    val cs = MaterialTheme.colorScheme
    // [一括編集] 円柱は1セル編集。まとめて変更するダイアログの開閉。
    var showBulk by rememberSaveable { mutableStateOf(false) }
    // [週ページング＋横スクロール併用] 全日を横スクロールで保持しつつ、前週/次週 で1週ぶんジャンプ。
    //   現在週は左端可視日から導出＝自由スクロールにも追従（トグルで列を隠さない）。
    val allDays = ui.days.coerceAtLeast(1)
    val weeks = remember(ui.startDate, allDays) { mondayWeeks(ui.startDate, allDays) }
    val hScroll = rememberScrollState()
    val scrollScope = rememberCoroutineScope()
    // [E7] グリッドのセル違反は共有フィルタ(vioEnabled)で表示/非表示（[Set化] visibleCellVio で判定）。
    Card(Modifier.fillMaxWidth()) {
        BoxWithConstraints {
        // [7日間表示] セル幅を「利用可能幅から1週間(7日)が名前列と同時に収まる」よう動的計算（旧: 48dp固定＝
        //   多くの端末で6日強しか見えず週の模様が切れていた）。36dp未満は記号(2文字15sp)の可読性が崩れるため
        //   下限36dp（極端に狭い端末のみ7日未満に妥協）、48dp超は広げない（広い端末はより多くの日が見える）。
        //   週ページングのスクロール量(cellWpx)も同じ値から計算＝ジャンプ位置は常にグリッドと整合。
        val gridCellW = ((this.maxWidth - 32.dp - 80.dp) / 7).coerceIn(36.dp, 48.dp)   // 32=Column水平padding, 80=名前列
        val cellWpx = with(LocalDensity.current) { gridCellW.roundToPx() }
        // derivedStateOf: hScroll.value を読むのはこの派生値の中だけ＝スクロールで再構成するのは週ラベル等の読者のみ
        //   （グリッド本体は curWeek を読まないので再構成されない＝スクロール性能を保つ）。
        val curWeek by remember(weeks, cellWpx) {
            derivedStateOf {
                val d = if (cellWpx > 0) hScroll.value / cellWpx else 0
                weeks.indexOfFirst { d <= it.last() }.let { if (it < 0) (weeks.size - 1).coerceAtLeast(0) else it }
            }
        }
        // [ジャンプ] 注目セルの日列へスクロールし、約2.5秒後にハイライトを解除（表示のみ）。
        LaunchedEffect(focusCell) {
            val fc = focusCell ?: return@LaunchedEffect
            hScroll.animateScrollTo((fc.second * cellWpx).coerceAtLeast(0))
            kotlinx.coroutines.delay(2_500)
            onFocusShown()
        }
        Column(Modifier.padding(16.dp)) {
            Text("勤務表", style = MaterialTheme.typography.titleMedium)
            Spacer(Modifier.height(8.dp))
            // [一括編集] まとめて変更は多数セルを上書きする上級操作のため、プロ表示時のみ。範囲×対象×シフトをダイアログで一括指定。
            if (proMode) {
                OutlinedButton(onClick = { showBulk = true }, enabled = !ui.running, modifier = Modifier.heightIn(min = 48.dp)) {
                    Text("まとめて割当")
                }
            }
            // [Web試作①] シフト別の人員不足サマリー: covU のある日数をシフト別に集計（多い順）＝
            //   「どのシフトが慢性的に埋まらないか」を1行で提示。E7 人員バケツOFF時は他の covU 表示と同様に隠す。
            if ("need" in vioEnabled) {
                val shortByShift = ui.needViolations.entries
                    .filter { it.value == "vio-covU" }
                    .mapNotNull { e ->
                        val p = e.key.split(",")
                        val k = p.getOrNull(0)?.toIntOrNull(); val j = p.getOrNull(1)?.toIntOrNull()
                        if (k == null || j == null) null else k to j
                    }
                    .groupBy({ it.first }, { it.second })
                    .entries.sortedByDescending { it.value.size }
                if (shortByShift.isNotEmpty()) {
                    Spacer(Modifier.height(6.dp))
                    Surface(color = cs.errorContainer, shape = MaterialTheme.shapes.small) {
                        Text("人員不足（全${allDays}日中）: " +
                            shortByShift.joinToString(" ・ ") { (k, ds) -> "${ui.shiftSymbols.getOrNull(k) ?: k} ${ds.distinct().size}日" },
                            color = cs.onErrorContainer, style = MaterialTheme.typography.labelMedium,
                            modifier = Modifier.fillMaxWidth().padding(horizontal = 10.dp, vertical = 6.dp))
                    }
                }
            }
            // [E7 誰が・いつ] 表示中(フィルタ通過)のセル違反を「名前 d日」で列挙（最大8件）。種別を絞ると場所が一目で分かる。
            //   種別チップは勤務表タブ上部の共有フィルタ(ViolationFilterBar)へ集約したのでここには出さない。
            run {
                val shownLocs = ui.violationCells.entries.filter { visibleCellVio(ui, it.key, vioEnabled) != null }.mapNotNull { e ->
                    val p = e.key.split(","); val i = p.getOrNull(0)?.toIntOrNull(); val j = p.getOrNull(1)?.toIntOrNull()
                    if (i == null || j == null) null else "${ui.staffNames.getOrNull(i) ?: "#$i"} ${j + 1}日"
                }
                if (shownLocs.isNotEmpty()) {
                    Spacer(Modifier.height(4.dp))
                    val more = if (shownLocs.size > 8) " 他${shownLocs.size - 8}件" else ""
                    Text("違反セル：${shownLocs.take(8).joinToString("、")}$more",
                        style = MaterialTheme.typography.labelSmall, color = cs.onSurfaceVariant)
                }
            }
            // [②] 凡例は上部「検索・凡例」折りたたみへ集約したためグリッド内からは撤去（重複回避）。
            // [週ページング＋横スクロール併用] 前週/次週 は hScroll を1週ぶんジャンプ（列は隠さない＝自由スクロールと併用）。
            if (weeks.size > 1) {
                Spacer(Modifier.height(10.dp))
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    OutlinedButton(
                        onClick = { val t = weeks[(curWeek - 1).coerceAtLeast(0)].first(); scrollScope.launch { hScroll.animateScrollTo(t * cellWpx) } },
                        enabled = curWeek > 0, modifier = Modifier.heightIn(min = 48.dp)) { Text("← 前週") }
                    val wk = weeks.getOrNull(curWeek)
                    val label = if (wk != null && wk.isNotEmpty()) "第${curWeek + 1}/${weeks.size}週（${wk.first() + 1}〜${wk.last() + 1}日）" else "第${curWeek + 1}週"
                    Text(label, style = MaterialTheme.typography.labelLarge, color = cs.onSurfaceVariant,
                        modifier = Modifier.weight(1f), textAlign = TextAlign.Center, maxLines = 1)
                    OutlinedButton(
                        onClick = { val t = weeks[(curWeek + 1).coerceAtMost(weeks.size - 1)].first(); scrollScope.launch { hScroll.animateScrollTo(t * cellWpx) } },
                        enabled = curWeek < weeks.size - 1, modifier = Modifier.heightIn(min = 48.dp)) { Text("次週 →") }
                }
            }
            // [違反ナビ] 表示中（フィルタ通過）の違反がある日を ＜前/次＞ で巡回（Web試作「不足日へ」の一般化）。
            //   ジャンプ先の日ヘッダは focusCell=(-1,j) の番兵で約2.5秒ハイライト（⑥日別ジャンプと同機構）。
            val vioDays = remember(ui.violationCells, ui.violationCellFamilies, ui.needViolations, vioEnabled) {
                val days = sortedSetOf<Int>()
                ui.violationCells.keys.forEach { key ->
                    if (visibleCellVio(ui, key, vioEnabled) != null) key.substringAfter(",").toIntOrNull()?.let { days.add(it) }
                }
                for ((k, cls) in ui.needViolations) {
                    if (vioVisible(cls, vioEnabled)) k.substringAfter(",").toIntOrNull()?.let { days.add(it) }
                }
                days.toList()
            }
            var navFlash by remember { mutableStateOf<Pair<Int, Int>?>(null) }
            LaunchedEffect(navFlash) {
                if (navFlash != null) { kotlinx.coroutines.delay(2_500); navFlash = null }
            }
            if (vioDays.isNotEmpty()) {
                var navIdx by remember(vioDays) { mutableIntStateOf(-1) }
                fun jumpTo(n: Int) {
                    navIdx = n
                    val d = vioDays[n]
                    navFlash = -1 to d
                    scrollScope.launch { hScroll.animateScrollTo((d * cellWpx).coerceAtLeast(0)) }
                }
                Spacer(Modifier.height(8.dp))
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    OutlinedButton(onClick = { jumpTo(if (navIdx <= 0) vioDays.size - 1 else navIdx - 1) },
                        modifier = Modifier.heightIn(min = 48.dp)) { Text("＜ 前の違反") }
                    Text(if (navIdx < 0) "違反のある日 ${vioDays.size}日" else "違反日 ${navIdx + 1}/${vioDays.size}",
                        style = MaterialTheme.typography.labelLarge, color = cs.onSurfaceVariant,
                        modifier = Modifier.weight(1f), textAlign = TextAlign.Center, maxLines = 1)
                    OutlinedButton(onClick = { jumpTo(if (navIdx < 0) 0 else (navIdx + 1) % vioDays.size) },
                        modifier = Modifier.heightIn(min = 48.dp)) { Text("次の違反 ＞") }
                }
            }
            Spacer(Modifier.height(12.dp))
            MagiFlatGrid(ui, onCellClick, vioEnabled, hScroll, nameQuery, cellW = gridCellW, focusCell = focusCell ?: navFlash, focusRange = focusRange, focusMode = focusMode, canDo = canDo)   // [円柱やめる] フィッシュアイ→平面グリッドに置換（旧円柱コードは削除済み）
            if (showBulk) AssignBulkSheet(ui, onBulkSet, onDismiss = { showBulk = false }, canDo = canDo)
        }
        }
    }
}

// [D7撤去] CellInfoDialog（読取モードの見るだけ理由表示, 3.119.0）は読取モード自体の撤去に伴い不要化・撤去。
//   理由表示はセル編集シート（常時開く）が一元的に担う。

/** 期間開始日の曜日インデックス（0=月..6=日）。解析不能なら 0。 */

internal fun startDowMonFirst(startDate: String): Int = try {
    (LocalDate.parse(startDate).dayOfWeek.value - 1).coerceIn(0, 6)
} catch (_: Exception) { 0 }

/** 違反セルの凡例（実線=必須 / 破線=要調整）。非色手がかりの意味を必ず示す。 */

@Composable
@OptIn(ExperimentalLayoutApi::class)
internal fun ViolationLegend(vioColor: Color, vioSoftColor: Color = MagiAccent.orange) {
    val cs = MaterialTheme.colorScheme
    // [実機指摘] 固定 Row では幅不足時に3項目目が縦1文字に潰れた→ FlowRow で項目単位に折り返す。
    FlowRow(horizontalArrangement = Arrangement.spacedBy(16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            Box(Modifier.size(width = 22.dp, height = 16.dp).border(3.dp, vioColor, RoundedCornerShape(4.dp)))
            // [B4] 色名は固定しない（ユーザーが違反色を変更でき、凡例とグリッドが食い違うため）。
            //   実線/破線の形状＋左の色見本が真の手がかり（色覚配慮＝形状符号化）。
            Text("実線＝必須違反", style = MaterialTheme.typography.labelSmall, color = cs.onSurfaceVariant)
        }
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            Box(Modifier.size(width = 22.dp, height = 16.dp).violationBorder(false, vioSoftColor, 4.dp))
            Text("破線＝要調整（重）", style = MaterialTheme.typography.labelSmall, color = cs.onSurfaceVariant)
        }
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            Box(Modifier.size(width = 22.dp, height = 16.dp).border(1.dp, cs.outlineVariant, RoundedCornerShape(4.dp)).drawBehind {
                val t = 12.dp.toPx()
                val p = Path().apply { moveTo(size.width - t, 0f); lineTo(size.width, 0f); lineTo(size.width, t); close() }
                drawPath(p, vioSoftColor)
            })
            Text("右上の角＝要調整（軽）", style = MaterialTheme.typography.labelSmall, color = cs.onSurfaceVariant)
        }
    }
}

/**
 * [色キー] シフトの色凡例。各シフトの記号を「実際のグリッド色」で表示し、色→シフトを即座にひける。
 *  シフト作成者は記号・名称を熟知している前提のため、記号=名称の説明ではなく、グリッドの着色を
 *  解読するための色対応を提供する（色覚配慮で記号文字も併記＝色＋形の二重符号化）。
 */
@OptIn(ExperimentalLayoutApi::class)
@Composable
internal fun ShiftColorLegend(symbols: List<String>, colorHex: List<String>, textHex: List<String>) {
    val items = symbols.indices.filter { symbols[it].isNotBlank() }
    if (items.isEmpty()) return
    val cs = MaterialTheme.colorScheme
    Column {
        Spacer(Modifier.height(8.dp))
        Text("シフトの色", style = MaterialTheme.typography.labelMedium, color = cs.onSurfaceVariant)
        Spacer(Modifier.height(4.dp))
        FlowRow(horizontalArrangement = Arrangement.spacedBy(6.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            items.forEach { i ->
                val bg = hexToColor(colorHex.getOrNull(i) ?: "")
                val fg = hexToColor(textHex.getOrNull(i) ?: "")
                // [実機指摘「有やAｱの形がおかしい」] 記号の文字種（ASCII/半角カナ/漢字）で内在サイズが
                //   異なりチップの高さ・形がバラついていた→ 固定サイズ（高さ32dp・最小幅48dp）＋中央寄せで均一に。
                Box(
                    Modifier.height(32.dp).widthIn(min = 48.dp).background(bg, RoundedCornerShape(6.dp))
                        .padding(horizontal = 10.dp),
                    contentAlignment = Alignment.Center,
                ) {
                    Text(symbols[i], style = MaterialTheme.typography.labelLarge, color = ensureReadable(bg, fg), fontWeight = FontWeight.Bold, maxLines = 1)
                }
            }
        }
    }
}

/**
 * 違反値("vio-<family>")が必須(HARD)系か判定。色に依らない手がかり(実線/破線)の切替に使う。
 * ハード族の一覧は MirrorKeys.hard を唯一の真実源とする（ここで列挙し直すと将来の追加/改名で乖離する）。
 */

internal fun isHardCellViolation(v: String?): Boolean =
    v != null && MirrorKeys.hard.any { v.contains(it) }

/** [判読性] 破線枠にする「重いソフト族」（low=90 / high=45 / c3mn=15）か。
 *  残りは右上の角マークに落として「格子全体が警告に埋まって必須違反が埋没する」のを防ぐ。
 *  [3.253.0以降の注記] c1 は重み 15（=c3mn）に上がったが、**最多件数のソフト族**のため飽和回避を
 *  優先して意図的に角マーク側に据え置く（表示の強さ＝重みだけでなく件数も加味）。severity-match を
 *  優先して c1 も破線にしたい場合は heavySoftFamilies に "c1" を足す一行変更（ただしグリッド飽和のリスク）。 */
internal val heavySoftFamilies = setOf("low", "high", "c3mn")
internal fun isHeavySoftCellViolation(v: String?): Boolean =
    v != null && familyOfVioClass(v) in heavySoftFamilies

/** 違反セルの非色手がかり: HARD=実線枠、SOFT=破線枠（色覚多様性／モノクロ印刷でも区別可能）。
 *  [校正] 色付きセル上でも埋もれないよう枠を太く（3dp）。
 *  [実機指摘/枠のハロー] halo!=null で枠の内側に対比色（surface）の縁取りを敷く。ダークテーマの違反色
 *  （淡い赤）は桃系セル背景と同系色で枠が埋没していた（アリフの c3n 実線枠が判読不能）。ハローが
 *  枠とセル地を分離し、任意のシフト色上で枠が浮く（角マーク 3.105.0 と同じ手法）。 */

internal fun Modifier.violationBorder(hard: Boolean, color: Color, radiusDp: androidx.compose.ui.unit.Dp, halo: Color? = null): Modifier =
    if (hard) {
        // [実機バグ修正] Modifier.border はチェーンの「先」が最後=最前面に描かれる（内側の drawContent 後に
        //   自分の枠を描くため）。旧実装はハローを先に置いたため 5dp のハローが違反色 3dp を覆い、枠が
        //   白リングだけに見えていた。違反色を先（最前面）・ハローを後（背面）に: 外側3dp=違反色/内側2dp=ハロー。
        this.border(3.dp, color, RoundedCornerShape(radiusDp))
            .then(if (halo != null) Modifier.border(5.dp, halo, RoundedCornerShape(radiusDp)) else Modifier)
    } else {
        this.drawBehind {
            val stroke = 3.dp.toPx()
            val r = radiusDp.toPx()
            // 破線の下に実線ハロー（太め）を敷く: 破線の隙間・両脇がハロー色になり、同系色セル上でも読める。
            if (halo != null) {
                drawRoundRect(
                    color = halo,
                    topLeft = Offset(stroke / 2f, stroke / 2f),
                    size = Size(size.width - stroke, size.height - stroke),
                    cornerRadius = CornerRadius(r, r),
                    style = Stroke(width = stroke + 2.dp.toPx()),
                )
            }
            drawRoundRect(
                color = color,
                topLeft = Offset(stroke / 2f, stroke / 2f),
                size = Size(size.width - stroke, size.height - stroke),
                cornerRadius = CornerRadius(r, r),
                style = Stroke(width = stroke, pathEffect = PathEffect.dashPathEffect(floatArrayOf(10f, 6f))),
            )
        }
    }


/** [希望の一括操作] 対象範囲(曜日/期間全体) × 対象(全員/1名) × 希望シフト。登録/クリア。誤操作防止で明示確定。 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
internal fun WishBulkSheet(ui: UiState, vm: MagiViewModel, presetWeekday: Int, onDismiss: () -> Unit) {
    val cs = MaterialTheme.colorScheme
    val sheetState = rememberModalBottomSheetState()
    val days = ui.days
    val weekdays = listOf("月", "火", "水", "木", "金", "土", "日")
    val startDow = startDowMonFirst(ui.startDate)
    var scope by remember { mutableIntStateOf(0) }                       // 0=この曜日, 1=期間全体
    var weekday by remember { mutableIntStateOf(presetWeekday.coerceIn(0, 6)) }
    var staffSel by remember { mutableIntStateOf(-1) }                   // -1=全職員, else staff index
    var picked by remember { mutableIntStateOf(-1) }                     // 選択中の希望シフト
    var showStaff by remember { mutableStateOf(false) }
    var confirmClearAll by remember { mutableStateOf(false) }
    val targetDays = if (scope == 1) (0 until days).toList()
        else (0 until days).filter { (startDow + it) % 7 == weekday }
    val allowed = if (staffSel >= 0) vm.allowedShiftsFor(staffSel).toList() else emptyList()
    val targetName = if (staffSel >= 0) (ui.staffNames.getOrNull(staffSel) ?: "$staffSel") else "全職員"
    ModalBottomSheet(onDismissRequest = onDismiss, sheetState = sheetState) {
        Column(
            Modifier.fillMaxWidth().padding(horizontal = 20.dp).padding(bottom = 28.dp).verticalScroll(rememberScrollState()),
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            DialogHeader("希望シフトの一括操作", onDismiss)
            Text("対象範囲", style = MaterialTheme.typography.labelMedium, color = cs.onSurfaceVariant)
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                listOf("この曜日", "期間全体").forEachIndexed { idx, label ->
                    val s = scope == idx
                    Box(Modifier.weight(1f).heightIn(min = 48.dp)
                        .background(if (s) cs.primaryContainer else cs.surfaceVariant, MaterialTheme.shapes.small)
                        .clickable { scope = idx }, contentAlignment = Alignment.Center) {
                        Text(label, color = if (s) cs.onPrimaryContainer else cs.onSurfaceVariant,
                            fontWeight = if (s) FontWeight.Bold else FontWeight.Normal)
                    }
                }
            }
            if (scope == 0) {
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    weekdays.forEachIndexed { idx, wd ->
                        val s = weekday == idx
                        Box(Modifier.weight(1f).heightIn(min = 48.dp)
                            .background(if (s) cs.primary else cs.surfaceVariant, MaterialTheme.shapes.extraSmall)
                            .clickable { weekday = idx }, contentAlignment = Alignment.Center) {
                            Text(wd, color = if (s) cs.onPrimary else cs.onSurfaceVariant,
                                fontWeight = if (s) FontWeight.Bold else FontWeight.Normal)
                        }
                    }
                }
            }
            Text("対象 ${targetDays.size}日。既存の希望は上書き。", style = MaterialTheme.typography.labelMedium, color = cs.onSurfaceVariant)
            Text("対象（誰に）", style = MaterialTheme.typography.labelMedium, color = cs.onSurfaceVariant)
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Box(Modifier.weight(1f).heightIn(min = 48.dp)
                    .background(if (staffSel < 0) cs.primary else cs.surfaceVariant, MaterialTheme.shapes.small)
                    .clickable { staffSel = -1 }, contentAlignment = Alignment.Center) {
                    Text("全職員", color = if (staffSel < 0) cs.onPrimary else cs.onSurfaceVariant, fontWeight = FontWeight.Bold)
                }
                Box(Modifier.weight(1f).heightIn(min = 48.dp)
                    .background(if (staffSel >= 0) cs.primary else cs.surfaceVariant, MaterialTheme.shapes.small)
                    .clickable { showStaff = true }, contentAlignment = Alignment.Center) {
                    Text(if (staffSel >= 0) "職員：$targetName" else "職員を選ぶ",
                        color = if (staffSel >= 0) cs.onPrimary else cs.onSurfaceVariant, fontWeight = FontWeight.Bold)
                }
            }
            Text("希望シフト（タップで選択）", style = MaterialTheme.typography.labelMedium, color = cs.onSurfaceVariant)
            ui.shiftSymbols.indices.toList().chunked(3).forEach { rowKeys ->
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    rowKeys.forEach { k ->
                        val sel = picked == k
                        val ng = staffSel >= 0 && k !in allowed
                        Box(Modifier.weight(1f).heightIn(min = 48.dp)
                            .background(if (sel) cs.primaryContainer else cs.surface, MaterialTheme.shapes.small)
                            .border(if (sel) 2.dp else 1.dp, if (sel) cs.primary else if (ng) cs.error else cs.outline, MaterialTheme.shapes.small)
                            .clickable { picked = k }, contentAlignment = Alignment.Center) {
                            Text((ui.shiftSymbols.getOrNull(k) ?: "$k") + (if (ng) " 外" else ""),
                                color = if (ng) cs.error else cs.onSurface, fontWeight = if (sel) FontWeight.Bold else FontWeight.Normal)
                        }
                    }
                    repeat(3 - rowKeys.size) { Spacer(Modifier.weight(1f)) }
                }
            }
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                OutlinedButton(onClick = {
                    if (scope == 1 && staffSel < 0) confirmClearAll = true
                    else { vm.clearWishesForDays(if (staffSel < 0) null else staffSel, targetDays); onDismiss() }
                }, enabled = !ui.running, modifier = Modifier.weight(1f).heightIn(min = 48.dp)) {
                    Text("この範囲を希望なしに", color = cs.error)
                }
                Button(onClick = {
                    if (picked in ui.shiftSymbols.indices) {
                        vm.setWishesForDays(if (staffSel < 0) null else staffSel, targetDays, picked); onDismiss()
                    }
                }, enabled = picked in ui.shiftSymbols.indices && targetDays.isNotEmpty() && !ui.running,
                    modifier = Modifier.weight(1f).heightIn(min = 48.dp)) {
                    Text(if (ui.running) "計算中は変更できません" else "適用（${targetDays.size}件）")
                }
            }
            Text("※ 期間全体×全職員の「希望なし」は全削除（確認あり）。元に戻すで取消可。",
                style = MaterialTheme.typography.labelSmall, color = cs.onSurfaceVariant)
        }
    }
    if (showStaff) {
        AlertDialog(
            onDismissRequest = { showStaff = false },
            confirmButton = {},
            dismissButton = { DialogDismissButton(onClick = { showStaff = false }, text = "閉じる") },
            title = { DialogHeader("職員を選ぶ", { showStaff = false }) },
            text = {
                Column(Modifier.verticalScroll(rememberScrollState())) {
                    ui.staffNames.forEachIndexed { idx, n ->
                        Box(Modifier.fillMaxWidth().heightIn(min = 48.dp).clickable { staffSel = idx; showStaff = false },
                            contentAlignment = Alignment.CenterStart) {
                            Text(n, Modifier.padding(vertical = 8.dp))
                        }
                    }
                }
            },
        )
    }
    if (confirmClearAll) {
        AlertDialog(
            onDismissRequest = { confirmClearAll = false },
            confirmButton = { DialogDangerButton("すべて削除", onClick = { confirmClearAll = false; vm.clearAllWishes(); onDismiss() }) },
            dismissButton = { DialogDismissButton(onClick = { confirmClearAll = false }) },
            title = { Text("すべての希望を削除") },
            text = { Text("登録済みの希望をすべて削除します。割当には影響しません。元に戻すで復元できます。") },
        )
    }
}

/** [割当の一括操作] 対象範囲(曜日/期間全体) × 対象(全員/1名) × シフト → まとめて割当。公・休で休みも設定可。元に戻すで取消可。円柱から起動。 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
internal fun AssignBulkSheet(ui: UiState, onBulkSet: (Collection<Pair<Int, Int>>, Int) -> Unit, onDismiss: () -> Unit, canDo: (Int, Int) -> Boolean = { _, _ -> true }) {
    val cs = MaterialTheme.colorScheme
    val sheetState = rememberModalBottomSheetState()
    val days = ui.days
    val weekdays = listOf("月", "火", "水", "木", "金", "土", "日")
    val startDow = startDowMonFirst(ui.startDate)
    var scope by remember { mutableIntStateOf(1) }        // 0=この曜日, 1=期間全体
    var weekday by remember { mutableIntStateOf(0) }
    var staffSel by remember { mutableIntStateOf(-1) }    // -1=全職員, else staff index
    var picked by remember { mutableIntStateOf(-1) }      // 選択中のシフト
    var showStaff by remember { mutableStateOf(false) }
    val targetDays = if (scope == 1) (0 until days).toList()
        else (0 until days).filter { (startDow + it) % 7 == weekday }
    val targetStaff = if (staffSel >= 0) listOf(staffSel) else ui.schedule.indices.toList()
    val targetName = if (staffSel >= 0) (ui.staffNames.getOrNull(staffSel) ?: "$staffSel") else "全職員"
    // [矛盾なく選択/実機指摘] 選んだシフトを担当できない職員は対象から自動で外す（担当外の一括割当＝
    //   大量の担当外違反を作れてしまう矛盾を根元で防ぐ）。除外人数は下の対象行に明示。
    val eligibleStaff = if (picked in ui.shiftSymbols.indices) targetStaff.filter { canDo(it, picked) } else targetStaff
    val skippedStaff = targetStaff.size - eligibleStaff.size
    val cellCount = eligibleStaff.size * targetDays.size
    ModalBottomSheet(onDismissRequest = onDismiss, sheetState = sheetState) {
        Column(
            Modifier.fillMaxWidth().padding(horizontal = 20.dp).padding(bottom = 28.dp).verticalScroll(rememberScrollState()),
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            DialogHeader("割当の一括操作", onDismiss)
            Text("対象範囲", style = MaterialTheme.typography.labelMedium, color = cs.onSurfaceVariant)
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                listOf("この曜日", "期間全体").forEachIndexed { idx, label ->
                    val s = scope == idx
                    Box(Modifier.weight(1f).heightIn(min = 48.dp)
                        .background(if (s) cs.primaryContainer else cs.surfaceVariant, MaterialTheme.shapes.small)
                        .clickable { scope = idx }, contentAlignment = Alignment.Center) {
                        Text(label, color = if (s) cs.onPrimaryContainer else cs.onSurfaceVariant,
                            fontWeight = if (s) FontWeight.Bold else FontWeight.Normal)
                    }
                }
            }
            if (scope == 0) {
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    weekdays.forEachIndexed { idx, wd ->
                        val s = weekday == idx
                        Box(Modifier.weight(1f).heightIn(min = 48.dp)
                            .background(if (s) cs.primary else cs.surfaceVariant, MaterialTheme.shapes.extraSmall)
                            .clickable { weekday = idx }, contentAlignment = Alignment.Center) {
                            Text(wd, color = if (s) cs.onPrimary else cs.onSurfaceVariant,
                                fontWeight = if (s) FontWeight.Bold else FontWeight.Normal)
                        }
                    }
                }
            }
            Text("対象（誰に）", style = MaterialTheme.typography.labelMedium, color = cs.onSurfaceVariant)
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Box(Modifier.weight(1f).heightIn(min = 48.dp)
                    .background(if (staffSel < 0) cs.primary else cs.surfaceVariant, MaterialTheme.shapes.small)
                    .clickable { staffSel = -1 }, contentAlignment = Alignment.Center) {
                    Text("全職員", color = if (staffSel < 0) cs.onPrimary else cs.onSurfaceVariant, fontWeight = FontWeight.Bold)
                }
                Box(Modifier.weight(1f).heightIn(min = 48.dp)
                    .background(if (staffSel >= 0) cs.primary else cs.surfaceVariant, MaterialTheme.shapes.small)
                    .clickable { showStaff = true }, contentAlignment = Alignment.Center) {
                    Text(if (staffSel >= 0) "職員：$targetName" else "職員を選ぶ",
                        color = if (staffSel >= 0) cs.onPrimary else cs.onSurfaceVariant, fontWeight = FontWeight.Bold)
                }
            }
            Text("対象 ${eligibleStaff.size}名 × ${targetDays.size}日 = ${cellCount}マス。既存の割当は上書き。" +
                (if (skippedStaff > 0) "（担当外 ${skippedStaff}名は対象外）" else ""),
                style = MaterialTheme.typography.labelMedium, color = cs.onSurfaceVariant)
            // [文言修正] 旧「公・休で…」の「公」はデータに存在しない記号だった（存在しない項目を語らない）。
            Text("シフト（タップで選択）", style = MaterialTheme.typography.labelMedium, color = cs.onSurfaceVariant)
            ui.shiftSymbols.indices.toList().chunked(3).forEach { rowKeys ->
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    rowKeys.forEach { k ->
                        val sel = picked == k
                        Box(Modifier.weight(1f).heightIn(min = 48.dp)
                            .background(if (sel) cs.primaryContainer else cs.surface, MaterialTheme.shapes.small)
                            .border(if (sel) 2.dp else 1.dp, if (sel) cs.primary else cs.outline, MaterialTheme.shapes.small)
                            .clickable { picked = k }, contentAlignment = Alignment.Center) {
                            Text(ui.shiftSymbols.getOrNull(k) ?: "$k",
                                color = ensureReadable(if (sel) cs.primaryContainer else cs.surface, hexToColor(ui.shiftTextHex.getOrNull(k) ?: "")), fontWeight = if (sel) FontWeight.Bold else FontWeight.Normal)
                        }
                    }
                    repeat(3 - rowKeys.size) { Spacer(Modifier.weight(1f)) }
                }
            }
            Button(onClick = {
                if (picked in ui.shiftSymbols.indices && cellCount > 0) {
                    val cells = eligibleStaff.flatMap { i -> targetDays.map { j -> i to j } }
                    onBulkSet(cells, picked); onDismiss()
                }
            }, enabled = picked in ui.shiftSymbols.indices && cellCount > 0 && !ui.running,
                modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp)) {
                // [矛盾なく選択] 押せない理由をボタン自身が語る（灰色の理由が見えない矛盾を解消）。
                // [監査#1] 実行中は適用不可（最適化完了時に上書きされ黙って消えるため）。
                Text(when {
                    ui.running -> "計算中は変更できません"
                    picked !in ui.shiftSymbols.indices -> "まずシフトを選んでください"
                    cellCount == 0 -> "対象がありません（担当できる職員なし）"
                    else -> "この${cellCount}マスに一括割当"
                })
            }
            Text("※ 選択したマスを上書きします。元に戻すで取消可。", style = MaterialTheme.typography.labelSmall, color = cs.onSurfaceVariant)
        }
    }
    if (showStaff) {
        AlertDialog(
            onDismissRequest = { showStaff = false },
            confirmButton = {},
            dismissButton = { DialogDismissButton(onClick = { showStaff = false }, text = "閉じる") },
            title = { DialogHeader("職員を選ぶ", { showStaff = false }) },
            text = {
                Column(Modifier.verticalScroll(rememberScrollState())) {
                    ui.staffNames.forEachIndexed { idx, n ->
                        Box(Modifier.fillMaxWidth().heightIn(min = 48.dp).clickable { staffSel = idx; showStaff = false },
                            contentAlignment = Alignment.CenterStart) {
                            Text(n, Modifier.padding(vertical = 8.dp))
                        }
                    }
                }
            },
        )
    }
}

/** 期間開始日 + j(0始まり) → "M/D"。解析失敗時は "(j+1)日"。 */
internal fun dayMD(startDate: String, j: Int): String = try {
    val d = LocalDate.parse(startDate).plusDays(j.toLong())
    "${d.monthValue}/${d.dayOfMonth}"
} catch (e: Exception) { "${j + 1}日" }

// [3.194.0 情報の冗長性検証] MismatchExtractCard（不一致だけ抽出）を撤去。TallyCard(職員別/日別)の
//   ▼▲バッジ・ScheduleGridの人員不足バナー/桃バッジと表示が重複しており、かつ apt(適切回数)由来の
//   違反を含まないため新しい表示より不完全だった（呼出0を確認済み）。

// ============================================================================
// 大規模UI改良: ユニバーサルデザイン + スマホ特化シェル (ボトムナビ + ステータスヒーロー)
// ============================================================================

// ============================================================================
// [集計] 各職員・各日のシフト集計（Excel版の右側=職員別 / 下側=日別 を再現）
// 表示中スケジュール(gridUi)から countMatrix / coverage 相当を算出して表で示す。
// 片手一本指: 横スクロール（rememberScrollState）でシフト列/日列を送る。
// ============================================================================
@Composable
internal fun TallyCard(ui: UiState, vm: MagiViewModel, onFix: (Int?, Int?) -> Unit = { _, _ -> }, vioEnabled: Set<String> = allVioBucketKeys) {
    val k = ui.shiftSymbols.size
    val s = ui.schedule.size
    val t = ui.days
    if (s == 0 || k == 0 || t == 0) return
    val cs = MaterialTheme.colorScheme
    // [直せる導線] 違反セルをタップ→原因(必要/下限/上限/目標 と現在)を数字で提示し「直し方を探す」へ。
    var detail by remember { mutableStateOf<TallyDetailUi?>(null) }
    // 職員別: perStaff[i][k] = スタッフ i がシフト k を担当した回数
    val perStaff = remember(ui.schedule, k) {
        Array(s) { i -> IntArray(k).also { c -> ui.schedule[i].forEach { v -> if (v in 0 until k) c[v]++ } } }
    }
    // 日別: perDay[j][k] = 日 j にシフト k へ配置された人数
    val perDay = remember(ui.schedule, k, t) {
        Array(t) { j -> IntArray(k).also { c -> for (i in 0 until s) { val v = ui.schedule[i].getOrNull(j) ?: -1; if (v in 0 until k) c[v]++ } } }
    }
    // 違反ハイライト色（Excel版の色分けに対応）: 不足=赤 / 過剰=橙。
    // 職員別は countViolations["i,k"](vio-low/vio-high=人数範囲)、日別は needViolations["k,j"](vio-covU/vio-covO=被覆)で判定。
    // [M6統一] 不足=vioColor(ユーザー設定色に連動・既定 赤)、超過=橙。グリッド/ヒートバーと同じ2色言語。
    val critC = ui.violationColorHex.takeIf { it.isNotBlank() }?.let { hexToColor(it) } ?: MagiAccent.red
    // [M2] 塗り飽和度を上げ暗テーマ・屋外グレアでの視認性を確保（0.30/0.36→0.45/0.50）。
    //   数字は太字化済(第5段)のため濃い塗りでも可読。
    val shortBg = critC.copy(alpha = 0.45f)
    // [文言整合監査] 超過/過剰の地色も要調整トークン(__vioSoft__)に追従（グリッドと同じ色言語）。
    val overBg = (ui.violationSoftColorHex.takeIf { it.isNotBlank() }?.let { hexToColor(it) } ?: MagiAccent.orange).copy(alpha = 0.50f)
    var mode by rememberSaveable { mutableStateOf(0) }   // 0=職員別 / 1=日別
    // [シンプルデザイン融合②] 集計期間の read-only ラベル（曜日付き）。startDate〜startDate+(days-1)。
    //   月スナップショットモデルのため <> ナビは付けない（集計は常に現在の全期間）。パース失敗時は非表示。
    val periodLabel = remember(ui.startDate, ui.days) {
        runCatching {
            val wk = listOf("月", "火", "水", "木", "金", "土", "日")
            fun fmt(d: LocalDate) = "${d.year}年${d.monthValue}月${d.dayOfMonth}日(${wk[d.dayOfWeek.value - 1]})"
            val s0 = LocalDate.parse(ui.startDate)
            "集計期間 ${fmt(s0)}〜${fmt(s0.plusDays((ui.days - 1).toLong()))}"
        }.getOrNull()
    }
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp)) {
            Text("シフト集計", style = MaterialTheme.typography.titleMedium)
            Spacer(Modifier.height(8.dp))
            MagiSegmentedControl(options = listOf("職員別", "日別"), selected = mode, onSelect = { mode = it })
            Spacer(Modifier.height(8.dp))
            periodLabel?.let {
                Text(it, style = MaterialTheme.typography.labelMedium, color = cs.onSurfaceVariant)
                Spacer(Modifier.height(8.dp))
            }
            if (mode == 0) {
                // [P7/実務者向け短文化] 「職員別」タブ名＋表そのもので内容は自明。残すのは操作の含意だけ。
                Text("ⓘ タップで内訳と直し方",
                    style = MaterialTheme.typography.labelMedium, color = cs.onSurfaceVariant)
                // [一括修正] 職員別の赤/橙は low/high(上下限)だけでなく aptLow/aptHigh(適切回数=目標)も同色マーク
                //   のため、凡例に「目標」を含める（旧「上限超過」だけでは 美幸B4=目標超過 の橙が読めなかった）。
                TallyLegend(shortBg, overBg)
                Spacer(Modifier.height(8.dp))
                val labW = 100.dp; val cw = 48.dp; val rh = 48.dp // [a11y] 集計セル 40x34 -> 48x48（違反セルはタップ可のため）
                Row {
                    Column {
                        TallyBox(labW, rh, cs.surfaceVariant, false) {
                            Text("職員", style = MaterialTheme.typography.labelMedium, color = cs.onSurfaceVariant, maxLines = 1)
                        }
                        for (i in 0 until s) TallyBox(labW, rh, cs.surfaceVariant, true) {
                            val nm = ui.staffNames.getOrNull(i) ?: "$i"
                            val gp = ui.staffGroupSymbols.getOrNull(i) ?: ""
                            Text(if (gp.isBlank()) nm else "$nm·$gp", style = MaterialTheme.typography.bodySmall,
                                color = cs.onSurface, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        }
                        // [D1] 期間合計の見出し（勤務表グリッドの「シフト別の合計」をここへ一本化）。
                        TallyBox(labW, rh, cs.surfaceVariant, true) {
                            Text("計（期間）", style = MaterialTheme.typography.labelMedium, color = cs.onSurfaceVariant, maxLines = 1)
                        }
                    }
                    Row(Modifier.horizontalScroll(rememberScrollState())) {
                        for (kk in 0 until k) Column {
                            val bg = tallyHex(ui.shiftColorHex.getOrNull(kk)) ?: cs.surfaceVariant
                            val fg = ensureReadable(bg, tallyHex(ui.shiftTextHex.getOrNull(kk)) ?: cs.onSurfaceVariant)
                            TallyBox(cw, rh, bg, false) {
                                Text(ui.shiftSymbols[kk], style = MaterialTheme.typography.labelMedium, fontWeight = FontWeight.Bold, color = fg, maxLines = 1)
                            }
                            for (i in 0 until s) {
                                val v = perStaff[i][kk]
                                // [E7] 回数(low/high/apt/c2)バケツOFF時はこのセルの違反表示を抑止（値は表示・色/枠だけ消す）。
                                val vio = ui.countViolations["$i,$kk"]?.takeIf { vioVisible(it, vioEnabled) }
                                // [レイアウト/実機指摘] 0セルは旧 cs.surface(UDで真っ白)＝表に白い穴が空いて見えた。
                                //   淡い同系色に沈めて「数字のあるセルが浮かぶ」市松を解消。
                                val cbg = when (vio) { "vio-low", "vio-aptLow" -> shortBg; "vio-high", "vio-aptHigh" -> overBg; else -> if (v == 0) cs.surfaceVariant.copy(alpha = 0.35f) else cs.surfaceVariant }
                                // [M3 色覚安全] 不足=▼ / 超過=▲ を数字に前置＝色に依らず方向が判る（色覚多様性・モノクロ印刷対応）。
                                val glyph = when (vio) { "vio-low", "vio-aptLow" -> "▼"; "vio-high", "vio-aptHigh" -> "▲"; else -> "" }
                                val cellCd = if (vio != null) {
                                    val dir = when (vio) { "vio-low", "vio-aptLow" -> "不足"; else -> "超過" }
                                    "${ui.staffNames.getOrNull(i) ?: i} 「${ui.shiftSymbols.getOrNull(kk) ?: kk}」 ${v}回 $dir・タップで詳細"
                                } else null
                                TallyBox(cw, rh, cbg, false, onClick = if (vio != null) ({ detail = staffViolDetail(vm, ui, i, kk, v, vio) }) else null, cd = cellCd) {
                                    if (v != 0 || vio != null) Text("$glyph$v", style = MaterialTheme.typography.bodySmall, color = cs.onSurface, fontWeight = if (vio != null) FontWeight.Bold else FontWeight.Normal, maxLines = 1)
                                }
                            }
                            // [D1] シフト別の期間合計（列合計）。グリッドの重複行を廃止しここへ集約。
                            TallyBox(cw, rh, cs.surfaceVariant, false) {
                                Text("${(0 until s).sumOf { perStaff[it][kk] }}", style = MaterialTheme.typography.bodySmall, fontWeight = FontWeight.Bold, color = cs.onSurface)
                            }
                        }
                    }
                }
            } else {
                // [P7/実務者向け短文化] 「日別」タブ名＋表で自明。日別は横スクロール（他の日）の含意も添える。
                Text("ⓘ タップで内訳と直し方 ・ 👆 左右スワイプで他の日",
                    style = MaterialTheme.typography.labelMedium, color = cs.onSurfaceVariant)
                TallyLegend(shortBg, overBg)
                Spacer(Modifier.height(8.dp))
                val labW = 84.dp; val cw = 48.dp; val rh = 48.dp // [a11y] 日別集計セル 34x34 -> 48x48
                Row {
                    Column {
                        TallyBox(labW, rh, cs.surfaceVariant, false) {
                            Text("シフト", style = MaterialTheme.typography.labelMedium, color = cs.onSurfaceVariant, maxLines = 1)
                        }
                        for (kk in 0 until k) {
                            // [レイアウト/実機指摘] 全日0のシフト行（未使用シフト）はラベルも淡色に沈め、
                            //   使っている行の模様を浮かび上がらせる（行は消さない＝存在は読める）。
                            val rowZero = (0 until t).all { perDay[it][kk] == 0 }
                            val bg0 = tallyHex(ui.shiftColorHex.getOrNull(kk)) ?: cs.surfaceVariant
                            val bg = if (rowZero) bg0.copy(alpha = 0.35f) else bg0
                            val fg = if (rowZero) cs.onSurfaceVariant else ensureReadable(bg0, tallyHex(ui.shiftTextHex.getOrNull(kk)) ?: cs.onSurfaceVariant)
                            TallyBox(labW, rh, bg, true) {
                                Text(ui.shiftSymbols[kk], style = MaterialTheme.typography.bodySmall, fontWeight = if (rowZero) FontWeight.Normal else FontWeight.Bold, color = fg, maxLines = 1)
                            }
                        }
                    }
                    Row(Modifier.horizontalScroll(rememberScrollState())) {
                        for (j in 0 until t) Column {
                            TallyBox(cw, rh, cs.surfaceVariant, false) {
                                Text("${j + 1}", style = MaterialTheme.typography.labelMedium, color = cs.onSurfaceVariant, maxLines = 1)
                            }
                            for (kk in 0 until k) {
                                val v = perDay[j][kk]
                                // [E7] 人員(covU/covO)バケツOFF時はこの日セルの違反表示を抑止（値は表示・色/枠だけ消す）。
                                val vio = ui.needViolations["$kk,$j"]?.takeIf { vioVisible(it, vioEnabled) }
                                // [レイアウト/実機指摘] 0セルは白い穴に見えるため淡色へ（職員別と同じ）。
                                val cbg = when (vio) { "vio-covU" -> shortBg; "vio-covO" -> overBg; else -> if (v == 0) cs.surfaceVariant.copy(alpha = 0.35f) else cs.surfaceVariant }
                                // [M3 色覚安全] 人員不足=▼ / 過剰=▲ を数字に前置。色に依らず方向が判る。
                                val glyph = when (vio) { "vio-covU" -> "▼"; "vio-covO" -> "▲"; else -> "" }
                                val cellCd = if (vio != null) {
                                    val dir = if (vio == "vio-covU") "人員不足" else "人員過剰"
                                    "${j + 1}日 「${ui.shiftSymbols.getOrNull(kk) ?: kk}」 ${v}人 $dir・タップで詳細"
                                } else null
                                TallyBox(cw, rh, cbg, false, onClick = if (vio != null) ({ detail = dayViolDetail(vm, ui, kk, j, v, vio) }) else null, cd = cellCd) {
                                    if (v != 0 || vio != null) Text("$glyph$v", style = MaterialTheme.typography.bodySmall, color = cs.onSurface, fontWeight = if (vio != null) FontWeight.Bold else FontWeight.Normal, maxLines = 1)
                                }
                            }
                        }
                    }
                }
            }
            detail?.let { d ->
                AlertDialog(
                    onDismissRequest = { detail = null },
                    title = { Text(d.title) },
                    text = {
                        Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                            d.lines.forEach { Text(it, style = MaterialTheme.typography.bodyMedium) }
                        }
                    },
                    confirmButton = {
                        DialogConfirmButton("直し方を探す", onClick = { val f = d.focus; val sh = d.shift; detail = null; onFix(f, sh) })
                    },
                    dismissButton = { DialogDismissButton(onClick = { detail = null }, text = "閉じる") },
                )
            }
        }
    }
}

/** [直せる導線] 集計セルの違反詳細。focus=直す対象スタッフ(日別はnull=全体探索)。 */
private data class TallyDetailUi(val title: String, val lines: List<String>, val focus: Int?, val shift: Int? = null)

/** 職員別セル(i,k): 現在回数と 下限/上限/目標 の差を数字で。 */
private fun staffViolDetail(vm: MagiViewModel, ui: UiState, i: Int, k: Int, count: Int, vio: String): TallyDetailUi {
    val (lo, hi, apt) = vm.staffCellLimits(i, k)
    val name = ui.staffNames.getOrNull(i) ?: "$i"
    val sym = ui.shiftSymbols.getOrNull(k) ?: "$k"
    val lines = ArrayList<String>()
    lines += "現在 ${count}回"
    when (vio) {
        "vio-low" -> if (lo != null) lines += "下限 ${lo}回 → ${(lo - count).coerceAtLeast(0)}回 不足"
        "vio-high" -> if (hi != null) lines += "上限 ${hi}回 → ${(count - hi).coerceAtLeast(0)}回 超過"
        "vio-aptLow" -> if (apt != null) lines += "目標 ${apt}回 → ${(apt - count).coerceAtLeast(0)}回 不足"
        "vio-aptHigh" -> if (apt != null) lines += "目標 ${apt}回 → ${(count - apt).coerceAtLeast(0)}回 超過"
    }
    return TallyDetailUi("$name ・ $sym", lines, i, k)
}

/** 日別セル(k,j): 現在人数と 必要数レンジ の差を数字で。 */
private fun dayViolDetail(vm: MagiViewModel, ui: UiState, k: Int, j: Int, count: Int, vio: String): TallyDetailUi {
    val limits = vm.needCellLimits(k, j)
    val sym = ui.shiftSymbols.getOrNull(k) ?: "$k"
    val lines = ArrayList<String>()
    lines += "現在 ${count}人"
    if (limits != null) {
        val (lo, hi) = limits
        when (vio) {
            "vio-covU" -> lines += "必要 ${lo}人 → ${(lo - count).coerceAtLeast(0)}人 不足"
            "vio-covO" -> lines += "適正 ${hi}人 → ${(count - hi).coerceAtLeast(0)}人 過剰"
        }
    }
    return TallyDetailUi("$sym ・ ${j + 1}日", lines, null, k)
}

private fun tallyHex(hex: String?): Color? = if (hex.isNullOrBlank()) null else hexToColor(hex)

/** シフト集計の違反ハイライト凡例（不足=赤 / 過剰=橙）。 */
@Composable
private fun TallyLegend(shortBg: Color, overBg: Color) {
    // [シンプルデザイン融合] スクショの1行凡例に統一: ▼不足 ▲超過 — 対象外。
    //   タブ名（職員別/日別）が不足/超過の意味を文脈で示すため、旧・長文ラベル
    //   （回数が下限/目標未満 等）は撤去。詳細はセルのタップで出す。色見本＋▼▲＝色覚二重符号化。
    val cs = MaterialTheme.colorScheme
    Spacer(Modifier.height(6.dp))
    Row(verticalAlignment = Alignment.CenterVertically) {
        Box(Modifier.size(13.dp).background(shortBg, RoundedCornerShape(3.dp)))
        Spacer(Modifier.width(4.dp))
        Text("▼ 不足", style = MaterialTheme.typography.labelSmall, color = cs.onSurfaceVariant)
        Spacer(Modifier.width(14.dp))
        Box(Modifier.size(13.dp).background(overBg, RoundedCornerShape(3.dp)))
        Spacer(Modifier.width(4.dp))
        Text("▲ 超過", style = MaterialTheme.typography.labelSmall, color = cs.onSurfaceVariant)
        Spacer(Modifier.width(14.dp))
        Text("— 対象外", style = MaterialTheme.typography.labelSmall, color = cs.onSurfaceVariant)
    }
}

@Composable
private fun TallyBox(
    w: androidx.compose.ui.unit.Dp, h: androidx.compose.ui.unit.Dp, bg: Color, start: Boolean,
    onClick: (() -> Unit)? = null,
    cd: String? = null,
    content: @Composable () -> Unit,
) {
    Box(Modifier.width(w).height(h).padding(1.dp)) {
        Box(
            Modifier.fillMaxSize().background(bg, RoundedCornerShape(8.dp))
                .then(if (onClick != null) Modifier.clickable(onClick = onClick) else Modifier)
                // [a11y/B1] 違反セルは数字だけでは読み上げが「9」等になり文脈が失われる。渡された時のみ
                //   「氏名 シフト N回 不足」のような説明を公開（タップ先の詳細と同義）。非違反セルは cd=null で無音のまま。
                .then(if (cd != null) Modifier.semantics { contentDescription = cd } else Modifier)
                .then(if (start) Modifier.padding(horizontal = 6.dp) else Modifier),
            contentAlignment = if (start) Alignment.CenterStart else Alignment.Center,
        ) { content() }
    }
}

// ===== 平面グリッド（円柱インターフェース置き換え）=====
// フィッシュアイ(円柱)をやめ、均一セルのスプレッドシート型に。名前列固定・横スクロールで日移動。
// 歪みなし＝全職員×全日で記号/違反が明瞭（周辺日の潰れを構造的に解消）。Composeネイティブでタップ/スクロール。
@Composable
internal fun MagiFlatGrid(ui: UiState, onCellClick: (Int, Int) -> Unit, vioEnabled: Set<String> = allVioBucketKeys, hScroll: ScrollState = rememberScrollState(), nameQuery: String = "", cellW: androidx.compose.ui.unit.Dp = 48.dp, focusCell: Pair<Int, Int>? = null, focusRange: Triple<Int, Int, Int>? = null, focusMode: Boolean = false, canDo: (Int, Int) -> Boolean = { _, _ -> true }) {
    val cs = MaterialTheme.colorScheme
    val days = ui.days.coerceAtLeast(1)
    val staffCount = ui.schedule.size
    if (staffCount == 0) { Text("勤務表データがありません。", color = cs.onSurfaceVariant); return }
    // [週ページング] 全日を横スクロールで保持しつつ（併用）、外部 hScroll を受けて 前週/次週 でジャンプできる。
    val vioColor = ui.violationColorHex.takeIf { it.isNotBlank() }?.let { hexToColor(it) } ?: cs.error
    // [色変更] 要調整(ソフト)色はトークン __vioSoft__（設定→違反種別の色）から解決。空=既定の橙。
    val vioSoftColor = ui.violationSoftColorHex.takeIf { it.isNotBlank() }?.let { hexToColor(it) } ?: MagiAccent.orange
    val shiftColorsC = remember(ui.shiftColorHex) { ui.shiftColorHex.map { hexToColor(it) } }
    val shiftTextC = remember(ui.shiftTextHex) { ui.shiftTextHex.map { hexToColor(it) } }
    val sdow = startDowMonFirst(ui.startDate)
    val weekdayJa = listOf("月", "火", "水", "木", "金", "土", "日")
    val todayIdx = remember(ui.startDate, days) {
        runCatching {
            val off = (LocalDate.now().toEpochDay() - LocalDate.parse(ui.startDate).toEpochDay()).toInt()
            if (off in 0 until days) off else -1
        }.getOrDefault(-1)
    }
    // [E7] 種別フィルタ: バケツOFFのセル違反は枠を出さない（vioVisible=false→0）。表示のみ・違反自体は不変。
    // [判読性] 0=なし / 1=必須(実線) / 2=重いソフト(破線) / 3=軽いソフト(右上角マーク)。
    //   従来は全ソフトが太い破線枠＝数百件で格子が警告に飽和し、必須違反1件が埋没していた。
    // [Set化] 表示中(フィルタ通過)の最重クラス。段階(vioKind)と族別色(3.122.0)の両方の源泉。
    val vioCls = remember(ui.violationCells, ui.violationCellFamilies, staffCount, days, vioEnabled) {
        Array(staffCount) { i -> Array(days) { d -> visibleCellVio(ui, "$i,$d", vioEnabled) } }
    }
    val vioKind = remember(vioCls) {
        Array(staffCount) { i -> IntArray(days) { d ->
            val v = vioCls[i][d]
            when { v == null -> 0; isHardCellViolation(v) -> 1; isHeavySoftCellViolation(v) -> 2; else -> 3 }
        } }
    }
    // [整合性修正/情報の冗長性検証] チェッカーの pref 判定（MirrorCore.kt）は「実現可能な希望
    // （canDo）の未充足のみ」を違反として数える（担当不可の不可能希望は対称除外＝別途「実現できない希望」
    // 警告が案内）。旧実装はここで wish!=schedule のみ比較しており canDo を見ていなかったため、実現不可能な
    // 希望まで「未反映（直せる）」として桃バッジ表示していた＝チェッカーとの不整合。canDo を通し、実現不可能な
    // 希望はバッジ0（無し）にしてチェッカーの pref 判定と意味を一致させる。
    val wishKind = remember(ui.wishes, ui.schedule, staffCount, days) {
        Array(staffCount) { i -> IntArray(days) { d ->
            val wk = ui.wishes["$i,$d"]
            if (wk == null || !canDo(i, wk)) 0
            else { val k = ui.schedule.getOrNull(i)?.getOrNull(d) ?: -1; if (wk == k) 1 else 2 }
        } }
    }
    // [判読性] 休セルは淡色＋細字で視覚的に後退させ、勤務セルの模様（誰がいつ働くか）を浮かび上がらせる。
    //   記号「休」から解決（改名データでは -1=後退なし＝従来表示）。色データ・スコアリング不変。
    val restIdx = remember(ui.shiftSymbols) { ui.shiftSymbols.indexOfFirst { it.trim() == "休" } }
    // [グループ色帯/Web試作の移植] 名前列の左端4dpにグループ色の帯。行の視線追跡と所属の一目把握を助ける。
    //   色は群の出現順に黄金角で自動割当（設定不要・群1つなら実質無地）。
    val groupOrder = remember(ui.staffGroupSymbols) { ui.staffGroupSymbols.distinct() }
    // [悲観検証P2/フォント拡大] 記号はセル幅への物理フィット優先（cellW×0.40・上限15dp を dp→sp 変換）。
    //   端末のフォント拡大(1.3x等)で 15sp→19.5dp となり全角2文字がセル(36〜48dp)からクリップして
    //   記号が誤読になる（Dﾃ→D）のを防ぐ。可読の代替は contentDescription と編集シート（通常どおり拡大）。
    val symFontSize = with(LocalDensity.current) { minOf(cellW * 0.40f, 15.dp).toSp() }
    val headFontSize = with(LocalDensity.current) { 12.dp.toSp() }   // 曜日/▼N も同方針で列幅フィット
    val dayVioH = remember(vioKind) { IntArray(days) { d -> (0 until staffCount).count { vioKind[it][d] == 1 } } }
    val dayVioS = remember(vioKind) { IntArray(days) { d -> (0 until staffCount).count { vioKind[it][d] >= 2 } } }
    val dayShort = remember(ui.v6, days) { IntArray(days) { d -> ui.v6?.dayRisks?.getOrNull(d)?.shortage ?: 0 } }

    // [a11y] 生の Box.clickable セルは M3 の 48dp タッチ補完が効かないため、主操作セルの高さは 48dp を維持。
    //   幅は「7日間表示」の明示要件でセル幅を 36〜48dp に可変化（36×48dp = タッチ面は縦方向で確保・片手一本指仕様）。
    // [レイアウト整合] headH は 日番号+曜日+不足+下線 の3行分。端末フォント拡大(≥1.3x)でも下線/数字が欠けないよう 72dp。
    //   nameW は 4文字名(拡大時)が省略されないよう 80dp。headH は共有定数なので氏名列ヘッダと連動＝崩れなし。
    // [7日間表示] cellW は ScheduleGrid が「1週間が収まる幅」を動的計算して注入（既定48dp=単独利用時）。
    val nameW = 80.dp; val cellH = 48.dp; val headH = 72.dp
    Column {
        // [P7/実務者向け短文化] スクロール・週送り・土日色・休の淡色は操作/見た目から自明のため説明しない。
        //   常時可視で必要なのは違反枠の読み方だけ（詳細凡例は「検索・凡例」内）。
        Text("タップで修正。違反枠: 実線=必須 ・ 破線=重 ・ 右上角=軽。希望: 桃バッジ=未反映 ・ 緑リング=反映済み",
            style = MaterialTheme.typography.labelMedium, color = cs.onSurfaceVariant)
        Spacer(Modifier.height(8.dp))
        Row {
            Column {
                Box(Modifier.width(nameW).height(headH), contentAlignment = Alignment.CenterStart) {
                    Text("職員", style = MaterialTheme.typography.labelMedium, color = cs.onSurfaceVariant)
                }
                for (i in 0 until staffCount) {
                    Row(Modifier.width(nameW).height(cellH).padding(end = 4.dp), verticalAlignment = Alignment.CenterVertically) {
                        // [グループ色帯] 左端4dp=所属グループ色（出現順に黄金角で自動割当）。行追跡の視線ガイド兼用。
                        val gi = groupOrder.indexOf(ui.staffGroupSymbols.getOrNull(i) ?: "").coerceAtLeast(0)
                        Box(Modifier.width(4.dp).height(cellH - 12.dp)
                            .background(Color.hsv(((gi * 137) % 360).toFloat(), 0.40f, 0.72f), RoundedCornerShape(2.dp)))
                        Spacer(Modifier.width(4.dp))
                        // [検索] 一致する職員名を太字＋青で強調（行は隠さず＝被覆の文脈を保つ）。
                        val nm = ui.staffNames.getOrNull(i) ?: "$i"
                        val hit = nameQuery.isNotBlank() && nm.contains(nameQuery, ignoreCase = true)
                        Text(nm, style = MaterialTheme.typography.bodySmall, color = if (hit) MagiAccent.blue else cs.onSurface,
                            fontWeight = if (hit) FontWeight.Bold else FontWeight.Normal, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    }
                }
            }
            Row(Modifier.horizontalScroll(hScroll)) {
                for (d in 0 until days) {
                    val dow = (sdow + d) % 7
                    // [UD監査] 今日マーカーの緑(3.4:1)は白地で不足→ tertiary(濃緑ロール)へ。
                    val dcol = when { d == todayIdx -> cs.tertiary; dow == 5 -> MagiAccent.blue; dow == 6 -> MagiAccent.red; else -> cs.onSurfaceVariant }
                    val hc = when { dayVioH[d] > 0 -> vioColor; dayVioS[d] > 0 -> vioSoftColor; else -> null }
                    // [⑥日別ジャンプ] 要確認一覧の日別項目(人員/群レンジ)から来たとき、日ヘッダを primary 枠で注目表示
                    //   （focusCell.first=-1 は「日のみ注目」＝どの行セルにも一致しない番兵）。約2.5秒で自動解除。
                    val dayFocused = focusCell != null && focusCell.first < 0 && focusCell.second == d
                    Column {
                        Column(Modifier.width(cellW).height(headH)
                            .then(if (dayFocused) Modifier.border(3.dp, cs.primary, RoundedCornerShape(6.dp)) else Modifier),
                            horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.Center) {
                            Text("${d + 1}", style = MaterialTheme.typography.labelMedium, color = dcol, fontWeight = if (d == todayIdx) FontWeight.Bold else FontWeight.Normal, maxLines = 1)
                            // [a11y] 荷重情報の「▼N」は別行の赤字バッジに分離（曜日と混ざって潰れないように）。
                            Text(weekdayJa[dow], fontSize = headFontSize, color = dcol, maxLines = 1)
                            // [E7] 「▼N」(人員不足)は covU 由来なので 人員バケツON時のみ表示（種別フィルタと整合）。
                            // [悲観検証P2+P7] 旧「不足N」(4文字)はフォント拡大時に38dp列からクリップ。集計凡例と
                            //   同語彙の「▼N」(2-3文字)へ短縮し、サイズも列幅フィット(dp→sp)に。
                            if (dayShort[d] > 0 && "need" in vioEnabled) Text("▼${dayShort[d]}", fontSize = headFontSize, color = cs.error, fontWeight = FontWeight.Bold, maxLines = 1)
                            if (hc != null) Box(Modifier.width(cellW - 10.dp).height(2.5.dp).background(hc, RoundedCornerShape(2.dp)))
                            else Spacer(Modifier.height(2.5.dp))
                        }
                        for (i in 0 until staffCount) {
                            val k = ui.schedule.getOrNull(i)?.getOrNull(d) ?: -1
                            val isRest = k >= 0 && k == restIdx
                            val rawBg = if (k < 0) cs.surfaceVariant else (shiftColorsC.getOrNull(k) ?: cs.surfaceVariant)
                            val sym = ui.shiftSymbols.getOrNull(k) ?: ""
                            val vk = vioKind[i][d]; val wkk = wishKind[i][d]
                            val cellFocused = (focusCell?.first == i && focusCell.second == d) ||
                                (focusRange != null && focusRange.first == i && d >= focusRange.second && d <= focusRange.third)
                            // [集中モード] 違反・未反映希望・注目セル以外を淡色に沈める（非表示にはしない＝被覆の文脈は残す）。
                            val quiet = focusMode && vk == 0 && wkk != 2 && !cellFocused
                            // [判読性] 休は淡色化して後退（勤務セルが浮かぶ）。文字は onSurfaceVariant で可読性を担保。
                            val bg = if (isRest || quiet) rawBg.copy(alpha = 0.30f) else rawBg
                            // [コントラスト] 淡い背景に沈まないよう記号色をWCAGで保証（色データは不変）。
                            val fg = if (isRest || quiet) cs.onSurfaceVariant else ensureReadable(rawBg, shiftTextC.getOrNull(k) ?: cs.onSurface)
                            // [希望バッジ] 未反映（割付≠希望）のときは希望シフトの記号をバッジでセルに重ねる
                            //   （旧: 桃ドットのみで「何を希望していたか」が編集シートを開かないと分からなかった）。
                            val wishSym = if (wkk == 2) ui.wishes["$i,$d"]?.let { ui.shiftSymbols.getOrNull(it) } ?: "" else ""
                            val cd = "${ui.staffNames.getOrNull(i) ?: "#$i"} ${d + 1}日 ${sym.ifBlank { "なし" }}" +
                                (if (vk == 1) "・必須違反" else if (vk >= 2) "・要調整" else "") +
                                (if (wkk == 2) "・希望未反映（希望=${wishSym.ifBlank { "?" }}）" else if (wkk != 0) "・希望" else "") + "、タップで変更"
                            // [違反色/族別] このセルの表示中クラスの族色（未設定は重大度色）。枠・角マークに適用。
                            val cellVioC = vioCls[i][d]?.let { resolvedVioColor(ui, it, vioColor, vioSoftColor) }
                            FlatCell(cellW, cellH, sym, bg, fg, vk, wkk, cellVioC ?: vioColor, cellVioC ?: vioSoftColor, cd, dim = isRest || quiet, symSize = symFontSize, focused = cellFocused, wishSym = wishSym) { onCellClick(i, d) }
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun FlatCell(
    w: androidx.compose.ui.unit.Dp, h: androidx.compose.ui.unit.Dp, symbol: String,
    bg: Color, fg: Color, vk: Int, wk: Int, vioColor: Color, vioSoftColor: Color, cd: String, dim: Boolean = false,
    symSize: androidx.compose.ui.unit.TextUnit = 15.sp, focused: Boolean = false, wishSym: String = "", onClick: () -> Unit,
) {
    val cs = MaterialTheme.colorScheme
    Box(Modifier.width(w).height(h).padding(1.5.dp)) {
        Box(
            Modifier.fillMaxSize()
                .background(bg, RoundedCornerShape(6.dp))
                // [分離] 無違反セルにも微細な輪郭を付け、似た明度の隣接セルと切り分ける（違反時は違反枠が優先）。
                // [判読性] 枠は 1=実線(必須)/2=破線(重い調整)のみ。3=軽い調整は右上の角マークに落とし飽和を防ぐ。
                .then(when {
                    focused -> Modifier.border(3.dp, cs.primary, RoundedCornerShape(6.dp))   // [ジャンプ] 注目セル
                    // [枠のハロー] 違反色と同系色のセル背景でも枠が埋没しないよう surface の縁取りを敷く。
                    vk == 1 -> Modifier.violationBorder(true, vioColor, 6.dp, halo = cs.surface)
                    vk == 2 -> Modifier.violationBorder(false, vioSoftColor, 6.dp, halo = cs.surface)
                    else -> Modifier.border(1.dp, cs.outlineVariant, RoundedCornerShape(6.dp))
                })
                .clickable(onClick = onClick)
                // [a11y] 主操作セルを読み上げ対応（従来 contentDescription 無し）。氏名/日/シフト/違反/希望を1文で。
                .semantics(mergeDescendants = true) { contentDescription = cd },
            contentAlignment = Alignment.Center,
        ) {
            // [コントラスト] 記号は太字＋セル幅フィットの物理サイズ(P2)で沈み込み/クリップを防ぐ。休(dim)は細字で後退。
            if (symbol.isNotBlank()) Text(symbol, fontSize = symSize, fontWeight = if (dim) FontWeight.Normal else FontWeight.Bold, color = fg, maxLines = 1)
            // [判読性] 軽いソフト違反(vk=3)＝右上の角マーク（枠より静かな手がかり。色＋位置の二重符号化）。
            // [悲観検証P3] 9dp→12dp＋斜辺に surface のハロー縁取り。直射日光下・任意のシフト色上でも消えないように。
            if (vk == 3) {
                Box(Modifier.align(Alignment.TopEnd).padding(1.5.dp).size(12.dp).drawBehind {
                    val p = Path().apply { moveTo(0f, 0f); lineTo(size.width, 0f); lineTo(size.width, size.height); close() }
                    drawPath(p, cs.surface, style = Stroke(width = 2.dp.toPx()))
                    drawPath(p, vioSoftColor)
                })
            }
            // [希望バッジ] 未反映（割付≠希望）= 希望シフトの記号を桃色バッジで左下に重ねる（ユーザー指示。
            //   旧: 桃ドットのみで希望の中身が読めなかった）。反映済は従来どおり青緑リング（控えめ・情報は割付記号と同じ）。
            //   [コントラスト] 任意のシフト色上でも消えないよう surface のハローで縁取り。
            if (wk == 2 && wishSym.isNotBlank()) {
                Box(
                    Modifier.align(Alignment.BottomStart).padding(1.dp)
                        .background(cs.surface, RoundedCornerShape(4.dp)).padding(1.dp)
                        .background(MagiAccent.pink, RoundedCornerShape(3.dp))
                        .padding(horizontal = 2.dp),
                ) {
                    Text(wishSym, fontSize = symSize * 0.70f, fontWeight = FontWeight.Bold,
                        color = ensureReadable(MagiAccent.pink, Color(0xFFFFFFFF)), maxLines = 1)
                }
            } else if (wk != 0) {
                Box(
                    Modifier.align(Alignment.BottomStart).padding(1.5.dp).size(9.dp)
                        .background(cs.surface, RoundedCornerShape(50)).padding(1.dp)
                        .then(if (wk == 2) Modifier.background(MagiAccent.pink, RoundedCornerShape(50)) else Modifier.border(1.5.dp, cs.tertiary, RoundedCornerShape(50))),
                )
            }
        }
    }
}

