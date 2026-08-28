using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;
// System.Range (built-in C# 8+ slice type, brought into scope by the SDK's implicit
// `global using global::System;`) collides by simple name with MagiEngine.Model.Range.
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

public class EliteIntegrationPolishTest
{
    // [検証で判明] 2職員同一群・単一勤務シフトの当初案は fair(群内公平化)/weekly(曜日平準化) が
    // 副次的に絡み、covO(weight1.0)が安いため s1 単独追加(b)だけでも weightedScore が before を
    // 下回り「単独で正式改善」になってしまっていた（手計算では見落としていたが check() は全19族を
    // 常に評価するため fair/weekly も非ゼロで寄与する）。2群1名ずつ(fair対象外=m<2)・2勤務シフト
    // X/Yで構成し直し、a/b とも「相手のシフトへ移る」ことで必ず covU(HARD,重み8000)を作る対称設計に
    // 変更（休/勤務の別を変えない=全候補でweeklyが定数化し、それも交絡しない）。これで両半移動は
    // hard>0という揺るぎない優先順位だけで非改善と判定でき、両方同時適用の完全swapだけが
    // 被覆・個人下限を同時に満たしhard=0へ改善する。
    private static MagiState State() => MinimalState.Build(
        startDate: "2026-08-01", endDate: "2026-08-01",
        shifts: new List<Shift>
        {
            new("休", "休", "", ""),
            new("X勤務", "X", "1", ""),
            new("Y勤務", "Y", "1", ""),
        },
        groups: new List<Group> { new("G0", "G0"), new("G1", "G1") },
        staffList: new List<Staff> { new("s0", 0), new("s1", 1) },
        use2Patterns: false,
        groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 }, new List<int> { 1, 1, 1 } },
        groupShiftApt: new List<IReadOnlyList<string>>
        {
            new List<string> { "", "", "" },
            new List<string> { "", "", "" },
        },
        schedule: new List<IReadOnlyList<int>> { new List<int> { 1 }, new List<int> { 2 } },
        wishes: new Dictionary<string, int>(),
        staffRange: new Dictionary<string, Range>
        {
            ["0,2"] = new("1", ""),
            ["1,1"] = new("1", ""),
        },
        needDay1: new Dictionary<string, string>(), needDay2: new Dictionary<string, string>(),
        cons1: new List<C1Row>(), cons2: new List<C2Row>(), cons3: new List<C3Row>(),
        cons3n: new List<C3Row>(), cons3m: new List<C3Row>(), cons3mn: new List<C3Row>(),
        cons41: new List<C41Row>(), cons42: new List<C42Row>());

    private static MagiState PinState() => MinimalState.Build(
        startDate: "2026-08-01", endDate: "2026-08-01",
        shifts: new List<Shift>
        {
            new("休", "休", "", ""),
            new("固定勤務", "X", "", ""),
            new("不足勤務", "Y", "1", ""),
        },
        groups: new List<Group> { new("G", "G") },
        staffList: new List<Staff> { new("s0", 0) },
        use2Patterns: false,
        groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } },
        groupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "", "" } },
        schedule: new List<IReadOnlyList<int>> { new List<int> { 1 } },
        wishes: new Dictionary<string, int>(),
        staffRange: new Dictionary<string, Range>
        {
            ["0,1"] = new("1", "1"),
            ["0,2"] = new("1", ""),
        },
        needDay1: new Dictionary<string, string>(), needDay2: new Dictionary<string, string>(),
        cons1: new List<C1Row>(), cons2: new List<C2Row>(), cons3: new List<C3Row>(),
        cons3n: new List<C3Row>(), cons3m: new List<C3Row>(), cons3mn: new List<C3Row>(),
        cons41: new List<C41Row>(), cons42: new List<C42Row>());

    private static AdaptiveElite Elite(MagiState st, int[][] schedule, HypothesisEpochRole role, bool bridge) =>
        AdaptiveElite.Create(schedule, UnifiedViolationChecker.Check(st, schedule), role, 1, 1, bridge);

    [Fact]
    public void DisagreementFusionCombinesTwoIndividuallyNonImprovingMovesIntoAFeasibleSwap()
    {
        var st = State();
        var root = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, root);
        // A: s0 を X→Y へ動かす。s0のY下限は満たすが、Xが空席(covU)・Yが2人(covO)の新規HARD/SOFTを作る。
        var a = root.Copy2D();
        a[0][0] = 2;
        // B: s1 を Y→X へ動かす。同型で X が2人(covO)・Y が空席(covU)を作る。
        var b = root.Copy2D();
        b[1][0] = 1;
        var ar = UnifiedViolationChecker.Check(st, a);
        var br = UnifiedViolationChecker.Check(st, b);
        Assert.False(AdaptiveEliteArchive.Better(ar, before), "A単独は正式改善でない");
        Assert.False(AdaptiveEliteArchive.Better(br, before), "B単独は正式改善でない");

        var result = EliteIntegrationPolish.Apply(
            st,
            root,
            new List<AdaptiveElite>
            {
                Elite(st, a, HypothesisEpochRole.HardDebtRsiPlus, true),
                Elite(st, b, HypothesisEpochRole.PersonalRsi, false),
            },
            () => false,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 3_000L,
            new EliteIntegrationPolish.Config(MaxPairs: 0, MaxFusionGroups: 4, MaxFusionCells: 4));
        var after = UnifiedViolationChecker.Check(st, result.Schedule);
        Assert.True(AdaptiveEliteArchive.Better(after, before));
        // weekly(曜日平準化)は両職員とも全期間「勤務」のまま(休/勤務の別を変えない設計)のため
        // 全候補で定数2のまま残る＝完全な0ではなく before(low×2=4) からの改善(4→2)を確認する。
        Assert.Equal(2, after.Total);
        Assert.Equal(2, result.Schedule[0][0]);
        Assert.Equal(1, result.Schedule[1][0]);
        Assert.True(result.FusionImprovements > 0);
        for (var i = 0; i < root.Length; i++) Assert.True(root[i].SequenceEqual(st.Schedule[i]));
    }

    [Fact]
    public void BridgeScheduleIsNeverReturnedDirectly()
    {
        var st = State();
        var root = st.Schedule.ToIntArray2D();
        var bridge = root.Copy2D();
        bridge[0][0] = 0;
        var result = EliteIntegrationPolish.Apply(
            st, root,
            new List<AdaptiveElite> { Elite(st, bridge, HypothesisEpochRole.HardDebtRsiPlus, true) },
            () => false,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 1_000L);
        for (var i = 0; i < root.Length; i++) Assert.True(root[i].SequenceEqual(result.Schedule[i]));
    }

    [Fact]
    public void ArchivedReportIsNotTrustedWithoutOfficialRecheck()
    {
        var st = State();
        var root = st.Schedule.ToIntArray2D();
        var worse = root.Copy2D();
        worse[0][0] = 0;
        var fakePerfect = UnifiedViolationChecker.Check(st, root) with
        {
            Total = 0, Hard = 0, Soft = 0, WeightedScore = 0.0,
        };
        var result = EliteIntegrationPolish.Apply(
            st, root,
            new List<AdaptiveElite>
            {
                AdaptiveElite.Create(worse, fakePerfect, HypothesisEpochRole.DayBlockAlns, 1, 1, false),
            },
            () => false,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 1_000L);
        for (var i = 0; i < root.Length; i++) Assert.True(root[i].SequenceEqual(result.Schedule[i]));
    }

    [Fact]
    public void ExactPinRegressionRejectsOtherwiseBetterElite()
    {
        var st = PinState();
        var root = st.Schedule.ToIntArray2D();
        var before = UnifiedViolationChecker.Check(st, root);
        var candidate = new[] { new[] { 2 } };
        var candidateReport = UnifiedViolationChecker.Check(st, candidate);
        Assert.True(AdaptiveEliteArchive.Better(candidateReport, before), "全体目的だけなら改善する反例");
        var result = EliteIntegrationPolish.Apply(
            st, root,
            new List<AdaptiveElite> { Elite(st, candidate, HypothesisEpochRole.PersonalRsi, false) },
            () => false,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 1_000L);
        for (var i = 0; i < root.Length; i++) Assert.True(root[i].SequenceEqual(result.Schedule[i]));
    }

    [Fact]
    public void ExpiredDeadlineIsNoOpAndDoesNotMutateInput()
    {
        var st = State();
        var root = st.Schedule.ToIntArray2D();
        var saved = root.Copy2D();
        var result = EliteIntegrationPolish.Apply(st, root, new List<AdaptiveElite>(), () => false, 0L);
        for (var i = 0; i < root.Length; i++)
        {
            Assert.True(saved[i].SequenceEqual(root[i]));
            Assert.True(saved[i].SequenceEqual(result.Schedule[i]));
        }
    }

    // [賢く再構成, 3.268.0] relink/fusion のセル優先順位をc1優先(3段階)へ拡張した際に新設した
    // C1Cells の抽出正しさを直接検証する。Violations(1セル=最重1クラスのみ)だけを見ると、
    // c1がより重い違反(c3n等)と同一セルで重なった場合に取りこぼす(3.205.0のC1Polish anchor選定と
    // 同型のシャドーイング)。C1Cells は CellFamilies(1セルの全クラスを保持)を見るため、
    // このシャドーイングがあっても正しく拾えることを固定する。
    [Fact]
    public void C1CellsFindsC1ViolationEvenWhenShadowedByHeavierViolationAtSameCell()
    {
        var report = new ViolationReport(
            Violations: new Dictionary<string, string> { ["0,0"] = "vio-c3n", ["1,1"] = "vio-c2" },
            NeedViolations: new Dictionary<string, string>(),
            CountViolations: new Dictionary<string, string>(),
            Breakdown: new Dictionary<string, int>(),
            Total: 0, Hard: 0, Soft: 0, WeightedScore: 0.0,
            CellFamilies: new Dictionary<string, IReadOnlyList<string>>
            {
                ["0,0"] = new List<string> { "vio-c3n", "vio-c1" },
                ["1,1"] = new List<string> { "vio-c2" },
            });
        var cells = EliteIntegrationPolish.C1Cells(report);
        Assert.Equal(new HashSet<(int, int)> { (0, 0) }, cells);
    }

    [Fact]
    public void C1CellsIsEmptyWhenNoCellHasC1()
    {
        var report = new ViolationReport(
            Violations: new Dictionary<string, string> { ["0,0"] = "vio-c2" },
            NeedViolations: new Dictionary<string, string>(),
            CountViolations: new Dictionary<string, string>(),
            Breakdown: new Dictionary<string, int>(),
            Total: 0, Hard: 0, Soft: 0, WeightedScore: 0.0,
            CellFamilies: new Dictionary<string, IReadOnlyList<string>> { ["0,0"] = new List<string> { "vio-c2" } });
        Assert.Empty(EliteIntegrationPolish.C1Cells(report));
    }

    // ---- [3.349.2/敵対検証] 素材が使えないときの早期 return -------------------------------------
    // 旧実装はここで**ログを1行も返さなかった**ので、実機ログから「統合が走ったが素材が無かった」のか
    // 「そもそも呼ばれていない」のかが読めなかった。ただし PORTFOLIO 以外は毎回 elites が空なので、
    // **エリートはあったのに1件も使えなかったとき**だけ出す（全ワーカーが同じ解へ潰れた信号）。

    [Fact]
    public void CollapsedElitesAreReportedInsteadOfReturningSilently()
    {
        var st = State();
        var root = st.Schedule.ToIntArray2D();
        // 素材はあるが全部 root と同一＝統合の余地なし。
        var result = EliteIntegrationPolish.Apply(
            st, root,
            new List<AdaptiveElite>
            {
                Elite(st, root.Copy2D(), HypothesisEpochRole.PersonalRsi, false),
                Elite(st, root.Copy2D(), HypothesisEpochRole.HardDebtRsiPlus, false),
            },
            () => false,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 3_000L);
        Assert.Equal(0, result.ElitesUsed); // 使えた素材は0件
        Assert.Single(result.Logs); // ログを1行返す
        Assert.Contains("すべて現在の勤務表と同一", result.Logs[0].Message); // 潰れたことが読める
        for (var i = 0; i < root.Length; i++)
        {
            Assert.True(result.Schedule[i].SequenceEqual(root[i])); // 盤面は入口のまま
        }
    }

    [Fact]
    public void NoElitesAtAllStaysSilent()
    {
        // PORTFOLIO 以外の実行は毎回ここを通る。1行足すと全実行でノイズになるので出さない
        // （3.288.0 のログスパム対策と同じ判断）。
        var st = State();
        var root = st.Schedule.ToIntArray2D();
        var result = EliteIntegrationPolish.Apply(
            st, root, new List<AdaptiveElite>(),
            () => false, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 3_000L);
        Assert.Equal(0, result.ElitesUsed);
        Assert.Empty(result.Logs); // 素材ゼロのときは無言
    }
}
