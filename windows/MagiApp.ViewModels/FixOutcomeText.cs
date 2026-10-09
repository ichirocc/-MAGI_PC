namespace MagiApp.ViewModels;

/// <summary>直し方を当てた結果の 1 行（Kotlin <c>FixOutcome</c>）。表示は <c>MagiViewModel.FixOutcomeLine</c>＝盤面か設定が変わったら出さない。</summary>
public sealed record FixOutcome(string Line);

/// <summary>直し方を当てたあと、同じ問題について「どうなったか」を 1 行で返す（Kotlin <c>FixOutcomeText.kt</c>、3.643.0）。</summary>
public static class FixOutcomeText
{
    public static string Applied(string label, int hardBefore, int hardAfter, int totalBefore, int totalAfter) =>
        hardAfter == 0 && hardBefore > 0 ? $"{label} を当てました。必須違反はなくなりました（合計 {totalBefore}→{totalAfter}）。"
        : hardAfter < hardBefore ? $"{label} を当てました。必須違反 {hardBefore}→{hardAfter}。ほかの必須違反は {hardAfter} 件残っています。"
        : $"{label} を当てました。必須違反は変わらず {hardAfter} 件（合計 {totalBefore}→{totalAfter}）。";

    /// <param name="stillMiss">再検査後にその枠でまだ足りない人数（null＝解消）。</param>
    public static string Guided(string dayLabel, string shiftSymbol, int? stillMiss, int hardAfter) =>
        stillMiss is null
            ? $"{dayLabel} の「{shiftSymbol}」の人員不足を解消しました。" + (hardAfter > 0 ? $"ほかの必須違反は {hardAfter} 件残っています。" : "必須違反はなくなりました。")
            : $"{dayLabel} の「{shiftSymbol}」はまだ {stillMiss}人 足りません。必須違反は {hardAfter} 件です。";
}
