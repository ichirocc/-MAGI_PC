using System.Globalization;

namespace MagiApp.ViewModels;

/// <summary>
/// Kotlin原本 <c>KeptResultText</c>。keep-best で前回を維持したときの文言。比べる順は betterReport（必須→重み付き評価点→合計）なので、
/// 決め手になった項目と重みを必ず見せる（合計だけ減って維持されると理由が読めない）。
/// </summary>
public static class KeptResultText
{
    public sealed record Score(long Hard, double Weighted, int Total);

    private static string W(Score s) =>
        ((long)Math.Round(s.Weighted, MidpointRounding.AwayFromZero)).ToString("#,0", CultureInfo.InvariantCulture);
    private static string Parts(Score s) => $"必須{s.Hard}・評価点{W(s)}・合計{s.Total}";

    public static string Reason(Score now, Score prev) =>
        now.Hard != prev.Hard ? (now.Hard > prev.Hard ? "必須違反が多いため" : "必須違反が少ないため")
        : now.Weighted != prev.Weighted ? (now.Weighted > prev.Weighted ? "重み付き評価点が高いため" : "重み付き評価点が低いため")
        : now.Total != prev.Total ? (now.Total > prev.Total ? "違反の合計が多いため" : "違反の合計が少ないため")
        : "同じ点数のため";

    public static string Screen(Score now, Score prev) =>
        $"改善が確認できなかったため、前回の勤務表を維持しました（今回の案は採用していません）。理由：{Reason(now, prev)}（今回 {Parts(now)}／前回 {Parts(prev)}。比べる順＝必須→評価点→合計）。";

    public static string Log(string prefix, Score now, Score prev) =>
        $"{prefix}: 今回 {Parts(now)} は前回 {Parts(prev)} 以下に改善せず（{Reason(now, prev)}）→ 前回を維持";

    /// <summary>維持の文言に理由を1文足す（入口で上限0のセルを外して必須が増えたとき等）。</summary>
    public static string WithNote(string text, string? note) => note is null ? text : $"{text} {note}";
}

/// <summary>計算が終わったときの通知（Kotlin <c>completedText</c>、判断支援 UX 仕様 §9）。</summary>
public static class CompletedText
{
    public static string Of(int hard, IReadOnlyDictionary<string, int> breakdown, string how) =>
        hard == 0 ? $"計算が完了しました{how}。設定した必須条件を満たしています。配布前の確認に進んでください（要調整 {AnalysisTriage.UnitTotals(breakdown, MagiEngine.V6.MirrorKeys.Soft) ?? "なし"}）"
                  : $"計算が完了しました{how}。{AnalysisTriage.ReportCountsText(breakdown)}";

    /// <summary>採用した結果の件数が入力より増えたのに重み付き評価点が下がったとき、採用の理由を 1 文で（件数だけ見ると悪化に見えるため）。それ以外は null。</summary>
    public static string? AdoptedDespiteMore(int prevTotal, int newTotal, double prevWeighted, double newWeighted) =>
        newTotal > prevTotal && newWeighted < prevWeighted ? $"件数は +{newTotal - prevTotal} ですが、重い違反が減ったため改善として採用しました" : null;
}
