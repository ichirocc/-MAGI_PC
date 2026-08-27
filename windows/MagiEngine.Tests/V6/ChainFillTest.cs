using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;
// System.Range (built-in C# 8+ slice type) collides by simple name with MagiEngine.Model.Range;
// see MinimalState.cs for the same alias and rationale.
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// Faithful port of Kotlin's <c>ChainFillTest</c>.
///
/// [E11] <see cref="V6SearchOperators.FindCovUChain"/>（多人数ブロック移動）の検証。実機 2026-08
/// データでユーザーが手作業で見つけた「勤務→勤務」連鎖（既存の休→勤務修復では踏めない）を、決定的な
/// 連鎖探索が解けることを確認する。
///
/// [フェーズ5b, 移植範囲の限定] Kotlin原本の <c>c1PolishSolvesViaChainWhenNoDirectSwapPartner</c> は
/// このファイルへ移植しない。<c>V6HotfixPasses.applyC1WindowPolish</c>（フェーズ6・
/// <c>V6HotfixPasses.kt</c> の対象）に依存するテストで、そのファイルが存在しないと書けない。
/// フェーズ6で <c>V6HotfixPasses.cs</c> を移植する際に、そのテストファイル側へ追加する。
/// </summary>
public class ChainFillTest
{
    // 8/17 相当（深さ2連鎖）: covU=P。P可能者(上條)はQに在勤 → Qを空けると covU → Qは山本(Rに在勤)が埋め、
    // Rは過剰なので山本が抜けても充足。期待: 上條 Q→P, 山本 R→Q の2手。
    private static MagiState Depth2State() => MinimalState.Build(
        startDate: "2026-08-01", endDate: "2026-08-01",
        // shift: 0=休(need無) 1=P(need1) 2=Q(need1) 3=R(need1)
        shifts: new List<Shift>
        {
            new("休", "休", "", ""), new("P", "P", "1", ""), new("Q", "Q", "1", ""), new("R", "R", "1", ""),
        },
        groups: new List<Group> { new("G0", "G0"), new("G1", "G1"), new("G2", "G2") },
        // G0=休/P/Q, G1=休/Q/R, G2=休/R
        groupShift: new List<IReadOnlyList<int>>
        {
            new List<int> { 1, 1, 1, 0 },
            new List<int> { 1, 0, 1, 1 },
            new List<int> { 1, 0, 0, 1 },
        },
        // 上條∈G0(Qに在勤), 山本∈G1(Rに在勤), X∈G2(Rに在勤→Rを過剰に), Y∈G2(休)
        staffList: new List<Staff> { new("上條", 0), new("山本", 1), new("X", 2), new("Y", 2) },
        schedule: new List<IReadOnlyList<int>>
        {
            new List<int> { 2 }, // 上條 = Q
            new List<int> { 3 }, // 山本 = R
            new List<int> { 3 }, // X = R（R が2人＝過剰）
            new List<int> { 0 }, // Y = 休
        });

