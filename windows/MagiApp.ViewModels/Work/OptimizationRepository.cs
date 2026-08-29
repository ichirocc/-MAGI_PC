using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels.Work;

/// <summary>
/// [フェーズ9] <c>MagiViewModel</c> と（将来 Phase 10 で実装する）バックグラウンド最適化タスクとの
/// プロセス内ブリッジ（<c>OptimizationRepository.kt</c> の逐語移植）。
///
/// Kotlin原本はAndroid/WorkManagerに一切依存しない純粋な <c>object</c>
/// （<c>MutableStateFlow</c>/<c>MutableSharedFlow</c> によるプロセス内 pub/sub）である——
/// WorkManagerに依存するのは*これを使って実際にバックグラウンドで最適化を走らせる*
/// <c>OptimizationWorker.kt</c> の側だけ。したがってこのクラス自体は Phase 10（背景実行）を
/// 待たず今のうちに完全移植できる（Phase 10 で作るのは、この Repository へ実際に
/// progress/result を publish する Windows 側のバックグラウンドタスク実装だけ）。
///
/// <c>StateFlow&lt;T&gt;</c>（最新値＋変更通知）を C# では「volatile バッキングフィールド越しの
/// 読み取り専用プロパティ＋変更イベント」の対で表現する。<c>SharedFlow(replay=8)</c> の
/// 「後から購読しても直近8件を受け取れる」というリプレイ機構は、現時点では実際に publish する
/// Phase 10 プロデューサが存在しないため意図的に単純化し、単なるイベント（購読中のリスナーにしか
/// 届かない）とする。Phase 10 で実際の「再起動直後に Worker が先に走る」シナリオが生じた時点で、
/// 必要なら小さなリングバッファを足す（ここに「なぜ簡略化したか」を残しておく＝HF77 の精神：
/// 実装していない振る舞いを実装済みであるかのように見せない）。
/// </summary>
public static class OptimizationRepository
{
    public const long ProgressPushMs = 200;

    public sealed record BgProgress(string Phase, int Hard, int Soft, int Total, long Iters, long ElapsedMs);

    /// <summary>
    /// 入力（背景タスクの起点となる盤面）。Kotlin の <c>Pair&lt;MagiState, Array&lt;IntArray&gt;&gt;?</c>
    /// を専用のレコード型に置き換えた（値型タプルは <c>volatile</c> の対象にならないため。参照型の
    /// レコードなら <c>volatile</c> なフィールドとして安全に公開できる）。
    /// </summary>
    public sealed record RequestPayload(MagiState State, int[][] Schedule);

    public sealed record BgResult(
        int[][] Schedule,
        ViolationReport Report,
        string Phase,
        /// <summary>
        /// [3.410.0/U-01 の由来をそのまま記録] これを計算した実行の ID
        /// （0=識別子を持たない旧経路／プロセス再起動後の復元）。
        /// 入力の指紋だけで受容を決めると、置き換えられた古い実行が完了間際に publish した結果を
        /// 「いま走らせている実行の答え」として受け取ってしまう——ファイル側の所有権はファイルを
        /// 守るだけで、このメモリ経由の公開は素通りするため、識別子をここに載せて塞ぐ。
        /// </summary>
        long RunId = 0L);

    public static volatile RequestPayload? Request;
    public static volatile int Seconds = 60;
    public static volatile int Workers = 4;

    private static volatile bool _running;
    public static bool Running => _running;
    public static event Action<bool>? RunningChanged;

    private static volatile BgProgress? _progress;
    public static BgProgress? Progress => _progress;
    public static event Action<BgProgress>? ProgressPublished;

    private static volatile BgResult? _result;
    public static BgResult? Result => _result;
    public static event Action<BgResult?>? ResultPublished;

    /// <summary>
    /// [3.385.0/3.388.0 の由来をそのまま記録] Worker が黙って落とした耐久保証の失敗を、書き出せる
    /// 操作ログへ届けるための唯一の経路。<paramref name="level"/> は「I」（正常な完了・進捗）と
    /// 「W」（失敗・警告）を区別する——このアプリの診断ログは「まず [W] を拾う」読み方が定着している
    /// ため、正常系まで警告として流すとその読み方が壊れる。
    /// </summary>
    public static event Action<string, string>? NotePublished;

    public static void PublishNote(string level, string msg) => NotePublished?.Invoke(level, msg);

    public static void SetRunning(bool v)
    {
        _running = v;
        RunningChanged?.Invoke(v);
    }

    public static void PublishProgress(BgProgress p)
    {
        _progress = p;
        ProgressPublished?.Invoke(p);
    }

    public static void PublishResult(BgResult? r)
    {
        _result = r;
        ResultPublished?.Invoke(r);
    }

    public static void Clear()
    {
        _progress = null;
        _result = null;
    }
}
