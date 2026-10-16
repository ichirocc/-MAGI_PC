using System.Threading;
using MagiEngine.Model;
using MagiEngine.V6;
// System.Range (built-in C# 8+ slice type, brought into scope by the SDK's implicit
// `global using global::System;`) collides by simple name with MagiEngine.Model.Range.
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [3.278.0/監査で実証された2クラッシュの回帰テスト, フェーズ6ピース30]
/// <c>PolishRobustnessTest.kt</c>のうち <see cref="V6HotfixPasses.RunPostOptimization"/> および
/// その依存 <see cref="C1RepairIndex"/>/<see cref="C1DeltaPrefilter"/> を直接運動させる2件を移植。
///
/// [フェーズ7ピース5, 訂正・追加移植] <c>tuningCountersDoNotLoseIncrementsUnderParallelWorkers</c>/
/// <c>tuningSummaryDistinguishesOffFromOnWithNoObservedEffect</c> は、フェーズ6当時「<c>TuningTelemetry</c>
/// は <c>RunPostOptimization</c>本体から一切参照されない」ため対象外としていたが、この判断は
/// <b>「<c>TuningTelemetry</c>自体がまだ移植されていない」</b>という一時的な理由に過ぎなかった
/// （<c>RunPostOptimization</c>と機能的に結合しているかどうかとは無関係）。ピース5で
/// <see cref="TuningTelemetry"/> を移植したので、Kotlin原本と同じくこのファイルへ2件とも追加した
/// （物理的な co-location は Kotlin 原本の構成をそのまま踏襲＝機能結合ではなく利便上の同居）。
///
/// 移植しなかった残り3件の理由:
///  - <c>minCostAssignmentReturnsNullForAllInfRowInsteadOfCrashing</c> →
///    <c>MinCostAssignmentTest.SolveReturnsNullWhenARowIsEntirelyInfeasible</c>等で既に移植済み。
///  - <c>dayAssignmentPolishSkipsInfeasibleDaysInsteadOfCrashing</c> →
///    <c>V6HotfixPassesDayAssignTest.SkipsInfeasibleDaysInsteadOfCrashing</c>で既に移植済み。
///  - <c>emptyStaffIsRejectedWithAnActionableMessage</c> → <c>V6FinalPort.handleOptimize</c> に依存し、
///    それは未移植（フェーズ7「V6FinalPort統括」のスコープ、ピース18）。
///
///  1. MinCostAssignment: 全INF行（担当可否ゼロの群の職員・-1センチネル等）で j1=-1 のまま p[-1] を読み
///     AIOOBE でプロセスごと落ちていた（handleOptimize フル経路=12秒予算で再現済み。ポテンシャル v[j] は
///     単調非増加のため全INF行では厳密更新が構造的に一度も起きない＝決定的クラッシュ）。C# 側はこの修正
///     （<c>MinCostAssignment.Solve</c> の nullable 化）を既に引き継いだ状態で移植済み。
///  2. C1RepairAnalysis.Opportunities: -1 セルを無検証で covUCell(-1,…)＝need1[-1][j] へ渡し AIOOBE
///     （hasActionableC1 ゲート／applyC1IndexChainRepair の両経路で再現済み・3.270.0 と同型の取り残し）。
///     C# 側は<see cref="C1RepairAnalysis"/>で <c>old &gt;= 0 &amp;&amp; old &lt; p.K</c> ガードとして
///     既に修正済み（本テストはその回帰を固定する）。
/// </summary>
public class PolishRobustnessTest
{
    // ==== [3.694.0] 設定の開始時・容認採用の行（整形）と、並列呼出しでも保持が 5 件を超えないこと ====

