using MagiEngine.Model;
using MagiEngine.V6;
// System.Range (built-in C# 8+ slice type, brought into scope by the SDK's implicit
// `global using global::System;`) collides by simple name with MagiEngine.Model.Range.
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [フェーズ7ピース12] <see cref="V6SanityPort.BuildViolationDebug"/> の移植元テストの抽出
/// （Kotlin原本 <c>V6SanityPortTest.kt</c>、~60テストのうち <c>buildViolationDebug</c> を直接呼ぶ
/// 3件のみ）: <c>violationDebugReportsC1CountsPerStaffAndRule</c>／
/// <c>violationDebugShowsFiresAndLocationsWhenTheyDiffer</c>／
/// <c>coverageDetailHeaderSeparatesFireCountFromLocationCount</c>。
///
/// 同ファイルの他のテストは <c>V6SanityPort.build()</c>（piece 16）・<c>buildGuidance</c>
/// （piece 14）・<c>ConstraintMus</c>（piece 13）向けで、それぞれ別ピースのテストファイルの対象の
/// ためここでは対象外。<see cref="V6SanityPort.SafeDayLabel"/> はこの3テストのいずれも直接検証しない
/// （Kotlin側は private でテストが無い。piece 2 の <c>V6SanityPort.Core.cs</c> の doc comment に
/// あるとおり、この関数の Kotlin/.NET 分岐点は既に実 Kotlin ランタイムで検証済み）。
/// </summary>
public class V6SanityPortViolationDebugTest
{
    /// <summary>
    /// [3.227.0/c1内訳 移植元] 「違反詳細 c1(N件)」はDETAIL_CAP=8で打ち切られ職員別の内訳が読めない
    /// ため、職員×窓ルール別の全件集計を別行で出すようにした。s0のみ「休(5日窓≥2)」ルールに1件違反する
    /// 最小盤面で、正確な件数がその1行に出ることを固定する。
    /// </summary>
    [Fact]
    public void ViolationDebugReportsC1CountsPerStaffAndRule()
    {
        var st = new MagiState(
            StartDate: "2026-06-01", EndDate: "2026-06-07",
            Shifts: new List<Shift> { new("休", "休", "0", ""), new("A", "A", "0", "") },
            Groups: new List<Group> { new("G", "G") },
            StaffList: new List<Staff> { new("s0", 0), new("s1", 0) },
            Use2Patterns: false,
            GroupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
            GroupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" } },
            // s0: 最初の5日が全て A（休0回、5日窓で休>=2に違反）／s1: 休とAの交互（常に窓内2回以上で違反なし）
            Schedule: new List<IReadOnlyList<int>>
            {
                new List<int> { 1, 1, 1, 1, 1, 0, 0 },
                new List<int> { 0, 1, 0, 1, 0, 1, 0 },
            },
            Wishes: new Dictionary<string, int>(), StaffRange: new Dictionary<string, Range>(),
            NeedDay1: new Dictionary<string, string>(), NeedDay2: new Dictionary<string, string>(),
            Cons1: new List<C1Row> { new("5", "休", "2") }, Cons2: new List<C2Row>(),
            Cons3: new List<C3Row>(), Cons3n: new List<C3Row>(), Cons3m: new List<C3Row>(), Cons3mn: new List<C3Row>(),
            Cons41: new List<C41Row>(), Cons42: new List<C42Row>(),
            SkillGroups: new List<Group>(), Cons41s: new List<C41Row>(), Cons42s: new List<C42Row>(),
            ShiftColors: new Dictionary<string, string>(), Extras: new Dictionary<string, System.Text.Json.JsonElement>());
        var sched = st.Schedule.ToIntArray2D();
        var report = UnifiedViolationChecker.Check(st, sched);
        Assert.True(report.Breakdown.GetValueOrDefault("c1", 0) > 0, "前提: c1違反が発生していること");
        var lines = V6SanityPort.BuildViolationDebug(st, sched, report);
        var summary = Assert.Single(lines, l => l.Contains("c1内訳"));
        // [3.282.0] 計数を「違反ラン先頭のみ」→ checker の inc と同じ「違反窓ごと」へ是正。
        //   この盤面は窓j=0(休0回)とj=1(休1回)の2窓が違反＝breakdown c1=2 と厳密に一致する
        //   （旧実装は連続窓を1ランとして「1件」と表示し breakdown と食い違う第3の計数だった）。
        Assert.Equal(2, report.Breakdown["c1"]);
        Assert.Contains("s0 休(5日窓≥2)2件", summary);
        Assert.DoesNotContain("s1 ", summary);
    }

