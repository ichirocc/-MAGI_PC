package com.magi.app.model

import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test

/**
 * [外部レビュー P2-02] `mapObjects`/`mapArrays` は要素がオブジェクト/配列でなければ黙って読み飛ばしていた
 * （null・数値・文字列の混入で staff/schedule 等が本来より短いリストへ静かに変わる）。
 * 明示的な失敗（IllegalArgumentException）へ変えたことをここで固定する。
 */
class StateParserTest {

    // 最小の妥当な JSON（staff 2件・shifts 1件）。壊れていない入力は従来どおり通ることの対照。
    private val validJson = """
        {
          "shifts": [{"name":"日勤","kigou":"日","need1":"1","need2":""}],
          "groups": [{"name":"A","kigou":"A"}],
          "staff": [{"name":"田中","groupIdx":0,"skillIdx":0},{"name":"鈴木","groupIdx":0,"skillIdx":0}]
        }
    """.trimIndent()

    @Test
    fun wellFormedArraysParseNormally() {
        val st = StateParser.parse(validJson)
        assertEquals(2, st.staff.size)
        assertEquals(1, st.shifts.size)
        assertEquals(1, st.groups.size)
    }

    @Test
    fun nullElementInStaffArrayThrowsInsteadOfSilentlyShrinking() {
        // staff[1] が null（配列としては2要素だが妥当なオブジェクトは1件だけ）。
        val corrupted = """
            {
              "shifts": [{"name":"日勤","kigou":"日","need1":"1","need2":""}],
              "groups": [{"name":"A","kigou":"A"}],
              "staff": [{"name":"田中","groupIdx":0,"skillIdx":0}, null]
            }
        """.trimIndent()
        val e = assertThrows(IllegalArgumentException::class.java) { StateParser.parse(corrupted) }
        // 旧実装ならここで例外が飛ばず staff.size==1 の「別の（縮んだ）勤務表」として静かに読み込めていた。
        assert(e.message?.contains("staff") == true) { "message should name the broken field: ${e.message}" }
    }

    @Test
    fun numberElementInScheduleRowArrayThrows() {
        // schedule の行そのもの（配列であるべき）が数値になっている壊れたケース。
        val corrupted = """
            {
              "shifts": [{"name":"日勤","kigou":"日","need1":"1","need2":""}],
              "groups": [{"name":"A","kigou":"A"}],
              "staff": [{"name":"田中","groupIdx":0,"skillIdx":0}],
              "schedule": [[0], 5]
            }
        """.trimIndent()
        val e = assertThrows(IllegalArgumentException::class.java) { StateParser.parse(corrupted) }
        assert(e.message?.contains("schedule") == true) { "message should name the broken field: ${e.message}" }
    }
}
