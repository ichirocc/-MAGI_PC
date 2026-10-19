using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>
/// [S6] 「設定を緩めたら」の承認フロー R1〜R5（Kotlin <c>ui/RelaxApproval.kt</c>、<c>docs/s6_relax_trial.md</c> §15）。画面の状態遷移だけの純関数で、エンジンの出力は変えない。
/// R1 試算結果の確認（読み取り専用）→ R2 変更内容の確認 → R3 承認（理由は必須）→ R5 結果。条件を満たさない・変更しない・反映できないは R4 終了。
/// </summary>
public static class RelaxApproval
{
    public enum Step { R1, R2, R3, R4, R5 }

    /// <summary>R4 の理由。NoWall・Stopped を「解消できない」「0 件の証拠」と言わない（J7）。</summary>
    public enum EndReason { NoWall, Stopped, Unavailable, NoGain, Stale, NotReproduced, Blocked, Declined, RolledBack, SaveFailed }

    public static string TextOf(EndReason e) => e switch
    {
        EndReason.NoWall => "上限0を外しても、この違反を解く手順は見つかりませんでした。解消できないという意味ではありません。",
        EndReason.Stopped => "試算を途中で止めたので、結果はありません。",
        EndReason.Unavailable => "この勤務表では試算できません。",
        EndReason.NoGain => "試算しても必須違反は減りませんでした。",
        EndReason.Stale => "勤務表か設定が変わりました。もう一度試算してください。",
        EndReason.NotReproduced => "試算の手順を当てても同じ結果になりませんでした。もう一度試算してください。",
        EndReason.Blocked => "いまは反映できません（実行中か、データに問題があります）。",
        EndReason.Declined => "変更しないを選びました。",
        EndReason.RolledBack => "反映後の確認が合わなかったので元に戻しました。",
        _ => "保存できなかったので元に戻しました。",
    };

    /// <summary>反映の結果（VM が当てて確かめた事実）。</summary>
    public abstract record Verdict
    {
        public sealed record StaleV : Verdict;
        public sealed record NotReproducedV : Verdict;
        public sealed record BlockedV : Verdict;
        public sealed record Applied(int PostHard) : Verdict;
        public sealed record SaveFailedV : Verdict;
        public static readonly Verdict Stale = new StaleV();
        public static readonly Verdict NotReproduced = new NotReproducedV();
        public static readonly Verdict Blocked = new BlockedV();
        public static readonly Verdict SaveFailed = new SaveFailedV();
    }

    /// <summary>
    /// <paramref name="TrialId"/>・<paramref name="Fingerprint"/>＝試算とデータの指紋（短く表示）。<paramref name="DecisionId"/>＝承認を押したときに決まる。
    /// <paramref name="PressedId"/>＝反映を始めた decisionId（同じ id の 2 回目は何もしない）。<paramref name="Applied"/>＝承認した変更（前→後）の行。
    /// </summary>
    public sealed record Flow(
        Step Step, string TrialId, string Fingerprint, int ExpectedHard = -1, EndReason? End = null, string? Detail = null,
        string? DecisionId = null, string? PressedId = null, int PostHard = -1, IReadOnlyList<string>? Applied = null);

    public const string ReadOnlyHead = "読み取り専用・設定はまだ変わっていません";
    public const string NotApprovalNote = "試算で解けたことは変更の承認ではありません";
    public const string UnchangedLine = "設定は変わっていません";
    public const string ApproverLine = "承認者: この端末の利用者";
    public const string RebuildNote = "再作成は別の操作です（必要ならこのあと「再作成」を押してください）。";
    public const string ToReview = "変更内容を確認する";
    public const string ToApproval = "承認手続きへ進む";
    public const string Approve = "承認して反映する";
    public const string Decline = "変更しない";
    public const string Close = "閉じる";

    public static string ShortId(long key) => ((ulong)key).ToString("x16")[8..];

    public static string TrialId(long stateKey, long boardKey) => ShortId(unchecked(stateKey * 31 + boardKey));

    public static string Fingerprint(long stateKey, long boardKey) => $"{ShortId(stateKey)[..4]}-{ShortId(boardKey)[..4]}";

