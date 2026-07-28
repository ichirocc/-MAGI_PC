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
import androidx.compose.runtime.key
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

internal fun decodeCsvBytes(bytes: ByteArray): String {
    val utf8 = runCatching {
        val dec = Charsets.UTF_8.newDecoder()
            .onMalformedInput(java.nio.charset.CodingErrorAction.REPORT)
            .onUnmappableCharacter(java.nio.charset.CodingErrorAction.REPORT)
        dec.decode(java.nio.ByteBuffer.wrap(bytes)).toString()
    }.getOrNull()
    val text = utf8 ?: runCatching { String(bytes, charset("MS932")) }.getOrElse { String(bytes, Charsets.UTF_8) }
    return text.removePrefix("﻿")
}


@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun MagiApp(vm: MagiViewModel = viewModel()) {
    val ui by vm.ui.collectAsStateWithLifecycle()
    // [保存] バックグラウンド遷移(ON_STOP/ON_PAUSE)で保留中の編集を即時永続化する。
    //   制約編集などはデバウンス保存のため、即背景化→プロセス破棄だと失われ得る。その保険。
    val lifecycleOwner = androidx.lifecycle.compose.LocalLifecycleOwner.current
    androidx.compose.runtime.DisposableEffect(lifecycleOwner, vm) {
        val observer = androidx.lifecycle.LifecycleEventObserver { _, event ->
            if (event == androidx.lifecycle.Lifecycle.Event.ON_STOP ||
                event == androidx.lifecycle.Lifecycle.Event.ON_PAUSE) {
                vm.saveNow()
            }
        }
        lifecycleOwner.lifecycle.addObserver(observer)
        onDispose { lifecycleOwner.lifecycle.removeObserver(observer) }
    }
    // [Web反映/Wake Lock] 最適化(前景)中は画面を消灯させない＝計算の中断・ライブ表示の停止を防ぐ。
    val rootView = androidx.compose.ui.platform.LocalView.current
    androidx.compose.runtime.LaunchedEffect(ui.running) { rootView.keepScreenOn = ui.running }
    val ctx = androidx.compose.ui.platform.LocalContext.current
    val scope = rememberCoroutineScope()
    val haptic = LocalHapticFeedback.current
    var editingCell by remember { mutableStateOf<Pair<Int, Int>?>(null) }
    var oneHand by rememberSaveable { mutableStateOf(false) }
    var proMode by rememberSaveable { mutableStateOf(false) }   // [プロ編集] 表示モード（false=かんたん / true=プロ）
    var editScope by rememberSaveable { mutableStateOf(0) }   // [入口4分割] 編集タブ: 0=月次条件 / 1=職員管理 / 2=年間マスター
    // [下流→上流ディープリンク] 要確認一覧「設定で直す」→ 該当職員/シフトを事前選択して開く（-1=無し・消費で戻す）。
    var deepLinkWishStaff by rememberSaveable { mutableStateOf(-1) }
    var deepLinkNeedShift by rememberSaveable { mutableStateOf(-1) }
    var wishConfirm by remember { mutableStateOf(0) } // >0: 担当外件数の確認ダイアログ表示
    var rosterCsvChoice by remember { mutableStateOf<String?>(null) } // !=null: 勤務表/希望 取込選択ダイアログ
    var pendingCsvImport by remember { mutableStateOf<String?>(null) } // !=null: 取込種別の選択ダイアログ
    var pendingExportKind by remember { mutableStateOf<String?>(null) } // staff/wishes/cons: コンポーネント別出力
    var guidedFix by remember { mutableStateOf(false) }              // [operator_ux §5] 「なおすのを手伝って」対話

    val openJsonLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.OpenDocument()
    ) { uri ->
        if (uri != null) {
            scope.launch {
                val text = withContext(Dispatchers.IO) {
                    runCatching {
                        ctx.contentResolver.openInputStream(uri)?.use {
                            it.readBytes().toString(Charsets.UTF_8)
                        }
                    }.getOrNull()
                }
                if (text != null) vm.load(text)
            }
        }
    }

    val openCsvLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.OpenDocument()
    ) { uri ->
        if (uri != null) {
            scope.launch {
                val text = withContext(Dispatchers.IO) {
                    runCatching {
                        ctx.contentResolver.openInputStream(uri)?.use { decodeCsvBytes(it.readBytes()) }
                    }.getOrNull()
                }
                if (text != null) {
                    // 取込種別はオペレーターが選択する（自動判定しない）。
                    pendingCsvImport = text
                }
            }
        }
    }

    val saveJsonLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.CreateDocument("application/json")
    ) { uri ->
        if (uri != null) {
            scope.launch {
                val json = withContext(Dispatchers.Default) { vm.exportJson() }
                if (json != null) {
                    withContext(Dispatchers.IO) {
                        runCatching {
                            ctx.contentResolver.openOutputStream(uri)?.use { it.write(json.toByteArray(Charsets.UTF_8)) }
                        }
                    }
                }
            }
        }
    }

    val saveCsvLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.CreateDocument("text/csv")
    ) { uri ->
        if (uri != null) {
            scope.launch {
                val csv = withContext(Dispatchers.Default) { vm.exportCsv() }
                if (csv != null) {
                    withContext(Dispatchers.IO) {
                        runCatching {
                            ctx.contentResolver.openOutputStream(uri)?.use {
                                // UTF-8 BOM を付与。日本の Excel は BOM 無し UTF-8 を CP932 と誤読し文字化けするため、
                                // BOM(EF BB BF) を先頭に書いて Unicode(UTF-8) と認識させる。取込側は removePrefix で BOM 除去済。
                                it.write(byteArrayOf(0xEF.toByte(), 0xBB.toByte(), 0xBF.toByte()))
                                it.write(csv.toByteArray(Charsets.UTF_8))
                            }
                        }
                    }
                }
            }
        }
    }

    val saveComponentCsvLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.CreateDocument("text/csv")
    ) { uri ->
        val kind = pendingExportKind; pendingExportKind = null
        if (uri != null && kind != null) {
            scope.launch {
                val csv = withContext(Dispatchers.Default) {
                    when (kind) {
                        "staff" -> vm.exportStaffCsv()
                        "wishes" -> vm.exportWishesCsv()
                        "cons" -> vm.exportConstraintsCsv()
                        else -> null
                    }
                }
                if (csv != null) {
                    withContext(Dispatchers.IO) {
                        runCatching {
                            ctx.contentResolver.openOutputStream(uri)?.use {
                                it.write(byteArrayOf(0xEF.toByte(), 0xBB.toByte(), 0xBF.toByte()))
                                it.write(csv.toByteArray(Charsets.UTF_8))
                            }
                        }
                    }
                }
            }
        }
    }

    val saveLogLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.CreateDocument("text/plain")
    ) { uri ->
        if (uri != null) {
            scope.launch {
                val text = withContext(Dispatchers.Default) { vm.exportLogs() }
                if (text != null) {
                    withContext(Dispatchers.IO) {
                        runCatching {
                            ctx.contentResolver.openOutputStream(uri)?.use { it.write(text.toByteArray(Charsets.UTF_8)) }
                        }
                    }
                }
            }
        }
    }

    val saveLogJsonLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.CreateDocument("application/json")
    ) { uri ->
        if (uri != null) {
            scope.launch {
                val text = withContext(Dispatchers.Default) { vm.exportLogsJson() }
                if (text != null) {
                    withContext(Dispatchers.IO) {
                        runCatching {
                            ctx.contentResolver.openOutputStream(uri)?.use { it.write(text.toByteArray(Charsets.UTF_8)) }
                        }
                    }
                }
            }
        }
    }
    val notifPermLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.RequestPermission()
    ) { /* [監査A10] 権限ダイアログの解決後に開始（許可有無に関わらず計算は継続。許可時のみ前景・完了
           通知が見える）。既許可なら即時に結果が返るため遅延なし。 */
        vm.runInBackground()
    }
    val onBgOptimize: () -> Unit = {
        notifPermLauncher.launch(android.Manifest.permission.POST_NOTIFICATIONS)
    }

    var tab by rememberSaveable { mutableStateOf(0) }
    // [ジャンプ/Web試作の移植] 要確認一覧→勤務表タブの注目セル(i,j)。表示後に自動クリア（一時ハイライト）。
    var focusCell by remember { mutableStateOf<Pair<Int, Int>?>(null) }
    // [窓ハイライト③] 編集シートを開いている間、c1/c3/c3m の違反窓・連の範囲を薄枠で示す(閉じたら消す)。
    var focusRange by remember { mutableStateOf<Triple<Int, Int, Int>?>(null) }
    val loadSample: () -> Unit = {
        scope.launch {
            val text = withContext(Dispatchers.IO) {
                runCatching {
                    val asset = runCatching { ctx.assets.open("sample_state_v6.json") }.getOrElse { ctx.assets.open("sample_state.json") }
                    asset.use { it.readBytes().toString(Charsets.UTF_8) }
                }.getOrNull()
            }
            if (text != null) vm.load(text)
        }
    }
    val openJson: () -> Unit = { openJsonLauncher.launch(arrayOf("application/json", "text/plain", "*/*")) }

    Scaffold(
        // [現在地] トップバー副題を現在タブ名に同期（従来は固定"勤務表"で「今どこ」が不明だった）。下部ナビの選択と一致。
        topBar = { MagiTopBar(ui, when (tab) { 0 -> "ホーム"; 1 -> "勤務表"; 2 -> "編集"; 3 -> "分析"; else -> "設定" }) },
        bottomBar = {
            Column {
                if (ui.loaded) BottomCommandBar(ui, vm)
                MagiBottomNav(tab) { tab = it }
            }
        },
        containerColor = MaterialTheme.colorScheme.background,
    ) { pad ->
        Column(
            Modifier
                .fillMaxSize()
                .padding(pad)
                .padding(horizontal = 16.dp)
                .padding(top = if (oneHand) 120.dp else 0.dp) // 片手モード: 内容を親指の届く下方へ
                .verticalScroll(rememberScrollState()),
            verticalArrangement = Arrangement.spacedBy(20.dp),
        ) {
            Spacer(Modifier.height(4.dp))
            if (!ui.loaded && tab != 4) {
                // [⛏6] 「新規に作る」→ 最小データで開始し、編集タブ(年次マスター)へ誘導。
                //   そこで E6 の折りたたみ節＋⛏12 の一括追加でシフト/グループ/スタッフを育てる。
                EmptyStateCard(onOpen = openJson, onSample = loadSample, onNew = { vm.initBlankState(); tab = 2; editScope = 2 })
            } else when (tab) {
                0 -> {
                    InterruptedBanner(ui, onRerun = { vm.runV6FullOptimize() }, onDismiss = { vm.dismissInterrupted() })
                    // [operator_ux §3] 思考誘導ホーム：いまの状態に応じて「次にやること」を1枚＋大ボタン1つで提示。
                    OperatorNextActionCard(
                        ui = ui,
                        onMake = { vm.runV6FullOptimize() },
                        onSmartInitial = { vm.generateSmartInitial() },
                        onStop = { vm.stop() },
                        onExport = { saveCsvLauncher.launch("magi_schedule_${System.currentTimeMillis()}.csv") },
                        onSchedule = { tab = 1 },
                        // [監査#1] 人手不足が無い必須違反(希望/禁止連続/群)では GuidedFix(不足専用)が空回りする
                        //   → 不足なし時は分析タブの修復フローへ。
                        onFix = { if (ui.coverageDiag?.shortfalls.isNullOrEmpty()) { tab = 3; vm.findFixSuggestions() } else guidedFix = true },
                        onSetup = { tab = 2 },
                    )
                    LiveScheduleCard(ui)
                    // [冗長性削減] StatusHero(状態三重表示) / SummaryCard(統計は「ようす」と重複＋開発用語) /
                    //   QuickActionGrid(下部ナビと4/6重複) は home から除外。詳細統計は「ようす」タブへ集約。
                    CopilotCard(ui, onGoEdit = { tab = 2 }, onSoftPolish = { vm.runSoftPolish() })
                    CoverageDiagnosisCard(ui)
                    // [3.280.0] 禁止連続(c3n)の「なぜ崩せないか」診断（CoverageDiag の c3n 版・c3n=0 なら非表示）。
                    ForbiddenRunDiagnosisCard(ui, onRelaxRule = { vm.relaxForbiddenRule(it) })
                    SettingIssuesCard(ui, onFix = { vm.applySettingFix(it) }, onGoEdit = { tab = 2 })
                    // [スクショ指摘/撤去] 「ほかの作り方」カード（速くつくる/かんたんに/閉じても大丈夫）は
                    //   主導線（思考誘導カード＋下部バー）と重複し、実行中は全ボタン無効の死に領域だった
                    //   （ユーザー赤囲い指示）。唯一固有のバックグラウンド実行は設定タブ「最適化設定」へ移設。
                    AlternativesCard(ui, onApply = { vm.applyAlternative(it) })
                }
                1 -> {
                    val openEditor: (Int, Int) -> Unit = { i, j ->
                        haptic.performHapticFeedback(HapticFeedbackType.LongPress)
                        editingCell = i to j
                        // [窓ハイライト③] c1/c3/c3m はどの範囲の違反かをグリッド上でも示す（シート表示中のみ）。
                        focusRange = vm.violationRange(i, j)?.let { Triple(i, it.first, it.second) }
                    }
                    // [D7] 読取(結果)モードはユーザー判断で撤去（「下書き（直す）モードだけで大丈夫」）。
                    //   勤務表は常に直接編集の1本＝タップで即編集シート。最適化完了時は schedule==resultSchedule
                    //   のため結果はそのまま見えている。誤編集は「元に戻す」が担保。
                    var wishBulkOpen by rememberSaveable { mutableStateOf(false) }
                    // [E7] 違反 種別フィルタ（勤務表タブ全面共有）。初期=全ON。bitmask(Int)で rememberSaveable 保存
                    //   （回転/プロセス復元で保持）。表示のみ・スコアリング不変。ビット i = vioBuckets[i] のON/OFF。
                    var vioMask by rememberSaveable { mutableIntStateOf((1 shl vioBuckets.size) - 1) }
                    val vioEnabled = remember(vioMask) {
                        vioBuckets.filterIndexed { i, _ -> (vioMask shr i) and 1 == 1 }.map { it.key }.toSet()
                    }
                    // [集中モード] 違反・未反映希望以外のセルを淡色化するトグル（既定OFF・回転/復元で保持）。
                    var focusMode by rememberSaveable { mutableStateOf(false) }
                    // [画面修正版 ②] 検索・凡例（折りたたみ）。検索=職員名で該当グリッド行を強調（回転/復元で保持）。
                    var searchQuery by rememberSaveable { mutableStateOf("") }
                    WishApplyCard(ui, onApply = {
                        val oos = vm.wishOutOfScopeCount()
                        if (oos > 0) wishConfirm = oos else vm.applyWishes(false)
                    })
                    // [E7] 種別フィルタ行（違反があるときだけ表示）。グリッド/カレンダー/集計を1つのフィルタで絞る。
                    // [画面修正版 ③] 要確認件数＝違反ロケーション数（セル+日+回数の各マップの実箇所数）。
                    val vioLocCount = ui.violationCells.size + ui.needViolations.size + ui.countViolations.size
                    ViolationFilterBar(vioBucketLocCounts(ui), vioEnabled, onToggle = { key ->
                        val i = vioBuckets.indexOfFirst { it.key == key }
                        if (i >= 0) vioMask = vioMask xor (1 shl i)
                    }, locCount = vioLocCount, focusMode = focusMode, onFocusMode = { focusMode = it })
                    // [画面修正版 ②] 検索・凡例の統合折りたたみ（E7フィルタは上の独立バーのまま＝可視）。
                    SearchLegendBar(ui, searchQuery, onQuery = { searchQuery = it })
                    ScheduleGrid(ui, onCellClick = openEditor, proMode = proMode, vioEnabled = vioEnabled, nameQuery = searchQuery,
                        onBulkSet = { cells, k -> vm.setCells(cells, k) },
                        focusCell = focusCell, onFocusShown = { focusCell = null }, focusRange = focusRange, focusMode = focusMode,
                        canDo = { i, k -> vm.allowedShiftsFor(i).contains(k) })
                    // [3.193.0 シンプル化] 「職員別カレンダー」（StaffCalendarCard）を撤去。既存コメントが
                    //   自認していたとおり全職員グリッドと同じ盤面の二重表示＝密度/冗長の主因だった。撤去。
                    TallyCard(ui, vm, onFix = { staff, shift -> tab = 3; vm.findFixSuggestions(staff, shift) }, vioEnabled = vioEnabled)
                    // [3.194.0 情報の冗長性検証] 「不一致だけ抽出」（MismatchExtractCard）を撤去。
                    //   TallyCard(職員別/日別)の▼▲バッジ・ScheduleGridの人員不足バナー/桃バッジと
                    //   内容が重複しており、しかも apt(適切回数)由来の違反を含まず新しい表示より不完全だった。
                    OutlinedButton(onClick = { wishBulkOpen = true }, enabled = !ui.running, modifier = Modifier.fillMaxWidth()) {
                        Text("希望シフトの一括操作")
                    }
                    if (wishBulkOpen) {
                        WishBulkSheet(ui, vm, presetWeekday = 0, onDismiss = { wishBulkOpen = false })
                    }
                }
                2 -> {
                    // [見つけやすさ改善] 案内カードの「希望シフト」行タップで月次条件タブへ直行。
                    //   WishCardは常時展開のカレンダー主導線のため、タブ切替のみで編集画面に到達する。
                    val openWish: () -> Unit = { editScope = 0 }
                    SetupGuideCard(ui, vm, editScope = editScope, onOpenWish = openWish)
                    // [入口4分割] 入力場所を「いつ触るか」で分ける: 月次条件(毎月)/職員管理(随時)/年間マスター(制度変更時)。
                    //   4か所目の勤務表グリッドは勤務表タブが担当（作成後の例外・違反修正）。
                    MagiSegmentedControl(options = listOf("月次条件", "職員管理", "年間マスター"), selected = editScope, onSelect = { editScope = it })
                    // [発見性] 各スコープの中身を1行で示す。
                    Text(
                        when (editScope) {
                            0 -> "翌月だけの条件：希望・必要人数・例外（毎月ここから）"
                            1 -> "入退職・所属・資格スキル・個人の回数（随時変更）"
                            else -> "毎月は変えない土台：シフト・ルール・人数（制度変更時のみ）"
                        },
                        style = MaterialTheme.typography.labelMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                    when (editScope) {
                        0 -> {
                            // [月次条件] チェックリスト→月えらび→希望→日別例外。入力順序＝作成前の安全な流れ。
                            MonthlyChecklistCard(ui, vm, onMake = { vm.runV6FullOptimize(); tab = 0 }, onOpenWish = openWish)
                            MonthPickerCard(ui, vm)
                            // [3.190.0 横展開・検討のうえ対象外] WishCard/NeedCalendarCard は選択中の職員/シフト
                            //   (i/k・remember)を保持したまま複数回編集する設計のため、key(ui.editRev)で包むと
                            //   自分自身の編集コミット(editRev変化)のたびに選択がリセットされ、③より悪い退行を生む。
                            //   よって対象外（未確認のリスクへの予防的変更よりも確定した退行の回避を優先）。
                            WishCard(ui, vm, initialStaff = deepLinkWishStaff.takeIf { it >= 0 }, onInitialConsumed = { deepLinkWishStaff = -1 })
                            NeedCalendarCard(ui, vm, initialShift = deepLinkNeedShift.takeIf { it >= 0 }, onInitialConsumed = { deepLinkNeedShift = -1 })
                            key(ui.editRev) { NeedDayCard(ui, vm) }
                        }
                        1 -> {
                            // [職員管理] 入退職・所属・スキルの随時変更（人の属性管理に純化。個人の回数上下限は
                            //   年間マスター「③ 回数（1人あたり）」の StaffRangeCard へ=3.286.0 冗長性A）。
                            key(ui.editRev) { StaffManageCard(ui, vm) }
                            // [3.286.0 冗長性A] StaffRangeCard は年間マスター「③ 回数（1人あたり）」へ一本化
                            //   （旧: 職員管理と③の2ドアに同一カード全体が重複＝編集タブ内で唯一のカード丸ごと重複だった。
                            //   回数設定は③が意味的定位置・職員管理は人の属性管理=入職/退職/改名/所属/スキルに純化）。
                        }
                        else -> {
                            // [見直し候補] 月次の修正から送られたルール見直しメモ（あれば先頭に表示）。
                            ReviewMemoCard(ui, vm)
                            // [年度始めモード] シフト別の実働体制（担当人数 vs 需要・欠勤耐性）を土台編集の入口で提示。
                            StaffingRealityCard(ui, vm)
                            Surface(color = MaterialTheme.colorScheme.secondaryContainer, shape = MaterialTheme.shapes.medium) {
                                // [P7/実務者向け短文化] 3文→1文。触るべきでない理由の説教は削り、行き先だけ示す。
                                Text("土台の設定（制度変更時のみ）。毎月の調整は「月次条件」、人の入替は「職員管理」へ。",
                                    color = MaterialTheme.colorScheme.onSecondaryContainer,
                                    modifier = Modifier.fillMaxWidth().padding(12.dp),
                                    style = MaterialTheme.typography.bodyMedium)
                            }
                            // [E6案A] 長大スクロールを畳んで削減。①のみ既定で展開。展開状態は rememberSaveable で保持。
                            // [3.190.0 横展開] ①②④⑤も③と同じ再構成保証を適用（CollapsibleSection の content
                            //   ラムダが ui/vm を捕捉しスキップ判定に絡む同型の懸念に対する予防的対応。
                            //   Ws1Card=use2トグル・担当可否チップ／SkillGroupCard=スキル割当ボタン／
                            //   ConstraintsCard(s)=行タップ編集後の一覧表示、がいずれも生の vm 読取で
                            //   即時反映を期待する箇所のため key(ui.editRev) で編集ごとに確実に作り直す）。
                            CollapsibleSection("① シフト・グループ・職員", "yr_ws1", initiallyExpanded = true) {
                                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                                    SectionNote("勤務の種類・グループ・職員と、群×勤務の担当可否を決めます。")
                                    key(ui.editRev) { Ws1Card(ui, vm) }
                                }
                            }
                            CollapsibleSection("② スキルグループ", "yr_skillg") {
                                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                                    SectionNote("資格や対応できる業務などの“スキル”でまとめる単位です。勤務のグループとは別の切り口で分けます（例：採血できる人・リーダーできる人）。")
                                    key(ui.editRev) { SkillGroupCard(ui, vm) }
                                }
                            }
                            // ③ 回数（1人あたり）★統合: 目標(apt) ＋ 個人の下限上限(ws5) ＋ グループ一括
                            CollapsibleSection("③ 回数（1人あたり）", "yr_count") {
                                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                                    SectionNote("1人がその勤務へ1か月に何回入るかを調整します。『目標』は近づけたい回数（やわらかい）、『下限/上限』は必ず守る回数（かたい）。グループまとめての一括設定もできます。")
                                    // [実機バグ修正/③回数] +/-を押しても画面上の数字が更新されない（タブを離れて
                                    //   戻ると反映される＝データは正しく保存されるが同一画面での再描画だけが
                                    //   遅れる）。CollapsibleSection の content ラムダが ui/vm を捕捉するため
                                    //   スキップ判定が絡み再構成が伝播しないケースがある。key(ui.editRev) で
                                    //   editRev 変化ごとにこの3カードのみ確実に作り直す（タブ往復と同じ効果）。
                                    key(ui.editRev) { AptCard(ui, vm) }
                                    key(ui.editRev) { StaffRangeCard(ui, vm) }
                                    key(ui.editRev) { GroupRangeCard(ui, vm) }
                                }
                            }
                            // ④ 人数と組み合わせ ★統合: グループ(C41/C42) ＋ スキルグループ(C41s/C42s)
                            CollapsibleSection("④ 人数と組み合わせ", "yr_headcount") {
                                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                                    SectionNote("その日その勤務に入る人数の下限／上限（群のレンジ）と、同じ日に一緒に入れない組み合わせ（群ペア禁止）を設定します。グループ単位とスキルグループ単位の両方を扱えます。")
                                    key(ui.editRev) {
                                        ConstraintsCard(ui, vm, title = "グループ単位（群のレンジ・群ペア禁止）",
                                            keys = setOf("cons41", "cons42"))
                                    }
                                    key(ui.editRev) { SkillConstraintsCard(ui, vm) }
                                }
                            }
                            CollapsibleSection("⑤ 並び・くり返し", "yr_cons") {
                                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                                    SectionNote("勤務の並び方のルールです。『○日間に△回以上』の窓の要件、個人の合計（回数）、『必須／禁止／推奨／回避の並び』のパターンを設定します。")
                                    key(ui.editRev) {
                                        ConstraintsCard(ui, vm, title = "",
                                            keys = setOf("cons1", "cons2", "cons3", "cons3n", "cons3m", "cons3mn"))
                                    }
                                }
                            }
                        }
                    }
                }
                3 -> {
                    // [N2/⛏11] プロ表示トグルを分析タブ上部に常設（従来は設定タブの外観カード内＝
                    //   タブ往復が必要だった）。proMode は共有状態なので設定側トグルと同期する。
                    MagiSegmentedControl(options = listOf("一般", "プロ"), selected = if (proMode) 1 else 0, onSelect = { proMode = it == 1 })
                    // [スクショ指摘/撤去] 概要ヒーロー（対象人数/対象期間）は読込ステータス行と重複の固定値で
                    //   トリアージに寄与しないため撤去（ユーザー赤囲い指示）。件数は要確認一覧の見出しが担う。
                    // [★1/E1] 要確認一覧＝散在していた診断を「箇所単位・重大度」で1ハブに統合（web「画面修正版」confirm 移植）。
                    //   タブ先頭のヒーローとして配置。staff 紐付き項目タップで修復フロー(findFixSuggestions)へ。表示のみ・スコア不変。
                    ConfirmListCard(ui, onFocusStaff = { vm.findFixSuggestions(it) }, onGoEdit = { tab = 2 }, proMode = proMode,
                        onShowCell = { i, j -> focusCell = i to j; tab = 1 },
                        // [⑥日別ジャンプ] 人員/群レンジ(日×シフト)の項目→勤務表タブの該当日列へ（i=-1=日のみ注目）。
                        onShowDay = { j -> focusCell = -1 to j; tab = 1 },
                        // [下流→上流ディープリンク] pref→希望シフト登録(職員)、covU/covO→必要人数カレンダー(シフト)。編集タブ/月次条件へ。
                        onFixWish = { s -> deepLinkWishStaff = s; editScope = 0; tab = 2 },
                        onFixNeed = { k -> deepLinkNeedShift = k; editScope = 0; tab = 2 })
                    // [★3+4] 日別/人別 注意リスト＋「要確認のみ」トグル（web「画面修正版」day/staff＋alertOnly 融合）。
                    //   人別行タップで修復フローへ。BottleneckCard(top5テキスト) の上位互換のため下の BottleneckCard は撤去。
                    AttentionCardsSection(ui, onFocusStaff = { vm.findFixSuggestions(it) })
                    // [冗長性/用語][コメント訂正] 開発用の V6 1ヶ月俯瞰(HARD Core/Guard・Apt/Equalize/covU 等の
                    //   生指標)は「詳細設定(上級者)」ではなく分析タブのプロ表示にのみ一本化済み（冗長性J1、
                    //   MagiSetupCards.kt 参照。旧コメントは移設先が実態と食い違っていたため訂正）。
                    // [プロ編集] プロ表示モードのときは数値診断（V6 1ヶ月俯瞰・生指標）を前面に出す。
                    if (proMode) V6DashboardCard(ui.v6)
                    // [実機指摘] 重み表（WeightTableCard）は分析タブから設定タブの最適化設定直後へ移動。
                    // [IA重複解消] BossCard は FixSuggestionCard と同じ提案＋適用を二重描画していたため撤去（下の FixSuggestionCard に一本化）。
                    // [見直し/IA重複解消] OverviewDashboard(気になる点=総違反リング / 注意の日リング)を撤去。融合カードが上位代替:
                    //   気になる点(総数)→ヒーロー規模＋要確認一覧ヘッダ件数、注意の日→AttentionCardsSection の日別リスト。
                    //   「違反総数」の三重表示を D2(HARD三重リング撤去)と同方針で解消。composable 定義は残置=無害。
                    // [3.286.0 冗長性D] CheckSummaryView（チェック概要=必須違反数の1行）を撤去。必須違反数は
                    //   ホームの OperatorNextActionCard と要確認一覧（ConfirmListCard）見出しで既に2重に提示済みで
                    //   3重目だった（3.83.0 の維持判断は ConfirmListCard ヒーロー化前。違反ゼロ時の達成表示も
                    //   ConfirmListCard が持つため喪失情報なし）。
                    BreakdownCard(ui, onFocusStaff = { vm.findFixSuggestions(it) }, proMode = proMode)
                    // [★3+4] BottleneckCard(top5テキスト) は AttentionCardsSection(上・全件＋トグル＋タップ修復) が上位互換のため撤去。
                    FixSuggestionCard(ui, onSearch = { vm.findFixSuggestions(null) }, onApply = { vm.applyFixSuggestion(it) }, proMode = proMode)
                    // [3.122.0→3.132系] ColorSettingsView（違反種別の色=族別の色設定）は設定タブのシフトの表示色直後に配置。
                }
                else -> {
                    AppearanceCard(oneHand, { oneHand = it }, proMode) { proMode = it }
                    ShiftColorCard(ui, vm)
                    // [IA重複解消 3.132系] 違反の色は ColorSettingsView（基準色2種＋族別）へ一本化し、
                    //   シフトの表示色の直後＝色設定の定位置に配置（旧: 詳細設定の折りたたみ内で見つけにくい＋
                    //   ShiftColorCard 内に必須色だけの部分入口が重複していた）。
                    ColorSettingsView(ui, vm)
                    DataActionsCard(
                        ui = ui,
                        onOpenJson = openJson,
                        onSample = loadSample,
                        onSaveJson = { saveJsonLauncher.launch("magi_state_${System.currentTimeMillis()}.json") },
                        onOpenCsv = { openCsvLauncher.launch(arrayOf("text/csv", "text/comma-separated-values", "text/plain", "*/*")) },
                        onSaveCsv = { saveCsvLauncher.launch("magi_schedule_${System.currentTimeMillis()}.csv") },
                        onCheck = { vm.refreshCheck() },
                        onSaveStaffCsv = { pendingExportKind = "staff"; saveComponentCsvLauncher.launch("magi_staff_${System.currentTimeMillis()}.csv") },
                        onSaveWishesCsv = { pendingExportKind = "wishes"; saveComponentCsvLauncher.launch("magi_wishes_${System.currentTimeMillis()}.csv") },
                        onSaveConstraintsCsv = { pendingExportKind = "cons"; saveComponentCsvLauncher.launch("magi_constraints_${System.currentTimeMillis()}.csv") },
                        onRestorePrev = { vm.restorePreviousData() },
                    )
                    SettingsCard(ui, vm, onBgOptimize = onBgOptimize)
                    // [実機指摘/移動] 重み表＝最適化の優先順位の根拠。実行条件（最適化設定）の隣が定位置。
                    WeightTableCard()
                    // [冗長性] 旧 OperatorLogView（見出し「操作ログ」だが中身は診断ログ＝誤ラベルで、
                    //   詳細設定の LogsCard と重複）を撤去。ログは詳細設定>ログ(操作+診断)に一本化。
                    AdvancedSettingsSection(
                        ui = ui,
                        vm = vm,
                        onExportLog = { saveLogLauncher.launch("magi_log_${System.currentTimeMillis()}.txt") },
                        onExportJson = { saveLogJsonLauncher.launch("magi_log_${System.currentTimeMillis()}.json") },
                    )
                }
            }
            ui.message?.let { MessageBar(it) }
            Spacer(Modifier.height(12.dp)) // 下部コマンドバー分の余白
        }
        val cell = editingCell
        if (cell != null) {
            ShiftPickerSheet(
                ui = ui,
                vm = vm,
                cell = cell,
                onPick = { k ->
                    haptic.performHapticFeedback(HapticFeedbackType.LongPress)
                    vm.setCell(cell.first, cell.second, k)
                    editingCell = null
                    focusRange = null
                },
                onDismiss = { editingCell = null; focusRange = null },
            )
        }
        if (guidedFix) {
            GuidedFixDialog(ui, vm, onDismiss = { guidedFix = false }, onRerun = { guidedFix = false; vm.runV6FullOptimize() })
        }
        pendingCsvImport?.let { csvText ->
            AlertDialog(
                onDismissRequest = { pendingCsvImport = null },
                title = { Text("取込種別を選択") },
                text = {
                    Text(
                        "この CSV を何として取り込みますか？\n\n" +
                            "・データ全体（新規）：勤務表テンプレ/ユニット列形式を新しいデータとして読み込み\n" +
                            "・勤務表（重ね合わせ）：氏名,1日,2日… の表を現在の割り当てに重ねる\n" +
                            "・職員一覧：氏名,グループ,スキル（所属群/スキルを更新）\n" +
                            "・希望シフト：氏名,日,希望シフト（希望を置換）\n" +
                            "・各制約：種別タグ付き（制約一式・個人レンジを置換）",
                    )
                },
                confirmButton = {
                    Column(horizontalAlignment = Alignment.End, verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        DialogConfirmButton("データ全体（新規）", onClick = {
                            if (com.magi.app.v6.RosterCsvImport.detect(csvText)) { rosterCsvChoice = csvText } else { vm.importCsvSmart(csvText) }
                            pendingCsvImport = null
                        })
                        DialogConfirmButton("勤務表（重ね合わせ）", onClick = { vm.importCsv(csvText); pendingCsvImport = null })
                        DialogConfirmButton("職員一覧", onClick = { vm.importStaffCsv(csvText); pendingCsvImport = null })
                        DialogConfirmButton("希望シフト", onClick = { vm.importWishesCsv(csvText); pendingCsvImport = null })
                        DialogConfirmButton("各制約", onClick = { vm.importConstraintsCsv(csvText); pendingCsvImport = null })
                    }
                },
                dismissButton = { DialogDismissButton(onClick = { pendingCsvImport = null }) },
            )
        }
        rosterCsvChoice?.let { csvText ->
            AlertDialog(
                onDismissRequest = { rosterCsvChoice = null },
                title = { Text("CSVの取り込み方法") },
                text = {
                    Text(
                        "この勤務表CSVを、どちらとして取り込みますか？\n\n" +
                            "・勤務表：表のとおり、いまの割り当てとして読み込みます。\n" +
                            "・希望シフト：表を職員の希望として読み込み、勤務表は空から作成して最適化で希望を尊重します。",
                    )
                },
                confirmButton = {
                    Column(horizontalAlignment = Alignment.End, verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        DialogConfirmButton("勤務表として取り込む", onClick = { vm.importRosterAs(csvText, false); rosterCsvChoice = null })
                        DialogDismissButton(onClick = { vm.importRosterAs(csvText, true); rosterCsvChoice = null }, text = "希望シフトとして取り込む")
                    }
                },
                dismissButton = { DialogDismissButton(onClick = { rosterCsvChoice = null }) },
            )
        }
        if (wishConfirm > 0) {
            AlertDialog(
                onDismissRequest = { wishConfirm = 0 },
                title = { Text("担当外の希望を含めますか？") },
                text = { Text("担当できないグループの希望が ${wishConfirm} 件あります。含めて反映すると担当不可の配置になります（違反として検出されます）。") },
                confirmButton = {
                    DialogConfirmButton("含めて反映", onClick = { vm.applyWishes(true); wishConfirm = 0 })
                },
                dismissButton = {
                    DialogDismissButton(onClick = { vm.applyWishes(false); wishConfirm = 0 }, text = "担当内のみ反映")
                },
            )
        }
    }
}

