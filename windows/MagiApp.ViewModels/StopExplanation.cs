using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>次の一手の種類（ホームの結果カードは既存のボタンへ対応づける）。Kotlin <c>StopNext</c>。</summary>
public enum StopNext { None, MoreTime, ReviewStaffing, ReviewWishes, FindFix }

/// <summary>停滞で早く終えた理由と次の一手の一文（Kotlin <c>ui/StopExplanation.kt</c>、3.643.0、<c>docs/stall_escape.md</c> §12）。内部名を出さない。純関数＝WinUI から切り離してテストする。</summary>
public sealed record StopExplanation(string Line, StopNext Next)
{
    public static StopExplanation? Of(V6FinalPort.StopSummary s)
    {
        var hard = s.RemainingHard;
        if (hard == 0)
            return s.EarlyStop ? new StopExplanation($"改善が止まったので {s.UsedSec} 秒で終えました（予算 {s.BudgetSec} 秒）。必須違反はありません。", StopNext.None) : null;
        return s.Kind switch
        {
            V6FinalPort.StopKind.Deadline =>
                new StopExplanation($"制限時間（{s.BudgetSec} 秒）いっぱいまで探しました。最後の改善から {s.StalledSec} 秒で、必須違反は {hard} 件残っています。", StopNext.MoreTime),
            V6FinalPort.StopKind.PlateauFloor =>
                new StopExplanation($"{s.UsedSec} 秒で終えました。残る必須違反 {hard} 件は、担当できる人数が足りない人員不足で、探索では減りません。", StopNext.ReviewStaffing),
            V6FinalPort.StopKind.WishFloor =>
                new StopExplanation($"{s.UsedSec} 秒で終えました。残る必須違反 {hard} 件は希望どうしのぶつかりで、いまの希望のままでは減りません。", StopNext.ReviewWishes),
            V6FinalPort.StopKind.C3nWallCertified =>
                new StopExplanation($"{s.UsedSec} 秒で終えました。残る必須違反 {hard} 件は禁止の並びで、本人の希望で固定された並びです。希望を変えない限り崩せません。", StopNext.ReviewWishes),
            V6FinalPort.StopKind.C3nWallEmpirical =>
                new StopExplanation($"{s.UsedSec} 秒で終えました。残る必須違反 {hard} 件は禁止の並びで、1 マスの変更・玉突き・隣の日の調整では崩せませんでした。", StopNext.FindFix),
            _ =>
                new StopExplanation($"改善が {s.StalledSec} 秒止まったので {s.UsedSec} 秒で終えました（予算 {s.BudgetSec} 秒）。必須違反 {hard} 件が残っています。", StopNext.FindFix),
        };
    }

    public static string? NextLabel(StopNext n) => n switch
    {
        StopNext.None => null,
        StopNext.MoreTime => "時間を増やして再作成する",
        StopNext.ReviewStaffing => "担当を見直す（人を増やす）",
        StopNext.ReviewWishes => "希望を見直す",
        _ => "直し方を探す",
    };
}
