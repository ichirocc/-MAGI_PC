package com.magi.app.ui

import com.magi.app.v6.MirrorKeys
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * [3.382.0/R-08] 族を追加したとき E7 フィルタが**静かに壊れる**のを防ぐ。
 *
 * `bucketOfFamily` はバケツに属さない族に対し `null` を返し、`vioVisible` はそれを「常に表示」として扱う。
 * つまり `MirrorKeys.all` に20族目を足して `vioBuckets` にも `vioBucketlessFamilies` にも入れ忘れると、
 * その族は**フィルタで絞れないのに例外も警告も出ない**（利用者から見れば「消したのに消えない違反」）。
 *
 * ここで固定するのは「分類表と `MirrorKeys.all` が過不足なく一致する」という1つの不変条件だけ。
 * 実際に 3.72.0 で weekly が19族目として追加された経緯があり、同じことは今後も起こる。
 */
class VioBucketsTest {
    @Test
    fun everyViolationFamilyIsEitherBucketedOrExplicitlyBucketless() {
        val bucketed = vioBuckets.flatMap { it.families }.toSet()
        val all = MirrorKeys.all.toSet()

        // ① 族が重複して複数バケツに入っていない（入ると件数チップが二重に数える）。
        assertEquals("同じ族が複数のバケツに入っている", vioBuckets.sumOf { it.families.size }, bucketed.size)

        // ② 分類漏れ＝新しい族を足したのにどちらにも入れていない（静かに「常に表示」へ落ちる）。
        assertEquals(
            "MirrorKeys.all にあるのに vioBuckets にも vioBucketlessFamilies にも無い族",
            emptySet<String>(), all - bucketed - vioBucketlessFamilies,
        )

        // ③ 逆方向＝実在しない族名（改名・タイプミス）がバケツに残っている。
        assertEquals("MirrorKeys.all に無い族がバケツに入っている", emptySet<String>(), bucketed - all)
        assertEquals("MirrorKeys.all に無い族が除外リストに入っている", emptySet<String>(), vioBucketlessFamilies - all)

        // ④ バケツキーの重複なし（トグル状態が別バケツと混線する）。
        assertEquals("バケツキーが重複している", vioBuckets.size, allVioBucketKeys.size)
    }

    /** 分類の意味論そのもの（バケツ対象外＝常に表示・aptLow/aptHigh は apt に畳む）も併せて固定する。 */
    @Test
    fun bucketlessFamiliesAreAlwaysVisibleAndAptVariantsFoldIntoApt() {
        for (f in vioBucketlessFamilies) {
            assertNull("場所を持たない族はバケツに属さない: $f", bucketOfFamily(f))
            assertTrue("バケツ対象外は絞り込みの影響を受けない: $f", vioVisible("vio-$f", emptySet()))
        }
        assertEquals("apt", familyOfVioClass("vio-aptLow"))
        assertEquals("apt", familyOfVioClass("vio-aptHigh"))
        assertEquals("count", bucketOfFamily(familyOfVioClass("vio-aptHigh")))
        assertTrue("バケツONなら表示", vioVisible("vio-covU", setOf("need")))
        assertTrue("バケツOFFなら非表示", !vioVisible("vio-covU", setOf("pref")))
    }
}