    [Fact]
    public void StartLineListsTheNineTogglesWithoutCounters()
    {
        Assert.Equal(
            "ネイティブ加速（設定）=ON / Kotlin照合（設定）=ON / 必須が増える候補の事前フィルタ=OFF / 禁止連続の崩し範囲=OFF / 仕上げ最適化=ON" +
            " / 結合探索を粘り強く=OFF / 一括見直しの自動調整=ON / 公平化/適切回数の他ソフト許容(6%)=OFF / 回数連鎖研磨=ON",
            TuningTelemetry.StartLine(nativeOn: true, parityOn: true, filterC3nIncrease: false, wideC3nBreakDays: false, softPolishOn: true,
                combineExhaustPairs: false, lnsAdaptive: true, aptFairSoftTolerance: false, countChainPolish: true));
        Assert.Equal(9, TuningTelemetry.ToggleWords.Count);
        // 「設定の効き」の語は従来のまま（ToggleWords から組む）。
        var summary = TuningTelemetry.Summary(nativeOn: false, parityOn: false, softPolishOn: false, combineExhaustPairs: false, lnsAdaptive: true, aptFairSoftTolerance: false, countChainPolish: false);
        Assert.StartsWith("ネイティブ加速=OFF / Kotlin照合=OFF / 必須が増える候補の事前フィルタ=", summary);
        Assert.Contains(" / 一括見直しの自動調整=ON / 公平化/適切回数の他ソフト許容(6%)=OFF / 回数連鎖研磨=OFF", summary);
    }

    [Fact]
    public void ToleratedLinesFormatIsFixedAndTheOverflowIsOneLine()
    {
        var note = new TuningTelemetry.ToleratedNote("apt", 4, 3, 120, 118, 0, 0, RawDelta: 5.0, Forgiven: 1.8, EffectiveDelta: -1.8);
        var lines = TuningTelemetry.ToleratedLines(new[] { note, note, note, note, note }, total: 7);
        Assert.Equal(6, lines.Count);
        Assert.Equal("1/7: apt 件数 4→3 ・ 合計 120→118 ・ 必須 0→0 ・ 容認 1.8 ・ 素の差 +5.0 ・ 実効差 -1.8", lines[0]);
        Assert.Equal("5/7: apt 件数 4→3 ・ 合計 120→118 ・ 必須 0→0 ・ 容認 1.8 ・ 素の差 +5.0 ・ 実効差 -1.8", lines[4]);
        Assert.Equal("ほか 2 件（先頭 5 件のみ）", lines[5]);
        Assert.Equal("1/1: fair 件数 2→1 ・ 合計 9→9 ・ 必須 1→1 ・ 容認 0.5 ・ 素の差 +0.0 ・ 実効差 -0.5",
            Assert.Single(TuningTelemetry.ToleratedLines(new[] { new TuningTelemetry.ToleratedNote("fair", 2, 1, 9, 9, 1, 1, 0.0, 0.5, -0.5) }, total: 1)));
        // 実効差 0.0（合計の減で採用）は符号なし＝Kotlin `toleratedLine` と同じ文字列。
        Assert.Equal("2/3: fair 件数 4→3 ・ 合計 120→118 ・ 必須 0→0 ・ 容認 1.8 ・ 素の差 +1.8 ・ 実効差 0.0",
            TuningTelemetry.ToleratedLines(new[] { note, new TuningTelemetry.ToleratedNote("fair", 4, 3, 120, 118, 0, 0, 1.8, 1.8, 0.0) }, total: 3)[1]);
        Assert.Empty(TuningTelemetry.ToleratedLines(Array.Empty<TuningTelemetry.ToleratedNote>(), total: 0));
    }

    [Fact]
    public void ParallelToleratedNotesNeverExceedTheCap()
    {
        TuningTelemetry.Reset();
        try
        {
            Parallel.For(0, 64, _ => TuningTelemetry.NoteTolerated("apt", 4, 3, 120, 118, 0, 0, 5.0, 1.8, -1.8));
            var notes = TuningTelemetry.ToleratedNotes();
            Assert.Equal(TuningTelemetry.ToleratedNoteCap, notes.Count);
            Assert.All(notes, n => Assert.Equal("apt", n.Family));
            Assert.Equal("ほか 59 件（先頭 5 件のみ）", TuningTelemetry.ToleratedLines(notes, total: 64)[5]);
        }
        finally { TuningTelemetry.Reset(); }
    }

