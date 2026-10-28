using System.Text.RegularExpressions;
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

    /// <summary>カードの見出し。何もしなかった輪は出さない（null）。要調整は動いた単位の 1 つだけ（全単位は「当てた手」の中＝<see cref="SoftDetail"/>）。</summary>
    public static string? Headline(AgentRecord r) =>
        r.Steps.Count == 0 ? null
        : $"代行で {r.Steps.Count} 手を当てました（" +
          (r.HardBefore != r.HardAfter ? $"必須違反 {r.HardBefore}→{r.HardAfter}" : $"要調整 {SoftHeadDelta(r.SoftBefore, r.SoftAfter)}") + "）";

    /// <summary>通知の 1 行（カードと同じ文を 2 か所に出さない＝内訳はカード）。</summary>
    public static string? Notice(AgentRecord r) => r.Steps.Count == 0 ? null : $"代行で {r.Steps.Count} 手を当てました";

    /// <summary>「当てた手」を開いたときの要調整の全単位。</summary>
    public static string SoftDetail(AgentRecord r) => $"要調整 {r.SoftBefore} → {r.SoftAfter}";

    private static readonly Regex SoftToken = new(@"^(\d+)(\D+)$");

    /// <summary>「147件・3人・80回」と「140件・4人・70回」から、値が動いた最初の単位を「147→140件」で（消えた単位は 0）。どれも動かなければ先頭の単位。</summary>
    public static string SoftHeadDelta(string before, string after)
    {
        static List<(string Unit, string N)> Parse(string s) => s.Split('・')
            .Select(t => SoftToken.Match(t.Trim())).Where(m => m.Success).Select(m => (m.Groups[2].Value, m.Groups[1].Value)).ToList();
        var b = Parse(before);
        var a = new Dictionary<string, string>();
        foreach (var (u, n) in Parse(after)) a[u] = n;
        if (b.Count == 0) return $"{before}→{after}";
        var pick = b.FirstOrDefault(x => !a.TryGetValue(x.Unit, out var v) || v != x.N);
        if (pick == default) pick = b[0];
        return $"{pick.N}→{(a.TryGetValue(pick.Unit, out var now) ? now : "0")}{pick.Unit}";
    }

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
