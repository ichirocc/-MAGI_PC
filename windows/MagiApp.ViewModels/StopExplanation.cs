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
        var used = $"使った時間 {s.UsedSec} 秒・組む時間 {s.BudgetSec} 秒";
        // 必須 0 なら早く終えた理由は言わない（結果カードの見出しで足りる）。
        if (s.RemainingHard == 0) return null;
        return s.Kind switch
        {
            V6FinalPort.StopKind.Deadline =>
                new StopExplanation($"組む時間（{s.BudgetSec} 秒）いっぱい探しました（最後の改善から {s.StalledSec} 秒）。残る必須違反は {hard}です。",
                    // 探索時間がすでに上限なら「延ばす」は勧めない。
                    s.BudgetSec < V6FinalPort.MaxOptimizeSec ? StopNext.MoreTime : StopNext.FindFix),
            V6FinalPort.StopKind.PlateauFloor =>
                new StopExplanation($"{s.UsedSec} 秒で終えました。残る必須違反 {hard}は担当できる職員が足りない人員不足です。担当条件か職員を見直してください。", StopNext.ReviewStaffing),
            V6FinalPort.StopKind.WishFloor =>
                new StopExplanation($"{s.UsedSec} 秒で終えました。残る必須違反 {hard}は同時に満たせない希望によるものです。希望の調整が必要です。", StopNext.ReviewWishes),
            V6FinalPort.StopKind.C3nWallCertified =>
                new StopExplanation($"{s.UsedSec} 秒で終えました。残る必須違反 {hard}は希望で固定された禁止の並びです。希望の調整が必要です。", StopNext.ReviewWishes),
            V6FinalPort.StopKind.C3nWallEmpirical =>
                new StopExplanation($"{s.UsedSec} 秒で終えました。残る必須違反 {hard}は禁止の並びです。1 マスの変更・玉突き・隣接日の調整では解消できませんでした。", StopNext.FindFix),
            _ =>
                new StopExplanation($"{s.StalledSec} 秒間、改善が見られなかったため、組むのを早めに終えました（{used}）。必須違反 {hard}が残っています。", StopNext.FindFix),
        };
    }

    /// <summary>次の一手は直す場所（タブと節）から言う＝カードの文はボタンでないので、行き先を文で示す。</summary>
    public static string? NextLabel(StopNext n) => n switch
    {
        StopNext.None => null,
        StopNext.MoreTime => "設定タブの「組む時間を変える」で組む時間を延ばし、再作成する",
        StopNext.ReviewStaffing => "編集タブの「年間マスター」①で、担当できる職員を確認する",
        StopNext.ReviewWishes => "編集タブの「月次条件」で希望を見直す",
        _ => "分析タブで直し方を探す",
    };
}