    /// <summary>試算と押した時刻から決まる id（同じ入力なら同じ id）。</summary>
    public static string DecisionId(string trialId, long epochMs)
    {
        long h = 0;
        foreach (var c in trialId) h = unchecked(h * 31 + c);
        return ShortId(unchecked(h * 1_000_003L + epochMs));
    }

    public static bool Improves(RelaxTrial.Outcome? outcome) => outcome is RelaxTrial.Result r && r.Rr < r.H0;

    /// <summary>開いたときの状態。改善のある Result だけが R1、他は理由の分かれた R4。</summary>
    public static Flow Start(RelaxTrial.Outcome? outcome, bool fresh, string trialId, string fingerprint)
    {
        var b = new Flow(Step.R1, trialId, fingerprint, ExpectedHard: (outcome as RelaxTrial.Result)?.Rr ?? -1);
        if (!fresh || outcome is null) return b with { Step = Step.R4, End = EndReason.Stale };
        return outcome switch
        {
            RelaxTrial.NoWallOutcome => b with { Step = Step.R4, End = EndReason.NoWall },
            RelaxTrial.StoppedOutcome => b with { Step = Step.R4, End = EndReason.Stopped },
            RelaxTrial.Unavailable u => b with { Step = Step.R4, End = EndReason.Unavailable, Detail = u.Reason },
            _ when !Improves(outcome) => b with { Step = Step.R4, End = EndReason.NoGain },
            _ => b,
        };
    }

    /// <summary>R1→R2→R3。古くなっていたら R4（何も変えない）。</summary>
    public static Flow Next(Flow f, bool fresh)
    {
        if (f.Step != Step.R1 && f.Step != Step.R2) return f;
        if (!fresh) return f with { Step = Step.R4, End = EndReason.Stale };
        return f with { Step = f.Step == Step.R1 ? Step.R2 : Step.R3 };
    }

    public static Flow DeclineFlow(Flow f) => f.Step is Step.R4 or Step.R5 ? f : f with { Step = Step.R4, End = EndReason.Declined };

    /// <summary>「承認して反映する」を押せるか: 理由が空でなく、改善のある Result で、試算が今のデータのもの。</summary>
    public static bool CanApprove(string reason, RelaxTrial.Outcome? outcome, bool fresh) =>
        !string.IsNullOrWhiteSpace(reason) && Improves(outcome) && fresh;

    /// <summary>押した: R3 でまだ押していなければ decisionId を刻んで返す。2 回目（同じ決定）は null＝何もしない。</summary>
    public static Flow? Press(Flow f, string decisionId) =>
        f.Step != Step.R3 || f.PressedId is not null ? null : f with { DecisionId = decisionId, PressedId = decisionId };

    /// <summary>反映後の確認: 当てた後の必須件数が試算の rr と同じときだけ成功。</summary>
    public static bool PostCheckOk(int expected, int got) => expected >= 0 && got == expected;

    /// <summary>VM の結果から最後の状態へ。成功と言うのは事後確認が合ったときだけ。</summary>
    public static Flow Finish(Flow f, Verdict v) => v switch
    {
        Verdict.StaleV => f with { Step = Step.R4, End = EndReason.Stale },
        Verdict.NotReproducedV => f with { Step = Step.R4, End = EndReason.NotReproduced },
        Verdict.BlockedV => f with { Step = Step.R4, End = EndReason.Blocked },
        Verdict.SaveFailedV => f with { Step = Step.R4, End = EndReason.SaveFailed },
        Verdict.Applied a when PostCheckOk(f.ExpectedHard, a.PostHard) => f with { Step = Step.R5, PostHard = a.PostHard },
        Verdict.Applied a => f with { Step = Step.R4, End = EndReason.RolledBack, PostHard = a.PostHard },
        _ => f,
    };

    /// <summary>操作ログの 1 行（承認）。</summary>
    public static string ApprovalLog(string decisionId, string trialId, string reason, IReadOnlyList<string> changes) =>
        $"S6承認 decisionId={decisionId} trial={trialId} 理由={reason.Trim().Replace('\n', ' ')} 前→後 {string.Join("、", changes)} {ApproverLine}";

    /// <summary>操作ログの 1 行（反映）。結果＝成功・失敗・復旧。</summary>
    public static string ApplyLog(string result, int check) => $"S6反映 結果={result} check={check}";
}
