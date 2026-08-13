package com.magi.app.v6

import com.magi.app.model.C1Row
import com.magi.app.model.Group
import com.magi.app.model.MagiState
import com.magi.app.model.Range
import com.magi.app.model.Shift
import com.magi.app.model.Staff
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * [初期解生成(賢い版), 新設] `SmartInitialScheduler`単体の検証。
 * 「希望→C1→必要人数→個人下限→残り埋め」の順で、既存`GreedyMirrorScheduler`(C1非考慮)より
 * C1充足に優れることを確認する。
 */
class SmartInitialSchedulerTest {
    private fun blankState(cons1: List<C1Row> = emptyList()): MagiState = MagiState(
        startDate = "2026-01-01", endDate = "2026-01-11",
        shifts = listOf(Shift("休", "休", "", ""), Shift("X", "X", "", "")),
        groups = listOf(Group("G", "G")),
        staff = listOf(Staff("a", 0)),
        use2Patterns = false,
        groupShift = listOf(listOf(1, 1)),
        groupShiftApt = listOf(listOf("", "")),
        schedule = listOf(List(11) { -1 }),
        wishes = emptyMap(),
        staffRange = emptyMap(),
        needDay1 = emptyMap(), needDay2 = emptyMap(),
        cons1 = cons1, cons2 = emptyList(), cons3 = emptyList(),
        cons3n = emptyList(), cons3m = emptyList(), cons3mn = emptyList(),
        cons41 = emptyList(), cons42 = emptyList(),
    )

    @Test
    fun satisfiesC1FromBlankWhereGreedyMirrorSchedulerFails() {
        // [3.345.0] 対照の窓ルールを「5日窓 X>=2」→「3日窓 X>=2」へ。休を優先する restBonus を外した結果、
        //   簡易作成は最少回数のシフトを選び続けて 休/X の交互になり、緩い 5日窓 X>=2 は偶然満たしてしまう。
        //   3日窓 X>=2 は交互配置では満たせない（窓に X が1つしか入らない）ため対照として成立する。
        val st = blankState(cons1 = listOf(C1Row(day1 = "3", shiftKigou = "X", day2 = "2")))

        val smart = SmartInitialScheduler.generate(st)
        assertEquals(0, smart.report.breakdown["c1"] ?: -1)
        assertEquals(0, smart.report.hard)

        // 対照: 既存の簡易作成(C1非考慮)は同じ盤面でc1を解消できない。
        val naive = GreedyMirrorScheduler.generate(st)
        assertTrue("既存の簡易作成はC1を考慮しないため違反が残るはず", (naive.report.breakdown["c1"] ?: 0) > 0)
    }

    @Test
    fun respectsFeasibleWish() {
        val st = blankState(cons1 = listOf(C1Row(day1 = "5", shiftKigou = "X", day2 = "2")))
            .let { it.copy(wishes = mapOf("0,2" to 1)) }   // shift index 1 = "X"

        val result = SmartInitialScheduler.generate(st)
        assertEquals(1, result.newScheduleXAt(0, 2))
    }

    @Test
    fun isNoOpFriendlyWhenNoCons1Rules() {
        val st = blankState()
        val result = SmartInitialScheduler.generate(st)
        // C1規則が無くても正常に完成盤面を返す(空きセルが残らない)。
        assertTrue(result.schedule[0].all { it in 0..1 })
    }

    @Test
    fun satisfiesMultipleC1RulesOnSameShiftSimultaneously() {
        // 同一シフト(休)に「5日窓≥1」と「14日窓≥4」の2規則を同時に課す
        // （CLAUDE.md記載の実運用例 cons1=[5日窓休≥1, 14日窓休≥4, ...] と同型の同一シフト複数規則）。
        val st = MagiState(
            startDate = "2026-01-01", endDate = "2026-01-14",
            shifts = listOf(Shift("休", "休", "", ""), Shift("X", "X", "", "")),
            groups = listOf(Group("G", "G")),
            staff = listOf(Staff("a", 0)),
            use2Patterns = false,
            groupShift = listOf(listOf(1, 1)),
            groupShiftApt = listOf(listOf("", "")),
            schedule = listOf(List(14) { -1 }),
            wishes = emptyMap(), staffRange = emptyMap(),
            needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = listOf(
                C1Row(day1 = "5", shiftKigou = "休", day2 = "1"),
                C1Row(day1 = "14", shiftKigou = "休", day2 = "4"),
            ),
            cons2 = emptyList(), cons3 = emptyList(),
            cons3n = emptyList(), cons3m = emptyList(), cons3mn = emptyList(),
            cons41 = emptyList(), cons42 = emptyList(),
        )
        val result = SmartInitialScheduler.generate(st)
        assertEquals(0, result.report.breakdown["c1"] ?: -1)
        assertEquals(0, result.report.hard)
        val restCount = result.schedule[0].count { it == 0 }
        assertTrue("14日窓規則(≥4)を満たすには休が4日以上必要", restCount >= 4)
    }

