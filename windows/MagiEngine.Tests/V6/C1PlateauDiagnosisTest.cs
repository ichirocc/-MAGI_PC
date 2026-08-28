using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [3.322.0 移植元] c1 頭打ちの構造化診断。原因の判定・最終盤面での再フィルタ・文言の固定。
/// </summary>
public class C1PlateauDiagnosisTest
{
    /// <summary>キーは (職員, シフト, 規則index)。テストは規則index 0 を既定に使う。</summary>
    private static C1PlateauDiagnosis Build(
        IReadOnlyDictionary<(int, int, int), IReadOnlyDictionary<string, int>> stats,
        IReadOnlyDictionary<(int, int, int), IReadOnlyDictionary<string, int>>? culprits = null,
        Func<int, int, int, bool>? stillDeficient = null,
        int remainingC1 = 9) =>
        C1PlateauDiagnosis.Build(
            remainingC1: remainingC1,
            blockStats: stats,
            culpritStats: culprits ?? new Dictionary<(int, int, int), IReadOnlyDictionary<string, int>>(),
            staffName: i => $"S{i}",
            shiftKigou: k => $"K{k}",
            ruleLabel: r => $"R{r}",
            stillDeficient: stillDeficient ?? ((_, _, _) => true));

    [Fact]
    public void PinRejectionsDominateSoCauseIsPinConstrained()
    {
        var d = Build(new Dictionary<(int, int, int), IReadOnlyDictionary<string, int>>
        {
            [(0, 1, 0)] = new Dictionary<string, int>
            {
                [C1PlateauDiagnosis.REASON_PIN] = 12,
                [C1PlateauDiagnosis.REASON_SCORE] = 3,
            },
        });
        var e = d.Entries.Single();
        Assert.Equal(C1PlateauCause.PinConstrained, e.Cause);
        Assert.Equal(12, e.RejectedByPin);
        Assert.Equal(3, e.RejectedByScore);
        Assert.Equal(1, d.PinConstrained);
        // 「構造的に不能」とは言わない＝緩めれば通る可能性を残した文言であること。
        var action = e.RecommendedAction();
        Assert.True(action.Contains("固定"), "回数固定の緩和を案内する");
        Assert.True(!action.Contains("不能"), "不能と断定しない");
    }

    [Fact]
    public void ScoreRejectionsReportTheWorstFamilyInTheAction()
    {
        var d = Build(
            new Dictionary<(int, int, int), IReadOnlyDictionary<string, int>>
            {
                [(2, 0, 0)] = new Dictionary<string, int> { [C1PlateauDiagnosis.REASON_SCORE] = 7 },
            },
            culprits: new Dictionary<(int, int, int), IReadOnlyDictionary<string, int>>
            {
                [(2, 0, 0)] = new Dictionary<string, int> { ["low"] = 5, ["high"] = 2 },
            });
        var e = d.Entries.Single();
        Assert.Equal(C1PlateauCause.ScoreTradeoff, e.Cause);
        Assert.Equal(("low", 5), e.TopScoreCulprits.First());
        Assert.True(e.RecommendedAction(_ => "下限割れ").Contains("下限割れ"), "表示名は呼出側から受ける");
    }

    [Fact]
    public void NoCandidateCountsBothOfItsReasonStrings()
    {
        var d = Build(new Dictionary<(int, int, int), IReadOnlyDictionary<string, int>>
        {
            [(1, 1, 0)] = new Dictionary<string, int>
            {
                [C1PlateauDiagnosis.REASON_NO_CANDIDATE] = 4,
                [C1PlateauDiagnosis.REASON_NO_REPACK] = 5,
            },
        });
        var e = d.Entries.Single();
        Assert.Equal(C1PlateauCause.NoCandidate, e.Cause);
        Assert.Equal(9, e.NoCandidate);
        // 候補が1件も作れていない＝却下の観測が無い。
        Assert.Equal(C1PlateauEvidence.Unknown, e.Evidence);
    }

    [Fact]
    public void NoCandidateIsNeverClaimedWhenSomeCandidateWasActuallyRejected()
    {
        // 実データで踏んだ取り違え: 「スコア却下8・候補なし10」を件数の多数決で分類すると
        //   「入れ替えられる相手が見つかりません」と案内してしまうが、実際は相手が居て
        //   禁止連続で落ちていた。候補が1件でも作れているなら「候補なし」とは言わない。
        var d = Build(
            new Dictionary<(int, int, int), IReadOnlyDictionary<string, int>>
            {
                [(0, 0, 0)] = new Dictionary<string, int>
                {
                    [C1PlateauDiagnosis.REASON_SCORE] = 8,
                    [C1PlateauDiagnosis.REASON_NO_CANDIDATE] = 10,
                },
            },
            culprits: new Dictionary<(int, int, int), IReadOnlyDictionary<string, int>>
            {
                [(0, 0, 0)] = new Dictionary<string, int> { ["c3n"] = 5 },
            });
        var e = d.Entries.Single();
        Assert.Equal(C1PlateauCause.ScoreTradeoff, e.Cause);
        Assert.Equal(10, e.NoCandidate); // 件数自体は内訳に残す
        Assert.True(!e.RecommendedAction().Contains("見つかりません"), "相手が居ないとは言わない");
    }

