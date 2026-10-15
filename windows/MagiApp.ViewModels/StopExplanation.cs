using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>次の一手の種類（ホームの結果カードは既存のボタンへ対応づける）。Kotlin <c>StopNext</c>。</summary>
public enum StopNext { None, MoreTime, ReviewStaffing, ReviewWishes, FindFix }

/// <summary>停滞で早く終えた理由と次の一手の一文（Kotlin <c>ui/StopExplanation.kt</c>、3.643.0、<c>docs/stall_escape.md</c> §12）。内部名を出さない。純関数＝WinUI から切り離してテストする。</summary>
public sealed record StopExplanation(string Line, StopNext Next)
{
    public static StopExplanation? Of(V6FinalPort.StopSummary s, IReadOnlyDictionary<string, int>? breakdown = null)
    {
        var hard = AnalysisTriage.HardText(breakdown, s.RemainingHard);
        // 使った時間は後処理まで含めた全体、無改善の秒数は探索の終了時点の値（どちらの時点かを文で言う）。
        var used = $"使った時間 {s.UsedSec} 秒・探索時間 {s.BudgetSec} 秒";
        // 必須 0 なら早く終えた理由は言わない（結果カードの見出しで足りる）。
        if (s.RemainingHard == 0) return null;
        return s.Kind switch
        {
            V6FinalPort.StopKind.Deadline =>
                new StopExplanation($"探索時間（{s.BudgetSec} 秒）いっぱいまで探しました。探索の終了時点で、最後の改善から {s.StalledSec} 秒が経過していました。最終結果の必須違反は {hard}です。",
                    // 探索時間がすでに上限なら「延ばす」は勧めない。
                    s.BudgetSec < V6FinalPort.MaxOptimizeSec ? StopNext.MoreTime : StopNext.FindFix),
            V6FinalPort.StopKind.PlateauFloor =>
                new StopExplanation($"{s.UsedSec} 秒で終えました。残る必須違反 {hard}は、担当できる職員が足りないための人員不足です。解消するには、担当条件や配置できる職員の見直しが必要です。", StopNext.ReviewStaffing),
            V6FinalPort.StopKind.WishFloor =>
                new StopExplanation($"{s.UsedSec} 秒で終えました。残る必須違反 {hard}は、同時には満たせない希望によるものです。解消するには、対象の希望を調整する必要があります。", StopNext.ReviewWishes),
            V6FinalPort.StopKind.C3nWallCertified =>
                new StopExplanation($"{s.UsedSec} 秒で終えました。残る必須違反 {hard}は禁止の並びで、希望によって勤務が固定されています。解消には希望の調整が必要です。", StopNext.ReviewWishes),
            V6FinalPort.StopKind.C3nWallEmpirical =>
                new StopExplanation($"{s.UsedSec} 秒で終えました。残る必須違反 {hard}は禁止の並びです。1 マスの変更、複数の勤務の入れ替え（玉突き）、隣接日の調整を試しましたが、解消する案は見つかりませんでした。", StopNext.FindFix),
            _ =>
                new StopExplanation($"{s.StalledSec} 秒間、改善が見られなかったため、探索を早めに終了しました（{used}）。必須違反 {hard}が残っています。", StopNext.FindFix),
        };
    }

    /// <summary>次の一手は直す場所（タブと節）から言う＝カードの文はボタンでないので、行き先を文で示す。</summary>
    public static string? NextLabel(StopNext n) => n switch
    {
        StopNext.None => null,
        StopNext.MoreTime => "設定タブの「勤務表をつくる時間」で探索時間を延ばし、再作成する",
        StopNext.ReviewStaffing => "編集タブの「年間マスター」①で、担当できる職員を確認する",
        StopNext.ReviewWishes => "編集タブの「月次条件」で希望を見直す",
        _ => "分析タブで直し方を探す",
    };
}