    /// <summary>
    /// [3.282.0/新領域ログ監査 移植元] 違反詳細ヘッダは「最重クラスで解決済みのセル位置数」で
    /// breakdown の fire 数と意味が異なり（c3n=1 fireでもパターン全セルをmark等）、実機ログで
    /// 「c1(11件)」vs「UnifiedCheck c1=12」の食い違いとして混乱を生んでいた。fires(breakdown)と
    /// 位置数が異なるときは「件数F・場所N箇所」と両方を明示することを固定する。
    /// </summary>
    [Fact]
    public void ViolationDebugShowsFiresAndLocationsWhenTheyDiffer()
    {
        var st = new MagiState(
            StartDate: "2026-06-01", EndDate: "2026-06-03",
            Shifts: new List<Shift> { new("休", "休", "0", ""), new("A", "A", "0", "") },
            Groups: new List<Group> { new("G", "G") },
            StaffList: new List<Staff> { new("s0", 0) },
            Use2Patterns: false,
            GroupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
            GroupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" } },
            Schedule: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 0 } },   // A A 休 → 禁止連続 [A,A] に1 fire・mark は2セル
            Wishes: new Dictionary<string, int>(), StaffRange: new Dictionary<string, Range>(),
            NeedDay1: new Dictionary<string, string>(), NeedDay2: new Dictionary<string, string>(),
            Cons1: new List<C1Row>(), Cons2: new List<C2Row>(), Cons3: new List<C3Row>(),
            Cons3n: new List<C3Row> { new(new List<string> { "A", "A" }) },
            Cons3m: new List<C3Row>(), Cons3mn: new List<C3Row>(),
            Cons41: new List<C41Row>(), Cons42: new List<C42Row>(),
            SkillGroups: new List<Group>(), Cons41s: new List<C41Row>(), Cons42s: new List<C42Row>(),
            ShiftColors: new Dictionary<string, string>(), Extras: new Dictionary<string, System.Text.Json.JsonElement>());
        var sched = st.Schedule.ToIntArray2D();
        var report = UnifiedViolationChecker.Check(st, sched);
        Assert.Equal(1, report.Breakdown["c3n"]);   // 前提: c3n は1 fire
        var lines = V6SanityPort.BuildViolationDebug(st, sched, report);
        var detail = Assert.Single(lines, l => l.Contains("違反詳細 c3n"));
        Assert.Contains("c3n(件数1・場所2箇所)", detail);   // fire数と位置数が異なるときは両方を明示すること
    }

    /// <summary>
    /// [3.380.0/実機ログ起因 移植元] `違反詳細 covO(...)` のヘッダが**場所数を件数として**出していた。
    /// 実機ログ: <c>UnifiedCheck covO=23</c> / <c>CoverageDiag 人員過剰 合計23 — 14枠</c> に対し
    /// <c>違反詳細 covO(14件)</c>。他の族は3.282.0で「件数F・場所N箇所」と書き分けているのに、被覆
    /// セクションの emit だけ fires を渡していなかった（covO は1枠が複数人ぶん超過しうるので両者が
    /// 大きく食い違う）。同じ report の中で数字が矛盾して見える。
    /// </summary>
    [Fact]
    public void CoverageDetailHeaderSeparatesFireCountFromLocationCount()
    {
        // 1日・休の必要人数0に対し3人とも休＝covO は 1枠で 3件。
        var st = new MagiState(
            StartDate: "2026-09-01", EndDate: "2026-09-01",
            Shifts: new List<Shift> { new("休", "休", "0", "") },
            Groups: new List<Group> { new("G", "G") },
            StaffList: new List<Staff> { new("A", 0), new("B", 0), new("C", 0) },
            Use2Patterns: false,
            GroupShift: new List<IReadOnlyList<int>> { new List<int> { 1 } },
            GroupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "" } },
            Schedule: new List<IReadOnlyList<int>> { new List<int> { 0 }, new List<int> { 0 }, new List<int> { 0 } },
            Wishes: new Dictionary<string, int>(), StaffRange: new Dictionary<string, Range>(),
            NeedDay1: new Dictionary<string, string>(), NeedDay2: new Dictionary<string, string>(),
            Cons1: new List<C1Row>(), Cons2: new List<C2Row>(),
            Cons3: new List<C3Row>(), Cons3n: new List<C3Row>(), Cons3m: new List<C3Row>(), Cons3mn: new List<C3Row>(),
            Cons41: new List<C41Row>(), Cons42: new List<C42Row>(),
            SkillGroups: new List<Group>(), Cons41s: new List<C41Row>(), Cons42s: new List<C42Row>(),
            ShiftColors: new Dictionary<string, string>(), Extras: new Dictionary<string, System.Text.Json.JsonElement>());
        var sched = new[] { new[] { 0 }, new[] { 0 }, new[] { 0 } };
        var rep = UnifiedViolationChecker.Check(st, sched);
        Assert.Equal(3, rep.Breakdown["covO"]);   // 前提: 1枠に3件ぶんの過剰が立つ
        var line = Assert.Single(V6SanityPort.BuildViolationDebug(st, sched, rep), l => l.Contains("違反詳細 covO"));
        // 件数と場所を書き分ける（旧: 場所数を件数として「covO(1件)」）。
        Assert.Contains("件数3・場所1箇所", line);
    }
}