/**
 * [DefragLiveView 移植] 計算中の最良盤面ライブ表示。実行中のみ・折りたたみ。前回スナップショットと比較し
 * 変化セルを赤枠でハイライト（操作不可の読取専用。オペレーターに「組んでいる様子」を見せて安心させる）。
 */

@Composable
internal fun MagiTopBar(ui: UiState, sectionTitle: String = "勤務表") {
    Surface(color = MaterialTheme.colorScheme.surface, tonalElevation = 2.dp, shadowElevation = 2.dp) {
        Row(
            Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 12.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Surface(color = MaterialTheme.colorScheme.primary, shape = MaterialTheme.shapes.small) {
                Text(
                    "MAGI",
                    style = MaterialTheme.typography.titleMedium,
                    fontWeight = FontWeight.Bold,
                    color = MaterialTheme.colorScheme.onPrimary,
                    modifier = Modifier.padding(horizontal = 12.dp, vertical = 6.dp),
                )
            }
            Spacer(Modifier.width(10.dp))
            Text(sectionTitle, style = MaterialTheme.typography.titleMedium, color = MaterialTheme.colorScheme.onSurfaceVariant)
            Spacer(Modifier.weight(1f))
            if (ui.loaded) {
                val ok = ui.hasResult && ui.bestHard == 0L
                val label: String; val fg: Color; val bg: Color
                when {
                    ui.running -> {
                        // [進捗の見える化] バッジに改善の手応えを添える: hard残あり→⚠数、hard=0→soft改善(init→best)。
                        val prog = when {
                            ui.bestHard > 0L -> " ⚠${ui.bestHard}"
                            ui.initSoft > 0L && ui.bestSoft in 0 until ui.initSoft -> " ${ui.initSoft}→${ui.bestSoft}"
                            else -> ""
                        }
                        label = "実行中$prog"; fg = MaterialTheme.colorScheme.onPrimaryContainer; bg = MaterialTheme.colorScheme.primaryContainer
                    }
                    ok -> { label = "配布可"; fg = MaterialTheme.colorScheme.onTertiaryContainer; bg = MaterialTheme.colorScheme.tertiaryContainer }
                    ui.hasResult -> { label = "未解決 ${ui.bestHard}"; fg = MaterialTheme.colorScheme.onErrorContainer; bg = MaterialTheme.colorScheme.errorContainer }
                    else -> { label = "未計算"; fg = MaterialTheme.colorScheme.onSurfaceVariant; bg = MaterialTheme.colorScheme.surfaceVariant }
                }
                Surface(color = bg, shape = MaterialTheme.shapes.small) {
                    Text(label, color = fg, style = MaterialTheme.typography.labelLarge, maxLines = 1,
                        modifier = Modifier.padding(horizontal = 12.dp, vertical = 6.dp))
                }
            }
        }
    }
}