    [Fact]
    public void ChainFillSolvesDepth2Deadlock()
    {
        var st = Depth2State();
        var p = ScheduleUtil.CachedProblem(st);
        var sched = st.Schedule.ToIntArray2D();

        // 前提: P(shift1) が covU、既存の休→勤務修復では踏めない（休のYはPを担当不可＝G2）。
        var before = UnifiedViolationChecker.Check(st, sched);
        Assert.True(before.Breakdown.GetValueOrDefault("covU") > 0, "初期はP不足(covU>0)であること");

        // 連鎖探索（seed 固定で決定的）。P=shift1, j=0。
        var chain = V6SearchOperators.FindCovUChain(p, sched, 1, 0, new JavaRandom(42));
        Assert.NotNull(chain); // 勤務→勤務の連鎖が見つかること
        Assert.True(chain!.Count is >= 1 and <= 2, "2手以内の連鎖");

        // 適用して covU=0・hard 非増加を確認（keep-best 相当の妥当性）。
        foreach (var mv in chain) sched[mv[0]][mv[1]] = mv[2];
        var after = UnifiedViolationChecker.Check(st, sched);
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("covU")); // 連鎖適用後は covU=0
        Assert.True(after.Hard <= before.Hard, "hard は悪化しない");
    }

    // [玉突きの三連] 3人の交代連鎖でしか埋まらない局面: P<-Q<-R<-S（末端Sが過剰=余裕）。
    // a(Q担当) が P へ、b(R担当) が Q へ、c(S担当) が R へ動いて初めて covU=0 になり、
    // どの1人の直接移動でも別の covU を生むだけの「深さ3」を BFS が正しく踏むことを確認する。
    [Fact]
    public void ChainFillSolvesDepth3Cascade()
    {
        var shifts = new List<Shift>
        {
            new("休", "休", "", ""), new("P", "P", "1", ""), new("Q", "Q", "1", ""),
            new("R", "R", "1", ""), new("S", "S", "1", ""),
        };
        var groups = new List<Group> { new("G0", "G0"), new("G1", "G1"), new("G2", "G2"), new("G3", "G3") };
        // G0=休/P/Q, G1=休/Q/R, G2=休/R/S, G3=休/S（各群は隣接シフトのみ担当可＝連鎖を1本道にする）
        var groupShift = new List<IReadOnlyList<int>>
        {
            new List<int> { 1, 1, 1, 0, 0 },
            new List<int> { 1, 0, 1, 1, 0 },
            new List<int> { 1, 0, 0, 1, 1 },
            new List<int> { 1, 0, 0, 0, 1 },
        };
        var staff = new List<Staff> { new("a", 0), new("b", 1), new("c", 2), new("d", 3) };
        // a=Q, b=R, c=S, d=S（Sが2人＝過剰=末端の余裕）
        var schedule = new List<IReadOnlyList<int>>
        {
            new List<int> { 2 }, new List<int> { 3 }, new List<int> { 4 }, new List<int> { 4 },
        };
        var st = MinimalState.Build(
            startDate: "2026-08-01", endDate: "2026-08-01",
            shifts: shifts, groups: groups, staffList: staff,
            groupShift: groupShift, schedule: schedule);

        var p = ScheduleUtil.CachedProblem(st);
        var sched = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, sched);
        Assert.True(before.Breakdown.GetValueOrDefault("covU") > 0, "P不足(covU>0)が前提");

        var chain = V6SearchOperators.FindCovUChain(p, sched, 1, 0, new JavaRandom(11));
        Assert.NotNull(chain); // 3人連鎖が見つかること
        Assert.Equal(3, chain!.Count); // 深さ3(3手)の連鎖であること
        foreach (var mv in chain) sched[mv[0]][mv[1]] = mv[2];

        // a=P, b=Q, c=R, d=S(不変)
        Assert.Equal(new[] { 1, 2, 3, 4 }, new[] { sched[0][0], sched[1][0], sched[2][0], sched[3][0] });
        var after = UnifiedViolationChecker.Check(st, sched);
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("covU")); // covU が解消されること
        Assert.True(after.Hard <= before.Hard, "hard は悪化しない");
    }

    // [玉突きの五連] 5人の交代連鎖: P<-Q<-R<-S<-T<-U（末端Uが過剰=余裕）。maxDepth=5 の上限まで
    // BFS が正しく踏み、5手全てを一括で返すことを確認する（深さ3と同型の一本道を1段長くしたもの）。
    [Fact]
    public void ChainFillSolvesDepth5Cascade()
    {
        var shifts = new List<Shift>
        {
            new("休", "休", "", ""), new("P", "P", "1", ""), new("Q", "Q", "1", ""),
            new("R", "R", "1", ""), new("S", "S", "1", ""), new("T", "T", "1", ""), new("U", "U", "1", ""),
        };
        var groups = new List<Group>
        {
            new("G0", "G0"), new("G1", "G1"), new("G2", "G2"),
            new("G3", "G3"), new("G4", "G4"), new("G5", "G5"),
        };
        // G0=休/P/Q ... G4=休/T/U, G5=休/U（末端）。各群は隣接シフトのみ担当可＝連鎖を1本道にする。
        var groupShift = new List<IReadOnlyList<int>>
        {
            new List<int> { 1, 1, 1, 0, 0, 0, 0 },
            new List<int> { 1, 0, 1, 1, 0, 0, 0 },
            new List<int> { 1, 0, 0, 1, 1, 0, 0 },
            new List<int> { 1, 0, 0, 0, 1, 1, 0 },
            new List<int> { 1, 0, 0, 0, 0, 1, 1 },
            new List<int> { 1, 0, 0, 0, 0, 0, 1 },
        };
        var staff = new List<Staff>
        {
            new("a", 0), new("b", 1), new("c", 2), new("d", 3), new("e", 4), new("f", 5),
        };
        // a=Q, b=R, c=S, d=T, e=U, f=U（Uが2人＝過剰=末端の余裕）
        var schedule = new List<IReadOnlyList<int>>
        {
            new List<int> { 2 }, new List<int> { 3 }, new List<int> { 4 },
            new List<int> { 5 }, new List<int> { 6 }, new List<int> { 6 },
        };
        var st = MinimalState.Build(
            startDate: "2026-08-01", endDate: "2026-08-01",
            shifts: shifts, groups: groups, staffList: staff,
            groupShift: groupShift, schedule: schedule);

        var p = ScheduleUtil.CachedProblem(st);
        var sched = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, sched);
        Assert.True(before.Breakdown.GetValueOrDefault("covU") > 0, "P不足(covU>0)が前提");

        var chain = V6SearchOperators.FindCovUChain(p, sched, 1, 0, new JavaRandom(13));
        Assert.NotNull(chain); // 5人連鎖が見つかること
        Assert.Equal(5, chain!.Count); // 深さ5(5手)の連鎖であること
        foreach (var mv in chain) sched[mv[0]][mv[1]] = mv[2];

        Assert.Equal(
            new[] { 1, 2, 3, 4, 5, 6 }, // a=P,b=Q,c=R,d=S,e=T,f=U(不変)
            new[] { sched[0][0], sched[1][0], sched[2][0], sched[3][0], sched[4][0], sched[5][0] });
        var after = UnifiedViolationChecker.Check(st, sched);
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("covU")); // covU が解消されること
        Assert.True(after.Hard <= before.Hard, "hard は悪化しない");
    }

    // [3.232.0/ドッグフーディングで発見・maxDepth既定引き上げの検証] 深さ5(旧既定の上限)を1手超える
    // 深さ6の連鎖のみに解がある盤面（ChainFillSolvesDepth5Cascadeと同型を1段延長）。旧既定
    // maxDepth=5を明示指定すると見つからず、新既定((p.K-1).coerceAtLeast(1)=7)なら見つかることを固定する。
    [Fact]
    public void ChainFillFindsDepth6ChainOnlyReachableWithRaisedDefaultMaxDepth()
    {
        var shifts = new List<Shift>
        {
            new("休", "休", "", ""), new("P", "P", "1", ""), new("Q", "Q", "1", ""),
            new("R", "R", "1", ""), new("S", "S", "1", ""), new("T", "T", "1", ""),
            new("U", "U", "1", ""), new("V", "V", "1", ""),
        };
        var groups = new List<Group>
        {
            new("G0", "G0"), new("G1", "G1"), new("G2", "G2"), new("G3", "G3"),
            new("G4", "G4"), new("G5", "G5"), new("G6", "G6"),
        };
        // G0=休/P/Q ... G5=休/U/V, G6=休/V（末端）。各群は隣接シフトのみ担当可＝連鎖を1本道にする。
        var groupShift = new List<IReadOnlyList<int>>
        {
            new List<int> { 1, 1, 1, 0, 0, 0, 0, 0 },
            new List<int> { 1, 0, 1, 1, 0, 0, 0, 0 },
            new List<int> { 1, 0, 0, 1, 1, 0, 0, 0 },
            new List<int> { 1, 0, 0, 0, 1, 1, 0, 0 },
            new List<int> { 1, 0, 0, 0, 0, 1, 1, 0 },
            new List<int> { 1, 0, 0, 0, 0, 0, 1, 1 },
            new List<int> { 1, 0, 0, 0, 0, 0, 0, 1 },
        };
        var staff = new List<Staff>
        {
            new("a", 0), new("b", 1), new("c", 2), new("d", 3),
            new("e", 4), new("f", 5), new("g", 6), new("h", 6),
        };
        // a=Q,b=R,c=S,d=T,e=U,f=V,g=V,h=V（Vが3人＝過剰=末端の余裕、g/hがG6=休/Vのみ）
        var schedule = new List<IReadOnlyList<int>>
        {
            new List<int> { 2 }, new List<int> { 3 }, new List<int> { 4 }, new List<int> { 5 },
            new List<int> { 6 }, new List<int> { 7 }, new List<int> { 7 }, new List<int> { 7 },
        };
        var st = MinimalState.Build(
            startDate: "2026-08-01", endDate: "2026-08-01",
            shifts: shifts, groups: groups, staffList: staff,
            groupShift: groupShift, schedule: schedule);

        var p = ScheduleUtil.CachedProblem(st);
        Assert.Equal(8, p.K); // K=8シフト(休+P..V)である前提
        var sched = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, sched);
        Assert.True(before.Breakdown.GetValueOrDefault("covU") > 0, "P不足(covU>0)が前提");

        var capped = V6SearchOperators.FindCovUChain(p, sched, 1, 0, new JavaRandom(13), maxDepth: 5);
        Assert.Null(capped); // 旧既定maxDepth=5では深さ6の連鎖に届かないこと

        var chain = V6SearchOperators.FindCovUChain(p, sched, 1, 0, new JavaRandom(13));
        Assert.NotNull(chain); // 新既定((p.K-1)=7)なら深さ6の連鎖が見つかること
        Assert.Equal(6, chain!.Count); // 深さ6(6手)の連鎖であること
        foreach (var mv in chain) sched[mv[0]][mv[1]] = mv[2];
        var after = UnifiedViolationChecker.Check(st, sched);
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("covU")); // covU が解消されること
        Assert.True(after.Hard <= before.Hard, "hard は悪化しない");
    }

    // 8/11 相当（深さ1）: covU=Cｵ、唯一の Cｵ可能者が過剰シフト B4 に在勤 → 1手で covU/covO 同時解消。
    [Fact]
    public void ChainFillSolvesDepth1FromOvercoveredShift()
    {
        var shifts = new List<Shift>
        {
            new("休", "休", "", ""),
            new("Co", "Co", "1", ""), // need1=1
            new("B4", "B4", "1", ""), // need1=1（現状2＝過剰）
        };
        var groups = new List<Group> { new("G0", "G0") };
        var groupShift = new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } }; // 全員 休/Co/B4 可
        var staff = new List<Staff> { new("モニカ", 0), new("a", 0), new("b", 0) };
        var schedule = new List<IReadOnlyList<int>>
        {
            new List<int> { 2 }, // モニカ = B4
            new List<int> { 2 }, // a = B4（B4 が2人＝過剰）
            new List<int> { 0 }, // b = 休
        };
        var st = MinimalState.Build(
            startDate: "2026-08-01", endDate: "2026-08-01",
            shifts: shifts, groups: groups, staffList: staff,
            groupShift: groupShift, schedule: schedule);

        var p = ScheduleUtil.CachedProblem(st);
        var sched = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, sched);
        // Co(shift1) covU。ただし 休のbが Co を担当可能なので、休→勤務でも解けるが、連鎖は過剰B4からの
        // 1手（深さ1）も踏める。ここでは連鎖が非nullで covU を解消することを確認（どの1手でも可）。
        var chain = V6SearchOperators.FindCovUChain(p, sched, 1, 0, new JavaRandom(7));
        Assert.NotNull(chain);
        foreach (var mv in chain!) sched[mv[0]][mv[1]] = mv[2];
        var after = UnifiedViolationChecker.Check(st, sched);
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("covU"));
        Assert.True(after.Hard <= before.Hard);
    }

    // [三連/五連など任意長への配慮] 長さ2の枝刈りしか見ていないと、三連禁止(P,P,P)を新たに作る手を
    // 素通ししてしまう。a=[P,Q,P]・b=[休,休,休] で day1 の P不足を埋める候補は a/b の2人だが、
    // a の day1 を P にすると day0,1,2=P,P,P で三連禁止に触れる。b は無関係なので安全。
    // FindCovUChain が a を枝刈りし、b だけを使う連鎖（1手）に着地することを確認する。
    // [隣接日調整の対象外であることを保証] b を day0/day2 に希望固定し、a の禁止連続を隣接日調整
    //   （下記 ChainFillResolvesC3nBlockViaAdjacentDayFix）で回避できないようにする。これが無いと
    //   b が a の day0/day2 の肩代わりに使えてしまい、結果が非決定的（RNG次第でaかbか）になる。
    [Fact]
    public void ChainFillAvoidsTripleForbiddenRun()
    {
        // shift: 0=休 1=P(need1・cons3n=P,P,P三連禁止) 2=Q
        var shifts = new List<Shift> { new("休", "休", "", ""), new("P", "P", "1", ""), new("Q", "Q", "", "") };
        var groups = new List<Group> { new("G0", "G0") };
        var groupShift = new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } };
        var staff = new List<Staff> { new("a", 0), new("b", 0) };
        var schedule = new List<IReadOnlyList<int>>
        {
            new List<int> { 1, 2, 1 }, // a: P, Q, P（day1 を P にすると P,P,P で三連禁止）
            new List<int> { 0, 0, 0 }, // b: 休, 休, 休（day1 を P にしても無関係で安全）
        };
        var st = MinimalState.Build(
            startDate: "2026-08-01", endDate: "2026-08-03",
            shifts: shifts, groups: groups, staffList: staff,
            groupShift: groupShift, schedule: schedule,
            wishes: new Dictionary<string, int> { ["1,0"] = 0, ["1,2"] = 0 },
            cons3n: new List<C3Row> { new(new List<string> { "P", "P", "P" }) });

        var p = ScheduleUtil.CachedProblem(st);
        var sched = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, sched);
        // 前提: day1 は P が0人(covU)・三連はまだ未成立（a は P,Q,P で Q が割って入っている）。
        Assert.True(before.Breakdown.GetValueOrDefault("covU") > 0, "day1 の P不足(covU)が前提");
        Assert.Equal(0, before.Breakdown.GetValueOrDefault("c3n"));

        var chain = V6SearchOperators.FindCovUChain(p, sched, 1, 1, new JavaRandom(3));
        Assert.NotNull(chain); // day1 の P不足を埋める連鎖が見つかること
        foreach (var mv in chain!) sched[mv[0]][mv[1]] = mv[2];

        Assert.Equal(new[] { 1, 2, 1 }, sched[0]); // a の行は不変（三連トラップを避けて動かさない）
        Assert.Equal(1, sched[1][1]); // b の day1 が P で埋まる
        var after = UnifiedViolationChecker.Check(st, sched);
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("covU")); // covU が解消されること
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("c3n")); // 三連禁止(c3n)を新たに作らないこと
    }

    // [禁止連続の回避=隣接日調整] a=[P,Q,P]・b=[休,休,休]で day1 の P不足を埋めたいが、b は day1 に
    // 希望固定（休）で使えず、直接候補は a のみ。a を day1=P にすると三連禁止に触れるため、
    // FindCovUChain が a の day0 を休へ変えて三連を崩し、空いた day0 の P不足を b で玉突き充填する
    // （2段の合流手）ことを確認する。ユーザー指摘「禁止連続の並びにならないようにする」への対応。
    [Fact]
    public void ChainFillResolvesC3nBlockViaAdjacentDayFix()
    {
        var shifts = new List<Shift> { new("休", "休", "", ""), new("P", "P", "1", ""), new("Q", "Q", "", "") };
        var groups = new List<Group> { new("G0", "G0") };
        var groupShift = new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } };
        var staff = new List<Staff> { new("a", 0), new("b", 0) };
        var schedule = new List<IReadOnlyList<int>>
        {
            new List<int> { 1, 2, 1 }, // a: P, Q, P
            new List<int> { 0, 0, 0 }, // b: 休, 休, 休
        };
        var st = MinimalState.Build(
            startDate: "2026-08-01", endDate: "2026-08-03",
            shifts: shifts, groups: groups, staffList: staff,
            groupShift: groupShift, schedule: schedule,
            wishes: new Dictionary<string, int> { ["1,1"] = 0 }, // b は day1 のみ希望固定(休)＝直接候補から除外。day0/day2は自由。
            cons3n: new List<C3Row> { new(new List<string> { "P", "P", "P" }) });

        var p = ScheduleUtil.CachedProblem(st);
        var sched = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, sched);
        Assert.True(before.Breakdown.GetValueOrDefault("covU") > 0, "day1 の P不足(covU)が前提");
        Assert.Equal(0, before.Breakdown.GetValueOrDefault("c3n"));
        Assert.True(p.WishLocked(1, 1), "bはday1希望固定で直接候補から除外される前提");

        var chain = V6SearchOperators.FindCovUChain(p, sched, 1, 1, new JavaRandom(5));
        Assert.NotNull(chain); // 隣接日調整＋玉突きでday1のP不足を埋める連鎖が見つかること
        foreach (var mv in chain!) sched[mv[0]][mv[1]] = mv[2];

        Assert.Equal(1, sched[0][1]); // a の day1 が P で埋まる（禁止連続を回避しつつ使われる）
        Assert.Equal(0, sched[0][0]); // a の day0 が休へ変わり三連を崩す
        Assert.Equal(1, sched[1][0]); // 空いた day0 の P不足を b が玉突きで埋める
        var after = UnifiedViolationChecker.Check(st, sched);
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("covU")); // covU が解消されること
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("c3n")); // 三連禁止(c3n)を新たに作らないこと
        Assert.True(after.Hard <= before.Hard, "hard は悪化しない");
    }

    // Problem.MakesForbiddenRun 自体の直接検証（三連・五連）。
    [Fact]
    public void MakesForbiddenRunDetectsTripleAndQuintuple()
    {
        var shifts = new List<Shift> { new("休", "休", "", ""), new("P", "P", "", "") };
        var groups = new List<Group> { new("G0", "G0") };
        var staff = new List<Staff> { new("a", 0) };
        MagiState StateWith(List<int> sched, List<C3Row> cons3n) => MinimalState.Build(
            startDate: "2026-08-01", endDate: $"2026-08-0{sched.Count}",
            shifts: shifts, groups: groups, staffList: staff,
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
            schedule: new List<IReadOnlyList<int>> { sched },
            cons3n: cons3n);

        // 三連禁止(P,P,P)。row = P,休,P,休,休（休=0,P=1）。
        var row3 = new List<int> { 1, 0, 1, 0, 0 };
        var p3 = ScheduleUtil.CachedProblem(StateWith(row3, new List<C3Row> { new(new List<string> { "P", "P", "P" }) }));
        var sc3 = row3.ToArray();
        // position1(休)をPにすると positions0..2=P,P,P で三連成立。
        Assert.True(p3.MakesForbiddenRun(new[] { sc3 }, 0, 1, 1), "position1をPにすると三連禁止に触れる");
        // position3(休)をPにしても positions1..3=休,P,P / positions2..4=P,P,休 のどちらも三連に届かない。
        Assert.False(p3.MakesForbiddenRun(new[] { sc3 }, 0, 3, 1), "position3をPにしても三連には届かない");

        // 五連禁止(P×5)。row = P,P,休,P,P。position2(休)をPにすると全区間が P,P,P,P,P で五連成立。
        var row5 = new List<int> { 1, 1, 0, 1, 1 };
        var p5 = ScheduleUtil.CachedProblem(StateWith(row5, new List<C3Row> { new(Enumerable.Repeat("P", 5).ToList()) }));
        Assert.True(p5.MakesForbiddenRun(new[] { row5.ToArray() }, 0, 2, 1), "position2をPにすると五連禁止に触れる");
    }

    // [ユーザー指摘の検証=「Dﾃ-Dﾃ」仮説] 「移動先の翌日が別の禁止連続に触れるなら、同じシフトを
    //   もう一度充てる(例: 夜勤の連続=Dﾃ-Dﾃ)ことを試してみては」という提案の検証。実データ(cons3n=
    //   Dﾃ-A4/Aｱ/Cｵ/Cｱ/B4/Cｳ/B1・Dﾃ-休-A4/Aｱ の3連含む)を Python でリプレイしたところ、対象3名
    //   （実機ログの金沢勇輝=Dﾃ-Cｳ・モニカ=Dﾃ-休-Aｱ・アリフ=Dﾃ-Cｱ）はいずれも TryFixForbiddenRunViaAdjacentDay
    //   の altOrder 走査（休優先→担当可能シフト全種）で既に解決できることを確認。「同じシフトの
    //   繰り返し」は altOrder の2番目（休の次）に自然に含まれるため自動的に試されるが、単発では
    //   万能ではない: 翌々日が別の禁止連続の相手（例 Dﾃ-Aｱ）だと「Dﾃ-Dﾃ」自体が新たな禁止連続を
    //   翌日側へ1日ずらすだけで終わる。本テストは、この「繰り返しでは解決しないが、全候補探索の
    //   結果、別の安全なシフトで解決する」ケースを最小構成で再現し固定する:
    //   shift: 0=休 1=P(need1・充填対象=「Dﾃ」役) 2=N(P-Nが2連禁止＝「Aｱ」役) 3=O(禁止連続と無関係="有"役)
    //   cons3n = [P,N](2連) と [P,休,N](3連)。i の day1(j)=休(現在)・day2(j+1)=休・day3(j+2)=N。
    //   day1をPにすると [P,休,N] の3連禁止に触れる → 隣接日調整は day2 を別シフトへ変えようとする。
    //   1回目の試み(繰り返し=P, 「Dﾃ-Dﾃ」相当)は day2,3=[P,N] で2連禁止に新たに触れて失敗 → 続けて
    //   day2=N も day1,2=[P,N]で直ちに失敗 → day2=O でようやく成立（P-O・O-N とも禁止パターン外）。
    [Fact]
    public void ChainFillAdjacentFixTriesRepeatShiftThenFallsBackToSafeAlternative()
    {
        var shifts = new List<Shift>
        {
            new("休", "休", "", ""), new("P", "P", "", ""),
            new("N", "N", "", ""), new("O", "O", "", ""),
        };
        var groups = new List<Group> { new("G0", "G0") };
        var groupShift = new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1, 1 } };
        var staff = new List<Staff> { new("i", 0) };
        var schedule = new List<IReadOnlyList<int>> { new List<int> { 0, 0, 0, 2 } }; // day0=休, day1(j)=休, day2(j+1)=休, day3(j+2)=N
        var st = MinimalState.Build(
            startDate: "2026-08-01", endDate: "2026-08-04",
            shifts: shifts, groups: groups, staffList: staff,
            groupShift: groupShift, schedule: schedule,
            // [S=1のためP(need1)は「day1のみ」に限定] 基本need1を空にし、needDay1でday1だけ1を要求。
            //   基本need1="1"をシフト全日一律にすると、単一staffでは他日のPも埋まらずcovUが残ってしまう。
            needDay1: new Dictionary<string, string> { ["1,1"] = "1" },
            cons3n: new List<C3Row> { new(new List<string> { "P", "N" }), new(new List<string> { "P", "休", "N" }) });

        var p = ScheduleUtil.CachedProblem(st);
        var sched = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, sched);
        Assert.True(before.Breakdown.GetValueOrDefault("covU") > 0, "day1 の P不足(covU)が前提");
        Assert.Equal(0, before.Breakdown.GetValueOrDefault("c3n"));
        Assert.True(p.MakesForbiddenRun(sched, 0, 1, 1), "day1をPにすると[P,休,N]の3連禁止に触れる前提");
        // 「繰り返し(Dﾃ-Dﾃ相当)」= day2もPにする案は、day2,3=[P,N]で新たな2連禁止に触れ単体では不成立。
        var repeatSched = new[] { (int[])sched[0].Clone() };
        repeatSched[0][2] = 1;
        Assert.True(p.MakesForbiddenRun(repeatSched, 0, 2, 1), "繰り返し(day2もP)は day2,3=[P,N]で新たな禁止連続に触れる");

        var chain = V6SearchOperators.FindCovUChain(p, sched, 1, 1, new JavaRandom(9));
        Assert.NotNull(chain); // altOrder走査で「Dﾃ-Dﾃ」を試した上で別の安全なシフトへ着地すること
        foreach (var mv in chain!) sched[mv[0]][mv[1]] = mv[2];

        Assert.Equal(1, sched[0][1]); // i の day1 が P で埋まる
        Assert.Equal(3, sched[0][2]); // day2 は繰り返し(P)でもN でもなく安全なOへ
        var after = UnifiedViolationChecker.Check(st, sched);
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("covU")); // covU が解消されること
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("c3n")); // 三連禁止(c3n)を新たに作らないこと
        Assert.True(after.Hard <= before.Hard, "hard は悪化しない");
    }

    // [敵対的レビュー修正の回帰] TryComplete の静的 cnt[] 補正が「到着」だけでなく「離脱」も
    // 両方加味しないと、実際には別の covU を作る連鎖を安全と誤判定しうる（false accept）ことを固定する。
    // P(root,need1,0人)←Q(need2,2人=a,k1 在勤)←M(need1,1人=g 在勤) の3段連鎖: a が Q→P、g が M→Q、
    // k1 が Q→M と動く手は P を解消するが、正味では a と k1 の2人が Q を抜け g の1人しか戻らないため
    // Q が need2→1人 に壊れる。祖先の「到着」(g→Q)のみを補正し「離脱」(a→Q)を見逃す半端な修正だと
    // Q のtrueCntを3(過大)と誤算し、この有害な連鎖を安全と判定してしまう。正しい修正は到着と離脱を
    // 両方加味し trueCnt=2(不変)と正しく算出してこの連鎖を却下する。
    [Fact]
    public void ChainFillNeverBreaksAnotherShiftViaStaleAncestorCount()
    {
        var shifts = new List<Shift>
        {
            new("休", "休", "", ""), new("P", "P", "1", ""),
            new("Q", "Q", "2", ""), new("M", "M", "1", ""),
        };
        var groups = new List<Group> { new("G0", "G0"), new("G1", "G1") };
        // G0(a)=休/P/Q, G1(k1,g)=休/Q/M（k1・gは同一群＝同じ担当可能シフトで対称）
        var groupShift = new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1, 0 }, new List<int> { 1, 0, 1, 1 } };
        var staff = new List<Staff> { new("a", 0), new("k1", 1), new("g", 1) };
        var schedule = new List<IReadOnlyList<int>> { new List<int> { 2 }, new List<int> { 2 }, new List<int> { 3 } }; // a=Q, k1=Q（Qが2人=need2ちょうど）, g=M
        var st = MinimalState.Build(
            startDate: "2026-08-01", endDate: "2026-08-01",
            shifts: shifts, groups: groups, staffList: staff,
            groupShift: groupShift, schedule: schedule);

        var p = ScheduleUtil.CachedProblem(st);
        var sched = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, sched);
        Assert.True(before.Breakdown.GetValueOrDefault("covU") > 0, "P不足(covU>0)が前提");
        Assert.Null(before.NeedViolations.GetValueOrDefault("2,0")); // Qはちょうどneed2で充足済み(covU無し)が前提

        var chain = V6SearchOperators.FindCovUChain(p, sched, 1, 0, new JavaRandom(5));
        if (chain != null)
        {
            // 万一チェーンが見つかった場合でも、適用後に他シフト(Q含む)へ新たな covU を作らないこと
            // （見つからず null が最も一般的だが、候補順序の実装詳細に依存しないよう安全性で担保する）。
            foreach (var mv in chain) sched[mv[0]][mv[1]] = mv[2];
            var after = UnifiedViolationChecker.Check(st, sched);
            Assert.True(
                after.Breakdown.GetValueOrDefault("covU") <= before.Breakdown.GetValueOrDefault("covU"),
                "連鎖適用後に新たな covU を作らないこと");
        }
        // このデータでは有効な安全な連鎖が存在しない設計のため、見つからない(null)ことが期待値。
        Assert.Null(chain); // Qを壊す唯一の経路しか無いため連鎖は見つからない(null)であること
    }

    // [頭打ち調査・3.218.0] rangeAvoid が無いと、FindCovUChain は候補のコストを一切見ず rng順で最初に
    // 完成した候補をそのまま返す。桒澤美幸のAｱ超過(3.215.0 RangePolish)が研磨されずに残る実例を追跡した
    // 結果、「候補自身の新規range(high)違反を招く」手を引くと、isBetterが改善なしとして却下し、その日は
    // 二度と試行されない（1日1回きりの呼出のため）ことが根本原因と判明。
    // 盤面: 休(0)/P(1)の2シフト。day0=Pがcovid(covU)、bad/goodともにday0=休で担当可能・希望非固定・
    // 禁止連続なし＝どちらも構造的に同格の候補（1手で即完成）。bad は day1 に既にP保有＋staffRange hi=1
    // のため、day0のPを埋めると2>hi=1で自身の新規high違反を招く。good は無制限（staffRange未設定）。
    // rangeAvoid を渡すと、rng順に関わらず必ず good が選ばれることを複数seedで固定する
    // （渡さない場合は shuffle 次第で bad が選ばれ得ることも併せて確認＝旧実装の脆さの実証）。
    private static MagiState RangeAvoidState() => MinimalState.Build(
        startDate: "2026-08-01", endDate: "2026-08-02",
        shifts: new List<Shift> { new("休", "休", "", ""), new("P", "P", "1", "") },
        groups: new List<Group> { new("G0", "G0") },
        groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } }, // 休/Pとも担当可
        staffList: new List<Staff> { new("bad", 0), new("good", 0) },
        schedule: new List<IReadOnlyList<int>>
        {
            new List<int> { 0, 1 }, // bad = 休, P（day1に既にP保有）
            new List<int> { 0, 0 }, // good = 休, 休
        },
        staffRange: new Dictionary<string, Range> { ["0,1"] = new Range("", "1") }); // bad(index0)のP上限=1

    [Fact]
    public void ChainFillRangeAvoidAlwaysPrefersCandidateWithoutOwnRangeViolation()
    {
        var st = RangeAvoidState();
        var p = ScheduleUtil.CachedProblem(st);
        var before = UnifiedViolationChecker.Check(st, st.Schedule.ToIntArray2D());
        Assert.True(before.Breakdown.GetValueOrDefault("covU") > 0, "day0のP不足(covU>0)が前提");

        bool ExceedsHi(int staffIdx, int[][] work, int fillShift)
        {
            int hi = p.RangeHi[staffIdx][fillShift];
            if (hi == int.MaxValue) return false;
            int c = 0;
            for (int jj = 0; jj < p.T; jj++) if (work[staffIdx][jj] == fillShift) c++;
            return c + 1 > hi;
        }

        var badPickedWithoutAvoid = false;
        for (var seed = 0; seed <= 15; seed++)
        {
            var sched = st.Schedule.ToIntArray2D();
            var chain = V6SearchOperators.FindCovUChain(p, sched, 1, 0, new JavaRandom(seed));
            Assert.NotNull(chain); // 候補が2人ともいるため必ず連鎖(1手)が見つかること seed=$seed
            Assert.Single(chain!);
            if (chain[0][0] == 0) badPickedWithoutAvoid = true;

            var schedWithAvoid = st.Schedule.ToIntArray2D();
            var chainWithAvoid = V6SearchOperators.FindCovUChain(
                p, schedWithAvoid, 1, 0, new JavaRandom(seed),
                rangeAvoid: (staffIdx, fillShift) => ExceedsHi(staffIdx, schedWithAvoid, fillShift));
            Assert.NotNull(chainWithAvoid); // rangeAvoid指定時も連鎖は見つかること seed=$seed
            Assert.Equal(1, chainWithAvoid![0][0]); // rangeAvoid指定時はrng順に関わらずgood(index1)が選ばれること seed=$seed
        }
        Assert.True(
            badPickedWithoutAvoid,
            "rangeAvoid無しではrng順次第でbad(index0, 自身の新規high違反を招く候補)が選ばれ得ること" +
            "（旧実装の脆さの実証。全seedでgoodのみ選ばれるなら本テストの前提が崩れている）");
    }
}
