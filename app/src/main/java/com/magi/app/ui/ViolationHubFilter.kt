package com.magi.app.ui

/**
 * [3.459.0/分析タブ統合] `ViolationHubCard`（分析タブの統合カード）が、勤務表タブと共有する
 * 族フィルタ(`vioEnabled`)を「一覧／日別・人別／内訳」の3ビューへ一様に適用するための
 * Compose 非依存フィルタ。`VioBucketsTest` と同じ理由でロジックだけをここへ切り出してある。
 *
 * 元の3枚のカード（旧 ConfirmListCard/AttentionCardsSection/BreakdownCard）は、いずれも
 * `violationCells`/`needViolations`/`countViolations`（＋各 `*Families`/`breakdown`）という
 * 同じ3系統のデータを別々の切り口で読んでいるだけだった。ここで**フィルタ済みの `UiState` を1つ**
 * 作れば、3つの表示ロジック（`ConfirmListBody`/`AttentionBody`/`BreakdownBody`）は元のまま
 * （中身は1文字も変えていない）で、族フィルタが3ビューへ一様に効く。
 *
 * バケツ対象外の族（fair/weekly＝`distLocations` 由来、`vioBucketlessFamilies`）は `vioVisible` の
 * 規約どおり常に通す（フィルタしない・触らない）。表示のみ・スコアリング/エンジンは完全に不変。
 */
internal fun applyVioFilter(ui: UiState, enabled: Set<String>): UiState {
    if (enabled == allVioBucketKeys) return ui   // 全ON＝無変更（既定表示と完全一致・早期return）
    fun keep(cls: String) = vioVisible(cls, enabled)
    fun famsOf(single: Map<String, String>, fams: Map<String, List<String>>, key: String): List<String> =
        fams[key] ?: listOfNotNull(single[key])
    fun filterSingle(single: Map<String, String>, fams: Map<String, List<String>>): Map<String, String> =
        single.filterKeys { key -> famsOf(single, fams, key).any(::keep) }
    fun filterFams(single: Map<String, String>, fams: Map<String, List<String>>): Map<String, List<String>> =
        fams.filterKeys { key -> famsOf(single, fams, key).any(::keep) }
    val breakdown = ui.breakdown.filterKeys { fam -> bucketOfFamily(fam) == null || bucketOfFamily(fam) in enabled }
    return ui.copy(
        violationCells = filterSingle(ui.violationCells, ui.violationCellFamilies),
        violationCellFamilies = filterFams(ui.violationCells, ui.violationCellFamilies),
        needViolations = filterSingle(ui.needViolations, ui.needFamilies),
        needFamilies = filterFams(ui.needViolations, ui.needFamilies),
        countViolations = filterSingle(ui.countViolations, ui.countFamilies),
        countFamilies = filterFams(ui.countViolations, ui.countFamilies),
        breakdown = breakdown,
        // distLocations（fair/weekly）は意図的に不変＝バケツ対象外は常に表示という規約と一致させる。
    )
}