@Composable
internal fun BottomCommandBar(ui: UiState, vm: MagiViewModel) {
    val cs = MaterialTheme.colorScheme
    // 一本指: 主要操作を画面下部に全幅・大ボタン(60dp)で常設。指の届く範囲で押し外しにくい。文脈で 停止/作成/最適化。
    // [DESIGN.md P3] 重い影(8dp)を廃し、surfaceContainer トーン＋軽い影(2dp)で本文から分離（melta-ui: 影より境界/トーン）。
    Surface(color = cs.surfaceContainer, tonalElevation = 2.dp, shadowElevation = 2.dp) {
        Row(
            Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 10.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            if (ui.canUndo && !ui.running) {
                OutlinedButton(
                    onClick = { vm.undo() },
                    modifier = Modifier.heightIn(min = 60.dp).semantics { contentDescription = "直前の操作を元に戻す" },
                ) { Text("元に戻す") }
                Spacer(Modifier.width(10.dp))
            }
            // [Web反映] やり直し（手動修正ループ）。元に戻した直後だけ出す。
            if (ui.canRedo && !ui.running) {
                OutlinedButton(
                    onClick = { vm.redo() },
                    modifier = Modifier.heightIn(min = 60.dp).semantics { contentDescription = "元に戻した操作をやり直す" },
                ) { Text("やり直し") }
                Spacer(Modifier.width(10.dp))
            }
            when {
                ui.running -> Button(
                    onClick = { vm.stop() },
                    modifier = Modifier.weight(1f).heightIn(min = 60.dp),
                    colors = ButtonDefaults.buttonColors(containerColor = cs.errorContainer, contentColor = cs.onErrorContainer),
                ) {
                    Icon(Icons.Filled.Stop, contentDescription = null)
                    Spacer(Modifier.width(8.dp))
                    Text("やめる", style = MaterialTheme.typography.titleMedium, maxLines = 1, overflow = TextOverflow.Ellipsis)
                }
                !ui.hasResult -> Button(
                    // [統一] ラベル「勤務表をつくる」＝本最適化（思考誘導カードの大ボタンと同一動作）。
                    //   [3.126.0] 「下書きをつくる」補助はユーザー判断で撤去済み＝作成導線はこの1本。
                    onClick = { vm.runV6FullOptimize() },
                    modifier = Modifier.weight(1f).heightIn(min = 60.dp),
                ) {
                    Icon(Icons.Filled.PlayArrow, contentDescription = null)
                    Spacer(Modifier.width(8.dp))
                    Text("勤務表をつくる", style = MaterialTheme.typography.titleMedium, maxLines = 1, overflow = TextOverflow.Ellipsis)
                }
                else -> Button(
                    onClick = { vm.runV6FullOptimize() },
                    modifier = Modifier.weight(1f).heightIn(min = 60.dp),
                ) {
                    Icon(Icons.Filled.PlayArrow, contentDescription = null)
                    Spacer(Modifier.width(8.dp))
                    Text("もう一度つくる", style = MaterialTheme.typography.titleMedium, maxLines = 1, overflow = TextOverflow.Ellipsis)
                }
            }
        }
    }
}