    [Fact]
    public void PinIsOnlyTheCauseWhenItStrictlyOutnumbersScoreRejections()
    {
        // 同数ならスコア却下側に倒す（ピンを緩めても通らない可能性が同程度に高いため、
        //   「回数固定が壁」という強い断定を避ける）。
        var d = Build(new Dictionary<(int, int, int), IReadOnlyDictionary<string, int>>
        {
            [(0, 0, 0)] = new Dictionary<string, int>
            {
                [C1PlateauDiagnosis.REASON_PIN] = 4,
                [C1PlateauDiagnosis.REASON_SCORE] = 4,
            },
        });
        Assert.Equal(C1PlateauCause.ScoreTradeoff, d.Entries.Single().Cause);
    }

    [Fact]
    public void EntriesResolvedOnTheFinalBoardAreDropped()
    {
        var stats = new Dictionary<(int, int, int), IReadOnlyDictionary<string, int>>
        {
            [(0, 1, 0)] = new Dictionary<string, int> { [C1PlateauDiagnosis.REASON_PIN] = 2 },
            [(1, 1, 0)] = new Dictionary<string, int> { [C1PlateauDiagnosis.REASON_PIN] = 2 },
        };
        // 研磨の時点では2件とも残っていたが、後続パスが (1,1) を直した。
        var d = Build(stats).RefreshedAgainst(1, (i, _, _) => i == 0);
        Assert.Single(d.Entries);
        Assert.Equal(0, d.Entries.Single().Staff);
        Assert.Equal(1, d.RemainingC1);
    }

    [Fact]
    public void AlreadyResolvedTargetsAreNeverListed()
    {
        // 対象が全部解消されたなら c1 も 0（残っていないので語ることが無い）。
        var d = Build(
            new Dictionary<(int, int, int), IReadOnlyDictionary<string, int>>
            {
                [(0, 1, 0)] = new Dictionary<string, int> { [C1PlateauDiagnosis.REASON_PIN] = 2 },
            },
            stillDeficient: (_, _, _) => false,
            remainingC1: 0);
        Assert.True(d.Entries.Count == 0, "解消済みは出さない");
        Assert.True(!d.CauseUnknown, "原因未確定でもない");
        Assert.True(d.LogLines().Count == 0, "ログも出さない");
    }

    [Fact]
    public void RemainingWithoutAnyObservationIsReportedAsCauseUnknown()
    {
        // [3.325.0] c1 は残っているのに却下の観測が1件も無い＝原因未確定。
        //   ここで「直せない理由」を語ると観測していないことを語ることになる。
        var d = Build(
            new Dictionary<(int, int, int), IReadOnlyDictionary<string, int>>
            {
                [(0, 1, 0)] = new Dictionary<string, int> { [C1PlateauDiagnosis.REASON_PIN] = 2 },
            },
            stillDeficient: (_, _, _) => false,
            remainingC1: 7);
        Assert.True(d.CauseUnknown, "観測ゼロ＋残存＝原因未確定");
        Assert.True(d.Entries.Count == 0, "内訳は持たない");
        var line = d.LogLines().Single();
        Assert.True(line.Contains("7件"), "残存件数を名乗る");
        Assert.True(line.Contains("原因未確定"), "原因未確定と明示する");
    }

    [Fact]
    public void EntriesAreOrderedByHowMuchWasActuallyTried()
    {
        var d = Build(new Dictionary<(int, int, int), IReadOnlyDictionary<string, int>>
        {
            [(0, 1, 0)] = new Dictionary<string, int> { [C1PlateauDiagnosis.REASON_PIN] = 2 },
            [(1, 1, 0)] = new Dictionary<string, int> { [C1PlateauDiagnosis.REASON_PIN] = 40 },
        });
        Assert.Equal(1, d.Entries.First().Staff);
        Assert.True(d.LogLines().First().Contains("窓の要件"), "ログ先頭は残存件数を名乗る");
    }

    // --- [3.331.0/実機ログ] 巡ごとの観測を合算する ---

