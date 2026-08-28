using MagiEngine.Model;
using MagiEngine.V6;
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// フェーズ7 ピース3（<c>V6PortAnalyzer.Coverage.cs</c>＝<c>DiagnoseCoverage</c>）の移植テスト。
///
/// <c>V6PortAnalyzerTest.kt</c>（Kotlin 側、全16件）のうち、この移植で対象なのは
/// <c>DiagnoseCoverage</c> を直接呼び、戻り値の型（<see cref="CoverageVerdict"/>/
/// <see cref="CoverageShortfall"/>/<see cref="CoverageSurplus"/>/<see cref="CoverageDiagnosis"/>）
/// にしか依存しない9件だけ。以下は明示的にスコープ外（依存先が未移植のため）:
///  - <c>v6OverviewComputesAptAndRisk</c>（piece 9, <c>V6PortAnalyzer.Analyze</c>）
///  - <c>diagnoseForbiddenRuns*</c> の8件（piece 4, <c>ForbiddenCellEscape</c>/<c>ForbiddenRunDiagnosis</c>）
///  - <c>forbiddenRunSeqLabelMatchesRuleKeyDerivedFromCons3nRows</c>（piece 4）
///  - <c>wishPinnedCellIsNotAWallWhenMovingItRemovesTwoForbiddenFires</c>（piece 4）
///  - <c>adjacentDayFixIsNotAnEscapeWhenItOnlyTradesForbiddenRunForABrokenWish</c>（piece 4）
///  - <c>residualAnalysisTreatsWishBlockedCovUAsAWallEvenWhenSupplyFloorIsZero</c>
///    （piece 17, <c>V6FinalPort.CovUBlockedAmount</c>/<c>CovUStructuralWall</c> 未移植。
///    ただし <c>V6PortAnalyzer.DiagnoseCoverage(CascadeChainState(cWished: true))</c> 自体は
///    piece 3 のみで再現できるため、その部分の前提だけ本ファイルの
///    <see cref="BlockedNowSeparatesStaticCapacityFromWhatCanActuallyBeFilledNow"/> で固定済み）
///
/// <c>DayLabel</c> 自体は Kotlin 側に直接のユニットテストが無いが、<see cref="V6SanityPort.SafeDayLabel"/>
/// との唯一の相違点（負のオフセットを拒否するガードが無い）が load-bearing な差なので、
/// <see cref="V6SanityPortTest"/> の <c>SafeDayLabel</c> テストと同じ規律で新規に固定する。
/// </summary>
public class V6PortAnalyzerCoverageTest
{
    private static readonly IReadOnlyDictionary<string, System.Text.Json.JsonElement> NoExtras =
        new Dictionary<string, System.Text.Json.JsonElement>();

    /// <summary>
    /// 26フィールド全指定の <see cref="MagiState"/> コンストラクタを直接叩く代わりの薄いビルダ。
    /// Kotlin 側テストが省略している（＝Kotlin のデータクラス既定値に委ねている）
    /// skillGroups/cons41s/cons42s/shiftColors/extras 等は、この9件どのテストでも変化しないため
    /// 固定で空にする。groupShiftApt は全テストとも groupShift と同じ形の空文字列のみ
    /// （適切回数の目標を1件も設定しない）なので自動で埋める。
    /// </summary>
    private static MagiState St(
        string startDate, string endDate,
        IReadOnlyList<Shift> shifts, IReadOnlyList<Group> groups, IReadOnlyList<Staff> staff,
        bool use2, IReadOnlyList<IReadOnlyList<int>> groupShift,
        IReadOnlyList<IReadOnlyList<int>> schedule,
        IReadOnlyDictionary<string, int>? wishes = null,
        IReadOnlyDictionary<string, Range>? staffRange = null) => new(
        StartDate: startDate, EndDate: endDate,
        Shifts: shifts, Groups: groups, StaffList: staff,
        Use2Patterns: use2,
        GroupShift: groupShift,
        GroupShiftApt: groupShift.Select(g => (IReadOnlyList<string>)g.Select(_ => "").ToList()).ToList(),
        Schedule: schedule,
        Wishes: wishes ?? new Dictionary<string, int>(),
        StaffRange: staffRange ?? new Dictionary<string, Range>(),
        NeedDay1: new Dictionary<string, string>(), NeedDay2: new Dictionary<string, string>(),
        Cons1: new List<C1Row>(), Cons2: new List<C2Row>(),
        Cons3: new List<C3Row>(), Cons3n: new List<C3Row>(), Cons3m: new List<C3Row>(), Cons3mn: new List<C3Row>(),
        Cons41: new List<C41Row>(), Cons42: new List<C42Row>(),
        SkillGroups: new List<Group>(), Cons41s: new List<C41Row>(), Cons42s: new List<C42Row>(),
        ShiftColors: new Dictionary<string, string>(),
        Extras: NoExtras);