@Composable
internal fun MagiBottomNav(selected: Int, onSelect: (Int) -> Unit) {
    val items = listOf(
        Triple("ホーム", Icons.Filled.Home, "ホーム"),
        Triple("勤務表", Icons.Filled.DateRange, "勤務表"),
        Triple("編集", Icons.Filled.Edit, "初期設定と制約の編集"),
        Triple("分析", Icons.Filled.Assessment, "分析と違反"),
        Triple("設定", Icons.Filled.Settings, "設定とデータ"),
    )
    NavigationBar {
        items.forEachIndexed { i, item ->
            NavigationBarItem(
                selected = selected == i,
                onClick = { onSelect(i) },
                icon = { Icon(item.second, contentDescription = null) }, // [a11y] ラベル常時表示のためアイコンCDは重複回避で null
                label = { Text(item.first, style = MaterialTheme.typography.labelMedium) },
                alwaysShowLabel = true,
            )
        }
    }
}

/** 前回の計算が中断（プロセスkill等）された場合の復帰バナー。入力は復元済みで、ワンタップ再実行できる。 */

@Composable
internal fun InterruptedBanner(ui: UiState, onRerun: () -> Unit, onDismiss: () -> Unit) {
    if (!ui.interruptedRun || ui.running) return
    val cs = MaterialTheme.colorScheme
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            Text("前回の計算は中断されました", style = MaterialTheme.typography.titleMedium)
            Text(ui.interruptedInfo ?: "入力は自動保存済みです。もう一度実行できます。",
                style = MaterialTheme.typography.bodyMedium, color = cs.onSurfaceVariant)
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                Button(onClick = onRerun, enabled = ui.loaded,
                    modifier = Modifier.weight(1f).heightIn(min = 48.dp)) { Text("もう一度実行") }
                OutlinedButton(onClick = onDismiss,
                    modifier = Modifier.heightIn(min = 48.dp)) { Text("閉じる") }
            }
        }
    }
}