    [Fact]
    public void MergedWithSumsObservationsAcrossRoundsInsteadOfOverwriting()
    {
        // 実機ログの再現: 1巡目は7箇所を観測、2巡目は（1巡目が直したあとの盤面なので）3箇所だけ。
        // 旧実装は 2巡目で上書きし、5日窓の説明が丸ごと消え、件数も実際より小さく出ていた。
        var round1 = C1PlateauDiagnosis.Build(
            remainingC1: 11,
            blockStats: new Dictionary<(int, int, int), IReadOnlyDictionary<string, int>>
            {
                [(0, 1, 0)] = new Dictionary<string, int> { [C1PlateauDiagnosis.REASON_SCORE] = 8 },   // 5日窓
                [(0, 1, 1)] = new Dictionary<string, int> { [C1PlateauDiagnosis.REASON_SCORE] = 24 },  // 14日窓
            },
            culpritStats: new Dictionary<(int, int, int), IReadOnlyDictionary<string, int>>
            {
                [(0, 1, 0)] = new Dictionary<string, int> { ["low"] = 8 },
                [(0, 1, 1)] = new Dictionary<string, int> { ["low"] = 23, ["high"] = 1 },
            },
            staffName: _ => "古泉 健一",
            shiftKigou: _ => "休",
            ruleLabel: r => r == 0 ? "5日で1回以上" : "14日で4回以上",
            stillDeficient: (_, _, _) => true);
        var round2 = C1PlateauDiagnosis.Build(
            remainingC1: 8,
            blockStats: new Dictionary<(int, int, int), IReadOnlyDictionary<string, int>>
            {
                [(0, 1, 1)] = new Dictionary<string, int> { [C1PlateauDiagnosis.REASON_SCORE] = 6 },
            },
            culpritStats: new Dictionary<(int, int, int), IReadOnlyDictionary<string, int>>
            {
                [(0, 1, 1)] = new Dictionary<string, int> { ["low"] = 6 },
            },
            staffName: _ => "古泉 健一",
            shiftKigou: _ => "休",
            ruleLabel: r => r == 0 ? "5日で1回以上" : "14日で4回以上",
            stillDeficient: (_, _, _) => true);
        var merged = round1.MergedWith(round2);
        Assert.Equal(2, merged.Entries.Count); // 2巡目に出てこない5日窓も残る
        Assert.Equal(8, merged.RemainingC1); // 残数は新しい方
        var wide = merged.Entries.First(e => e.RuleIndex == 1);
        Assert.Equal(30, wide.RejectedByScore); // 件数は全巡の合計
        Assert.Equal(29, wide.TopScoreCulprits.First(c => c.Family == "low").Count); // 主因の族も合算
        Assert.Equal("low", wide.TopScoreCulprits.First().Family); // 多い順
        var narrow = merged.Entries.First(e => e.RuleIndex == 0);
        Assert.Equal(8, narrow.RejectedByScore); // 1巡目だけの観測はそのまま
    }

    [Fact]
    public void MergedWithRecomputesCauseFromTheCombinedCounts()
    {
        // 1巡目はピン破りが多く、2巡目はスコア却下が多い。合算後の分類は合計で決め直す。
        static C1PlateauDiagnosis One(int pin, int score, int remaining) =>
            C1PlateauDiagnosis.Build(
                remainingC1: remaining,
                blockStats: new Dictionary<(int, int, int), IReadOnlyDictionary<string, int>>
                {
                    [(0, 1, 0)] = new Dictionary<string, int>
                    {
                        [C1PlateauDiagnosis.REASON_PIN] = pin,
                        [C1PlateauDiagnosis.REASON_SCORE] = score,
                    },
                },
                culpritStats: new Dictionary<(int, int, int), IReadOnlyDictionary<string, int>>(),
                staffName: _ => "s",
                shiftKigou: _ => "X",
                ruleLabel: _ => "r",
                stillDeficient: (_, _, _) => true);
        var a = One(pin: 5, score: 1, remaining: 3);
        var b = One(pin: 0, score: 9, remaining: 2);
        Assert.Equal(C1PlateauCause.PinConstrained, a.Entries.Single().Cause);
        var merged = a.MergedWith(b);
        // 合計 pin=5 score=10 → スコア却下が多い
        Assert.Equal(C1PlateauCause.ScoreTradeoff, merged.Entries.Single().Cause);
    }

    [Fact]
    public void MergedWithHandlesEmptySides()
    {
        var empty = new C1PlateauDiagnosis(5, Array.Empty<C1PlateauEntry>());
        var one = C1PlateauDiagnosis.Build(
            remainingC1: 3,
            blockStats: new Dictionary<(int, int, int), IReadOnlyDictionary<string, int>>
            {
                [(0, 1, 0)] = new Dictionary<string, int> { [C1PlateauDiagnosis.REASON_SCORE] = 2 },
            },
            culpritStats: new Dictionary<(int, int, int), IReadOnlyDictionary<string, int>>(),
            staffName: _ => "s",
            shiftKigou: _ => "X",
            ruleLabel: _ => "r",
            stillDeficient: (_, _, _) => true);
        Assert.Single(empty.MergedWith(one).Entries); // 空 + 有 = 有
        // 有 + 空 は観測を捨てず、残数だけ新しい方を採る（2巡目が何も観測しなくても説明を失わない）。
        var kept = one.MergedWith(empty);
        Assert.Single(kept.Entries);
        Assert.Equal(5, kept.RemainingC1);
    }
}
