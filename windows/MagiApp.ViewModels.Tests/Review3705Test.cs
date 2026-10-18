using MagiEngine.V6;

namespace MagiApp.ViewModels.Tests;

/// <summary>Kotlin <c>Review3705Test</c> の 1 対 1 移植。3.705.0: 分析タブの状態の段（一文・3 点・主ボタン 1 つ）と、変更案で直せる必須違反の和集合。</summary>
public class Review3705Test
{
    private static UiState Made(Action<UiState>? edit = null)
    {
        var u = new UiState { Loaded = true, HasResult = true, EngineRan = true, CheckRev = 1, BestHard = 2L, BestSoft = 5L,
            Breakdown = new Dictionary<string, int> { ["c3n"] = 2, ["c1"] = 5 } };
        edit?.Invoke(u);
        return u;
    }

    private static UiState Fresh() => new() { Loaded = true };

    [Fact]
    public void SentenceFollowsBoardStatusDistinctions()
    {
        Assert.Equal("まだ作っていません。", AnalysisTriage.AnalysisSummaryOf(Fresh(), 0, null).Sentence);
        Assert.Equal("勤務表をつくっています。表示は途中の値です。", AnalysisTriage.AnalysisSummaryOf(Made(u => u.Running = true), 2, null).Sentence);
        Assert.Equal("手で編集した勤務表です（自動作成はまだ）。", AnalysisTriage.AnalysisSummaryOf(Made(u => u.EngineRan = false), 2, null).Sentence);
        Assert.Equal("必須2件 · 要調整5件", AnalysisTriage.AnalysisSummaryOf(Made(), 2, null).Sentence);
        Assert.Equal("必須0件 · 要調整5件",
            AnalysisTriage.AnalysisSummaryOf(Made(u => { u.BestHard = 0L; u.Breakdown = new Dictionary<string, int> { ["c1"] = 5 }; }), 0, null).Sentence);
        Assert.Equal("必須違反も要調整もありません。",
            AnalysisTriage.AnalysisSummaryOf(Made(u => { u.BestHard = 0L; u.BestSoft = 0L; u.Breakdown = new Dictionary<string, int>(); }), 0, null).Sentence);
        // 自動作成のあと手で変えた盤面は、上部の札と同じく「自動作成済み」の側。
        Assert.Equal("必須2件 · 要調整5件", AnalysisTriage.AnalysisSummaryOf(Made(u => { u.EngineRan = false; u.RanBeforeEdit = true; }), 2, null).Sentence);
    }

    [Fact]
    public void PrimaryIsTourThenGuidedElseNone()
    {
        var tour = AnalysisTriage.AnalysisSummaryOf(Made(), 3, null);
        Assert.Equal(AnalysisPrimary.Tour, tour.Primary);
        Assert.Equal("必須違反3か所を順に見る", tour.PrimaryLabel);
        var shortDiag = new CoverageDiagnosis(3, 0, 3, new[] { Shortfall(4), Shortfall(4, 1), Shortfall(9) }, Array.Empty<string>(), 0, Array.Empty<CoverageSurplus>());
        var guided = AnalysisTriage.AnalysisSummaryOf(Made(u => { u.Breakdown = new Dictionary<string, int> { ["covU"] = 3 }; u.CoverageDiag = shortDiag; }), 0, null);
        Assert.Equal(AnalysisPrimary.Guided, guided.Primary);
        Assert.Equal("なおし方を見る（2日）", guided.PrimaryLabel);
        Assert.Equal(AnalysisPrimary.None, AnalysisTriage.AnalysisSummaryOf(Made(u => u.BestHard = 0L), 3, null).Primary);
        Assert.Equal(AnalysisPrimary.None, AnalysisTriage.AnalysisSummaryOf(Made(u => u.Running = true), 3, null).Primary);
        Assert.Equal(AnalysisPrimary.None, AnalysisTriage.AnalysisSummaryOf(Fresh(), 0, null).Primary);
        Assert.Null(AnalysisTriage.AnalysisSummaryOf(Made(), 0, null).PrimaryLabel);
    }