    /// <summary>G1 = 担当可否が1つもチェックされていない群（正規のエディタ操作で作れるデータ）。</summary>
    private static MagiState EmptyBucketState()
    {
        var shifts = new List<Shift> { new("休", "休", "", "", ShiftRole.Rest), new("X", "X", "1", "") };
        return new MagiState(
            StartDate: "2026-01-01", EndDate: "2026-01-03",
            Shifts: shifts, Groups: new List<Group> { new("G0", "G0"), new("G1", "G1") },
            StaffList: new List<Staff> { new("s0", 0), new("s1", 1) }, Use2Patterns: false,
            GroupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 }, new List<int> { 0, 0 } },
            GroupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "" }, new List<string> { "", "" } },
            Schedule: new List<IReadOnlyList<int>> { new List<int> { 1, 0, 1 }, new List<int> { 0, 1, 0 } },
            Wishes: new Dictionary<string, int>(), StaffRange: new Dictionary<string, Range>(),
            NeedDay1: new Dictionary<string, string>(), NeedDay2: new Dictionary<string, string>(),
            Cons1: new List<C1Row>(), Cons2: new List<C2Row>(), Cons3: new List<C3Row>(),
            Cons3n: new List<C3Row>(), Cons3m: new List<C3Row>(), Cons3mn: new List<C3Row>(),
            Cons41: new List<C41Row>(), Cons42: new List<C42Row>(),
            SkillGroups: new List<Group>(), Cons41s: new List<C41Row>(), Cons42s: new List<C42Row>(),
            ShiftColors: new Dictionary<string, string>(),
            Extras: new Dictionary<string, System.Text.Json.JsonElement>());
    }

    /// <summary>範囲外セル(99)入り＝normalizeSchedule が -1 センチネルへ写像する盤面。c1不足窓が -1 日を含む。</summary>
    private static MagiState Neg1CellState()
    {
        var shifts = new List<Shift> { new("休", "休", "", "", ShiftRole.Rest), new("X", "X", "", ""), new("Y", "Y", "1", "") };
        return new MagiState(
            StartDate: "2026-01-01", EndDate: "2026-01-03",
            Shifts: shifts, Groups: new List<Group> { new("G", "G") },
            StaffList: new List<Staff> { new("s0", 0) }, Use2Patterns: false,
            GroupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } },
            GroupShiftApt: new List<IReadOnlyList<string>> { new List<string> { "", "", "" } },
            Schedule: new List<IReadOnlyList<int>> { new List<int> { 2, 99, 2 } },
            Wishes: new Dictionary<string, int>(), StaffRange: new Dictionary<string, Range>(),
            NeedDay1: new Dictionary<string, string>(), NeedDay2: new Dictionary<string, string>(),
            Cons1: new List<C1Row> { new("3", "X", "1") }, Cons2: new List<C2Row>(), Cons3: new List<C3Row>(),
            Cons3n: new List<C3Row>(), Cons3m: new List<C3Row>(), Cons3mn: new List<C3Row>(),
            Cons41: new List<C41Row>(), Cons42: new List<C42Row>(),
            SkillGroups: new List<Group>(), Cons41s: new List<C41Row>(), Cons42s: new List<C42Row>(),
            ShiftColors: new Dictionary<string, string>(),
            Extras: new Dictionary<string, System.Text.Json.JsonElement>());
    }

    [Fact]
    public void RunPostOptimizationSurvivesEmptyBucketStaff()
    {
        // 旧: applyDayAssignmentPolish 経由で runPostOptimization 全体（=handleOptimize 本番経路）が
        //   決定的にクラッシュしていた（12秒予算のフル経路で再現済み）。
        var s = EmptyBucketState();
        var res = V6HotfixPasses.RunPostOptimization(s, s.Schedule.ToIntArray2D(), algoName: "test", seed: 1L);
        var rep = UnifiedViolationChecker.Check(s, res.Schedule);
        Assert.True(rep.Total >= 0, "完走しレポートが得られる");
    }

    [Fact]
    public void C1IndexGateAndRepairSurviveNeg1Cells()
    {
        // 旧: opportunities が -1 セルで covUCell(-1,…)＝need1[-1][j] を読み、hasActionableC1 ゲートと
        //   applyC1IndexChainRepair の両方が AIOOBE。修正後は除去項0として扱い完走する。
        var s = Neg1CellState();
        var p = new Problem(s);
        var sc = s.Schedule.ToIntArray2D();
        Assert.Equal(-1, ScheduleUtil.NormalizeSchedule(sc, p)[0][1]); // 前提: 99 は -1 に正規化される
        var index = C1RepairIndex.Build(p, sc);
        Assert.True(C1DeltaPrefilter.HasActionableC1(index), "不足窓は正しく検出される（クラッシュせず）");
        var res = V6HotfixPasses.ApplyC1IndexChainRepair(s, sc);
        Assert.True(res.AfterTotal >= 0, "index駆動修復も完走する");
    }

    /// <summary>
    /// [Kotlin 3.360.1/敵対検証の回帰テスト] 8並列スレッド×20,000回の加算がちょうど16万になることを
    /// 固定する（<see cref="Interlocked"/> による原子加算でなければ、この並行負荷は確実に取りこぼす）。
    /// </summary>
    [Fact]
    public void TuningCountersDoNotLoseIncrementsUnderParallelWorkers()
    {
        TuningTelemetry.Reset();
        const int threads = 8;
        const int perThread = 20_000;
        using var gate = new CountdownEvent(1);
        var ts = Enumerable.Range(0, threads).Select(_ => new Thread(() =>
        {
            gate.Wait();
            for (var k = 0; k < perThread; k++) TuningTelemetry.IncrementParityChecks();
        })).ToList();
        foreach (var t in ts) t.Start();
        gate.Signal();
        foreach (var t in ts) t.Join();
        Assert.Equal(threads * perThread, TuningTelemetry.ParityChecksCount());
        TuningTelemetry.Reset();
        Assert.Equal(0, TuningTelemetry.ParityChecksCount());
    }

    /// <summary>
    /// [Kotlin 3.356.0の回帰テスト] トグル OFF は "OFF"、ON だが未発火は「この実行では観測なし」、
    /// 実際に加算されると件数を明示する。<c>Reset()</c> で前の実行の数字を持ち越さないことも固定する。
    /// </summary>
    [Fact]
    public void TuningSummaryDistinguishesOffFromOnWithNoObservedEffect()
    {
        var wide = PolishGate.WideC3nBreakDays;
        var filter = PolishGate.FilterC3nIncrease;
        try
        {
            TuningTelemetry.Reset();
            PolishGate.WideC3nBreakDays = false;
            PolishGate.FilterC3nIncrease = true;
            var off = TuningTelemetry.Summary(nativeOn: false, parityOn: false, softPolishOn: false,
                combineExhaustPairs: false, lnsAdaptive: false, aptFairSoftTolerance: false, countChainPolish: false);
            Assert.Contains("禁止連続の崩し範囲=OFF", off);
            Assert.Contains("必須が増える候補の事前フィルタ=ON(この実行では観測なし)", off);

            // Kotlin原本の `TuningTelemetry.c3nFilterSkipped.set(12)` に相当（このC#移植では直接の
            // Set は公開していないため、同じ終値になるまで公開の増分メソッドを繰り返す）。
            for (var i = 0; i < 12; i++) TuningTelemetry.IncrementC3nFilterSkipped();
            var on = TuningTelemetry.Summary(nativeOn: true, parityOn: true, softPolishOn: true,
                combineExhaustPairs: false, lnsAdaptive: false, aptFairSoftTolerance: false, countChainPolish: false);
            Assert.Contains("ネイティブ加速=ON", on);
            Assert.Contains("必須が増える候補の事前フィルタ=ON(12件", on);
            // reset で実行ごとの計測に戻ること（前の実行の数字を持ち越さない）。
            TuningTelemetry.Reset();
            Assert.Contains(
                "必須が増える候補の事前フィルタ=ON(この実行では観測なし)",
                TuningTelemetry.Summary(true, true, true, false, false, false, false));
        }
        finally
        {
            PolishGate.WideC3nBreakDays = wide;
            PolishGate.FilterC3nIncrease = filter;
            TuningTelemetry.Reset();
        }
    }
}
