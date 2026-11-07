using MagiEngine.Model;
using MagiEngine.Tests.Fixtures;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;
// System.Range (built-in C# 8+ slice type, brought into scope by the SDK's implicit
// `global using global::System;`) collides by simple name with MagiEngine.Model.Range.
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [希望固定の徹底] 規則 A: 盤面ごと・他の盤面から写す経路でも希望固定セルへ希望以外を書かない
/// （Kotlin側 WishPinStrictTest.kt と同型）。
///
/// 固定具は<b>希望どうしの衝突</b>（s0 の 2〜4 日目に休の希望 × 禁止の並び「休→休→休」）。希望を 1 つ崩すと
/// c3n(9000)→pref(8000) で HARD 件数は 1 のまま weighted だけ下がる＝keep-best は崩した盤面を採る。
/// 各経路について「ON なら希望が残る」と「OFF なら崩れる（＝ガードが差を作っている）」を対で固定する。
/// </summary>
public class WishPinStrictTest
{
    private const int Rest = 0;
    private const int A = 1;
    private static readonly int[] WishDays = { 1, 2, 3 };

    private static MagiState SelfConflictState(int[][] schedule) => MinimalState.Build(
        startDate: "2026-08-01", endDate: "2026-08-05",
        shifts: new List<Shift> { new("休み", "休", "", "", ShiftRole.Rest), new("早番", "A", "", "") },
        groups: new List<Group> { new("G0", "G0") },
        staffList: new List<Staff> { new("s0", 0), new("s1", 0) },
        use2Patterns: false,
        groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
        groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" } },
        schedule: schedule.Select(r => (IReadOnlyList<int>)r.ToList()).ToList(),
        wishes: WishDays.ToDictionary(d => $"0,{d}", _ => Rest),
        staffRange: new Dictionary<string, Range>(),
        needDay1: new Dictionary<string, string>(), needDay2: new Dictionary<string, string>(),
        cons1: new List<C1Row>(), cons2: new List<C2Row>(), cons3: new List<C3Row>(),
        cons3n: new List<C3Row> { new(new List<string> { "休", "休", "休" }) },
        cons3m: new List<C3Row>(), cons3mn: new List<C3Row>(), cons41: new List<C41Row>(), cons42: new List<C42Row>());

    /// <summary>希望どおり（c3n が 1 件）。</summary>
    private static int[][] Kept() => new[] { new[] { A, Rest, Rest, Rest, A }, new[] { Rest, A, A, A, Rest } };
    /// <summary>3 日目の希望を崩した盤面（c3n→pref）。</summary>
    private static int[][] Broken() => new[] { new[] { A, Rest, A, Rest, A }, new[] { Rest, A, A, A, Rest } };

    private static List<(int, int)> BrokenWishes(Problem p, int[][] s)
    {
        var list = new List<(int, int)>();
        for (var i = 0; i < p.S; i++)
            for (var j = 0; j < p.T; j++)
                if (p.WishLocked(i, j) && s[i][j] != p.Wish[i][j]) list.Add((i, j));
        return list;
    }


    [Fact]
    public void FixtureIsARealSelfConflictWhereBreakingTheWishWins()
    {
        var st = SelfConflictState(Kept());
        var keptRep = UnifiedViolationChecker.Check(st, Kept());
        var brokenRep = UnifiedViolationChecker.Check(st, Broken());
        Assert.Equal(1, keptRep.Breakdown.GetValueOrDefault("c3n", 0));
        Assert.Equal(1, brokenRep.Breakdown.GetValueOrDefault("pref", 0));
        Assert.Equal(keptRep.Hard, brokenRep.Hard); // HARD 件数は同じ
        Assert.True(UnifiedViolationChecker.BetterReport(brokenRep, keptRep), "崩した方が keep-best に勝つ（ガードが無いと漏れる前提）");
    }

