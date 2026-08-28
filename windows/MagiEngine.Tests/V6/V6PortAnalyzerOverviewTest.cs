using MagiEngine.Model;
using MagiEngine.V6;
// System.Range (built-in C# 8+ slice type, brought into scope by the SDK's implicit
// `global using global::System;`) collides by simple name with MagiEngine.Model.Range.
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [フェーズ7ピース9] <c>V6PortAnalyzerTest.kt</c>のうち <c>v6OverviewComputesAptAndRisk</c>
/// （<see cref="V6PortAnalyzer.Analyze"/> を運動させる唯一のテスト）を移植。
///
/// 同ファイルの残り十数件（<c>diagnoseCoverage*</c>/<c>diagnoseForbiddenRuns*</c>/
/// <c>blockedNow*</c>/<c>residualAnalysis*</c>/<c>infeasibleWish*</c>）はフェーズ7ピース3で
/// 既に <c>V6PortAnalyzerCoverageTest.cs</c>/<c>V6PortAnalyzerForbiddenTest.cs</c> として
/// 移植済み（対象は <see cref="V6PortAnalyzer.DiagnoseCoverage"/>/<see cref="V6PortAnalyzer.
/// DiagnoseForbiddenRuns"/> であり、本ピースの <c>Analyze</c> とは別の公開関数）。
/// </summary>
public class V6PortAnalyzerOverviewTest
{
    [Fact]
    public void V6OverviewComputesAptAndRisk()
    {
        var st = new MagiState(
            StartDate: "2025-12-01",
            EndDate: "2025-12-03",
            Shifts: new List<Shift> { new("休み", "休", "", ""), new("早番", "A", "1", "1") },
            Groups: new List<Group> { new("G", "G") },
            StaffList: new List<Staff> { new("s0", 0), new("s1", 0) },
            Use2Patterns: true,
            GroupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
            GroupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "2" } },
            Schedule: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 }, new List<int> { 0, 0, 0 } },
            Wishes: new Dictionary<string, int>(),
            StaffRange: new Dictionary<string, Range>(),
            NeedDay1: new Dictionary<string, string>(),
            NeedDay2: new Dictionary<string, string>(),
            Cons1: new List<C1Row>(),
            Cons2: new List<C2Row>(),
            Cons3: new List<C3Row>(),
            Cons3n: new List<C3Row>(),
            Cons3m: new List<C3Row>(),
            Cons3mn: new List<C3Row>(),
            Cons41: new List<C41Row>(),
            Cons42: new List<C42Row>(),
            SkillGroups: new List<Group>(),
            Cons41s: new List<C41Row>(),
            Cons42s: new List<C42Row>(),
            ShiftColors: new Dictionary<string, string>(),
            Extras: new Dictionary<string, System.Text.Json.JsonElement>());

        var report = UnifiedViolationChecker.Check(st);
        var v6 = V6PortAnalyzer.Analyze(st, st.Schedule.ToIntArray2D(), report);
        Assert.Equal(3, v6.Demand);
        Assert.Equal(100, v6.CoveragePct);
        Assert.True(v6.AptPenalty > 0.0);
        Assert.Contains(v6.SanityNotes, n => n.Contains("groupShiftApt"));
    }
}
