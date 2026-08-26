package com.magi.app.ui

import org.junit.Assert.assertEquals
import org.junit.Assert.assertSame
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * [3.459.0/分析タブ統合] `applyVioFilter` の不変条件を固定する。
 *
 * ①全ON（`allVioBucketKeys`）なら**参照そのまま**返す＝既定表示（フィルタ無し）と完全に同一の挙動。
 * ②多重family（1セルに複数の違反クラスが重なる、`violationCellFamilies`/`needFamilies`/`countFamilies`）は
 *   **どれか1つでも表示中バケツに属せば残す**（`visibleCellVio` と同じ規約。単一クラス側の値だけを見ると
 *   誤って落としてしまう）。
 * ③`breakdown` はバケツ対象外の族（fair/weekly）を無条件に残す。
 * ④`distLocations`（fair/weekly の場所）はフィルタの対象外＝一切触らない。
 */
class ViolationHubFilterTest {
    @Test
    fun allEnabledReturnsTheSameInstance() {
        val ui = UiState(violationCells = mapOf("0,0" to "vio-pref"))
        assertSame("全バケツONなら早期returnで参照そのまま", ui, applyVioFilter(ui, allVioBucketKeys))
    }

    @Test
    fun cellsSurviveIfAnyOverlappingFamilyIsVisible() {
        val ui = UiState(
            violationCells = mapOf(
                "a" to "vio-pref",       // 単独: pref バケツ
                "b" to "vio-c1",         // 単独: window バケツ
                // 単一クラスは重い groupViol(group バケツ) だが、families には軽い c1(window バケツ) も重なる。
                "c" to "vio-groupViol",
            ),
            violationCellFamilies = mapOf(
                "a" to listOf("vio-pref"),
                "b" to listOf("vio-c1"),
                "c" to listOf("vio-groupViol", "vio-c1"),
            ),
        )
        // window だけ有効＝ a(pref)は消え、b(window単独)は残り、c(groupは無効だがwindowが重なる)も残る。
        val filtered = applyVioFilter(ui, setOf("window"))
        assertEquals(setOf("b", "c"), filtered.violationCells.keys)
        assertEquals(setOf("b", "c"), filtered.violationCellFamilies.keys)
    }

    @Test
    fun needAndCountMapsAreFilteredTheSameWay() {
        val ui = UiState(
            needViolations = mapOf("0,1" to "vio-covU", "0,2" to "vio-covO"),
            needFamilies = mapOf("0,1" to listOf("vio-covU"), "0,2" to listOf("vio-covO")),
            countViolations = mapOf("0,0" to "vio-low", "1,0" to "vio-apt"),
            countFamilies = mapOf("0,0" to listOf("vio-low"), "1,0" to listOf("vio-apt")),
        )
        val filtered = applyVioFilter(ui, setOf("need"))   // 人員(need)バケツのみ有効
        assertEquals(setOf("0,1", "0,2"), filtered.needViolations.keys)
        assertTrue("回数(count)バケツは無効なので countViolations は空", filtered.countViolations.isEmpty())
        assertTrue(filtered.countFamilies.isEmpty())
    }

    @Test
    fun breakdownKeepsBucketlessFamiliesRegardlessOfFilter() {
        val ui = UiState(breakdown = mapOf("pref" to 5, "c1" to 3, "fair" to 2, "weekly" to 1))
        val filtered = applyVioFilter(ui, setOf("pref"))
        // pref バケツのみ有効＝ c1(window) は消え、fair/weekly(バケツ対象外) は常に残る。
        assertEquals(mapOf("pref" to 5, "fair" to 2, "weekly" to 1), filtered.breakdown)
    }

    @Test
    fun distLocationsIsNeverTouched() {
        val locs = mapOf("fair" to listOf(listOf(0, 1, 3)))
        val ui = UiState(distLocations = locs, violationCells = mapOf("a" to "vio-pref"))
        val filtered = applyVioFilter(ui, setOf("window"))   // pref を含まないバケツ集合でも distLocations は不変
        assertSame(locs, filtered.distLocations)
    }
}
