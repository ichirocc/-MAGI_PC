using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;
// System.Range (built-in C# 8+ slice type, brought into scope by the SDK's implicit
// `global using global::System;`) collides by simple name with MagiEngine.Model.Range.
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [フェーズ6, 可変長ブロック交換] <see cref="V6HotfixPasses.ApplyAdaptiveBlockSwapPolish"/> の検証。
///
/// [Kotlin原本] <c>AdaptiveBlockSwapPolishTest.kt</c> の6テストを1:1で移植する。旧
/// <c>applyBlockSwapPolish</c>（同一担当グループ×15日固定、Kotlin 3.300.0 で削除済み・本移植でも
/// 未実装）は (a) 別グループ同士の交換 (b) 15日以外の長さ に到達できなかった。各テストは新演算子だけが
/// 到達できる最小盤面（別グループ・可変長・2〜4者巡回・厳密ピン保存・希望固定の据え置き）を固定する。
/// </summary>
public class V6HotfixPassesAdaptiveBlockSwapTest
{
    /// <summary>
    /// T=11・2職員・別グループ。両者とも 休/X/Y を担当できる。
    /// X も Y も毎日1人必要なので、2人は必ず「片方X・片方Y」に分かれる＝被覆は交換で不変。
    /// 個人の回数ピンは A=「Xを11回」/ B=「Yを11回」だが、初期盤面は真逆（A=Y×11, B=X×11）。
    /// → 11日ブロックを丸ごと交換した時だけ両者の下限割れが同時に解消する。
    /// </summary>
    private static MagiState CrossGroupState(IReadOnlyDictionary<string, int>? wishes = null) => MinimalState.Build(
        startDate: "2026-02-01", endDate: "2026-02-11",
        shifts: new List<Shift> { new("休み", "休", "", ""), new("X", "X", "1", "1"), new("Y", "Y", "1", "1") },
        groups: new List<Group> { new("G0", "G0"), new("G1", "G1") },
        staffList: new List<Staff> { new("A", 0), new("B", 1) },
        use2Patterns: false,
        groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 }, new List<int> { 1, 1, 1 } },
        schedule: new List<IReadOnlyList<int>>
        {
            Enumerable.Repeat(2, 11).ToList(),   // A: Y×11（Xの下限11を丸ごと割っている）
            Enumerable.Repeat(1, 11).ToList(),   // B: X×11（Yの下限11を丸ごと割っている）
        },
        wishes: wishes,
        staffRange: new Dictionary<string, Range>
        {
            ["0,1"] = new("11", "11"),   // A の X を 11 に固定
            ["1,2"] = new("11", "11"),   // B の Y を 11 に固定
        });

    [Fact]
    public void AdaptiveSwapCrossesGroupsAndBlockLengthsThatTheLegacyPassCannotReach()
    {
        var st = CrossGroupState();
        var sched = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, sched);
        Assert.True((before.Breakdown.TryGetValue("low", out var lowB) ? lowB : 0) > 0, "初期は個人下限割れがある");
        Assert.Equal(0, before.Hard); // 初期 HARD=0（被覆は満たしている）

        // 旧 applyBlockSwapPolish（同一グループ×15日固定）は削除済み。この盤面は同一グループのペアが
        //   存在しないため旧パスは手を1つも作れなかった＝ここで確認する改善は新演算子に固有のもの。
        //   別グループ×11日ブロックで両者の下限割れが同時に解消する。
        var res = V6HotfixPasses.ApplyAdaptiveBlockSwapPolish(st, sched.Copy2D());
        var after = UnifiedViolationChecker.Check(st, res.NewSchedule);
        Assert.True(res.Applied > 0, "可変長ブロック交換が採用されたこと");
        Assert.Equal(0, after.Breakdown.TryGetValue("low", out var lowA) ? lowA : 0); // 個人下限割れが解消
        Assert.Equal(0, after.Hard); // HARD は不変(=0)
        // 被覆保存: 各日の X/Y はそれぞれ1人のまま。
        for (var j = 0; j < 11; j++)
        {
            var col = Enumerable.Range(0, 2).Select(i => res.NewSchedule[i][j]).ToList();
            Assert.Equal(1, col.Count(v => v == 1));
            Assert.Equal(1, col.Count(v => v == 2));
        }
    }

    /// <summary>
    /// [3.291.0 候補生成の緩和] 希望固定日をブロック内に含んでいても、その日だけ据え置いて残りを交換する。
    ///
    /// 旧（全か無か）の候補生成では、ブロック内に希望固定が1日でもあれば return null でブロックごと棄却
    /// していた。この盤面は T=11 で有効な長さが 11 のみ＝唯一のブロック(0〜10日)が固定日を必ず含むため、
    /// 旧実装なら候補0件＝完全に不活性になる。緩和後は固定日を除く10日が交換され、下限割れが 22→2 まで
    /// 縮む（固定日ぶんの1回だけ届かない＝据え置きの意味論そのもの）。
    /// </summary>
    [Fact]
    public void AdaptiveSwapKeepsWishLockedDaysInPlaceAndSwapsTheRest()
    {
        // A は6日目(index 5)に Y を希望＝その日は動かせない。
        var st = CrossGroupState(wishes: new Dictionary<string, int> { ["0,5"] = 2 });
        var sched = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, sched);
        Assert.Equal(22, before.Breakdown.TryGetValue("low", out var lowB) ? lowB : 0); // 初期の下限割れ合計(A11+B11)

        var res = V6HotfixPasses.ApplyAdaptiveBlockSwapPolish(st, sched.Copy2D());
        var after = UnifiedViolationChecker.Check(st, res.NewSchedule);
        Assert.True(res.Applied > 0, "固定日があっても残りの日で交換が成立すること");
        Assert.Equal(2, res.NewSchedule[0][5]); // 希望固定日の A は据え置き(Y のまま)
        Assert.Equal(1, res.NewSchedule[1][5]); // 希望固定日の B も据え置き(X のまま)
        Assert.Equal(2, after.Breakdown.TryGetValue("low", out var lowA) ? lowA : 0); // 固定日ぶん(A・B 各1回)だけ残して下限割れが縮む
        Assert.Equal(0, after.Hard); // HARD は不変(=0・希望も充足のまま)
        for (var j = 0; j < 11; j++)
        {
            var col = Enumerable.Range(0, 2).Select(i => res.NewSchedule[i][j]).ToList();
            Assert.Equal(1, col.Count(v => v == 1));
            Assert.Equal(1, col.Count(v => v == 2));
        }
    }

    /// <summary>
    /// [3.292.0 3者巡回] 3職員がそれぞれ「担当できる2シフト」しか持たず、<b>どの2者交換も担当不可で
    /// 成立しない</b>が3者巡回 A←C←B←A なら全員が目標シフトへ収まる盤面。
    ///
    /// A: X/Y 可（今 Y・X を11回欲しい） / B: Y/Z 可（今 Z・Y を11回欲しい） / C: X/Z 可（今 X・Z を11回
    /// 欲しい）。2者交換は A↔B が Z を A に、B↔C が X を B に、A↔C が Y を C に渡すため3通りとも canDo で
    /// 不成立。
    /// </summary>
    private static MagiState ThreeWayCycleState() => MinimalState.Build(
        startDate: "2026-02-01", endDate: "2026-02-11",
        shifts: new List<Shift>
        {
            new("休み", "休", "", ""), new("X", "X", "1", "1"), new("Y", "Y", "1", "1"), new("Z", "Z", "1", "1"),
        },
        groups: new List<Group> { new("G0", "G0"), new("G1", "G1"), new("G2", "G2") },
        staffList: new List<Staff> { new("A", 0), new("B", 1), new("C", 2) },
        use2Patterns: false,
        groupShift: new List<IReadOnlyList<int>>
        {
            new List<int> { 0, 1, 1, 0 },   // A: X/Y
            new List<int> { 0, 0, 1, 1 },   // B: Y/Z
            new List<int> { 0, 1, 0, 1 },   // C: X/Z
        },
        schedule: new List<IReadOnlyList<int>>
        {
            Enumerable.Repeat(2, 11).ToList(),   // A: Y×11（欲しいのは X）
            Enumerable.Repeat(3, 11).ToList(),   // B: Z×11（欲しいのは Y）
            Enumerable.Repeat(1, 11).ToList(),   // C: X×11（欲しいのは Z）
        },
        staffRange: new Dictionary<string, Range>
        {
            ["0,1"] = new("11", "11"),   // A の X を 11 に固定
            ["1,2"] = new("11", "11"),   // B の Y を 11 に固定
            ["2,3"] = new("11", "11"),   // C の Z を 11 に固定
        });

    [Fact]
    public void ThreeWayCycleSolvesWhatNoTwoWaySwapCan()
    {
        var st = ThreeWayCycleState();
        var sched = st.Schedule.ToIntArray2D();
        var initLow = UnifiedViolationChecker.Check(st, sched).Breakdown.TryGetValue("low", out var lb) ? lb : 0;
        Assert.Equal(33, initLow); // 初期の下限割れ合計(3人×11)

        // 2者交換までに制限すると、どの手も担当不可で成立しない。
        var pairOnly = V6HotfixPasses.ApplyAdaptiveBlockSwapPolish(st, sched.Copy2D(), maxCycle: 2);
        Assert.Equal(0, pairOnly.Applied); // 2者交換だけでは到達不能

        // 3者巡回を許すと1手で全員が目標へ収まる。
        var res = V6HotfixPasses.ApplyAdaptiveBlockSwapPolish(st, sched.Copy2D());
        var after = UnifiedViolationChecker.Check(st, res.NewSchedule);
        Assert.True(res.Applied > 0, "3者巡回が採用されたこと");
        Assert.Equal(0, after.Breakdown.TryGetValue("low", out var la) ? la : 0); // 下限割れが完全に解消
        Assert.Equal(0, after.Hard); // HARD は不変(=0)
        for (var j = 0; j < 11; j++)
        {
            var col = Enumerable.Range(0, 3).Select(i => res.NewSchedule[i][j]).OrderBy(v => v).ToList();
            Assert.Equal(new List<int> { 1, 2, 3 }, col); // 被覆保存(X/Y/Z 各1人)
        }
    }

    /// <summary>
    /// [3.292.0 多者巡回] 4職員が一本の4者巡回でしか解けない盤面。
    /// A: P/Q 可（今 Q・P が目標） / B: Q/R（今 R・Q が目標） / C: R/S（今 S・R が目標） / D: S/P（今 P・S
    /// が目標）。2者交換も3者巡回も canDo で全滅し、A←D←C←B←A の4者巡回だけが閉じる。
    /// </summary>
    private static MagiState FourWayCycleState() => MinimalState.Build(
        startDate: "2026-02-01", endDate: "2026-02-11",
        shifts: new List<Shift>
        {
            new("休み", "休", "", ""), new("P", "P", "1", "1"), new("Q", "Q", "1", "1"),
            new("R", "R", "1", "1"), new("S", "S", "1", "1"),
        },
        groups: new List<Group> { new("G0", "G0"), new("G1", "G1"), new("G2", "G2"), new("G3", "G3") },
        staffList: new List<Staff> { new("A", 0), new("B", 1), new("C", 2), new("D", 3) },
        use2Patterns: false,
        groupShift: new List<IReadOnlyList<int>>
        {
            new List<int> { 0, 1, 1, 0, 0 },   // A: P/Q
            new List<int> { 0, 0, 1, 1, 0 },   // B: Q/R
            new List<int> { 0, 0, 0, 1, 1 },   // C: R/S
            new List<int> { 0, 1, 0, 0, 1 },   // D: S/P
        },
        schedule: new List<IReadOnlyList<int>>
        {
            Enumerable.Repeat(2, 11).ToList(),   // A: Q×11（欲しいのは P）
            Enumerable.Repeat(3, 11).ToList(),   // B: R×11（欲しいのは Q）
            Enumerable.Repeat(4, 11).ToList(),   // C: S×11（欲しいのは R）
            Enumerable.Repeat(1, 11).ToList(),   // D: P×11（欲しいのは S）
        },
        staffRange: new Dictionary<string, Range>
        {
            ["0,1"] = new("11", "11"),
            ["1,2"] = new("11", "11"),
            ["2,3"] = new("11", "11"),
            ["3,4"] = new("11", "11"),
        });

    [Fact]
    public void FourWayCycleSolvesWhatShorterCyclesCannot()
    {
        var st = FourWayCycleState();
        var sched = st.Schedule.ToIntArray2D();
        var initLow = UnifiedViolationChecker.Check(st, sched).Breakdown.TryGetValue("low", out var lb) ? lb : 0;
        Assert.Equal(44, initLow); // 初期の下限割れ合計(4人×11)

        var upToThree = V6HotfixPasses.ApplyAdaptiveBlockSwapPolish(st, sched.Copy2D(), maxCycle: 3);
        Assert.Equal(0, upToThree.Applied); // 3者巡回まででは到達不能

        var res = V6HotfixPasses.ApplyAdaptiveBlockSwapPolish(st, sched.Copy2D());
        var after = UnifiedViolationChecker.Check(st, res.NewSchedule);
        Assert.True(res.Applied > 0, "4者巡回が採用されたこと");
        Assert.Equal(0, after.Breakdown.TryGetValue("low", out var la) ? la : 0); // 下限割れが完全に解消
        Assert.Equal(0, after.Hard); // HARD は不変(=0)
        for (var j = 0; j < 11; j++)
        {
            var col = Enumerable.Range(0, 4).Select(i => res.NewSchedule[i][j]).OrderBy(v => v).ToList();
            Assert.Equal(new List<int> { 1, 2, 3, 4 }, col); // 被覆保存(P/Q/R/S 各1人)
        }
    }

    /// <summary>
    /// [3.294.0 ピン保存交換] ブロック全体を交換すると厳密ピン(lo==hi)が崩れる盤面で、<b>ピンの回数が
    /// 変わらない部分集合</b>だけを交換して改善に到達する。
    ///
    /// A は休4回で固定（lo=hi=4・充足中）、Y が下限2に対し0回。ブロック(11日)を丸ごと B と交換すると
    /// A の休は 4→2 になり ExactPinRegression で必ず却下される（＝旧実装なら採用0）。休の増減が打ち消し
    /// 合う日だけを選べば、休4を保ったまま Y の下限割れを解消できる。
    /// </summary>
    private static MagiState PinnedRestState() => MinimalState.Build(
        startDate: "2026-02-01", endDate: "2026-02-11",
        shifts: new List<Shift> { new("休み", "休", "1", "1"), new("X", "X", "1", "1"), new("Y", "Y", "1", "1") },
        groups: new List<Group> { new("G0", "G0"), new("G1", "G1"), new("G2", "G2") },
        staffList: new List<Staff> { new("A", 0), new("B", 1), new("C", 2) },
        use2Patterns: false,
        groupShift: new List<IReadOnlyList<int>>
        {
            new List<int> { 1, 1, 1 }, new List<int> { 1, 1, 1 }, new List<int> { 1, 1, 1 },
        },
        schedule: new List<IReadOnlyList<int>>
        {
            new List<int> { 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1 },   // A: 休4 / X7 / Y0
            new List<int> { 1, 1, 1, 1, 0, 0, 2, 2, 2, 2, 2 },   // B: X4 / 休2 / Y5
            new List<int> { 2, 2, 2, 2, 2, 2, 0, 0, 0, 0, 0 },   // C: Y6 / 休5
        },
        staffRange: new Dictionary<string, Range>
        {
            ["0,0"] = new("4", "4"),   // A の休を4回に固定（現状ちょうど充足）
            ["0,2"] = new("2", ""),    // A の Y は2回以上（現状0＝下限割れ）
        });

    [Fact]
    public void PinPreservingSwapKeepsExactPinAndStillImproves()
    {
        var st = PinnedRestState();
        var sched = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, sched);
        Assert.Equal(2, before.Breakdown.TryGetValue("low", out var lowB) ? lowB : 0); // A の Y が下限2に対し0＝下限割れ2
        Assert.Equal(4, Enumerable.Range(0, 11).Count(j => sched[0][j] == 0)); // 初期の A の休は4（ピン充足）

        var res = V6HotfixPasses.ApplyAdaptiveBlockSwapPolish(st, sched.Copy2D());
        var after = UnifiedViolationChecker.Check(st, res.NewSchedule);
        Assert.True(res.Applied > 0, "部分集合の交換が採用されたこと");
        Assert.Equal(4, Enumerable.Range(0, 11).Count(j => res.NewSchedule[0][j] == 0)); // 厳密ピン（A の休4）が保たれること
        var lowA = after.Breakdown.TryGetValue("low", out var la) ? la : 0;
        var lowB2 = before.Breakdown.TryGetValue("low", out var lb2) ? lb2 : 0;
        Assert.True(lowA < lowB2); // 下限割れが減ること
        Assert.Equal(0, after.Hard); // HARD は不変(=0)
        for (var j = 0; j < 11; j++)
        {
            var col = Enumerable.Range(0, 3).Select(i => res.NewSchedule[i][j]).OrderBy(v => v).ToList();
            Assert.Equal(new List<int> { 0, 1, 2 }, col); // 被覆保存(休/X/Y 各1人)
        }
    }

    [Fact]
    public void AdaptiveSwapIsNoOpOnAlreadyOptimalBoard()
    {
        var st = CrossGroupState();
        // 最初から正しい配置（A=X, B=Y）にしておく。
        var ok = new[] { Enumerable.Repeat(1, 11).ToArray(), Enumerable.Repeat(2, 11).ToArray() };
        var res = V6HotfixPasses.ApplyAdaptiveBlockSwapPolish(st, ok);
        Assert.Equal(0, res.Applied); // 改善手が無ければ採用0
        Assert.True(res.Logs.Count > 0, "ログが出ること");
    }
}
