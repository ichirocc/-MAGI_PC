using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>代行役（AX）が 1 回の輪で当てた手の記録（画面のカードと操作ログに使う）。<paramref name="UndoSerial"/>＝代行の Undo 段（先頭にあるときだけカードから戻せる）。</summary>
public sealed record AgentRecord(
    IReadOnlyList<string> Steps,
    int HardBefore, int HardAfter,
    string SoftBefore, string SoftAfter,
    string Stop,
    long UndoSerial = 0L);

/// <summary>代行役のカード・設定の文と判断待ちの数え方。Kotlin <c>AgentText.kt</c> の移植。</summary>
public static class AgentText
{
    /// <summary>代行の 1 回の輪の上限（探索 1 回は <see cref="AgentLoop.SearchMs"/>）。</summary>
    public const long BudgetMs = 30_000L;
    /// <summary>1 手の案が尽きたときの「自動で整える」の上限（秒）。</summary>
    public const int WidenMaxSec = 10;

    public const string RunningLine = "代行で仕上げています…（安全な勤務の変更を順に当てています）";
    public const string ToggleLabel = "作成のあと代行で仕上げる";
    public const string ToggleNote = "必須違反が残ったら、安全な勤務の変更だけを自動で当てます。希望と設定の変更は承認を待ちます。";
    public const string ResumeNote = "承認すると、代行が続きを回します。";

    /// <summary>カードの見出し。何もしなかった輪は出さない（null）。</summary>
    public static string? Headline(AgentRecord r) =>
        r.Steps.Count == 0 ? null
        : $"代行で {r.Steps.Count} 手を当てました（" +
          (r.HardBefore != r.HardAfter ? $"必須違反 {r.HardBefore}→{r.HardAfter}" : $"要調整 {r.SoftBefore}→{r.SoftAfter}") + "）";

    /// <summary>判断待ちの内訳（ホームの判断のカード・設定ミスのカードにある承認の数）。空なら判断待ちは無い。</summary>
    public static List<string> PendingItems(int wishes, bool relaxFound, int issues)
    {
        var outList = new List<string>();
        if (wishes > 0) outList.Add($"希望の見直し {wishes}件");
        if (relaxFound) outList.Add("設定を緩める候補");
        if (issues > 0) outList.Add($"設定ミスの直し {issues}件");
        return outList;
    }

    /// <summary>判断待ちの 1 行。</summary>
    public static string? PendingLine(IReadOnlyList<string> items) => items.Count == 0 ? null : "判断待ち：" + string.Join("・", items);

    /// <summary>カードの判断待ちの 1 行（必須が残ったときだけ）。希望の数は希望を外したらの試算の候補（直接＋人員不足の行）、設定ミスは診断の件数。</summary>
    public static string? PendingLineFor(UiState ui, bool relaxFound)
    {
        if ((ui.AgentRecord?.HardAfter ?? 0) <= 0) return null;
        var c = NextActionGuide.WishTrialCandidatesOf(ui);
        return PendingLine(PendingItems(c.Direct.Count + c.Shortfall.Sum(g => g.Rows.Count), relaxFound, ui.SettingIssues.Count));
    }

    /// <summary>輪の止まり方（操作ログ用）。</summary>
    public static string StopWord(AgentLoop.Stop stop) => stop switch
    {
        AgentLoop.Stop.NoCandidate => "当てられる手が尽きた",
        AgentLoop.Stop.RoundLimit => "手数の上限",
        AgentLoop.Stop.TimeLimit => "時間の上限",
        _ => "中断",
    };
}
