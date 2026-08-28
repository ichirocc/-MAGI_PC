using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;
// System.Range (built-in C# 8+ slice type, brought into scope by the SDK's implicit
// `global using global::System;`) collides by simple name with MagiEngine.Model.Range.
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [フェーズ6, C1（期間要件）研磨] <see cref="V6HotfixPasses.ApplyC1WindowPolish"/> の検証。
///
/// [Kotlin原本] <c>C1RelocationPolishTest.kt</c>の7件を移植:
///  - <c>c1PolishAppliesMirrorRectangleWhenSameDaySwapIsRejected</c>→
///    <see cref="AppliesMirrorRectangleWhenSameDaySwapIsRejected"/>（手R1=鏡像長方形）。
///  - <c>c1PolishAppliesSelfSwapWhenNoOtherStaffCanTakeTheShift</c>→
///    <see cref="AppliesSelfSwapWhenNoOtherStaffCanTakeTheShift"/>（手R2=自己2日swap）。
///  - <c>c1PolishFindsAnchorEvenWhenC1MarkIsShadowedByHeavierViolationAtSameCell</c>→
///    <see cref="FindsAnchorEvenWhenC1MarkIsShadowedByHeavierViolationAtSameCell"/>
///    （cellFamiliesアンカー選定のシャドーイング退行回帰）。
///  - <c>c1PolishIsNoOpWhenAlreadySatisfied</c>→<see cref="IsNoOpWhenAlreadySatisfied"/>。
///  - <c>c1PolishLogsNoCandidateReasonWhenOnlyChainPartnerIsWishLocked</c>→
///    <see cref="LogsNoCandidateReasonWhenOnlyChainPartnerIsWishLocked"/>（頭打ち理由の可視化）。
///  - <c>c1PolishResolvesViaExhaustiveRepackWhenNoPartnerOrDonorExists</c>→
///    <see cref="ResolvesViaExhaustiveRepackWhenNoPartnerOrDonorExists"/>（手R3=全ペア網羅再配置）。
///  - <c>c1PolishRepackIsNoOpWhenAlreadyOptimallyPlaced</c>→
///    <see cref="RepackIsNoOpWhenAlreadyOptimallyPlaced"/>。
///
/// 加えて <c>ChainFillTest.kt</c>の <c>c1PolishSolvesViaChainWhenNoDirectSwapPartner</c>
/// （E11多人数玉突き連鎖＝手Bの検証）を <see cref="SolvesViaChainWhenNoDirectSwapPartner"/>
/// として移植する。<c>ChainFillTest.cs</c>のクラスdocが「<c>applyC1WindowPolish</c>に依存する
/// ためこのファイル(<c>V6SearchOperators.FindCovUChain</c>専用)へは書けない・フェーズ6で
/// このファイルが出来たときにそちら側へ追加する」と明示的に留保していたテスト。
///
/// 未移植（範囲外）: <c>c3PolishFindsAnchorEvenWhenC3mMarkIsShadowedByHeavierViolationAtSameCell</c>
/// は対象が別関数（<see cref="V6HotfixPasses.ApplyC3SequencePolish"/>、既に
/// <c>V6HotfixPassesCyclicSwapTest.cs</c>で別途カバー済み）。
/// <c>V6FinalBridgePortTest.kt</c>の <c>c1WindowPolishNeverWorsens</c> は共有フィクスチャ基盤
/// （<c>sampleState()</c>/<c>notWorseThan()</c>）が未移植のため据え置き。
/// </summary>
public class V6HotfixPassesC1WindowTest
{
    private static List<Shift> Shifts() => new()
    {
        new("Y", "Y", "", ""),   // index0 = 汎用シフト（need無し）
        new("X", "X", "", ""),  // index1 = cons1 対象シフト（need無し・c1のみで縛る）
    };

