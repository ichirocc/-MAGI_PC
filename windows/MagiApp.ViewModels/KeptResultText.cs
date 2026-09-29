using System.Globalization;

namespace MagiApp.ViewModels;

/// <summary>
/// Kotlin原本 <c>KeptResultText</c>。keep-best で前回を維持したときの文言。比べる順は betterReport（必須→重み→合計）なので、
/// 決め手になった項目と重みを必ず見せる（合計だけ減って維持されると理由が読めない）。
/// </summary>
public static class KeptResultText
{
    public sealed record Score(long Hard, double Weighted, int Total);

    private static string W(Score s) =>
        ((long)Math.Round(s.Weighted, MidpointRounding.AwayFromZero)).ToString("#,0", CultureInfo.InvariantCulture);
    private static string Parts(Score s) => $"必須{s.Hard}・重み{W(s)}・合計{s.Total}";

    public static string Reason(Score now, Score prev) =>
        now.Hard != prev.Hard ? (now.Hard > prev.Hard ? "必須が多いため" : "必須が少ないため")
        : now.Weighted != prev.Weighted ? (now.Weighted > prev.Weighted ? "重みが大きいため" : "重みが小さいため")
        : now.Total != prev.Total ? (now.Total > prev.Total ? "合計が多いため" : "合計が少ないため")
        : "同じ点数のため";

    public static string Screen(Score now, Score prev) =>
        $"今回（{Parts(now)}）は前回（{Parts(prev)}）より改善しませんでした（{Reason(now, prev)}。比べる順＝必須→重み→合計）。前回の結果を維持します。";

    public static string Log(string prefix, Score now, Score prev) =>
        $"{prefix}: 今回 {Parts(now)} は前回 {Parts(prev)} 以下に改善せず（{Reason(now, prev)}）→ 前回を維持";
}
