using MagiEngine.Model;
using MagiEngine.V6;
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// フェーズ7 ピース4（<c>V6PortAnalyzer.Forbidden.cs</c>＝<see cref="V6PortAnalyzer.DiagnoseForbiddenRuns"/>）の
/// 移植テスト。
///
/// <c>V6PortAnalyzerTest.kt</c>（Kotlin 側、全16件）のうち、この移植で対象なのは
/// <c>diagnoseForbiddenRuns</c> を直接呼ぶ8件だけ:
///  - <c>diagnoseForbiddenRunsMarksFreelyBreakableRunAsEscapable</c>
///  - <c>diagnoseForbiddenRunsReportsWishPinnedRunAsStructurallyBlocked</c>
///  - <c>diagnoseForbiddenRunsVerifiesChainEscapeWhenDepartureCreatesCovU</c>
///  - <c>diagnoseForbiddenRunsReportsNoReceiverWallWhenChainCannotFill</c>
///  - <c>diagnoseForbiddenRunsVerifiesAdjacentDayEscape</c>
///  - <c>forbiddenRunSeqLabelMatchesRuleKeyDerivedFromCons3nRows</c>
///  - <c>wishPinnedCellIsNotAWallWhenMovingItRemovesTwoForbiddenFires</c>
///  - <c>adjacentDayFixIsNotAnEscapeWhenItOnlyTradesForbiddenRunForABrokenWish</c>
///
/// 残り8件は他ピースの対象（<c>V6PortAnalyzerCoverageTest.cs</c> の冒頭コメントに内訳あり）:
/// 9件が piece 3（<c>DiagnoseCoverage</c>、既に <c>V6PortAnalyzerCoverageTest.cs</c> で移植済み）、
/// <c>v6OverviewComputesAptAndRisk</c> が piece 9（<c>V6PortAnalyzer.Analyze</c>）、
/// <c>residualAnalysisTreatsWishBlockedCovUAsAWallEvenWhenSupplyFloorIsZero</c> が piece 17
/// （<c>V6FinalPort.CovUBlockedAmount</c>/<c>CovUStructuralWall</c> 未移植）。
/// </summary>
public class V6PortAnalyzerForbiddenTest
{
    private static readonly IReadOnlyDictionary<string, System.Text.Json.JsonElement> NoExtras =
        new Dictionary<string, System.Text.Json.JsonElement>();

