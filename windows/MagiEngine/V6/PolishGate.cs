namespace MagiEngine.V6;

/// <summary>
/// Faithful (partial) port of Kotlin's <c>PolishGate</c> object (in <c>V6HotfixPasses.kt</c>) — a
/// session-only, UI-toggle -&gt; engine-flag settings passthrough (not persisted; same shape as
/// <c>NativeGate</c> per the Kotlin source comment).
///
/// [フェーズ5b, 移植範囲の限定] Kotlin原本は <c>filterC3nIncrease</c>/<c>normalStallFraction</c> も
/// 持つが、それらは <c>V6HotfixPasses.kt</c>（フェーズ6）の消費者しか読まない。本フェーズが必要とする
/// <see cref="WideC3nBreakDays"/>（<c>V6SearchOperators.BreakableDaysFor</c> が読む）だけを先に移植し、
/// 残りはフェーズ6でこのファイルへ追記する（単一ソースを2箇所に複製して片方が取り残される事故＝
/// このコードベースの Kotlin 側履歴に繰り返し記録されているアンチパターンを避けるため）。
/// </summary>
public static class PolishGate
{
    /// <summary>
    /// [c3n 回避の範囲拡張, Kotlin 3.303.0] 禁止連続を崩しに行く日を j±1 固定から「パターンがまたぐ
    /// 全日」へ広げる。3連（例: Dﾃ→休→A4）の先頭 j-2 に届くようになる<b>正しい</b>一般化だが、
    /// 実データ3件で利得が一貫しなかったため既定 OFF（golden=中立 / real=weighted 改善だが covU 2件を
    /// c3n 2件へ付け替え・c1 悪化 / user=悪化）。「安全であること」と「有益であること」は別、という
    /// Kotlin 側の一貫した設計規律により、計測が支持しない既定変更はしない。
    /// </summary>
    public static volatile bool WideC3nBreakDays = false;
}
