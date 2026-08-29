# 画面マップ（Kotlin Compose → C#/WinUI3、下調べ資料）

フェーズ8/9のXAML移植に先立ち、`app/src/main/java/com/magi/app/ui/` の残る14ファイルを並列サブエージェント（Workflowツール、14体・約905万トークン・87ツール呼出）で読み解いた結果を構造化して記録したもの。**下調べ資料であってコードではない**——実際に画面を移植するときは、この文書を出発点にしつつ必ず元のKotlinソースを直接読んで確認すること（サブエージェントの要約は取りこぼしや解釈違いを含みうる）。

生成元: Workflow run `wf_dbb07808-09a`（Task ID `wlp0iwewf`）。対象コミットは`magi7ichiro-fork`側の当時のHEAD。

## 対象ファイル一覧

- [MagiApp.kt](#magiappkt)
- [MagiDashboardCards.kt](#magidashboardcardskt)
- [MagiScheduleViews.kt](#magischeduleviewskt)
- [MagiSetupCards.kt](#magisetupcardskt)
- [Ws1Editor.kt](#ws1editorkt)
- [ConstraintEditor.kt](#constrainteditorkt)
- [NeedDayEditor.kt](#needdayeditorkt)
- [StaffRangeEditor.kt](#staffrangeeditorkt)
- [WishEditor.kt](#wisheditorkt)
- [ShiftColorEditor.kt](#shiftcoloreditorkt)
- [SkillGroupEditor.kt](#skillgroupeditorkt)
- [V6RemainingScreens.kt](#v6remainingscreenskt)
- [MagiComponents.kt](#magicomponentskt)
- [StaffManageCard.kt](#staffmanagecardkt)

## 凡例

各ファイルにつき以下の節を記録:

- **役割**: このファイルが何を担うか
- **Composable関数**: 名前・シグネチャ・何を描画するか・呼んでいるViewModelメソッド・読んでいるUiStateフィールド・Compose固有のローカル状態
- **ヘルパー関数**: 純粋関数・非Composable補助関数
- **ダイアログ/オーバーレイ**: AlertDialog・BottomSheet等の一覧
- **Android/Compose固有パターン**: WinUI3へ素直に移せない機構とその対処方針
- **落とし穴**: 移植時に見落としやすい罠
- **外部依存**: このファイルが依存する他のファイル/シンボル
- **呼び出し元**: このファイルの公開シンボルを呼んでいる箇所（不明な場合は空欄）

---

## MagiApp.kt

### 役割

MagiApp.kt defines the app's root composable (`MagiApp`), which hosts the Scaffold shell (top bar, bottom command bar + bottom nav, themed snackbar host) and switches between 5 top-level tabs by a `tab: Int` state — 0=ホーム(home dashboard), 1=勤務表(schedule grid + tally, with its own sub-toggles for search/concentration-mode/wish-bulk-sheet), 2=編集(edit, itself split into 3 further sub-scopes via `editScope`: 月次条件/職員管理/年間マスター), 3=分析(analysis/violations), 4=設定(settings) — delegating essentially all actual screen content to card/section composables imported from sibling UI files. It owns all root-level modal/overlay state (the cell-edit ModalBottomSheet, a GuidedFixDialog, and three CSV-import/confirmation AlertDialogs) plus every Android Storage-Access-Framework file-picker launcher used for JSON/CSV/log import and export and the notification-permission request for background optimization. Beyond `MagiApp` itself, the file also defines five small chrome/presentational composables local to it (`MagiTopBar`, `BottomCommandBar`, `MagiBottomNav`, `InterruptedBanner`, `EmptyStateCard`) and two non-Composable helper functions for CSV byte decoding (UTF-8-with-Shift-JIS-fallback + BOM stripping) and a size-capped stream read used to guard against OOM on oversized imported files.

### Composable関数

#### `MagiApp`

```kotlin
@OptIn(ExperimentalMaterial3Api::class) @Composable fun MagiApp(vm: MagiViewModel = viewModel())
```

**描画内容:** The app's root composable: a Scaffold with a top bar (MagiTopBar), a bottom bar (BottomCommandBar + MagiBottomNav stacked in a Column), and a themed SnackbarHost, wrapping a single scrollable Column whose content switches on `tab` (0..4) between the Home, Schedule, Edit (3 sub-scopes via `editScope`), Analysis, and Settings screen content — delegating essentially all real content to card composables imported from sibling files. It also hosts, at the Scaffold's root layer, a conditional cell-edit ModalBottomSheet (ShiftPickerSheet), a GuidedFixDialog, and three AlertDialogs for CSV-import disambiguation and a 'wish out of scope' confirmation.

**ViewModel呼出:**

- `vm.saveNow()`
- `vm.load(text)`
- `vm.notifyOpenFailure(r, kind)`
- `vm.exportJson()`
- `vm.notifySave(r, what)`
- `vm.notify(text, level)`
- `vm.exportCsv()`
- `vm.exportStaffCsv()`
- `vm.exportWishesCsv()`
- `vm.exportConstraintsCsv()`
- `vm.exportLogs()`
- `vm.exportLogsJson()`
- `vm.runInBackground()`
- `vm.clearMessage(m)`
- `vm.initBlankState()`
- `vm.runV6FullOptimize()`
- `vm.generateSmartInitial()`
- `vm.stop()`
- `vm.dismissInterrupted()`
- `vm.findFixSuggestions()`
- `vm.runSoftPolish()`
- `vm.relaxForbiddenRule(it)`
- `vm.relaxStaffRangePin(i, k, loD, hiD)`
- `vm.applySettingFix(it)`
- `vm.applyAlternative(it)`
- `vm.editBlockedNow()`
- `vm.violationRange(i, j)`
- `vm.wishOutOfScopeCount()`
- `vm.applyWishes(includeOutOfScope)`
- `vm.setCells(cells, k)`
- `vm.allowedShiftsFor(i)`
- `vm.refreshCheck()`
- `vm.restorePreviousData()`
- `vm.setCell(i, j, k)`
- `vm.importCsvSmart(csvText)`
- `vm.importCsv(csvText)`
- `vm.importStaffCsv(csvText)`
- `vm.importWishesCsv(csvText)`
- `vm.importConstraintsCsv(csvText)`
- `vm.importRosterAs(csvText, asWishes)`

**読むUiStateフィールド:**

- `ui.running`
- `ui.loaded`
- `ui.message`
- `ui.messageIsError`
- `ui.coverageDiag`
- `ui.violationCells`
- `ui.needViolations`
- `ui.countViolations`
- `ui.editRev`

**Compose固有ローカル状態:**

- editingCell: Pair<Int,Int>? = null — remember { mutableStateOf<Pair<Int,Int>?>(null) }
- oneHand: Boolean = false — rememberSaveable (one-hand layout offset toggle)
- proMode: Boolean = false — rememberSaveable (pro/simple display mode, shared with analysis-tab segmented control)
- plainCellBorder: Boolean = false — rememberSaveable (grid cell outline visibility)
- editScope: Int = 0 — rememberSaveable (edit-tab sub-scope: 0=月次条件/1=職員管理/2=年間マスター)
- deepLinkWishStaff: Int = -1 — rememberSaveable (deep-link target staff index for WishCard, -1=none)
- deepLinkNeedShift: Int = -1 — rememberSaveable (deep-link target shift index for NeedCalendarCard, -1=none)
- wishConfirm: Int = 0 — remember { mutableStateOf(0) } (>0 = show out-of-scope-wish confirm dialog, value = count shown in dialog text)
- rosterCsvChoice: String? = null — remember (non-null = show roster-vs-wishes CSV disambiguation dialog, value = pending CSV text)
- pendingCsvImport: String? = null — remember (non-null = show import-kind selection dialog, value = pending CSV text)
- pendingExportKind: String? = null — remember (stashes 'staff'/'wishes'/'cons' between requesting a component CSV export and the saveComponentCsvLauncher callback firing)
- guidedFix: Boolean = false — remember (show GuidedFixDialog)
- openJsonLauncher/openCsvLauncher/saveJsonLauncher/saveCsvLauncher/saveComponentCsvLauncher/saveLogLauncher/saveLogJsonLauncher/notifPermLauncher — 8x rememberLauncherForActivityResult (SAF open/save document + notification-permission launchers)
- tab: Int = 0 — rememberSaveable (top-level tab index, 0=home..4=settings)
- focusCell: Pair<Int,Int>? = null — remember (grid cell to auto-scroll-to/flash-highlight, e.g. -1 to j means 'day-only' focus)
- focusRange: Triple<Int,Int,Int>? = null — remember (staff, startDay, endDay window to outline on the grid while the edit sheet is open)
- vioMask: Int = (1 shl vioBuckets.size) - 1 — rememberSaveable via mutableIntStateOf (bitmask of enabled violation-family filter buckets, all-on by default)
- vioEnabled: Set<String> — remember(vioMask) { derived filter-key set from vioMask }
- snackbarHostState: SnackbarHostState — remember { SnackbarHostState() }
- (inside tab==1 branch) wishBulkOpen: Boolean = false — rememberSaveable (bulk-wish bottom sheet visibility)
- (inside tab==1 branch) focusMode: Boolean = false — rememberSaveable ('concentration mode' dimming toggle for the schedule grid)
- (inside tab==1 branch) searchQuery: String = "" — rememberSaveable (staff-name search filter for the schedule grid legend bar)


#### `MagiTopBar`

```kotlin
@Composable internal fun MagiTopBar(ui: UiState, sectionTitle: String = "勤務表")
```

**描画内容:** A Surface-backed top app bar row: a primary-colored 'MAGI' logo pill, the current section title text (passed in from MagiApp based on `tab`), and — only if `ui.loaded` — a right-aligned status badge whose label/colors branch on state: '実行中 <progress suffix>' while running, '配布可' (feasible) when a result exists with bestHard==0, '必須違反 N' when a result exists but hard>0, or '未計算' otherwise.

**読むUiStateフィールド:**

- `ui.loaded`
- `ui.running`
- `ui.bestHard`
- `ui.initSoft`
- `ui.bestSoft`
- `ui.hasResult`


#### `BottomCommandBar`

```kotlin
@Composable internal fun BottomCommandBar(ui: UiState, vm: MagiViewModel)
```

**描画内容:** A Surface-backed bottom row of 60dp-min-height, one-hand-reachable buttons: optional '元に戻す' (undo) and 'やり直し' (redo) buttons when applicable, followed by a single mutually-exclusive trailing primary action — 'やめる' (stop, error-colored) while running or fix-searching, else '勤務表をつくる' (create) when no result yet exists, else 'もう一度つくる' (re-create).

**ViewModel呼出:**

- `vm.undo()`
- `vm.redo()`
- `vm.stop()`
- `vm.runV6FullOptimize()`

**読むUiStateフィールド:**

- `ui.canUndo`
- `ui.running`
- `ui.canRedo`
- `ui.fixSearching`
- `ui.hasResult`


#### `MagiBottomNav`

```kotlin
@Composable internal fun MagiBottomNav(selected: Int, onSelect: (Int) -> Unit)
```

**描画内容:** A Material3 NavigationBar with 5 always-labeled NavigationBarItems (ホーム/勤務表/編集/分析/設定, icons Home/DateRange/Edit/Assessment/Settings), highlighting the item matching `selected` and invoking `onSelect(index)` on tap.


#### `InterruptedBanner`

```kotlin
@Composable internal fun InterruptedBanner(ui: UiState, onRerun: () -> Unit, onDismiss: () -> Unit)
```

**描画内容:** Renders nothing (early return) unless `ui.interruptedRun && !ui.running`; otherwise shows a Card with '前回の計算は中断されました' title, `ui.interruptedInfo` (or a fallback string) body, and two buttons: 'もう一度実行' (enabled only when `ui.loaded`, calls onRerun) and '閉じる' (calls onDismiss).

**読むUiStateフィールド:**

- `ui.interruptedRun`
- `ui.running`
- `ui.interruptedInfo`
- `ui.loaded`


#### `EmptyStateCard`

```kotlin
@Composable internal fun EmptyStateCard(onOpen: () -> Unit, onSample: () -> Unit, onNew: () -> Unit)
```

**描画内容:** A full-width, large-shape, surfaceVariant-colored empty-state Card shown when no data is loaded: a DateRange icon, '勤務表データを開きましょう' title, explanatory body text, and three full-width 56dp-tall buttons — 'データを開く' (calls onOpen), 'サンプルで試す' (outlined, calls onSample), '新規につくる（空から）' (outlined, calls onNew).


### ヘルパー関数

- `decodeCsvBytes`（`internal fun decodeCsvBytes(bytes: ByteArray): String`）: Decodes a raw CSV byte array into a String for import: first tries strict UTF-8 decoding (failing on malformed/unmappable bytes rather than substituting), falls back to MS932 (Shift-JIS, common for Japanese Excel CSV exports) if strict UTF-8 fails, falls back to lenient UTF-8 as a last resort if even MS932 decoding throws, then strips a leading UTF-8 BOM character from the result.
- `InputStream.readAtMost`（`private fun java.io.InputStream.readAtMost(limit: Long = MAX_IMPORT_BYTES): ByteArray`）: Reads an InputStream into a ByteArray in 64KB chunks, throwing IOException mid-read (before finishing) if the cumulative byte count exceeds `limit` (default 32MiB via the file-level `MAX_IMPORT_BYTES` const) — a guard against out-of-memory crashes when the user picks an unexpectedly huge file via the SAF file pickers.

### ダイアログ/オーバーレイ

- ModalBottomSheet (external `ShiftPickerSheet`): triggered by tapping a schedule-grid cell (openEditor sets `editingCell = i to j`, guarded by `!vm.editBlockedNow()`), or by picking a cell to fix from other flows. Confirms via `onPick = { k -> vm.setCell(i, j, k); editingCell = null; focusRange = null }`; cancels via `onDismiss = { editingCell = null; focusRange = null }`.
- GuidedFixDialog (external): triggered from Home's OperatorNextActionCard onFix handler when `guidedFix = true` is set (only when there ARE coverage shortfalls; otherwise it routes to the Analysis tab + `vm.findFixSuggestions()` instead). Confirms via onRerun (`guidedFix = false; vm.runV6FullOptimize()`); dismisses via onDismiss (`guidedFix = false`).
- AlertDialog 'pendingCsvImport' (取込種別を選択): triggered when a CSV is picked via openCsvLauncher (sets `pendingCsvImport = text`). Presents 6 buttons: 5 stacked import-kind choices in the confirmButton slot — 'データ全体（新規）' (routes to RosterCsvImport.detect() check, then either shows the rosterCsvChoice dialog or calls `vm.importCsvSmart`), '勤務表（重ね合わせ）' (`vm.importCsv`), '職員一覧' (`vm.importStaffCsv`), '希望シフト' (`vm.importWishesCsv`), '各制約' (`vm.importConstraintsCsv`) — plus a real cancel in the dismissButton slot (`pendingCsvImport = null`).
- AlertDialog 'rosterCsvChoice' (CSVの取り込み方法): triggered from the pendingCsvImport dialog's 'データ全体（新規）' branch when `RosterCsvImport.detect(csvText)` is true. Two stacked choices in confirmButton — '勤務表として取り込む' (`vm.importRosterAs(csvText, false)`) and '希望シフトとして取り込む' (styled as DialogDismissButton but placed in the confirmButton column, calls `vm.importRosterAs(csvText, true)`) — plus a real cancel in the dismissButton slot.
- AlertDialog 'wishConfirm' (担当外の希望を含めますか？): triggered from WishApplyCard's onApply handler (tab 1) when `vm.wishOutOfScopeCount() > 0` (the count is stored in `wishConfirm` and shown in the dialog body). Two stacked choices in confirmButton — '含めて反映' (`vm.applyWishes(true)`) and '担当内のみ反映' (styled as DialogDismissButton but in the confirmButton column, `vm.applyWishes(false)`) — plus a real cancel in the dismissButton slot (`wishConfirm = 0`, discards without applying).
- Snackbar (SnackbarHost at Scaffold level): driven by `LaunchedEffect(ui.message)` — whenever `ui.message` becomes non-null it dismisses any current snackbar and shows the new one (Long duration + errorContainer colors if `ui.messageIsError`, else Short duration + inverseSurface colors), then calls `vm.clearMessage(m)` so the same text can re-fire later. Used for one-shot success/failure/info notifications from every import/export/action handler in this file (e.g. save failures, 'no data to export', open failures).

### Android/Compose固有パターン（要変換方針）

- collectAsStateWithLifecycle(): `val ui by vm.ui.collectAsStateWithLifecycle()` (line 155) is the single subscription to the ViewModel's `StateFlow<UiState>`; every downstream `ui.xxx` read in this file and in every child composable it invokes is implicitly reactive because Compose tracks reads and recomposes on change. This is the central data-binding seam of the whole screen tree. WinUI3/x:Bind has no single 'subscribe here, everything downstream is live' mechanism — each bound control needs its own `{x:Bind ViewModel.Field, Mode=OneWay}`/`{Binding}`, and the ViewModel must raise per-property `INotifyPropertyChanged` events; Compose's granular automatic read-tracking must be manually replicated as granular XAML bindings.
- key(ui.editRev) { ... }: used repeatedly in the tab==2 (year-master) branch, e.g. `key(ui.editRev) { StaffManageCard(ui, vm) }`, `key(ui.editRev) { Ws1Card(ui, vm) }`, `key(ui.editRev) { CountsCard(ui, vm) }`, `key(ui.editRev) { ConstraintsCard(...) }` etc. Per the file's own comments this is a documented workaround for a Compose-specific recomposition-skip bug (structurally-equal `UiState` copies weren't forcing re-render of nested `var`-holding cards until a monotonic `editRev` counter changed forces the whole subtree to be discarded and rebuilt). WinUI3/x:Bind does not have Compose's positional-memoization/skip-recomposition model that caused this bug, so this specific pattern is very likely Compose-only baggage that should NOT be literally ported — the underlying fix in WinUI3 is simply ensuring the ViewModel raises `PropertyChanged` correctly for the fields those cards read.
- LaunchedEffect(key) { ... }: two uses — `LaunchedEffect(ui.running) { rootView.keepScreenOn = ui.running }` (line 171, a wake-lock side effect keyed on ui.running) and `LaunchedEffect(ui.message) { ...; snackbarHostState.showSnackbar(m, duration=...); vm.clearMessage(m) }` (line 390, shows a Snackbar and then clears the message so it can re-fire). LaunchedEffect runs a coroutine that cancels-and-relaunches whenever its key changes; there is no equivalent primitive in WinUI3 — must be hand-built as a `PropertyChanged` handler in code-behind or an event the ViewModel raises (e.g. 'ShowSnackbarRequested'), with manual cancellation/debounce logic if needed.
- DisposableEffect(lifecycleOwner, vm) { ...; onDispose { ... } }: registers a `LifecycleEventObserver` (lines 159-168) that calls `vm.saveNow()` on `ON_STOP`/`ON_PAUSE` (app backgrounded/covered) to flush pending debounced saves before the process might be killed, removing the observer in onDispose. WinUI3 desktop apps have no equivalent 'the OS may kill me any moment while backgrounded' lifecycle event the way Android does; closest analogs are `Window.Closed`/`Window.VisibilityChanged`, which fire in different circumstances — this needs a deliberate redesign (e.g. hook Window.Closed plus a periodic/idle autosave) rather than a literal port.
- remember(vioMask) { ... } (parameterized remember as poor-man's derivedStateOf): line 362, memoizes `vioEnabled: Set<String>` recomputed only when `vioMask` (an Int bitmask) changes, avoiding recompute on every recomposition. In C#/WinUI3 this is naturally a computed property recalculated in the `vioMask` property setter, or a manually cache-invalidated field — straightforward but must be done by hand (no automatic memoization primitive).
- rememberCoroutineScope(): `val scope = rememberCoroutineScope()` (line 173) gives a composition-lifetime CoroutineScope used to `scope.launch { withContext(Dispatchers.IO/Default) { ... } }` from click/callback handlers (all the import/export file operations) — i.e. async work triggered by a UI event, not by composition itself. WinUI3 equivalent is an `async void`/`async Task` event handler using `await Task.Run(...)` for IO/CPU work, with the `await` continuation automatically marshaling back to the UI thread via WinUI3's SynchronizationContext (no explicit dispatcher call needed on resume, unlike raw background threads) — but unhandled exceptions in `async void` handlers crash the app, unlike Kotlin's scoped coroutine exception handling.
- rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()/CreateDocument(mime)/RequestPermission()): 8 launchers declared up front (openJsonLauncher, openCsvLauncher, saveJsonLauncher, saveCsvLauncher, saveComponentCsvLauncher, saveLogLauncher, saveLogJsonLauncher, notifPermLauncher, lines 191-350) — Android's 'register a callback now, invoke `.launch(...)` later to trigger the system picker/permission UI' pattern backed by ActivityResultRegistry. WinUI3 file pickers are imperative and awaited directly at the call site (`await new FileOpenPicker().PickSingleFileAsync()`/`FileSavePicker.PickSaveFileAsync()`) and require WinRT window-handle interop (`WinRT.Interop.InitializeWithWindow`) since desktop pickers need an owner HWND — there is no persistent pre-registered launcher object; each pick is a fresh async call. `notifPermLauncher`'s runtime notification-permission prompt also has no WinUI3 desktop equivalent (no Android-13-style runtime permission for toast notifications) and is likely dropped or stubbed in the port.
- LocalHapticFeedback.current / haptic.performHapticFeedback(HapticFeedbackType.LongPress): fired on cell-tap-to-open-editor (line 475) and on confirming a shift pick in the bottom sheet's onPick (line 707). Android-only vibration/haptic CompositionLocal; WinUI3 desktop has no standard haptic feedback API (no vibration hardware) — this should be dropped or become a platform-conditional no-op in the port.
- LocalView.current used only for `rootView.keepScreenOn = ui.running` (line 171): Android View-flag to prevent screen dim/lock during a long-running optimization run. WinUI3 equivalent is `Windows.System.Display.DisplayRequest` with paired `RequestActive()`/`RequestRelease()` calls — a request/release API, not a settable boolean property, so the `LaunchedEffect(ui.running)` pattern that flips a property must become explicit request-on-true/release-on-false calls.
- 'One-hand phone operation' layout convention: `oneHand` state (rememberSaveable, default false) pushes the entire scrollable content Column down via `.padding(top = if (oneHand) 120.dp else 0.dp)` (line 425), and every primary action button uses oversized touch targets (`Modifier.heightIn(min = 60.dp)` in BottomCommandBar/InterruptedBanner, `.height(56.dp)` in EmptyStateCard) — a deliberate mobile-thumb-reach ergonomics decision documented elsewhere in the project as a hard product constraint ('片手一本指'). This has no automatic WinUI3 desktop equivalent (mouse/keyboard/pen input doesn't have 'thumb reach') and likely needs to be revisited as a product decision rather than translated literally.
- ModalBottomSheet (via the externally-defined `ShiftPickerSheet`, gated behind `@OptIn(ExperimentalMaterial3Api::class)` on MagiApp itself, invoked conditionally when `editingCell != null` around line 701): Material3's slide-up-from-bottom overlay-with-scrim pattern. WinUI3 has no built-in 'modal bottom sheet' control — closest equivalents are a custom bottom-anchored `Flyout`/`TeachingTip`, a `ContentDialog`, or a hand-authored overlay `Grid` with a slide-in `Storyboard` animation; requires custom control authoring, not a stock-control swap.
- AlertDialog with a stacked-buttons workaround to exceed the 2-slot (confirmButton/dismissButton) API: three separate AlertDialogs (pendingCsvImport: 5 stacked options in confirmButton + 1 real cancel in dismissButton = 6 buttons total, lines 718-746; rosterCsvChoice: 2 stacked options + 1 cancel, lines 747-766; wishConfirm: 2 stacked options + 1 cancel, lines 767-785) place multiple `DialogConfirmButton`/`DialogDismissButton`-styled composables inside a `Column` in the single `confirmButton` slot, with the *real* cancel action in a separate `dismissButton` slot. WinUI3's `ContentDialog` caps out at exactly 3 named buttons (PrimaryButtonText/SecondaryButtonText/CloseButtonText) — the 6-button pendingCsvImport dialog cannot be expressed as a stock ContentDialog at all and needs a custom dialog body (e.g. a StackPanel/ListView of buttons inside `Content`, with CloseButtonText as the only stock button), while the 2-option+cancel dialogs map cleanly to Primary/Secondary/Close.
- Material Icons (androidx.compose.material.icons.filled.*) — Home/DateRange/Edit/Assessment/Settings (MagiBottomNav), PlayArrow/Stop (BottomCommandBar), DateRange (EmptyStateCard): Material Design iconography with no automatic 1:1 WinUI3 mapping; WinUI3 typically uses Segoe Fluent Icons via `FontIcon`/glyph codepoints or `SymbolIcon`, so each icon needs a manually re-selected, semantically-equivalent Fluent glyph (or a bundled custom icon asset).
- Kotlin trailing-lambda / default-parameter DSL idioms throughout (e.g. `Card(Modifier.fillMaxWidth()) { ... }`, `Button(onClick = ..., modifier = ...) { Text(...) }`, `MagiTopBar(ui: UiState, sectionTitle: String = "勤務表")`, many callback params defaulting to `{}`): idiomatic Compose DSL style with no 1:1 XAML equivalent; each 'trailing content lambda' composable typically becomes either a XAML `<UserControl>` exposing a `ContentPresenter`/named DependencyProperties, or a C# method taking an `Action`/builder delegate — a per-case translation decision, not mechanical.

### 落とし穴

- Orphaned/stale KDoc comment: lines 789-792 contain a doc comment beginning '[DefragLiveView 移植] 計算中の最良盤面ライブ表示。実行中のみ・折りたたみ…' that describes the externally-defined `LiveScheduleCard` composable (which IS called at line 450 inside MagiApp's tab==0 branch), but the comment sits directly above the unrelated `MagiTopBar` function definition (line 795), not above any LiveScheduleCard definition (which isn't even in this file). This is a documentation-drift artifact — do not translate it as describing MagiTopBar.
- This file has a large number of dead/unused imports left over from refactors where composables were extracted to other files: `BorderStroke`, `horizontalScroll`, `BoxWithConstraints`, `FlowRow`/`ExperimentalLayoutApi`, `CircleShape`, `RoundedCornerShape`, `CardDefaults`, `CircularProgressIndicator`, `LinearProgressIndicator`, `drawBehind`, `CornerRadius`, `Offset`, `PathEffect`, `Stroke`, `FontFamily`, `Icons.Filled.CheckCircle`, `Icons.Filled.KeyboardArrowDown`, `Icons.Filled.KeyboardArrowUp`, `mutableStateListOf`, `ImageVector`, `detectHorizontalDragGestures`, `pointerInput`, `java.time.LocalDate`, and the v6-layer types `V6PortReport`, `V6Algorithm`, `CoverageVerdict`, `MirrorKeys` are all imported but never referenced anywhere in this file's actual code (verified by grep against the full source). Do not assume this file directly manipulates those types/APIs just because they're imported — trust only what is actually used in the composable bodies.
- `decodeCsvBytes` decoding strategy: it FIRST attempts strict UTF-8 decode (REPORT on malformed/unmappable input, i.e. fails fast rather than silently substituting), and only on failure falls back to `Charsets.forName("MS932")` (Windows-31J / Shift-JIS variant common in Japanese Excel exports), with a final fallback to lenient UTF-8 if even that throws. It then strips a leading UTF-8 BOM character ('﻿', written in source as a literal BOM char inside the string literal, i.e. `removePrefix("﻿")`). This 3-tier decode-with-fallback + BOM-strip logic must be preserved exactly for CSV import correctness — a naive 'always UTF-8' port will corrupt Shift-JIS files exported from Excel.
- `InputStream.readAtMost(limit = 32MiB)` reads in 64KB chunks and throws `IOException` mid-read (not after buffering everything) the moment the running total exceeds the limit — this is a deliberate OOM guard added because the app previously crashed on oversized picked files with no user-facing error. Any WinUI3 port of the file-pick flow must replicate a bounded/streamed read rather than a naive `File.ReadAllBytes`/`Stream.CopyTo` with no size cap.
- CSV export write-then-verify pattern: every save flow (saveJsonLauncher, saveCsvLauncher, saveComponentCsvLauncher, saveLogLauncher, saveLogJsonLauncher) opens the OutputStream inside a `runCatching { ... } `, throws `FileNotFoundException` explicitly if `openOutputStream(uri)` returns null, and only THEN calls `vm.notifySave(r, what)` with the `Result`. This exists because `ActivityResultContracts.CreateDocument` materializes a 0-byte file at the chosen URI before the callback even runs, so a silently-swallowed write failure previously left a phantom empty file with the user believing the save succeeded — the WinUI3 `FileSavePicker` equivalent needs an equivalent explicit success/failure surfacing, not a fire-and-forget write.
- The `pendingExportKind` String? state (staff/wishes/cons) is a 'stash before firing an async system picker, read back in a later deferred callback' pattern, needed only because a single `rememberLauncherForActivityResult` callback can't otherwise know which of 3 export kinds was requested when it fires later. In WinUI3 the picker call and its continuation live in the SAME async method (`await picker.PickSaveFileAsync()` returns right there), so this stash-and-recall indirection is unnecessary in the port — a plain local variable captured by the async method suffices.
- The `notifPermLauncher` callback comment explicitly states the app proceeds to `vm.runInBackground()` regardless of whether the POST_NOTIFICATIONS permission was granted or denied — the permission result only affects whether the user will SEE progress/completion notifications, not whether the background optimization actually runs. Don't assume a denied-permission branch exists that blocks the background run.
- `focusCell: Pair<Int,Int>?` uses `-1` as a sentinel first component (`focusCell = -1 to j`) to mean 'highlight this day column only, no specific staff row' (see the ViolationHubCard's `onShowDay` callback) — this is a magic-value overload of the pair, not a real staff index, and any WinUI3 port of the equivalent 'jump to grid cell/column' state should make this an explicit nullable-row concept rather than reusing -1.
- `vioMask`/`vioEnabled` derive their filter keys from `vioBuckets` (an externally-defined `List<VioBucket>`) purely by iteration ORDER — `vioMask` is a bitmask where bit `i` corresponds to `vioBuckets[i]`, so if the order/count of `vioBuckets` ever changes across app versions, a `rememberSaveable`-persisted `vioMask` bitmask from a prior session could silently map to the wrong buckets after an update. Any WinUI3 persistence of an equivalent bitmask must either version it or persist bucket keys directly instead of positional bits.
- `Column` root layout uses `.verticalScroll(rememberScrollState())` directly (not `LazyColumn`) despite containing potentially dozens of large card composables across all tabs — every tab's full content tree is composed even when off-screen within the scroll, since there's no lazy/virtualized layout here. A literal WinUI3 `StackPanel` inside a `ScrollViewer` would have the same non-virtualized characteristic; if performance requires it, a `ItemsRepeater`/virtualizing panel would be a deliberate deviation from this file's structure, not a direct port.
- Many Japanese inline comments throughout this file reference internal project version tags in brackets like '[3.409.12]', '[3.398.0]', '[3.405.0]', '3.286.0 冗長性A' — these are historical changelog/rationale references (explaining WHY a piece of UI is shaped the way it is, often to avoid a previously-encountered regression) and should be read for INTENT during translation, but the version tags themselves carry no meaning outside this project's own changelog and should not be treated as something to preserve or look up.

### 外部依存

- `MagiViewModel (com.magi.app.ui) — the ViewModel type; nearly every action in this file calls a method on it`
- `UiState (com.magi.app.ui) — the observed state type read throughout as `ui.xxx``
- `OperatorNextActionCard, LiveScheduleCard, CopilotCard, CoverageDiagnosisCard, ForbiddenRunDiagnosisCard, C1PlateauCard, PinFixedImpactCard, SettingIssuesCard, AlternativesCard — Home-tab (tab 0) card composables`
- `WishApplyCard, ViolationFilterBar, SearchLegendBar, ScheduleGrid, TallyCard, WishBulkSheet — Schedule-tab (tab 1) composables`
- `SetupGuideCard, MonthlyChecklistCard, MonthPickerCard, WishCard, NeedCalendarCard, NeedDayCard, StaffManageCard, ReviewMemoCard, StaffingRealityCard, Ws1Card, SkillGroupCard, CountsCard, ConstraintsCard, SkillConstraintsCard — Edit-tab (tab 2, various editScope sub-scopes) composables`
- `ViolationHubCard, V6DashboardCard, FixSuggestionCard — Analysis-tab (tab 3) composables`
- `AppearanceCard, ShiftColorCard, ColorSettingsView, DataActionsCard, SettingsCard, WeightTableCard, AdvancedSettingsSection — Settings-tab (tab 4) composables`
- `MagiSetupCards.CollapsibleSection, MagiSetupCards.SectionNote — collapsible year-master section wrapper + note text used in the Edit tab's else-branch`
- `MagiComponents.MagiSegmentedControl — 2/3-option segmented control used for the edit-scope selector and the general/pro toggle`
- `Affordance.DialogConfirmButton, Affordance.DialogDismissButton — shared AlertDialog button styles used by all four AlertDialogs in this file`
- `VioBuckets.vioBuckets (List<VioBucket>, each with a `.key`) and MagiScheduleViews.vioBucketLocCounts(ui) — violation-family filter bucket definitions/counts backing `vioMask`/`vioEnabled`/ViolationFilterBar/ViolationHubCard`
- `ShiftPickerSheet — external ModalBottomSheet composable for picking a shift for a cell (see dialogsAndOverlays)`
- `GuidedFixDialog — external dialog composable for the 'なおすのを手伝って' guided-fix flow`
- `com.magi.app.v6.RosterCsvImport.detect(csvText: String): Boolean — v6-layer object method used to decide whether a freshly-picked CSV looks like a full roster/unit-column template (routes to the rosterCsvChoice dialog) vs a plain schedule overlay`


---

## MagiDashboardCards.kt

### 役割

Home/Analysis-tab dashboard card composables for the MAGI ShiftOptimizer app: the "next action" hero card (OperatorNextActionCard), a guided-fix AlertDialog for coverage shortages, a family of read-only diagnostic cards (coverage shortage/surplus, forbidden-sequence "wall" diagnosis, C1-window-plateau reasons, pin-lock impact, setting-issue suggestions), the V6 native-engine metrics dashboard and a weight/priority table, plus the consolidated "violation hub" card (list / by-day-staff / breakdown views) and the fix-suggestion / alternative-solution / wish-apply cards. With the sole exception of GuidedFixDialog (which takes a MagiViewModel and calls vm.shortageFixCandidates/vm.setCell directly), every composable here is purely presentational: it reads an immutable UiState snapshot and invokes caller-supplied lambda callbacks — no business logic, scoring, or engine calls live in this file.

### Composable関数

#### `GuidedFixDialog`

```kotlin
internal fun GuidedFixDialog(ui: UiState, vm: MagiViewModel, onDismiss: () -> Unit, onRerun: () -> Unit)
```

**描画内容:** An AlertDialog whose title/body switch between 4 mutually-exclusive branches evaluated in order: (1) an actionable non-blocked shortage exists ('target') -> shows the shortfall text plus up to 8 candidate-staff buttons (or the diagnosis reason text if no candidates); (2) INFEASIBLE shortfalls remain -> lists up to 4 with reasons; (3) FIXABLE-but-blockedNow shortfalls remain -> lists up to 4 with reasons and a 'won't change on rerun' notice; (4) else -> an all-clear message. confirmButton is 'もう一度つくる' (only when allDone) else a '閉じる' dismiss button; dismissButton shows a second '閉じる' only when allDone.

**ViewModel呼出:**

- `vm.shortageFixCandidates`
- `vm.setCell`

**読むUiStateフィールド:**

- `ui.coverageDiag`

**Compose固有ローカル状態:**

- cands: List<...> (candidate type returned by vm.shortageFixCandidates, fields staffIndex/name/fromRest observed) — remember(target.dayIndex, target.shiftIndex, ui.coverageDiag) { vm.shortageFixCandidates(target.dayIndex, target.shiftIndex) }, computed on first composition / recomputed when the keys change


#### `OperatorNextActionCard`

```kotlin
internal fun OperatorNextActionCard(ui: UiState, onMake: () -> Unit, onSmartInitial: () -> Unit, onStop: () -> Unit, onExport: () -> Unit, onSchedule: () -> Unit, onFix: () -> Unit, onSetup: () -> Unit)
```

**描画内容:** A single Card whose color/headline/big-button/helper-button are chosen by a 5-branch `when` over ui.running / !ui.hasResult / ui.bestHard==0L / infeasible / else, producing an OpNextPlan. Shows an optional phase badge ('探索'/'完成'/'狩猟') when not running, the headline, a 'people short / completion %' line, an optional result-provenance footnote, a running-state progress row (CircularProgressIndicator + progressSummary text) or a big Button + optional OutlinedButton helper, and (when hasResult && !running) a bottom TextButton to regenerate the draft.

**読むUiStateフィールド:**

- `ui.running`
- `ui.coverageDiag`
- `ui.hasResult`
- `ui.bestHard`
- `ui.satisfaction`


#### `CopilotCard`

```kotlin
internal fun CopilotCard(ui: UiState, onGoEdit: () -> Unit, onSoftPolish: () -> Unit = {})
```

**描画内容:** Optional Card (returns without rendering if nothing to show) with up to 3 warning/advice Surfaces: an errorContainer block for impossible-wish count with an 'edit wishes' button, a secondaryContainer block for ui.copilotHint with an 'edit in tab' button, and a tertiaryContainer block (shown when polishExhausted && !running) offering '自動で整える' / '手修正' buttons.

**読むUiStateフィールド:**

- `ui.impossibleWishCount`
- `ui.copilotHint`
- `ui.polishExhausted`
- `ui.running`


#### `CoverageDiagnosisCard`

```kotlin
internal fun CoverageDiagnosisCard(ui: UiState)
```

**描画内容:** Returns without rendering if ui.coverageDiag is null or has neither shortage nor surplus. Otherwise a Card with two optional sections: a shortage section (dynamic headline text based on allInfeasible/allBlockedNow/blockedNowSlots/infeasibleSlots, up to 6 Surface rows each showing day/shift/need/got/miss plus a status MagiTagChip('充足不可'/'今は不能'/'充足可能') and reason text, plus an optional 'relaxations' suggestion block); and a surplus section (up to 6 Surface rows showing day/shift/need/got/excess/reason with an optional '(主因: X)' family label via breakdownLabels).

**読むUiStateフィールド:**

- `ui.coverageDiag`


#### `ForbiddenRunDiagnosisCard`

```kotlin
internal fun ForbiddenRunDiagnosisCard(ui: UiState, onRelaxRule: (String) -> Unit = {})
```

**描画内容:** Returns without rendering if ui.forbiddenDiag is null or has no runs. Otherwise a Card listing up to 6 forbidden-sequence 'run' rows (staffName/seqLabel, a '崩せない'/'崩す手あり' MagiTagChip, a per-cell escape-tag line built from com.magi.app.v6.ForbiddenCellEscape, and a hint text), plus a lower 'walls' section that groups the un-escapable runs by seqLabel and offers a per-rule TextButton ('この並びの禁止をやめる') wired to onRelaxRule(seqLabel), enabled=!ui.running.

**読むUiStateフィールド:**

- `ui.forbiddenDiag`
- `ui.running`


#### `C1PlateauCard`

```kotlin
internal fun C1PlateauCard(ui: UiState, onGoEdit: () -> Unit = {})
```

**描画内容:** Returns without rendering if ui.c1Plateau is null. If diag.causeUnknown, shows a short 'cause not determined' Card and returns. Else, if !diag.hasEntries returns nothing; otherwise a Card listing up to 6 entries (label, a cause MagiTagChip via com.magi.app.v6.C1PlateauCause, a breakdown of rejectedByPin/rejectedByScore/noCandidate counts, and a recommendedAction text resolved through breakdownLabels), plus a conditional 'review personal counts' TextButton when pinConstrained>0.

**読むUiStateフィールド:**

- `ui.c1Plateau`
- `ui.running`


#### `PinFixedImpactCard`

```kotlin
internal fun PinFixedImpactCard(ui: UiState, onGoEdit: () -> Unit = {}, onRelax: (Int, Int, Int, Int) -> Unit = { _, _, _, _ -> })
```

**描画内容:** Returns without rendering if ui.observedPinBlockedAttempts <= 0. Otherwise a Card explaining how many polish attempts were rejected solely due to a fixed (lo==hi) personal count, plus (when ui.pinTargets is non-empty) up to 5 per-target Surface rows each with 'lower by 1' / 'raise by 1' TextButtons wired to onRelax(staff, shift, delta, delta), enabled=!ui.running, and a final 'review personal counts' TextButton.

**読むUiStateフィールド:**

- `ui.observedPinBlockedAttempts`
- `ui.pinTargets`
- `ui.running`


#### `SettingIssuesCard`

```kotlin
internal fun SettingIssuesCard(ui: UiState, onFix: (com.magi.app.v6.SettingIssue) -> Unit, onGoEdit: () -> Unit)
```

**描画内容:** Returns without rendering if ui.settingIssues is empty. Otherwise a Card listing up to 6 issues, each an errorContainer Surface with a kind MagiTagChip (com.magi.app.v6.IssueKind: WISH/CONSTRAINT/DEMAND/RANGE mapped to Japanese label + color), the 'where'/'problem'/'fix' text, and an optional action Button (onFix(s)) when s.actionLabel is non-empty; ends with an overflow note and an 'edit settings/wishes' OutlinedButton.

**読むUiStateフィールド:**

- `ui.settingIssues`


#### `V6DashboardCard`

```kotlin
internal fun V6DashboardCard(v6: V6PortReport?)
```

**描画内容:** Returns without rendering if v6 is null. Otherwise a 'pro mode' engine-metrics Card: an optional coverage% MagiScoreGauge, a Row of 3 BigStat tiles (HARD Core / Guard / 充足%), a top-risk headline, a monospace metrics line (Apt/Equalize/Demand/covU), and up to 3 sanity-warning lines in error color. Note: takes a V6PortReport parameter directly, NOT a UiState, so it has no ui.xxx field reads.


#### `WeightTableCard`

```kotlin
internal fun WeightTableCard()
```

**描画内容:** A parameterless Card ('直す優先順位') that reads the static external map MirrorKeys.weights directly, splits entries into 'hard' (weight >= 1000.0) and 'soft' (< 1000.0) groups sorted descending by weight, and renders each as a Row of (Japanese label via breakdownLabels) + '×<weight>' text, hard rows in error color.


#### `ConfirmFilterChip`

```kotlin
private fun ConfirmFilterChip(label: String, count: Int, selected: Boolean, onClick: () -> Unit)
```

**描画内容:** A small pill-shaped clickable Surface showing '<label> <count>', bold when selected, tinted secondaryContainer when selected else surfaceVariant.


#### `ConfirmRow`

```kotlin
private fun ConfirmRow(item: ConfirmItem, onFocusStaff: (Int) -> Unit, onShowCell: (Int, Int) -> Unit, onShowDay: (Int) -> Unit, onFixWish: (Int) -> Unit = {}, onFixNeed: (Int) -> Unit = {})
```

**描画内容:** A single tappable Surface row: a colored badge Box with item.mark (2-char glyph, color/bg keyed by item.kind: 0=error, 1=warn, 2=tertiary), a Column of item.title/item.sub text, and a trailing element that is either a '設定で直す' TextButton (when item.wishStaff or item.needShift is non-null, calling onFixWish/onFixNeed) or a '直し方→'/'勤務表→' hint Text (when clickable). Row itself is clickable when item.staff!=null or (staff==null && day!=null), dispatching to onShowCell/onFocusStaff/onShowDay accordingly.


#### `ViolationHubCard`

```kotlin
internal fun ViolationHubCard(ui: UiState, vioEnabled: Set<String>, onToggleBucket: (String) -> Unit, onFocusStaff: (Int) -> Unit, onGoEdit: () -> Unit, proMode: Boolean = false, onShowCell: (Int, Int) -> Unit = { i, _ -> onFocusStaff(i) }, onShowDay: (Int) -> Unit = {}, onFixWish: (Int) -> Unit = {}, onFixNeed: (Int) -> Unit = {})
```

**描画内容:** The consolidated Home/Analysis violations card (replaces the old ConfirmListCard/AttentionCardsSection/BreakdownCard). Has an 'all clear' early-return branch (Card with only a success message, shown when totalItems==0 && issueCount==0 && !hasBreakdownViolations, AND only if ui.schedule.isNotEmpty() && !ui.running). Otherwise renders a Card with: a title with locCount, an optional 'setting issues' banner with a 'go to settings' TextButton, a ViolationBucketChips filter row (shared with the schedule tab), a MagiSegmentedControl (一覧/日別・人別/内訳) dispatching to ConfirmListBody/AttentionBody/BreakdownBody, and an optional 'values not yet final' footnote when running.

**読むUiStateフィールド:**

- `ui.settingIssues`
- `ui.violationCells`
- `ui.violationCellFamilies`
- `ui.needViolations`
- `ui.countViolations`
- `ui.schedule`
- `ui.staffNames`
- `ui.shiftSymbols`
- `ui.startDate`
- `ui.breakdown`
- `ui.running`

**Compose固有ローカル状態:**

- totalItems: Int = remember(ui.violationCells, ui.violationCellFamilies, ui.needViolations, ui.countViolations, ui.schedule, ui.staffNames, ui.shiftSymbols, ui.startDate) { confirmItems(ui) }.size
- filteredUi: UiState = remember(ui, vioEnabled) { applyVioFilter(ui, vioEnabled) }
- mode: Int = rememberSaveable { mutableIntStateOf(0) } — initial 0 (0=一覧 1=日別・人別 2=内訳)


#### `ConfirmListBody`

```kotlin
private fun ConfirmListBody(ui: UiState, onFocusStaff: (Int) -> Unit, proMode: Boolean, onShowCell: (Int, Int) -> Unit, onShowDay: (Int) -> Unit, onFixWish: (Int) -> Unit, onFixNeed: (Int) -> Unit)
```

**描画内容:** Body content (no own Card) for the '一覧' segment of ViolationHubCard: an optional hint text, a horizontally-scrollable Row of ConfirmFilterChip('全部'/'不足・必須'/'過剰・要調整'/'窓') filtered by kind, and either an empty-state text or up to 40 ConfirmRow items (with an overflow note beyond 40).

**読むUiStateフィールド:**

- `ui.violationCells`
- `ui.violationCellFamilies`
- `ui.needViolations`
- `ui.countViolations`
- `ui.schedule`
- `ui.staffNames`
- `ui.shiftSymbols`
- `ui.startDate`

**Compose固有ローカル状態:**

- items: List<ConfirmItem> = remember(ui.violationCells, ui.violationCellFamilies, ui.needViolations, ui.countViolations, ui.schedule, ui.staffNames, ui.shiftSymbols, ui.startDate) { confirmItems(ui) }
- filter: Int = rememberSaveable { mutableStateOf(-1) } — initial -1 (-1=全部 0=不足・必須 1=過剰・要調整 2=窓)


#### `AttentionBody`

```kotlin
private fun AttentionBody(ui: UiState, onFocusStaff: (Int) -> Unit, onShowDay: (Int) -> Unit)
```

**描画内容:** Body content for the '日別・人別' segment: returns nothing if ui.schedule is empty. Otherwise a 'want alerts only' Switch, a MagiSegmentedControl (日別/人別), and a list of AttentionRow items — day rows (0 until days) show dayMD(ui.startDate,j) title, aggregated shift+direction sub text, alert count from dayAlerts, clickable->onShowDay(j) when count>0; staff rows (0 until staffCount) show staff name title, aggregated shift+direction sub text, alert count from staffAlerts, clickable->onFocusStaff(i) when count>0. All aggregation (staffAlerts/staffShifts/dayAlerts/dayShifts HashMaps) is built inline from ui.countViolations/ui.violationCells/ui.needViolations on each recomposition.

**読むUiStateフィールド:**

- `ui.schedule`
- `ui.countViolations`
- `ui.violationCells`
- `ui.needViolations`
- `ui.staffNames`
- `ui.shiftSymbols`
- `ui.startDate`

**Compose固有ローカル状態:**

- mode: Int = rememberSaveable { mutableIntStateOf(0) } — initial 0 (0=日別 1=人別)
- alertOnly: Boolean = rememberSaveable { mutableStateOf(true) } — initial true


#### `AttentionRow`

```kotlin
private fun AttentionRow(title: String, sub: String, alerts: Int, warnBg: Color, warnFg: Color, onClick: (() -> Unit)?, hint: String = "直し方→")
```

**描画内容:** A generic list row (Surface) with a title/sub Column, an optional alert-count badge Surface (shown when alerts>0), and an optional trailing hint Text; row is clickable only when onClick != null.


#### `BreakdownBody`

```kotlin
private fun BreakdownBody(ui: UiState, onFocusStaff: (Int) -> Unit, proMode: Boolean)
```

**描画内容:** Body content for the '内訳' segment: a 'critical only' Switch, an optional hint text (non-pro mode), three BreakdownGroup sections ('必須'/'人数の範囲'/'任意' — the latter two hidden when criticalOnly is true), and (when a chip is `expanded`) an inline expanding Surface panel listing breakdownLocations(key, ui) with a per-location clickable Text (staff-linked -> onFocusStaff) or plain Text (unlinked), a 'close' TextButton, and a running-state footnote.

**読むUiStateフィールド:**

- `ui.breakdown`

**Compose固有ローカル状態:**

- criticalOnly: Boolean = rememberSaveable { mutableStateOf(false) } — initial false
- expanded: String? = rememberSaveable { mutableStateOf<String?>(null) } — initial null (currently expanded family key)


#### `BreakdownGroup`

```kotlin
internal fun BreakdownGroup(title: String, keys: List<String>, severity: Int, ui: UiState, labels: Map<String, String>, expanded: String?, onTap: (String) -> Unit)
```

**描画内容:** A titled Column of SeverityChip rows, laid out 2-per-Row via keys.chunked(2) (padding the last odd row with a Spacer). Reads ui.breakdown[key] directly (not inside SeverityChip) to compute each chip's count.

**読むUiStateフィールド:**

- `ui.breakdown`


#### `SeverityChip`

```kotlin
internal fun SeverityChip(label: String, count: Int, severity: Int, famKey: String, expanded: Boolean, onTap: (String) -> Unit, modifier: Modifier = Modifier)
```

**描画内容:** A single stat chip Surface: label + count Text, an up/down chevron Icon shown only when count>0 (active), colored by severity (0=primary,1=secondary,2=error containers) when active else surfaceVariant/onSurfaceVariant; clickable (onTap(famKey)) and given a semantics contentDescription only when active. Takes count: Int directly, NOT a UiState.


#### `BigStat`

```kotlin
internal fun BigStat(label: String, value: String, modifier: Modifier = Modifier)
```

**描画内容:** A simple stat tile Surface: large bold single-line value Text over a smaller centered label Text.


#### `FixSuggestionCard`

```kotlin
internal fun FixSuggestionCard(ui: UiState, onSearch: () -> Unit, onApply: (com.magi.app.v6.FixSuggestion) -> Unit, proMode: Boolean = false)
```

**描画内容:** A Card with a title (optionally suffixed with ui.fixFocusName), a '探す'/'全体で再探索' TextButton (hidden, replaced by '探索中…' text, while ui.fixSearching), an optional hint text, and either a searching/empty-state message or a list of ui.fixSuggestions rows — each a Surface with a fixKindTag MagiTagChip, label, a diff summary line (family label + signed delta via breakdownLabels), a total-delta line, and an 'この手を適用' Button (onApply(s), enabled=!ui.running).

**読むUiStateフィールド:**

- `ui.fixFocusName`
- `ui.fixSearching`
- `ui.fixSuggestions`
- `ui.running`


#### `AlternativesCard`

```kotlin
internal fun AlternativesCard(ui: UiState, onApply: (Int) -> Unit)
```

**描画内容:** Returns without rendering if ui.alternatives is empty. Otherwise a Card titled '他の案（N）' listing each alternative String in a Row with an '採用' OutlinedButton (onApply(index), enabled=!ui.running).

**読むUiStateフィールド:**

- `ui.alternatives`
- `ui.running`


#### `WishApplyCard`

```kotlin
internal fun WishApplyCard(ui: UiState, onApply: () -> Unit)
```

**描画内容:** Returns without rendering if !ui.loaded. Otherwise a single-Row Card ('希望シフトを反映' title + description) with a trailing '希望を反映する' OutlinedButton (onApply, enabled=!ui.running).

**読むUiStateフィールド:**

- `ui.loaded`
- `ui.running`


### ヘルパー関数

- `breakdownLocations`（`internal fun breakdownLocations(famKey: String, ui: UiState): List<Pair<String, Int?>>`）: Resolves a breakdown family key (e.g. "low","covU","c1","fair","weekly") to a list of (display-text, staffIndex-or-null) pairs describing every violation 'location' for that family. Branches on famKey into 4 shapes: (a) low/high/c2/apt -> per-staff-per-shift via ui.countFamilies (falling back to ui.countViolations) keyed "i,k"; (b) covU/covO/c41/c41s -> per-shift-per-day via ui.needFamilies (falling back to ui.needViolations) keyed "k,j", staff always null; (c) weekly/fair -> ui.distLocations[famKey] triples [staff,shift,deviation]; (d) everything else -> per-staff-per-day cell via ui.violationCellFamilies (falling back to ui.violationCells) keyed "i,j", reading the actual assigned shift symbol from ui.schedule[i][j]. Used by BreakdownBody's expanded-panel and (indirectly) informs the tap targets.
- `confirmItems`（`private fun confirmItems(ui: UiState): List<ConfirmItem>`）: Flattens the three raw per-cell violation maps into one sorted list of ConfirmItem rows for the '一覧' view: iterates ui.needViolations (covU/covO/c41/c41s, keyed "k,j") producing kind 0/1 rows with needShift set for covU/covO; iterates ui.countViolations (low/high/c2/aptLow/aptHigh, keyed "i,k") producing kind 0/1 rows; iterates ui.violationCells (c1/pref/groupViol/c3n/c3/c3m/c3mn/c42/c42s, keyed "i,j") producing kind 0/1/2 rows with wishStaff set when 'pref' is among the cell's families (from ui.violationCellFamilies) and sub built by joining all overlapping family labels. Final list is sortedWith(compareBy(kind, order)).
- `fixKindTag`（`private fun fixKindTag(k: com.magi.app.v6.FixKind): Pair<String, androidx.compose.ui.graphics.Color>`）: Maps each com.magi.app.v6.FixKind enum value (CHANGE, CHANGE_MULTI, SWAP, SWAP_XDAY, SWAP_MULTI, CHAIN, WINDOW) to a (Japanese chip label, MagiAccent color) pair — CHANGE/CHANGE_MULTI->green '変更'/'複数変更', SWAP/SWAP_XDAY->blue '交換'/'別日交換', SWAP_MULTI->purple '3人交換', CHAIN->red '連鎖', WINDOW->orange '再最適化' — used as the tag chip on each FixSuggestionCard suggestion row.

### ダイアログ/オーバーレイ

- AlertDialog in GuidedFixDialog (the entire function body): triggered externally by the caller (e.g. Home tab's 'なおすのを手伝って' button, wired via the onFix callback of OperatorNextActionCard — the actual open/close trigger lives outside this file). Title toggles between 'なおすのを手伝います' and '直し終わりました！' (when allDone). Body renders one of 4 branches (target-available / infeasible / blocked / all-clear) as described in the GuidedFixDialog composable entry. confirmButton = DialogConfirmButton('もう一度つくる', onRerun) only when allDone, else DialogDismissButton('閉じる', onDismiss); dismissButton slot shows a second DialogDismissButton('閉じる', onDismiss) only when allDone (comment: '修正中は「閉じる」だけ…完了時のみ第2ボタンを出す').
- No ModalBottomSheet, DropdownMenu, or Snackbar is actually used anywhere in this file, despite `ModalBottomSheet`/`rememberModalBottomSheetState`/`Scaffold`/`NavigationBar`/`NavigationBarItem` being imported at the top of the file — these are dead imports, not real overlays; do not port a bottom sheet or nav-bar structure for this file.
- BreakdownBody's `expanded`-family panel (a plain Surface shown conditionally below the BreakdownGroup rows, with a header Row + 'close' TextButton) is an inline in-place expander/accordion, NOT a modal/overlay dialog — it participates in the same scrollable Column as the rest of the card and should be ported as an expander/disclosure element embedded in the page, not a Popup/ContentDialog/Flyout.

### Android/Compose固有パターン（要変換方針）

- remember(key1, key2, ...) { computation } used for memoized derived values keyed on multiple UiState collection fields at once (GuidedFixDialog's `cands`; ViolationHubCard's `totalItems`/`filteredUi`; ConfirmListBody's `items`) — in WinUI3/x:Bind this is a computed property recalculated when ANY of those bound collections raises PropertyChanged/CollectionChanged, or an explicit cache invalidated in the ViewModel whenever the source properties are set.
- rememberSaveable { mutableStateOf(...) } / mutableIntStateOf(...) used for small local UI toggle/filter state that must survive process death/config change but is NOT part of UiState/the ViewModel (ViolationHubCard.mode; ConfirmListBody.filter; AttentionBody.mode/alertOnly; BreakdownBody.criticalOnly/expanded) — in WinUI3 this maps to page-instance fields; true 'survive app restart' persistence would need explicit SuspensionManager/ApplicationData.LocalSettings wiring, which Compose's rememberSaveable gives 'for free' via the Activity's saved-instance-state bundle.
- LaunchedEffect(ui.breakdown) { ... } in BreakdownBody — a declarative side-effect that re-runs whenever the `ui.breakdown` Map reference changes, and closes the `expanded` accordion panel if that key's count has dropped to 0. In WinUI3/MVVM this is NOT a recomposition-triggered effect; it must become an explicit reaction to the bound Breakdown collection changing (e.g. in the ViewModel's setter for the Breakdown property, or a CollectionChanged handler in code-behind) that clears the ExpandedFamilyKey property.
- Composable functions with default lambda parameters that reference sibling parameters, e.g. `onShowCell: (Int, Int) -> Unit = { i, _ -> onFocusStaff(i) }` in ViolationHubCard — Kotlin default-arg closures capturing another parameter. C#/WinUI3 has no direct equivalent; needs an explicit null-check-and-fallback in code-behind, or requiring the caller to always supply both delegates.
- Kotlin destructuring of a Pair-returning external function, e.g. `val (amber, onAmber) = magiWarnColors()` (OperatorNextActionCard) and `val (warnBg, warnFg) = magiWarnColors()` (ConfirmRow) — port as C# tuple deconstruction `var (amber, onAmber) = MagiWarnColors();`.
- Modifier.semantics { contentDescription = "..." } for screen-reader narration on tappable rows (ConfirmRow, SeverityChip) — maps to AutomationProperties.Name in WinUI3 XAML.
- Imperative conditional Modifier building, e.g. `var m = Modifier.fillMaxWidth().heightIn(min = 56.dp); if (clickable) m = m.clickable { ... }.semantics { ... }` (ConfirmRow), and `var m = Modifier...; if (expanded) m = m.border(...); if (active) m = m.clickable { ... }` (SeverityChip), and `var m = Modifier...; if (onClick != null) m = m.clickable { onClick() }` (AttentionRow) — a Compose idiom of building up a single Modifier chain based on runtime flags. In XAML this becomes multiple bound properties (Cursor/IsHitTestVisible/BorderBrush/BorderThickness) applied via converters or VisualStateManager rather than one fluent chain.
- when-blocks over v6-package enums used fully-qualified inline rather than via imported unqualified members: com.magi.app.v6.CoverageVerdict.INFEASIBLE, com.magi.app.v6.ForbiddenCellEscape.{FREE,CHAIN,ADJACENT,PINNED,BLOCKED}, com.magi.app.v6.C1PlateauCause.{PIN_CONSTRAINED,SCORE_TRADEOFF,NO_CANDIDATE}, com.magi.app.v6.IssueKind.{WISH,CONSTRAINT,DEMAND,RANGE} — each maps to a C# enum + switch-expression (or a Converter class) producing a (label, Brush) pair.
- MaterialTheme.colorScheme / MaterialTheme.typography / MaterialTheme.shapes referenced pervasively for theming (cs.primaryContainer, cs.onPrimaryContainer, cs.errorContainer, cs.tertiaryContainer, cs.surfaceVariant, typography.titleMedium/bodyMedium/labelSmall/etc, shapes.small/medium) — must map to a WinUI3 ThemeResource/StaticResource dictionary (Fluent DynamicResource brushes for colors, TextBlock Style resources for the type scale, CornerRadius resources for shapes).
- Trailing-lambda 'content slot' nesting for Card { Column { ... } } / Row { ... } / Surface { ... } used ~80+ times across this file — WinUI3 has no lambda-slot equivalent; each becomes an explicit nested Grid/StackPanel/Border element tree in XAML with corresponding named children for any conditionally-shown branch.
- Widespread early-return-nothing pattern at the top of a @Composable (`if (issues.isEmpty()) return`, `val diag = ui.c1Plateau ?: return`, `if (!diag.hasRuns) return`, `if (ui.alternatives.isEmpty()) return`, `if (!ui.loaded) return`) used ~10 times to conditionally omit an entire card from the tree — must become a bound Visibility=Collapsed/Visible (via a BoolToVisibility or null-to-Visibility converter) on the equivalent XAML panel rather than a code-behind early return, since XAML trees are declared, not conditionally constructed per-frame.
- Kotlin collection-pipeline idioms used throughout that need LINQ translation: .mapNotNull{} (filter+select with null-drop), .distinct(), .groupBy{}, .sortedByDescending{}/.sortedWith(compareBy(...)), .chunked(2), .take(N), .joinToString(sep){...}, .filter{}.firstOrNull{} — e.g. ForbiddenRunDiagnosisCard's wall-grouping pipeline `diag.runs.filter{!it.escapable}.groupBy{it.seqLabel}.toList().sortedByDescending{it.second.size}`.

### 落とし穴

- Architectural asymmetry: only GuidedFixDialog takes `vm: MagiViewModel` and calls vm.xxx() directly (vm.shortageFixCandidates, vm.setCell). Every other composable in this file takes only `ui: UiState` plus caller-supplied lambda callbacks (onMake, onFix, onApply, onFocusStaff, onShowCell, onShowDay, onFixWish, onFixNeed, onRelax, onRelaxRule, onGoEdit, etc.) — the actual wiring of those callbacks to real ViewModel methods happens OUTSIDE this file (in the Home/Analysis tab host, not read as part of this task). When porting, only GuidedFixDialog's code-behind should call into the ViewModel; everything else should bind to VM-exposed properties/commands passed down.
- `OpNextPlan` (internal class, NOT a composable, ~line 200) is a plain value holder returned by a 5-branch `when` inside OperatorNextActionCard: fields container: Color, fg: Color, headline: String, bigLabel: String, bigAction: () -> Unit, bigEnabled: Boolean, helperLabel: String?, helperAction: () -> Unit. Port this as a small record/POCO computed by a switch expression on (running, hasResult, bestHard, infeasible) — note it carries executable delegates (bigAction/helperAction), not just display data.
- `ConfirmItem` (private data class, ~line 928) fields: kind: Int (0=不足/必須, 1=過剰/調整, 2=窓/c1), mark: String (<=2-char badge glyph), title: String, sub: String, staff: Int?, day: Int?, order: Int (composite sort key), wishStaff: Int? = null, needShift: Int? = null. Produced only by confirmItems(ui) and consumed only by ConfirmRow / ConfirmListBody / ViolationHubCard.totalItems — treat as a lightweight view-row POCO in the port, not part of the persisted domain model.
- This file contains a large number of UNUSED imports that are NOT exercised anywhere in the composable bodies (verified via grep, not by inference) — do not treat their presence as evidence of real functionality: rememberLauncherForActivityResult, ActivityResultContracts, BoxWithConstraints, FlowRow, rememberCoroutineScope, drawBehind, CornerRadius, Offset, Size, PathEffect, androidx.compose.ui.graphics.drawscope.Stroke, kotlinx.coroutines.Dispatchers/launch/withContext, ModalBottomSheet, rememberModalBottomSheetState, NavigationBar, NavigationBarItem, Scaffold, mutableStateListOf, LocalHapticFeedback, HapticFeedbackType, detectHorizontalDragGestures, pointerInput, com.magi.app.v6.V6Algorithm, and most Icons.Filled.* (Add, Assessment, CheckCircle, DateRange, Edit, Home, PlayArrow, Settings, Stop, Warning) — only Icons.Filled.KeyboardArrowUp/KeyboardArrowDown are actually used (in SeverityChip).
- `ViolationHubCard` (3.459.0 per code comments) consolidates 3 formerly-separate cards (ConfirmListCard/AttentionCardsSection/BreakdownCard) into one Card with a MagiSegmentedControl dispatching to 3 private 'body' composables (ConfirmListBody/AttentionBody/BreakdownBody) that render their content WITHOUT their own Card wrapper — the outer ViolationHubCard owns the single Card/padding/title. Its 'all achieved' early-exit is a structurally DIFFERENT small Card (only rendered when ui.schedule.isNotEmpty() && !ui.running, in addition to the zero-violation checks), not just a visibility toggle on the same tree.
- ViolationHubCard's achievement check deliberately combines TWO independent conditions: `totalItems == 0` (from confirmItems(ui), covering violationCells/needViolations/countViolations) AND `!hasBreakdownViolations` (`ui.breakdown.values.any { it > 0 }`) — because the 'fair' and 'weekly' families are aggregate deviation metrics with NO per-cell location (they never appear in violationCells/needViolations/countViolations, only via ui.distLocations), so checking totalItems alone could show a false 'all clear' when fair/weekly violations remain. Comment explicitly calls this out as fixing an 'external review P1-01' bug.
- `ConfirmListBody`'s `filter` (rememberSaveable Int) is defensively clamped every recomposition into `effFilter`: if the currently-selected filter kind's count has dropped to 0 (e.g. re-optimizing eliminates all 'c1/窓' violations while filter==2 is selected), it silently falls back to -1 ('全部'/show-all) instead of leaving an empty list with a stale selected chip. Port this reset-on-zero-count logic explicitly.
- `breakdownLocations(famKey, ui)` has FOUR structurally different resolution paths depending on famKey, and each reads from a different UiState map — getting the wrong shape breaks the tap-to-see-locations feature: (a) low/high/c2/apt -> ui.countFamilies (fallback ui.countViolations) keyed "i,k"; (b) covU/covO/c41/c41s -> ui.needFamilies (fallback ui.needViolations) keyed "k,j" with staffIndex always null (not person-linked); (c) weekly/fair -> ui.distLocations[famKey] (list of [staff,shift,deviation] Int triples), a completely separate data source; (d) all other families (c1,c3,c3n,c3m,c3mn,c42,c42s,pref,groupViol) -> ui.violationCellFamilies (fallback ui.violationCells) keyed "i,j", where the displayed shift symbol is read live from ui.schedule[i][j] rather than derived from the family key.
- `confirmItems(ui)`'s ConfirmItem.order field is a magic-number composite sort key of the form `kind*100000 + <band-offset> + <secondaryIndex>*100 + <tertiaryIndex>` (needViolations items: `kind*100000 + j*100 + k`; countViolations items add a `+40000` band offset; violationCells items add a `+80000` band offset) purely so the 3 heterogeneous source maps interleave deterministically under `sortedWith(compareBy({it.kind},{it.order}))`. Replace with an explicit multi-key OrderBy/ThenBy in the port rather than literally porting the arithmetic.
- The `ui.running` flag gates 3 DIFFERENT things simultaneously across multiple cards in this file: (1) disabling action buttons (`enabled = !ui.running` — PinFixedImpactCard's relax buttons, C1PlateauCard's edit button, ForbiddenRunDiagnosisCard's relax-rule button, FixSuggestionCard's apply button, AlternativesCard's adopt button, WishApplyCard's apply button); (2) showing a '※実行中のため確定前の値です' / '実行中です。確定後に…' not-yet-final disclaimer text (ViolationHubCard, BreakdownBody's expanded panel); (3) suppressing the achievement/'all clear' branch of ViolationHubCard entirely. A single bound IsRunning VM property must drive all three per affected card.
- 5 different diagnostic cards (SettingIssuesCard `.take(6)`, CoverageDiagnosisCard `.take(6)` twice for shortfalls/surpluses, ForbiddenRunDiagnosisCard `.take(6)`, C1PlateauCard `.take(6)`) truncate their lists with `.take(N)` and then append a plain 'ほか N 件…' overflow-count Text — this is manual pagination-by-truncation, not a real virtualized/paged list control; consider a single reusable helper/behavior in the port instead of duplicating the pattern 4-5x.
- Every diagnostic/breakdown display resolves internal engine keys to Japanese via the SAME external lookup `breakdownLabels[key] ?: key` (defined in a separate BreakdownLabels.kt, NOT in this file) — this is the single source of truth for family display names across WeightTableCard, BreakdownGroup/SeverityChip (via BreakdownBody), ConfirmRow/confirmItems, C1PlateauCard, CoverageDiagnosisCard (blockedFamily), and FixSuggestionCard (diff family names). The fallback-to-raw-key behavior (`?: key`/`?: it`/`?: fam`) means an unmapped key silently leaks an internal English/technical key into the Japanese UI rather than crashing — worth flagging as a translation completeness risk if this table isn't ported 1:1.
- `WeightTableCard()` takes NO parameters and reads a global static `MirrorKeys.weights: Map<String,Double>` directly (splitting entries into weight>=1000.0 'hard'/absolute vs <1000.0 'soft'/preference groups). It has a LOCAL (non-top-level, nested inside the composable) helper `fun fmt(w: Double): String` that renders a whole-number Double without a trailing '.0' (`if (w == w.toLong().toDouble()) w.toLong().toString() else w.toString()`) — this local function is not captured in the top-level helperFunctions list because it is not top-level, but is essential to port correctly.
- `GuidedFixDialog`'s branch-priority order matters for correctness and must be preserved exactly: `target != null` (an actionable, non-blocked FIXABLE shortfall) is checked FIRST, then `infeasible.isNotEmpty()`, then `blocked.isNotEmpty()`, then the else/'all clear' branch. `allDone` (used for the dialog title AND to decide whether a rerun button appears) is computed SEPARATELY as `target == null && blocked.isEmpty() && infeasible.isEmpty()` — note `blocked` excludes shortfalls whose verdict is INFEASIBLE (`it.miss > 0 && it.blockedNow && it.verdict != CoverageVerdict.INFEASIBLE`), i.e. blocked and infeasible are disjoint sets.
- `AttentionBody`'s per-day/per-staff aggregation (local `HashMap<Int,Int>` staffAlerts/dayAlerts and `HashMap<Int, LinkedHashSet<String>>` staffShifts/dayShifts) is rebuilt from scratch by iterating ui.countViolations/ui.violationCells/ui.needViolations on EVERY recomposition — it is NOT wrapped in `remember`, unlike the sibling `items`/`totalItems` computations elsewhere in this file. This is O(violation-count) work done inline each recompose; a WinUI3 port should likely memoize this in the ViewModel rather than recompute on every UI refresh.
- `SettingIssuesCard`'s per-issue Surface hardcodes `color = cs.errorContainer` for ALL 4 IssueKind values (WISH/CONSTRAINT/DEMAND/RANGE) — only the small leading MagiTagChip's color varies by kind (WISH=blue, CONSTRAINT=red, DEMAND=red, RANGE=orange); the surrounding card background does not vary by severity/kind the way CoverageDiagnosisCard/ForbiddenRunDiagnosisCard/C1PlateauCard do (those vary container color between errorContainer and secondaryContainer based on a blocked/pin flag).
- The composable `V6DashboardCard` is the only one in this file that does NOT take a `ui: UiState` parameter at all — it takes `v6: V6PortReport?` directly (presumably `ui.v6` or similar, resolved by the caller before invoking this composable), so it has zero `ui.xxx` reads by design; do not assume a missing UiState wiring is a bug when porting.

### 外部依存

- `UiState (data class defined elsewhere — the immutable screen-state snapshot; virtually every composable in this file takes `ui: UiState` and reads its fields)`
- `MagiViewModel (ViewModel class defined elsewhere — only GuidedFixDialog receives it directly and calls vm.shortageFixCandidates/vm.setCell)`
- `MagiAccent (color-token singleton — MagiAccent.blue/red/orange/green/purple used throughout for MagiTagChip/status colors)`
- `MagiTagChip (composable — small colored status/tag chip; used in CoverageDiagnosisCard, ForbiddenRunDiagnosisCard, C1PlateauCard, SettingIssuesCard, FixSuggestionCard)`
- `MagiScoreGauge (composable — large numeric gauge; used once in V6DashboardCard for coverage%)`
- `MagiSegmentedControl (composable — segmented tab/toggle control; used in ViolationHubCard and AttentionBody)`
- `ViolationBucketChips (composable, defined in MagiScheduleViews.kt per in-file comments — the shared 'violation family bucket' filter chip row also used by the schedule/勤務表 tab; used in ViolationHubCard)`
- `applyVioFilter(ui, vioEnabled) (function, external — filters a UiState's violation maps/breakdown down to the enabled family buckets; used in ViolationHubCard)`
- `vioBucketLocCounts(ui) (function, external — computes unfiltered per-bucket location counts for the chip badges; used in ViolationHubCard)`
- `magiWarnColors() (function, external — returns a (containerColor, contentColor) Pair for the amber/warning role; used in OperatorNextActionCard, ConfirmRow, AttentionBody)`
- `ensureReadable(background: Color, foreground: Color): Color (function, external — WCAG-contrast-safe color resolver; used in OperatorNextActionCard's phase badge, ConfirmRow's/AttentionRow's trailing hint text)`
- `progressSummary(ui: UiState): String (function, external — formats the 'improvement %/remaining time/iteration count' running-state line; used in OperatorNextActionCard)`
- `dayMD(startDate: String, dayIndex: Int): String (function, external — formats a 0-based day index + ui.startDate into a 'M/D(曜)' label; used directly in AttentionBody and inside the breakdownLocations/confirmItems helper functions)`
- `breakdownLabels (Map<String,String>, defined in BreakdownLabels.kt per in-file comment at line 857 — maps internal family/kind keys like 'low','covU','c1','fair' to Japanese display labels; used in WeightTableCard, BreakdownGroup/SeverityChip via BreakdownBody, ConfirmRow/confirmItems, C1PlateauCard, CoverageDiagnosisCard, FixSuggestionCard)`
- `com.magi.app.v6.V6PortReport (data class — the parameter type of V6DashboardCard, and the nested type of several UiState fields' payloads)`
- `com.magi.app.v6.V6Algorithm (imported at file top but NOT referenced anywhere in this file's body — dead import)`
- `com.magi.app.v6.CoverageVerdict (enum: FIXABLE, INFEASIBLE — used in GuidedFixDialog and CoverageDiagnosisCard, both qualified and via the direct import)`
- `com.magi.app.v6.MirrorKeys (object — MirrorKeys.weights: Map<String,Double> read once in WeightTableCard)`
- `com.magi.app.v6.ForbiddenCellEscape (enum: FREE, CHAIN, ADJACENT, PINNED, BLOCKED — used fully-qualified in ForbiddenRunDiagnosisCard)`
- `com.magi.app.v6.C1PlateauCause (enum: PIN_CONSTRAINED, SCORE_TRADEOFF, NO_CANDIDATE — used fully-qualified in C1PlateauCard)`
- `com.magi.app.v6.IssueKind (enum: WISH, CONSTRAINT, DEMAND, RANGE — used fully-qualified in SettingIssuesCard)`
- `com.magi.app.v6.SettingIssue (data class — parameter/element type in SettingIssuesCard; fields observed: kind, where, problem, fix, actionLabel)`
- `com.magi.app.v6.FixKind (enum: CHANGE, CHANGE_MULTI, SWAP, SWAP_XDAY, SWAP_MULTI, CHAIN, WINDOW — mapped by the local fixKindTag() helper)`
- `com.magi.app.v6.FixSuggestion (data class — element type of ui.fixSuggestions in FixSuggestionCard; fields observed: kind, label, diff (List<Pair<String,Int>>), deltaTotal)`
- `DialogConfirmButton, DialogDismissButton (composables, defined elsewhere — used as GuidedFixDialog's confirmButton/dismissButton content)`


---

## MagiScheduleViews.kt

### 役割

MagiScheduleViews.kt implements the 勤務表 (schedule) tab's primary interactive surfaces: the editable staff×day schedule grid itself (ScheduleGrid wrapping MagiFlatGrid/FlatCell, with week-paging, a violation-navigation strip, and a shift-shortage banner), the per-cell and bulk editing bottom sheets (ShiftPickerSheet for one cell, WishBulkSheet/AssignBulkSheet for many cells at once), the shift-count/coverage tally table (TallyCard with its supporting TallyBox/TallyLegend and detail-dialog helpers), and shared chrome reused elsewhere in the app — the violation-family filter chip row (ViolationBucketChips/ViolationFilterBar), the collapsible search+legend bar (SearchLegendBar/ViolationLegend/ShiftColorLegend), and a small in-progress live-preview card (LiveScheduleCard). Most functions here are pure/stateless UI given `UiState` (the app's centralized read-model) plus a handful of callback lambdas; direct `MagiViewModel` mutation calls are limited to wish-editing and detail-lookup helpers, while actual cell-assignment writes are deliberately routed back out through caller-supplied callbacks (onCellClick/onPick/onBulkSet) rather than called directly, since these grid/sheet composables are reused with different wiring by more than one screen.

### Composable関数

#### `LiveScheduleCard`

```kotlin
internal fun LiveScheduleCard(ui: UiState)
```

**描画内容:** A card shown only while an optimization run is in progress and a live preview schedule exists (early-returns otherwise); a toggle TextButton ('途中経過を見る'/'途中経過を隠す') expands/collapses a tiny colored-cell grid mirroring ui.liveSchedule, with any cell whose value changed since the last recomposition outlined in the theme's error color.

**読むUiStateフィールド:**

- `ui.running`
- `ui.liveSchedule`
- `ui.shiftColorHex`

**Compose固有ローカル状態:**

- show: Boolean via rememberSaveable { mutableStateOf(false) } — expands/collapses the change-preview grid
- prevHolder: arrayOfNulls<List<List<Int>>>(1) via remember { } — an imperative (non-@State) holder for the prior liveSchedule snapshot, used only for diffing, deliberately NOT wired as observable state to avoid a recomposition loop
- changed: HashSet<Int> via remember(cur) { ... } — packed-index (i*100000+j) set of cells whose value differs from prevHolder's snapshot, recomputed whenever `cur` (=ui.liveSchedule) changes


#### `ShiftPickerSheet`

```kotlin
internal fun ShiftPickerSheet(ui: UiState, vm: MagiViewModel, cell: Pair<Int, Int>, onPick: (Int) -> Unit, onDismiss: () -> Unit)
```

**描画内容:** A ModalBottomSheet editor for one schedule cell (staff `i`, day `j` from `cell`): a status Surface listing the cell's violation reasons, current assignment, wish status, and a 'flag for base-rule review' button; a 割当/希望 (assignment/wish) mode toggle; and a grid of shift-symbol option buttons (chunked in rows of 4) that either invoke `onPick(k)` (assignment mode) or immediately call `vm.setWish`/dismiss (wish mode). Shows a 'delete wish' button when in wish mode with an existing wish.

**ViewModel呼出:**

- `vm.allowedShiftsFor(i)`
- `vm.setWish(i, j, k)`
- `vm.removeWish(i, j)`
- `vm.addReviewMemo(text)`

**読むUiStateフィールド:**

- `ui.schedule`
- `ui.wishes`
- `ui.staffNames`
- `ui.shiftSymbols`
- `ui.violationColorHex`
- `ui.violationSoftColorHex`
- `ui.needViolations`

**Compose固有ローカル状態:**

- sheetState via rememberModalBottomSheetState()
- allowed: List<Int> via remember(cell) { vm.allowedShiftsFor(i).toList() } — shifts this staff member is qualified for
- mode: Int via remember(cell) { mutableIntStateOf(0) } — 0=割当(assignment), 1=希望(wish)
- haptic via LocalHapticFeedback.current — not state, but used to fire HapticFeedbackType.LongPress on wish set/remove


#### `ViolationBucketChips`

```kotlin
internal fun ViolationBucketChips(bucketCounts: Map<String, Int>, enabled: Set<String>, onToggle: (String) -> Unit, locCount: Int = -1, focusMode: Boolean = false, onFocusMode: (Boolean) -> Unit = {}, showFocusToggle: Boolean = true)
```

**描画内容:** A stateless header row ('違反フィルタ（種別）・要確認 N件' + optional 'すべて表示' reset button + optional '集中' focus-mode FilterChip) followed by a FlowRow of one FilterChip per entry in the external `vioBuckets` table, each labeled '{label} {count}' and dimmed when count is 0. Shared by the standalone ViolationFilterBar and an analysis-tab combined card.


#### `ViolationFilterBar`

```kotlin
internal fun ViolationFilterBar(bucketCounts: Map<String, Int>, enabled: Set<String>, onToggle: (String) -> Unit, locCount: Int = -1, focusMode: Boolean = false, onFocusMode: (Boolean) -> Unit = {}, showFocusToggle: Boolean = true)
```

**描画内容:** Wraps ViolationBucketChips in a Card+Column for the schedule tab's standalone filter bar; renders nothing at all if every bucket count is 0.


#### `SearchLegendBar`

```kotlin
internal fun SearchLegendBar(ui: UiState, query: String, onQuery: (String) -> Unit)
```

**描画内容:** A collapsible Card ('検索・凡例', default closed) that, when opened, shows a staff-name search OutlinedTextField (with a 'consume/clear' trailing button), the violation-border legend (ViolationLegend, only if ui.violationCells is non-empty), and the shift-color legend (ShiftColorLegend).

**読むUiStateフィールド:**

- `ui.violationColorHex`
- `ui.violationSoftColorHex`
- `ui.violationCells`
- `ui.shiftSymbols`
- `ui.shiftColorHex`
- `ui.shiftTextHex`

**Compose固有ローカル状態:**

- open: Boolean via rememberSaveable { mutableStateOf(false) } — expands/collapses the section


#### `ScheduleGrid`

```kotlin
internal fun ScheduleGrid(ui: UiState, onCellClick: (Int, Int) -> Unit, proMode: Boolean = false, nameQuery: String = "", vioEnabled: Set<String> = allVioBucketKeys, onBulkSet: (Collection<Pair<Int, Int>>, Int) -> Unit = { _, _ -> }, focusCell: Pair<Int, Int>? = null, onFocusShown: () -> Unit = {}, focusRange: Triple<Int, Int, Int>? = null, focusMode: Boolean = false, canDo: (Int, Int) -> Boolean = { _, _ -> true }, plainCellBorder: Boolean = false)
```

**描画内容:** The outer schedule-grid Card ('勤務表'): a pro-mode-only 'まとめて割当' bulk-assign entry button, an optional shift-shortage-by-day banner, the grid itself (delegated to MagiFlatGrid), a Monday-based week-paging row (only if the period spans >1 week), and a violation-day navigation row (only if any violating day exists under the current filter). Conditionally renders AssignBulkSheet when `showBulk` is toggled on.

**読むUiStateフィールド:**

- `ui.days`
- `ui.startDate`
- `ui.running`
- `ui.needViolations`
- `ui.violationCells`
- `ui.violationCellFamilies`

**Compose固有ローカル状態:**

- showBulk: Boolean via rememberSaveable { mutableStateOf(false) } — controls AssignBulkSheet visibility
- weeks: List<List<Int>> via remember(ui.startDate, allDays) { mondayWeeks(ui.startDate, allDays) }
- hScroll: ScrollState via rememberScrollState() — shared horizontal scroll state forwarded into MagiFlatGrid
- scrollScope: CoroutineScope via rememberCoroutineScope() — used to launch hScroll.animateScrollTo(...) from button onClick handlers
- gridCellW: Dp — computed inside BoxWithConstraints from `this.maxWidth`, `((maxWidth-32dp-80dp)/7).coerceIn(36dp,48dp)`, not memoized (recomputed every recomposition)
- cellWpx: Int via with(LocalDensity.current) { gridCellW.roundToPx() }
- curWeek: Int via remember(weeks, cellWpx) { derivedStateOf { ... } } — derived from hScroll.value, the week index the current scroll offset falls in
- vioDays: List<Int> via remember(ui.violationCells, ui.violationCellFamilies, ui.needViolations, vioEnabled) { ... } — sorted distinct day-indices with a currently-visible violation
- navFlash: Pair<Int,Int>? via remember { mutableStateOf(null) } — transient day-header highlight target set by the ＜前/次＞ nav buttons; self-clears after 2500ms via LaunchedEffect(navFlash)
- navIdx: Int via remember(vioDays) { mutableIntStateOf(-1) } — index into vioDays, declared only inside the `if (vioDays.isNotEmpty())` block
- LaunchedEffect(focusCell): animates hScroll to focusCell's day column, then calls onFocusShown() after a 2500ms delay
- LaunchedEffect(navFlash): clears navFlash to null after a 2500ms delay when non-null


#### `ViolationLegend`

```kotlin
internal fun ViolationLegend(vioColor: Color, vioSoftColor: Color = MagiAccent.orange)
```

**描画内容:** A stateless FlowRow of 5 legend rows explaining the grid's non-color visual cues: solid border='実線＝絶対NG', dashed border='破線＝できれば直す（重）', corner-triangle marker='右上の角＝できれば直す（軽）', pink filled badge='桃バッジ＝希望が未反映', teal-ringed badge='緑リング＝希望が反映済み'.


#### `ShiftColorLegend`

```kotlin
internal fun ShiftColorLegend(symbols: List<String>, colorHex: List<String>, textHex: List<String>)
```

**描画内容:** A stateless FlowRow of fixed-size (32dp tall, min 48dp wide) colored chips, one per non-blank shift symbol, each showing that shift's configured background/text color under a 'シフトの色' label; renders nothing if `symbols` has no non-blank entries.


#### `WishBulkSheet`

```kotlin
internal fun WishBulkSheet(ui: UiState, vm: MagiViewModel, presetWeekday: Int, onDismiss: () -> Unit)
```

**描画内容:** A ModalBottomSheet for bulk wish assignment/clearing: scope toggle (この曜日/期間全体), conditional weekday-of-week picker row, target-staff picker (全職員 or one via a nested AlertDialog), a wished-shift picker grid (chunked 3, flags infeasible shifts with a red border+'外' but still allows selecting them), and a bottom row of 'この範囲を希望なしに' / '適用（N件）' buttons; a nested 'すべての希望を削除' confirmation AlertDialog guards the full-wipe case (scope=期間全体 & staff=全員 & clear).

**ViewModel呼出:**

- `vm.allowedShiftsFor(staffSel)`
- `vm.clearWishesForDays(staffOrNull, targetDays)`
- `vm.setWishesForDays(staffOrNull, targetDays, picked)`
- `vm.clearAllWishes()`

**読むUiStateフィールド:**

- `ui.days`
- `ui.startDate`
- `ui.running`
- `ui.staffNames`
- `ui.shiftSymbols`

**Compose固有ローカル状態:**

- sheetState via rememberModalBottomSheetState()
- scope: Int via remember { mutableIntStateOf(0) } — 0=この曜日, 1=期間全体
- weekday: Int via remember { mutableIntStateOf(presetWeekday.coerceIn(0, 6)) }
- staffSel: Int via remember { mutableIntStateOf(-1) } — -1=全職員, else a staff index
- picked: Int via remember { mutableIntStateOf(-1) } — selected wish shift index, -1=none
- showStaff: Boolean via remember { mutableStateOf(false) } — toggles the nested staff-picker AlertDialog
- confirmClearAll: Boolean via remember { mutableStateOf(false) } — toggles the nested 'delete all wishes' confirm AlertDialog


#### `AssignBulkSheet`

```kotlin
internal fun AssignBulkSheet(ui: UiState, onBulkSet: (Collection<Pair<Int, Int>>, Int) -> Unit, onDismiss: () -> Unit, canDo: (Int, Int) -> Boolean = { _, _ -> true })
```

**描画内容:** A ModalBottomSheet for bulk cell assignment: scope toggle, conditional weekday picker, target-staff picker (全職員 or one via a nested AlertDialog), a target-count summary noting how many staff were auto-excluded as unqualified once a shift is picked, a shift-picker grid (chunked 3, no per-shift eligibility flag), and a single confirm Button whose own label text explains why it's disabled (計算中/no shift picked/zero eligible cells) or otherwise reads 'この{N}マスに一括割当'.

**読むUiStateフィールド:**

- `ui.days`
- `ui.startDate`
- `ui.running`
- `ui.staffNames`
- `ui.shiftSymbols`
- `ui.schedule`
- `ui.shiftTextHex`

**Compose固有ローカル状態:**

- sheetState via rememberModalBottomSheetState()
- scope: Int via remember { mutableIntStateOf(1) } — 0=この曜日, 1=期間全体 (default differs from WishBulkSheet's 0)
- weekday: Int via remember { mutableIntStateOf(0) }
- staffSel: Int via remember { mutableIntStateOf(-1) }
- picked: Int via remember { mutableIntStateOf(-1) } — selected target shift index
- showStaff: Boolean via remember { mutableStateOf(false) }


#### `TallyCard`

```kotlin
internal fun TallyCard(ui: UiState, vm: MagiViewModel, onFix: (Int?, Int?) -> Unit = { _, _ -> }, vioEnabled: Set<String> = allVioBucketKeys)
```

**描画内容:** A shift-tally Card with a 職員別/日別 MagiSegmentedControl and a read-only period-range label; a scrollable table of counts (staff×shift totals in 職員別 mode, shift×day totals in 日別 mode) with violating cells colored red (shortage/▼) or orange (excess/▲) and tappable to open an AlertDialog breakdown with a '直し方を探す' button calling `onFix`. Returns early (renders nothing) if the schedule/shift/day dimensions are 0.

**ViewModel呼出:**

- `vm.staffCellLimits(i, k) [via staffViolDetail helper]`
- `vm.needCellLimits(k, j) [via dayViolDetail helper]`

**読むUiStateフィールド:**

- `ui.shiftSymbols`
- `ui.schedule`
- `ui.days`
- `ui.violationColorHex`
- `ui.violationSoftColorHex`
- `ui.startDate`
- `ui.staffNames`
- `ui.staffGroupSymbols`
- `ui.shiftColorHex`
- `ui.shiftTextHex`
- `ui.countViolations`
- `ui.needViolations`

**Compose固有ローカル状態:**

- detail: TallyDetailUi? via remember { mutableStateOf(null) } — currently-open cell-detail AlertDialog content, or null if closed
- perStaff: Array<IntArray> via remember(ui.schedule, k) { ... } — per-(staff,shift) assignment counts, recomputed straight from ui.schedule
- perDay: Array<IntArray> via remember(ui.schedule, k, t) { ... } — per-(day,shift) headcounts, recomputed straight from ui.schedule
- mode: Int via rememberSaveable { mutableStateOf(0) } — 0=職員別, 1=日別
- periodLabel: String? via remember(ui.startDate, ui.days) { runCatching { ... }.getOrNull() } — formatted '集計期間 …〜…' label, null on parse failure


#### `TallyLegend`

```kotlin
private fun TallyLegend(shortBg: Color, overBg: Color)
```

**描画内容:** A stateless one-line legend Row: '▼ 不足' swatch, '▲ 超過' swatch, and a plain '— 対象外' label.


#### `TallyBox`

```kotlin
private fun TallyBox(w: androidx.compose.ui.unit.Dp, h: androidx.compose.ui.unit.Dp, bg: Color, start: Boolean, onClick: (() -> Unit)? = null, cd: String? = null, content: @Composable () -> Unit)
```

**描画内容:** A single reusable table-cell Box used throughout TallyCard: colored background with a larger corner radius plus a trailing '›' chevron whenever `onClick` is non-null (affordance derived purely from click-handler presence, not a separate flag), an optional accessibility contentDescription (`cd`), and either start-aligned or centered slotted `content`.


#### `MagiFlatGrid`

```kotlin
internal fun MagiFlatGrid(ui: UiState, onCellClick: (Int, Int) -> Unit, vioEnabled: Set<String> = allVioBucketKeys, hScroll: ScrollState = rememberScrollState(), nameQuery: String = "", cellW: androidx.compose.ui.unit.Dp = 48.dp, focusCell: Pair<Int, Int>? = null, focusRange: Triple<Int, Int, Int>? = null, focusMode: Boolean = false, canDo: (Int, Int) -> Boolean = { _, _ -> true }, plainCellBorder: Boolean = false)
```

**描画内容:** The core flat/spreadsheet-style schedule grid: a fixed name column (per-group color band, search-match highlighting) beside a horizontally-scrolled block of per-day columns, each with a header (day number, weekday label with holiday/weekend tinting, an optional '▼N' shortage badge, a violation-severity underline bar) and one FlatCell per staff row underneath; tapping a cell fires `onCellClick(i,d)` and sets a transient row/column cross-highlight (`tapped`) that self-clears after 2500ms.

**読むUiStateフィールド:**

- `ui.days`
- `ui.schedule`
- `ui.violationColorHex`
- `ui.violationSoftColorHex`
- `ui.shiftColorHex`
- `ui.shiftTextHex`
- `ui.startDate`
- `ui.violationCells`
- `ui.violationCellFamilies`
- `ui.wishes`
- `ui.v6`
- `ui.staffGroupSymbols`
- `ui.staffNames`
- `ui.shiftSymbols`

**Compose固有ローカル状態:**

- vioColor / vioSoftColor: Color — plain vals (not remembered) derived from ui.violationColorHex/violationSoftColorHex with MaterialTheme fallback
- shiftColorsC: List<Color> via remember(ui.shiftColorHex) { ui.shiftColorHex.map { hexToColor(it) } }
- shiftTextC: List<Color> via remember(ui.shiftTextHex) { ... }
- holidayCtx via LocalContext.current
- holidayName: Array<String?> via remember(ui.startDate, days) { Array(days) { ... JapanHolidays.nameOf(holidayCtx, date) } }
- todayIdx: Int via remember(ui.startDate, days) { ... } — day-column index matching LocalDate.now(), or -1
- vioCls: Array<Array<String?>> via remember(ui.violationCells, ui.violationCellFamilies, staffCount, days, vioEnabled) { ... } — per-(staff,day) visible violation class, or null
- vioKind: Array<IntArray> via remember(vioCls) { ... } — 0..3 severity classification per cell, derived from vioCls
- wishKind: Array<IntArray> via remember(ui.wishes, ui.schedule, staffCount, days) { ... } — 0/1/2 wish-vs-assignment status per cell, gated by canDo(i, wk)
- groupOrder: List<String> via remember(ui.staffGroupSymbols) { ui.staffGroupSymbols.distinct() } — first-appearance order of group symbols, drives each row's color-band hue
- symFontSize: TextUnit via with(LocalDensity.current) { minOf(cellW * 0.40f, 15.dp).toSp() }
- headFontSize: TextUnit via with(LocalDensity.current) { 12.dp.toSp() }
- dayVioH: IntArray via remember(vioKind) { IntArray(days) { d -> count of vioKind[*][d]==1 } } — hard-violation count per day
- dayVioS: IntArray via remember(vioKind) { IntArray(days) { d -> count of vioKind[*][d]>=2 } } — soft-violation count per day
- dayShort: IntArray via remember(ui.v6, days) { IntArray(days) { d -> ui.v6?.dayRisks?.getOrNull(d)?.shortage ?: 0 } }
- tapped: Pair<Int,Int>? via remember { mutableStateOf(null) } — last-tapped (staff,day) cell, drives row/column cross-highlight, self-clears after 2500ms via LaunchedEffect(tapped)


#### `FlatCell`

```kotlin
private fun FlatCell(w: androidx.compose.ui.unit.Dp, h: androidx.compose.ui.unit.Dp, symbol: String, bg: Color, fg: Color, vk: Int, wk: Int, vioColor: Color, vioSoftColor: Color, cd: String, dim: Boolean = false, symSize: androidx.compose.ui.unit.TextUnit = 15.sp, focused: Boolean = false, wishSym: String = "", plainBorder: Boolean = false, onClick: () -> Unit)
```

**描画内容:** The single per-(staff,day) cell primitive: a rounded colored Box that (in priority order) shows a solid primary-colored focus border, else a solid hard-violation border (vk==1), else a dashed soft-violation border (vk==2), else (if plainBorder) a plain 1dp outline; overlays a bold shift symbol, an optional top-right corner-triangle marker for light violations (vk==3), and a bottom-left badge showing either the mismatched wish symbol (pink, wk==2) or a plain matched/unmatched wish dot (wk==1/2 without a symbol). The whole cell is clickable with a composed accessibility description.


### ヘルパー関数

- `progressSummary`（`internal fun progressSummary(ui: UiState): String`）: Builds the human-readable, middle-dot-joined progress summary line shown during/after an optimization run: remaining hard-violation count (with the starting count once it exceeds the current), or the soft 'improvement %' once hard violations reach 0, plus a running total when hard>0, plus a formatted M:SS time-remaining derived from ui.budgetSec/ui.elapsedMs. Deliberately omits iteration-count/rate figures per an in-file comment (moved to diagnostic logs as 'developer-facing' numbers).
- `mondayWeeks`（`internal fun mondayWeeks(startDate: String, days: Int): List<List<Int>>`）: Splits the 0-based day-index range [0, days) into Monday-starting week buckets (each inner list holds the day-indices in that week), using startDowMonFirst(startDate) for the offset of day 0; drives ScheduleGrid's week-paging navigation.
- `cellVioClasses`（`internal fun cellVioClasses(ui: UiState, key: String): List<String>`）: Returns all violation classes ('vio-xxx') present at a given "i,j" cell key, preferring the multi-class map ui.violationCellFamilies[key] and falling back to a single-element list from the legacy single-class map ui.violationCells[key] if the former is empty/absent.
- `visibleCellVio`（`internal fun visibleCellVio(ui: UiState, key: String, enabled: Set<String>): String?`）: Returns the first (heaviest-weighted, since cellVioClasses is weight-descending) violation class at `key` that currently passes the E7 bucket filter (`enabled`, via the external vioVisible predicate), or null if none do.
- `resolvedVioColor`（`internal fun resolvedVioColor(ui: UiState, cls: String?, hardC: Color, softC: Color): Color`）: Single source of truth for violation-border/badge color: returns hardC if cls is null; else a user-configured per-family override from ui.violationFamilyColorHex[familyOfVioClass(cls)] if set; else hardC or softC depending on isHardCellViolation(cls).
- `vioBucketLocCounts`（`internal fun vioBucketLocCounts(ui: UiState): Map<String, Int>`）: Tallies, per E7 filter-bucket key, the number of distinct violation LOCATIONS (not raw fire-counts) across all three violation maps (ui.violationCells' keys via cellVioClasses/familyOfVioClass/bucketOfFamily, ui.needViolations.values, ui.countViolations.values), so filter-chip counts share the same unit as the '要確認 N件' heading.
- `startDowMonFirst`（`internal fun startDowMonFirst(startDate: String): Int`）: Parses startDate (java.time.LocalDate) and returns its Monday-based day-of-week index (0=Monday..6=Sunday), defaulting to 0 on any parse failure.
- `isHardCellViolation`（`internal fun isHardCellViolation(v: String?): Boolean`）: True if the violation string v names one of the engine's HARD-severity families, checked by substring match against MirrorKeys.hard (the single external source of truth for hard vs soft family names).
- `isHeavySoftCellViolation`（`internal fun isHeavySoftCellViolation(v: String?): Boolean`）: True if v's family (via external familyOfVioClass) is one of the 'heavy' soft families in the co-located top-level constant `internal val heavySoftFamilies = setOf("low", "high", "c1", "c3mn")` — these get a dashed border instead of the lighter corner-triangle marker, so only the highest-weighted/most-numerous soft violations visually compete with hard violations for attention.
- `Modifier.violationBorder`（`internal fun Modifier.violationBorder(hard: Boolean, color: Color, radiusDp: androidx.compose.ui.unit.Dp, halo: Color? = null): Modifier`）: Draws the grid's non-color violation cue as a Modifier extension (not a composable, no @Composable annotation): for hard=true, a 3dp solid colored border chained AFTER an optional 5dp neutral-surface halo border — ordered so the colored border paints frontmost (Compose's Modifier.border paints the last-chained border on top); for hard=false, a manually drawBehind-drawn 3dp dashed rounded-rect stroke (10px-on/6px-off) atop an optional thicker solid halo stroke underneath, so the dashes' gaps still show the halo color against same-hued cell backgrounds.
- `dayMD`（`internal fun dayMD(startDate: String, j: Int): String`）: Formats a 0-based day index j (relative to startDate) as 'M/D', falling back to '(j+1)日' if startDate fails to parse.
- `staffViolDetail`（`private fun staffViolDetail(vm: MagiViewModel, ui: UiState, i: Int, k: Int, count: Int, vio: String): TallyDetailUi`）: Builds the AlertDialog content for a tapped 職員別 (staff×shift) tally cell: fetches (lo, hi, apt) via vm.staffCellLimits(i, k) and produces a title+lines pair explaining the specific violation ('vio-low'/'vio-high'/'vio-aptLow'/'vio-aptHigh') as a current-count-vs-limit shortfall/excess sentence, returning focus=i so onFix targets that one staff member.
- `dayViolDetail`（`private fun dayViolDetail(vm: MagiViewModel, ui: UiState, k: Int, j: Int, count: Int, vio: String): TallyDetailUi`）: Builds the AlertDialog content for a tapped 日別 (shift×day) tally cell: fetches (lo, hi) via vm.needCellLimits(k, j) and produces a title+lines pair for 'vio-covU' (shortage) or 'vio-covO' (excess), returning focus=null so onFix triggers a whole-schedule search rather than targeting one staff member.
- `tallyHex`（`private fun tallyHex(hex: String?): Color?`）: Null-safe wrapper around the external hexToColor(String): Color — returns null (letting the caller fall back to a theme default) if hex is null or blank, instead of attempting to parse an empty string.

### ダイアログ/オーバーレイ

- ShiftPickerSheet — ModalBottomSheet, the per-(staff,day) cell editor. Defined but never invoked from within this file itself; an external caller (e.g. MagiApp.kt) is responsible for showing it when a grid cell is tapped (ScheduleGrid/MagiFlatGrid only expose onCellClick(i,j) as a callback, they don't own the sheet's visibility/selected-cell state). Confirms an assignment via onPick(k) (assignment mode) or immediately via vm.setWish(i,j,k)/vm.removeWish(i,j) (wish mode, each followed by onDismiss()); cancelled by the sheet's own scrim/swipe dismiss or the DialogHeader's close control.
- WishBulkSheet — ModalBottomSheet, the bulk wish set/clear editor (weekday-or-period × all-staff-or-one × wished shift). Defined but never invoked from within this file; shown externally, pre-seeded with `presetWeekday`. 'この範囲を希望なしに' confirms a clear via vm.clearWishesForDays (routed through a confirmation dialog first if scope=期間全体 & staff=全職員, to guard the destructive full-wipe case); '適用（N件）' confirms via vm.setWishesForDays.
- WishBulkSheet's nested staff-picker AlertDialog (state: showStaff) — lists ui.staffNames as tappable rows; tapping a row sets staffSel and dismisses; dismissButton='閉じる' cancels without changing staffSel.
- WishBulkSheet's nested 'すべての希望を削除' confirmation AlertDialog (state: confirmClearAll) — confirmButton is a DialogDangerButton('すべて削除') that calls vm.clearAllWishes() and dismisses the parent sheet too; dismissButton is a DialogDismissButton that just closes this confirm dialog, leaving the parent sheet open and no wishes cleared.
- AssignBulkSheet — ModalBottomSheet, the bulk cell-assignment editor (weekday-or-period × all-staff-or-one × one shift, auto-excluding staff who can't do the picked shift). Invoked from within this file, from ScheduleGrid, toggled by local `showBulk` state via the 'まとめて割当' button (proMode only). Confirms via the caller-supplied onBulkSet(cells, picked) callback then onDismiss(); the confirm button's own label text doubles as the disabled-reason explanation when not confirmable.
- AssignBulkSheet's nested staff-picker AlertDialog (state: showStaff) — structurally identical to WishBulkSheet's: lists ui.staffNames as tappable rows, sets staffSel on tap, dismissButton='閉じる' cancels.
- TallyCard's cell-detail AlertDialog (state: detail) — a read-only breakdown (current count vs. limit/target) for one tapped violating tally cell, built by staffViolDetail (職員別 mode) or dayViolDetail (日別 mode). confirmButton is a DialogConfirmButton('直し方を探す') that calls onFix(focus, shift) and clears `detail`; dismissButton is a DialogDismissButton('閉じる') that only clears `detail`.

### Android/Compose固有パターン（要変換方針）

- remember(keys) { computeArrayOrList() } memoized derived collections (MagiFlatGrid's vioCls/vioKind/wishKind/dayVioH/dayVioS/dayShort/holidayName/todayIdx/shiftColorsC/shiftTextC/groupOrder; TallyCard's perStaff/perDay; ScheduleGrid's weeks/vioDays/curWeek): where used: MagiFlatGrid, TallyCard, ScheduleGrid: why it matters: Compose skips recomputation when the listed keys are referentially/structurally unchanged; WinUI3/x:Bind has no automatic memoization, so each of these needs to become an explicit cached property (in the view-model or code-behind) recomputed only in response to specific PropertyChanged events (Schedule, Days, StartDate, etc.), not recomputed on every UI pass, or behavior/perf will diverge from the source.
- derivedStateOf reading a ScrollState's `.value` so only week-label readers recompose, not the whole grid (ScheduleGrid's `curWeek`): where used: ScheduleGrid: why it matters: XAML has no lazy-derived-state primitive tied to a scroll offset; the port needs an explicit handler on the shared ScrollViewer's ViewChanged/ViewChanging event updating a bound `CurrentWeek` property, taking care that updating it doesn't itself trigger a layout pass over the (potentially large) grid.
- rememberSaveable-backed toggle/mode state (LiveScheduleCard.show, TallyCard.mode, SearchLegendBar.open): where used: LiveScheduleCard, TallyCard, SearchLegendBar: why it matters: rememberSaveable survives process death/config change (rotation) but not app relaunch; in WinUI3 a plain private field or a non-persisted DependencyProperty is the equivalent unless suspend/resume restoration is also wanted (a stronger guarantee than the source actually provides) — don't over-persist.
- LaunchedEffect(key) self-cancelling/self-restarting delayed side effects (ScheduleGrid's focusCell-scroll-then-clear and navFlash-clear; MagiFlatGrid's tapped-clear), all using a 2500ms kotlinx.coroutines.delay: where used: ScheduleGrid, MagiFlatGrid: why it matters: LaunchedEffect auto-cancels and restarts its coroutine whenever its key changes; a DispatcherTimer-based port must replicate that cancel-and-restart-on-new-value semantics explicitly (dispose any previous timer before starting a new one keyed to the new value), or rapid successive taps/jumps will leave stacked timers clearing the wrong highlight.
- A `remember { arrayOfNulls<...>(1) }` used purely as an imperative (non-@State) box to remember the previous snapshot across recompositions WITHOUT itself causing recomposition (LiveScheduleCard.prevHolder): where used: LiveScheduleCard: why it matters: must become a plain private field/instance variable in the C# port, NOT an ObservableProperty — the source comment explicitly warns against a 'recomposition loop'; wiring it as a bindable property would create feedback (the diff-then-overwrite read pattern would fire PropertyChanged every render).
- BoxWithConstraints + `this.maxWidth` for a responsive per-column cell width (ScheduleGrid: `gridCellW = ((maxWidth - 32dp - 80dp) / 7).coerceIn(36dp, 48dp)`), whose pixel form (`cellWpx`) is then reused to convert day-indices into scroll offsets: where used: ScheduleGrid: why it matters: no BoxWithConstraints equivalent in XAML; needs a SizeChanged handler (or an ActualWidth-bound converter) that recomputes BOTH the column width AND re-derives cellWpx (and any pending week/violation/focus scroll target) whenever available width changes, not just once at load.
- A single shared ScrollState (hScroll) passed as a parameter from ScheduleGrid down into MagiFlatGrid so the parent's week/violation-jump buttons can scroll a Row rendered in the child, while a visually-fixed name column sits OUTSIDE that scrolled Row (both children of an outer, non-scrolled Row): where used: ScheduleGrid + MagiFlatGrid: why it matters: WinUI3's ScrollViewer scrolls its single Content as a whole; there is no built-in 'frozen first column beside a horizontally-scrolled remainder' composition matching this split-Row structure — needs either a pinned-column Grid plus an offset-synced second ScrollViewer, or a DataGrid-style frozen-column control; the `animateScrollTo(pixelOffset)` calls need to become `ScrollViewer.ChangeView(horizontalOffset: pixelOffset, ..., true)` using the same cellWpx pixel math.
- Per-cell accessibility text built by string concatenation inside the render loop and attached via `Modifier.semantics(mergeDescendants = true) { contentDescription = cd }` (FlatCell) or `Modifier.semantics { contentDescription = cd }` (TallyBox, MagiFlatGrid's holiday day-header): where used: FlatCell, TallyBox, MagiFlatGrid: why it matters: maps to AutomationProperties.Name (and possibly merging children) in WinUI3; since the strings encode several inline-computed flags (violation kind, wish kind, holiday name), the port needs a converter or a precomputed bound string per cell-viewmodel rather than a one-off XAML-markup string.
- Chained conditional `Modifier` composition via `.then(if (cond) Modifier.X else Modifier)` (used pervasively — FlatCell's border `when` block, WishBulkSheet/AssignBulkSheet's toggle-box backgrounds, ShiftPickerSheet's option boxes): where used: throughout the file: why it matters: no line-for-line XAML analog; the `else Modifier` idiom (a true no-op) should be OMITTED in the port (leave the property/Style at its default), and the conditional chains are best expressed as VisualStateManager states, a converter bound to the same boolean/int, or straightforward imperative Border/Background assignment in code-behind.
- Deterministic golden-angle HSV color assignment for per-group row color bands: `Color.hsv(((gi * 137) % 360).toFloat(), 0.40f, 0.72f)` where `gi` is the group's first-appearance index into `groupOrder` (`remember(ui.staffGroupSymbols) { ui.staffGroupSymbols.distinct() }`): where used: MagiFlatGrid (name-column group band): why it matters: needs a hand-ported HSV→RGB conversion in C# using the exact same constants (137° step, S=0.40, V=0.72) and the same 'first-appearance order via distinct()' rule for `gi`, if the two platforms' group colors must match pixel-for-pixel.
- `List<Int>.chunked(n)` + manual `repeat(n - rowKeys.size) { Spacer(Modifier.weight(1f)) }` row-padding to keep a short last row's items the same width as full rows (shift-option grids in ShiftPickerSheet/WishBulkSheet/AssignBulkSheet): where used: ShiftPickerSheet, WishBulkSheet, AssignBulkSheet: why it matters: WinUI3 lacks a weight-based flexible-Row layout with this chunk+pad idiom; a fixed-column `UniformGrid` (handles equal-width wrapping natively) or an explicit Grid with N equal ColumnDefinitions per row is the natural equivalent, not a literal port of Modifier.weight + empty spacers.
- `FlowRow` (@OptIn(ExperimentalLayoutApi::class)) used for legend/chip rows that wrap onto new lines when they don't fit (ViolationBucketChips, ViolationLegend, ShiftColorLegend): where used: ViolationBucketChips, ViolationLegend, ShiftColorLegend: why it matters: needs a WinUI3 WrapPanel/ItemsWrapGrid equivalent (StackPanel/Grid don't wrap); Compose's independent horizontal/vertical Arrangement.spacedBy spacing must be reproduced as the WrapPanel's item/line spacing.
- Ad hoc composite-index packing instead of a proper composite key type: `set.add(i * 100000 + j)` in LiveScheduleCard's change-detection, standing in for a 2D HashSet<Pair<Int,Int>>: where used: LiveScheduleCard (`changed` set): why it matters: this packing assumes `j < 100000` and should NOT be ported literally; C# should use a `HashSet<(int, int)>` value-tuple (natively hashable), since the packing trick is only a Kotlin-era workaround, not load-bearing data shape.
- Sentinel-value semantics baked into plain Ints rather than nullable types or dedicated enums: `focusCell.first < 0` (specifically -1, meaning 'day-only focus, matches no staff row', constructed elsewhere as `focusCell = -1 to j`) and `ui.schedule[i][d] ?: -1` (meaning 'no/invalid shift assigned'): where used: ScheduleGrid, MagiFlatGrid: why it matters: these are two UNRELATED uses of -1 and must each keep their exact original meaning in the port (e.g. a nullable int? for the schedule-cell case, a distinguishable flag/enum for the day-only-focus case) — conflating them would silently break either the day-jump highlight or out-of-range cell rendering.
- Small integer state machines encoded as raw Int rather than a Kotlin enum class — `vk` (0..3: none/hard/heavy-soft/light-soft) and `wk`/`wkk` (0..2: none/matched-wish/mismatched-wish): where used: MagiFlatGrid, FlatCell: why it matters: comparisons like `vk >= 2`, `vk == 1`, `wkk != 2` rely on both the specific integer values AND their ordering (`>= 2` groups heavy-soft and light-soft together as 'any soft'); a `enum class` port must preserve an equivalent ordinal ordering (or replace `>=` with explicit set-membership checks) or it will silently change which violations get which border style.
- rememberCoroutineScope() used to launch a coroutine from inside a non-suspend onClick lambda (`scrollScope.launch { hScroll.animateScrollTo(...) }` in ScheduleGrid's week/violation-nav buttons): where used: ScheduleGrid: why it matters: WinUI3 Click event handlers can be declared `async void`/`async Task` and directly await the scroll animation (via ChangeView plus a ViewChanged-completion wrapper) — there is no need to port the rememberCoroutineScope()+launch{} indirection as its own concept, just call the async scroll operation directly from the Click handler.

### 落とし穴

- A large fraction of the file's own imports are dead/unused in the current body — confirmed via grep: Canvas, rememberTextMeasurer, drawText, TextStyle, FontFamily, NavigationBar, NavigationBarItem, Scaffold (appears only inside a Japanese comment, not code), mutableStateListOf, ImageVector, BorderStroke, CircleShape, ButtonDefaults, CardDefaults, CircularProgressIndicator, LinearProgressIndicator, collectAsStateWithLifecycle, viewModel, Switch, rememberLauncherForActivityResult, ActivityResultContracts, Dispatchers, withContext, kotlin.math.roundToInt, and every Icons.Filled.* import except KeyboardArrowUp/KeyboardArrowDown (Add/Assessment/CheckCircle/DateRange/Edit/Home/PlayArrow/Settings/Stop/Warning are imported but never referenced). These are leftovers from earlier UI iterations — an in-file comment near line 1348 notes the old 'cylinder/fisheye interface' (円柱インターフェース) was replaced by today's flat grid, which likely explains the stale Canvas/drawText/rememberTextMeasurer imports. Do NOT treat 'is imported here' as 'needs a WinUI3 equivalent' — verify actual usage before porting a dependency.
- ScheduleGrid and MagiFlatGrid are two layered composables with heavy parameter overlap (both accept vioEnabled, focusCell, focusRange, focusMode, canDo, plainCellBorder). ScheduleGrid computes layout-derived values (gridCellW, cellWpx, weeks, vioDays) and merges its OWN internal transient highlight state (navFlash, from its own violation-nav buttons) with the EXTERNALLY-passed focusCell parameter via `focusCell ?: navFlash` before forwarding into MagiFlatGrid. Any restructuring that collapses these two composables must preserve this exact merge-with-external-precedence rule (external focusCell wins if non-null, else internal navFlash) — not just 'whichever fired most recently'.
- TallyCard is a sibling of ScheduleGrid, not a child call within it (grep confirms TallyCard is never invoked from inside this file) — it's presumably composed alongside ScheduleGrid by an external screen composable. It recomputes its own count matrices (perStaff/perDay) directly from ui.schedule, but colors those counts using THREE SEPARATE, differently-keyed violation maps: ui.violationCells/ui.violationCellFamilies (key "i,j"=staff,day — used by the grid, NOT by TallyCard), ui.countViolations (key "i,k"=staff,shift — TallyCard 職員別 mode), and ui.needViolations (key "k,j"=shift,day — TallyCard 日別 mode AND the grid's day-header shortage badge). Mixing up these three key formats during translation would silently miscolor cells.
- Violation color resolution has a mandatory 3-tier fallback that must go through resolvedVioColor() (or an exact equivalent) everywhere a violation-colored border/badge is drawn: (1) a per-family user override from ui.violationFamilyColorHex[familyOfVioClass(cls)], (2) else the two blanket-severity user-configurable colors ui.violationColorHex/ui.violationSoftColorHex selected via isHardCellViolation(cls), (3) else a hardcoded MaterialTheme/MagiAccent default. Re-deriving colors locally anywhere as a shortcut would silently break the app's 'recolor a specific violation family' settings feature.
- The Int value -1 is overloaded with two UNRELATED meanings in this file: (a) ui.schedule[i][d] ?: -1 = 'no/invalid shift value in this schedule cell' (drives surfaceVariant background + blank symbol), and (b) focusCell.first < 0 (specifically constructed elsewhere as the pair -1 to j) = 'day-only focus that intentionally matches no staff row', used only to drive the day-HEADER highlight for the day-jump/violation-nav features. Do not conflate these two independent uses of -1 during translation.
- MagiFlatGrid's wishKind array deliberately gates on the CALLER-SUPPLIED canDo(i, wk) predicate (default {_,_->true}, but ScheduleGrid always forwards a real canDo) so a wish for a shift the staff member is NOT qualified for renders no badge at all — this specifically matches the underlying engine's `pref` violation semantics, which likewise exclude infeasible wishes from counting as violations (per an in-file comment referencing this exact historical bug-fix). If the port's grid ever falls back to the permissive default canDo, the previously-fixed 'infeasible wish shows a badge' bug would silently return.
- Two DIFFERENT highlight mechanisms coexist with DIFFERENT precedence relative to the violation border: (a) `focused` (driven by the external focusCell/focusRange jump-navigation parameters) is checked FIRST in FlatCell's border `when` block, so it fully REPLACES the violation border with a solid primary-colored border while active; (b) `tapped` (MagiFlatGrid's own internal cross-highlight from a raw cell tap) deliberately does NOT touch the cell's own border at all — per an explicit comment ('セル自体の枠（違反表示）は変更しない') — it only tints the row background and the day header. These two features must not be merged into one override-priority scheme.
- WishBulkSheet and AssignBulkSheet are near-duplicate implementations of the same 'scope toggle + weekday row + staff picker + shift grid' structure, independently reimplemented rather than sharing a composable — AND they diverge in a semantically meaningful way: WishBulkSheet's shift grid flags infeasible shifts per-shift (red border+'外') but still allows selecting them (a wish for an infeasible shift is legal, just flagged), and does NOT filter the staff list; AssignBulkSheet's shift grid has NO per-shift infeasibility flag at all, but instead silently FILTERS THE STAFF LIST once a shift is picked (eligibleStaff = targetStaff.filter { canDo(it, picked) }), because an actual assignment must be feasible. This asymmetry is intentional business logic and must be preserved distinctly even if the two sheets are refactored to share markup.
- AssignBulkSheet's confirm Button's own label text IS its disabled-reason explanation (a single `when` inside the Button's Text{} switches between '計算中は変更できません' / 'まずシフトを選んでください' / '対象がありません（担当できる職員なし）' / 'この{N}マスに一括割当'), rather than a separate disabled-tooltip — this 'button label doubles as its own disabled-reason' convention recurs elsewhere in this codebase per its own comments ('矛盾なく選択'/'押せない理由をボタン自身が語る') and should be treated as a deliberate house pattern to replicate, not simplified away.
- Holiday/weekend day-header tinting treats a Japanese public holiday (via the external JapanHolidays.nameOf) IDENTICALLY to a Sunday (both render red) REGARDLESS of what weekday the holiday actually falls on (a weekday holiday still gets red, not its normal weekday color); Saturday independently renders blue. This 'holiday overrides weekday color' rule is intentional (per an in-file comment referencing Japanese calendar convention) and requires the full JapanHolidays data/logic ported alongside for date-for-date parity. The 'today' column additionally overrides both with a distinct tertiary text color and explicitly does NOT get a background tag at all ('to avoid confusion' per comment) — three overlapping-but-distinct rules on the same header cell.
- symFontSize for the grid's shift-symbol text is recomputed every recomposition as min(cellW * 0.40, 15.dp).toSp() — it shrinks proportionally with the also-dynamically-computed responsive column width, specifically to survive Android's system font-scale accessibility setting (up to ~1.3x) without a 2-character full-width shift symbol clipping out of its cell. WinUI3 doesn't push a single global text-scale-factor into layout measurement the same way; the port needs to either read the OS text-scale setting and replicate this proportional-shrink formula, or accept this specific accessibility fix may not have a 1:1 equivalent.
- TallyBox's visual affordance (bigger corner radius + trailing '›' chevron) is derived PURELY from whether the caller passed a non-null onClick lambda, not from a separate isClickable flag — per an explicit in-file design-history comment ('3.397.0 形が語る' / 'let the form itself communicate the affordance', motivated by a documented prior bug where users couldn't tell a cell was tappable). A port that separates 'has a Tapped handler' from 'looks tappable' into two independently-settable properties risks reintroducing that exact bug class.
- ScheduleGrid's shortage-summary banner ('人員不足（全N日中）: ...') is gated on `"need" in vioEnabled` (disappears if the user filters OUT the coverage bucket via the E7 chips) even though its underlying data (ui.needViolations entries with value=="vio-covU") is otherwise independent of the grid's per-cell filtering — this cross-feature gating is easy to miss if the banner and the E7 filter bar end up as fully independent view-model properties in the port.
- TallyCard's mode toggle (職員別/日別) uses the shared external MagiSegmentedControl composable, but the visually-similar 'scope' toggles inside WishBulkSheet/AssignBulkSheet (この曜日/期間全体) and the 割当/希望 mode toggle inside ShiftPickerSheet are each independently hand-built as a Row of two clickable Boxes with manual background/border — i.e. at least two different code patterns implement what looks like the same 'segmented control' affordance. Do not assume all instances can be collapsed into one WinUI3 control without visually confirming they behave identically; the source itself does not share the implementation.
- All user-facing strings in this file (e.g. '実線＝絶対NG', 'できれば直す（重）' vs '（軽）', the 2-space+middle-dot+2-space "  ・  " join separator in progressSummary, the trailing '›' chevron chosen over an earlier 'ⓘ' icon) reflect a long, explicitly documented history of end-user comprehension fixes recorded in the app's own change history (referenced by version-number comments like '3.396.0', '3.397.0' throughout). Treat all such copy/spacing/glyph choices as intentional final content, not placeholder text to be freely reworded or re-spaced during translation.

### 外部依存

- `MagiViewModel (the `vm` parameter type; methods called from this file: allowedShiftsFor(i), setWish(i,j,k), removeWish(i,j), addReviewMemo(text), clearWishesForDays(staffOrNull,days), setWishesForDays(staffOrNull,days,shift), clearAllWishes(), staffCellLimits(i,k), needCellLimits(k,j))`
- `UiState (the `ui` parameter type — the app's central read-model; ~25 distinct fields are read across this file, see each composable's uiStateFieldsRead)`
- `com.magi.app.v6.MirrorKeys (imported; `.hard: List<String>` is the single source of truth for which violation-family substrings count as HARD, used by isHardCellViolation)`
- `com.magi.app.v6.V6PortReport (imported; the type of ui.v6, from which only `.dayRisks[j]?.shortage` is read in MagiFlatGrid)`
- `com.magi.app.v6.V6Algorithm (imported but not referenced anywhere in this file's body)`
- `com.magi.app.v6.CoverageVerdict (imported but not referenced anywhere in this file's body)`
- `MagiAccent (color token object, likely MagiTokens.kt; .orange/.pink/.red/.blue/.green are used as default/fallback colors throughout)`
- `hexToColor(hex: String): Color — hex-string-to-Color parsing helper, defined elsewhere in the ui package (not in this file)`
- `ensureReadable(bg: Color, fg: Color): Color — WCAG-contrast-fixup helper, defined elsewhere in the ui package`
- `DialogHeader(title: String, onDismiss: () -> Unit) — shared dialog title/close-button composable, defined elsewhere`
- `DialogDismissButton(onClick, text = ...) — shared dialog dismiss-button composable, defined elsewhere`
- `DialogConfirmButton(text, onClick) — shared dialog confirm-button composable, defined elsewhere`
- `DialogDangerButton(text, onClick) — shared dialog destructive-action-button composable, defined elsewhere`
- `MagiSegmentedControl(options: List<String>, selected: Int, onSelect: (Int) -> Unit) — shared segmented-control composable, used once by TallyCard`
- `VioBuckets.kt: vioBuckets, vioVisible(cls, enabled), allVioBucketKeys, familyOfVioClass(cls), bucketOfFamily(family) — the E7 violation-family/bucket classification table, explicitly split out of this file per an in-file comment (3.382.0)`
- `breakdownLabels: Map<String,String> (BreakdownLabels.kt per app history) — family-key → Japanese-label lookup, used in ShiftPickerSheet's violation-reason text and review-memo text`
- `JapanHolidays.nameOf(context: android.content.Context, date: java.time.LocalDate): String? — cached, bundled-asset-JSON-backed Japan public-holiday lookup used for day-header weekend/holiday tinting in MagiFlatGrid; the JapanHolidays object/data itself lives in a separate file`
- `External caller(s) of this file's top-level composables not shown here — most plausibly MagiApp.kt or a similar screen-level composable, which is responsible for owning the show/hide state and selected-cell state for ShiftPickerSheet and WishBulkSheet (both are defined in this file but never instantiated within it) and for wiring ScheduleGrid's onCellClick/canDo/onBulkSet callbacks to actual mutation logic`


---

## MagiSetupCards.kt

### 役割

Defines a set of 15 reusable Jetpack Compose Card/Row composables (plus one pure helper function) used primarily by the app's Settings ('設定') tab and its month-picker/initial-setup-guide areas: MonthPickerCard, SetupGuideCard (+ GuideRow), SettingsCard (+ OptimizationTuningSection, which itself is embedded inside AdvancedSettingsSection's collapsible 'detailed settings' panel), MonthlyChecklistCard (+ ChecklistRow), StaffingRealityCard, ReviewMemoCard, LogsCard, DataActionsCard, and AppearanceCard. All of these read from a shared `UiState` snapshot and/or invoke mutation methods on a shared `MagiViewModel` following the app's unidirectional state-flow convention, with a few exceptions (AppearanceCard, and to a lesser extent LogsCard/DataActionsCard) that are pure presentation components driven entirely by explicit params/callback lambdas rather than `ui`/`vm` directly. The file additionally defines two small, generic, file-agnostic layout primitives — `SectionNote` (a tinted info banner Surface) and `CollapsibleSection` (a rememberSaveable-backed, string-keyed expand/collapse wrapper around an arbitrary `content` slot) — that are declared here but consumed by OTHER UI files elsewhere in the app, not invoked anywhere within this file itself.

### Composable関数

#### `MonthPickerCard`

```kotlin
@Composable internal fun MonthPickerCard(ui: UiState, vm: MagiViewModel)
```

**描画内容:** Renders a Card titled '対象の月' (Target Month) showing the parsed current month/year label (e.g. '2026年 8月', falling back to '未設定' if ui.startDate fails LocalDate.parse) flanked by '前の月'/'次の月' OutlinedButtons, plus a full-width '来月にする' (set-to-next-month) OutlinedButton below. Returns early (renders nothing) when ui.loaded is false.

**ViewModel呼出:**

- `vm.shiftMonth(-1)`
- `vm.shiftMonth(1)`
- `vm.setNextMonth()`

**読むUiStateフィールド:**

- `ui.loaded`
- `ui.startDate`
- `ui.running`

**Compose固有ローカル状態:**

- val label: String? = remember(ui.startDate) { runCatching { LocalDate.parse(ui.startDate) -> "${d.year}年 ${d.monthValue}月" }.getOrNull() } — memoized parsed month/year label, re-derived only when ui.startDate changes; result is null on parse failure (UI then shows '未設定').


#### `SetupGuideCard`

```kotlin
@Composable internal fun SetupGuideCard(ui: UiState, vm: MagiViewModel, editScope: Int = -1, onOpenWish: (() -> Unit)? = null)
```

**描画内容:** Renders a Card titled '初期設定の手順' (Initial Setup Steps): an optional '── 月次条件（毎月）──' section (hidden when editScope == 0) with GuideRow items for wish count and needDay-exception count, followed by a permanent '── 年間マスター（制度が変わったときだけ）──' section with GuideRow items for basic info / constraints / ranges counts, ending in a secondaryContainer-tinted 'next step' hint banner whose text branches on setup completeness (staff/shifts missing -> wishes missing -> ready). Returns early when ui.loaded is false.

**ViewModel呼出:**

- `vm.setupCounts()`

**読むUiStateフィールド:**

- `ui.loaded`


#### `GuideRow`

```kotlin
@Composable internal fun GuideRow(label: String, value: String, done: Boolean, onClick: (() -> Unit)? = null)
```

**描画内容:** Renders one horizontal row: a bold ✓ (done, cs.primary) / ・ (not done, cs.onSurfaceVariant) status glyph, a weight(1f) label Text, and a trailing value Text with ' ›' appended and colored cs.primary only when onClick is non-null (otherwise cs.onSurfaceVariant, no arrow); the whole Row is only wrapped in Modifier.clickable when onClick is supplied.


#### `SettingsCard`

```kotlin
@Composable internal fun SettingsCard(ui: UiState, vm: MagiViewModel, onBgOptimize: () -> Unit = {})
```

**描画内容:** Renders the '最適化設定' (Optimization Settings) Card: a budget-seconds label plus a −60秒/+60秒 Button stepper clamped to [10, MAX_BUDGET_SEC], a '計算方式' label with the current algorithm's Japanese name and (when ui.v6Algorithm == V6Algorithm.AUTO) a resolved-algorithm hint line, a horizontally-scrolling Row with one selected Button / unselected OutlinedButton per V6Algorithm enum value, a full-width 'バックグラウンドでつくる（閉じても続行）' OutlinedButton, and a small app-version Text fetched via PackageManager.

**ViewModel呼出:**

- `vm.setBudget(...)`
- `vm.setV6Algorithm(alg)`

**読むUiStateフィールド:**

- `ui.budgetSec`
- `ui.running`
- `ui.v6Algorithm`

**Compose固有ローカル状態:**

- val versionLabel: String = remember(ctx) { runCatching { "${pi.versionName} (${pi.longVersionCode})" }.getOrDefault("不明") } — memoized app-version string keyed on LocalContext.current identity; fallback/initial value "不明" (unknown) if PackageManager lookup throws.


#### `OptimizationTuningSection`

```kotlin
@Composable private fun OptimizationTuningSection(ui: UiState, vm: MagiViewModel)
```

**描画内容:** Renders (no Card wrapper — a bare Column, meant to be embedded inside AdvancedSettingsSection) advanced tuning controls: a workers-count −/+ Button stepper (range 1–16) with accessibility content-descriptions, an explanatory Text about hypothesis count / core-clamp behavior, then four Row(label+description)+Switch toggles — 'ネイティブ加速（C++）', 'Kotlin照合' (label/description turn error-colored when off), '禁止連続の事前フィルタ', '禁止連続の崩し範囲' (description/color depend on its own on/off state) — and finally a plain Switch+Text row for '仕上げ最適化'.

**ViewModel呼出:**

- `vm.setWorkers(...)`
- `vm.setNativeAccel(it)`
- `vm.setNativeParity(it)`
- `vm.setBlockSwapC3nFilter(it)`
- `vm.setWideC3nBreak(it)`
- `vm.setSoftPolish(it)`

**読むUiStateフィールド:**

- `ui.workers`
- `ui.running`
- `ui.nativeAccel`
- `ui.nativeParity`
- `ui.blockSwapC3nFilter`
- `ui.wideC3nBreak`
- `ui.softPolish`


#### `MonthlyChecklistCard`

```kotlin
@Composable internal fun MonthlyChecklistCard(ui: UiState, vm: MagiViewModel, onMake: () -> Unit, onOpenWish: (() -> Unit)? = null)
```

**描画内容:** Renders the '今月の作成条件' (This Month's Creation Conditions) Card: four ChecklistRow items — staff count, wish/vacation registration ratio (clickable through to onOpenWish), required-headcount standard/exception status, and input-diagnostics issue count — followed by a full-width '▶ 勤務表をつくる' primary Button (onClick = onMake). Returns early when ui.loaded is false.

**ViewModel呼出:**

- `vm.needDayOverrides()`
- `vm.ws1()`

**読むUiStateフィールド:**

- `ui.loaded`
- `ui.staffNames`
- `ui.wishes`
- `ui.settingIssues`
- `ui.running`


#### `ChecklistRow`

```kotlin
@Composable private fun ChecklistRow(label: String, value: String, ok: Boolean, onClick: (() -> Unit)? = null)
```

**描画内容:** Renders one horizontal checklist row: a bold ✓ (ok, cs.tertiary) / ！ (not ok, cs.error) status glyph, a weight(1f) label Text, and a trailing value Text with ' ›' appended and colored cs.primary only when onClick is non-null; the whole Row is only wrapped in Modifier.clickable when onClick is supplied.


#### `StaffingRealityCard`

```kotlin
@Composable internal fun StaffingRealityCard(ui: UiState, vm: MagiViewModel)
```

**描画内容:** Renders the 'この体制で回るか' (Does This Staffing Work) Card: for every shift index with nonzero total monthly demand, one Row showing a ✓/！/⚠ severity glyph (from slack = assignedCount − maxDailyNeed) plus the bold shift symbol plus a text summarizing assigned headcount, monthly person-days, per-person average (one decimal), and an understaffed/no-slack/slack-available phrase. Returns early (renders nothing) when ui.loaded is false or when no shift has any demand (rows list empty).

**ViewModel呼出:**

- `vm.allowedShiftsFor(i)`
- `vm.needCellLimits(k, j)`

**読むUiStateフィールド:**

- `ui.loaded`
- `ui.days`
- `ui.staffNames`
- `ui.shiftSymbols`


#### `ReviewMemoCard`

```kotlin
@Composable internal fun ReviewMemoCard(ui: UiState, vm: MagiViewModel)
```

**描画内容:** Renders a Card listing session-only '見直し候補（N件）' (Review Candidates) memo strings, with an explanatory note that they clear when the app exits, each memo shown on its own Row paired with a '済' (Done) DeleteRowButton to dismiss it. Returns early (renders nothing) when ui.reviewMemos is empty.

**ViewModel呼出:**

- `vm.removeReviewMemo(idx)`

**読むUiStateフィールド:**

- `ui.reviewMemos`


#### `AdvancedSettingsSection`

```kotlin
@Composable internal fun AdvancedSettingsSection(ui: UiState, vm: MagiViewModel, onExportLog: () -> Unit, onExportJson: () -> Unit)
```

**描画内容:** Renders a Card with a clickable header Row ('詳細設定（上級者向け）' title + explanatory subtitle + a 32dp circular Surface containing a KeyboardArrowUp/KeyboardArrowDown Icon chevron) that toggles a persisted `expanded` flag; when expanded, reveals OptimizationTuningSection(ui, vm) followed by LogsCard(ui = ui, onExportLog = onExportLog, onExportJson = onExportJson) inside a spacedBy(12.dp) Column.

**Compose固有ローカル状態:**

- var expanded: Boolean by rememberSaveable { mutableStateOf(false) } — process-death-surviving expand/collapse flag, initial value false, keyed implicitly by call-site slot (no explicit stateKey argument, unlike CollapsibleSection).


#### `LogsCard`

```kotlin
@Composable internal fun LogsCard(ui: UiState, onExportLog: () -> Unit, onExportJson: () -> Unit)
```

**描画内容:** Renders the 'ログ' (Logs) Card: a two-button Row ('テキスト出力'/'JSON出力', both enabled = hasAny [i.e. ui.opLog or ui.logs non-empty] — NOT gated on ui.running), a scrollable (heightIn max = 220.dp) monospace operation-log list from ui.opLog (newest-first, capped at 60 lines, lines containing '[W]' or '[E]' rendered in cs.error), and — only when ui.logs is non-empty — a '診断ログ' section listing up to 6 lines from ui.logs with maxLines = 2 + ellipsis truncation.

**読むUiStateフィールド:**

- `ui.opLog`
- `ui.logs`


#### `DataActionsCard`

```kotlin
@Composable internal fun DataActionsCard(ui: UiState, onOpenJson: () -> Unit, onSample: () -> Unit, onSaveJson: () -> Unit, onOpenCsv: () -> Unit, onSaveCsv: () -> Unit, onCheck: () -> Unit, onSaveStaffCsv: () -> Unit = {}, onSaveWishesCsv: () -> Unit = {}, onSaveConstraintsCsv: () -> Unit = {}, onRestorePrev: () -> Unit = {})
```

**描画内容:** Renders the 'データ' (Data) Card as stacked paired OutlinedButton rows (データを開く/サンプル, then データを保存/問題がないか調べる, then CSV取込/CSV出力, then — under a 'コンポーネント別 出力（取込種別と対・往復用）' label — 職員/希望/制約 component-CSV export buttons), plus a conditional full-width TextButton '開く前のデータに戻す（もう一度押すと入れ替え）' shown only when ui.prevBackupAvailable is true, placed between the top and second button rows. This composable takes no vm parameter — every action is a caller-supplied lambda.

**読むUiStateフィールド:**

- `ui.running`
- `ui.prevBackupAvailable`
- `ui.loaded`


#### `AppearanceCard`

```kotlin
@Composable internal fun AppearanceCard(oneHand: Boolean = false, onOneHand: (Boolean) -> Unit = {}, proMode: Boolean = false, onProMode: (Boolean) -> Unit = {}, plainCellBorder: Boolean = false, onPlainCellBorder: (Boolean) -> Unit = {})
```

**描画内容:** Renders the '外観' (Appearance) Card: a fixed 'universal-design high-contrast theme' note, a '片手モード' (one-hand mode) Switch+Text row, a '勤務表の通常セルに枠線を表示' (show grid plain-cell border) Switch+Text row, and a '表示モード' label with a two-option MagiSegmentedControl (かんたん/プロ) for display mode. This composable takes NO ui/vm parameters at all — all six values/callbacks are supplied directly as function parameters.


#### `SectionNote`

```kotlin
@Composable internal fun SectionNote(text: String)
```

**描画内容:** Renders a single full-width Surface (surfaceVariant background color, MaterialTheme.shapes.small rounded corners, 12.dp padding) containing one bodySmall Text — a generic tinted info/note banner. Not invoked anywhere else within this file; intended for reuse by other UI files.


#### `CollapsibleSection`

```kotlin
@Composable internal fun CollapsibleSection(title: String, stateKey: String, initiallyExpanded: Boolean = false, content: @Composable () -> Unit)
```

**描画内容:** Renders a generic reusable expand/collapse wrapper: a clickable surfaceVariant Surface header Row (bold titleSmall `title` Text on the left, '▼ 閉じる'/'▶ 開く' toggle-label Text on the right) that shows/hides the caller-supplied `content` composable slot below it when tapped. Not invoked anywhere else within this file; intended for reuse by other UI files (e.g. wrapped elsewhere in `key(ui.editRev) { ... }` per project history).

**Compose固有ローカル状態:**

- var expanded: Boolean by rememberSaveable(stateKey) { mutableStateOf(initiallyExpanded) } — process-death-surviving expand/collapse flag; initial value = the `initiallyExpanded` parameter (function default false); explicitly keyed by the caller-supplied `stateKey: String` parameter, so distinct call sites (or a reused logical section) persist independent expansion state across recomposition/rotation/process restore.


### ヘルパー関数

- `v6AlgorithmLabel`（`internal fun v6AlgorithmLabel(alg: V6Algorithm): String`）: Pure, non-Composable mapping function that returns a Japanese display label for a given V6Algorithm enum value via an exhaustive `when` expression: AUTO -> "おまかせ", V5 -> "高速", ALNS -> "破壊再構築", RSI -> "違反集中", RSI_PLUS -> "違反集中＋", PORTFOLIO -> "方式ミックス". Used both for the V6Algorithm picker buttons in SettingsCard's UI and (implicitly, via a comment reference) elsewhere in the app wherever a V6Algorithm value needs a human-readable label.

### Android/Compose固有パターン（要変換方針）

- remember(key) { ... } for derived/memoized pure values: MonthPickerCard's `label` (keyed on ui.startDate), SettingsCard's `versionLabel` (keyed on LocalContext ctx, effectively computed once per composition lifetime), MonthlyChecklistCard's `wishStaff` (keyed on ui.wishes+staffN). These recompute only when their key(s) change and have no lifecycle callback. WinUI3/x:Bind has no direct 'remember' primitive — needs either a computed ViewModel property with PropertyChanged raised on the same dependency changes, or a manually-cached private field invalidated in the relevant setter, to avoid recomputing every render.
- rememberSaveable { mutableStateOf(false) } (AdvancedSettingsSection, anonymous call-site key) and rememberSaveable(stateKey) { mutableStateOf(initiallyExpanded) } (CollapsibleSection, explicit string key reused across many OTHER files' call sites, e.g. the project's 'yr_count' key). This is local, UI-only boolean state that Compose additionally survives across process death via a SavedStateHandle-style bundle keyed by call-site slot or the explicit stateKey string. WinUI3 has no built-in analog: a plain bool field/DependencyProperty on the control survives only in-memory; cross-session persistence (if required) needs an explicit app-level keyed state store using the same stateKey strings.
- LocalContext.current (CompositionLocal) in SettingsCard, used only to call ctx.packageManager.getPackageInfo(ctx.packageName, 0) for the app version string. Android-specific system-service access via composition-local context. WinUI3 equivalent is Windows.ApplicationModel.Package.Current.Id.Version (a struct with Major/Minor/Build/Revision, not a PackageInfo) — call directly, no CompositionLocal needed.
- Modifier.horizontalScroll(rememberScrollState()) in SettingsCard (the V6Algorithm.values() button Row) and Modifier.verticalScroll(rememberScrollState()) in LogsCard (the op-log Box capped at heightIn(max = 220.dp)). Each pairs an ephemeral (non-Saveable) scroll-position remember with a scrollable Modifier. WinUI3 equivalent is a horizontally/vertically scrolling ScrollViewer wrapping a StackPanel/ItemsControl; scroll position is not restored across recomposition loss in Compose either, so no extra persistence is required, but the Row-in-ScrollViewer vs Column-in-Box-in-ScrollViewer structures must be rebuilt explicitly in XAML.
- Modifier.clickable(...) applied to whole arbitrary Row/Surface/Column containers rather than a native clickable widget — used in GuideRow, ChecklistRow, the header Row of AdvancedSettingsSection, and the header Surface of CollapsibleSection. Compose's clickable modifier adds ripple + semantics + pointer-input handling to any layout node. WinUI3 has no 'make any panel clickable' modifier — needs Tapped/PointerPressed event handlers on the Grid/StackPanel plus manual visual-state (pressed/hover) feedback, or wrapping in an unstyled ButtonBase, which is the closer XAML idiom.
- Modifier.semantics { contentDescription = "..." } used twice in OptimizationTuningSection on the worker −/+ Buttons ('同時計算数を減らす'/'同時計算数を増やす'). Android/Compose accessibility label attached via a modifier chain. WinUI3 equivalent is the attached property AutomationProperties.Name set on the corresponding Button in XAML.
- MaterialTheme.colorScheme.* / MaterialTheme.typography.* / MaterialTheme.shapes.* CompositionLocal-based theme-token lookups, used pervasively (every single composable in this file reads MaterialTheme.colorScheme and/or .typography at minimum, e.g. cs.primary, cs.onSurfaceVariant, cs.error, cs.tertiary, cs.secondaryContainer, cs.onSecondaryContainer, cs.surfaceVariant). Compose resolves these per-composition via a CompositionLocalProvider set once high in the tree (MainActivity's MagiTheme, per project docs). WinUI3 equivalent is {ThemeResource ...} XAML markup-extension lookups against a merged ResourceDictionary — each token access site needs a corresponding named brush/typography resource.
- Local `data class RowV(val sym: String, val q: Int, val d: Int, val maxNeed: Int)` declared INSIDE the StaffingRealityCard @Composable function body (a Kotlin local class). C# does not support declaring a class/record type inside a method body the way Kotlin does — this type must be hoisted to a private nested record/class at the enclosing Page/UserControl level (or a static helper file) in the C# port.
- run { ... } scope-function block in OptimizationTuningSection wrapping the `cores`/`hyp`/`spawn`/`overNote` temporaries purely to limit their visibility — this is plain Kotlin scoping, NOT a remember/effect/coroutine construct; translate as an inline local block or an extracted private helper method, not as any async/lifecycle API.
- Dynamic per-enum-value UI generation: SettingsCard iterates `V6Algorithm.values().forEach { alg -> ... }` to emit one Button (selected: `ui.v6Algorithm == alg`) or OutlinedButton (unselected) per enum constant inside a horizontally-scrolling Row. Needs an ItemsRepeater/ItemsControl bound to the enum values (or a manually unrolled RadioButtons/ToggleButton group) in XAML, with per-item logic to pick the selected visual style.
- Nullable trailing-lambda optional callback params (`onOpenWish: (() -> Unit)? = null` on SetupGuideCard/MonthlyChecklistCard; `onClick: (() -> Unit)? = null` on GuideRow/ChecklistRow) used to conditionally enable both the clickable Modifier AND the trailing '›' arrow glyph/text — i.e. one nullable param drives two separate rendering decisions. In C#/WinUI3 this maps to a nullable ICommand/Action property on a row control, with both IsHitTestVisible/Tapped-wiring and the arrow-glyph Visibility bound off `Command != null`.

### 落とし穴

- Extensive dead/unused imports confirmed via grep (each symbol appears ONLY on its own `import` line, never referenced anywhere else in the file's code): rememberLauncherForActivityResult, ActivityResultContracts, BorderStroke, BoxWithConstraints, FlowRow, ExperimentalLayoutApi, ButtonDefaults, CardDefaults, CircularProgressIndicator, LinearProgressIndicator, AlertDialog, ModalBottomSheet, rememberModalBottomSheetState, NavigationBar, NavigationBarItem, Scaffold, ExperimentalMaterial3Api, mutableIntStateOf, rememberCoroutineScope, mutableStateListOf, drawBehind, CornerRadius, Offset, Size, PathEffect, drawscope.Stroke, ImageVector, LocalHapticFeedback, HapticFeedbackType, detectHorizontalDragGestures, pointerInput, collectAsStateWithLifecycle, viewmodel.compose.viewModel, kotlinx.coroutines.Dispatchers, kotlinx.coroutines.withContext, kotlinx.coroutines.launch, com.magi.app.v6.V6PortReport, com.magi.app.v6.CoverageVerdict, com.magi.app.v6.MirrorKeys, and 8 of the 10 imported Icons.Filled.* (Add, Assessment, CheckCircle, DateRange, Edit, Home, PlayArrow, Settings, Stop, Warning — only KeyboardArrowUp/KeyboardArrowDown are actually used, in AdvancedSettingsSection). Do NOT infer that any dialogs/effects/coroutine-launch/drag-gesture/accessibility-haptics patterns exist in this file just because these symbols are imported — this file's actual scope is much smaller than its import list suggests, consistent with the project's history of composables being progressively moved OUT of this file into other files over many revisions.
- StaffingRealityCard's data computation (canDoCount fill-loop over every staff member × their vm.allowedShiftsFor(i) results, plus the rows build-loop over every shift × every day calling vm.needCellLimits(k,j) once per day) is intentionally NOT wrapped in remember — it fully recomputes on every recomposition of this composable, including recompositions triggered by unrelated state changes elsewhere in the same ui object. Preserve this as-is (don't silently add memoization) unless an improvement is explicitly requested.
- MonthlyChecklistCard memoizes only ONE of its four derived values (wishStaff, via remember(ui.wishes, staffN)); needExceptions (vm.needDayOverrides().size), needStdOk (vm.ws1()?.shifts?.any{...} == true), and issues (ui.settingIssues.size) are plain unmemoized vals recomputed on every recomposition. This selective/asymmetric memoization is easy to accidentally 'normalize' during translation — preserve it faithfully.
- AppearanceCard is the ONLY composable in this file that does NOT take (ui: UiState, vm: MagiViewModel) as its params — it takes 6 raw params instead (oneHand/onOneHand/proMode/onProMode/plainCellBorder/onPlainCellBorder). The caller (outside this file) must thread values like ui.oneHand and wrapper lambdas like { vm.setOneHand(it) } in from elsewhere; do not assume this card reads UiState/MagiViewModel directly, and do not assume the exact ui.xxx field names it corresponds to since they are not visible in this file.
- LogsCard's and DataActionsCard's action buttons are ALL driven by caller-supplied lambda parameters (onExportLog, onExportJson, onOpenJson, onSample, onSaveJson, onOpenCsv, onSaveCsv, onCheck, onSaveStaffCsv, onSaveWishesCsv, onSaveConstraintsCsv, onRestorePrev) — neither composable takes a vm: MagiViewModel parameter at all. The real vm.xxx() calls those lambdas ultimately perform live in the CALLER file (not opened in this task, likely MagiApp.kt) — do not attribute those ViewModel calls to this file.
- LogsCard's two export buttons are enabled by `hasAny = ui.opLog.isNotEmpty() || ui.logs.isNotEmpty()`, NOT by `!ui.running` like almost every other interactive control in this file — this is the one explicit exception to the pervasive 'disable while running' pattern used elsewhere (MonthPickerCard, SettingsCard, OptimizationTuningSection, MonthlyChecklistCard, DataActionsCard all gate on !ui.running in ~15+ places).
- SectionNote and CollapsibleSection are generic presentation-only primitives defined in this file but NEVER invoked anywhere else within this same file — per project history they are consumed by OTHER UI files not opened in this task (e.g. CollapsibleSection("③ 回数（1人あたり）", "yr_count") wrapped in key(ui.editRev){...} in the app-shell file, and SectionNote by editor files). When porting, make these broadly-reusable WinUI3 controls/templates (e.g. an Expander-style UserControl for CollapsibleSection keyed by a string identifier, a styled bordered TextBlock for SectionNote), not page-private controls.
- GuideRow and ChecklistRow are near-duplicate row composables (same layout shape: leading glyph + label + trailing value, optional whole-row click, optional trailing '›') but use DIFFERENT semantic color pairs and glyph characters for their leading status glyph: GuideRow's done-glyph is '✓' (cs.primary) / '・' (cs.onSurfaceVariant); ChecklistRow's ok-glyph is '✓' (cs.tertiary) / '！' (cs.error). Do not merge these into one shared control without preserving both distinct variants (ChecklistRow's red '！' signals a stronger attention-needed state than GuideRow's neutral gray '・').
- Two structurally independent expand/collapse implementations exist for the same conceptual UI pattern: AdvancedSettingsSection inlines its own header Row + rememberSaveable{mutableStateOf(false)} (anonymous key) + a circular-Surface-wrapped Icon(KeyboardArrowUp/Down) chevron; CollapsibleSection is a separate, more generic version using text glyphs ('▼ 閉じる'/'▶ 開く') instead of an Icon, and REQUIRES an explicit stateKey string param. AdvancedSettingsSection does NOT reuse CollapsibleSection even though it structurally could — preserve as two separate implementations unless consolidation is explicitly requested.
- SettingsCard's budget stepper clamps the new value via .coerceAtLeast(10) / .coerceAtMost(MAX_BUDGET_SEC) in the View layer BEFORE calling vm.setBudget(...), AND separately re-checks the same bounds (ui.budgetSec > 10 / ui.budgetSec < MAX_BUDGET_SEC) for the Button's `enabled` flag — a redundant double-bound-check pattern. MAX_BUDGET_SEC's exact numeric value and defining file are unknown from this file alone (the on-screen text says '最長5分' i.e. max 5 minutes, strongly suggesting 300, but this is inferred from a UI string, not confirmed in-file — verify against its actual declaration when porting).
- SettingsCard's AUTO-resolution hint calls V6FinalPort.getAlgorithmLabel(ui.budgetSec) and destructures .icon/.name/.desc off the returned object — this is a SEPARATE labeling path from the local v6AlgorithmLabel(V6Algorithm) helper function in this same file (which maps the raw enum selection to a label). Do not conflate 'the label for the user's V6Algorithm.AUTO selection' (v6AlgorithmLabel(V6Algorithm.AUTO) == 'おまかせ') with 'the label for the algorithm AUTO actually resolves to at the current budget' (V6FinalPort.getAlgorithmLabel(ui.budgetSec).name) — both are shown together in the same UI region but come from different sources and describe different things.
- Local `data class RowV(...)` is declared INSIDE the StaffingRealityCard function body (a Kotlin local class) — this construct has no direct C# equivalent (C# cannot declare a class/record type inside a method body); it must be hoisted to a private nested type when porting.
- This file contains ZERO actual AlertDialog / DropdownMenu / Snackbar / ModalBottomSheet usage despite AlertDialog and ModalBottomSheet both being imported — all real dialog/overlay/bottom-sheet behavior for these settings screens lives in other files not opened in this task. Do not invent dialog ports for MagiSetupCards.kt.
- Visibility split: OptimizationTuningSection and ChecklistRow are declared `private fun` (only callable from within MagiSetupCards.kt itself, by AdvancedSettingsSection and MonthlyChecklistCard respectively); every other composable in the file is `internal fun` (visible module-wide, callable from other UI files in the same Android module/app). Preserve this visibility distinction in the C# port (e.g. private helper methods/controls scoped to one file/class vs internal-to-assembly public UserControls).

### 外部依存

- `UiState — data/state holder type read by nearly every composable in this file (ui.loaded, ui.running, ui.startDate, ui.budgetSec, ui.v6Algorithm, ui.workers, ui.nativeAccel, ui.nativeParity, ui.blockSwapC3nFilter, ui.wideC3nBreak, ui.softPolish, ui.staffNames, ui.wishes, ui.settingIssues, ui.days, ui.shiftSymbols, ui.reviewMemos, ui.opLog, ui.logs, ui.prevBackupAvailable); not imported (same package com.magi.app.ui), not opened in this task — defined elsewhere, e.g. MagiUiState.kt per project convention.`
- `MagiViewModel — ViewModel type; not imported (same package), not opened in this task. Methods invoked from this file: setBudget, setV6Algorithm, setWorkers, setNativeAccel, setNativeParity, setBlockSwapC3nFilter, setWideC3nBreak, setSoftPolish, shiftMonth, setNextMonth, setupCounts, needDayOverrides, ws1, allowedShiftsFor, needCellLimits, removeReviewMemo.`
- `com.magi.app.v6.V6Algorithm — imported enum; .values() iterated in SettingsCard, members AUTO/V5/ALNS/RSI/RSI_PLUS/PORTFOLIO referenced by name in v6AlgorithmLabel's when-expression.`
- `com.magi.app.v6.V6FinalPort — imported object; V6FinalPort.getAlgorithmLabel(ui.budgetSec) called in SettingsCard, return value's .icon/.name/.desc fields are read.`
- `com.magi.app.v6.V6NativeOptimizer — referenced only via fully-qualified name (NOT imported at the top of the file); V6NativeOptimizer.hypothesisCount(ui.workers) and V6NativeOptimizer.hypothesisSpawnPlan(ui.workers, hyp) called in OptimizationTuningSection.`
- `MAX_BUDGET_SEC — top-level Int constant referenced but not declared in this file; used as the upper clamp bound for the budget-seconds stepper (SettingsCard).`
- `magiWarnColors() — function referenced but not declared in this file, returns a Pair-like value consumed via .second; used in StaffingRealityCard for the 'no slack remaining' warning glyph color.`
- `DeleteRowButton — composable referenced but not declared in this file; used in ReviewMemoCard as DeleteRowButton(onClick = { vm.removeReviewMemo(idx) }, text = "済").`
- `MagiSegmentedControl — composable referenced but not declared in this file; used in AppearanceCard as MagiSegmentedControl(options = listOf("かんたん", "プロ"), selected = if (proMode) 1 else 0, onSelect = { onProMode(it == 1) }).`


---

## Ws1Editor.kt

### 役割

Ws1Editor.kt implements the 年間マスター (annual master data) editor card for the MAGI shift-optimizer Android app: the 'ws1' problem-definition screen where an operator edits the scheduling period length (day count), the 'use a second need-pattern' flag, the roster of shift types (symbol/name/min-max headcount), the roster of staff groups, the roster of individual staff (name + group membership), the group×shift 'can-do' capability matrix, and per-group per-shift soft 'apt' (適切回数/appropriate-count) targets. It is a thin CRUD presentation layer over MagiViewModel's ws1* mutation API — every edit re-dimensions the working schedule table and re-runs the constraint check downstream — and embeds live validation against the ported V6SanityPort business-rule engine directly inside its editing dialogs (e.g. flagging need1>need2 range conflicts, and warning when summed apt targets exceed achievable capacity for a shift). It renders as a single Card (Ws1Card) with six labeled sections plus a family of small shared dialog/field helper composables (W1Shell/W1Text/W1Field/LoadoutHeader) and a separately-exported apt-target sub-section (AptSection/AptStepper) used elsewhere in the app's 'counts' consolidation card.

### Composable関数

#### `Ws1Card`

```kotlin
@OptIn(ExperimentalLayoutApi::class) @Composable fun Ws1Card(ui: UiState, vm: MagiViewModel)
```

**描画内容:** Renders a Card holding the full annual-master editing form: LOADOUT header; PERIOD row (read-only start/end date text, an editable day-count field + '変更' commit button, and a use2 Switch); ARSENAL section listing every shift as a tappable row (symbol/name/min/max, edit + conditional delete buttons) plus 'シフト追加'/'一括追加' buttons; SQUAD section listing every group as a tappable row (symbol/name, edit + conditionally-rendered delete) plus 'グループ追加'; PARTY section listing every staff member as a tappable row (name + group symbol, edit + conditional delete) plus '職員追加'/'一括追加'; and a MATRIX section rendering, per group, a FlowRow of FilterChips (one per shift) toggling that group's can-do capability. It also owns and renders, via a trailing `when` block, whichever dialog its local `dialog` state currently selects — one of the four add/edit form dialogs (via the shared W1Shell) or the shift/group/staff delete-confirmation AlertDialog.

**ViewModel呼出:**

- `vm.ws1()`
- `vm.ws1ResizeDays(it)`
- `vm.ws1SetUse2(it)`
- `vm.ws1SetGroupShift(g, k, !on)`
- `vm.ws1ShiftRefCount(k)`
- `vm.ws1GroupRefCount(g)`
- `vm.ws1CanRemoveGroup(g)`
- `vm.ws1GroupMemberCount(g)`
- `vm.ws1EditShift(d.k, n, kg, n1, n2)`
- `vm.ws1AddShift(n, kg, n1, n2)`
- `vm.ws1EditGroup(d.g, n, kg)`
- `vm.ws1AddGroup(n, kg)`
- `vm.ws1EditStaff(d.i, n, gi)`
- `vm.ws1AddStaff(n, gi)`
- `vm.ws1RemoveShift(d.index)`
- `vm.ws1RemoveGroup(d.index)`
- `vm.ws1RemoveStaff(d.index)`

**読むUiStateフィールド:**

- `ui.running`

**Compose固有ローカル状態:**

- dialog: Ws1Dialog? = null — var dialog by remember { mutableStateOf<Ws1Dialog?>(null) }; holds which modal (if any) is open and its payload
- daysText: String = v.days.toString() — var daysText by remember(v.days) { mutableStateOf(v.days.toString()) }; re-keyed (reset) whenever v.days changes upstream


#### `ShiftDialog`

```kotlin
@Composable private fun ShiftDialog(title: String, name0: String, kigou0: String, need10: String, need20: String, onOk: (String, String, String, String) -> Unit, onClose: () -> Unit)
```

**描画内容:** Renders a W1Shell-hosted form (kigou text field, name text field, a Row of need1/need2 numeric fields) for adding or editing one shift record. Shows a red validation hint (NEED_ORDER_HINT) and marks both need fields isError=true when need1>need2 per V6SanityPort.rangeOrderConflict(need1, need2), and disables the shell's OK button in that case or when kigou is blank.

**Compose固有ローカル状態:**

- name: String = name0 — remember { mutableStateOf(name0) }
- kigou: String = kigou0 — remember { mutableStateOf(kigou0) }
- need1: String = need10 — remember { mutableStateOf(need10) }
- need2: String = need20 — remember { mutableStateOf(need20) }


#### `GroupDialog`

```kotlin
@Composable private fun GroupDialog(title: String, name0: String, kigou0: String, onOk: (String, String) -> Unit, onClose: () -> Unit)
```

**描画内容:** Renders a W1Shell-hosted form (kigou text field, name text field) for adding or editing one group record; the shell's OK button is enabled only when kigou is non-blank.

**Compose固有ローカル状態:**

- name: String = name0 — remember { mutableStateOf(name0) }
- kigou: String = kigou0 — remember { mutableStateOf(kigou0) }


#### `StaffDialog`

```kotlin
@Composable internal fun StaffDialog(title: String, name0: String, group0: Int, groupKigou: List<String>, onOk: (String, Int) -> Unit, onClose: () -> Unit)
```

**描画内容:** Renders a W1Shell-hosted form (name text field, and either a group-selection dropdown button showing the currently chosen group's kigou or, when groupKigou is empty, an error-colored 'add a group first' hint) for adding or editing one staff member's name and group assignment. OK is enabled only when name is non-blank and groupKigou is non-empty.

**Compose固有ローカル状態:**

- name: String = name0 — remember { mutableStateOf(name0) }
- gi: Int = group0.coerceIn(0, (groupKigou.size - 1).coerceAtLeast(0)) — remember { mutableStateOf(...) }; clamped initial group index
- open: Boolean = false — remember { mutableStateOf(false) }; controls the group DropdownMenu's expanded state


#### `BulkAddDialog`

```kotlin
@Composable private fun BulkAddDialog(title: String, hint: String, groups: List<String>?, onApply: (List<String>, Int) -> Unit, onClose: () -> Unit)
```

**描画内容:** Renders a W1Shell-hosted form (hint text, a multiline OutlinedTextField for newline-separated entries, and — only when groups != null — a default-group dropdown or a 'create a group first' error hint) used to bulk-add many shifts or many staff at once from pasted/typed lines. Shows a live '追加: N件' count derived from non-blank trimmed lines and enables OK only when at least one such line exists (and, for staff, when groups is non-empty).

**Compose固有ローカル状態:**

- text: String = "" — remember { mutableStateOf("") }; raw multiline input
- gi: Int = 0 — remember { mutableStateOf(0) }; default group index applied to every bulk-added staff line
- open: Boolean = false — remember { mutableStateOf(false) }; controls the default-group DropdownMenu's expanded state


#### `W1Shell`

```kotlin
@Composable private fun W1Shell(title: String, onClose: () -> Unit, onOk: () -> Unit, okEnabled: Boolean, content: @Composable ColumnScope.() -> Unit)
```

**描画内容:** Renders the shared Material3 AlertDialog chrome reused by all four add/edit dialogs: a DialogHeader(title, onClose) as the dialog title, a scrollable Column (verticalScroll + spacedBy 10.dp) hosting the caller-supplied `content` slot as the dialog body, a DialogConfirmButton('OK', enabled = okEnabled, onClick = onOk) as confirmButton, and a DialogDismissButton(onClick = onClose) as dismissButton; onDismissRequest also calls onClose.


#### `W1Text`

```kotlin
@Composable private fun W1Text(label: String, value: String, onChange: (String) -> Unit)
```

**描画内容:** Renders a single full-width, single-line OutlinedTextField bound to value/onChange with the given label; used for free-text fields such as kigou and name.


#### `W1Field`

```kotlin
@Composable private fun W1Field(label: String, value: String, modifier: Modifier = Modifier, isError: Boolean = false, onChange: (String) -> Unit)
```

**描画内容:** Renders a single-line, digit-filtered OutlinedTextField (onValueChange strips non-digit characters before forwarding, keyboardOptions = Number, optional isError red styling) used for numeric-only fields such as need1/need2.


#### `AptSection`

```kotlin
@OptIn(ExperimentalLayoutApi::class) @Composable internal fun AptSection(ui: UiState, vm: MagiViewModel)
```

**描画内容:** Renders the '目標（やわらかい）' (soft apt-target) grid: a 'space above' overload-warning Surface (errorContainer color) listing every shift whose summed group apt targets exceed its achievable capacity per vm.aptBalances(), each with a per-shift explanatory message and remediation hint; a 'reset all' Row showing the count of currently-set targets plus a DeleteRowButton('目標を全リセット', enabled only when count > 0); and, per group with ≥1 allowed shift, a group-name label followed by a FlowRow of AptStepper controls (one per allowed shift) reading/writing v.groupShiftApt via vm.ws1SetGroupApt. Also renders its own confirm-reset-all AlertDialog gated by local confirmResetApt state, whose confirm action calls vm.ws1ResetGroupApt().

**ViewModel呼出:**

- `vm.ws1()`
- `vm.aptBalances()`
- `vm.ws1SetGroupApt(g, k, it)`
- `vm.ws1ResetGroupApt()`

**読むUiStateフィールド:**

- `ui.editRev`
- `ui.structureEdited`

**Compose固有ローカル状態:**

- confirmResetApt: Boolean = false — remember { mutableStateOf(false) }; controls the reset-all-apt-targets confirmation AlertDialog
- overloaded (derived list, not a mutableStateOf) — remember(ui.editRev, ui.structureEdited) { vm.aptBalances().filter { it.overloaded } }; recomputed whenever ui.editRev or ui.structureEdited changes


#### `AptStepper`

```kotlin
@Composable private fun AptStepper(label: String, value: String, onChange: (String) -> Unit)
```

**描画内容:** Renders one '− value ＋' Row control for a single group/shift apt-target cell: a '−' TextButton and a '＋' TextButton (each with an accessibility semantics.contentDescription) that step the string value through blank⇄0⇄1⇄2… and report the new string via onChange; the current value (or an em-dash '—' placeholder when blank) is shown in a fixed-width monospace Text between the two buttons. Neither button takes an `enabled` parameter (always tappable regardless of ui.running, since this composable has no ui: UiState parameter at all).


#### `LoadoutHeader`

```kotlin
@Composable private fun LoadoutHeader(code: String, jp: String)
```

**描画内容:** Renders a small two-part section-header Row: a bold, letter-spaced English 'code name' label (12sp, primary color) followed by a bold Japanese description label (14sp, onSurface color); used at the top of every major section of Ws1Card (LOADOUT/PERIOD/ARSENAL/SQUAD/PARTY/MATRIX).


### ダイアログ/オーバーレイ

- W1Shell -> AlertDialog (Material3): generic modal shell reused for 4 dialog kinds (ShiftDialog: add/edit one shift's kigou/name/need1/need2; GroupDialog: add/edit one group's kigou/name; StaffDialog: add/edit one staff's name + group; BulkAddDialog: multiline bulk-add for shifts or staff). Triggered by setting Ws1Card's local `dialog` state to Ws1Dialog.EditShift/AddShift/EditGroup/AddGroup/EditStaff/AddStaff/BulkAddShift/BulkAddStaff. Confirm ('OK', gated by an okEnabled validity boolean specific to each dialog) invokes the caller-supplied onOk which calls the matching vm.ws1AddXxx/ws1EditXxx then sets dialog=null. Dismiss (DialogDismissButton) and onDismissRequest both call onClose, which sets dialog=null with no mutation.
- Ws1Card's inline AlertDialog for Ws1Dialog.ConfirmDelete: confirms deletion of one shift/group/staff row. Triggered by tapping a row's DeleteRowButton, which pre-computes a reference-count `note` (via vm.ws1ShiftRefCount/vm.ws1GroupRefCount, empty string if 0; for staff a fixed non-conditional warning string) and a `label` before opening. Confirm (DialogDangerButton '削除') dispatches on d.kind ('shift'/'group'/'staff', a raw String discriminator, non-exhaustive when-statement with no else branch) to vm.ws1RemoveShift/ws1RemoveGroup/ws1RemoveStaff, then dialog=null. Dismiss via DialogDismissButton or onDismissRequest sets dialog=null with no mutation. Body text: '${d.label} を削除します。${d.note}残りの設定は自動で調整されます。よろしいですか？'.
- StaffDialog's DropdownMenu (group picker): opened by tapping an OutlinedButton showing the currently selected group's kigou (or '(なし)' fallback); each DropdownMenuItem sets local `gi` to that group's index and closes the menu (open=false). Rendered only when groupKigou is non-empty; otherwise an error hint is shown instead and the parent dialog's OK stays disabled.
- BulkAddDialog's DropdownMenu (default-group picker, staff bulk-add path only): identical open/select/close pattern to StaffDialog's, selecting the default group index `gi` that will be applied to every bulk-added staff line. Rendered only when `groups != null`; entirely absent for shift bulk-add.
- AptSection's inline AlertDialog for confirmResetApt: confirms clearing every group×shift apt target back to blank. Triggered by the 'reset all' DeleteRowButton labeled '目標を全リセット' (enabled only when at least one target is currently set). Confirm (DialogDangerButton '全リセット') calls vm.ws1ResetGroupApt() then closes (confirmResetApt=false). Dismiss via DialogDismissButton sets confirmResetApt=false with no mutation. Body explains the reset is undoable via '元に戻す' and does not affect canDo assignments, range limits, or the schedule itself.

### Android/Compose固有パターン（要変換方針）

- remember { mutableStateOf(...) }: used for every dialog's local form-field state (Ws1Card.dialog; ShiftDialog.name/kigou/need1/need2; GroupDialog.name/kigou; StaffDialog.name/gi/open; BulkAddDialog.text/gi/open; AptSection.confirmResetApt): Compose's recomposition-scoped mutable state with no automatic persistence; a WinUI3/x:Bind port needs either ViewModel-bound properties (INotifyPropertyChanged) or code-behind private fields with explicit UI-update calls, since XAML data binding has no built-in equivalent of 'local Composable state that resets on recomposition key change'.
- remember(v.days) { mutableStateOf(v.days.toString()) } (Ws1Card.daysText): a KEYED remember — the local text buffer is re-initialized to v.days.toString() only when the upstream v.days value itself changes (e.g. after a successful ws1ResizeDays commit), not on every keystroke, so user-typed-but-uncommitted text survives ordinary recomposition. Needs a deliberate 'reset bound TextBox.Text on source-property-changed' handler in WinUI3, not a naive one-way/two-way binding.
- remember(ui.editRev, ui.structureEdited) { vm.aptBalances().filter { it.overloaded } } (AptSection.overloaded): a keyed remember working around Android/Compose StateFlow's structural-equality change-suppression (documented project-wide pattern: ui.editRev is a monotonic counter bumped by every structural edit so Compose is forced to recompute derived values it would otherwise skip). WinUI3 INotifyPropertyChanged doesn't suppress notifications this way, so the literal workaround isn't needed, but the underlying intent ('recompute the apt-overload warning after any structural edit, from any screen') must be reproduced via whatever change-notification the port uses.
- Modifier.clickable(enabled = !ui.running) { dialog = Ws1Dialog.Edit... } applied to entire Rows for shift/group/staff list items (tap-anywhere-to-edit), coexisting with a nested EditRowButton performing the identical action (and, on shift/staff rows, a nested DeleteRowButton performing a different action): Compose's clickable on the inner Button/IconButton consumes its own tap so the outer row-click does not also fire. A WinUI3 Grid/StackPanel + Tapped-event translation must verify the nested Button's click marks the routed event Handled so it doesn't double-fire the row handler.
- @OptIn(ExperimentalLayoutApi::class) FlowRow (androidx.compose.foundation.layout.FlowRow): used twice — the MATRIX group×shift FilterChip grid in Ws1Card, and the apt-target AptStepper grid in AptSection — to wrap chip rows onto multiple lines. WinUI3 has no built-in FlowRow/WrapPanel in classic XAML; requires the Community Toolkit WrapPanel or a custom Panel, chosen deliberately for both grids.
- FilterChip (Material3) with selected=on, enabled=!ui.running, a monospace kigou label, and a leadingIcon that is Icons.Filled.Check only when on==true (else null): no 1:1 WinUI3 control; needs a custom/styled ToggleButton reproducing both the selected visual state and the conditional-icon behavior.
- W1Shell's content: @Composable ColumnScope.() -> Unit parameter is a slot-API / scoped-lambda: ShiftDialog/GroupDialog/StaffDialog/BulkAddDialog each supply different child form fields as a trailing composable lambda placed inside the shell's scrollable Column. WinUI3/XAML has no equivalent of passing arbitrary child UI as a function with an implicit layout-scope receiver; each call site needs to become either a separate ContentDialog/UserControl per dialog kind, or a ContentControl/DataTemplateSelector keyed off a discriminator, populated in code-behind.
- sealed interface Ws1Dialog with data class variants (EditShift/EditGroup/EditStaff/ConfirmDelete, carrying payload) and object variants (AddShift/AddGroup/AddStaff/BulkAddShift/BulkAddStaff, parameterless singletons), driven through var dialog: Ws1Dialog? and an exhaustive 'when (val d = dialog) { is X -> …; Y -> …; null -> Unit }' with no else/default arm: C# has no built-in closed-hierarchy ADT with this ergonomics or compiler-enforced exhaustiveness. Needs a deliberate translation (e.g. enum discriminator + nullable payload record, or a sealed abstract record hierarchy with pattern matching) plus a deliberate '_ => throw' style default arm to replicate Kotlin's 'new variant without a matching branch fails to compile' safety net.
- Kotlin default parameters (W1Field's modifier: Modifier = Modifier, isError: Boolean = false; ConfirmDelete's note: String = ""): work transparently for plain function calls in Kotlin but have no equivalent in XAML markup, and C# optional parameters don't help when a control is instantiated declaratively; requires explicit overloads or explicit values at every call site during translation.
- Higher-order functional parameters used systematically across every dialog composable (onOk: (String,...)->Unit, onChange: (String)->Unit, onApply: (List<String>,Int)->Unit, onClose: ()->Unit): standard Kotlin event-callback idiom; maps to C# Action/Action<T>/Func<T> delegates or ICommand/RelayCommand objects in a WinUI3 MVVM port — none of these leaf composables hold ViewModel command objects directly, they're purely presentational and report only via lambdas supplied by their caller (Ws1Card).

### 落とし穴

- AptStepper's blank/0 stepping is asymmetric-looking but exact: pressing '−' on a blank value sets '0' (not a no-op); pressing '＋' on blank ALSO sets '0' (via `?: -1` then `+1` then coerceAtLeast(0)); pressing '−' on '0' clears back to blank; pressing '−' on any N>0 decrements normally; '＋' always increments/never goes below 0. Full sequence: blank ⇄ 0 ⇄ 1 ⇄ 2 … Both buttons converge on '0' from blank — this must be preserved exactly, not 'simplified' to a normal numeric up/down.
- Ws1Card.daysText (remember(v.days){...}) is a staged/buffered edit that resets ONLY when the upstream v.days value itself changes (e.g. after a successful ws1ResizeDays commit or a full reload) — typed-but-uncommitted text otherwise survives recomposition. The '変更' commit button has NO parse-validity gating beyond `enabled = !ui.running`; if daysText.toIntOrNull() is null, tapping silently no-ops (no error shown, button stays enabled).
- AptSection is the ONLY mutation-heavy sub-area in this file that does NOT gate its controls on `!ui.running`: the '目標を全リセット' DeleteRowButton is enabled solely by `aptSet > 0`, and AptStepper's +/− TextButtons pass no `enabled` parameter at all (always tappable, even mid-background-computation). Every other row/button/chip in Ws1Card (shifts, groups, staff, and the MATRIX FilterChips) uses `enabled = !ui.running`. Confirm whether this asymmetry should be reproduced as-is or is a latent bug to flag, but do not silently unify it during translation.
- Ws1Dialog is a Kotlin sealed interface mixing parameterized data class variants (EditShift, EditGroup, EditStaff, ConfirmDelete) and parameterless object singletons (AddShift, AddGroup, AddStaff, BulkAddShift, BulkAddStaff), driving one `var dialog by remember { mutableStateOf<Ws1Dialog?>(null) }` and an exhaustive `when (val d = dialog) { is X -> …; Y -> …; null -> Unit }` with no fallback arm — Kotlin's compiler enforces that every variant is handled. C# has no equivalent closed-hierarchy discriminated union with this ergonomics/exhaustiveness guarantee out of the box; needs a deliberate choice (enum discriminator + payload record, or sealed abstract record + pattern matching with an explicit throw-default arm) so a future new dialog kind can't silently fall through unhandled.
- Ws1Dialog.ConfirmDelete.kind is a plain String discriminator ('shift'/'group'/'staff'), dispatched via a NON-exhaustive `when (d.kind) { "shift" -> …; "group" -> …; "staff" -> … }` statement (used as a statement, not an expression, so no else/default is required) inside the delete confirm button's onClick. Any other string value would silently do nothing except close the dialog. A C# port should likely use a real enum for `kind`, but should deliberately decide whether to preserve or eliminate the 'unrecognized value silently no-ops' behavior.
- Delete-confirmation `note` text is populated with DIFFERENT semantics per row kind: for shifts and groups it is CONDITIONAL — computed only when vm.ws1ShiftRefCount(k) / vm.ws1GroupRefCount(g) > 0, else an empty string, so the 'references N constraints' warning shows only if constraints actually reference it — whereas for staff it is an UNCONDITIONAL fixed literal ('この職員の勤務・希望も消えます。') passed on every staff delete regardless of any counted condition. Do not unify these three into one code path; the staff case has no analogous count/gate. The confirm-dialog body concatenates it with no extra separator: `"${d.label} を削除します。${d.note}残りの設定は自動で調整されます。よろしいですか？"` — `note` must already include its own trailing Japanese punctuation when non-empty.
- Group deletion's confirm-dialog `label` is itself conditionally built: `"グループ ${toHankakuKigou(gr.kigou)}" + if (members > 0) "（所属${members}名→先頭グループへ移動）" else ""`, where `members = vm.ws1GroupMemberCount(g)` — an entirely separate conditional annotation from the referencing-constraint `note` described above; both end up concatenated into the same single dialog body string.
- Whole-row `Modifier.clickable(enabled = !ui.running) { dialog = Ws1Dialog.Edit... }` is layered on Rows for shift/group/staff list items in ADDITION to a nested EditRowButton (and, on shift/staff rows, a nested DeleteRowButton) that perform the same or a different action inside the same Row. In Compose the nested Button's own clickable consumes the gesture so the row-click does not double-fire; a WinUI3 Grid/StackPanel+Tapped translation with a nested Button must be verified to have the same non-double-fire behavior, not assumed.
- The shift/group/staff row `if (v.shifts.size > 1)` / group's `vm.ws1CanRemoveGroup(g)` / `if (v.staff.size > 1)` guards use DIFFERENT mechanisms per row kind to decide whether the delete button renders at all: shifts and staff use a client-side `.size > 1` check on the local list, but groups use a dedicated ViewModel-computed boolean (`vm.ws1CanRemoveGroup(g)`). When any of these guards is false, an explanatory red Text is shown instead of (not in addition to) the delete button ('最後の1シフトは削除できません…' / '最後の1グループは削除できません…' / '最後の1名は削除できません…'), each with its own distinct wording. Preserve the per-kind mechanism, do not unify into one shared '.size > 1' check.
- W1Field's onValueChange strips non-digit characters (`it.filter { c -> c.isDigit() }`) and pairs this with `KeyboardOptions(keyboardType = KeyboardType.Number)`, intentionally keeping the bound value as a String (not Int) so that blank remains a valid, distinct 'unset' state (used throughout the app to mean 'no minimum/maximum configured'). WinUI3's NumberBox is NOT a safe drop-in here since it coerces toward a numeric type/NaN rather than preserving an empty-string 'unset' semantic; use a plain TextBox with InputScope=Number plus a BeforeTextChanging/TextChanging digit-filter handler instead.
- `isError: Boolean` on Material3 OutlinedTextField (W1Field, driven from ShiftDialog's `bad` computed flag) gives free built-in red-outline/error theming with zero extra code; WinUI3 TextBox has no native IsError-style property, so the port needs an explicit visual-state/brush-swap trigger bound to the same boolean.
- `v.groups.getOrNull(st.groupIdx)?.kigou?.let { toHankakuKigou(it) } ?: "?"` displays a literal `"?"` glyph when a staff member's groupIdx doesn't resolve (out of range) — this is a DIFFERENT fallback convention from the `"(なし)"` ('none') text used elsewhere in the app for unassigned skillIdx==-1 per project history. Preserve the `"?"` literal as-is here; do not normalize it to match the other screen's convention.
- Bulk-add for shifts sets BOTH kigou and name to the identical raw input line, leaving need1/need2 blank: `lines.forEach { vm.ws1AddShift(it, it, "", "") }`. Bulk-add for staff applies the SAME selected default group index to every line: `lines.forEach { vm.ws1AddStaff(it, gi) }`. Neither path calls a dedicated batch/bulk API on the ViewModel — both loop client-side over the singular ws1AddShift/ws1AddStaff calls, so each resulting row is presumably its own independent mutation from the ViewModel's/undo-stack's perspective (verify against MagiViewModel, which is external to this file, when porting undo semantics).
- Every AlertDialog in this file correctly places only pure-cancel actions in the `dismissButton` slot (DialogDismissButton, no side effects other than closing/resetting local state) and the sole destructive/confirming action always in `confirmButton` (DialogConfirmButton/DialogDangerButton) — i.e. no action-in-dismiss-slot anti-pattern is present here. Confirm this invariant is preserved (not inverted) when porting to WinUI3 ContentDialog's PrimaryButtonClick/CloseButtonClick.
- AptSection's overload-warning message branches its ENTIRE wording (not just one word) on `b.isRest`: rest-shift ('休') overload messages reference '休める日数の上限' (max days-off capacity) and suggest lowering the target or reviewing other shifts' lower bounds, while ordinary-shift overload messages reference '必要人数の合計' (total required headcount) and suggest lowering the target or raising required headcount — two structurally parallel but textually distinct templates keyed off one boolean field on each vm.aptBalances() result item.
- LoadoutHeader renders each major section's heading as a stylized two-part 'English code-name + Japanese description' Row (LOADOUT/PERIOD/ARSENAL/SQUAD/PARTY/MATRIX) — an intentional 'HUD' visual motif per the file's own comment ('HUDコンセプトの二段見出し(英コードネーム — 日本語)を踏襲'); the English words are NOT placeholders needing localization/translation, they must be preserved verbatim as the deliberate stylistic choice they are.
- Rows for shift/group/staff list items use `Modifier.fillMaxWidth().heightIn(min = 48.dp)` as an explicit minimum touch-target height (48dp) for the whole clickable row, on top of whatever intrinsic height the child EditRowButton/DeleteRowButton icons impose — preserve this minimum-hit-target sizing in the WinUI3 translation (e.g. a MinHeight on the row Grid/StackPanel), not just on the inner buttons.
- This file has zero LaunchedEffect, DisposableEffect, derivedStateOf, AnimatedVisibility, BackHandler, rememberCoroutineScope, rememberSaveable, or Modifier.pointerInput usages — all local state is plain (optionally-keyed) `remember { mutableStateOf(...) }`, and every 'effect' is a synchronous imperative callback body (onClick/onOk/onChange lambdas that call a vm.* method then set dialog/confirm state to null/false). There is no async/coroutine work, animation, or process-death state restoration to account for in this specific file.

### 外部依存

- `com.magi.app.toHankakuKigou (top-level function, imported) — converts full-width/zenkaku shift & group symbol strings to half-width/hankaku for display; called throughout on shift kigou, group kigou, and a staff member's group kigou.`
- `com.magi.app.v6.V6SanityPort (object, imported) — V6SanityPort.rangeOrderConflict(need1, need2) is called directly from ShiftDialog for live min>max validation of need1/need2; V6SanityPort.aptBalances(...) is (per project history) the underlying source of truth behind vm.aptBalances(), though not invoked directly from this file.`
- `UiState (type, not defined in this file) — fields read in this file: running, editRev, structureEdited.`
- `MagiViewModel (type, not defined in this file) — methods invoked: ws1(), ws1ResizeDays(Int), ws1SetUse2(Boolean), ws1SetGroupShift(Int,Int,Boolean), ws1ShiftRefCount(Int), ws1GroupRefCount(Int), ws1CanRemoveGroup(Int), ws1GroupMemberCount(Int), ws1EditShift(Int,String,String,String,String), ws1AddShift(String,String,String,String), ws1EditGroup(Int,String,String), ws1AddGroup(String,String), ws1EditStaff(Int,String,Int), ws1AddStaff(String,Int), ws1RemoveShift(Int), ws1RemoveGroup(Int), ws1RemoveStaff(Int), aptBalances(), ws1SetGroupApt(Int,Int,String), ws1ResetGroupApt(). Also the return type of vm.ws1() — an editor snapshot with fields days, use2, shifts (list with k/name/kigou/need1/need2), groups (list with name/kigou), staff (list with name/groupIdx), groupShift (2D Int/flag matrix), groupShiftApt (2D String matrix), startDate, endDate — and the element type of vm.aptBalances()'s return list, with fields overloaded (Boolean), kigou (String), aptSum (Int/number), isRest (Boolean), capacity (Int/number), shortfall (Int/number).`
- `EditRowButton, DeleteRowButton, AddRowButton (Composables, not defined in this file — shared row-action affordances, referenced elsewhere in the project as living in Affordance.kt) — reusable icon buttons used throughout Ws1Card and AptSection for edit/delete/add actions; DeleteRowButton is also called with an explicit `text` parameter in AptSection ('目標を全リセット').`
- `DialogHeader, DialogConfirmButton, DialogDismissButton, DialogDangerButton (Composables, not defined in this file — shared dialog chrome, referenced elsewhere in the project as living in Affordance.kt) — used to build W1Shell and the inline ConfirmDelete / confirmResetApt AlertDialogs.`
- `NEED_ORDER_HINT (String constant, not defined in this file — referenced elsewhere in the project as living in Affordance.kt) — validation-hint text shown under need1/need2 in ShiftDialog when V6SanityPort.rangeOrderConflict flags a min>max conflict.`


---

## ConstraintEditor.kt

### 役割

This file implements the constraint (business-rule) family CRUD editor UI for the MAGI shift optimizer app. It renders two card variants — `ConstraintsCard` (a filterable list of constraint families: window requirements cons1, personal totals cons2, group ranges/pair-bans cons41/cons42, and sequence patterns cons3/cons3n/cons3m/cons3mn) and `SkillConstraintsCard` (the skill-group-scoped variants cons41s/cons42s) — each showing per-family lists of existing constraint rows with tap-to-edit/delete affordances and an "add" flow. A shared `ConstraintDialog` (rendered via a generic `Shell` AlertDialog wrapper) handles both adding a new row and editing an existing row (index-based prefill) for every constraint family type, dispatching on confirm to the matching `MagiViewModel.addConsX(...)` or `vm.updateConstraint(...)` call. Small reusable form primitives `NumField` (digit-only text input) and `Picker` (dropdown-menu-backed selector) are also defined here and reused across all family-specific dialog bodies.

### Composable関数

#### `ConstraintsCard`

```kotlin
fun ConstraintsCard(ui: UiState, vm: MagiViewModel, title: String = "ルールの編集（勤務の並び・回数）", keys: Set<String>? = null)
```

**描画内容:** Renders a Card containing an optional title (Text, suppressed entirely if `title` is blank), a collapsible ConstraintHelpExpander, and one section per constraint family (from vm.constraintFamilies(), optionally filtered by `keys`) — each section shows the family's title Text, either a placeholder "(なし)" Text or a ConstraintRow per existing row, an "追加" AddRowButton, and a Divider. When `addFamily` or `editTarget` state is non-null it also composes a ConstraintDialog overlay for creating or modifying a single row.

**ViewModel呼出:**

- `vm.constraintFamilies()`
- `vm.removeConstraint(fam.key, idx)`

**読むUiStateフィールド:**

- `ui.running`

**Compose固有ローカル状態:**

- addFamily: String? = null (remember { mutableStateOf<String?>(null) })
- editTarget: Pair<String, Int>? = null (remember { mutableStateOf<Pair<String, Int>?>(null) })


#### `ConstraintHelpExpander`

```kotlin
private fun ConstraintHelpExpander(families: List<MagiViewModel.ConstraintFamilyView>)
```

**描画内容:** Renders a tap-to-toggle header Row (min 48dp, clickable) whose label text switches between "ⓘ 詳しい説明を閉じる" and "ⓘ 詳しい説明（それぞれの条件の意味）"; when expanded (`open == true`) it renders a Column listing, for each family whose key exists in the external `constraintHelp` map, that family's title Text plus its help-body Text (families without a map entry are silently skipped, no placeholder), followed by a shared `CONSTRAINT_HELP_FOOTER` caption Text.

**Compose固有ローカル状態:**

- open: Boolean = false (remember { mutableStateOf(false) })


#### `ConstraintRow`

```kotlin
private fun ConstraintRow(row: String, enabled: Boolean, onEdit: () -> Unit, onDelete: () -> Unit)
```

**描画内容:** Renders a single constraint row as a Row: a weighted, clip-rounded, clickable(enabled=enabled) Column (min 48dp height, vertically centered content) that shows the row's plain-text description and invokes `onEdit` when tapped, followed by a small Spacer, an EditRowButton(onClick=onEdit, enabled=enabled), another Spacer, and a DeleteRowButton(onClick=onDelete, enabled=enabled).


#### `SkillConstraintsCard`

```kotlin
fun SkillConstraintsCard(ui: UiState, vm: MagiViewModel)
```

**描画内容:** Renders a Card whose Column always starts with a primary-colored caption Text ("上の「スキルグループ」に対する専用ルールです。"). If vm.skillGroupKigouList() is empty it renders only a secondary explanatory Text prompting the user to add a skill group first and nothing else. Otherwise it renders a ConstraintHelpExpander followed by one section per family from vm.skillConstraintFamilies() in the same title/rows-or-"(なし)"/ConstraintRow-list/AddRowButton/Divider layout used by ConstraintsCard. It also conditionally renders a ConstraintDialog overlay for add/edit when `addFamily`/`editTarget` is non-null, mirroring ConstraintsCard's pattern.

**ViewModel呼出:**

- `vm.skillConstraintFamilies()`
- `vm.skillGroupKigouList()`
- `vm.removeConstraint(fam.key, idx)`

**読むUiStateフィールド:**

- `ui.running`

**Compose固有ローカル状態:**

- addFamily: String? = null (remember { mutableStateOf<String?>(null) })
- editTarget: Pair<String, Int>? = null (remember { mutableStateOf<Pair<String, Int>?>(null) })


#### `ConstraintDialog`

```kotlin
private fun ConstraintDialog(family: String, vm: MagiViewModel, editIndex: Int? = null, onClose: () -> Unit)
```

**描画内容:** Dispatches on `when (family)` to one of several family-specific dialog bodies, each rendered via the shared Shell(...)/AlertDialog chrome: cons1 = day-count NumField + shift Picker + count NumField; cons2 = shift Picker + count NumField; cons41/cons41s = group-or-skill Picker + shift Picker + a Row of two weighted NumFields (下限/上限) with isError styling and a conditional RANGE_ORDER_HINT Text when the range is invalid; cons42/cons42s = two group-or-skill Picker + shift Picker pairs; cons3/cons3n/cons3m/cons3mn (single shared branch) = a caption Text plus five sequential shift Pickers (slot 1 required, slots 2-5 optional via a blank-prefixed options list). Any other `family` value hits `else -> onClose()`, which renders nothing and immediately invokes the close callback.

**ViewModel呼出:**

- `vm.shiftKigouList()`
- `vm.groupKigouList()`
- `vm.skillGroupKigouList()`
- `vm.constraintRowValues(family, editIndex)`
- `vm.updateConstraint(family, editIndex, values)`
- `vm.addCons1(d1, sk, d2)`
- `vm.addCons2(sk, c)`
- `vm.addCons41(gk, sk, l, u)`
- `vm.addCons42(g1, g2, s1, s2)`
- `vm.addCons41s(gk, sk, l, u)`
- `vm.addCons42s(g1, g2, s1, s2)`
- `vm.addCons3(family, listOf(a, b, c, d, e))`

**Compose固有ローカル状態:**

- init: List<String>? (remember(family, editIndex) { editIndex?.let { vm.constraintRowValues(family, it) } }) — keyed remember, recomputed only when family or editIndex changes
- [cons1] d1: String = init?.getOrNull(0) ?: "" (remember { mutableStateOf(...) })
- [cons1] sk: String = init?.getOrNull(1) ?: shifts.firstOrNull() ?: "" (remember { mutableStateOf(...) })
- [cons1] d2: String = init?.getOrNull(2) ?: "" (remember { mutableStateOf(...) })
- [cons2] sk: String = init?.getOrNull(0) ?: shifts.firstOrNull() ?: "" (remember { mutableStateOf(...) })
- [cons2] c: String = init?.getOrNull(1) ?: "" (remember { mutableStateOf(...) })
- [cons41] gk: String = init?.getOrNull(0) ?: groups.firstOrNull() ?: "" (remember { mutableStateOf(...) })
- [cons41] sk: String = init?.getOrNull(1) ?: shifts.firstOrNull() ?: "" (remember { mutableStateOf(...) })
- [cons41] l: String = init?.getOrNull(2) ?: "" (remember { mutableStateOf(...) })
- [cons41] u: String = init?.getOrNull(3) ?: "" (remember { mutableStateOf(...) })
- [cons42] g1: String = init?.getOrNull(0) ?: groups.firstOrNull() ?: "" (remember { mutableStateOf(...) })
- [cons42] s1: String = init?.getOrNull(1) ?: shifts.firstOrNull() ?: "" (remember { mutableStateOf(...) })
- [cons42] g2: String = init?.getOrNull(2) ?: groups.firstOrNull() ?: "" (remember { mutableStateOf(...) })
- [cons42] s2: String = init?.getOrNull(3) ?: shifts.firstOrNull() ?: "" (remember { mutableStateOf(...) })
- [cons41s] gk: String = init?.getOrNull(0) ?: skills.firstOrNull() ?: "" (remember { mutableStateOf(...) })
- [cons41s] sk: String = init?.getOrNull(1) ?: shifts.firstOrNull() ?: "" (remember { mutableStateOf(...) })
- [cons41s] l: String = init?.getOrNull(2) ?: "" (remember { mutableStateOf(...) })
- [cons41s] u: String = init?.getOrNull(3) ?: "" (remember { mutableStateOf(...) })
- [cons42s] g1: String = init?.getOrNull(0) ?: skills.firstOrNull() ?: "" (remember { mutableStateOf(...) })
- [cons42s] s1: String = init?.getOrNull(1) ?: shifts.firstOrNull() ?: "" (remember { mutableStateOf(...) })
- [cons42s] g2: String = init?.getOrNull(2) ?: skills.firstOrNull() ?: "" (remember { mutableStateOf(...) })
- [cons42s] s2: String = init?.getOrNull(3) ?: shifts.firstOrNull() ?: "" (remember { mutableStateOf(...) })
- [cons3/cons3n/cons3m/cons3mn] a: String = init?.getOrNull(0) ?: shifts.firstOrNull() ?: "" (remember { mutableStateOf(...) })
- [cons3-family] b: String = init?.getOrNull(1) ?: "" (remember { mutableStateOf(...) })
- [cons3-family] c: String = init?.getOrNull(2) ?: "" (remember { mutableStateOf(...) })
- [cons3-family] d: String = init?.getOrNull(3) ?: "" (remember { mutableStateOf(...) })
- [cons3-family] e: String = init?.getOrNull(4) ?: "" (remember { mutableStateOf(...) })


#### `Shell`

```kotlin
private fun Shell(title: String, okLabel: String, onClose: () -> Unit, onAdd: () -> Unit, addEnabled: Boolean, content: @Composable ColumnScope.() -> Unit)
```

**描画内容:** Renders a Material3 AlertDialog whose confirmButton is a DialogConfirmButton(okLabel, enabled=addEnabled, onClick=onAdd), dismissButton is a DialogDismissButton(onClick=onClose), title is a DialogHeader(title, onClose), onDismissRequest=onClose, and whose text slot is a vertically-scrollable Column (Arrangement.spacedBy(10.dp)) hosting the caller-supplied `content()` lambda.


#### `NumField`

```kotlin
private fun NumField(label: String, value: String, modifier: Modifier = Modifier.width(150.dp), isError: Boolean = false, onChange: (String) -> Unit)
```

**描画内容:** Renders a single-line OutlinedTextField bound to `value`, with a `label` caption Text, a Number software-keyboard type (KeyboardOptions), the given `isError` state driving the field's built-in error coloring, and an onValueChange handler that filters every keystroke down to digit characters only (`it.filter { c -> c.isDigit() }`) before calling `onChange`.


#### `Picker`

```kotlin
private fun Picker(label: String, options: List<String>, selected: String, onSelect: (String) -> Unit)
```

**描画内容:** Renders a Column with a `label` caption Text above a Box containing an OutlinedButton (showing `selected`, or "(なし)" if `selected` is blank) that sets `open = true` on click, plus a DropdownMenu(expanded=open) anchored to that Box listing a DropdownMenuItem (monospace font, showing "(なし)" for a blank option) per entry in `options`; tapping an item calls `onSelect(opt)` and sets `open = false`.

**Compose固有ローカル状態:**

- open: Boolean = false (remember { mutableStateOf(false) })


### ダイアログ/オーバーレイ

- ConstraintDialog (rendered via the Shell/AlertDialog wrapper): the add-or-edit modal for a single constraint row of one family (cons1/cons2/cons41/cons42/cons41s/cons42s/cons3-family). Triggered from ConstraintsCard or SkillConstraintsCard by tapping a family's AddRowButton (sets addFamily=family key, opens in add mode) or tapping/EditRowButton-ing an existing ConstraintRow (sets editTarget=(family key, row index), opens in edit mode with fields prefilled from vm.constraintRowValues). Confirms via the Shell's DialogConfirmButton, which calls the family-specific vm.addConsX(...) (add mode) or vm.updateConstraint(family, editIndex, values) (edit mode) through the local `commit(values, add)` helper, then calls onClose(); the confirm button is disabled unless the family's required fields are non-blank and (for cons41/cons41s) the lower/upper bound values are not order-inverted. Cancels via the Shell's DialogDismissButton or the AlertDialog's onDismissRequest, both of which call onClose() and clear the triggering state without saving.
- Picker's DropdownMenu (per-Picker-instance popup, used inside every ConstraintDialog family branch and reused generically): an anchored dropdown list of selectable string options (shift/group/skill codes, plus for cons3 slots 2-5 a leading blank option rendered as '(なし)'), opened by tapping the OutlinedButton showing the current selection; tapping a DropdownMenuItem invokes onSelect(opt) and closes the menu (open = false). This is a lightweight, non-modal overlay layered above whatever composable hosts the Picker (an AlertDialog body in every call site in this file).
- ConstraintHelpExpander (not a true dialog/overlay, but a local expand/collapse disclosure toggled by tap): shown inline at the top of both ConstraintsCard's and SkillConstraintsCard's family list (only when there is at least one skill group, for the latter). Tapping its header Row toggles local `open` state between showing just the header label and showing the full per-family help text plus footer; it does not open any AlertDialog/popup and has no confirm/cancel semantics of its own.

### Android/Compose固有パターン（要変換方針）

- remember { mutableStateOf<String?>(null) } / mutableStateOf<Pair<String,Int>?>(null) as a dialog-trigger + payload combo: `addFamily`/`editTarget` in ConstraintsCard and SkillConstraintsCard: a single nullable field both signals 'is a dialog open' and carries the parameters needed to render it (family key alone for add, family+row-index for edit) — WinUI3 needs an equivalent pair of nullable fields (or a small dialog-request DTO) that both opens a ContentDialog and supplies its data.
- remember(family, editIndex) { ... } keyed remember: the `init` val in ConstraintDialog: recomputes vm.constraintRowValues(family, editIndex) only when `family` or `editIndex` change, not on every recomposition — in WinUI3 this must become an explicit re-fetch performed exactly when the dialog is (re)opened for a different family/row, never a field that can silently go stale across reopenings for the same instance.
- per-when-branch remember { mutableStateOf(...) } text field state with reused variable names across branches: d1/sk/d2 (cons1), sk/c (cons2), gk/sk/l/u (cons41 AND cons41s — same identifiers in two separate branch scopes), g1/s1/g2/s2 (cons42 AND cons42s — same identifiers in two separate branch scopes), a/b/c/d/e (cons3 family): each `when(family)` branch declares its own independent `remember` state even when it reuses a variable name from another branch — a rewrite must not accidentally treat sk/l/u/g1/s1/g2/s2 as shared fields across family types; they are distinct per-branch locals that happen to share names.
- @Composable ColumnScope.() -> Unit trailing content lambda parameter: Shell's `content` parameter, filled with a different set of NumField/Picker calls per when-branch inside ConstraintDialog: a scoped composable-content parameter used as a mini-DSL for injecting per-family form fields into shared dialog chrome — WinUI3 has no direct equivalent; requires either per-family XAML DataTemplates selected by family key, or a shared ContentDialog base/UserControl whose content Grid is populated per family.
- conditional composable invocation driven by nullable state: `if (fam != null) ConstraintDialog(...)` and `editTarget?.let { (k, i) -> ConstraintDialog(...) }` in both ConstraintsCard and SkillConstraintsCard: the dialog is only composed while its trigger state is non-null and vanishes from composition once `onClose` resets that state to null — the WinUI3 equivalent is explicitly showing/hiding (or constructing/tearing down) a ContentDialog gated on the same nullable state.
- side-effecting else-branch evaluated during composition: `else -> onClose()` at the end of the family `when` inside ConstraintDialog: invokes a state-mutating callback directly in the composable body (not inside a click handler or LaunchedEffect) — this runs as a plain side effect any time this branch is reached during (re)composition; in WinUI3 this should be an immediate guard (checked once when the dialog would otherwise be constructed for an unrecognized family key) that cancels/closes rather than a repeatable event.
- forEachIndexed-driven dynamic UI generation: `families.forEachIndexed { fi, fam -> ... }` and the nested `fam.rows.forEachIndexed { idx, row -> ConstraintRow(...) } }` in both ConstraintsCard and SkillConstraintsCard: builds a variable number of family sections and rows straight from ViewModel-supplied lists, capturing `idx` for edit/delete callbacks — maps to an ItemsControl/ItemsRepeater (or manual StackPanel population in code-behind) bound to the equivalent C# collections, with the index preserved for the edit/delete command parameters.
- Modifier.clickable(enabled = enabled) applied to a whole Row/Column as the primary tap target (not a Button): ConstraintRow's row-body Column (`.heightIn(min = 48.dp)`) and ConstraintHelpExpander's header Row: the entire row area (not just an icon/button) is the tap target for 'edit row' or 'toggle expand', with an explicit 48dp minimum touch height and an `enabled` gate — needs a Button-with-transparent-chrome, or a Grid/StackPanel with a Tapped handler plus equivalent minimum hit-area and IsEnabled-driven interaction.
- DropdownMenu/DropdownMenuItem anchored-popup selector: the Picker composable: an OutlinedButton showing the current selection opens a DropdownMenu of options (including a blank '' option shown as '(なし)'), each item rendered in FontFamily.Monospace — WinUI3 equivalent is a Button + Flyout(ListView) or a styled ComboBox; the blank string must be preserved as a legitimate, selectable option distinct from 'nothing selected'.
- AlertDialog wrapper (Shell) as the single confirm/dismiss chrome for every family: standard title/scrollable-content/PrimaryButton(confirm, gated by `addEnabled`)/SecondaryButton(dismiss) — direct analog to a WinUI3 ContentDialog with IsPrimaryButtonEnabled bound to the same validity flag and a ScrollViewer-wrapped content Grid for the (potentially tall, e.g. 5-Picker cons3) body.
- Kotlin default parameter values on public/private composable signatures: `title: String = "ルールの編集（勤務の並び・回数）"` and `keys: Set<String>? = null` on ConstraintsCard; `editIndex: Int? = null` on ConstraintDialog; `modifier: Modifier = Modifier.width(150.dp)` and `isError: Boolean = false` on NumField — must become optional parameters/constructor overloads or nullable-with-fallback logic in C#, since XAML/C# APIs don't support Kotlin-style default arguments the same way.
- digit-only free-text filtering keeps the field String-typed instead of Int-typed: NumField's `onChange(it.filter { c -> c.isDigit() })`: every numeric field's backing value is a plain String (not an Int) specifically so an empty string can represent 'unset' — the surrounding label text documents field-specific blank semantics ('空=0' for cons41/cons41s lower bound, '空=無制限' for their upper bound, '空=ここで終了' for cons3 slots 2-5) — a naive port to a NumberBox/int-typed property would collapse 'blank/unset' into 0 and lose meaning the engine treats differently.
- inline (non-remembered) derived validation flag recomputed every recomposition: `val bad = V6SanityPort.rangeOrderConflict(l, u) != null` in the cons41 and cons41s branches: gates the Shell's `addEnabled` (via `!bad`), drives `isError` on both the lower- and upper-bound NumField instances, and conditionally shows a RANGE_ORDER_HINT Text — in WinUI3 this becomes a live-recomputed validation property (raised on PropertyChanged of the two bound text fields) driving IsPrimaryButtonEnabled plus error styling on two TextBoxes and a conditionally-Visible hint TextBlock.
- family-keyed String dispatch as the entire control-flow backbone, with an intentional many-keys-to-one-branch grouping: `when (family) { "cons1" -> ...; "cons2" -> ...; "cons41" -> ...; "cons42" -> ...; "cons41s" -> ...; "cons42s" -> ...; "cons3", "cons3n", "cons3m", "cons3mn" -> ...; else -> onClose() }`: the four sequence-pattern keys deliberately share one UI body (only the dialog title text differs, via a nested `when (family) { "cons3n" -> "禁止の並び"; "cons3m" -> "推奨の並び"; "cons3mn" -> "回避の並び"; else -> "必須の並び" }`), while the other five families each get their own distinct branch — this grouping must be preserved as-is (e.g. as a switch with a fall-through case, or a family→ViewFactory map) rather than flattened into per-family duplicated UI, since it encodes which families are UI-identical vs. UI-distinct.
- list-index positional value contract for edit prefill and commit: `vm.constraintRowValues(family, editIndex)` returns a `List<String>?` whose element order is documented in-file ('値の並びは vm.constraintRowValues と同一（追加ダイアログの入力順）') as exactly matching the order fields are declared/read (`init?.getOrNull(0)`, `getOrNull(1)`, ...) per family, and the same ordered `List<String>` is passed back to `vm.updateConstraint(family, editIndex, values)` / the family-specific `vm.addConsX(...)` calls via the local `commit(values, add)` function — this is a load-bearing, type-system-invisible (plain `List<String>`, not a data class) positional contract that must be preserved index-for-index per family (cons1=[d1,sk,d2]; cons2=[sk,c]; cons41/cons41s=[gk,sk,l,u]; cons42/cons42s=[g1,s1,g2,s2]; cons3-family=[a,b,c,d,e]) when re-expressed as a C# record/array in the rewrite.

### 落とし穴

- The `else -> onClose()` branch of ConstraintDialog's family `when` is a side effect executed directly during composition (not inside a click handler or LaunchedEffect): if `family` is ever a value not in the recognized set, `onClose()` fires as part of just rendering that branch, and will fire again on every recomposition that reaches this branch until the parent's trigger state (addFamily/editTarget) is actually cleared. A straightforward WinUI3 port must not literally 're-run this on every UI refresh'; it should be a one-time guard evaluated when the dialog would be constructed/opened for an unrecognized key.
- Every per-family text-field `remember { mutableStateOf(...) }` inside ConstraintDialog lives inside a distinct branch of the same `when(family)` expression within a single composable function — Compose's branch-based slot table keeps these correctly isolated per family even though many branches reuse the SAME variable names (sk, l, u, g1, s1, g2, s2) for semantically DIFFERENT fields (e.g. cons41's `gk` is a group code, cons41s's `gk` — same name, separate branch — is a skill code). When translating, do not merge these into shared ViewModel/code-behind fields; each family effectively needs its own isolated set of bound properties even where names collide.
- The local nested function `commit(values: List<String>, add: () -> Unit)` (declared inside ConstraintDialog, not a top-level helper) is the single dispatch point for BOTH add and edit across all seven family branches: if `editIndex != null` it calls `vm.updateConstraint(family, editIndex, values)`, otherwise it invokes the caller-supplied `add` lambda (which itself calls the specific `vm.addConsX(...)`); in both cases it then calls `onClose()`. Every branch's Shell(onAdd = { commit(listOf(...)) { vm.addConsX(...) } }) relies on this exact wrapping — losing it would require duplicating the add/edit branching seven times.
- The `List<String>` value order returned by `vm.constraintRowValues(family, editIndex)` and consumed by `init?.getOrNull(N)` (and mirrored back out via `commit(listOf(...))`) is an UNTYPED positional contract, not enforced by the compiler: cons1=[d1,sk,d2] ('何日間','シフト','必要数'); cons2=[sk,c] ('シフト','合計'); cons41/cons41s=[gk,sk,l,u] ('グループ/スキル','シフト','下限','上限'); cons42/cons42s=[g1,s1,g2,s2] ('グループ/スキル1','シフト1','グループ/スキル2','シフト2'); cons3-family=[a,b,c,d,e] (up to 5 sequential shift picks, blank-terminated after the first). Getting this order wrong in the C# rewrite would silently swap which UI field maps to which stored value with no compile-time or obvious runtime error.
- `RANGE_ORDER_HINT` external constant and the `val bad = V6SanityPort.rangeOrderConflict(l, u) != null` check appear identically in BOTH the cons41 and cons41s branches (duplicated inline, not factored into a shared local function) — the comment at line 274 explicitly notes this ('cons41 と同じ（群かスキル群かの違いだけ）'), confirming the duplication is intentional/accepted rather than an oversight to silently dedupe away without checking both call sites still validate correctly.
- NumField's digit-only filter (`it.filter { c -> c.isDigit() }`) strips ALL non-digit characters including a leading minus sign — none of the numeric fields in this file support negative values; blank string is the only 'unset' representation, and each field's adjacent label documents what blank means for THAT specific field ('空=0' vs '空=無制限' vs '空=ここで終了' vs required-and-blank-disables-confirm for the 1st slot/d1/sk/d2/sk/c/gk fields). These per-field blank semantics are NOT uniform and must be re-derived from each field's label/validation logic individually, not assumed to all mean the same thing.
- Picker's `options` list can legitimately contain an empty string `""` as a real, selectable entry (via `shiftsOpt = listOf("") + shifts`, used only for cons3 slots 2-5, i.e. `b`,`c`,`d`,`e`); both the trigger OutlinedButton's label and the corresponding DropdownMenuItem render `""` as the display text "(なし)". This must not be confused with 'no option selected' — a WinUI3 ComboBox/ListView SelectedItem of empty-string is a valid, meaningful state here (it means 'stop the sequence at this slot').
- `ConstraintsCard`'s `keys: Set<String>? = null` parameter filters the family list from `vm.constraintFamilies()` down to a subset when non-null (`all.filter { it.key in keys }`), per the KDoc comment about co-locating group-level C41/C42 into a dedicated section elsewhere in the app for discoverability — this same composable is evidently reused with different `keys` values from other files (not read here), so its default (unfiltered, `keys = null`) and filtered behaviors must both be preserved as distinct call modes.
- `ConstraintsCard`'s `title: String` default is a non-blank Japanese heading, but passing an explicit blank string (`title = ""`) suppresses the title Text entirely via `if (title.isNotBlank())` — this is a deliberate 'headless embed' mode (used, per the `keys`-filtering comment, when this card is placed under another screen's own heading) and must be preserved as an explicit blank-vs-default distinction, not treated as 'always show a title'.
- `ConstraintsCard` and `SkillConstraintsCard` independently re-implement nearly identical family-section rendering (title Text, rows-or-'(なし)' placeholder, AddRowButton, Divider) with their own separate `addFamily`/`editTarget` state and their own separate calls to `ConstraintDialog`/`vm.removeConstraint` — they are NOT sharing a common sub-composable for this loop body in the current source (each has its own literal `families.forEachIndexed { ... }` block). `SkillConstraintsCard` additionally gates the entire families section behind `vm.skillGroupKigouList().isEmpty()`, showing a totally different single-message layout when no skill groups exist yet — this early-return-style branch happens INSIDE the Card's Column, not as a separate composable.
- `ConstraintRow`'s KDoc explicitly documents that a former per-row secondary explanatory text line ('sub', from a prior version referenced as 3.409.18) was intentionally removed in favor of consolidating all explanatory content into the shared `ConstraintHelpExpander` — when rebuilding, do not reintroduce a second line of text under each row; the row is single-line-of-content (plus the label chrome) by design.
- None of the public composables (`ConstraintsCard`, `SkillConstraintsCard`) accept an incoming `Modifier` parameter — they hard-code `Modifier.fillMaxWidth()` on their root `Card` internally, which is atypical for reusable Compose components and means external layout customization (e.g. padding, weight) is not currently possible from call sites; note this constraint if the C#/XAML equivalents are meant to be more flexibly composable controls.
- The `V6SanityPort` import (line 25) is interleaved alphabetically-out-of-order among the `androidx.compose.material3.*` imports rather than grouped with other non-androidx imports — cosmetic only, but indicates the import block is not strictly organized/sorted, so don't assume import ordering carries any semantic grouping meaning when tracing dependencies.
- `Shell`'s AlertDialog `text` slot content is wrapped in `Modifier.verticalScroll(rememberScrollState())`, meaning every family's dialog body (including the 5-Picker cons3 form) is scrollable by design — a WinUI3 ContentDialog port must wrap its content Grid/StackPanel in a ScrollViewer to preserve this for taller family forms on small screens.

### 外部依存

- `UiState (data class field read: ui.running)`
- `MagiViewModel (calls: constraintFamilies(), skillConstraintFamilies(), skillGroupKigouList(), removeConstraint(family: String, index: Int), shiftKigouList(), groupKigouList(), constraintRowValues(family: String, index: Int), updateConstraint(family: String, index: Int, values: List<String>), addCons1(d1, sk, d2), addCons2(sk, c), addCons41(gk, sk, l, u), addCons42(g1, g2, s1, s2), addCons41s(gk, sk, l, u), addCons42s(g1, g2, s1, s2), addCons3(family, values: List<String>))`
- `MagiViewModel.ConstraintFamilyView (nested type consumed via fields: .key: String, .title: String, .rows: List<String>)`
- `com.magi.app.v6.V6SanityPort (explicit import; call: V6SanityPort.rangeOrderConflict(lo: String, hi: String) used for cons41/cons41s lower>upper validation)`
- `constraintHelp (external Map<String, String> of family-key -> help body text, referenced but not imported/declared in this file — per project history, defined in ConstraintHelp.kt)`
- `CONSTRAINT_HELP_FOOTER (external String constant, referenced but not declared in this file — per project history, defined in ConstraintHelp.kt)`
- `RANGE_ORDER_HINT (external String constant, referenced but not declared in this file — per project history, a shared hint string defined alongside other cross-screen constants, likely in Affordance.kt)`
- `AddRowButton (external composable from Affordance.kt, invoked with (label: String, onClick, enabled))`
- `EditRowButton (external composable from Affordance.kt, invoked with (onClick, enabled))`
- `DeleteRowButton (external composable from Affordance.kt, invoked with (onClick, enabled))`
- `DialogConfirmButton (external composable from Affordance.kt, invoked with (label, enabled, onClick) inside Shell's confirmButton slot)`
- `DialogDismissButton (external composable from Affordance.kt, invoked with (onClick) inside Shell's dismissButton slot)`
- `DialogHeader (external composable from Affordance.kt, invoked with (title, onClose) inside Shell's title slot)`


---

## NeedDayEditor.kt

### 役割

NeedDayEditor.kt implements the daily staffing-need (必要人数) calendar editor used in the monthly-conditions editing scope. Its main entry point, NeedCalendarCard, lets the shift creator pick a shift via a dropdown, view/select one-or-more days on a Sunday-first month calendar (each day showing its effective min–max staffing need), and either bulk-apply or bulk-reset the need inline for the selected days, or edit the shift's own base need1/need2 via a bottom sheet. A secondary card, NeedDayCard, offers a flat cross-shift list of all day-level overrides for confirmation/deletion. The file also defines small shared layout primitives (MonthHeaderStatic, SelectorField, CountPill, NumberStepper) that are reused by the sibling wish-shift calendar editor.

### Composable関数

#### `MonthHeaderStatic`

```kotlin
internal fun MonthHeaderStatic(startDate: String)
```

**描画内容:** Renders a centered, non-interactive month header row: a dimmed '‹' arrow, the bold primary-colored month label (e.g. '2025年6月'), and a dimmed '›' arrow. The arrows are purely decorative (no month paging, per business decision D6: one app-state = one month). Renders nothing at all if the computed label is blank (date parse failure).


#### `SelectorField`

```kotlin
internal fun SelectorField(label: String, value: String, onClick: () -> Unit)
```

**描画内容:** Renders a bordered, clickable Column styled as a dropdown-field anchor: a small label above, and a Row below showing the current value plus a '▼' glyph. Tapping invokes onClick; the caller is expected to open a DropdownMenu as a sibling composable (this component has no menu logic of its own).


#### `CountPill`

```kotlin
internal fun CountPill(text: String)
```

**描画内容:** Renders a fully-rounded pill Surface with a primary-color background containing the given text in onPrimary color, used to show a selection count such as 'N日選択中'.


#### `NeedCalendarCard`

```kotlin
fun NeedCalendarCard(ui: UiState, vm: MagiViewModel, initialShift: Int? = null, onInitialConsumed: () -> Unit = {})
```

**描画内容:** The main daily-staffing-need calendar card: a titled Card containing a shift-symbol dropdown selector plus the shift's 'standard' (base need1/need2) label, a static month header, the NeedMonthGrid calendar for the currently selected shift, and — only when one or more days are selected — an inline NeedApplyPanel for bulk-editing those days. Tapping the standard-value text opens a BaseNeedSheet bottom sheet to edit the shift's base need1/need2. Supports a deep-link initialShift parameter that pre-selects a shift and is consumed once via LaunchedEffect (calling onInitialConsumed). Returns early (renders nothing) if vm.ws1() is null or has no shifts.

**ViewModel呼出:**

- `vm.ws1()`
- `vm.needCellLimits(k, j)`
- `vm.needDayOverrides()`
- `vm.setShiftNeed(k, p1, p2)`

**読むUiStateフィールド:**

- `ui.startDate`
- `ui.days`
- `ui.running`

**Compose固有ローカル状態:**

- k: Int = remember { mutableStateOf(initialShift?.takeIf { it in v.shifts.indices } ?: 0) } — selected shift index; also unconditionally clamped to 0 in the composable body if it falls out of v.shifts.indices
- daysSel: Set<Int> = remember(k) { mutableStateOf(emptySet<Int>()) } — selected 1-based day numbers for the current shift; automatically resets to empty whenever k (the selected shift) changes
- shiftMenu: Boolean = remember { mutableStateOf(false) } — whether the shift-symbol DropdownMenu is expanded
- baseSheet: Boolean = remember { mutableStateOf(false) } — whether the BaseNeedSheet ModalBottomSheet is shown


#### `NeedApplyPanel`

```kotlin
private fun NeedApplyPanel(ui: UiState, vm: MagiViewModel, k: Int, days: Set<Int>, baseN1: String, baseN2: String, onCancel: () -> Unit, onDone: () -> Unit)
```

**描画内容:** An inline (non-modal) panel shown below the calendar when ≥1 day is selected, for bulk-setting min/max staffing on those days: a CountPill plus a (possibly-truncated, e.g. '6/3、6/8、6/17、ほか2日') list of selected dates, two NumberStepper rows (最低人数/上限人数) inside a Column whose border turns error-red when min>max, a Cancel/Apply Button row, and a full-width '選択した日を未設定に戻す' TextButton that deletes the day-level overrides for all selected days.

**ViewModel呼出:**

- `vm.setNeedDay(k, d - 1, p1, p2)`
- `vm.removeNeedDay(k, d - 1)`

**読むUiStateフィールド:**

- `ui.startDate`
- `ui.running`

**Compose固有ローカル状態:**

- p1: String = remember(k) { mutableStateOf(baseN1) } — editable '最低人数' text value, reseeded from the shift's base need1 whenever k changes
- p2: String = remember(k) { mutableStateOf(baseN2) } — editable '上限人数' text value, reseeded from the shift's base need2 whenever k changes


#### `BaseNeedSheet`

```kotlin
private fun BaseNeedSheet(kigou: String, need1: String, need2: String, running: Boolean, onApply: (String, String) -> Unit, onDismiss: () -> Unit)
```

**描画内容:** A ModalBottomSheet titled '基本の必要人数（${kigou}の既定値）' containing two NumberStepper rows (最低人数/上限人数, blank label '未設定') inside a border that turns error-red on a min>max conflict, an error hint text when invalid, and a full-width 'Save' Button that calls onApply(p1, p2) then onDismiss(); disabled while running=true or invalid.

**Compose固有ローカル状態:**

- sheetState = rememberModalBottomSheetState() — Material3 bottom-sheet show/hide/drag state
- p1: String = remember { mutableStateOf(need1) } — editable '最低人数' text value, seeded from the passed-in need1 parameter
- p2: String = remember { mutableStateOf(need2) } — editable '上限人数' text value, seeded from the passed-in need2 parameter


#### `NeedMonthGrid`

```kotlin
private fun NeedMonthGrid(startDate: String, ranges: List<Pair<Int, Int>?>, individualDays: Set<Int>, selectedDays: Set<Int>, onToggle: (Int) -> Unit)
```

**描画内容:** Renders the tappable calendar grid for the currently selected shift: a Sunday-first weekday header row (Sunday text colored MagiAccent.red, Saturday colored MagiAccent.blue), followed by week rows (chunked by 7) of day cells padded with blank spacer Boxes before day 1 and after the last day. Each real day cell shows the day number, an effective need-range label ('lo–hi', a single number when lo==hi, or '—' when unset), a Check icon when selected or a small colored dot when individually overridden (and neither icon/dot when plain-default), and toggles selection on tap via onToggle(day+1); cell background/border/text colors switch to the primaryContainer palette when selected.


#### `NeedDayCard`

```kotlin
fun NeedDayCard(ui: UiState, vm: MagiViewModel)
```

**描画内容:** A read-mostly card titled '日別の必要人数（例外）一覧' listing every day-level need override across ALL shifts (a cross-shift complement to the per-shift NeedCalendarCard), each row showing '{symbol}  {day+1}日  最低 {p1|-}人 / 上限 {p2|-}人' plus a DeleteRowButton to remove that single override; shows a '（例外なし — すべて既定値）' placeholder text when the override list is empty.

**ViewModel呼出:**

- `vm.needDayOverrides()`
- `vm.removeNeedDay(o.k, o.j)`

**読むUiStateフィールド:**

- `ui.running`


#### `NumberStepper`

```kotlin
internal fun NumberStepper(label: String, value: String, onChange: (String) -> Unit, min: Int, blankLabel: String)
```

**描画内容:** A shared one-thumb-friendly numeric stepper Row: a label, a large '−' OutlinedButton, a centered 72dp-wide display of either the current value or blankLabel when value is blank, and a large '＋' OutlinedButton. Shared with the sibling StaffRange editor in the same package (per source comment). Not remembered state — the displayed number (n = value.toIntOrNull()) is recomputed each recomposition from the passed-in value; all mutation flows out through onChange.


### ヘルパー関数

- `monthLabel`（`internal fun monthLabel(startDate: String): String`）: Parses an ISO-8601 start-date string (java.time.LocalDate.parse) into a Japanese 'YYYY年M月' label for the static month header; returns empty string on ANY parse exception, which causes MonthHeaderStatic to render nothing at all.
- `dayChipLabel`（`internal fun dayChipLabel(startDate: String, day1: Int): String`）: Given the period's ISO start date and a 1-based day-of-period number, computes the calendar date (startDate + (day1-1) days) and formats it as 'M/D(曜)' using a Monday-first Japanese weekday abbreviation array (月火水木金土日, indexed by ISO DayOfWeek.value-1); correctly spans month boundaries. Falls back to '${day1}日' on any parse exception. Used for both single-day chips and the truncated multi-day summary label in NeedApplyPanel.

### ダイアログ/オーバーレイ

- DropdownMenu (shift-symbol selector) in NeedCalendarCard: triggered by tapping the shift-symbol Surface (sets shiftMenu=true); iterates v.shifts, each DropdownMenuItem on click sets k=idx, resets daysSel to emptySet(), and closes the menu (shiftMenu=false); onDismissRequest also just sets shiftMenu=false (no distinct 'cancel' vs 'confirm' — every item click both selects and closes).
- ModalBottomSheet (BaseNeedSheet) in NeedCalendarCard: triggered by tapping the '標準 $baseLabel' text (sets baseSheet=true); confirms via the full-width 'Save/保存' Button which calls onApply(p1, p2) → vm.setShiftNeed(k, p1, p2), then onDismiss() (closes sheet); cancels via onDismissRequest=onDismiss (scrim tap / swipe-down gesture), applying nothing; Save is disabled while running=true or while p1/p2 fail V6SanityPort.rangeOrderConflict validation (shown as a red border + hint text instead of blocking silently).

### Android/Compose固有パターン（要変換方針）

- remember(k) { mutableStateOf(emptySet<Int>()) }: NeedCalendarCard.daysSel: Compose's keyed remember() automatically clears the selected-days set whenever the shift index k changes; WinUI3/x:Bind has no automatic 'reset local state on key change' — must explicitly clear the SelectedDays collection whenever SelectedShiftIndex changes (e.g. in the property setter or a command handler).
- remember(k) { mutableStateOf(baseN1/baseN2) }: NeedApplyPanel.p1/p2: same keyed-reset idiom for the min/max text fields. Note that since this panel only renders while daysSel is non-empty, and daysSel itself resets to empty whenever k changes, in practice the panel actually unmounts/remounts on shift change rather than re-keying while visible — preserve this 'panel disappears on shift switch' behavior rather than trying to keep it open with reseeded values.
- LaunchedEffect(initialShift) { ... }: NeedCalendarCard: a one-shot side effect keyed on the initialShift parameter — on change it writes k and invokes onInitialConsumed() so the caller can clear the deep-link value upstream ('consume navigation parameter once'). In WinUI3 this needs an explicit value-changed handler (e.g. a DependencyProperty PropertyChangedCallback or ViewModel-side one-time consumption) rather than relying on recomposition-triggered effects.
- Unconditional state-correction in the composable body (`if (k !in v.shifts.indices) k = 0`): NeedCalendarCard: mutates state directly during composition (idempotent bounds-clamp, not wrapped in LaunchedEffect/derivedStateOf) — a Compose idiom for keeping local state consistent with a possibly-shrinking backing list. In WinUI3 this must be re-triggered explicitly whenever the Shifts collection changes (e.g. via CollectionChanged) rather than 'on every render'.
- rememberModalBottomSheetState() + ModalBottomSheet (@OptIn(ExperimentalMaterial3Api::class) on both NeedCalendarCard and BaseNeedSheet): BaseNeedSheet: Material3's bottom-sheet component with its own show/hide animation and swipe-to-dismiss state machine; no direct WinUI3 equivalent — typically mapped to a ContentDialog, Flyout, or custom bottom-sheet UserControl, with the sheet's implicit dismiss gestures (scrim tap, swipe-down) reimplemented explicitly.
- DropdownMenu anchored inside a Box via expanded/onDismissRequest boolean state (NeedCalendarCard.shiftMenu): Compose's DropdownMenu is a popup positioned relative to its parent; map to a MenuFlyout/Flyout attached to the anchor element, with the bound bool both opening the flyout and being reset by the flyout's Closed event.
- Modifier.semantics { contentDescription = ... } with dynamically interpolated strings: NeedMonthGrid day cells (selection/individual/range state baked into the description string) and NumberStepper's +/− buttons (label baked into 'を減らす'/'を増やす'): Compose colocates the accessibility text with the visual declaration and it recomposes with state; WinUI3 equivalent is AutomationProperties.Name, usually requiring an x:Bind converter or code-behind update rather than inline declaration.
- Icons.Filled.Check (androidx.compose.material.icons): NeedMonthGrid selected-day indicator — needs a WinUI3 equivalent such as SymbolIcon(Symbol.Accept) or a FontIcon glyph.
- CircleShape dot indicator (Box(Modifier.size(5.dp).background(cs.primary, CircleShape))): NeedMonthGrid's 'individually overridden day' marker — a tiny 5dp filled circle; map to an Ellipse of equivalent size bound to the same boolean.
- MaterialTheme.colorScheme tokens (cs.primary, cs.primaryContainer, cs.onPrimaryContainer, cs.outline, cs.outlineVariant, cs.surface, cs.surfaceVariant, cs.onSurface, cs.onSurfaceVariant, cs.error, cs.onPrimary): used throughout for borders/backgrounds/text that change with selection/validity state; must be remapped to the app's WinUI3 theme-resource brushes with equivalent semantic roles (including error/invalid state).

### 落とし穴

- Two different week-start conventions coexist: dayChipLabel's weekday array is Monday-first (月火水木金土日, indexed via ISO DayOfWeek.value-1), while NeedMonthGrid's calendar grid is Sunday-first (weekJa = 日月火水木金土, with sdow = (startDowMonFirst(startDate)+1) % 7 converting the Monday-first offset to Sunday-first). Getting this +1 %7 conversion wrong silently misaligns the calendar's leading blank cells relative to the actual day-of-week.
- NeedApplyPanel's baseN1/baseN2 parameters (NeedCalendarCard passes shift.need1/shift.need2, i.e. the SHIFT'S base/default need) are NOT any per-day override that might already exist on the selected days — opening/showing the panel always seeds the min/max fields from the shift default, discarding any prior per-day override values for display purposes (the fields are purely an input buffer that overwrites whatever is submitted via setNeedDay).
- '選択した日を未設定に戻す' in NeedApplyPanel calls vm.removeNeedDay(k, d-1) for every selected day (deleting the day-level override entirely, reverting to the shift's base need) — this is a DISTINCT action from Apply/setNeedDay, and unlike the Apply button it is gated ONLY by !ui.running (not by the blank/invalid checks that gate Apply).
- 0-based vs 1-based day indexing crosses several boundaries and must be preserved exactly: `ranges` is built 0-based via vm.needCellLimits(k, j) for j in 0 until ui.days; NeedMonthGrid receives this 0-based list but calls onToggle(j+1) — so daysSel stores 1-based day numbers (matching dayChipLabel's day1 param); NeedApplyPanel then converts back to 0-based when calling vm.setNeedDay(k, d-1, ...) / vm.removeNeedDay(k, d-1).
- NumberStepper's '−' button is a 3-way state machine, not a simple decrement: value==null(blank) → min; value==min → '' (blank, i.e. 'no constraint'/cleared); otherwise → n-1. Decrementing FROM the minimum clears the field to represent an unset/unbounded value rather than refusing to go below min — a deliberate design choice easy to lose in a naive port.
- NumberStepper's '＋' button treats a blank value as (min-1) before incrementing and clamping to min — i.e. pressing '+' on a blank field jumps straight to exactly `min` (not min+1); verify this off-by-one precisely when porting.
- The `if (daysSel.isNotEmpty()) {...} else { }` branch in NeedCalendarCard has an empty else block — dead code / no-op, not a meaningful 'nothing selected' state to translate; there is currently no UI shown when zero days are selected beyond the calendar itself.
- baseLabel's derivation from shift.need1/need2 (both String, possibly non-numeric/blank) follows a strict 4-branch precedence that must be preserved verbatim: (1) both null → '未設定'; (2) n2 is null OR n2==n1 → '${n1 ?: n2}人'; (3) n1 is null (n2 non-null and n1 null) → '${n2}人'; (4) else → '$n1–${n2}人'. This is NOT simply 'show a range or a single number'.
- individualDays is recomputed every recomposition by filtering vm.needDayOverrides() for entries whose .k == the CURRENTLY selected shift k, then mapping to .j — it is not any per-shift cached collection; the equivalent WinUI3 binding should reactively re-filter the overrides collection by the currently selected shift index whenever either changes.
- V6SanityPort.rangeOrderConflict is explicitly used as the SOLE min>max validation source in both NeedApplyPanel and BaseNeedSheet (per an in-code comment citing a prior bug where hand-duplicated validation logic drifted out of sync across the group-range/skill-group/staff-range editors, versions 3.403.0/3.352.0) — do not reimplement this comparison locally in the C# port; mirror it from wherever the V6SanityPort equivalent lives in the ported business layer.
- NeedCalendarCard renders nothing at all (early return, no Card) if vm.ws1() returns null OR v.shifts is empty — both are 'hide the entire card' guards, not 'show an empty-state UI' guards.
- BaseNeedSheet takes NO UiState/ViewModel reference — the caller (NeedCalendarCard) passes running=ui.running as a plain Boolean and onApply/onDismiss as callbacks. BaseNeedSheet is a fully decoupled, purely presentational component; preserve this so the WinUI3 equivalent has no direct dependency on the app-wide view model.
- Deep-link consumption of initialShift requires BOTH: (a) using it to seed k's INITIAL remember() value at first composition, AND (b) a LaunchedEffect(initialShift) that re-applies it and calls onInitialConsumed() on every later change. (a) alone misses a deep-link value arriving/changing after first composition; (b) alone misses the very-first-composition case before any recomposition occurs.
- Date parsing (java.time.LocalDate.parse, assumed ISO-8601 'yyyy-MM-dd') in both monthLabel and dayChipLabel silently swallows ANY exception with a fail-soft fallback (monthLabel → '' which makes MonthHeaderStatic render nothing; dayChipLabel → '${day1}日' as a degraded label) — never throws, never shows an error UI; preserve this exact fail-soft behavior in the C# DateOnly/DateTime port rather than surfacing a parse exception.

### 外部依存

- `UiState (ui: UiState parameter) — exposes ui.startDate, ui.days, ui.running consumed in this file`
- `MagiViewModel (vm: MagiViewModel parameter) — exposes vm.ws1(), vm.needCellLimits(shiftIdx, dayIdx), vm.needDayOverrides(), vm.setShiftNeed(shiftIdx, need1, need2), vm.setNeedDay(shiftIdx, dayIdx, min, max), vm.removeNeedDay(shiftIdx, dayIdx)`
- `V6SanityPort.rangeOrderConflict(lo, hi) (com.magi.app.v6, explicit import) — single-source min>max validation shared with the group-range/skill-group/staff-range editors; used by NeedApplyPanel and BaseNeedSheet`
- `MagiTokens.MagiAccent — design-token object; MagiAccent.red / MagiAccent.blue used for Sunday/Saturday weekday header text color in NeedMonthGrid`
- `Affordance.NEED_ORDER_HINT — shared error-hint string constant (per project notes, defined in Affordance.kt) shown when min>max in NeedApplyPanel and BaseNeedSheet`
- `DeleteRowButton(onClick, enabled) — shared row-delete icon-button composable, defined elsewhere in the ui package, used in NeedDayCard`
- `startDowMonFirst(startDate: String): Int — shared top-level helper function (defined outside this file, alongside the sibling wish-shift calendar editor) returning the Monday-first day-of-week offset of the period start; converted to Sunday-first via (startDowMonFirst(startDate)+1)%7 in NeedMonthGrid`
- `vm.ws1() return type — exposes .shifts: List<Shift-like> where each entry has .kigou (String symbol), .need1 (String), .need2 (String)`
- `vm.needDayOverrides() return type — a list of override entries exposing .k (Int shift index), .j (Int 0-based day index), .kigou (String), .p1 (String), .p2 (String)`


---

## StaffRangeEditor.kt

### 役割

Implements the '③ 回数（1人あたり）' (per-person monthly shift-count) editing UI inside the yearly master-data edit scope of the MAGI shift-optimizer app. Hosts CountsCard, a single unified Material3 Card (introduced 3.466.0, replacing three previously-separate cards) that stacks AptSection (defined elsewhere, not in this file), StaffRangeSection (per-staff lo/hi range editor), and GroupRangeSection (bulk group-wide range setter that fans out into the same per-staff model). All edits write through to the existing `staffRange["i,k"] = Range(lo,hi)` domain model and existing scoring engine via MagiViewModel calls — this file is purely a UI/editing layer with no new constraint types or scoring logic of its own.

### Composable関数

#### `CountsCard`

```kotlin
@Composable fun CountsCard(ui: UiState, vm: MagiViewModel)
```

**描画内容:** A single Material3 Card containing a Column: an intro Text explaining 目標(soft target, near a value) vs 下限/上限(hard limit, always enforced), then AptSection(ui, vm), a Divider, StaffRangeSection(ui, vm), a Divider, and GroupRangeSection(ui, vm) — all stacked with 8dp spacing. It is the sole entry point/container tying the three count-editing sub-sections together.


#### `StaffRangeSection`

```kotlin
@OptIn(ExperimentalLayoutApi::class) @Composable internal fun StaffRangeSection(ui: UiState, vm: MagiViewModel)
```

**描画内容:** A Column with a bold '下限・上限（かたい）' heading, a legend Text, and — if vm.staffCountRules() is non-empty — one block per distinct staff (bold name label + a FlowRow of InputChips, one chip per shift row showing kigou, an optional '{lo}–{hi}' / '≤{hi}' / '≥{lo}' range label, an optional '目標{aptRaw}→{aptEff}' or '目標{aptEff}' target label, and '・今{now}' current-count, chip background tinted red/orange when that cell violates low/aptLow or high/aptHigh). Tapping a chip opens StaffRangeDialog pre-filled with that (staff,shift); chips whose row has an explicit range also show a 32dp '×' Icon that calls vm.removeStaffRange immediately (no confirmation). If staffCountRules() is empty, shows a '（設定なし）' placeholder instead. Ends with an AddRowButton('上下限を追加') that opens a blank StaffRangeDialog; the dialog itself is conditionally composed via `dialog?.let { ... }` when the local `dialog` state is non-null.

**ViewModel呼出:**

- `vm.staffCountRules()`
- `vm.removeStaffRange(r.i, r.k)`
- `vm.shiftKigouList()`
- `vm.allowedShiftsFor(idx)`
- `vm.setStaffRange(i, k, lo, hi)`

**読むUiStateフィールド:**

- `ui.schedule`
- `ui.countViolations`
- `ui.violationColorHex`
- `ui.violationSoftColorHex`
- `ui.running`
- `ui.loaded`
- `ui.staffNames`

**Compose固有ローカル状態:**

- var dialog by remember { mutableStateOf<StaffRangeEdit?>(null) } — type StaffRangeEdit?, initial null; drives whether StaffRangeDialog is composed and, when non-null, what it is pre-filled with.


#### `StaffRangeDialog`

```kotlin
@Composable internal fun StaffRangeDialog(init: StaffRangeEdit, staff: List<String>, shifts: List<String>, allowedFor: (Int) -> Set<Int>, onApply: (Int, Int, String, String) -> Unit, onClose: () -> Unit)
```

**描画内容:** An AlertDialog titled '個人別の回数' (via DialogHeader) with: a staff-choice OutlinedButton+DropdownMenu, a shift-choice OutlinedButton+DropdownMenu whose items are filtered to `idx in allowedFor(i)`, then a Column (bordered in error color when `bad`) holding two NumberStepper rows for 下限/上限 (min=0, blankLabel='なし'), and a conditional hint Text — RANGE_ORDER_HINT in error color if `bad`, else RANGE_REQUIRED_HINT in muted color if both lo and hi are blank. confirmButton is DialogConfirmButton('適用', enabled=ok) calling onApply(i,k,lo.trim(),hi.trim()); dismissButton is DialogDismissButton calling onClose.

**Compose固有ローカル状態:**

- var i by remember { mutableStateOf(init.i) } — Int, initial = init.i (selected staff index)
- var k by remember { mutableStateOf(init.k) } — Int, initial = init.k (selected shift index)
- var lo by remember { mutableStateOf(init.lo) } — String, initial = init.lo (blank = no lower limit)
- var hi by remember { mutableStateOf(init.hi) } — String, initial = init.hi (blank = no upper limit)
- var openS by remember { mutableStateOf(false) } — Boolean, initial false; staff DropdownMenu expanded flag
- var openK by remember { mutableStateOf(false) } — Boolean, initial false; shift DropdownMenu expanded flag


#### `GroupRangeSection`

```kotlin
@OptIn(ExperimentalLayoutApi::class) @Composable internal fun GroupRangeSection(ui: UiState, vm: MagiViewModel)
```

**描画内容:** A Column with a bold 'グループ一括設定' heading, a description Text, and — if vm.groupRangeSummary() is non-empty — a bold count line ('適用中のグループ上下限（N件・個人の回数にも展開済み）') plus a FlowRow of InputChips, one per applied group-range summary row, each labeled '{groupName}·{halfwidth kigou} {range label}（{count}名）' where count is either just members or 'shared/members' depending on whether shared>=members; each chip shows a 32dp '×' Icon calling vm.clearGroupRange immediately (no confirmation). Ends with an AddRowButton('グループに上下限を適用') that opens GroupRangeDialog, composed conditionally via `if (dialog) { ... }`.

**ViewModel呼出:**

- `vm.groupRangeSummary()`
- `vm.clearGroupRange(gr.g, gr.k, gr.lo, gr.hi)`
- `vm.groupLabels()`
- `vm.shiftKigouList()`
- `vm.allowedShiftsForGroup(g)`
- `vm.groupMemberCount(g)`
- `vm.setGroupRange(g, k, lo, hi)`

**読むUiStateフィールド:**

- `ui.running`
- `ui.loaded`

**Compose固有ローカル状態:**

- var dialog by remember { mutableStateOf(false) } — Boolean, initial false; controls whether GroupRangeDialog is composed. NOTE: this is a plain Boolean, not a nullable data-holder — tapping either the add-button or an existing chip both just set it true, so the dialog always opens BLANK regardless of trigger (no pre-fill path exists for editing an existing group range).


#### `GroupRangeDialog`

```kotlin
@Composable internal fun GroupRangeDialog(groups: List<String>, shifts: List<String>, allowedFor: (Int) -> Set<Int>, memberCount: (Int) -> Int, onApply: (Int, Int, String, String) -> Unit, onClose: () -> Unit)
```

**描画内容:** An AlertDialog titled 'グループ単位の回数' (via DialogHeader) with: a group-choice OutlinedButton+DropdownMenu (button label and each item show '{group}（{memberCount}名）'), a shift-choice OutlinedButton+DropdownMenu labeled 'シフト（全員が担当可のもの）' filtered to `idx in allowed` where `allowed = allowedFor(g)`, then a Column (bordered in error color when `bad`) holding two NumberStepper rows for 下限/上限, a conditional hint Text (RANGE_ORDER_HINT / RANGE_REQUIRED_HINT, same pattern as StaffRangeDialog), and a trailing labelSmall footer Text explaining overwrite semantics ('全員の個人上下限に設定し、下限=上限なら適切回数も同時に設定します（既存の個人設定は上書き）。'). confirmButton is DialogConfirmButton('適用', enabled=ok) calling onApply(g,k,lo.trim(),hi.trim()); dismissButton is DialogDismissButton calling onClose.

**Compose固有ローカル状態:**

- var g by remember { mutableStateOf(0) } — Int, initial 0 (selected group index)
- var k by remember { mutableStateOf(0) } — Int, initial 0 (selected shift index)
- var lo by remember { mutableStateOf("") } — String, initial empty
- var hi by remember { mutableStateOf("") } — String, initial empty
- var openG by remember { mutableStateOf(false) } — Boolean, initial false; group DropdownMenu expanded flag
- var openK by remember { mutableStateOf(false) } — Boolean, initial false; shift DropdownMenu expanded flag


### ダイアログ/オーバーレイ

- StaffRangeDialog (AlertDialog, title '個人別の回数'): triggered either by the AddRowButton in StaffRangeSection (opens blank: StaffRangeEdit(0,0,"","")) or by tapping any per-staff InputChip (opens pre-filled: StaffRangeEdit(r.i, r.k, r.lo, r.hi)). Confirms via DialogConfirmButton('適用', enabled = ok) → onApply(i,k,lo.trim(),hi.trim()) → vm.setStaffRange(...) then dialog=null. Cancels via DialogDismissButton or onDismissRequest → onClose() → dialog=null.
- GroupRangeDialog (AlertDialog, title 'グループ単位の回数'): triggered either by the AddRowButton in GroupRangeSection or by tapping any applied-group summary InputChip — BOTH triggers just set the local Boolean `dialog=true` with no pre-fill data passed, so the dialog always opens blank (g=0,k=0,lo="",hi="") regardless of which chip (if any) was tapped. Confirms via DialogConfirmButton('適用', enabled = ok) → onApply(g,k,lo.trim(),hi.trim()) → vm.setGroupRange(...) then dialog=false. Cancels via DialogDismissButton or onDismissRequest → onClose() → dialog=false.
- Staff-picker DropdownMenu (inside StaffRangeDialog): expanded=openS; each staff name is a DropdownMenuItem setting i=idx and openS=false; dismiss request sets openS=false only.
- Shift-picker DropdownMenu (inside StaffRangeDialog): expanded=openK; items filtered to `idx in allowedFor(i)` (recomputed from current i); selecting sets k=idx and openK=false. Changing staff (i) afterward does NOT reset k, and the dialog's `ok` gate does not re-check k against allowedFor(i) — only against shifts.indices.
- Group-picker DropdownMenu (inside GroupRangeDialog): expanded=openG; each item shows '{name}（{memberCount(idx)}名）'; selecting sets g=idx, resets k=0, and sets openG=false.
- Shift-picker DropdownMenu (inside GroupRangeDialog): expanded=openK; items filtered to `idx in allowed` where `val allowed = allowedFor(g)` is recomputed at composition time from the current g; selecting sets k=idx and openK=false.

### Android/Compose固有パターン（要変換方針）

- remember { mutableStateOf(...) } via `by` delegate for both dialog-visibility flags and in-dialog form fields (dialog in StaffRangeSection/GroupRangeSection; i/k/lo/hi/openS/openK in StaffRangeDialog; g/k/lo/hi/openG/openK in GroupRangeDialog): correctness depends on the dialog being fully removed from the composition tree between opens (see next pattern) so fields re-initialize from `init`/defaults every time — in WinUI3 this must be an explicit 'reset fields on show', not a persistently-bound ViewModel property that could carry stale values across dialog invocations.
- Conditional composition via nullable/Boolean local state (`dialog?.let { d -> StaffRangeDialog(...) }` in StaffRangeSection; `if (dialog) { GroupRangeDialog(...) }` in GroupRangeSection): gates whether the dialog overlay exists in the tree at all — maps to lazily constructing and ShowAsync()'ing a ContentDialog only when the flag becomes truthy, and fully discarding its local edit state when it becomes null/false again.
- InputChip(selected, enabled, onClick, label, colors, trailingIcon) where trailingIcon is an inner `Icon(...).clickable(enabled=!ui.running){...}`: used identically for per-staff range chips and per-group summary chips — the chip body's own onClick (open edit dialog) and the trailing '×' icon's own click (delete immediately, no confirmation) are two independently hit-testable regions layered inside one chip; must be modeled as two separate tap targets in a WinUI3 custom chip control, not a single Button.Click.
- FlowRow (@OptIn(ExperimentalLayoutApi::class)) used in StaffRangeSection (per-staff chip lists) and GroupRangeSection (applied-group chip list): needs a wrap-layout (e.g. WrapPanel / ItemsRepeater with a wrap layout) in WinUI3 since chips must reflow onto new lines based on available width.
- Conditional Modifier composition (`if (bad) Modifier.border(1.dp, MaterialTheme.colorScheme.error, MaterialTheme.shapes.medium) else Modifier`) wrapping the paired lo+hi NumberStepper Column, identical in both dialogs: maps to a converter/state-triggered BorderBrush+BorderThickness (or VisualState) applied to the whole stepper group as one unit, not to each stepper individually.
- String-typed numeric editing state (`lo`/`hi` declared as `String`, not `Int`), representing a tri-state 'blank = no limit / digits = a limit', trimmed only at submit time inside onApply (`lo.trim()`, `hi.trim()`), never during typing: WinUI3 binding must preserve raw string state through the whole edit session and only coerce/trim on commit, matching NumberStepper's own blank-handling contract (NumberStepper itself is external and not defined in this file).
- Inline, non-memoized pure-function validation re-evaluated on every recomposition (`val bad = V6SanityPort.rangeOrderConflict(lo, hi) != null`; `val ok = ... && !bad`, identical shape in both dialogs): fine under Compose's cheap recomposition model, but should become an observable/computed property re-evaluated on PropertyChanged of the relevant fields (i/k/lo/hi, or g/k/lo/hi) in the WinUI3 port, not a one-shot evaluation.
- Elvis-chain design-token fallback with hex-string parsing (`ui.violationColorHex.takeIf { it.isNotBlank() }?.let { hexToColor(it) } ?: MagiAccent.red`, and the symmetric one for `.violationSoftColorHex`/`MagiAccent.orange`): 'possibly-blank override, else fall back to a design-token default' — translate as a null/empty-coalescing chain, not a force-unwrap.
- `groupBy { it.i }` then `.forEach { (_, list) -> ... }` over the flat list returned by vm.staffCountRules(), performed client-side in StaffRangeSection to build per-staff header groups (no VM-side grouping): C# LINQ `GroupBy` preserves first-encounter key order like Kotlin's `groupBy`, so a direct translation should match ordering, but this grouping logic itself must be reproduced in the view/view-model layer, not assumed to be pre-grouped by the data source.

### 落とし穴

- Both dialog composables (StaffRangeDialog, GroupRangeDialog) contain zero direct ViewModel references — all vm.* coupling is injected via lambda parameters (allowedFor, onApply) bound at the call site in the enclosing Section composables. Keep them VM-agnostic when porting, driven purely by delegate callbacks/converters, or the wiring will diverge.
- Validation asymmetry between the two dialogs: GroupRangeDialog's `ok` requires `k in allowed` (shift must belong to the currently-selected group's allowed set), but StaffRangeDialog's `ok` only requires `k in shifts.indices` — it never re-validates k against allowedFor(i). Combined with StaffRangeDialog never resetting k when i changes (GroupRangeDialog DOES reset k=0 when g changes), StaffRangeDialog could in theory let confirm succeed with a k that isn't actually allowed for the currently-selected staff. Preserve exactly as-is; this is a faithful port, not a bugfix.
- InputChip.trailingIcon is `null` (no delete affordance rendered) in StaffRangeSection when `!r.hasRange` — those are apt-target-only rows with no explicit staffRange entry to delete. Gate delete-icon visibility on r.hasRange specifically, not merely on the row existing.
- chipColors in StaffRangeSection collapses FOUR distinct violation-key strings into TWO visual buckets: "vio-low" and "vio-aptLow" both render the same red-family tinted background; "vio-high" and "vio-aptHigh" both render the same orange-family tinted background; any other value (including absent map entry) uses default untinted chip colors.
- ui.countViolations is looked up with a composite String key built exactly as `"${r.i},${r.k}"` (comma-joined staff-index,shift-index, no spaces) — preserve this exact key format if the underlying dictionary/map is translated to C#.
- The '今{now}' current-count shown per chip is computed client-side, not read from a precomputed ViewModel field: `ui.schedule.getOrNull(r.i)?.count { it == r.k } ?: 0` — i.e. count entries in staff r.i's schedule row equal to shift index r.k. Requires ui.schedule to be shaped as an indexable [staffIndex][dayIndex] = shiftIndex structure; the exact UiState type is external and was not read in this pass.
- toHankakuKigou(...) full-width→half-width conversion is applied to the kigou in GroupRangeSection's summary-chip label, but NOT applied to r.kigou in StaffRangeSection's per-staff chip label — this asymmetry exists in the source and must be preserved exactly, not normalized.
- Deletion (vm.removeStaffRange, vm.clearGroupRange) fires immediately on tapping a chip's '×' icon in either section — there is no confirmation dialog, Snackbar, or undo affordance visible anywhere in this file for either delete path.
- GroupRangeDialog always opens completely blank (g=0,k=0,lo="",hi="") no matter whether it was opened via the generic add button or by tapping an EXISTING applied-group summary chip — unlike StaffRangeDialog, which pre-fills from the tapped row's data. Tapping an existing group-range chip does NOT let the user edit that specific group/shift/range in place; it only opens a fresh blank dialog (footer text warns that confirming will overwrite existing individual staffRange settings for the chosen group+shift).
- StaffRangeEdit is an `internal data class(val i: Int, val k: Int, val lo: String, val hi: String)` defined locally in this file — a transient UI parameter bag only used to open/pre-fill StaffRangeDialog. Do not conflate it with the underlying persisted `staffRange["i,k"] = Range(lo,hi)` domain model referenced in comments (that Range type lives externally in the ViewModel/engine layer).
- In both dialogs, the error-colored border Column wraps BOTH the lo and hi NumberStepper rows together as a single visual group when `bad` is true — it is not applied to each stepper individually.
- RANGE_ORDER_HINT / RANGE_REQUIRED_HINT text and V6SanityPort.rangeOrderConflict(lo,hi) validation semantics are both external; their exact copy/logic must be sourced from Affordance.kt and V6SanityPort.kt respectively (not guessed) for a faithful port of the validation-message behavior.
- CountsCard's first child call, `AptSection(ui, vm)`, is NOT defined in this file — it was intentionally left unread per task scope. A complete C#/WinUI3 port of CountsCard cannot be finished without separately mapping AptSection from wherever it is defined.
- `enabled = !ui.running` is applied independently to every interactive element (each AddRowButton, each InputChip, each chip's trailing delete-icon .clickable modifier) — there is no single top-level disabling overlay for the whole card/section; each control must independently bind IsEnabled to the inverse of the running flag.

### 外部依存

- `toHankakuKigou (top-level function, package com.magi.app) — full-width→half-width kigou string conversion, imported explicitly and used only in GroupRangeSection's chip label.`
- `V6SanityPort (object, package com.magi.app.v6) — V6SanityPort.rangeOrderConflict(lo, hi) is the sole call, used for lo>hi validation in both dialogs.`
- `UiState — external data holder passed as `ui`; fields read: schedule, countViolations, violationColorHex, violationSoftColorHex, running, loaded, staffNames.`
- `MagiViewModel — external class passed as `vm`; see per-composable viewModelCalls lists for exact methods (staffCountRules, removeStaffRange, shiftKigouList, allowedShiftsFor, setStaffRange, groupRangeSummary, clearGroupRange, groupLabels, allowedShiftsForGroup, groupMemberCount, setGroupRange). Return-row shapes for staffCountRules() (fields used: i, k, staffName, kigou, lo, hi, aptEff, aptRaw, hasRange) and groupRangeSummary() (fields used: g, k, groupName, kigou, lo, hi, shared, members) are inferred from usage here but defined externally.`
- `AptSection — composable called as the first child of CountsCard (`AptSection(ui, vm)`); NOT defined in this file, left unread per task scope.`
- `AddRowButton — composable helper used in StaffRangeSection ('上下限を追加') and GroupRangeSection ('グループに上下限を適用'); signature inferred as taking a label String, an onClick lambda, and an `enabled: Boolean`.`
- `DialogConfirmButton / DialogDismissButton / DialogHeader — composable helpers used identically in both AlertDialogs for the confirm slot, dismiss slot, and title slot respectively.`
- `NumberStepper — composable used for all four lo/hi editing rows across both dialogs; called as NumberStepper(label, value, onValueChange, min = 0, blankLabel = "なし"); exact stepping/clamping/blank-toggle behavior lives externally.`
- `RANGE_ORDER_HINT / RANGE_REQUIRED_HINT — external String constants used verbatim as the two possible validation-hint messages in both dialogs.`
- `hexToColor(hex: String): Color — external converter used to turn a possibly-blank hex string from UiState into a Compose Color.`
- `MagiAccent — external design-token object; MagiAccent.red and MagiAccent.orange are used as color fallbacks.`


---

## WishEditor.kt

### 役割

Test purpose sentence.

### Composable関数

#### `WishCard`

```kotlin
fun WishCard()
```

**描画内容:** test


### ダイアログ/オーバーレイ

- test dialog

### Android/Compose固有パターン（要変換方針）

- test pattern

### 落とし穴

- test gotcha

### 外部依存

- `test dep`


---

## ShiftColorEditor.kt

### 役割

Implements the "シフトの表示色" (shift display color) settings card and its shared color-picker dialog for the MAGI ShiftOptimizer app. `ShiftColorCard` lists every shift type as a tappable color chip (via `vm.shiftColorList()`) and lets the user override or reset each shift's display color, which only affects rendering on the schedule grid (explicitly documented as non-scoring/display-only: "表示専用のため採点・エンジンに影響しない"). `ColorPickerDialog`, `ColorChip`, `Swatch`, and `hexToColor` are generic/reusable and are also shared by other screens (per in-file comments, at least a violation-severity color picker elsewhere, and `ScheduleGrid` reuses `hexToColor`). This file has zero interaction with the optimization engine, checker, or scoring.

### Composable関数

#### `ShiftColorCard`

```kotlin
@OptIn(ExperimentalLayoutApi::class)
@Composable
fun ShiftColorCard(
    ui: UiState,
    vm: MagiViewModel,
)
```

**描画内容:** A Card titled "シフトの表示色" with a description line. If `vm.shiftColorList()` is empty it shows a "（データ未読込）" placeholder; otherwise it shows a wrap-layout `FlowRow` of `ColorChip` entries, one per shift (chip disabled while `ui.running`). Tapping a chip sets local state `target` to that shift's symbol, which conditionally renders a `ColorPickerDialog` below the Card (rendered outside/after the Card composable, sibling in the same function).

**ViewModel呼出:**

- `vm.shiftColorList()`
- `vm.setShiftColor(kg, hex)`
- `vm.resetShiftColor(kg)`

**読むUiStateフィールド:**

- `ui.running`

**Compose固有ローカル状態:**

- var target by remember { mutableStateOf<String?>(null) } — the shift symbol (kigou) currently selected for color editing; null means the picker dialog is hidden. Cleared back to null inside onPick/onReset/onClose callbacks passed to ColorPickerDialog.


#### `ColorChip`

```kotlin
@Composable
internal fun ColorChip(
    hex: String,
    label: String,
    custom: Boolean,
    enabled: Boolean = true,
    onClick: () -> Unit,
)
```

**描画内容:** A single Row (min height 48dp) clipped to `MaterialTheme.shapes.medium`, bordered 2dp `colorScheme.primary` if `custom` is true else 1dp `colorScheme.outline`, made clickable (respecting `enabled`), containing a 24dp `Swatch(hex)` followed by `Text(label)` in `bodyMedium` style. This is the shared "press to change a color" affordance used identically by both the shift-color list here and (per KDoc) the violation-severity color list elsewhere.


#### `Swatch`

```kotlin
@Composable
private fun Swatch(hex: String, sizeDp: androidx.compose.ui.unit.Dp)
```

**描画内容:** A small square Box of size `sizeDp`, filled with `hexToColor(hex)` background clipped to `MaterialTheme.shapes.small`, with a 1dp 50%-alpha `colorScheme.outline` border in the same shape. Pure color-preview element, used both inline in ColorChip (24dp) and in the dialog's "current color" row (28dp).


#### `ColorPickerDialog`

```kotlin
@Composable
internal fun ColorPickerDialog(
    kigou: String,
    currentHex: String,
    onPick: (String) -> Unit,
    onReset: () -> Unit,
    onClose: () -> Unit,
    defaultHex: String = "",
)
```

**描画内容:** A Material3 `AlertDialog` (`onDismissRequest = onClose`). `title = DialogHeader("「$kigou」の色", onClose)`. `confirmButton = DialogConfirmButton("閉じる", onClick = onClose)`. `dismissButton = DialogDismissButton(onClick = onReset, text = "既定に戻す")` — NOTE: the dismiss slot holds a real mutating "reset to default" action, not a neutral cancel. Body (`text`) is a scrollable Column containing: (1) a Row showing `Swatch(effectiveHex, 28.dp)` plus a label "現在の色（既定）" if `currentHex` is blank else "現在の色"; (2) a "色を選ぶ" label; (3) `COLOR_PALETTE.chunked(6)` rendered as rows of `Row(fillMaxWidth)`, each cell a `Box` sized via `weight(1f).aspectRatio(1f)` (equal-width squares), background = `hexToColor(hex)` clipped `extraSmall`, bordered 3dp `colorScheme.primary` if `hex.equals(effectiveHex, ignoreCase=true)` (selected) else 1dp `colorScheme.outline`, `clickable { onPick(hex) }`, with `semantics { contentDescription = "色 $hex" + (selected suffix) }`; selected cells show a bold "✓" Text colored via `pickFg(hex)`. Any incomplete final row (fewer than 6 colors) is padded with blank `Spacer(Modifier.weight(1f).aspectRatio(1f))` cells to keep grid alignment/square sizing. `effectiveHex = currentHex.ifBlank { defaultHex }` drives both the preview swatch and the selection highlight; ShiftColorCard's call site never passes `defaultHex`, so it defaults to `""`, meaning `effectiveHex` is blank (renders gray via hexToColor's fallback) whenever the shift has no override.


### ヘルパー関数

- `hexToColor`（`internal fun hexToColor(hex: String): Color`）: Parses a "#rrggbb" or "#rgb" hex color string (trimmed, leading '#' stripped) into a Compose `Color`. 3-char hex is expanded by doubling each digit (e.g. "f0a" -> "ff00aa"); any length other than 3 or 6 falls back to the literal string "888888". Parsing failure (`toIntOrNull(16)` returns null) also falls back to `0x888888`. Returns an opaque RGB Color via `Color(r, g, b)` extracted with bit shifts (`shr 16`, `shr 8`, `and 0xFF`). Documented as shared with `ScheduleGrid` in the same package.
- `pickFg`（`private fun pickFg(bgHex: String): String`）: Given a background hex color, computes perceptual luminance `0.299*r + 0.587*g + 0.114*b` (invalid/non-6-char hex falls back to "888888") and returns a foreground hex string for readable contrast: "#14110d" (dark) if luminance > 140, else "#fbf4e8" (light). Used only to color the "✓" selection checkmark text inside the palette grid so it stays legible against any swatch background.

### ダイアログ/オーバーレイ

- ColorPickerDialog (Material3 AlertDialog): shown in ShiftColorCard whenever local state `target` is non-null (i.e., a ColorChip was tapped). Title shows the shift symbol being edited ("「$kigou」の色"). The confirm button ('閉じる') and the dialog's own dismiss-request (scrim tap / back) both just close it via `onClose` (target = null) without changing anything. The dismiss-slot button ('既定に戻す') instead performs a real action: calls `vm.resetShiftColor(kg)` then closes. Tapping any individual color swatch inside the grid immediately applies that color (`vm.setShiftColor(kg, hex)`) and closes the dialog — there is no separate 'confirm my swatch choice' step; picking IS committing.

### Android/Compose固有パターン（要変換方針）

- remember { mutableStateOf<String?>(null) } (property-delegate `by` syntax) in ShiftColorCard: drives whether ColorPickerDialog is shown, keyed on which shift's kigou is being edited; needs an explicit nullable observable backing field + dialog-visibility binding in WinUI3/x:Bind (e.g. a nullable string property whose setter opens/closes a ContentDialog), since there is no native "remember" concept.
- target?.let { kg -> ColorPickerDialog(...) } conditional composable rendering in ShiftColorCard: the dialog composable is only invoked (and thus only exists in the composition) when `target` is non-null — this is presence/absence conditional rendering, not just a visibility toggle; in WinUI3 this typically becomes an explicit `if (target != null) { show ContentDialog }` in code-behind rather than a declarative binding, because Compose recomposition semantics (mount/unmount) differ from XAML's show/hide.
- AlertDialog(onDismissRequest, confirmButton, dismissButton, title, text) slot-based API in ColorPickerDialog: `confirmButton` here is the neutral "閉じる" (Close) action wired to `onClose` (same as `onDismissRequest`), while `dismissButton` is NOT a cancel — it performs a real state-mutating "既定に戻す" (Reset to default) action via `onReset`. Any translation to WinUI3 ContentDialog (PrimaryButtonText/SecondaryButtonText/CloseButtonText + Click/PrimaryButtonClick/SecondaryButtonClick events, plus its own implicit ESC/light-dismiss behavior) must preserve this inverted mapping deliberately — do not assume dismissButton == cancel.
- FlowRow(horizontalArrangement, verticalArrangement) under @OptIn(ExperimentalLayoutApi::class) in ShiftColorCard: an experimental Compose Foundation wrap-layout with no first-class WinUI3 panel equivalent; needs a custom wrap/flow panel (e.g. a WrapPanel implementation or ItemsRepeater with a wrapping layout) to reproduce chip auto-wrapping with 8dp spacing in both axes.
- Modifier.weight(1f).aspectRatio(1f) combo on each palette swatch Box in ColorPickerDialog: produces N equal-width square cells inside a fixed-width Row (used together with `repeat(perRow - rowColors.size) { Spacer(Modifier.weight(1f).aspectRatio(1f)) }` to pad incomplete rows). WinUI3 has no direct weight+aspectRatio primitive on a Grid child; requires a Grid with equal `ColumnDefinition Width="*"` columns and a per-cell Height binding to ActualWidth (or a custom AspectRatio behavior), plus explicit blank placeholder cells to fill a short final row.
- Modifier.clickable(enabled = enabled, onClick = onClick) in ColorChip: standard Compose tap-with-disable-gate on an arbitrary Row (not a Button). In this file, disabling does NOT change the visual appearance at all (no alpha()/dim modifier applied when enabled=false) — only the tap handler is gated. Port to WinUI3 must decide whether to replicate this "disabled but visually unchanged" behavior (e.g. IsHitTestVisible/IsEnabled bound without a visual state trigger) or intentionally improve it with a dimmed disabled VisualState — flag as a behavioral decision point, not just a mechanical translation.
- Modifier.semantics { contentDescription = "色 $hex" + (if (selected) "・選択中" else "") } on each palette swatch: Compose accessibility narration API, applied ONLY to the dialog's palette grid cells (not to ColorChip or the "current color" preview Swatch, which rely implicitly on the adjacent Text label). Maps to `AutomationProperties.Name` in WinUI3, but the asymmetric application (only on grid cells) should be reviewed/preserved or deliberately extended when porting.

### 落とし穴

- The AlertDialog's dismissButton slot ('既定に戻す'/Reset-to-default) is a real mutating action, not a cancel/no-op — do not map it to a generic 'Cancel'/SecondaryButton-that-does-nothing pattern in WinUI3; it must invoke the reset callback just like a primary action would.
- ShiftColorCard never supplies `defaultHex` to ColorPickerDialog (uses the `= ""` default), so for THIS screen `effectiveHex` is blank whenever no override is set, and `hexToColor("")` falls through to the generic gray fallback (#888888) rather than showing the shift's true resolved default color (which per the ShiftColorCard KDoc is computed by a separate `resolveShiftColor` function not present in this file). Other call sites of this same dialog (per comments) DO pass a meaningful defaultHex — don't assume uniform behavior across all uses of ColorPickerDialog.
- Selection highlighting in the palette grid uses case-insensitive hex comparison (`hex.equals(effectiveHex, ignoreCase = true)`); the palette itself is stored fully lowercase, but user/stored hex values elsewhere may not be — preserve case-insensitive compare exactly.
- COLOR_PALETTE is a hand-curated, hard-coded compile-time list of exactly 36 lowercase hex strings (a 6x6 grid, `perRow = 6`) with an extensive multi-revision comment history (colors were manually chosen/replaced multiple times to satisfy contrast/anchor-color/saturation constraints, e.g. row0col0 = "#e08a1e" must stay fixed as an anchor color matching MagiAccent.orange used elsewhere). This is literal design data, not something to regenerate algorithmically — copy the 36 values verbatim in order.
- hexToColor's invalid-input fallback (gray 0x888888) and pickFg's two contrast-fallback strings ("#14110d" dark / "#fbf4e8" light, threshold luminance > 140) are three independent hard-coded constants — do not conflate or derive one from another.
- ColorChip's `enabled = false` state (used when `!ui.running` is false, i.e. during an active run) disables ONLY the click handler via `clickable(enabled = false)` — no opacity/alpha or other visual dimming is applied anywhere in this file, so a disabled chip is visually indistinguishable from an enabled one. Confirm intentionally whether to replicate this exact (non-obvious) lack-of-visual-feedback in the WinUI3 port or to add an explicit disabled visual state.
- Three distinct MaterialTheme.shapes tokens are used and must map to three distinct WinUI3 CornerRadius resources, not one shared radius: `MaterialTheme.shapes.medium` (ColorChip's outer border/clip), `MaterialTheme.shapes.small` (Swatch background/border), `MaterialTheme.shapes.extraSmall` (dialog palette grid cells).
- Accessibility contentDescription (`semantics { contentDescription = ... }`) is applied ONLY to the dialog's palette grid swatches (line ~252) — the ColorChip list items and the dialog's 'current color' preview Swatch have no explicit accessibility label and rely on an adjacent Text element for implicit screen-reader context; this asymmetry should be reviewed when adding AutomationProperties.Name in the WinUI3 port.
- `Swatch`'s `sizeDp` parameter type is written using the fully-qualified name `androidx.compose.ui.unit.Dp` (only the `.dp` extension is imported at the top of the file, not the `Dp` type itself) — purely a Kotlin authoring detail with no functional significance, but note when reading the exact signature.
- The dialog's grid-padding logic (`repeat(perRow - rowColors.size) { Spacer(...) }`) is dead in practice with the current 36-item palette (36 % 6 == 0, so every row is always full) but the generic code path exists and must still be correctly translated (WinUI3 Grid/Panel needs equivalent blank filler cells) in case the palette size ever changes to something not evenly divisible by 6.

### 外部依存

- `UiState — parameter type of ShiftColorCard; only field read is `ui.running` (defined elsewhere, not in this file)`
- `MagiViewModel — parameter type of ShiftColorCard; methods called: vm.shiftColorList(), vm.setShiftColor(kigou: String, hex: String), vm.resetShiftColor(kigou: String) (defined elsewhere, not in this file)`
- `Return element type of vm.shiftColorList() (name/type not defined in this file) — inferred to expose `.hex: String`, `.kigou: String`, `.custom: Boolean` fields, consumed as `sc.hex`/`sc.kigou`/`sc.custom` in ShiftColorCard and via `shifts.firstOrNull { it.kigou == kg }` to find `current.hex``
- `DialogHeader(title: String, onClose: () -> Unit) — shared dialog title composable used as the AlertDialog `title` slot in ColorPickerDialog (defined elsewhere, likely a shared Affordance/Dialogs helper file)`
- `DialogConfirmButton(text: String, onClick: () -> Unit) — shared composable used as the AlertDialog `confirmButton` slot (defined elsewhere)`
- `DialogDismissButton(onClick: () -> Unit, text: String) — shared composable used as the AlertDialog `dismissButton` slot (defined elsewhere)`
- `ScheduleGrid — other screen/file named in the KDoc on hexToColor as sharing that function within the same package (not otherwise referenced in this file's code)`
- `Another screen (referenced only in comments, not code) that shares ColorPickerDialog for a 'violation severity color' / '人員不足の色' picker, and that DOES pass a meaningful defaultHex (e.g. MagiAccent.orange = #E08A1E) unlike ShiftColorCard's call site here — name not given in this file (comment references it only descriptively)`


---

## SkillGroupEditor.kt

### 役割

Editor for the "スキルグループ" (skill group) second classification, which is separate from unit/担当 (assignment) groups and does not affect shift eligibility (canDo). It is a 1-person-1-skill classification referenced only by skill-group range rules (cons41s) and skill-group pair-prohibition rules (cons42s). The file provides CRUD for the list of skill groups (symbol + name) and, once at least one skill group exists, a per-staff-member dropdown to assign (or unassign, "(なし)") that staff member's single skill group.

### Composable関数

#### `SkillGroupCard`

```kotlin
fun SkillGroupCard(ui: UiState, vm: MagiViewModel)
```

**描画内容:** A Card containing: an explanatory line about the skill-group classification and, if any skill groups exist, a status line reporting how many skill-group rules currently reference this classification (or a note that zero do); a list of existing skill groups (symbol + name) each with an edit button and a delete button; an 'add skill group' row button; and, only if skill groups exist, a Divider followed by a per-staff-member row list where each row shows the staff name and a dropdown button (current skill symbol or '(なし)') to reassign that staff member's single skill group. It also conditionally renders an Add/Edit AlertDialog (via SkillGroupDialog) and a delete-confirmation AlertDialog based on local dialog/confirmDelete state.

**ViewModel呼出:**

- `vm.skillGroups()`
- `vm.ws1()`
- `vm.setStaffSkill(i, -1)`
- `vm.setStaffSkill(i, gi)`
- `vm.addSkillGroup(n, k)`
- `vm.editSkillGroup(d.g, n, k)`
- `vm.skillConstraintFamilies()`
- `vm.ws1SkillGroupRefCount(g)`
- `vm.removeSkillGroup(g)`

**読むUiStateフィールド:**

- `ui.loaded`
- `ui.running`

**Compose固有ローカル状態:**

- var dialog by remember { mutableStateOf<SkillDlg?>(null) } — type SkillDlg? (private sealed interface: object Add, or data class Edit(val g: Int, val name: String, val kigou: String)); initial value null; controls which of the Add/Edit AlertDialog variants is shown, and (for Edit) which index/name/symbol pre-fills the dialog.
- var confirmDelete by remember { mutableStateOf<Int?>(null) } — type Int?; initial value null; holds the index of the skill group pending delete confirmation; gates the inline delete AlertDialog.
- var open by remember { mutableStateOf(false) } — type Boolean; initial value false; declared inside the `staff.forEachIndexed { i, st -> ... }` loop body (one remembered instance per composed row, via Compose slot-table position, no explicit key(i)); controls that row's DropdownMenu expanded/collapsed state.


#### `SkillGroupDialog`

```kotlin
private fun SkillGroupDialog(title: String, name0: String, kigou0: String, onOk: (String, String) -> Unit, onClose: () -> Unit)
```

**描画内容:** An AlertDialog (via DialogHeader for the title) containing an OutlinedTextField for the symbol ('記号（例: N）', max 4 characters enforced manually) and an OutlinedTextField for the name ('名前（例: 看護）', unlimited length), plus a confirm button (label 'OK', enabled only when kigou is non-blank) and a dismiss button.

**Compose固有ローカル状態:**

- var name by remember { mutableStateOf(name0) } — type String; initial value is the `name0` parameter; bound to the name OutlinedTextField.
- var kigou by remember { mutableStateOf(kigou0) } — type String; initial value is the `kigou0` parameter; bound to the symbol OutlinedTextField, with onValueChange guarded to reject input beyond 4 characters.


### ダイアログ/オーバーレイ

- SkillGroupDialog (wraps an AlertDialog): reused for both the 'スキルグループ追加' (Add) and 'スキルグループ編集' (Edit) titles. Triggered by the `dialog` local state being SkillDlg.Add or SkillDlg.Edit (set from tapping the AddRowButton or an EditRowButton). Contains a symbol field (max 4 chars) and a name field. Confirm (DialogConfirmButton 'OK', enabled only when kigou is non-blank) invokes onOk(name, kigou), which calls vm.addSkillGroup(n, k) or vm.editSkillGroup(d.g, n, k) and then sets dialog = null. Cancel (DialogDismissButton) or DialogHeader's close action invokes onClose, setting dialog = null without saving.
- Inline delete-confirmation AlertDialog (unnamed, built directly inside SkillGroupCard): triggered when `confirmDelete` (an Int? index) is non-null, i.e. after tapping a DeleteRowButton. Title: 'スキルグループを削除しますか？'. Body text names the target skill group ('「{kigou} {name}」を削除します。所属していた職員のスキル割当は自動で付け替わります。元に戻すで取り消せます。') plus, conditionally, a reference-count note ('このスキルグループを参照する制約が{N}件あります。削除すると評価対象から外れます。') appended only when vm.ws1SkillGroupRefCount(g) > 0 — otherwise that sentence is simply omitted (concatenated as an empty string, not shown as a separate empty element). Confirm (DialogDangerButton '削除する') calls vm.removeSkillGroup(g) then sets confirmDelete = null. Dismiss (DialogDismissButton) or onDismissRequest just sets confirmDelete = null, canceling the delete.
- DropdownMenu (per staff row, one instance per row inside the staff.forEachIndexed loop): a Material3 dropdown anchored to an OutlinedButton inside a Box, controlled by the per-row `open` boolean. Opened by tapping the OutlinedButton (open = true). Contains one DropdownMenuItem for '(なし)' (calls vm.setStaffSkill(i, -1) then open = false) and one DropdownMenuItem per skill group showing '{kigou}  {name}' (calls vm.setStaffSkill(i, gi) then open = false). Also closes via onDismissRequest = { open = false } (tap-outside).

### Android/Compose固有パターン（要変換方針）

- remember { mutableStateOf<SkillDlg?>(null) } for `dialog`: SkillGroupCard line 47: drives which of two AlertDialog configurations (Add vs Edit) is shown via a `when(val d = dialog)` block; WinUI3/x:Bind needs an equivalent nullable state (e.g. an enum or nullable view-model property) that triggers ContentDialog construction/ShowAsync, since Compose's declarative recomposition-based conditional rendering does not map 1:1 onto WinUI3's typically-imperative ContentDialog.ShowAsync() flow.
- remember { mutableStateOf<Int?>(null) } for `confirmDelete`: SkillGroupCard line 49, comment tagged [破壊操作ガード] (destructive-operation guard): a two-step confirm pattern (tap delete sets confirmDelete=g, then a second AlertDialog must be explicitly confirmed) gating vm.removeSkillGroup(g); must preserve as a two-step confirmation, not a single-tap delete.
- Per-row remember { mutableStateOf(false) } for `open`, declared inside `staff.forEachIndexed { i, st -> ... }` (loop starts SkillGroupCard line 84, state declared line 87): relies on Compose's positional slot-table memory to give each row independent DropdownMenu open/closed state without an explicit key(i); the list is rendered via a plain non-lazy `forEachIndexed` inside a Column (not a LazyColumn), so every row is always composed and keeps its own remembered slot. In WinUI3, each item's flyout-open state must be truly independent per row (e.g. a property on a per-row bound view-model item), not a single shared/static field, and must not rely on positional recycling the way a virtualizing ListView would.
- DropdownMenu anchored via Box { OutlinedButton(...) ; DropdownMenu(expanded=open, onDismissRequest={open=false}) { ... } }: SkillGroupCard lines 88-105: Compose overlays the DropdownMenu relative to its Box anchor and closes on dismiss request or item click; WinUI3 equivalent is a Button with an attached MenuFlyout/Flyout, opened by toggling the Flyout's IsOpen (or calling ShowAt) rather than by expanded-boolean-driven recomposition.
- `when (val d = dialog) { SkillDlg.Add -> ...; is SkillDlg.Edit -> ...; null -> {} }`: SkillGroupCard lines 112-116: Kotlin sealed-interface exhaustive pattern match selecting which SkillGroupDialog invocation (title + prefill values + onOk callback) to render; WinUI3 needs equivalent conditional/enum-driven dialog construction logic in code-behind or a converter, since there is no native sealed-interface pattern match.
- `confirmDelete?.let { g -> ... }`: SkillGroupCard lines 118-130: idiomatic Kotlin 'render only if non-null'; maps to a null check gating ContentDialog.ShowAsync(), or a Visibility binding (null -> Collapsed) if using a custom overlay instead of a true modal dialog.
- Direct non-lazy loop emission of Composables via `skills.forEachIndexed { g, sg -> Row {...} }` (SkillGroupCard line 70) and `staff.forEachIndexed { i, st -> Row {...} }` (line 84), both inside a plain `Column`, not `LazyColumn`: no virtualization occurs; every skill group/staff row is always fully composed with its own remembered UI state (e.g. the per-row `open` dropdown flag). WinUI3 translation should use a StackPanel/ItemsControl bound to an ObservableCollection (all items always realized) rather than a virtualizing ListView/ItemsRepeater if exact parity of always-live per-row state is required.
- `enabled = !ui.running` bound on EditRowButton (line 73), DeleteRowButton (line 75), AddRowButton (line 78), and the per-staff OutlinedButton (line 89): a UiState boolean disables all mutation-capable controls while the optimization engine is running (app-wide invariant per CLAUDE.md's 'running' semantics); must bind IsEnabled to the equivalent busy-state property in the WinUI3 view model.
- Null-safe elvis chain `skills.getOrNull(st.skillIdx)?.kigou ?: "(なし)"` (SkillGroupCard line 90): renders the current dropdown button label; skillIdx == -1 (or any out-of-range index) must render the literal string '(なし)' rather than crashing or rendering blank — this mirrors the engine convention (per inline comment on line 99) that skillIdx=-1 means 'no skill group assigned' and is a normal, intentional value, not an error state. Preserve exactly via a null-conditional operator plus '??' or a value converter in C#; do NOT clamp/coerce -1 to 0.
- `MaterialTheme.typography.titleSmall` style token with no explicit fontSize override (SkillGroupCard line 83), annotated with an inline comment tagged [B6]: 'fontSize=13sp が titleSmall(16sp・+1sp スケール)を打ち消していた。override を外し scale に従わせる' — i.e. a previous explicit fontSize=13sp bug was overriding the theme's titleSmall token (16sp, plus the app-wide +1sp label-scale-up documented elsewhere in the project); the override was deliberately removed so this Text now inherits the theme default. WinUI3 translation should bind this TextBlock to a shared Style resource for titleSmall-equivalent typography rather than hardcoding a font size, to preserve 'follows the theme scale' behavior.
- Manual max-length guard via onValueChange instead of a native maxLength property: `OutlinedTextField(value = kigou, onValueChange = { if (it.length <= 4) kigou = it }, ...)` in SkillGroupDialog (~line 149): silently ignores/rejects any edit that would make the string exceed 4 characters (no truncation, no error message shown); the `name` field has no analogous length guard. Replicate the exact reject-if-over-4 behavior (e.g. via TextBox.TextChanging with e.Cancel, or a MaxLength=4 property if paste/typed behavior parity is verified equivalent) and leave the name field unconstrained.
- Icon with `contentDescription = "スキルを選ぶ"` on the dropdown chevron (`Icons.Filled.KeyboardArrowDown`, SkillGroupCard lines 92-96), described as a '校正' (proofreading/polish) comment adding a down-arrow dropdown affordance to the button: preserve as an accessible glyph (e.g. FontIcon/PathIcon with AutomationProperties.Name) signaling 'this button opens a dropdown', with the visible button label separately showing the currently selected skill symbol or '(なし)'.
- No LaunchedEffect, DisposableEffect, derivedStateOf, AnimatedVisibility, BackHandler, rememberCoroutineScope, or Modifier.pointerInput anywhere in this file — it is a purely synchronous, state-driven CRUD form; no async effects, animations, or custom gesture handling to translate beyond standard button/menu-item Click handlers.

### 落とし穴

- `if (!ui.loaded) return` at the very top of SkillGroupCard (line 43): the entire card renders nothing (not even a placeholder) if ui.loaded is false. Must be preserved as an early-exit / Visibility=Collapsed gate for the whole component, not just individual sub-sections.
- Two separate `if (skills.isNotEmpty())` blocks gate different content: the first (lines 59-68) gates the rule-count status text (computed via vm.skillConstraintFamilies().sumOf { it.rows.size }, showing one message if skillRules==0 and a different one otherwise); the second (lines 80-108) gates the Divider + '職員のスキル割当' section entirely — if there are zero skill groups, staff cannot be assigned any skill at all (the whole assignment UI is hidden, which is logically necessary since there'd be nothing to pick).
- skillIdx = -1 is a valid, intentional 'unassigned' sentinel value (per inline code comment: engine checks `ssk[i]==groupIdx` which is always false for -1, so -1 staff are simply excluded from any skill-group constraint) — it is NOT an error state and must render as '(なし)' via `skills.getOrNull(st.skillIdx)?.kigou ?: "(なし)"`, never coerced/clamped to 0 or treated as invalid.
- SkillGroupDialog is a single reused composable for both Add and Edit: for Add, it's invoked with empty strings (name0="", kigou0=""); for Edit, it's invoked with the existing d.name/d.kigou as prefill. Do not duplicate this into two separate dialog markups in translation — parameterize one dialog view the same way.
- The symbol ('記号') field enforces a hard max length of 4 characters via a manual `if (it.length <= 4)` guard on onValueChange (rejects further typed characters silently, no error message); the name field has no length constraint at all. This asymmetry must be preserved exactly.
- The Add/Edit dialog's confirm button is enabled only via `kigou.isNotBlank()` — the name field can be left blank and still submit successfully; only the symbol is required.
- Per-row DropdownMenu `open` state (declared with plain `remember`, no `key(i)`) depends on Compose's positional slot-table stability for the staff list; since the loop is a plain forEachIndexed inside a Column (not LazyColumn, no virtualization/recycling), this works reliably in Compose, but a WinUI3/XAML translation using a recycling ListView or ItemsRepeater must not accidentally share or misassign per-row open/closed flyout state across rows — each row's toggle must be independently backed (e.g., a property on a bound item, not a shared static/class field).
- The reference-count note in the delete-confirmation dialog is built as ONE concatenated string (`text = "...$name...$refNote"`), where refNote is either a real sentence or an empty string — it is not two separate conditionally-visible TextBlocks. Replicate as string interpolation/concatenation with a conditional substring, or ensure a two-control approach produces byte-identical rendered text when refs==0 (no stray blank line/paragraph).
- EditRowButton, DeleteRowButton, AddRowButton, DialogDangerButton, DialogDismissButton, DialogConfirmButton, and DialogHeader are all external composables defined in other files (not visible in this file) — their exact icon/color/sizing/interaction behavior (e.g. destructive styling for DialogDangerButton) must be sourced from their own definitions elsewhere, not inferred or invented here.
- Compose's AlertDialog is fully declarative (its presence/config is driven by recomposition of `dialog`/`confirmDelete` state, and it re-renders whenever ui/vm data it captures changes) whereas WinUI3's ContentDialog is typically shown imperatively via `await dialog.ShowAsync()`. This is an architectural mismatch requiring either an MVVM dialog-service pattern (raise an event/command that code-behind uses to construct and await a ContentDialog) or a custom overlay bound declaratively to Visibility — decide deliberately rather than defaulting to a naive ShowAsync() call inside a property setter.
- `Modifier.heightIn(min = 48.dp)` on the per-staff OutlinedButton (line 89) sets a minimum touch-target height distinct from the skill-group list's EditRowButton/DeleteRowButton (whose sizing is defined in their own external composable, not here) — do not assume all buttons in this file share identical sizing; only this specific OutlinedButton has an explicit min-height modifier in this file.
- The whole file has no ViewModel-level loading/error states beyond `ui.loaded`/`ui.running` — there is no pull-to-refresh, no async loading spinner, and no separate error-state UI; all data (skillGroups, staff list) is assumed synchronously available once ui.loaded is true.

### 外部依存

- `UiState (data class) — fields read: ui.loaded, ui.running`
- `MagiViewModel — methods called: skillGroups(), ws1(), setStaffSkill(Int, Int), addSkillGroup(String, String), editSkillGroup(Int, String, String), skillConstraintFamilies(), ws1SkillGroupRefCount(Int), removeSkillGroup(Int)`
- `MagiViewModel.ws1() return type — accessed via `vm.ws1()?.staff`, a nullable object exposing a `.staff` list (staff element type has `.name: String` and `.skillIdx: Int` fields, per usage `st.name` and `st.skillIdx`)`
- `Skill group element type returned by vm.skillGroups() — has `.kigou: String` and `.name: String` fields (per usage `sg.kigou`, `sg.name`)`
- `EditRowButton (composable, defined elsewhere) — called as EditRowButton(onClick = ..., enabled = !ui.running)`
- `DeleteRowButton (composable, defined elsewhere) — called as DeleteRowButton(onClick = ..., enabled = !ui.running)`
- `AddRowButton (composable, defined elsewhere) — called as AddRowButton("スキルグループ追加", onClick = ..., enabled = !ui.running)`
- `DialogDangerButton (composable, defined elsewhere) — used as the delete-confirmation dialog's confirm button, label "削除する"`
- `DialogDismissButton (composable, defined elsewhere) — used as the dismiss/cancel button in both dialogs`
- `DialogConfirmButton (composable, defined elsewhere) — used as the Add/Edit dialog's "OK" confirm button, with `enabled = kigou.isNotBlank()``
- `DialogHeader (composable, defined elsewhere) — used as the title slot of the Add/Edit AlertDialog, called as DialogHeader(title, onClose)`


---

## V6RemainingScreens.kt

### 役割

V6RemainingScreens.kt is a heavily-pruned remnant of a formerly larger composite/legacy screens file (per its own header comments documenting deletions across versions 3.86.0 and 3.286.0 of an unrendered composite screen plus 6 other dead composables: HeaderBar, RingGauge, OverviewDashboard, FlagsView, OperatorLogView, BottomNav, CheckSummaryView). What remains today is just two composables: (1) SectionSegment, a small generic 'titled Card section' layout helper with exactly one internal caller, and (2) ColorSettingsView, the live Settings-tab UI for customizing violation-severity/violation-family display colors used throughout the app's schedule-violation visualizations (grid borders, corner marks, day-header underlines, calendar cells, edit-sheet reason text per its own comments) -- it lets the user tap a color chip for the hard/critical baseline, the soft/warn baseline, or any individual violation-family (from MirrorKeys.all) to open a shared ColorPickerDialog and set/reset that color, with per-family overrides falling back to the two severity baselines and finally to a hardcoded gray.

### Composable関数

#### `SectionSegment`

```kotlin
fun SectionSegment(title: String, subtitle: String? = null, content: @Composable () -> Unit)
```

**描画内容:** A Material3 Card (Modifier.fillMaxWidth()) containing a Column (16dp padding) with: a bold Text(title), an optional Text(subtitle, fontSize=12sp, color=onSurfaceVariant) only if subtitle is non-null, a 10dp Spacer, then the invocation of the passed content() composable slot. It is a generic reusable 'titled card section' wrapper.


#### `ColorSettingsView`

```kotlin
@OptIn(ExperimentalLayoutApi::class) @Composable fun ColorSettingsView(ui: UiState, vm: MagiViewModel)
```

**描画内容:** The settings-tab 'violation family color' editor. Wraps everything in SectionSegment("違反種別の色") { ... }. Inside: computes local vals cs=MaterialTheme.colorScheme, baseHard=ui.violationColorHex.ifBlank{"#BA1A1A"}, baseSoft=ui.violationSoftColorHex.ifBlank{"#E08A1E"}. First FlowRow (8dp/8dp spacing) loops MirrorKeys.all, computing per-key severity (ShiftAppearance.severityFromVioKey), a Japanese severity label (CRITICAL->'必須', HIGH/WARN->'要調整', else->'情報'), a resolved hex (per-family override ui.violationFamilyColorHex[key] else severity-based baseHard/baseSoft else gray '#8A979B'), a violation count from ui.breakdown[key], and a composite label string (breakdownLabels[key] name + severity + optional 'N件' count suffix), then renders a ColorChip(hex, label, custom=famHex!=null, enabled=!ui.running) whose onClick sets pickFam=key. Then a subtitle Text('未設定の種別に効く基準色', 12sp) plus a second FlowRow with two ColorChip entries for '必須' (custom=ui.violationColorHex.isNotBlank()) and '要調整' (custom=ui.violationSoftColorHex.isNotBlank()), whose onClicks set pickFam to sentinel strings '__hard__'/'__soft__'. Outside/after the SectionSegment block, a `pickFam?.let { pf -> ... }` conditionally renders one of three ColorPickerDialog(...) overloads (branching on pf via `when`): '__hard__' branch edits the hard/critical baseline color, '__soft__' branch edits the soft/warn baseline color, and the else branch edits a specific per-family override color (using breakdownLabels[pf] as the dialog title and a severity-derived defaultHex). Each dialog's onPick/onReset/onClose callback also resets pickFam=null to close it.

**ViewModel呼出:**

- `vm.setViolationColor`
- `vm.resetViolationColor`
- `vm.setViolationSoftColor`
- `vm.resetViolationSoftColor`
- `vm.setViolationFamilyColor`
- `vm.resetViolationFamilyColor`

**読むUiStateフィールド:**

- `ui.violationColorHex`
- `ui.violationSoftColorHex`
- `ui.violationFamilyColorHex`
- `ui.breakdown`
- `ui.running`

**Compose固有ローカル状態:**

- var pickFam by remember { mutableStateOf<String?>(null) } -- String?, initial value null; holds either a MirrorKeys.all family key, or one of two sentinel strings '__hard__'/'__soft__', or null (no dialog open); drives which ColorPickerDialog variant (if any) is shown


### ダイアログ/オーバーレイ

- ColorPickerDialog (external composable, invoked 3 times with different parameter sets inside the `pickFam?.let { pf -> when(pf) { ... } }` block, lines 93-128): the ONLY overlay/dialog in this file. Trigger: appears whenever local state `pickFam` is non-null, which is set by tapping ANY ColorChip in either of the two FlowRow groups (a specific MirrorKeys family key from the first loop, or the sentinel strings '__hard__'/'__soft__' from the second row's two baseline chips). What confirms: the dialog's onPick(hex) callback -- for '__hard__' calls vm.setViolationColor(hex); for '__soft__' calls vm.setViolationSoftColor(hex); for any family key (else branch) calls vm.setViolationFamilyColor(pf, hex) -- and in all three cases also sets pickFam=null to dismiss the dialog. What cancels/resets: onReset() callback -- for '__hard__' calls vm.resetViolationColor(); for '__soft__' calls vm.resetViolationSoftColor(); for family keys calls vm.resetViolationFamilyColor(pf) -- also dismisses via pickFam=null. Plain dismiss (no save/reset): onClose = { pickFam = null }. Each branch passes a distinct kigou (dialog title: '必須違反（基準色）' / '要調整（基準色）' / breakdownLabels[pf]), currentHex (ui.violationColorHex / ui.violationSoftColorHex / ui.violationFamilyColorHex[pf] ?: ""), and defaultHex (hardcoded '#BA1A1A' / '#E08A1E' / severity-derived hardHex-or-softHex-or-'#8A979B'). No AlertDialog, ModalBottomSheet, DropdownMenu, or Snackbar appear anywhere in this file.

### Android/Compose固有パターン（要変換方針）

- var pickFam by remember { mutableStateOf<String?>(null) }: declared in ColorSettingsView (line 55): standard Compose 'which-dialog-is-open' local UI state via remember+mutableStateOf+by delegate, not rememberSaveable (does not survive process death/config change by design). In WinUI3 needs a change-notifying property (e.g. CommunityToolkit.Mvvm [ObservableProperty] or manual INotifyPropertyChanged) driving a ContentDialog/Flyout's visibility/open state, since x:Bind requires explicit change notification rather than delegate re-invocation on read.
- pickFam?.let { pf -> ... } (line 93): Compose's idiomatic 'conditionally emit a composable only when state is non-null' pattern used to gate rendering of the ColorPickerDialog overlay. Maps to an imperative ContentDialog.ShowAsync() call in code-behind triggered by the property setter, or a Visibility binding via a null-to-Visibility converter, since XAML has no inline 'if this is non-null, render this subtree' composable-lambda equivalent.
- Trailing-lambda content slot API: SectionSegment("違反種別の色") { ... } (line 61): Compose's slot-based content: @Composable () -> Unit trailing-lambda pattern for a reusable 'card wrapper' component. XAML has no direct trailing-lambda equivalent; requires either inlining the card chrome directly around the content, or a ContentControl/UserControl with an explicit Content property/ContentTemplate.
- FlowRow (androidx.compose.foundation.layout.FlowRow, @OptIn(ExperimentalLayoutApi::class)), used twice (lines 66 and 88) with Arrangement.spacedBy(8.dp) on BOTH horizontal and vertical axes: an auto-wrapping chip row. WinUI3 has no built-in wrap panel in core XAML; needs CommunityToolkit WrapPanel or a custom wrapping ItemsRepeater layout, matching the 8dp gap on both axes.
- MirrorKeys.all.forEach { key -> ... } (line 67): a plain Kotlin forEach loop directly emitting composables (ColorChip) inside a FlowRow -- NOT a LazyColumn/LazyRow with item keys, so there is no Compose list-recomposition-identity (key=) concern to preserve. A simple code-behind loop or non-virtualizing ItemsRepeater over the fixed MirrorKeys.all collection is sufficient in WinUI3; no virtualization/key infrastructure required.
- when (pf) { "__hard__" -> ...; "__soft__" -> ...; else -> ... } (lines 98-127): Kotlin exhaustive-branch when used to both select AND directly emit one of three differently-parameterized ColorPickerDialog(...) call overloads (different kigou/currentHex/defaultHex/onPick/onReset callbacks per branch). Translate as an if/else-if/else chain (or switch on a small enum) in code-behind that selects which dialog parameter set (title, current color, default color, save/reset/close callbacks) to use -- there is no single dialog signature to bind polymorphically.
- String interpolation with mandatory braces before Japanese text: "${count}件" (informational only, no C# equivalent risk) -- source comment (lines 78-80) explicitly documents that Kotlin treats Japanese characters as valid identifier-continuation characters, so "$count件" WITHOUT braces would silently parse as an undefined identifier `count件` (a real, previously-hit compile-failure bug class per project history, tag '3.290.0'). This is a Kotlin/JVM lexer quirk with zero analog in C# string interpolation ($"{count}件" is always unambiguous in C#) -- flag for the human translator as 'no action needed', not as something requiring a parallel guard in C#.
- @OptIn(ExperimentalLayoutApi::class) annotation on ColorSettingsView (line 45): marks that FlowRow was (at this Compose version) an experimental Foundation API deliberately adopted anyway -- purely a Compose-versioning signal, irrelevant to WinUI3, but indicates the wrap-layout requirement was considered important enough to accept an experimental dependency; pick a robust/well-supported WinUI3 wrapping control rather than something similarly fragile.

### 落とし穴

- This file used to be a much larger 'composite legacy screens' file. Its own header comments (lines 26-31) document that across versions 3.86.0 and 3.286.0, an unrendered/dead composite screen (also literally named `V6RemainingScreens`) plus HeaderBar, RingGauge, OverviewDashboard, FlagsView, OperatorLogView, BottomNav, and CheckSummaryView were all deleted as confirmed-zero-external-reference dead code. Only 2 composables survive: SectionSegment and ColorSettingsView. Do NOT treat this file's name as indicating it represents 'one screen' -- it is now a small settings-fragment file with an outdated/misleading name.
- SectionSegment is a generically-named, generically-signatured 'titled card section' helper, but grep across the entire app/src/main/java/com/magi/app tree confirms it has EXACTLY ONE call site in the whole codebase -- ColorSettingsView, in this same file. It is not shared/reused elsewhere despite looking like a reusable design-system primitive. Safe to inline into the ColorSettingsView translation, or keep as a small private local helper -- no need to expose it as a shared/public WinUI3 control.
- pickFam mixes TWO magic sentinel string literals, "__hard__" and "__soft__", together with real MirrorKeys.all family-key strings (presumably values like c1/c3n/covU/etc., not enumerated in this file) in the SAME nullable-String state variable. The `when` block special-cases the two sentinels before falling through to `else` for genuine family keys. This sentinel-in-a-plain-string state model (rather than e.g. a sealed class/discriminated union) must be reproduced faithfully -- e.g. as a nullable string, or as an enum with two extra non-family cases, in the WinUI3 translation -- to correctly route to the 3 different dialog-parameter branches.
- Color resolution is a documented 3-tier fallback (see comments lines 52-54, 63-65, 71-76, 94-97): tier 1 = explicit per-family override ui.violationFamilyColorHex[key] if present and non-blank ('custom'=true in that case); tier 2 = severity-derived baseline (baseHard for severity CRITICAL, baseSoft for HIGH/WARN); tier 3 = hardcoded gray '#8A979B' for INFO/other severities. The baselines themselves ALSO fall back one level: ui.violationColorHex.ifBlank{'#BA1A1A'} and ui.violationSoftColorHex.ifBlank{'#E08A1E'}. These 3 hardcoded hex literals (#BA1A1A red, #E08A1E amber, #8A979B gray) appear in BOTH the inline color-resolution logic AND as the `defaultHex` arguments passed into ColorPickerDialog (used there presumably for its own default/reset-highlighting behavior) -- keep all occurrences numerically consistent when translating; do not let them drift into two different constants.
- The `custom` boolean passed to ColorChip means two DIFFERENT things depending on which chip: for the per-family chips (first FlowRow) it answers 'is there a non-blank per-family override at all' (famHex != null); for the two baseline chips (second FlowRow) it answers 'has the user explicitly set this baseline away from its hardcoded default' (ui.violationColorHex.isNotBlank() / ui.violationSoftColorHex.isNotBlank()). Do not conflate these two semantically-different 'is this customized' questions when translating the ColorChip control's IsCustom-equivalent property.
- enabled = !ui.running is applied uniformly to EVERY ColorChip in both loops (5 occurrences total: once inside the MirrorKeys.all forEach, and twice for the two baseline chips) -- this is a single cross-cutting 'disable all color pickers while a long-running optimization job is active elsewhere in the app' gate, not a per-chip business rule. Must be wired in WinUI3 to whatever global 'IsOptimizationRunning'-equivalent flag exists, applied identically to every chip's IsEnabled.
- The 'N件' (item count) suffix is conditionally appended ONLY when count > 0: label = "..." + (if (count > 0) " ${count}件" else "") -- i.e. a zero count shows NO suffix at all (not '0件'). This exact display nuance is easy to lose if naively translated to an unconditional string-format call.
- Both FlowRow instances use Arrangement.spacedBy(8.dp) on BOTH the horizontalArrangement AND verticalArrangement parameters -- an 8dp gap in both the wrap (row) direction and the line-break (column) direction. A WinUI3 WrapPanel/ItemsRepeater-based replacement needs matching item spacing configured on BOTH axes to reproduce this look, not just one.
- The comment on lines 78-80 explaining Kotlin's Japanese-as-identifier-character lexical quirk (and the resulting mandatory `${var}` braces before adjoining Japanese text, referencing a real historical compile-failure at project version tag '3.290.0') is purely informational for a Kotlin reader and has literally zero bearing on the C#/WinUI3 translation -- C# string interpolation ($"{count}件") has no such ambiguity ever. Flag this as 'no action needed', not as a bug pattern to defensively replicate.
- File ends at line 129 (closing brace of ColorSettingsView) with a trailing blank line 130 -- purely cosmetic EOF whitespace, noted only because full-file coverage was required and to confirm nothing beyond it was missed.
- SectionSegment's `subtitle: String? = null` default-null optional parameter is never actually used with a non-null value anywhere in this file -- ColorSettingsView's only call, `SectionSegment("違反種別の色") { ... }`, relies on the default (omits subtitle entirely), so the subtitle-Text branch inside SectionSegment is dead code from this file's perspective (though the parameter itself is part of SectionSegment's general-purpose signature).

### 外部依存

- `UiState (fields read here: violationColorHex, violationSoftColorHex, violationFamilyColorHex, breakdown, running) -- data class defined elsewhere in the ui package (e.g. MagiUiState.kt per project history), not defined in this file`
- `MagiViewModel (methods invoked: setViolationColor, resetViolationColor, setViolationSoftColor, resetViolationSoftColor, setViolationFamilyColor, resetViolationFamilyColor) -- ViewModel class defined elsewhere, not in this file`
- `com.magi.app.v6.MirrorKeys -- imported object; `.all` is iterated as a collection of violation-family key strings (e.g. c1/c3n/covU/etc. per project history) -- defined in the v6 engine package, not in this file`
- `com.magi.app.v6.ShiftAppearance -- imported object; `.severityFromVioKey(key: String): String` is called to classify a family key's severity (returns strings compared against "CRITICAL"/"HIGH"/"WARN") -- defined in the v6 engine package, not in this file`
- `breakdownLabels -- referenced unqualified as a Map (breakdownLabels[key] / breakdownLabels[pf]) mapping violation-family keys to Japanese display labels; not imported explicitly so it resolves via same-package (com.magi.app.ui) visibility -- NOT defined in this file`
- `ColorChip -- a @Composable function invoked with (hex, label, custom, enabled, onClick=...) but its declaration is NOT present in this file -- external dependency in another ui-package file`
- `ColorPickerDialog -- a @Composable function invoked 3x with named params (kigou, currentHex, defaultHex, onPick, onReset, onClose) but its declaration is NOT present in this file -- external dependency in another ui-package file`
- `androidx.compose.* framework APIs used via import: Arrangement, ExperimentalLayoutApi, FlowRow, Column, Spacer, fillMaxWidth, height, padding, Card, MaterialTheme, Text (material3), Composable, getValue, mutableStateOf, remember, setValue (runtime), Modifier, FontWeight (ui.text.font), dp, sp (ui.unit) -- standard Jetpack Compose framework, not project code`

### 呼び出し元

- `ColorSettingsView: LIVE -- grep-confirmed external call site at MagiApp.kt:671 (`ColorSettingsView(ui, vm)`), inside the app's Settings tab, positioned directly after the 'shift display color' section per surrounding comments referencing versions 3.122.0->3.132.x IA-consolidation decisions. (Also merely mentioned in explanatory comments -- not calls -- at ShiftColorEditor.kt:149 and MagiSetupCards.kt:532.)`
- `SectionSegment: ORPHANED with respect to the rest of the codebase -- grep across app/src/main/java/com/magi/app finds it referenced ONLY within this same file: its own definition (line 34) and its single call site inside ColorSettingsView (line 61, same file). No other file in the app module calls SectionSegment. It survives only transitively because its sole caller, ColorSettingsView, is itself live.`


---

## MagiComponents.kt

### 役割

This file defines three small, stateless, reusable Compose UI primitives shared across MAGI's screens per its header KDoc: a segmented control (MagiSegmentedControl, spec §4.5), a large centered score/progress gauge (MagiScoreGauge, spec §4.6), and a colored pill/tag chip (MagiTagChip, spec §4.8), all implementing docs/magi_design_system.md §4. It builds on Material3 as the theme base while applying the app's 'Planner' visual taste (soft rounded pills, gentle tinted fills instead of hard white steps, large numerals, segmented controls) under a strict one-finger-operation touch-target constraint. It is not itself a screen/tab — it is a leaf component library with no ViewModel or UiState coupling, consumed by other screen-level composables (not present in this file) that pass in plain data/callback parameters.

### Composable関数

#### `MagiSegmentedControl`

```kotlin
fun MagiSegmentedControl(options: List<String>, selected: Int, onSelect: (Int) -> Unit, modifier: Modifier = Modifier)
```

**描画内容:** Renders a horizontal segmented control: an outer rounded Surface (surfaceVariant background, MaterialTheme.shapes.large) containing a 4dp-padded Row (4dp spacing, selectableGroup semantics) of equal-width pill Surfaces, one per string in `options`. The pill at index `selected` gets a primaryContainer background, onPrimaryContainer text, and semibold FontWeight; all other pills are transparent with onSurfaceVariant text and normal weight. Each pill is min 48dp tall, tappable (invokes onSelect(i)), and shows a single-line, ellipsis-truncating label (labelLarge) centered horizontally and vertically.


#### `MagiScoreGauge`

```kotlin
fun MagiScoreGauge(score: Int, max: Int = 100, label: String, sub: String? = null, accent: Color? = null, modifier: Modifier = Modifier)
```

**描画内容:** Renders a centered vertical column (8dp spacing): a bottom-aligned Row pairing a large bold score number (displaySmall, colored by `accent ?: cs.primary`) with a smaller '/ max' suffix (titleMedium, onSurfaceVariant); below it a full-width LinearProgressIndicator (min 8dp tall) whose fill ratio is `score/max` clamped to [0,1], tinted the same accent color with a surfaceVariant track. Beneath the bar it shows a centered `label` (titleSmall, onSurface) and, only if `sub` is non-null, a centered `sub` caption (bodySmall, onSurfaceVariant).


#### `MagiTagChip`

```kotlin
fun MagiTagChip(text: String, color: Color, modifier: Modifier = Modifier, leadingIcon: ImageVector? = null)
```

**描画内容:** Renders a small stadium/pill-shaped Surface (RoundedCornerShape(50)) with a 14%-alpha tint of `color` as background, a 1dp 30%-alpha border of the same `color`, and a WCAG-safe content color computed by ensureReadable() from the color pre-composited over the theme surface. Inside, a horizontally-arranged Row (4dp spacing, centered vertically, 11dp/6dp padding) optionally shows a leading ImageVector icon (min 18dp, no content description / decorative) followed by a single-line, semibold `text` label (labelLarge).


### Android/Compose固有パターン（要変換方針）

- modifier.weight(1f) in Row: MagiSegmentedControl per-option pill Surface (line 59): evenly distributes N segment pills across the row width for a variable, data-driven `options.size`; WinUI3 has no Row-weight equivalent for a dynamic item count — needs a Grid with programmatically generated star-sized ColumnDefinitions, or an ItemsRepeater/UniformGrid, not static XAML columns.
- Surface(onClick = { onSelect(i) }): MagiSegmentedControl option pill (line 56-57), and plain non-clickable Surface wrappers in MagiSegmentedControl's outer container (line 45) and MagiTagChip (line 127): Material3's clickable Surface bundles ripple feedback, click semantics, and touch-target sizing in one call; WinUI3 has no single equivalent — needs a Button with a stripped-down ControlTemplate/Style or a Border plus pointer event handlers, and ripple must be reproduced or intentionally omitted.
- heightIn(min = 48.dp) / heightIn(min = 8.dp) / heightIn(min = 18.dp): MagiSegmentedControl pill (line 59), MagiScoreGauge LinearProgressIndicator (line 107), MagiTagChip leading icon (line 140): sets a floor on size without capping the max, and per the inline comment at line 58-59 is an EXPLICIT workaround because Surface(onClick) does not auto-apply the Material minimum touch target in this usage; WinUI3's MinHeight is the direct analog but must be applied per-element and does not behave identically when content is naturally smaller.
- selectableGroup() (line 51) + semantics { this.selected = active } (line 59): MagiSegmentedControl: pure accessibility metadata marking the Row as a mutually-exclusive selection group and flagging each pill's selected state for screen readers; has zero visual effect and is easy to silently drop in a visual-only port. WinUI3 needs an AutomationPeer/AutomationProperties equivalent (or repurposing RadioButtons/SelectorItem semantics) to preserve parity.
- LinearProgressIndicator(progress = { ratio }, ...): MagiScoreGauge (line 105-110): uses the Compose 1.6+ state-lambda progress API (defers the read into the indicator's own recomposition scope); WinUI3 ProgressBar.Value is a plain bound double, not a deferred callback — needs an x:Bind/Binding to a recomputed property, not a lambda.
- color.copy(alpha = 0.14f).compositeOver(MaterialTheme.colorScheme.surface): MagiTagChip (line 126): manually pre-composites a translucent tint over the theme surface color into an opaque 'effective background', per the inline [UD監査] comment (lines 124-125) this exists specifically because raw accent color text on the 14%-alpha chip background fails WCAG contrast (measured 2.7:1 orange / 3.4:1 green); the result feeds ensureReadable() to pick a safe foreground. WinUI3's Color has no compositeOver — the src*srcA + dst*(1-srcA) per-channel blend must be reimplemented, and ensureReadable's own algorithm (not in this file) must also be ported for the fix to actually work.
- RoundedCornerShape(50) (line 132): MagiTagChip: a 50dp corner radius on a chip much shorter than 100dp tall, producing Compose's auto-clamped fully-rounded 'stadium/pill' look; WinUI3's CornerRadius does not clamp identically across controls — likely needs CornerRadius set to height/2 (or a large fixed value guaranteed to exceed it) to reproduce the same stadium silhouette.
- Default Kotlin parameter values — max: Int = 100, sub: String? = null, accent: Color? = null, modifier: Modifier = Modifier (MagiScoreGauge), leadingIcon: ImageVector? = null, modifier: Modifier = Modifier (MagiTagChip), modifier: Modifier = Modifier (MagiSegmentedControl): call sites can omit these entirely in Kotlin; XAML/C# has no direct declarative equivalent — needs DependencyProperty defaults, overloads, or explicit null-coalescing fallback logic (MagiScoreGauge already models the pattern itself via `accent ?: cs.primary` at line 94).
- forEachIndexed building UI nodes imperatively inside a @Composable function body: MagiSegmentedControl (line 54, iterating `options`): each loop iteration directly emits a Surface node — this is manual imperative UI construction, not a bound ItemsControl. A WinUI3 port must choose between looping in code-behind to build children into a Grid/StackPanel, or binding an ItemsRepeater/ItemsControl to an ObservableCollection<string> with an inline DataTemplate that reproduces the per-item 'is this index == selected' branching logic.
- MaterialTheme.colorScheme.* / MaterialTheme.typography.* / MaterialTheme.shapes.* theme-token reads scattered across all three composables (e.g. lines 44,47,61-64,93-94,102-103,105-111,126,131): per CLAUDE.md the actual token source of truth is `MainActivity.MagiTheme` (color/typography/shape) plus `MagiTokens.kt` (semantic/shift colors, spacing) — NEITHER file was read for this task, so concrete hex/sp/dp values are not visible here; the C# port must pull resolved values from those external sources rather than invent placeholders.

### 落とし穴

- Doc-comment/code mismatch on minimum sizes: the file header (lines 30-34) states the one-finger-operation constraint as 'タップ標的は最小48dp、セグメントは最小44dp高' (tap targets min 48dp, SEGMENTS min 44dp height), but the actual MagiSegmentedControl code enforces `heightIn(min = 48.dp)` (line 59) for its segment pills, not 44dp. The C# port should follow the code's 48dp, not the doc comment's 44dp claim.
- LinearProgressIndicator uses the newer Compose 1.6+ lambda-based `progress = { ratio }` parameter (line 106), which defers the value read into the indicator's own draw/recomposition scope for perf. This is NOT a plain double; a naive translator might read it as `progress = ratio` (older API) — functionally similar end result in WinUI3 (bind ProgressBar.Value to a recomputed double), but the Kotlin call shape itself must not be copied literally as a value assignment.
- `compositeOver` (line 126) manually pre-blends a 14%-alpha color over the theme's `surface` color BEFORE passing it to `ensureReadable()` — this two-step process (alpha-blend to get 'effective background', THEN compute a readable foreground against that effective background) is easy to collapse incorrectly into a single alpha-blend or single contrast-check step; both steps must be preserved distinctly, and the alpha value (0.14f) must match between the Surface's actual `color` param (line 130, also 0.14f) and the value used to compute `chipBg` (line 126) or the contrast fix will be computed against the wrong effective background.
- The chip's Surface `color` (line 130, `color.copy(alpha = 0.14f)`) is set independently from the pre-computed `chipBg` (line 126, same 0.14f composited over surface) used only to derive `contentColor` — these are two separate Color values in the code (one translucent for the Surface itself which Compose composites live at render time, one manually pre-composited opaque approximation used just for the contrast calculation). A translator must not conflate them into a single variable; WinUI3 has no live alpha-compositing against whatever is underneath a Border/Surface the way Compose's `Surface(color=...)` does, so the actual visual background may need to be manually pre-blended for WinUI3 too, not just the contrast-check copy.
- `Icon(it, contentDescription = null, ...)` (line 140) intentionally passes a null content description — this is decorative/redundant icon content deliberately excluded from the accessibility tree (the adjacent text already conveys the meaning). Must be preserved as such in WinUI3 (e.g. AutomationProperties.AccessibilityView="Raw" or an empty/omitted AutomationProperties.Name), not 'fixed' by adding alt text.
- `selectableGroup()` + `semantics { this.selected = active }` (lines 51, 59) are accessibility-only annotations with zero visual effect in Compose — a screenshot-driven or visual-only port would completely miss them since they render nothing, but they are required for screen-reader parity (announcing the segmented control as a mutually-exclusive group and each pill's selected state).
- None of the three composables in this file take a Compose 'slot' content lambda (`content: @Composable () -> Unit`) — they are pure leaf/data-driven components. This contrasts with other MAGI components referenced elsewhere in the codebase's history (e.g. CollapsibleSection) that DO use content slots; a reader should not assume a ContentPresenter/ContentControl templating pattern is needed for MagiSegmentedControl/MagiScoreGauge/MagiTagChip specifically — plain property binding suffices for these three.
- Inline Japanese comments encode a history of iterative accessibility/legibility fixes that should be preserved as code comments (not silently dropped) since they explain WHY specific numeric values were chosen: line 58 explains why heightIn(48dp) is explicit despite Surface(onClick) normally auto-applying it; lines 124-125 give the exact measured contrast failure ratios (2.7:1 orange, 3.4:1 green) that motivated the compositeOver fix; line 69 explains the horizontal/vertical padding choice is to prevent hard-clipping of labels at 320dp width or with enlarged system fonts; line 141 notes the tag chip text size was bumped from 13sp to 15sp for legibility — losing these comments in translation would make future C#/WinUI3 maintainers unable to tell which values are load-bearing accessibility fixes vs. arbitrary styling choices.
- `Row(verticalAlignment = Alignment.Bottom)` in MagiScoreGauge (line 101) bottom-aligns the large score digit and the smaller '/ max' suffix text, which are two different typography styles/sizes (displaySmall vs titleMedium) — Compose's Bottom alignment here aligns layout-box bottoms, not text baselines; a literal WinUI3 StackPanel(Orientation=Horizontal, VerticalAlignment=Bottom) on two differently-sized TextBlocks approximates but does not guarantee identical true-baseline text alignment — verify visually rather than assuming pixel parity.

### 外部依存

- `ensureReadable(background: Color, foreground: Color): Color — WCAG-contrast-safe foreground/text-color helper invoked in MagiTagChip (line 131); referenced without an import so it must be defined elsewhere in the same package (com.magi.app.ui), but its implementation/algorithm is not present in this file and was not read for this task.`
- `docs/magi_design_system.md §4.5 / §4.6 / §4.8 — external design-spec document named in the file's header KDoc (lines 30-34, 36, 83, 116) as the source-of-truth spec these three composables implement; not read for this task.`
- `MainActivity.MagiTheme and MagiTokens.kt — per CLAUDE.md these are the primary sources for MaterialTheme color/typography/shape tokens and semantic/spacing tokens consumed throughout this file (MaterialTheme.colorScheme/typography/shapes); named for context only, not read for this task.`


---

## StaffManageCard.kt

### 役割

StaffManageCard is a "職員管理" (staff management) card presenting a flat list of all staff members with per-row hire-date/rename/group editing, skill-group assignment via dropdown, and delete (退職/leave) with confirmation, plus an "入職" (hire/add) action at the bottom. Per an explicit design decision (3.114.0) noted in the file's KDoc, it deliberately duplicates functionality also present in a separate Ws1Card staff section (same ViewModel API) as an intentionally-coexisting alternate view; per-staff numeric shift-count ranges are explicitly out of scope here and live in a separate CountsCard component.

### Composable関数

#### `StaffManageCard`

```kotlin
fun StaffManageCard(ui: UiState, vm: MagiViewModel) [annotated @Composable; no explicit return type written, implicit Unit]
```

**描画内容:** A single Card containing: a header Text showing staff count, then one Row per staff member (name + group kigou label, optional skill-assignment dropdown button, an edit button, and — unless this is the last remaining staff member — a delete button), a warning Text shown only when exactly one staff member remains explaining deletion is blocked, and an 'add staff' button at the bottom. It also conditionally renders (outside the Card) up to one of: an edit StaffDialog, an add StaffDialog, or a delete-confirmation AlertDialog, depending on local state.

**ViewModel呼出:**

- `vm.ws1()`
- `vm.skillGroups()`
- `vm.setStaffSkill(i, -1)`
- `vm.setStaffSkill(i, gi)`
- `vm.ws1EditStaff(i, n, gi)`
- `vm.ws1AddStaff(n, gi)`
- `vm.ws1RemoveStaff(i)`

**読むUiStateフィールド:**

- `ui.running`
- `ui.loaded`

**Compose固有ローカル状態:**

- edit: MutableState<Triple<Int, String, Int>?> = remember { mutableStateOf(null) } — holds (staffIndex, currentName, currentGroupIdx) for the currently-open edit dialog; null means no edit dialog shown
- addOpen: MutableState<Boolean> = remember { mutableStateOf(false) } — whether the 'add new staff' dialog is shown
- confirmDelete: MutableState<Int?> = remember { mutableStateOf(null) } — staff index pending delete confirmation; null means no confirmation dialog shown
- open: MutableState<Boolean> = remember { mutableStateOf(false) } — declared INSIDE the forEachIndexed loop over v.staff, one independent instance per row, controlling that row's skill-assignment DropdownMenu expanded/collapsed state


### ダイアログ/オーバーレイ

- Edit-staff StaffDialog — title "職員の編集（改名・所属）", pre-filled with (nm=st.name, gi0=st.groupIdx) and the current group list (v.groups.map { toHankakuKigou(it.kigou) }); triggered by either tapping the row (clickable) or pressing its EditRowButton, both setting edit = Triple(i, st.name, st.groupIdx); on confirm calls vm.ws1EditStaff(i, n, gi) then sets edit = null; on cancel sets edit = null.
- Add-staff StaffDialog — title "入職（職員追加）", initial values name="" and groupIdx=0, same group list as above; triggered by the bottom AddRowButton (enabled only when ui.loaded && !ui.running) setting addOpen = true; on confirm calls vm.ws1AddStaff(n, gi) then sets addOpen = false; on cancel sets addOpen = false.
- Delete-confirmation AlertDialog — triggered by a row's DeleteRowButton setting confirmDelete = i (only rendered per-row when v.staff.size > 1); title rendered via DialogHeader("退職・削除の確認", { confirmDelete = null }); body text re-reads the staff name at index i at render time: "${v.staff.getOrNull(i)?.name ?: \"\"} を削除します。この職員の勤務・希望も消えます。"; confirmButton is DialogDangerButton("削除（退職）") calling vm.ws1RemoveStaff(i) then confirmDelete = null; dismissButton is DialogDismissButton setting confirmDelete = null; onDismissRequest also sets confirmDelete = null.
- Per-row skill-assignment DropdownMenu (anchored via Box to an OutlinedButton showing the current skill kigou or "(なし)") — only rendered when skills.isNotEmpty(); expanded state is the row-local `open` var; contains a leading "(なし)" item calling vm.setStaffSkill(i, -1) then closing, followed by one item per skill group ({kigou}  {name}) calling vm.setStaffSkill(i, gi) then closing; onDismissRequest closes it without changing selection.

### Android/Compose固有パターン（要変換方針）

- remember { mutableStateOf(...) } for edit/addOpen/confirmDelete: three separate local UI-only state holders on the composable root (Triple<Int,String,Int>?, Boolean, Int?) — need translation to observable backing fields/DependencyProperties or code-behind fields since WinUI3 has no Compose recomposition model.
- remember { mutableStateOf(false) } for `open` declared INSIDE the `v.staff.forEachIndexed { i, st -> ... }` loop body (per-row skill-dropdown open/closed flag): each row gets its own independently-remembered state via Compose's slot table tied to loop position. In WinUI3 this per-row transient state must become either a locally-scoped Flyout (which manages its own open state without a bound property — simplest) or a per-row view-model property (e.g. IsSkillMenuOpen) if using a virtualizing ItemsRepeater/ListView with recycled templates.
- Conditional composable mounting: `edit?.let { (i,nm,gi0) -> StaffDialog(...) }`, `if (addOpen) StaffDialog(...)`, `confirmDelete?.let { i -> AlertDialog(...) }` — dialogs exist in the tree only while their driving state is non-null/true (Compose mounts/unmounts them), unlike WinUI3 ContentDialogs which are typically shown imperatively via `await dialog.ShowAsync()`; must preserve the 'exactly one dialog visible at a time, driven by nullable/boolean state' semantics rather than always-present-but-Visibility-collapsed controls.
- Destructuring `edit?.let { (i, nm, gi0) -> ... }` on a Kotlin Triple<Int,String,Int> — needs an explicit tuple/record/3-field equivalent in C#.
- Modifier.clickable(enabled = !ui.running) applied to the whole Row (not just a button) so tapping anywhere on a staff row opens the edit dialog — in WinUI3 requires either a Button-wrapped row or manual Tapped/PointerPressed handling with IsHitTestVisible/IsEnabled toggled by the running flag.
- Box { OutlinedButton + DropdownMenu } anchored-menu pattern for skill assignment — maps to a Button with an attached Flyout/MenuFlyout in WinUI3; DropdownMenuItem entries map to MenuFlyoutItem entries.
- Column(Modifier.weight(1f)) inside a Row for the name/group text block that should fill remaining space alongside fixed-size trailing controls — maps to a Grid with a star-sized column (or equivalent proportional layout), not a StackPanel.
- heightIn(min = 48.dp) used for touch-target sizing on the row, the skill OutlinedButton, and implicitly on EditRowButton/DeleteRowButton (external) — maps to MinHeight="48" in WinUI3, consistent with the app's minimum-touch-target design convention.
- MaterialTheme.colorScheme.onSurfaceVariant and MaterialTheme.colorScheme.error, plus MaterialTheme.typography.titleMedium/bodyMedium/labelSmall — must map to the app's established WinUI3 theme resource brushes/text styles (the app's MagiTheme/MagiTokens equivalents), not hardcoded colors.
- Early-return guard `val v = vm.ws1() ?: return` at the top of the @Composable: the ENTIRE card renders nothing when null, not just a sub-element — in XAML/code-behind this is a guard that collapses/omits the whole UserControl's content, not a per-field null check.
- String templates with exact Japanese spacing/punctuation must be preserved verbatim, e.g. "職員一覧（${v.staff.size}名）", "グループ $gk", "${sg.kigou}  ${sg.name}" (note the double space between kigou and name), and the delete-confirmation sentence "${v.staff.getOrNull(i)?.name ?: ""} を削除します。この職員の勤務・希望も消えます。".
- Null-safe chained fallbacks: v.groups.getOrNull(st.groupIdx)?.kigou?.let { toHankakuKigou(it) } ?: "?"; skills.getOrNull(st.skillIdx)?.kigou ?: "(なし)"; v.staff.getOrNull(i)?.name ?: "" — exact fallback strings ("?", "(なし)", "") and bounds-safe/negative-index-safe lookup semantics (Kotlin getOrNull returns null for out-of-range including negative index) must be replicated exactly.

### 落とし穴

- The `open` DropdownMenu state is `remember`ed inside the `forEachIndexed` loop with no explicit `key()` — relies on Compose's positional slot-table memory per loop iteration. The list itself is a plain Column (not LazyColumn), so all rows exist in composition simultaneously and this works fine in Compose; but if translated to a virtualizing WinUI3 list control (ItemsRepeater/ListView with recycled DataTemplates), per-row transient state does not survive recycling the same way — prefer a self-contained Flyout (owns its own open state) over a bound per-row boolean.
- Tapping the row (Modifier.clickable on the whole Row) and pressing the explicit EditRowButton do the EXACT same action (edit = Triple(i, st.name, st.groupIdx)) — this is intentional redundancy (large tap target + explicit visible affordance), not a bug to deduplicate; both entry points must be preserved.
- ALL tracking (edit, delete, skill-assignment) is by raw positional list index `i` from forEachIndexed, not by a stable staff ID. The ViewModel methods (ws1EditStaff(i,...), ws1RemoveStaff(i), setStaffSkill(i,...)) all take this positional index — must not be swapped for ID-based lookups in the C# port, since that would change semantics.
- The delete-confirmation dialog's displayed name is re-read live from `v.staff.getOrNull(i)?.name` at render time (not captured at the moment the delete button was pressed) — so it reflects the list state at dialog-render time, not click time.
- `if (v.staff.size > 1)` guards rendering of the DeleteRowButton and is evaluated identically on every row against the aggregate list size (not per-item) — so once only 1 staff member remains, no row would show a delete button (moot since only 1 row exists then), and the explanatory Text ("最後の1名は削除できません…") appears once below the whole list, not per-row. This explanatory text (added per code comment referencing fix note 3.409.11, mirroring a similar fix elsewhere for group/shift deletion) exists specifically so the user understands WHY the delete button vanished — the disabled/absent state must be paired with this explanation, not just silently omitted.
- The skill-assignment button+dropdown block is entirely OMITTED from a row's layout (not merely hidden) when `skills.isEmpty()` — this changes the row's element count/spacing, so the WinUI3 layout must correctly reflow without leftover gaps when this section is absent, rather than just setting Visibility=Collapsed on a reserved-space element.
- `enabled = !ui.running` gates row click, EditRowButton, DeleteRowButton, and the skill OutlinedButton uniformly — while `AddRowButton`'s enabled condition is the compound `ui.loaded && !ui.running` (an extra loaded-check not applied to the per-row actions); preserve this asymmetry exactly rather than normalizing all buttons to the same condition.
- Skill index -1 (the 'no skill' sentinel, set by the "(なし)" DropdownMenuItem) and any other out-of-range skillIdx both resolve to displaying "(なし)" via Kotlin's `getOrNull` (which returns null for negative or out-of-bounds indices) chained with `?: "(なし)"` — replicate this bounds-and-sign-safe lookup, not just a simple array index.
- StaffDialog is reused with different parameters for both 'edit' and 'add' flows (different title, different initial name/groupIdx, and routed to different ViewModel calls on confirm) — treat as one parameterized dialog type with two call sites, not two separate dialog implementations, since its internal behavior (not visible in this file) is presumably shared.
- Only one of the three overlays (edit dialog, add dialog, delete-confirm dialog) can be open at a time in practice (each is driven by independent state with no explicit mutual exclusion enforced in code) — the code does not prevent theoretically opening more than one, so if the C# port enforces stricter mutual exclusion it would be a behavior change; port the independent nullable/boolean gating as-is.

### 外部依存

- `toHankakuKigou (top-level function from com.magi.app package, converts full-width kigou characters to half-width for display) — called on every group kigou and every staff row's group label`
- `UiState (parameter type `ui`, presumably from MagiUiState.kt) — only .running and .loaded are read here`
- `MagiViewModel (parameter type `vm`) — methods used: ws1(), skillGroups(), setStaffSkill(Int, Int), ws1EditStaff(Int, String, Int), ws1AddStaff(String, Int), ws1RemoveStaff(Int)`
- `StaffDialog (composable, not defined in this file; used twice with different title/initial-values/confirm-callback for edit vs add)`
- `EditRowButton (composable, shared row-action button, takes onClick and enabled)`
- `DeleteRowButton (composable, shared row-action button, takes onClick and enabled)`
- `AddRowButton (composable, shared bottom action button, takes label text, onClick, enabled)`
- `DialogDangerButton (composable, destructive confirm button for AlertDialog.confirmButton)`
- `DialogDismissButton (composable, cancel button for AlertDialog.dismissButton)`
- `DialogHeader (composable, dialog title row with an embedded close affordance, takes title text and a dismiss callback)`
- `vm.ws1() return type's fields: v.staff (list, elements have .name: String, .groupIdx: Int, .skillIdx: Int), v.groups (list, elements have .kigou: String)`
- `vm.skillGroups() return type's elements: sg.kigou: String, sg.name: String`
- `This card is documented (KDoc) as intentionally coexisting with a separate Ws1Card staff section elsewhere in the codebase (design decision 3.114.0) that calls the SAME vm.ws1EditStaff/ws1AddStaff/ws1RemoveStaff/setStaffSkill methods for a different layout — that other call site is not in this file but must be accounted for when planning the ViewModel-equivalent's API surface.`
- `Individual staff count-range editing is explicitly out of scope here and lives in a separate CountsCard/StaffRangeSection component (annual-master section ③), per the file's KDoc.`


---