    // [実バグ修正の回帰] DiagnoseCoverage が need1 のみを見て miss=need1-got を計算していたため、
    // need1 未設定・need2 単独定義（Problem.CovUCell の「片方定義=その値」対応セル）の covU 違反が
    // 診断から丸ごと消えていた。need1="" / need2="2" で1人しか配置しない盤面を使い、
    // CovUCell（source of truth）どおり不足1として検出されることを固定する。
    [Fact]
    public void DiagnoseCoverage_CatchesNeed2OnlyShortfall()
    {
        var st = St(
            startDate: "2025-12-01", endDate: "2025-12-01",
            shifts: new List<Shift> { new("休み", "休", "", ""), new("早番", "A", "", "2") },
            groups: new List<Group> { new("G", "G") },
            staff: new List<Staff> { new("s0", 0), new("s1", 0) },
            use2: true,
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
            schedule: new List<IReadOnlyList<int>> { new List<int> { 1 }, new List<int> { 0 } });

        var diag = V6PortAnalyzer.DiagnoseCoverage(st);
        Assert.Equal(1, diag.TotalShortfall);
        Assert.Single(diag.Shortfalls);
        var sf = diag.Shortfalls.Single();
        Assert.Equal(1, sf.ShiftIndex);
        Assert.Equal(1, sf.Got);
        Assert.Equal(1, sf.Miss);
    }