    /// <summary>
    /// 手R1（鏡像長方形）検証用の盤面。D=2,N=1窓・T=4日。
    /// i(職員0)=[X,X,Y,Y]: 手計算済み内訳 — 窓[0,1]=2X(余剰,day0安全) / 窓[1,2]=1X(day1は窓[1,2]がz=1&lt;=n=1で危険=非donor)
    ///   / 窓[2,3]=0X(不足=deficient)。donors={day0}のみ。
    /// i2(職員1)=[Y,Y,X,X]: 窓[0,1]=0X(deficient) / 窓[1,2]=1X(ok) / 窓[2,3]=2X(ok)。
    /// 手A(同日j=2のみ交換)を手計算すると: i 1→0fire(-1)・i2 1→2fire(+1)=総和±0で不採用（isBetterが拒否）。
    /// 手R1(day0とday2を同時に交換)は: i 1→0fire(-1)・i2 1→1fire(±0, 窓[0,1]解消/窓[1,2]新規で相殺)=総和-1で採用。
    /// 両職員とも同一グループ・両シフト担当可、needもwishもcons3nも無し＝covU/HARD不変。
    /// [3.287.0 keep-best統一で強化] docstring どおりの「回数固定職員」を staffRange の厳密ピン(X=2固定)で
    /// 実際に表現する。ピンを立てることで count-changing 手は low/high(90/45)+exactPinRegression で拒否され、
    /// 本テストの意図（移設だけが唯一の改善手である局面で R1 が機能する）が成立する。
    /// </summary>
    private static MagiState MirrorState()
    {
        var groups = new List<Group> { new("G0", "G0") };
        var staff = new List<Staff> { new("i", 0), new("i2", 0) };
        var schedule = new List<IReadOnlyList<int>>
        {
            new List<int> { 1, 1, 0, 0 },   // i:  X,X,Y,Y
            new List<int> { 0, 0, 1, 1 },   // i2: Y,Y,X,X
        };
        return MinimalState.Build(
            startDate: "2026-01-01", endDate: "2026-01-04",
            shifts: Shifts(), groups: groups, staffList: staff, use2Patterns: false,
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
            schedule: schedule,
            // X回数を厳密ピン（両職員とも現状=2）
            staffRange: new Dictionary<string, Range> { ["0,1"] = new("2", "2"), ["1,1"] = new("2", "2") },
            cons1: new List<C1Row> { new("2", "X", "1") });
    }

    [Fact]
    public void AppliesMirrorRectangleWhenSameDaySwapIsRejected()
    {
        var st = MirrorState();
        var sched = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, sched);
        Assert.Equal(0, before.Hard); // 初期 HARD=0（covU/c3n無し）
        Assert.True(before.Breakdown.GetValueOrDefault("c1", 0) > 0); // 初期 c1>0

        var res = V6HotfixPasses.ApplyC1WindowPolish(st, sched, maxPasses: 1);
        var after = UnifiedViolationChecker.Check(st, res.NewSchedule);

        Assert.True(res.Applied > 0, "鏡像長方形が採用されたこと（手計算どおり同日スワップは総和±0で不採用のはず）");
        Assert.True(after.Breakdown.GetValueOrDefault("c1", 0) < before.Breakdown.GetValueOrDefault("c1", 0), "c1 が減少したこと");
        Assert.Equal(0, after.Hard); // HARD 不変(=0)
        Assert.True(res.Logs.Any(l => l.Message.Contains("鏡像:1") || l.Message.Contains("鏡像:2")),
            "鏡像交換のログが記録されていること");

