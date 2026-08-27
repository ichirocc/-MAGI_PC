using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;
// System.Range (built-in C# 8+ slice type, brought into scope by the SDK's implicit
// `global using global::System;`) collides by simple name with MagiEngine.Model.Range.
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [初期解生成(賢い版), phase 4 port] <c>SmartInitialScheduler</c>単体の検証。
/// 「希望→C1→必要人数→個人下限→残り埋め」の順で、既存<c>GreedyMirrorScheduler</c>(C1非考慮)より
/// C1充足に優れることを確認する。1:1 port of <c>SmartInitialSchedulerTest.kt</c>'s 9 test methods.
/// </summary>
public class SmartInitialSchedulerTest
{
    private static MagiState BlankState(IReadOnlyList<C1Row>? cons1 = null) => MinimalState.Build(
        startDate: "2026-01-01", endDate: "2026-01-11",
        shifts: new List<Shift> { new("休", "休", "", ""), new("X", "X", "", "") },
        groups: new List<Group> { new("G", "G") },
        staffList: new List<Staff> { new("a", 0) },
        use2Patterns: false,
        groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
        groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" } },
        schedule: new List<IReadOnlyList<int>> { Enumerable.Repeat(-1, 11).ToList() },
        cons1: cons1);

    [Fact]
    public void SatisfiesC1FromBlankWhereGreedyMirrorSchedulerFails()
    {
        // [3.345.0] 対照の窓ルールを「5日窓 X>=2」→「3日窓 X>=2」へ。休を優先する restBonus を外した結果、
        //   簡易作成は最少回数のシフトを選び続けて 休/X の交互になり、緩い 5日窓 X>=2 は偶然満たしてしまう。
        //   3日窓 X>=2 は交互配置では満たせない（窓に X が1つしか入らない）ため対照として成立する。
        var st = BlankState(cons1: new List<C1Row> { new("3", "X", "2") });

        var smart = SmartInitialScheduler.Generate(st);
        Assert.Equal(0, smart.Report.Breakdown.GetValueOrDefault("c1", -1));
        Assert.Equal(0, smart.Report.Hard);

        // 対照: 既存の簡易作成(C1非考慮)は同じ盤面でc1を解消できない。
        var naive = GreedyMirrorScheduler.Generate(st);
        Assert.True(naive.Report.Breakdown.GetValueOrDefault("c1", 0) > 0,
            "既存の簡易作成はC1を考慮しないため違反が残るはず");
    }

    [Fact]
    public void RespectsFeasibleWish()
    {
        var st = BlankState(cons1: new List<C1Row> { new("5", "X", "2") })
            with { Wishes = new Dictionary<string, int> { ["0,2"] = 1 } }; // shift index 1 = "X"

        var result = SmartInitialScheduler.Generate(st);
        Assert.Equal(1, result.Schedule[0][2]);
    }

    [Fact]
    public void IsNoOpFriendlyWhenNoCons1Rules()
    {
        var st = BlankState();
        var result = SmartInitialScheduler.Generate(st);
        // C1規則が無くても正常に完成盤面を返す(空きセルが残らない)。
        Assert.All(result.Schedule[0], v => Assert.InRange(v, 0, 1));
    }

    [Fact]
    public void SatisfiesMultipleC1RulesOnSameShiftSimultaneously()
    {
        // 同一シフト(休)に「5日窓≥1」と「14日窓≥4」の2規則を同時に課す
        // （CLAUDE.md記載の実運用例 cons1=[5日窓休≥1, 14日窓休≥4, ...] と同型の同一シフト複数規則）。
        var st = MinimalState.Build(
            startDate: "2026-01-01", endDate: "2026-01-14",
            shifts: new List<Shift> { new("休", "休", "", ""), new("X", "X", "", "") },
            groups: new List<Group> { new("G", "G") },
            staffList: new List<Staff> { new("a", 0) },
            use2Patterns: false,
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
            groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" } },
            schedule: new List<IReadOnlyList<int>> { Enumerable.Repeat(-1, 14).ToList() },
            cons1: new List<C1Row> { new("5", "休", "1"), new("14", "休", "4") });

        var result = SmartInitialScheduler.Generate(st);
        Assert.Equal(0, result.Report.Breakdown.GetValueOrDefault("c1", -1));
        Assert.Equal(0, result.Report.Hard);
        int restCount = result.Schedule[0].Count(v => v == 0);
        Assert.True(restCount >= 4, "14日窓規則(≥4)を満たすには休が4日以上必要");
    }