    [Fact]
    public void ThirdPointOnlyWhenEvaluatedAndSearched()
    {
        Assert.Empty(AnalysisTriage.AnalysisSummaryOf(Made(), 2, null).Points);
        Assert.Equal("変更案で直せる必須違反 1か所", AnalysisTriage.AnalysisSummaryOf(Made(), 2, 1).Points[0]);
        Assert.Empty(AnalysisTriage.AnalysisSummaryOf(Made(u => u.CheckRev = 0), 2, 1).Points);
        Assert.Empty(AnalysisTriage.AnalysisSummaryOf(Made(u => u.Running = true), 2, 1).Points);
        Assert.Empty(AnalysisTriage.AnalysisSummaryOf(Fresh(), 0, 1).Points);
    }

    [Fact]
    public void ResolvedUnionCountsEachPlaceOnceAndChecksFreshness()
    {
        IReadOnlyList<FixCell> a = new[] { new FixCell(0, 1, 2) };
        IReadOnlyList<FixCell> b = new[] { new FixCell(1, 1, 0) };
        var k1 = new ViolationKey("c3n", ViolationKey.Kind.Cell, 0, 1);
        var k2 = new ViolationKey("covU", ViolationKey.Kind.Need, 2, 1);
        var union = CellSheetLogic.FixResolvedUnionOf(new[] { a, b }, ops => ReferenceEquals(ops, a) ? new HashSet<ViolationKey> { k1, k2 } : new HashSet<ViolationKey> { k2 });
        Assert.True(union.SetEquals(new[] { k1, k2 }));

        IReadOnlyList<IReadOnlyList<int>> sch = new[] { new[] { 1, 2 }, new[] { 0, 1 } };
        var sugg = new[]
        {
            new FixSuggestion(FixKind.Change, a, "", -1, -1, Array.Empty<(string, int)>()),
            new FixSuggestion(FixKind.Change, b, "", -1, -1, Array.Empty<(string, int)>()),
            new FixSuggestion(FixKind.Change, new[] { new FixCell(0, 0, 0) }, "", 0, -1, Array.Empty<(string, int)>()),
        };
        UiState Ui(Action<UiState>? edit = null) => Made(u =>
        {
            u.Schedule = sch; u.FixSuggestions = sugg; u.FixSearched = true; u.FixResolvedUnion = new FixResolvedUnion(new[] { a, b }, sch, union);
            edit?.Invoke(u);
        });
        Assert.Equal(2, CellSheetLogic.AnalysisFixResolved(Ui()));
        Assert.Null(CellSheetLogic.AnalysisFixResolved(Ui(u => u.FixSearched = false)));   // 未探索なら出さない
        Assert.Null(CellSheetLogic.AnalysisFixResolved(Ui(u => u.Schedule = new[] { new[] { 0, 2 }, new[] { 0, 1 } })));   // 盤面が変わったら出さない
        Assert.Null(CellSheetLogic.AnalysisFixResolved(Ui(u => u.FixSuggestions = sugg.Take(1).ToList())));   // 案が変わったら出さない
    }

    [Fact]
    public void IssueSettingsLabelNamesTheSection()
    {
        Assert.Equal("希望の設定を開く", AnalysisTriage.IssueSettingsLabel(IssueKind.Wish));
        Assert.Equal("必要人数の設定を開く", AnalysisTriage.IssueSettingsLabel(IssueKind.Demand));
        Assert.Equal("回数の設定を開く", AnalysisTriage.IssueSettingsLabel(IssueKind.Range));
        Assert.Equal("並び・期間の制約の設定を開く", AnalysisTriage.IssueSettingsLabel(IssueKind.Constraint));
    }

    private static CoverageShortfall Shortfall(int day, int shift = 0) =>
        new(day, "", shift, "", 2, 1, 1, 3, CoverageVerdict.Fixable, "");
}