        // 鏡像交換は両職員の総シフト回数（多重集合）を保存する（i, i2 とも X/Y の総数が不変）。
        static (int Cx, int Cy) CountsOf(int[][] sc, int i) =>
            (sc[i].Count(v => v == 1), sc[i].Count(v => v == 0));
        var (bx0, by0) = CountsOf(sched, 0); var (ax0, ay0) = CountsOf(res.NewSchedule, 0);
        var (bx1, by1) = CountsOf(sched, 1); var (ax1, ay1) = CountsOf(res.NewSchedule, 1);
        Assert.Equal(bx0, ax0); // 職員0の X 回数保存
        Assert.Equal(by0, ay0); // 職員0の Y 回数保存
        Assert.Equal(bx1, ax1); // 職員1の X 回数保存
        Assert.Equal(by1, ay1); // 職員1の Y 回数保存
    }

    /// <summary>
    /// 手R2（自己2日swap）検証用の盤面。D=2,N=1窓・T=4日。
    /// i(職員0)=[X,X,Y,Y]（mirrorStateと同一行）: donors={day0}・deficient window=[2,3](day2)。
    /// i2(職員1)は別グループでXを担当不可（groupShift=[1,0]）＝全日Yに固定＝手A/手R1の相手候補が構造上ゼロ
    ///   （i2は常にX不保持のため work[i2][j]==X が成立しない）。よって手R2（自己内の付け替え）だけが唯一の解。
    /// 手計算: i 単独で day0(X)→Y・day2(Y)→X の入替えで 1fire→0fire（窓[2,3]解消・窓[0,1]は2X→1Xで依然ok）。
    /// </summary>
    private static MagiState SelfSwapState()
    {
        var groups = new List<Group> { new("G0", "G0"), new("G1", "G1") };
        var staff = new List<Staff> { new("i", 0), new("bystander", 1) };
        var schedule = new List<IReadOnlyList<int>>
        {
            new List<int> { 1, 1, 0, 0 },   // i:         X,X,Y,Y
            new List<int> { 0, 0, 0, 0 },   // bystander: Y,Y,Y,Y（Xを担当不可＝手A/R1の相手になり得ない）
        };
        return MinimalState.Build(
            startDate: "2026-01-01", endDate: "2026-01-04",
            shifts: Shifts(), groups: groups, staffList: staff, use2Patterns: false,
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 }, new List<int> { 1, 0 } },
            schedule: schedule,
            cons1: new List<C1Row> { new("2", "X", "1") });
    }

    [Fact]
    public void AppliesSelfSwapWhenNoOtherStaffCanTakeTheShift()
    {
        var st = SelfSwapState();
        var sched = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, sched);
        Assert.Equal(0, before.Hard); // 初期 HARD=0
        Assert.True(before.Breakdown.GetValueOrDefault("c1", 0) > 0); // 初期 c1>0

        var res = V6HotfixPasses.ApplyC1WindowPolish(st, sched, maxPasses: 1);
        var after = UnifiedViolationChecker.Check(st, res.NewSchedule);

        Assert.True(res.Applied > 0, "自己swapが採用されたこと（相方候補が存在しないため手A/R1は不可能）");
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("c1", 0)); // c1 が解消されたこと（0まで）
        Assert.Equal(0, after.Hard); // HARD 不変(=0)
        Assert.True(res.Logs.Any(l => l.Message.Contains("自己:1")), "自己swapのログが記録されていること");
        // 自己swapは職員0自身のX/Y総回数を保存する。
        var cx0Before = sched[0].Count(v => v == 1);
        var cx0After = res.NewSchedule[0].Count(v => v == 1);
        Assert.Equal(cx0Before, cx0After); // 職員0の X 回数保存
    }

    /// <summary>
    /// [実バグ修正/anchorStaff の重み優先シャドーイング] 旧実装は anchorStaff の判定に rep0.violations
    /// （1セル=最重1クラスのみ）を使っていた。i(職員0)の唯一のc1マーク位置(day2)に、より重いc3n(HARD)も
    /// 同時に発火すると、violations["0,2"]は"vio-c3n"に上書きされ"vio-c1"は消える。iの他の日には
    /// c1マークが無いため、iはanchorStaffから完全に漏れ、本来採用可能な手A(同日スワップ)すら一度も
    /// 試されず c1=1のまま採用0回になっていた（cellFamilies=1セルの全クラス保持マップへ切替えて解消）。
    /// i(職員0)=[X,X,Y,Y]・cons3n=[Y,Y]（day2,3が禁止連続の完全一致で c3n 発火・c1のマーク位置と一致）。
    /// i2(職員1)=[X,X,X,X]（day2にXを持つ唯一の交換相手）。手計算: 同日day2スワップで i の窓[2,3]が
    /// 0X→1Xで解消(c1 1→0)・i2は窓が全てz=2→z=1でも&gt;=1のため不変。かつc3nもi側day2がXになり消滅(1→0)。
    /// HARDが1→0に改善するため isBetter は自明に採用する（旧実装ではそもそも試行されなかった手）。
    /// </summary>
    private static MagiState ShadowedAnchorState()
    {
        var groups = new List<Group> { new("G0", "G0") };
        var staff = new List<Staff> { new("i", 0), new("i2", 0) };
        var schedule = new List<IReadOnlyList<int>>
        {
            new List<int> { 1, 1, 0, 0 },   // i:  X,X,Y,Y
            new List<int> { 1, 1, 1, 1 },   // i2: X,X,X,X
        };
        return MinimalState.Build(
            startDate: "2026-01-01", endDate: "2026-01-04",
            shifts: Shifts(), groups: groups, staffList: staff, use2Patterns: false,
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
            schedule: schedule,
            cons1: new List<C1Row> { new("2", "X", "1") },
            cons3n: new List<C3Row> { new(new List<string> { "Y", "Y" }) });
    }

    [Fact]
    public void FindsAnchorEvenWhenC1MarkIsShadowedByHeavierViolationAtSameCell()
    {
        var st = ShadowedAnchorState();
        var sched = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, sched);
        Assert.Equal(1, before.Hard); // 初期 HARD=1（c3n 1件）
        Assert.Equal(1, before.Breakdown.GetValueOrDefault("c1", 0)); // 初期 c1=1（day2窓のみ不足）
        // [前提確認] c1のマーク位置がc3nに上書きされ、旧実装ではiがanchorStaffから漏れていたことの確認。
        Assert.NotEqual("vio-c1", before.Violations.GetValueOrDefault("0,2")); // 職員0のday2セルはvio-c1を含まない（c3nに上書き済み）
        Assert.Contains("vio-c1", before.CellFamilies.GetValueOrDefault("0,2", Array.Empty<string>())); // しかしcellFamiliesにはvio-c1も残っている

        var res = V6HotfixPasses.ApplyC1WindowPolish(st, sched, maxPasses: 1);
        var after = UnifiedViolationChecker.Check(st, res.NewSchedule);

        Assert.True(res.Applied > 0, "cellFamilies切替えにより職員0がanchorに入り、同日スワップが試行・採用されること");
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("c1", 0)); // c1 が解消されたこと
        Assert.Equal(0, after.Hard); // HARD も解消されたこと（c3n 1->0）
    }

    [Fact]
    public void IsNoOpWhenAlreadySatisfied()
    {
        // 全窓が既に充足済み（X,X,X,X）なら anchorStaff が空になり即終了・採用0。
        var groups = new List<Group> { new("G0", "G0") };
        var staff = new List<Staff> { new("i", 0) };
        var schedule = new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1, 1 } };
        var st = MinimalState.Build(
            startDate: "2026-01-01", endDate: "2026-01-04",
            shifts: Shifts(), groups: groups, staffList: staff, use2Patterns: false,
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
            schedule: schedule,
            cons1: new List<C1Row> { new("2", "X", "1") });
        var sched = st.Schedule.ToIntArray2D();
        var res = V6HotfixPasses.ApplyC1WindowPolish(st, sched);
        Assert.Equal(0, res.Applied); // 既に充足済みでは採用0(no-op)
    }

    // [頭打ちの理由を可視化=RangePolish(3.222.0)と同型をC1Polishへ横展開] Aが「休 5日窓≥2」に恒常的に
    // 不足(休を一度も持たない)。手A(同日交換=誰も休を持たない)/手R1/R2(donors()=Aは休を保有しない為
    // 常に空)は全滅し、手B(直接移動+玉突き)だけが唯一の経路になるが、唯一の玉突き候補Bが全日希望固定
    // (Z)のため findCovUChain が候補を1人も見つけられず「候補なし」で頭打ちする。ログの残存表示に
    // その理由が出ることを固定する。
    [Fact]
    public void LogsNoCandidateReasonWhenOnlyChainPartnerIsWishLocked()
    {
        var shifts = new List<Shift>
        {
            new("休", "休", "", ""),
            new("X", "X", "1", ""),
            new("Y", "Y", "", ""),
            new("Z", "Z", "", ""),
        };
        var groups = new List<Group> { new("GA", "GA"), new("GB", "GB") };
        var groupShift = new List<IReadOnlyList<int>>
        {
            new List<int> { 1, 1, 1, 0 }, // GA(A)=休,X,Y
            new List<int> { 1, 1, 0, 1 }, // GB(B)=休,X,Z
        };
        var staff = new List<Staff> { new("A", 0), new("B", 1) };
        var schedule = new List<IReadOnlyList<int>>
        {
            new List<int> { 1, 1, 1, 1, 1 }, // A = X×5（休を一度も持たない＝5日窓で常に不足）
            new List<int> { 3, 3, 3, 3, 3 }, // B = Z×5（需要なしだが全日希望固定＝玉突き候補として使えない）
        };
        var st = MinimalState.Build(
            startDate: "2026-08-01", endDate: "2026-08-05",
            shifts: shifts, groups: groups, staffList: staff, use2Patterns: false,
            groupShift: groupShift,
            schedule: schedule,
            wishes: new Dictionary<string, int> { ["1,0"] = 3, ["1,1"] = 3, ["1,2"] = 3, ["1,3"] = 3, ["1,4"] = 3 },
            cons1: new List<C1Row> { new("5", "休", "2") });
        var sched = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, sched);
        Assert.True(before.Breakdown.GetValueOrDefault("c1", 0) > 0); // 初期はc1違反があること

        var result = V6HotfixPasses.ApplyC1WindowPolish(st, sched, seed: 1L);
        Assert.Equal(0, result.Applied); // 唯一の玉突き候補が希望固定のため採用0回
        var msg = result.Logs.First().Message;
        Assert.True(msg.Contains("候補なし"), $"残存表示に候補なしの理由が出ること: {msg}");
        Assert.True(msg.Contains("A 休"), $"対象職員名(A)と休が出ること: {msg}");
    }

    /// <summary>
    /// [手R3・局所探索の強化=ユーザー指示「賢く深く網羅的に」] 単独職員(相手なし＝手A/R1不可能)・
    /// donors()が構造的に空(手R2不可能)・単独職員のためfindCovUChainも候補なし(手B「玉突き経由」不可能)
    /// という、既存の手A/R1/R2/手Bの玉突き経路が全滅する局面で、手R3(アンカー限定なしの全ペア網羅)だけが
    /// 解消できることを手計算(Pythonで独立検証済み)で確認する。d=3,n=1窓・T=6日・Xを2回(day0,day4、
    /// 互いに独立な窓しかカバーしない配置)。
    /// 手計算: 窓は4個(wStart=0..3)。day0は窓0のみをカバー(z=1,n=1でdonor対象外＝抜くと即NG)。
    /// day4は窓2,3をカバー(いずれもz=1でdonor対象外)。窓1のみ無人でfires=1。
    /// day4→day3への1回の交換で窓1,2,3を全てday3がカバーし(day0は窓0のまま)fires=0まで完全解消できる。
    /// [CI失敗で判明した見落とし修正] staffRangeでXの上限を保有回数(2)に固定しないと、手B(直接移動)が
    /// 「アンカー日を無条件にXへ追加する」だけでX回数を3回に増やし解決してしまう（単独職員かつneed無しの
    /// ためfindCovUChainの「玉突きが必要か」の判定自体が意味をなさず、直接追加がisBetterに素通りしていた）。
    /// X上限を現在の保有回数2に固定することで、手Bの回数増加による解決をhigh違反(重み90)として封じ、
    /// 手R3(回数保存)のみが解となるようにする。
    /// </summary>
    private static MagiState IsolatedRepackState()
    {
        var groups = new List<Group> { new("G0", "G0") };
        var staff = new List<Staff> { new("solo", 0) };
        var schedule = new List<IReadOnlyList<int>>
        {
            new List<int> { 1, 0, 0, 0, 1, 0 },   // X,Y,Y,Y,X,Y
        };
        return MinimalState.Build(
            startDate: "2026-01-01", endDate: "2026-01-06",
            shifts: Shifts(), groups: groups, staffList: staff, use2Patterns: false,
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
            schedule: schedule,
            // X(index1)の上限を現在の保有数(2)に固定
            staffRange: new Dictionary<string, Range> { ["0,1"] = new("", "2") },
            cons1: new List<C1Row> { new("3", "X", "1") });
    }

    [Fact]
    public void ResolvesViaExhaustiveRepackWhenNoPartnerOrDonorExists()
    {
        var st = IsolatedRepackState();
        var sched = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, sched);
        Assert.Equal(1, before.Breakdown.GetValueOrDefault("c1", 0)); // 初期 c1=1（窓1が不足）

        var res = V6HotfixPasses.ApplyC1WindowPolish(st, sched, maxPasses: 1);
        var after = UnifiedViolationChecker.Check(st, res.NewSchedule);

        Assert.True(res.Applied > 0, "手R3(全ペア再配置)が採用されたこと");
        Assert.Equal(0, after.Breakdown.GetValueOrDefault("c1", 0)); // c1 が完全解消されたこと（窓を全カバーする配置へ再構成）
        Assert.Equal(0, after.Hard); // HARD 不変(=0)
        Assert.True(res.Logs.Any(l => l.Message.Contains("再配置:1")), "再配置のログが記録されていること");
        // X の総回数は保存される（配置だけが変わる）。
        var cxBefore = sched[0].Count(v => v == 1);
        var cxAfter = res.NewSchedule[0].Count(v => v == 1);
        Assert.Equal(cxBefore, cxAfter); // X 回数保存
    }

    [Fact]
    public void RepackIsNoOpWhenAlreadyOptimallyPlaced()
    {
        // 既に窓を全カバーする最適配置（day0, day3）なら fires=0＝手R3も何もしない。
        var groups = new List<Group> { new("G0", "G0") };
        var staff = new List<Staff> { new("solo", 0) };
        var schedule = new List<IReadOnlyList<int>> { new List<int> { 1, 0, 0, 1, 0, 0 } }; // X,Y,Y,X,Y,Y
        var st = MinimalState.Build(
            startDate: "2026-01-01", endDate: "2026-01-06",
            shifts: Shifts(), groups: groups, staffList: staff, use2Patterns: false,
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
            schedule: schedule,
            cons1: new List<C1Row> { new("3", "X", "1") });
        var sched = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, sched);
        Assert.Equal(0, before.Breakdown.GetValueOrDefault("c1", 0)); // 既に最適配置でc1=0

        var res = V6HotfixPasses.ApplyC1WindowPolish(st, sched);
        Assert.Equal(0, res.Applied); // 既に最適なら採用0(no-op)
    }

    /// <summary>
    /// [Kotlin原本] <c>ChainFillTest.kt</c>の <c>c1PolishSolvesViaChainWhenNoDirectSwapPartner</c>。
    /// E11多人数玉突き連鎖（手B）が、同日の直接交換相手が存在しない局面でも
    /// <see cref="V6SearchOperators.FindCovUChain"/>経由で c1 不足を解消することを確認する。
    /// shift: 0=休 1=X(c1対象・need無) 2=A(need1・iのみ在勤) 3=B(need1・過剰=2人在勤)。
    /// i=A・h=B・h2=B（Bが2人＝過剰）。1日窓でXが1回以上＝毎日Xが必須(cons1)。
    /// day0にX在勤者がいない(直接交換相手なし)ため手Aは不成立＝手Bの玉突き連鎖だけが解。
    /// G0(i)="休,X,A"（Bは不可）・G1(h)="休,A,B"（Xは不可＝Aへ玉突きで補充できる唯一の候補）・
    /// G2(h2)="休,B"のみ（Aは不可）＝非対称配置で乱数シャッフル順に依らず解が一意になる。
    /// </summary>
    [Fact]
    public void SolvesViaChainWhenNoDirectSwapPartner()
    {
        var shifts = new List<Shift>
        {
            new("休", "休", "", ""), new("X", "X", "", ""),
            new("A", "A", "1", ""), new("B", "B", "1", ""),
        };
        var groups = new List<Group> { new("G0", "G0"), new("G1", "G1"), new("G2", "G2") };
        var groupShift = new List<IReadOnlyList<int>>
        {
            new List<int> { 1, 1, 1, 0 },
            new List<int> { 1, 0, 1, 1 },
            new List<int> { 1, 0, 0, 1 },
        };
        var staff = new List<Staff> { new("i", 0), new("h", 1), new("h2", 2) };
        var schedule = new List<IReadOnlyList<int>>
        {
            new List<int> { 2 }, new List<int> { 3 }, new List<int> { 3 },   // i=A, h=B, h2=B（Bが2人＝過剰）
        };
        var st = MinimalState.Build(
            startDate: "2026-08-01", endDate: "2026-08-01",
            shifts: shifts, groups: groups, staffList: staff, use2Patterns: false,
            groupShift: groupShift,
            schedule: schedule,
            cons1: new List<C1Row> { new("1", "X", "1") }); // 1日窓でXが1回以上＝毎日Xが必須
        var sched = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, sched);
        Assert.True(before.Breakdown.GetValueOrDefault("c1", 0) > 0, "c1(Xの窓不足)が前提");
        Assert.True(sched.All(row => row[0] != 1), "day0にX在勤者がいない(直接交換相手なし)が前提");

        var r = V6HotfixPasses.ApplyC1WindowPolish(st, sched);
        var after = UnifiedViolationChecker.Check(st, r.NewSchedule);

        Assert.Equal(0, after.Breakdown.GetValueOrDefault("c1", 0)); // 玉突き連鎖でc1不足が解消すること
        Assert.Equal(1, r.NewSchedule[0][0]); // i がXへ移ること
        Assert.Equal(2, r.NewSchedule[1][0]); // h がAへ玉突きで補充されること
        Assert.Equal(3, r.NewSchedule[2][0]); // h2 はB(不変・1人に減って丁度need1)
        Assert.True(after.Hard <= before.Hard); // hard は悪化しない
        Assert.True(after.Total <= before.Total); // total も悪化しない（keep-best）
    }
}