    [Fact]
    public void KeepsWishPinsAllowsRestoreAndCarryButNotANewBreak()
    {
        var st = SelfConflictState(Kept()) with
        {
            Shifts = new List<Shift> { new("休み", "休", "", "", ShiftRole.Rest), new("早番", "A", "", ""), new("遅番", "B", "", "") },
            GroupShift = new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } },
            GroupShiftApt = new List<IReadOnlyList<string>> { new List<string> { "", "", "" } },
        };
        var p = new Problem(st);
        var keptBoard = Kept();
        var brokenBoard = Broken();
        Assert.True(p.KeepsWishPins(keptBoard, keptBoard), "同じ盤面");
        Assert.False(p.KeepsWishPins(keptBoard, brokenBoard), "希望どおりのセルを崩す");
        Assert.True(p.KeepsWishPins(brokenBoard, brokenBoard), "入力で崩れていたセルを持ち越す");
        Assert.True(p.KeepsWishPins(brokenBoard, keptBoard), "入力で崩れていたセルを希望へ戻す");
        var other = Broken();
        other[0][2] = 2;
        Assert.False(p.KeepsWishPins(brokenBoard, other), "崩れていたセルを別の希望外の値へ動かす");
    }

    // (i) ELITE_RELINK の入口: 希望セルで食い違う相手へ再結合しても希望が残る。
    [Fact]
    public void ElitePathRelinkKeepsWishes()
    {
        var st = SelfConflictState(Kept());
        var p = new Problem(st);
        var on = V6NativeOptimizer.ElitePathRelink(st, Kept(), new List<int[][]> { Broken() }, () => false).Schedule;
        Assert.Empty(BrokenWishes(p, on));
    }

    // (ii) PERSON_SWAP_ILS の摂動: 希望固定の日だけ交換せず、それ以外の日は入れ替わる。
    [Fact]
    public void PersonSwapKickSkipsWishDays()
    {
        var st = SelfConflictState(Kept());
        var p = new Problem(st);
        for (var seed = 1L; seed <= 3L; seed++)
        {
            var on = Kept();
            V6NativeOptimizer.PersonSwapKick(p, on, new JavaRandom(seed), pairs: 1);
            Assert.Empty(BrokenWishes(p, on));                              // 希望固定の日は動かない
            Assert.Equal(new[] { Rest, Rest, Rest, Rest, Rest }, on[0]);     // 希望の無い日は入れ替わる
            Assert.Equal(new[] { A, A, A, A, A }, on[1]);                    // 相手も希望の日だけ残る
        }
    }

    private static AdaptiveElite Elite(MagiState st, int[][] board, bool bridge) =>
        AdaptiveElite.Create(board, UnifiedViolationChecker.Check(st, board), HypothesisEpochRole.EliteRelink, worker: 1, epoch: 0, bridge: bridge);

    // (iii) エリート統合の端点採用: 希望を崩したエリートは better でも採らない。
    [Fact]
    public void EliteIntegrationRejectsAnEndpointWithABrokenWish()
    {
        var st = SelfConflictState(Kept());
        var p = new Problem(st);
        var outSched = EliteIntegrationPolish.Apply(
            st, Kept(), new List<AdaptiveElite> { Elite(st, Broken(), bridge: false) }, () => false,
            EngineClock.NowMs() + 10_000L).Schedule;
        Assert.Empty(BrokenWishes(p, outSched));
    }

    // 端点としては採らない橋渡しエリートでも、それを起点にした relink の中間解が崩れを持ち込まない。
    [Fact]
    public void EliteIntegrationRelinkFromABrokenBridgeDoesNotCarryTheBreak()
    {
        var st = SelfConflictState(Kept());
        var p = new Problem(st);
        int[][] BridgeBoard() => new[] { new[] { A, Rest, A, Rest, A }, new[] { A, A, A, A, Rest } };
        var outSched = EliteIntegrationPolish.Apply(
            st, Kept(), new List<AdaptiveElite> { Elite(st, BridgeBoard(), bridge: true) }, () => false,
            EngineClock.NowMs() + 10_000L).Schedule;
        Assert.Empty(BrokenWishes(p, outSched));
    }

    // 規則 A のセル判定: 希望どおりのセルは動かさない・未反映は希望へだけ。
    [Fact]
    public void WishMoveAllowedIsRuleA()
    {
        var st = SelfConflictState(Kept()) with
        {
            Shifts = new List<Shift> { new("休み", "休", "", "", ShiftRole.Rest), new("早番", "A", "", ""), new("遅番", "B", "", "") },
            GroupShift = new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } },
            GroupShiftApt = new List<IReadOnlyList<string>> { new List<string> { "", "", "" } },
        };
        var p = new Problem(st);
        Assert.False(p.WishMoveAllowed(0, 1, Rest, A)); // (1) 希望どおり→別の値
        Assert.True(p.WishMoveAllowed(0, 1, A, Rest));  // (2) 未反映→希望
        Assert.False(p.WishMoveAllowed(0, 1, A, 2));    // (3) 未反映→希望でも今の値でもない
        Assert.True(p.WishMoveAllowed(0, 0, A, 2));     // 希望の無いセル
    }

    private static readonly Shift SRest = new("休", "休", "", "", ShiftRole.Rest);

    /// <summary>X・Y とも 1 日目に A（需要 1 → 1 人過剰）、B は受け皿。X は C（需要 0＝受け皿なし）を希望していて A のまま（未反映）。
    /// separateGroups: X と Y を別グループに置き fair を外す（2 人群では退避 1 セルが fair を 2 件動かし、covO 1 件(10)と同点＝3.647.0 fair 5）。</summary>
    private static MagiState CovOState(bool separateGroups = false) => MinimalState.Build(
        startDate: "2026-08-01", endDate: "2026-08-01",
        shifts: new List<Shift> { SRest, new("A", "A", "1", ""), new("B", "B", "", ""), new("C", "C", "0", "") },
        groups: separateGroups ? new List<Group> { new("G", "G"), new("H", "H") } : new List<Group> { new("G", "G") },
        staffList: new List<Staff> { new("X", 0), new("Y", separateGroups ? 1 : 0) },
        use2Patterns: false,
        groupShift: separateGroups ? new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1, 1 }, new List<int> { 1, 1, 1, 1 } } : new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1, 1 } },
        groupShiftApt: separateGroups ? new List<IReadOnlyList<string>> { new List<string> { "", "", "", "" }, new List<string> { "", "", "", "" } } : new List<IReadOnlyList<string>> { new List<string> { "", "", "", "" } },
        schedule: new List<IReadOnlyList<int>> { new List<int> { 1 }, new List<int> { 1 } },
        wishes: new Dictionary<string, int> { ["0,0"] = 3 },
        staffRange: new Dictionary<string, Range> { ["0,0"] = new("0", "0"), ["1,0"] = new("0", "0") });

    // 古泉 10/25 型: 未反映の希望セル（A）を covO 退避で B へ動かさない。希望の無い Y が退く。
    [Fact]
    public void CovOReliefNeverMovesAnUnreflectedWishCellToAThirdShift()
    {
        var st = CovOState(separateGroups: true);
        var on = V6HotfixPasses.ApplyCovOReliefPolish(st, st.Schedule.ToIntArray2D()).NewSchedule;
        Assert.Equal(1, on[0][0]); // X は A のまま
        Assert.Equal(2, on[1][0]); // Y が B へ
    }

    // RSI の covO 解消: X は 1・2 日目とも C を希望し「C→C」は禁止（希望どうしの衝突）。1 日目を希望へ戻すと
    // c3n(9000)>pref(8000) で悪化するので、旧挙動は第三のシフトへ逃がしていた。
    [Fact]
    public void RsiCovOFreeNeverMovesAnUnreflectedWishCellToAThirdShift()
    {
        var st = CovOState() with
        {
            EndDate = "2026-08-02",
            Shifts = new List<Shift> { SRest, new("A", "A", "1", ""), new("B", "B", "", ""), new("C", "C", "", "") },
            Schedule = new List<IReadOnlyList<int>> { new List<int> { 1, 3 }, new List<int> { 1, 0 } },
            Wishes = new Dictionary<string, int> { ["0,0"] = 3, ["0,1"] = 3, ["1,0"] = 1 },
            StaffRange = new Dictionary<string, Range>(),
            Cons3n = new List<C3Row> { new(new List<string> { "C", "C" }) },
        };
        var on = st.Schedule.ToIntArray2D();
        V6NativeOptimizer.ApplyCovOFree(st, on, new JavaRandom(1));
        Assert.Equal(1, on[0][0]); // X は A のまま
    }

    // 入口 hf66 と最終番兵の基準: 上限 0 で外すセルが未反映の希望セルなら、埋めシフト（休）でなく希望（B）へ。
    [Fact]
    public void CappedCellWithAnUnreflectedWishIsRefilledWithTheWish()
    {
        var st = CovOState() with
        {
            Wishes = new Dictionary<string, int> { ["0,0"] = 2 },
            StaffRange = new Dictionary<string, Range> { ["0,1"] = new("0", "0") },
        };
        var board = st.Schedule.ToIntArray2D();
        Assert.Equal(2, V6NativeOptimizer.Hf66DataHardening(st, board, "t")[0][0]);
        Assert.Equal(2, V6NativeOptimizer.ClearCappedCells(st, board).Schedule[0][0]);
    }

    // あとから足した希望（盤面は変えない＝setWish と同じ）: 規則 A は反映を妨げない（旧案「値によらず凍結」の退行の再発防止）。
    // 入口 hf67 → 決定的な後処理チェーンで、足した希望が載る（壁時計の探索は揺れるので測定側で見る）。
    // 共同 LNS の回数は既定だと golden でヒープ 1GB 超（Kotlin CI のテスト JVM は 512MB）なので絞る（Kotlin と同じ）。
    [Fact]
    public void AddedWishIsReflected()
    {
        var st0 = StateJsonSerializer.Parse(FixtureLoader.ReadRaw("golden_state.json"));
        var p0 = new Problem(st0);
        var board = p0.InitialAssignment();
        for (var a = 0; a < p0.S; a++) for (var b = 0; b < p0.T; b++) if (p0.WishLocked(a, b)) board[a][b] = p0.Wish[a][b];
        var rng = new JavaRandom(20260925L);
        int i, j; int[] alts;
        do
        {
            i = rng.NextInt(p0.S); j = rng.NextInt(p0.T);
            var ii = i; var jj = j;
            alts = p0.AllowedShiftsForStaff(i).Where(it => it != board[ii][jj] && p0.MayPlace(ii, it)).ToArray();
        } while (p0.Wish[i][j] >= 0 || alts.Length == 0);
        var k = alts[rng.NextInt(alts.Length)];
        var wishes = new Dictionary<string, int>(st0.Wishes) { [$"{i},{j}"] = k };
        var st = st0 with { Wishes = wishes, Schedule = board.Select(r => (IReadOnlyList<int>)r.ToList()).ToList() };
        Assert.True(new Problem(st).WishLocked(i, j) && board[i][j] != k, "足した希望は未反映から始まる");
        var entry = V6NativeOptimizer.Hf67HardRepair(st, board.Copy2D(), new JavaRandom(7)).Schedule;
        var on = V6HotfixPasses.RunPostOptimization(st, entry, "t", seed: 1L, deadlineMs: EngineClock.NowMs() + 3_600_000L,
            parameters: new V6HotfixPasses.PostOptimizationParams(Deterministic: true,
                C1LnsMaxEvaluations: 5_000, PersonalLnsMaxEvaluations: 5_000)).Schedule[i][j];
        Assert.Equal(k, on); // 足した希望が載る
    }
}