    // [3.263.0, 600秒改善ゼロの深い停滞調査で判明] 「玉突き」判定は1ホップ(直接移動が別のcovUを
    // 生むか)のみで、その先が実際に埋まる保証がなかった。実データ(FindCovUChainを200 seed総当たり)
    // で「玉突き候補はいるが下流の唯一の候補が希望固定で誰も動けない」真の壁を確認したため、
    // FindCovUChainで実在を検証してから案内を出し分けるよう修正。この2件は同一形状(X の covU、
    // Aが唯一の直接候補でYを空けるとcascade、CがYを埋める唯一の depth2 候補)で、Cの希望有無だけを
    // 変え、chainVerified の有無で案内文が変わることを固定する。
    private static MagiState CascadeChainState(bool cWished) => St(
        startDate: "2026-08-01", endDate: "2026-08-01",
        shifts: new List<Shift> { new("休", "休", "", ""), new("X", "X", "1", ""), new("Y", "Y", "1", "") },
        groups: new List<Group> { new("GA", "GA"), new("GC", "GC") },
        staff: new List<Staff> { new("A", 0), new("C", 1) },
        use2: false,
        // GA:休/X/Y可 / GC:休/Y可(Xは不可)
        groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 }, new List<int> { 1, 0, 1 } },
        // A=Y, C=休
        schedule: new List<IReadOnlyList<int>> { new List<int> { 2 }, new List<int> { 0 } },
        // Cが休に希望固定
        wishes: cWished ? new Dictionary<string, int> { ["1,0"] = 0 } : null);

    [Fact]
    public void DiagnoseCoverage_ConfirmsCascadeWhenChainActuallyResolves()
    {
        var diag = V6PortAnalyzer.DiagnoseCoverage(CascadeChainState(cWished: false));
        var sf = diag.Shortfalls.Single(s => s.ShiftIndex == 1);
        Assert.True(sf.Reason.Contains("玉突き=ブロック移動") && sf.Reason.Contains("必要"),
            "玉突き候補が本当に解消できるときは従来どおりの案内");
        Assert.False(sf.Reason.Contains("どう組んでも"), "「どう組んでも解消できません」は出さない");
    }

    [Fact]
    public void DiagnoseCoverage_WarnsWhenCascadeIsBlockedByDownstreamWish()
    {
        // Cが唯一のdepth2候補だが休へ希望固定＝FindCovUChainの候補から除外され連鎖が完成しない。
        var diag = V6PortAnalyzer.DiagnoseCoverage(CascadeChainState(cWished: true));
        var sf = diag.Shortfalls.Single(s => s.ShiftIndex == 1);
        Assert.True(sf.Reason.Contains("どう組んでも解消できません"), "連鎖が実在しないことを正直に案内する");
    }

    /// <summary>
    /// [3.344.0] 「充足可能N枠」というサマリと「いまの希望のままではどう組んでも解消できません」という
    /// 説明が同じ枠に同時に出ていた。<c>Verdict</c> は「担当できる人数 &gt;= 必要数」の静的判定なので
    /// Fixable のまま残すのが正しい（希望を1件変えれば直りうる）が、それだけを数えたサマリは
    /// 説明と矛盾する。実データ（real/user）でも「充足可能=3 不能=0」と出しながら3枠とも
    /// 「どう組んでも解消できません」だった。判定を文字列でなく値（<see cref="CoverageShortfall.BlockedNow"/>）
    /// として持たせる。
    /// </summary>
    [Fact]
    public void BlockedNowSeparatesStaticCapacityFromWhatCanActuallyBeFilledNow()
    {
        var blocked = V6PortAnalyzer.DiagnoseCoverage(CascadeChainState(cWished: true));
        var sfB = blocked.Shortfalls.Single(s => s.ShiftIndex == 1);
        Assert.Equal(CoverageVerdict.Fixable, sfB.Verdict); // 枠は足りているので verdict は Fixable のまま
        Assert.True(sfB.BlockedNow, "だが『いまの希望では埋められない』ことを値として持つ");
        Assert.Equal(1, blocked.BlockedNowSlots); // サマリと説明が一致する
        Assert.True(blocked.AllBlockedNow, "この盤面は探索を続けても covU が減らない");

        var fixable = V6PortAnalyzer.DiagnoseCoverage(CascadeChainState(cWished: false));
        var sfF = fixable.Shortfalls.Single(s => s.ShiftIndex == 1);
        Assert.False(sfF.BlockedNow, "玉突きが実在するなら『今は不能』とは言わない");
        Assert.Equal(0, fixable.BlockedNowSlots);
        Assert.False(fixable.AllBlockedNow, "再実行で解消し得る盤面を『減りません』と断定しない");
    }

    // [人員過剰(covO)の「なぜ減らないか」診断] 在勤2人のうち誰も希望固定・禁止連続に阻まれない盤面では
    // 「動かせる」人数が過剰人数と一致し、解消可能ヒントが出ることを固定する。
    [Fact]
    public void DiagnoseCoverage_MarksFreelyRelievableSurplus()
    {
        var st = St(
            startDate: "2025-12-01", endDate: "2025-12-01",
            shifts: new List<Shift> { new("休み", "休", "", ""), new("早番", "A", "1", "") },
            groups: new List<Group> { new("G", "G") },
            staff: new List<Staff> { new("s0", 0), new("s1", 0) },
            use2: false,
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
            // 両者ともA（必要1に対し現状2＝過剰1、休へ動かす余地あり）
            schedule: new List<IReadOnlyList<int>> { new List<int> { 1 }, new List<int> { 1 } },
            // [2026-08-27] 休（shift0）側に個人上限0を課し「動かすと high(45) が立つ」形にする
            //   （covO(重み5)より high がずっと重いので、covO の重みが今後さらに動いても崩れにくい）。
            staffRange: new Dictionary<string, Range> { ["0,0"] = new Range("0", "0"), ["1,0"] = new Range("0", "0") });

        var diag = V6PortAnalyzer.DiagnoseCoverage(st);
        Assert.Equal(1, diag.TotalSurplus);
        Assert.Single(diag.Surpluses);
        var sp = diag.Surpluses.Single();
        Assert.Equal(1, sp.ShiftIndex);
        Assert.Equal(2, sp.Got);
        Assert.Equal(1, sp.Excess);
        Assert.Contains("動かせる2人", sp.Reason);

        // [3.406.0] 旧テストは over-promise を固定していた。まず前提（この手は本当に改善しない）を
        //   engine で確かめてから、文言を固定する。休（shift0）の個人上限0により、動かすと
        //   high(重み45) が立ち、covO(重み5)の改善では割に合わない＝BetterReport は必ず拒否する。
        var moved = UnifiedViolationChecker.Check(st, new[] { new[] { 0 }, new[] { 1 } });
        var baseRep = UnifiedViolationChecker.Check(st, new[] { new[] { 1 }, new[] { 1 } });
        Assert.False(UnifiedViolationChecker.BetterReport(moved, baseRep), "1人動かす手は目的関数を改善しない");
        Assert.Contains("最適化は採用しません", sp.Reason);
        Assert.DoesNotContain("解消できます", sp.Reason);
        Assert.Equal("high", sp.BlockedFamily);
    }

    /// <summary>
    /// [3.406.0] 断言してよいのは実際に目的関数が良くなるときだけ。上と同じ形でも、2名を
    /// 別グループにすると fair は m&lt;2 で対象外になり、covO 1→0 が純粋な改善になる
    /// （実測: before total=3 → after total=2・BetterReport=true）。このときだけ
    /// 「『直し方を探す』で解消できます」と言い、主因は付けない。
    /// </summary>
    [Fact]
    public void DiagnoseCoverage_PromisesAFixOnlyWhenTheObjectiveActuallyImproves()
    {
        var st = St(
            startDate: "2025-12-01", endDate: "2025-12-01",
            shifts: new List<Shift> { new("休み", "休", "", ""), new("早番", "A", "1", "") },
            groups: new List<Group> { new("G0", "G0"), new("G1", "G1") },
            staff: new List<Staff> { new("s0", 0), new("s1", 1) },
            use2: false,
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 }, new List<int> { 1, 1 } },
            schedule: new List<IReadOnlyList<int>> { new List<int> { 1 }, new List<int> { 1 } });

        var moved = UnifiedViolationChecker.Check(st, new[] { new[] { 0 }, new[] { 1 } });
        var baseRep = UnifiedViolationChecker.Check(st, new[] { new[] { 1 }, new[] { 1 } });
        Assert.True(UnifiedViolationChecker.BetterReport(moved, baseRep), "1人動かす手は目的関数を改善する");

        var sp = V6PortAnalyzer.DiagnoseCoverage(st).Surpluses.Single();
        Assert.Contains("解消できます", sp.Reason);
        Assert.Null(sp.BlockedFamily);
    }

    // 両者とも希望固定（希望どおりに配置済み＝pref違反ゼロ）だと、動かすと希望未充足に化けるため
    // 「動かせる」人数は0になり、希望調整が必要という理由が出ることを固定する
    // （実機ログで「回数制限のない有が増えない」問い合わせの根本原因の再現）。
    [Fact]
    public void DiagnoseCoverage_MarksWishPinnedSurplusAsUnmovable()
    {
        var st = St(
            startDate: "2025-12-01", endDate: "2025-12-01",
            shifts: new List<Shift> { new("休み", "休", "", ""), new("早番", "A", "1", "") },
            groups: new List<Group> { new("G", "G") },
            staff: new List<Staff> { new("s0", 0), new("s1", 0) },
            use2: false,
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1 } },
            schedule: new List<IReadOnlyList<int>> { new List<int> { 1 }, new List<int> { 1 } },
            // 両者ともAを希望固定
            wishes: new Dictionary<string, int> { ["0,0"] = 1, ["1,0"] = 1 });

        var diag = V6PortAnalyzer.DiagnoseCoverage(st);
        Assert.Equal(1, diag.TotalSurplus);
        var sp = diag.Surpluses.Single();
        Assert.Equal(1, sp.Excess);
        Assert.Contains("希望固定2人", sp.Reason);
        Assert.Contains("希望", sp.Reason);
    }

    // [3.391.0 実バグ回帰] 実現不能な希望（担当できないシフトへの希望）を「別シフトへ固定」として
    // capacity から外していたため、verdict が Fixable→Infeasible へ倒れ「データ上、充足不可」という
    // 誤った断定を出していた。s1 は A を担当できるが、担当できない B への希望を持つ＝
    // WishLocked=false なのでこの枠へ回せる。旧実装なら capacity=1 &lt; need=2 で Infeasible。
    [Fact]
    public void InfeasibleWishDoesNotShrinkCoverageCapacity()
    {
        var st = St(
            startDate: "2025-12-01", endDate: "2025-12-01",
            shifts: new List<Shift> { new("休み", "休", "", ""), new("早番", "A", "2", "2"), new("遅番", "B", "", "") },
            groups: new List<Group> { new("G", "G"), new("H", "H") },
            staff: new List<Staff> { new("s0", 0), new("s1", 0) },
            use2: true,
            // 群G は 休/A のみ担当可（B は担当不可）。
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 0 }, new List<int> { 1, 1, 1 } },
            schedule: new List<IReadOnlyList<int>> { new List<int> { 1 }, new List<int> { 0 } },
            // s1 は担当できない B を希望＝実現不能
            wishes: new Dictionary<string, int> { ["1,0"] = 2 });

        var p = new Problem(st);
        Assert.False(p.WishLocked(1, 0), "前提: s1 の B 希望は実現不能");

        var diag = V6PortAnalyzer.DiagnoseCoverage(st);
        var sf = diag.Shortfalls.Single();
        Assert.Equal(1, sf.Miss); // A の不足1件
        Assert.Equal(CoverageVerdict.Fixable, sf.Verdict); // 実現不能な希望は capacity を減らさない
        Assert.DoesNotContain("充足不可", sf.Reason); // 「充足不可」と断定しない
    }

    // [3.391.0 実バグ回帰] covO 側も同型。希望が過剰シフトそのものを指し、かつ実現不能でないと
    // 旧コードの `wish == k` を踏まないので、s0 を「A を担当できないのに A に在勤し、A を希望している」
    // 形にする（＝groupViol も立っている）。旧実装はこれを「希望固定＝動かせない」と案内していたが、
    // 実現不能な希望は凍結しない＝動かせるし、動かせば groupViol も同時に消える。
    [Fact]
    public void InfeasibleWishIsNotReportedAsPinnedInSurplus()
    {
        var st = St(
            startDate: "2025-12-01", endDate: "2025-12-01",
            shifts: new List<Shift> { new("休み", "休", "", ""), new("早番", "A", "0", "0") },
            groups: new List<Group> { new("G", "G"), new("H", "H") },
            staff: new List<Staff> { new("s0", 0), new("s1", 1) },
            use2: true,
            // 群G(s0) は 休 のみ担当可＝A は担当不可。群H(s1) は両方可。
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 0 }, new List<int> { 1, 1 } },
            // s0 が担当外の A に在勤＝need 0 に対し過剰1
            schedule: new List<IReadOnlyList<int>> { new List<int> { 1 }, new List<int> { 0 } },
            // s0 は担当できない A を希望＝実現不能
            wishes: new Dictionary<string, int> { ["0,0"] = 1 });

        var p = new Problem(st);
        Assert.False(p.WishLocked(0, 0), "前提: s0 の A 希望は実現不能");
        Assert.Equal(1, UnifiedViolationChecker.Check(st, st.Schedule.ToIntArray2D()).Breakdown["groupViol"]);

        var sp = V6PortAnalyzer.DiagnoseCoverage(st).Surpluses.Single();
        Assert.Equal(1, sp.Excess); // A の過剰1件
        // reason は 0 件でも「希望固定0人」というラベルを必ず含むので、件数で見る。
        Assert.Contains("希望固定0人", sp.Reason);
        Assert.Contains("動かせる1人", sp.Reason);
    }

    // ==== DayLabel（V6PortAnalyzer.kt 末尾のトップレベル関数、直接のKotlinテストは無いが
    //   SafeDayLabel との唯一の相違点＝負オフセットガードの不在をここで新規に固定する） ====

    [Theory]
    [InlineData("2026-06-01", 0, "6/1(月)")]
    [InlineData("2026-06-01", 1, "6/2(火)")]
    [InlineData("2026-06-06", 0, "6/6(土)")]
    [InlineData("2026-06-07", 0, "6/7(日)")]
    [InlineData("2026-06-01", 30, "7/1(水)")]
    public void DayLabel_ComputesDateAndMondayFirstWeekday_SameAsSafeDayLabel(string startDate, int offset, string expected)
    {
        Assert.Equal(expected, V6PortAnalyzer.DayLabel(startDate, offset));
        // 非負オフセットでは SafeDayLabel と完全に同一結果になることも併せて確認する。
        Assert.Equal(V6SanityPort.SafeDayLabel(startDate, offset), V6PortAnalyzer.DayLabel(startDate, offset));
    }

    /// <summary>
    /// 唯一の相違点: <see cref="V6SanityPort.SafeDayLabel"/> は <c>offset &lt; 0</c> を即座に
    /// フォールバックへ倒すが、<see cref="V6PortAnalyzer.DayLabel"/> にはそのガードが無いため、
    /// 実際に <c>AddDays(-1)</c> で前日へロールバックした本物の日付を返す。
    /// </summary>
    [Fact]
    public void DayLabel_NegativeOffsetActuallyRollsBackward_UnlikeSafeDayLabel()
    {
        Assert.Equal("5/31(日)", V6PortAnalyzer.DayLabel("2026-06-01", -1));
        Assert.Equal("0日", V6SanityPort.SafeDayLabel("2026-06-01", -1));
    }

    [Fact]
    public void DayLabel_FallsBackToOffsetPlusOneWhenUnparseable()
    {
        Assert.Equal("6日", V6PortAnalyzer.DayLabel("garbage", 5));
    }
}