    @Test
    fun satisfiesC1RulesOnDifferentShiftsForSameStaff() {
        // 異なるシフト(A/B)に別々のC1規則を課すケース（複数規則がシフトをまたぐ場合）。
        // シフトindex順(A→B)で逐次構築するため、Aの決定がBの空き日を狭めるが、
        // 各規則が軽い(5日窓≥1)ため両立できることを確認する。
        val st = MagiState(
            startDate = "2026-01-01", endDate = "2026-01-11",
            shifts = listOf(Shift("休", "休", "", ""), Shift("A", "A", "", ""), Shift("B", "B", "", "")),
            groups = listOf(Group("G", "G")),
            staff = listOf(Staff("a", 0)),
            use2Patterns = false,
            groupShift = listOf(listOf(1, 1, 1)),
            groupShiftApt = listOf(listOf("", "", "")),
            schedule = listOf(List(11) { -1 }),
            wishes = emptyMap(), staffRange = emptyMap(),
            needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = listOf(
                C1Row(day1 = "5", shiftKigou = "A", day2 = "1"),
                C1Row(day1 = "5", shiftKigou = "B", day2 = "1"),
            ),
            cons2 = emptyList(), cons3 = emptyList(),
            cons3n = emptyList(), cons3m = emptyList(), cons3mn = emptyList(),
            cons41 = emptyList(), cons42 = emptyList(),
        )
        val result = SmartInitialScheduler.generate(st)
        assertEquals(0, result.report.breakdown["c1"] ?: -1)
        assertEquals(0, result.report.hard)
    }

    @Test
    fun respectsPersonalUpperLimitEvenWhenC1WindowRequiresMore() {
        // 実機ログ由来の構造的矛盾を最小再現: 「5日窓でXを2回以上」というC1規則は、
        // 10日間を通して満たすには複数回のX配置が要る（例: day2,4,6,8）。しかし本人の
        // 個人上限(staffRange hi=1)は1回までしか許さない。high(重み45)はc1(重み15)より
        // 重いため、C1充足のためだけに上限を超えてXを増やしてはならない。
        fun state(withCap: Boolean): MagiState = MagiState(
            startDate = "2026-01-01", endDate = "2026-01-10",
            shifts = listOf(Shift("休", "休", "", ""), Shift("X", "X", "", "")),
            groups = listOf(Group("G", "G")),
            staff = listOf(Staff("a", 0)),
            use2Patterns = false,
            groupShift = listOf(listOf(1, 1)),
            groupShiftApt = listOf(listOf("", "")),
            schedule = listOf(List(10) { -1 }),
            wishes = emptyMap(),
            staffRange = if (withCap) mapOf("0,1" to Range(lo = "", hi = "1")) else emptyMap(),
            needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = listOf(C1Row(day1 = "5", shiftKigou = "X", day2 = "2")),
            cons2 = emptyList(), cons3 = emptyList(),
            cons3n = emptyList(), cons3m = emptyList(), cons3mn = emptyList(),
            cons41 = emptyList(), cons42 = emptyList(),
        )

        val cappedResult = SmartInitialScheduler.generate(state(withCap = true))
        val cappedX = cappedResult.schedule[0].count { it == 1 }
        assertTrue("個人上限(hi=1)を超えて割り当ててはならない: xCount=$cappedX", cappedX <= 1)

        // 対照: 上限が無ければC1充足のためもっと多くのXを割り当てる
        // （＝上限パラメータが実際に効いていることの確認、恒常的にno-opでないことの担保）。
        val uncappedResult = SmartInitialScheduler.generate(state(withCap = false))
        val uncappedX = uncappedResult.schedule[0].count { it == 1 }
        assertTrue(
            "上限が無ければcapped構成(${cappedX}件)より多くXを割り当てるはず: uncapped=$uncappedX",
            uncappedX > cappedX,
        )
    }

