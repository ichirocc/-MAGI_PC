using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;
// System.Range (built-in C# 8+ slice type, brought into scope by the SDK's implicit
// `global using global::System;`) collides by simple name with MagiEngine.Model.Range.
// This alias makes bare `Range` in this file resolve unambiguously to our Range record
// (matches MinimalState.cs's convention).
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [3.273.0/3.274.0/3.279.0/3.314.0/3.315.0 移植元] C1 Repair Analysis（A6）＋ 厳密窓修復（A2/A3/A4）の検証。
/// 各テストは「厳密探索が単一same-day swapの合成では到達できない多日多職員連動手を見つける」
/// または「coverage入替でも解消不能を証明する」ことを、手計算で答えを設計した最小盤面で固定する。
///
/// [移植メモ] Kotlin原本の11テストのうち2件（<c>passAppliesExactRepairAndIsKeepBestSafe</c> /
/// <c>passIsNoOpWhenNoCons1</c>）は <c>V6HotfixPasses.applyC1ExactWindowRepair</c> に依存するが、
/// この関数はフェーズ6でまだ移植されていない（<c>V6HotfixPasses.cs</c> 自体が未作成）。同じ
/// フェーズ内で後ほど移植される予定のため、その時点でこの2テストを追加する（今は意図的に
/// 省略＝コンパイルできない呼び出しを持ち込まない）。
/// </summary>
public class C1RepairAnalysisTest
{
    private static MagiState St(
        int days, int staff, IReadOnlyList<IReadOnlyList<int>> sched, IReadOnlyList<C1Row> cons1,
        IReadOnlyDictionary<string, Range>? staffRange = null, IReadOnlyList<C3Row>? cons3n = null)
    {
        string end = "2026-01-" + days.ToString().PadLeft(2, '0');
        var shifts = new List<Shift> { new("休", "休", "", ""), new("X", "X", "", ""), new("Y", "Y", "", "") };
        return MinimalState.Build(
            startDate: "2026-01-01", endDate: end,
            shifts: shifts, groups: new List<Group> { new("G", "G") },
            staffList: Enumerable.Range(0, staff).Select(i => new Staff($"s{i}", 0)).ToList(),
            use2Patterns: false,
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } },
            groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "", "" } },
            schedule: sched, staffRange: staffRange,
            cons1: cons1, cons3n: cons3n);
    }

    // ---- [3.315.0 移植元] 探索の目的関数を実採否と揃える（厳密ピン・c3n） -------------------------
    //
    // 共通盤面: 3日・2職員・ルール「X 2日窓≥1」（窓 [0,1] と [1,2]）。
    //   i0 = Y,Y,Y → 2窓とも不足        a = X,X,Y → 充足
    // X トークンは day0/day1 に1個ずつ。coverage 保存の並べ替えで到達できる配置は4通りで、
    // joint c1 は baseline=2、最小=1。最小を取る配置は必ず i0 が X を1個受け取る
    //   （i0 が X を取らない配置は i0 が2件のまま＝joint>=2）。手計算で全4通りを検算済み。

    private static MagiState PinFixture(
        IReadOnlyDictionary<string, Range>? staffRange = null, IReadOnlyList<C3Row>? cons3n = null) =>
        St(
            3, 2,
            new List<IReadOnlyList<int>> { new List<int> { 2, 2, 2 }, new List<int> { 1, 1, 2 } },
            new List<C1Row> { new("2", "X", "1") },
            staffRange, cons3n);

    [Fact]
    public void ExactSolveFindsPatchWhenNoPinOrForbiddenRunBlocksIt()
    {
        // 回帰: 制約が無ければ従来どおり joint 2→1 の手を見つける。
        var s = PinFixture();
        var p = new Problem(s);
        var sched = s.Schedule.ToIntArray2D();
        Assert.Equal(2, UnifiedViolationChecker.Check(s, sched).Breakdown.GetValueOrDefault("c1"));
        var v = C1RepairAnalysis.Analyze(p, sched).First(w => w.Staff == 0);
        var r = C1RepairAnalysis.SolveWindow(p, sched, v);
        Assert.Equal(2, r.BaselineJointC1); // baseline は joint 2
        Assert.Equal(1, r.MinJointC1); // coverage保存で joint 1 まで下げられる
        Assert.NotNull(r.Patch); // 改善手が出る
    }

    [Fact]
    public void ExactSolveRejectsPatchThatBreaksAnExactPin()
    {
        // i0 の X を 0回に固定（lo==hi==0・現状も0＝ピン充足中）。joint を下げる配置は必ず i0 が X を
        // 受け取るので、ピンを守る限り改善手は存在しない。旧実装は joint c1 しか見ずこれを提案していた。
        var s = PinFixture(staffRange: new Dictionary<string, Range> { ["0,1"] = new Range("0", "0") });
        var p = new Problem(s);
        var sched = s.Schedule.ToIntArray2D();
        var v = C1RepairAnalysis.Analyze(p, sched).First(w => w.Staff == 0);
        var r = C1RepairAnalysis.SolveWindow(p, sched, v);
        Assert.Null(r.Patch); // 厳密ピンを崩す手は候補にしない
        Assert.Equal(r.BaselineJointC1, r.MinJointC1); // 採用候補が無いので baseline のまま
    }

    [Fact]
    public void ExactSolveRejectsPatchThatCreatesForbiddenRun()
    {
        // 禁止「Y→X」。baseline は i0=Y,Y,Y / a=X,X,Y で fire 0。joint を下げる配置は i0 か a の
        // どちらかに Y→X を作る（X を後ろの日へ移すため）ので、c3n を増やさない限り改善手は存在しない。
        var s = PinFixture(cons3n: new List<C3Row> { new(new List<string> { "Y", "X" }) });
        var p = new Problem(s);
        var sched = s.Schedule.ToIntArray2D();
        Assert.Equal(0, UnifiedViolationChecker.Check(s, sched).Breakdown.GetValueOrDefault("c3n")); // baseline に禁止連続は無い
        var v = C1RepairAnalysis.Analyze(p, sched).First(w => w.Staff == 0);
        var r = C1RepairAnalysis.SolveWindow(p, sched, v);
        Assert.Null(r.Patch); // 禁止連続を増やす手は候補にしない
        Assert.Equal(r.BaselineJointC1, r.MinJointC1); // 採用候補が無いので baseline のまま
    }

    [Fact]
    public void AnalyzeEnumeratesDeficientWindowsMatchingChecker()
    {
        // i0: X X Y Y  a: Y Y X X, ルール「X 2日窓≥1」。checker の c1 と件数一致を確認。
        var s = St(
            4, 2,
            new List<IReadOnlyList<int>> { new List<int> { 1, 1, 2, 2 }, new List<int> { 2, 2, 1, 1 } },
            new List<C1Row> { new("2", "X", "1") });
        var p = new Problem(s);
        var sched = s.Schedule.ToIntArray2D();
        var rep = UnifiedViolationChecker.Check(s, sched);
        var vios = C1RepairAnalysis.Analyze(p, sched);
        Assert.Equal(rep.Breakdown.GetValueOrDefault("c1"), vios.Count); // analyze の窓件数は checker の c1 と一致
    }

    [Fact]
    public void ExactSolveFindsCoordinatedCrossDayMultiStaffMove()
    {
        // i0: X X Y Y  a: Y Y X X, coverage=各日{X,Y}固定. ルール「X 2日窓≥1」.
        // 唯一の0達成は day1,day2 双方の i0<->a swap（多日連動）＝単一same-day swapの1手では到達不能.
        var s = St(
            4, 2,
            new List<IReadOnlyList<int>> { new List<int> { 1, 1, 2, 2 }, new List<int> { 2, 2, 1, 1 } },
            new List<C1Row> { new("2", "X", "1") });
        var p = new Problem(s);
        var sched = s.Schedule.ToIntArray2D();
        var baseReport = UnifiedViolationChecker.Check(s, sched);
        Assert.Equal(2, baseReport.Breakdown.GetValueOrDefault("c1"));
        var v = C1RepairAnalysis.Analyze(p, sched).First(w => w.Staff == 0);
        var r = C1RepairAnalysis.SolveWindow(p, sched, v);
        Assert.True(r.Exhaustive, "探索を完了(exhaustive)");
        Assert.Equal(0, r.MinJointC1); // joint c1 を 0 まで下げられると証明
        Assert.NotNull(r.Patch); // 改善手が出る
        var patch = r.Patch!;
        Assert.True(patch.Select(op => op[1]).Distinct().Count() >= 2, "多日連動(2日以上を触る)");

        // 適用して checker で確認: c1=0・coverage保存
        var w = sched.Select(row => (int[])row.Clone()).ToArray();
        foreach (var op in patch) w[op[0]][op[1]] = op[2];
        var after = UnifiedViolationChecker.Check(s, w);
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("c1"));
        for (int d = 0; d < p.T; d++)
        {
            for (int k = 0; k < p.K; k++)
            {
                int beforeCount = Enumerable.Range(0, p.S).Count(i => sched[i][d] == k);
                int afterCount = Enumerable.Range(0, p.S).Count(i => w[i][d] == k);
                Assert.Equal(beforeCount, afterCount); // coverage保存 d=d k=k
            }
        }
    }

    [Fact]
    public void ExactSolveProvesCoverageNeutralWallWhenTokensAreTrulyScarce()
    {
        // [3.274.0 監査で再設計] 窓内に X トークンが1個しかない構成で i0 が「X 3日窓≥2」を要求。
        //   どう並べ替えても i0 は窓内 X を最大1回しか持てない＝真の壁（minFocusResidual=1>0 を証明）。
        //   旧テストは「各日 X 1個(計3個)」で i0 が2個取れる＝壁でない構成を、rows未復元バグ由来の
        //   false wall で「壁」と誤検出していたのを固定していた（本セッションの監査で判明・是正）。
        // day: 0 1 2  i0: Y Y Y (X=0)  a: X 休 休 (X=1のみ)  → 窓内 X トークンは day0 の1個だけ.
        var s = St(
            3, 2,
            new List<IReadOnlyList<int>> { new List<int> { 2, 2, 2 }, new List<int> { 1, 0, 0 } },
            new List<C1Row> { new("3", "X", "2") });
        var p = new Problem(s);
        var sched = s.Schedule.ToIntArray2D();
        var v = C1RepairAnalysis.Analyze(p, sched).First(w => w.Staff == 0);
        var r = C1RepairAnalysis.SolveWindow(p, sched, v);
        Assert.True(r.Exhaustive, "探索完了");
        Assert.True(r.FocusResidual > 0, "焦点は coverage入替でも X を2回持てない(残>0を証明)");
        Assert.Null(r.Patch); // 焦点のjoint改善patchは存在しない
        var walls = C1RepairAnalysis.ProvenWalls(p, sched);
        Assert.Contains(walls, x => x.Staff == 0 && x.Shift == 1); // provenWalls が i0 の真の壁を検出
    }

    [Fact]
    public void ProvenWallsDoesNotFalselyFlagWhenFocusIsCoverageNeutrallySatisfiable()
    {
        // [3.274.0 監査回帰] 各日 X が1個ずつ(計3個)なら i0 は day0,day2 の X を取って窓を充足できる
        //   ＝壁ではない。min-joint配置では i0 が1個止まりでも、min-focus では0にできる。旧バグは
        //   これを false wall と誤検出していた。健全化後は wall を出さないことを固定する。
        var s = St(
            3, 3,
            new List<IReadOnlyList<int>>
            {
                new List<int> { 2, 0, 2 }, new List<int> { 1, 2, 1 }, new List<int> { 0, 1, 0 },
            },
            new List<C1Row> { new("3", "X", "2") });
        var p = new Problem(s);
        var sched = s.Schedule.ToIntArray2D();
        var walls = C1RepairAnalysis.ProvenWalls(p, sched);
        Assert.DoesNotContain(walls, x => x.Staff == 0); // 解消可能な窓を壁と誤検出しない
    }

    [Fact]
    public void ProvenWallsExaminesEveryWindowNotJustTheFirstPerStaffShift()
    {
        // [3.279.0/外部レビューC1-04 移植元] 同一職員・同一シフトに独立した複数の不足窓。
        //   i0: [Y,Y,Y,Y,Y]・a: [X,休,休,休,休]・ルール「X 2日窓≥1」。
        //   窓[0,1]: day0 の X トークンを i0 が取れる＝解消可能（壁でない）。
        //   窓[1,2]: 列1・2に X トークンが存在しない＝どう並べ替えても解消不能（真の壁）。
        //   旧: seen が staff×shift のみで最初の窓[0,1]しか探索せず、後続の真の壁を見逃していた。
        var s = St(
            5, 2,
            new List<IReadOnlyList<int>>
            {
                new List<int> { 2, 2, 2, 2, 2 }, new List<int> { 1, 0, 0, 0, 0 },
            },
            new List<C1Row> { new("2", "X", "1") });
        var p = new Problem(s);
        var sched = s.Schedule.ToIntArray2D();
        var walls = C1RepairAnalysis.ProvenWalls(p, sched);
        Assert.Contains(walls, x => x.Staff == 0 && x.Start == 1); // 2窓目以降の真の壁を検出（旧実装は見逃し）
        Assert.DoesNotContain(walls, x => x.Staff == 0 && x.Start == 0); // 解消可能な窓[0,1]は壁と誤検出しない
    }

    /// <summary>
    /// [3.314.0 移植元] 「証明済み壁」は探索空間を尽くしたときだけ名乗ってよい。旧実装は余力職員を
    /// <b>同群限定</b>で集め、しかも <see cref="Config.MaxInvolvedStaff"/> の cap で候補を切り捨てた
    /// あとも exhaustive=true を返していた＝真部分集合しか見ていないのに壁を証明していた。cap=1
    /// （焦点職員のみ）で呼べば候補は必ず切り捨てられるので、exhaustive を名乗ってはならない。
    /// </summary>
    [Fact]
    public void TruncatedCandidateSetMustNotClaimAnExhaustiveProof()
    {
        // 2職員×4日、ルール「X 2日窓>=1」。i1 は X を持つので候補になり得るが cap=1 で切り捨てられる。
        var state = St(
            4, 2,
            new List<IReadOnlyList<int>> { new List<int> { 2, 2, 2, 2 }, new List<int> { 1, 1, 1, 1 } },
            new List<C1Row> { new("2", "X", "1") });
        var p = new Problem(state);
        var v = C1RepairAnalysis.Analyze(p, state.Schedule.ToIntArray2D()).FirstOrDefault();
        Assert.NotNull(v); // 不足窓が検出される前提
        var capped = C1RepairAnalysis.SolveWindow(
            p, state.Schedule.ToIntArray2D(), v!,
            new Config(MaxInvolvedStaff: 1));
        Assert.False(capped.Exhaustive, "候補を cap で切り捨てたら証明を名乗らない");
    }
}