    [Fact]
    public void SatisfiesC1RulesOnDifferentShiftsForSameStaff()
    {
        // 異なるシフト(A/B)に別々のC1規則を課すケース（複数規則がシフトをまたぐ場合）。
        // シフトindex順(A→B)で逐次構築するため、Aの決定がBの空き日を狭めるが、
        // 各規則が軽い(5日窓≥1)ため両立できることを確認する。
        var st = MinimalState.Build(
            startDate: "2026-01-01", endDate: "2026-01-11",
            shifts: new List<Shift> { new("休", "休", "", ""), new("A", "A", "", ""), new("B", "B", "", "") },
            groups: new List<Group> { new("G", "G") },
            staffList: new List<Staff> { new("a", 0) },
            use2Patterns: false,
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } },
            groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "", "" } },
            schedule: new List<IReadOnlyList<int>> { Enumerable.Repeat(-1, 11).ToList() },
            cons1: new List<C1Row> { new("5", "A", "1"), new("5", "B", "1") });

        var result = SmartInitialScheduler.Generate(st);
        Assert.Equal(0, result.Report.Breakdown.GetValueOrDefault("c1", -1));
        Assert.Equal(0, result.Report.Hard);
    }

    [Fact]
    public void RespectsPersonalUpperLimitEvenWhenC1WindowRequiresMore()
    {
        // 実機ログ由来の構造的矛盾を最小再現: 「5日窓でXを2回以上」というC1規則は、
        // 10日間を通して満たすには複数回のX配置が要る（例: day2,4,6,8）。しかし本人の
        // 個人上限(staffRange hi=1)は1回までしか許さない。high(重み45)はc1(重み30)より
        // 重いため、C1充足のためだけに上限を超えてXを増やしてはならない。
        MagiState State(bool withCap) => MinimalState.Build(
            startDate: "2026-01-01", endDate: "2026-01-10",
            shifts: new List<Shift> { new("休", "休", "", ""), new("X", "X", "", "") },
            groups: new List<Group> { new("G", "G") },
            staffList: new List<Staff> { new("a", 0) },
            use2Patterns: false,
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
            groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" } },
            schedule: new List<IReadOnlyList<int>> { Enumerable.Repeat(-1, 10).ToList() },
            staffRange: withCap
                ? new Dictionary<string, Range> { ["0,1"] = new(Lo: "", Hi: "1") }
                : new Dictionary<string, Range>(),
            cons1: new List<C1Row> { new("5", "X", "2") });

        var cappedResult = SmartInitialScheduler.Generate(State(withCap: true));
        int cappedX = cappedResult.Schedule[0].Count(v => v == 1);
        Assert.True(cappedX <= 1, $"個人上限(hi=1)を超えて割り当ててはならない: xCount={cappedX}");

        // 対照: 上限が無ければC1充足のためもっと多くのXを割り当てる
        // （＝上限パラメータが実際に効いていることの確認、恒常的にno-opでないことの担保）。
        var uncappedResult = SmartInitialScheduler.Generate(State(withCap: false));
        int uncappedX = uncappedResult.Schedule[0].Count(v => v == 1);
        Assert.True(uncappedX > cappedX,
            $"上限が無ければcapped構成({cappedX}件)より多くXを割り当てるはず: uncapped={uncappedX}");
    }

    [Fact]
    public void RebuildsFromScratchEvenWhenInputScheduleIsAlreadyFullyFilled()
    {
        // [3.261.0, 実機報告「初期解生成後にC1違反になる/何度も出来ない」の再現] 旧実装は
        // 入力スケジュールの充足率(>=50%)で「既存表ベース(保持)/空表ベース(構築)」を切り替えており、
        // 既に100%充足済みの入力（1回目の生成直後・あるいは既存データ読込直後によくある状態）では
        // 全ステップが「空きセルが無い」でno-opし、C1が一切改善されないまま返っていた。
        // 全11日を「休」で埋めた（Xを一度も使わずC1「5日窓でX2回以上」に違反する）状態を入力にしても、
        // 常にゼロから組み立て直しC1が解消されることを固定する。
        var st = BlankState(cons1: new List<C1Row> { new("5", "X", "2") })
            with { Schedule = new List<IReadOnlyList<int>> { Enumerable.Repeat(0, 11).ToList() } };
        var before = UnifiedViolationChecker.Check(st, st.Schedule.ToIntArray2D());
        Assert.True(before.Breakdown.GetValueOrDefault("c1", 0) > 0, "入力(全休)は初期状態でC1違反があること");

        var result = SmartInitialScheduler.Generate(st);
        Assert.Equal(0, result.Report.Breakdown.GetValueOrDefault("c1", -1)); // 入力が100%充足済みでもC1が解消されること

        // 実際にボタンを連打した状況を再現: 1回目の出力を入力にしてもう一度呼んでも、
        // no-opで劣化せず同じ良い結果に到達すること（旧実装は完全な無変化になっていた）。
        var st2 = st with { Schedule = result.Schedule.Select(row => (IReadOnlyList<int>)row.ToList()).ToList() };
        var result2 = SmartInitialScheduler.Generate(st2);
        Assert.Equal(0, result2.Report.Breakdown.GetValueOrDefault("c1", -1)); // 繰り返し実行してもC1解消が保たれること
    }

    /// <summary>
    /// [/code-review, need2単独定義セル見落とし修正] need1未設定・need2のみで需要が定義されたシフトは、
    /// 旧実装ではstep③(日別必要人数)のdemandOrderへ一切追加されず、step⑤(残り埋め)のdemandBonusも
    /// 発火しないため、初期解生成が積極的に埋めず covU(HARD) 違反が残ったまま返っていた
    /// （3.173.0のCoverageDiagnosis修正・3.309.0のV6LateOperators.isBalanceable修正と同根）。
    /// </summary>
    [Fact]
    public void FillsNeed2OnlyDemandDuringInitialConstruction()
    {
        var st = MinimalState.Build(
            startDate: "2026-01-01", endDate: "2026-01-01",
            shifts: new List<Shift> { new("休", "休", "", ""), new("X", "X", "", "2") },
            groups: new List<Group> { new("G", "G") },
            staffList: new List<Staff> { new("s0", 0), new("s1", 0) },
            use2Patterns: true,
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
            groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" } },
            schedule: new List<IReadOnlyList<int>> { new List<int> { -1 }, new List<int> { -1 } });

        var smart = SmartInitialScheduler.Generate(st);
        Assert.Equal(0, smart.Report.Hard); // need2単独定義の需要も充足しHARD=0で返ること
        Assert.Equal(2, smart.Schedule.Count(row => row[0] == 1));

        // GreedyMirrorScheduler（簡易作成）も同一パターンの修正対象。
        var naive = GreedyMirrorScheduler.Generate(st);
        Assert.Equal(0, naive.Report.Hard); // 簡易作成も同様にneed2単独定義の需要を充足すること
    }

    // [3.391.0 実バグ回帰] 旧 GreedyMirrorScheduler は担当できないシフトへの希望まで**盤面へ置いて**
    // いた。pref は実現可能な希望しか数えない（MirrorCore）ので置いても pref は1点も得しない一方、
    // 担当外セル＝groupViol(HARD 10000) が確実に立つ＝純損。SmartInitialScheduler は同じ処理で
    // canDo を見ており（3.257.0）、旧世代の生成器だけが取り残されていた。両方で groupViol=0 を固定する。
    [Fact]
    public void NeitherGeneratorPlacesAnInfeasibleWish()
    {
        var st = MinimalState.Build(
            startDate: "2025-12-01",
            endDate: "2025-12-03",
            shifts: new List<Shift> { new("休み", "休", "", ""), new("早番", "A", "1", "1"), new("遅番", "B", "", "") },
            groups: new List<Group> { new("G", "G"), new("H", "H") },
            staffList: new List<Staff> { new("s0", 0), new("s1", 1) },
            use2Patterns: true,
            // 群G(s0) は 休/A のみ担当可＝B は担当不可。
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 0 }, new List<int> { 1, 1, 1 } },
            groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "", "" }, new List<string> { "", "", "" } },
            schedule: new List<IReadOnlyList<int>> { new List<int> { -1, -1, -1 }, new List<int> { -1, -1, -1 } },
            wishes: new Dictionary<string, int> { ["0,0"] = 2 }); // s0 が担当できない B を希望＝実現不能

        var p = new Problem(st);
        Assert.False(p.WishLocked(0, 0), "前提: s0 の B 希望は実現不能");

        var greedy = GreedyMirrorScheduler.Generate(st);
        Assert.Equal(0, UnifiedViolationChecker.Check(st, greedy.Schedule).Breakdown["groupViol"]); // 旧世代の生成器も担当外セルを作らない
        var smart = SmartInitialScheduler.Generate(st);
        Assert.Equal(0, UnifiedViolationChecker.Check(st, smart.Schedule).Breakdown["groupViol"]); // 新しい生成器も同じ
    }
}
