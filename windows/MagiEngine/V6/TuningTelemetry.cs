using System.Globalization;
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
    /// <summary>[3.540.0] 回数連鎖研磨が採用した連鎖数。</summary>
    private static int _countChainApplied;
    /// <summary>[3.535.0] 公平化/適切回数の他ソフト許容(6%)が、素のbetterReportなら却下された手を採用に転じさせた回数。</summary>
    private static int _aptFairToleranceUsed;

    /// <summary>必須が増える候補の事前フィルタ（ブロック巡回交換）が checker を呼ばずに落とした候補数。</summary>
    public static void IncrementC3nFilterSkipped() => Interlocked.Increment(ref _c3nFilterSkipped);

    /// <summary>禁止連続の崩し範囲が既定(前後1日)と違う候補日を返した回数（広がる／狭まるの両方）。</summary>
    public static void IncrementWideC3nDiffered() => Interlocked.Increment(ref _wideC3nDiffered);

    /// <summary>同・呼ばれた回数（広がらなかった分も含む）。</summary>
    public static void IncrementWideC3nCalls() => Interlocked.Increment(ref _wideC3nCalls);

    /// <summary>仕上げ最適化により PhaseB(LAHC) へ切り替わった回数。</summary>
    public static void IncrementLahcEntered() => Interlocked.Increment(ref _lahcEntered);

    /// <summary>Kotlin照合を実施した回数（ネイティブ結果を採用する直前の再評価）。</summary>
    public static void IncrementParityChecks() => Interlocked.Increment(ref _parityChecks);

    /// <summary>[3.540.0] 回数連鎖研磨が1連鎖を採用するたびに呼ぶ（採用連鎖数の加算）。</summary>
    public static void AddCountChainApplied(int n) => Interlocked.Add(ref _countChainApplied, n);

    /// <summary>[3.535.0] <see cref="V6HotfixPasses.ToleratedBetter"/> が容認で採用に転じさせるたびに呼ぶ。</summary>
    public static void IncrementAptFairToleranceUsed() => Interlocked.Increment(ref _aptFairToleranceUsed);

    /// <summary>[3.694.0] 容認採用 1 件の記録（族・族の件数・合計・必須の前後、素の差・容認量・実効差）。値は <see cref="V6HotfixPasses.ToleratedBetter"/> が判定に使ったもの。</summary>
    public sealed record ToleratedNote(string Family, int FamilyBefore, int FamilyAfter, int TotalBefore, int TotalAfter, int HardBefore, int HardAfter,
        double RawDelta, double Forgiven, double EffectiveDelta);

    /// <summary>保持する容認採用の件数（先頭だけ。総数は <see cref="AptFairToleranceUsedCount"/>）。</summary>
    public const int ToleratedNoteCap = 5;
    private static readonly ToleratedNote?[] _toleratedNotes = new ToleratedNote?[ToleratedNoteCap];
    private static int _toleratedNoteClaims;

    /// <summary>[3.694.0] 容認採用の分岐（既存の加算の隣）から 1 回。スロットは先頭 5 件だけ埋め、満杯なら何もしない。</summary>
    public static void NoteTolerated(string family, int familyBefore, int familyAfter, int totalBefore, int totalAfter, int hardBefore, int hardAfter,
        double rawDelta, double forgiven, double effectiveDelta)
    {
        var idx = Interlocked.Increment(ref _toleratedNoteClaims) - 1;
        if (idx < ToleratedNoteCap)
            Volatile.Write(ref _toleratedNotes[idx], new ToleratedNote(family, familyBefore, familyAfter, totalBefore, totalAfter, hardBefore, hardAfter, rawDelta, forgiven, effectiveDelta));
    }

    /// <summary>保持している容認採用（記録順・最大 5 件）。</summary>
    public static IReadOnlyList<ToleratedNote> ToleratedNotes()
    {
        var outNotes = new List<ToleratedNote>(ToleratedNoteCap);
        for (var i = 0; i < ToleratedNoteCap; i++) { var n = Volatile.Read(ref _toleratedNotes[i]); if (n is not null) outNotes.Add(n); }
        return outNotes;
    }

    /// <summary>[3.694.0] 「容認採用」の行（タグを除く本文）。先頭 5 件は 1 行ずつ、6 件目以降は 1 行の省略語。小数は InvariantCulture。</summary>
    public static IReadOnlyList<string> ToleratedLines(IReadOnlyList<ToleratedNote> notes, int total)
    {
        static string Signed(double v) => (v >= 0 ? "+" : "") + v.ToString("0.0", CultureInfo.InvariantCulture);
        var lines = new List<string>(notes.Count + 1);
        for (var i = 0; i < notes.Count; i++)
        {
            var n = notes[i];
            lines.Add($"{i + 1}/{total}: {n.Family} 件数 {n.FamilyBefore}→{n.FamilyAfter} ・ 合計 {n.TotalBefore}→{n.TotalAfter} ・ 必須 {n.HardBefore}→{n.HardAfter}" +
                $" ・ 容認 {n.Forgiven.ToString("0.0", CultureInfo.InvariantCulture)} ・ 素の差 {Signed(n.RawDelta)} ・ 実効差 {n.EffectiveDelta.ToString("0.0", CultureInfo.InvariantCulture)}");
        }
        if (total > notes.Count) lines.Add($"ほか {total - notes.Count} 件（先頭 {ToleratedNoteCap} 件のみ）");
        return lines;
    }

    /// <summary>同・この実行の保持分と総数から。</summary>
    public static IReadOnlyList<string> ToleratedLines() => ToleratedLines(ToleratedNotes(), Volatile.Read(ref _aptFairToleranceUsed));

    /// <summary>
    /// <see cref="IncrementParityChecks"/> の現在値を読む。Kotlin原本は5カウンタ全てを公開フィールドと
    /// して直接読めるが、このC#移植では読み取りアクセサは実際に必要な箇所（並行性の回帰テスト）にのみ
    /// 用意する（残り4カウンタは <see cref="Summary"/> 経由でしか読まれないため、専用アクセサは不要）。
    /// </summary>
    public static int ParityChecksCount() => Volatile.Read(ref _parityChecks);

    /// <summary>同・<see cref="IncrementAptFairToleranceUsed"/> の現在値を読む（並行性の回帰テスト用）。</summary>
    public static int AptFairToleranceUsedCount() => Volatile.Read(ref _aptFairToleranceUsed);

    /// <summary>ブロック巡回交換の事前フィルタが落とした候補数（Kotlin <c>c3nFilterSkipped.get()</c> と同じ読み出し）。</summary>
    public static int C3nFilterSkippedCount() => Volatile.Read(ref _c3nFilterSkipped);

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
        Volatile.Write(ref _countChainApplied, 0);
        Volatile.Write(ref _aptFairToleranceUsed, 0);
        Volatile.Write(ref _toleratedNoteClaims, 0);
        for (var i = 0; i < ToleratedNoteCap; i++) Volatile.Write(ref _toleratedNotes[i], null);
    }

    /// <summary>[3.694.0] 設定の表示語（「設定の効き」と「設定の開始時」が同じ語を使う。順は固定）。</summary>
    public static readonly IReadOnlyList<string> ToggleWords = new[]
    {
        "ネイティブ加速", "Kotlin照合", "必須が増える候補の事前フィルタ", "禁止連続の崩し範囲", "仕上げ最適化",
        "結合探索を粘り強く", "一括見直しの自動調整", "公平化/適切回数の他ソフト許容(6%)", "回数連鎖研磨",
    };

    /// <summary>[3.694.0] 「設定の開始時」の本文（タグを除く）。計数を含まない ON/OFF だけ。先頭 2 つの「（設定）」は利用者の選択を指す。</summary>
    public static string StartLine(bool nativeOn, bool parityOn, bool filterC3nIncrease, bool wideC3nBreakDays, bool softPolishOn,
        bool combineExhaustPairs, bool lnsAdaptive, bool aptFairSoftTolerance, bool countChainPolish)
    {
        static string W(bool b) => b ? "ON" : "OFF";
        var w = ToggleWords;
        return $"{w[0]}（設定）={W(nativeOn)} / {w[1]}（設定）={W(parityOn)} / {w[2]}={W(filterC3nIncrease)} / {w[3]}={W(wideC3nBreakDays)} / {w[4]}={W(softPolishOn)}" +
            $" / {w[5]}={W(combineExhaustPairs)} / {w[6]}={W(lnsAdaptive)} / {w[7]}={W(aptFairSoftTolerance)} / {w[8]}={W(countChainPolish)}";
    }

    /// <summary>各トグルの ON/OFF と、その実行で観測できた効果を1行にまとめる。
    /// [3.587.0, Kotlin原本] 4トグルは呼び出し元が実行に使ったスナップショットをそのまま渡す
    /// （<see cref="PolishGate"/>直読みだと表示が食い違いうる）。</summary>
    public static string Summary(bool nativeOn, bool parityOn, bool softPolishOn,
        bool combineExhaustPairs, bool lnsAdaptive, bool aptFairSoftTolerance, bool countChainPolish)
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
        var w = ToggleWords;
        return w[0] + "=" + (nativeOn ? "ON" : "OFF") +
            " / " + w[1] + "=" + Eff(parityOn, parityChecks, "回") +
            " / " + w[2] + "=" + Eff(PolishGate.FilterC3nIncrease, c3nFilterSkipped, "件の必ず却下される検査を省略") +
            " / " + w[3] + "=" + wide +
            " / " + w[4] + "=" + Eff(softPolishOn, lahcEntered, "回LAHCへ切替") +
            " / " + w[5] + "=" + (combineExhaustPairs ? "ON" : "OFF") +
            " / " + w[6] + "=" + (lnsAdaptive ? "ON" : "OFF") +
            " / " + w[7] + "=" + Eff(aptFairSoftTolerance, Volatile.Read(ref _aptFairToleranceUsed), "回、却下されるはずの手を採用") +
            " / " + w[8] + "=" + Eff(countChainPolish, Volatile.Read(ref _countChainApplied), "連鎖を採用");
    }
}