    /// <summary>
    /// Kotlin 側 <c>forbiddenState(...)</c> の直訳。既定は「休/X/Y の3シフト・単一職員 s0・
    /// 単一グループ(全担当可)」の最小盤面で、日数は <paramref name="schedule"/> の1行目の長さから
    /// 逆算する（Kotlin の <c>schedule[0].size</c>）。
    /// </summary>
    private static MagiState ForbiddenState(
        IReadOnlyList<IReadOnlyList<int>> schedule,
        IReadOnlyList<C3Row> cons3n,
        IReadOnlyDictionary<string, int>? wishes = null,
        IReadOnlyList<Shift>? shifts = null,
        IReadOnlyList<Staff>? staff = null,
        IReadOnlyList<IReadOnlyList<int>>? groupShift = null)
    {
        shifts ??= new List<Shift> { new("休", "休", "", ""), new("X", "X", "", ""), new("Y", "Y", "", "") };
        staff ??= new List<Staff> { new("s0", 0) };
        groupShift ??= new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } };
        var days = schedule[0].Count;
        return new MagiState(
            StartDate: "2026-01-01",
            EndDate: "2026-01-" + days.ToString().PadLeft(2, '0'),
            Shifts: shifts,
            Groups: Enumerable.Range(0, groupShift.Count).Select(idx => new Group($"G{idx}", $"G{idx}")).ToList(),
            StaffList: staff,
            Use2Patterns: false,
            GroupShift: groupShift,
            GroupShiftApt: groupShift.Select(g => (IReadOnlyList<string>)g.Select(_ => "").ToList()).ToList(),
            Schedule: schedule,
            Wishes: wishes ?? new Dictionary<string, int>(),
            StaffRange: new Dictionary<string, Range>(),
            NeedDay1: new Dictionary<string, string>(), NeedDay2: new Dictionary<string, string>(),
            Cons1: new List<C1Row>(), Cons2: new List<C2Row>(), Cons3: new List<C3Row>(),
            Cons3n: cons3n, Cons3m: new List<C3Row>(), Cons3mn: new List<C3Row>(),
            Cons41: new List<C41Row>(), Cons42: new List<C42Row>(),
            SkillGroups: new List<Group>(), Cons41s: new List<C41Row>(), Cons42s: new List<C42Row>(),
            ShiftColors: new Dictionary<string, string>(),
            Extras: NoExtras);
    }

    // 需要も希望も無い盤面の禁止連続は、どのセルも休へ変えるだけで安全に崩せる＝Free。
    // Free を含む run は「探索未到達の可能性」として案内される（もし残っていたら本物のシグナル）。
    [Fact]
    public void DiagnoseForbiddenRuns_MarksFreelyBreakableRunAsEscapable()
    {
        var st = ForbiddenState(
            schedule: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 0 } },   // X X 休 → [X,X] が1件
            cons3n: new List<C3Row> { new(new List<string> { "X", "X" }) });

        var diag = V6PortAnalyzer.DiagnoseForbiddenRuns(st);
        Assert.Equal(1, diag.TotalRuns);
        var run = Assert.Single(diag.Runs);
        Assert.True(run.Escapable, "安全な代替がある run は escapable");
        Assert.Contains(run.Cells, c => c.Escape == ForbiddenCellEscape.Free);
        Assert.Contains("探索未到達", run.Hint);
    }

    // 両セルとも本人希望どおり＝動かすと pref(9000)>c3n(7000) の悪化で isBetter が却下する（設計どおり）。
    // 全セル Pinned → 構造的に崩せないことを正直に案内する（実機 c3n=1 が67エポック不動だった穴の再現）。
    [Fact]
    public void DiagnoseForbiddenRuns_ReportsWishPinnedRunAsStructurallyBlocked()
    {
        var st = ForbiddenState(
            schedule: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 0 } },
            cons3n: new List<C3Row> { new(new List<string> { "X", "X" }) },
            wishes: new Dictionary<string, int> { ["0,0"] = 1, ["0,1"] = 1 });   // 両セルとも X を希望固定

        var diag = V6PortAnalyzer.DiagnoseForbiddenRuns(st);
        var run = Assert.Single(diag.Runs);
        Assert.True(run.Cells.All(c => c.Escape == ForbiddenCellEscape.Pinned), "全セル希望固定");
        Assert.True(diag.AllBlocked);
        Assert.True(
            run.Hint.Contains("希望固定") && run.Hint.Contains("残ります"),
            "希望固定の明示と対処の案内");
    }

    // 離脱すると covU 穴が空くが、玉突き連鎖（FindCovUChain=探索本体と同一関数）で埋め直せる局面は
    // Chain（実証済みの多段手）として案内される。
    [Fact]
    public void DiagnoseForbiddenRuns_VerifiesChainEscapeWhenDepartureCreatesCovU()
    {
        // P(需要1/日)を s0 が2連続（[P,P]禁止）。s0 が抜けた穴は休中の s1 が埋められる。
        var st = ForbiddenState(
            schedule: new List<IReadOnlyList<int>> { new List<int> { 1, 1 }, new List<int> { 0, 0 } },
            cons3n: new List<C3Row> { new(new List<string> { "P", "P" }) },
            shifts: new List<Shift> { new("休", "休", "", ""), new("P", "P", "1", ""), new("Q", "Q", "", "") },
            staff: new List<Staff> { new("s0", 0), new("s1", 0) },
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } });

        var diag = V6PortAnalyzer.DiagnoseForbiddenRuns(st);
        var run = Assert.Single(diag.Runs);
        Assert.True(
            run.Cells.Any(c => c.Escape == ForbiddenCellEscape.Chain),
            "玉突き連鎖の実在を実証したうえで escapable");
        Assert.True(run.Hint.Contains("玉突き") || run.Hint.Contains("隣接日"));
    }

    // 同じ局面で唯一の受け皿 s1 が両日とも休へ希望固定されると連鎖が実在しなくなり、
    // 「covU受け皿なし」として全セル Blocked＝構造的な壁を正直に報告する（3.263.0 の教訓の c3n 版）。
    [Fact]
    public void DiagnoseForbiddenRuns_ReportsNoReceiverWallWhenChainCannotFill()
    {
        var st = ForbiddenState(
            schedule: new List<IReadOnlyList<int>> { new List<int> { 1, 1 }, new List<int> { 0, 0 } },
            cons3n: new List<C3Row> { new(new List<string> { "P", "P" }) },
            wishes: new Dictionary<string, int> { ["1,0"] = 0, ["1,1"] = 0 },   // s1 は両日とも休へ希望固定＝連鎖の受け皿なし
            shifts: new List<Shift> { new("休", "休", "", ""), new("P", "P", "1", ""), new("Q", "Q", "", "") },
            staff: new List<Staff> { new("s0", 0), new("s1", 0) },
            groupShift: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } });

        var diag = V6PortAnalyzer.DiagnoseForbiddenRuns(st);
        var run = Assert.Single(diag.Runs);
        Assert.True(run.Cells.All(c => c.Escape == ForbiddenCellEscape.Blocked), "全セルが受け皿なしで塞がる");
        Assert.True(run.Cells.All(c => c.Detail.Contains("受け皿なし")));
        Assert.True(diag.AllBlocked);
        // [3.284.0] 「証明」の強さを限定: 受け皿なしの塞がりは「探索手の全滅を検証」であり
        //   全空間の数学的証明ではない＝断定を避けた文言になったことを固定。
        Assert.Contains("崩せる見込みがありません", run.Hint);
    }

    // 代替が全て新たな禁止連続を作る局面でも、隣接日調整（TryFixForbiddenRunViaAdjacentDay=
    // 探索本体と同一関数）で崩せるなら Adjacent として実証つきで案内される。
    [Fact]
    public void DiagnoseForbiddenRuns_VerifiesAdjacentDayEscape()
    {
        // s0: [Q, P, P, 休]。禁止=[P,P](run@1-2)・[Q,休]・[Q,Q]。
        //   day1 の代替は 休→[Q,休]@0 / Q→[Q,Q]@0 と全て新たな禁止連続を作るが、
        //   day0 を Q→休 に隣接日調整すれば day1=休 が安全に置ける＝Adjacent。
        //   day2 は休へ変えるだけで安全＝Free（同一 run 内で両分類が同時に検証される）。
        var st = ForbiddenState(
            schedule: new List<IReadOnlyList<int>> { new List<int> { 2, 1, 1, 0 } },
            cons3n: new List<C3Row>
            {
                new(new List<string> { "P", "P" }),
                new(new List<string> { "Q", "休" }),
                new(new List<string> { "Q", "Q" }),
            },
            shifts: new List<Shift> { new("休", "休", "", ""), new("P", "P", "", ""), new("Q", "Q", "", "") });

        var diag = V6PortAnalyzer.DiagnoseForbiddenRuns(st);
        var run = diag.Runs.Single(r => r.SeqLabel == "P→P");
        var day1 = run.Cells.Single(c => c.DayIndex == 1);
        var day2 = run.Cells.Single(c => c.DayIndex == 2);
        Assert.Equal(ForbiddenCellEscape.Adjacent, day1.Escape);
        Assert.Equal(ForbiddenCellEscape.Free, day2.Escape);
        Assert.True(run.Escapable);
    }

    /// <summary>
    /// [3.297.0 壁の緩和導線の前提] ForbiddenDiag の <c>SeqLabel</c> が、cons3n 行から
    /// <c>Problem.ResolveC3</c> と同じ意味論（<b>最初の空白まで</b>を本体とする）で作ったキーと一致すること。
    ///
    /// MagiViewModel（Android版）の <c>relaxForbiddenRule</c> はこのキー一致だけを頼りに「壁になっている
    /// 並び」を削除するため、ここがずれると「削除ボタンを押しても何も消えない」か「別のルールが消える」。
    /// 空白を<b>除去</b>する <c>SettingFixAction.DELETE_DUP_SEQ</c> のキーとは意味が違う点も同時に押さえる
    /// （このテストは Kotlin 側の意図をそのまま固定するだけで、C# 側に <c>DELETE_DUP_SEQ</c> 相当は
    /// まだ移植していない＝UI層/piece 9+の管轄）。
    /// </summary>
    [Fact]
    public void ForbiddenRunSeqLabelMatchesRuleKeyDerivedFromCons3nRows()
    {
        var rows = new List<C3Row>
        {
            new(new List<string> { "X", "X" }),
            new(new List<string> { "X", "Y", "" }),      // 末尾空白（実データで普通に出る形）
            new(new List<string> { "Y", "", "X" }),      // 途中空白＝ResolveC3 は ["Y"] として扱う
        };
        var st = ForbiddenState(
            schedule: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 2, 0 } },   // X X Y 休 → [X,X] と [X,Y] が1件ずつ
            cons3n: rows);

        static string RuleKey(C3Row row)
        {
            var patternList = row.Pattern.ToList();
            var end = patternList.FindIndex(string.IsNullOrWhiteSpace);
            var body = end >= 0 ? patternList.Take(end).ToList() : patternList;
            return string.Join("→", body);
        }
        var keys = rows.Select(RuleKey).ToList();
        Assert.Equal(new List<string> { "X→X", "X→Y", "Y" }, keys);

        var diag = V6PortAnalyzer.DiagnoseForbiddenRuns(st);
        Assert.True(diag.TotalRuns > 0, "違反 run が検出されること");
        foreach (var run in diag.Runs)
        {
            Assert.True(keys.Contains(run.SeqLabel), $"seqLabel={run.SeqLabel} が cons3n 行のキーに含まれること");
        }
        // 削除導線は同じ並びの重複行をまとめて消す＝キー一致で数えられること。
        Assert.Equal(1, keys.Count(k => k == "X→X"));
    }

    /// <summary>
    /// [3.311.0] 1セルが<b>複数の</b>禁止連続 fire に関与する局面では、希望どおりのセルでも動かす価値がある。
    /// 禁止「X→X」・行 X,X,X の中央セルは 2件の fire に関与し、休へ動かすと c3n 2→0 / pref 0→1 ＝
    /// BetterReport の第1キー hard が 2→1 と厳密に改善する（isBetter は採用する）。
    /// 旧実装は <c>wishLocked &amp;&amp; wish == cur</c> で HARD 差分を見ずに即 Pinned を返し、run 全体を
    /// 「構造壁」と誤診していた。偽の壁は 3.281.0 の短い停滞タイムアウトを早期に発火させうる。
    /// </summary>
    [Fact]
    public void WishPinnedCellIsNotAWallWhenMovingItRemovesTwoForbiddenFires()
    {
        var st = ForbiddenState(
            schedule: new List<IReadOnlyList<int>> { new List<int> { 1, 1, 1 } },   // X X X → [X,X] が2件
            cons3n: new List<C3Row> { new(new List<string> { "X", "X" }) },
            wishes: new Dictionary<string, int> { ["0,1"] = 1 });   // 中央セルだけ X を希望固定

        var diag = V6PortAnalyzer.DiagnoseForbiddenRuns(st);
        var center = diag.Runs.SelectMany(r => r.Cells).Where(c => c.DayIndex == 1).ToList();
        Assert.True(center.Count > 0, "中央セルが検出される");
        Assert.True(
            center.All(c => c.Escape != ForbiddenCellEscape.Pinned),
            $"希望固定でも正味の必須違反が減るなら壁ではない: {string.Join(",", center.Select(c => c.Escape))}");
        Assert.False(diag.AllBlocked, "この盤面を構造壁と誤診しない");
    }

    /// <summary>
    /// [3.343.0] <b>隣接日調整でも「正味の HARD が減るか」まで見る</b>。
    ///
    /// 3.311.0 で Pinned 判定へ <c>prefCost</c> を入れたとき、隣接日調整（Adjacent）の分岐には入れ忘れて
    /// いた。隣接日調整はこの職員の複数日を動かすので、本セルだけでなく行全体の希望違反が増えうる。
    ///
    /// この盤面（担当可能=休/X/Y の1名・T=3・行 X,Y,X・day1 は Y を希望固定）:
    ///  - day0 はどの代替も新たな禁止連続を作り、隣接日 day1 は希望固定で動かせない＝Blocked。
    ///  - day1 を「休」にすると「休→X」が新たに発火するが、day2 を「休」へ変えれば並びは崩せる。
    ///    ところが day1 の希望を破るので <b>c3n 1→0 に対し pref 0→1＝正味の HARD は減らない</b>
    ///    （weighted では 9000−7000＝+2000 の悪化で、BetterReport は決して採用しない）。
    /// 旧実装はこれを Adjacent＝「崩せる」と誤って主張し、①利用者へ「探索が見つけていないだけ」と
    /// 誤った期待を与え ②3.281.0 の停滞打ち切り（全 run 塞がりなら短い閾値）を発火させなくしていた。
    /// </summary>
    [Fact]
    public void AdjacentDayFixIsNotAnEscapeWhenItOnlyTradesForbiddenRunForABrokenWish()
    {
        var st = ForbiddenState(
            schedule: new List<IReadOnlyList<int>> { new List<int> { 1, 2, 1 } },   // X Y X → [X,Y] が1件
            cons3n: new List<C3Row>
            {
                new(new List<string> { "X", "Y" }),   // 本命の違反
                new(new List<string> { "Y", "Y" }),   // day0 を Y にしても崩れない
                new(new List<string> { "休", "X" }),  // day1 を休にすると新たに発火する
                new(new List<string> { "休", "Y" }),  // day0 を休にしても崩れない
            },
            wishes: new Dictionary<string, int> { ["0,1"] = 2 });   // day1 は Y を希望固定

        var diag = V6PortAnalyzer.DiagnoseForbiddenRuns(st);
        Assert.Equal(1, diag.TotalRuns);
        var cells = diag.Runs.SelectMany(r => r.Cells).ToList();
        Assert.True(
            cells.All(c => c.Escape != ForbiddenCellEscape.Adjacent),
            "希望を破る代金のほうが高い手を『崩せる』と主張してはいけない: " +
                string.Join(",", cells.Select(c => $"{c.DayIndex}:{c.Escape}")));
        var pinned = cells.Where(c => c.DayIndex == 1).ToList();
        Assert.True(
            pinned.All(c => c.Escape == ForbiddenCellEscape.Pinned),
            "希望が効いていることを『希望固定』として説明する: " + string.Join(",", pinned.Select(c => c.Escape)));
        Assert.True(diag.AllBlocked, "全セル塞がり＝停滞打ち切りが正しく発火できる");
    }
}
