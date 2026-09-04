namespace MagiEngine.V6;

/// <summary>
/// [レビュー指摘 2026-09-04] エンジン内の締切・経過・停滞判定に使う**単調時計**（ms）。
/// 旧: <c>DateTimeOffset.UtcNow</c>（壁時計）を締切に使っていた。壁時計は NTP 補正・手動の時刻変更・
/// スリープ復帰で前後する＝戻れば予算を大幅に超えて回り続け、進めば即時に期限切れ。
/// <see cref="Environment.TickCount64"/> は起動からの単調な ms。エンジン内で扱う「時刻(ms)」はすべてここから取る
/// （呼出側・テストが渡す <c>deadlineMs</c> も同じ時計で作ること）。ログに出す実時刻（<c>MirrorLog.Ts</c>・
/// <c>StartedAt</c>）と乱数シードだけは壁時計のまま。Kotlin 原本 <c>EngineClock</c>（3.490.0）と同値。
/// </summary>
public static class EngineClock
{
    public static long NowMs() => Environment.TickCount64;
}
