using System.Threading;

namespace MagiEngine.V6;

/// <summary>
/// Faithful port of Kotlin's <c>TuningTelemetry</c> object (in <c>V6HotfixPasses.kt</c>) — a
/// read-only, session-scoped counter set that answers "did this run's tuning toggles actually do
/// anything?" for the settings-tab advanced-tuning switches.
///
/// [Kotlin 3.356.0/ユーザー指示「オプションを減らせるようにログ強化する」] 設定タブ→詳細設定の調整
/// トグルが**その実行で実際に何をしたか**を数える。旧: トグルは6つあるのに、ログを見ても「ONにした
/// 意味があったか」が読めず、減らす判断ができなかった（`禁止連続の崩し範囲`・`立て直し方` に至っては
/// 実行の痕跡が一切出ない）。数回まわして毎回「観測なし」なら、そのトグルは消してよい、と言える。
/// [3.409.21] この計測が実際に判断を支えた＝立て直し方(adaptiveEscapeControl)とロール内並列SA
/// (portfolioRoleParallelSa)は単体 A/B の中立を根拠に削除（<see cref="PolishGate"/> 冒頭の記録参照）。
///
/// 読み取り専用の計数のみ＝探索・採否・スコアには一切影響しない。<c>Optimize()</c> 入口で
/// <see cref="Reset"/> する。
///
/// [3.360.1/敵対検証, Kotlin原本の履歴] 旧実装は素の可変フィールドへの `++`（read-modify-write）で、
/// 8並列ワーカーから加算されるため取りこぼしていた（<c>parityChecks</c> は SA/LAHC/ALNS/研磨の
/// 4経路×全ワーカーから毎チャンク）。<see cref="Interlocked"/> による原子加算へ（この C# 移植は
/// 最初から <see cref="Interlocked"/> を使うため、この回帰は存在しない）。加算は最も多い
/// <c>wideC3nCalls</c> でも実行あたり1万回弱＝checker 1回より桁違いに安く、速度への影響はない。
///
/// [C#移植上の判断] Kotlin原本は <c>AtomicInteger</c> フィールド + <c>fun reset()</c>/<c>fun
/// summary(...)</c> というオブジェクト（フィールドを直接公開する形）だが、C# ではフィールドを
/// private のまま隠し、増分は専用メソッドとして公開する（<see cref="V6NativeOptimizer.RunSlot"/>
/// の <c>Interlocked</c> ラップ静的メソッド群と同じ流儀）。読み取りは <see cref="Volatile.Read"/>
/// （<c>PolishGate</c> の <c>double</c> フィールドと同じ既定の読み取り idiom）、リセット時の
/// ゼロ書き込みは <see cref="Volatile.Write"/>（Kotlin の <c>AtomicInteger.set(0)</c> は CAS でなく
/// 単純な原子書き込みのため、<c>Interlocked.Exchange</c> の戻り値=旧値は使わない＝
/// <see cref="Volatile.Write"/> がより忠実）。
/// </summary>
public static class TuningTelemetry
{
    private static int _c3nFilterSkipped;
    private static int _wideC3nDiffered;
    private static int _wideC3nCalls;
    private static int _lahcEntered;
    private static int _parityChecks;

    /// <summary>禁止連続の事前フィルタが checker を呼ばずに落とした候補数。</summary>
    public static void IncrementC3nFilterSkipped() => Interlocked.Increment(ref _c3nFilterSkipped);

    /// <summary>禁止連続の崩し範囲が既定(前後1日)と違う候補日を返した回数（広がる／狭まるの両方）。</summary>
    public static void IncrementWideC3nDiffered() => Interlocked.Increment(ref _wideC3nDiffered);

    /// <summary>同・呼ばれた回数（広がらなかった分も含む）。</summary>
    public static void IncrementWideC3nCalls() => Interlocked.Increment(ref _wideC3nCalls);

    /// <summary>仕上げ最適化により PhaseB(LAHC) へ切り替わった回数。</summary>
    public static void IncrementLahcEntered() => Interlocked.Increment(ref _lahcEntered);