    @Test
    fun rebuildsFromScratchEvenWhenInputScheduleIsAlreadyFullyFilled() {
        // [3.261.0, 実機報告「初期解生成後にC1違反になる/何度も出来ない」の再現] 旧実装は
        // 入力スケジュールの充足率(>=50%)で「既存表ベース(保持)/空表ベース(構築)」を切り替えており、
        // 既に100%充足済みの入力（1回目の生成直後・あるいは既存データ読込直後によくある状態）では
        // 全ステップが「空きセルが無い」でno-opし、C1が一切改善されないまま返っていた。
        // 全11日を「休」で埋めた（Xを一度も使わずC1「5日窓でX2回以上」に違反する）状態を入力にしても、
        // 常にゼロから組み立て直しC1が解消されることを固定する。
        val st = blankState(cons1 = listOf(C1Row(day1 = "5", shiftKigou = "X", day2 = "2")))
            .let { it.copy(schedule = listOf(List(11) { 0 })) }
        val before = UnifiedViolationChecker.check(st, st.schedule.toIntArray2D())
        assertTrue("入力(全休)は初期状態でC1違反があること", (before.breakdown["c1"] ?: 0) > 0)

        val result = SmartInitialScheduler.generate(st)
        assertEquals("入力が100%充足済みでもC1が解消されること", 0, result.report.breakdown["c1"] ?: -1)

        // 実際にボタンを連打した状況を再現: 1回目の出力を入力にしてもう一度呼んでも、
        // no-opで劣化せず同じ良い結果に到達すること（旧実装は完全な無変化になっていた）。
        val st2 = st.copy(schedule = result.schedule.map { it.toList() })
        val result2 = SmartInitialScheduler.generate(st2)
        assertEquals("繰り返し実行してもC1解消が保たれること", 0, result2.report.breakdown["c1"] ?: -1)
    }

    private fun ScheduleRunResult.newScheduleXAt(staff: Int, day: Int): Int = schedule[staff][day]

    /**
     * [/code-review, need2単独定義セル見落とし修正] need1未設定・need2のみで需要が定義されたシフトは、
     * 旧実装ではstep③(日別必要人数)のdemandOrderへ一切追加されず、step⑤(残り埋め)のdemandBonusも
     * 発火しないため、初期解生成が積極的に埋めず covU(HARD) 違反が残ったまま返っていた
     * （3.173.0のCoverageDiagnosis修正・3.309.0のV6LateOperators.isBalanceable修正と同根）。
     */
    @Test
    fun fillsNeed2OnlyDemandDuringInitialConstruction() {
        val st = MagiState(
            startDate = "2026-01-01", endDate = "2026-01-01",
            shifts = listOf(Shift("休", "休", "", ""), Shift("X", "X", "", "2")),
            groups = listOf(Group("G", "G")),
            staff = listOf(Staff("s0", 0), Staff("s1", 0)),
            use2Patterns = true,
            groupShift = listOf(listOf(1, 1)),
            groupShiftApt = listOf(listOf("", "")),
            schedule = listOf(List(1) { -1 }, List(1) { -1 }),
            wishes = emptyMap(), staffRange = emptyMap(),
            needDay1 = emptyMap(), needDay2 = emptyMap(),
            cons1 = emptyList(), cons2 = emptyList(), cons3 = emptyList(),
            cons3n = emptyList(), cons3m = emptyList(), cons3mn = emptyList(),
            cons41 = emptyList(), cons42 = emptyList(),
        )
        val smart = SmartInitialScheduler.generate(st)
        assertEquals("need2単独定義の需要も充足しHARD=0で返ること", 0, smart.report.hard)
        assertEquals(2, smart.schedule.count { it[0] == 1 })

        // GreedyMirrorScheduler（簡易作成）も同一パターンの修正対象。
        val naive = GreedyMirrorScheduler.generate(st)
        assertEquals("簡易作成も同様にneed2単独定義の需要を充足すること", 0, naive.report.hard)
    }
}