@Composable
internal fun EmptyStateCard(onOpen: () -> Unit, onSample: () -> Unit, onNew: () -> Unit) {
    Surface(color = MaterialTheme.colorScheme.surfaceVariant, shape = MaterialTheme.shapes.large) {
        Column(
            Modifier.fillMaxWidth().padding(24.dp),
            verticalArrangement = Arrangement.spacedBy(16.dp),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            Icon(Icons.Filled.DateRange, contentDescription = null, tint = MaterialTheme.colorScheme.primary, modifier = Modifier.size(44.dp))
            Text("勤務表データを開きましょう", style = MaterialTheme.typography.titleLarge, color = MaterialTheme.colorScheme.onSurface)
            Text("保存済みのデータを開く、サンプルから始める、または空から新しく作れます。",
                style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSurfaceVariant, textAlign = TextAlign.Center)
            Button(onClick = onOpen, modifier = Modifier.fillMaxWidth().height(56.dp)) {
                Text("データを開く", style = MaterialTheme.typography.labelLarge)
            }
            OutlinedButton(onClick = onSample, modifier = Modifier.fillMaxWidth().height(56.dp)) {
                Text("サンプルで試す", style = MaterialTheme.typography.labelLarge)
            }
            // [⛏6] ゼロから作る起点。最小データで開始し、編集タブ(年次マスター)へ誘導する。
            OutlinedButton(onClick = onNew, modifier = Modifier.fillMaxWidth().height(56.dp)) {
                Text("新規につくる（空から）", style = MaterialTheme.typography.labelLarge)
            }
        }
    }
}


@Composable
internal fun MessageBar(text: String) {
    Surface(color = MaterialTheme.colorScheme.secondaryContainer, shape = MaterialTheme.shapes.medium) {
        Text(text, style = MaterialTheme.typography.bodyMedium, color = MaterialTheme.colorScheme.onSecondaryContainer,
            modifier = Modifier.fillMaxWidth().padding(14.dp))
    }
}