    /// <summary>Kotlin照合を実施した回数（ネイティブ結果を採用する直前の再評価）。</summary>
    public static void IncrementParityChecks() => Interlocked.Increment(ref _parityChecks);

    /// <summary>
    /// <see cref="IncrementParityChecks"/> の現在値を読む。Kotlin原本は5カウンタ全てを公開フィールドと
    /// して直接読めるが、このC#移植では読み取りアクセサは実際に必要な箇所（並行性の回帰テスト）にのみ
    /// 用意する（残り4カウンタは <see cref="Summary"/> 経由でしか読まれないため、専用アクセサは不要）。
    /// </summary>
    public static int ParityChecksCount() => Volatile.Read(ref _parityChecks);

    /// <summary>
    /// 実行ごとに 0 へ戻す（<c>Optimize()</c> 入口）。
    ///
    /// **既知の限界（意図的に残す）**: これは実行をまたぐ static なので、実行が重なると
    /// （旧実行が協調キャンセルを待つ間など）後発の reset が先行実行の計数を消し、両者が同じ箱へ
    /// 加算する。<see cref="V6NativeOptimizer.RunSlot"/>（Kotlin 3.335.0 相当）は同型の問題を
    /// コルーチン/非同期コンテキストで実行ごとの箱を運ぶことで解いたが、加算元の
    /// <see cref="V6SearchOperators.BreakableDaysFor"/> などは非 async の純関数でコンテキストを
    /// 読めないため同じ手が使えない。影響は**片方のログの診断値がずれる**だけで、勤務表・採否・
    /// スコアには一切触れない。
    /// </summary>
    public static void Reset()
    {
        Volatile.Write(ref _c3nFilterSkipped, 0);
        Volatile.Write(ref _wideC3nDiffered, 0);
        Volatile.Write(ref _wideC3nCalls, 0);
        Volatile.Write(ref _lahcEntered, 0);
        Volatile.Write(ref _parityChecks, 0);
    }

    /// <summary>各トグルの ON/OFF と、その実行で観測できた効果を1行にまとめる。</summary>
    public static string Summary(bool nativeOn, bool parityOn, bool softPolishOn)
    {
        static string Eff(bool on, int n, string unit) =>
            !on ? "OFF" : n > 0 ? $"ON({n}{unit})" : "ON(この実行では観測なし)";

        // 同一の値を2回読むと表示内で食い違う（別スレッドが加算しうる）ため、判定も表示も1回の読みで済ませる。
        var calls = Volatile.Read(ref _wideC3nCalls);
        var differed = Volatile.Read(ref _wideC3nDiffered);
        string wide;
        if (!PolishGate.WideC3nBreakDays) wide = "OFF";
        else if (calls == 0) wide = "ON(この実行では出番なし)";
        else if (differed == 0) wide = $"ON({calls}回呼ばれたが既定(前後1日)と同じ範囲＝OFFと差なし)";
        else wide = $"ON({calls}回中{differed}回は既定(前後1日)と違う範囲を探索)";

        var parityChecks = Volatile.Read(ref _parityChecks);
        var c3nFilterSkipped = Volatile.Read(ref _c3nFilterSkipped);
        var lahcEntered = Volatile.Read(ref _lahcEntered);

        // タグ（MirrorLog tag="設定の効き"）が同じ語を出すため、ここに前置きを付けると
        // 実機ログで「設定の効き: 設定の効き: …」と二重になる（Kotlin 3.409.16 で実機ログにより発覚）。
        // 本文だけを返す。
        return "ネイティブ加速=" + (nativeOn ? "ON" : "OFF") +
            " / Kotlin照合=" + Eff(parityOn, parityChecks, "回") +
            " / 禁止連続の事前フィルタ=" + Eff(PolishGate.FilterC3nIncrease, c3nFilterSkipped, "件の無駄な検査を省略・勤務表は不変") +
            " / 禁止連続の崩し範囲=" + wide +
            " / 仕上げ最適化=" + Eff(softPolishOn, lahcEntered, "回LAHCへ切替");
    }
}
