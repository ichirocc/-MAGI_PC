using System.Threading;

namespace MagiEngine.V6;

/// <summary>
/// Faithful port of the run-scoped/legacy-static infrastructure declared inside Kotlin's
/// <c>V6NativeOptimizer</c> object (<c>RunSlot</c>/<c>RunSlotElement</c>/<c>runSlot()</c>/
/// <c>ownsStatics()</c>, the legacy static mirrors, <c>compressFocusTrail</c>, and the
/// <c>liveBest</c>/<c>publishLiveBest</c> CAS machinery). This is phase-5c scope because
/// <c>RunMultiWorker</c> reads/writes several of these members directly.
///
/// [C#移植上の判断] Kotlin の <c>RunSlotElement</c>（<c>CoroutineContext.Element</c>）は、C# では
/// ラッパー型を作らず <see cref="AsyncLocal{T}"/> を直接使う。<c>AsyncLocal&lt;T&gt;.Value</c> への
/// 代入は、その代入を行った呼び出しとその子（await 先）の実行コンテキストにのみ伝播し、その呼び出しが
/// 戻ったあと呼び出し元のコンテキストには伝播しない（ExecutionContext の capture/restore が
/// <c>await</c> 境界ごとに起きるため）— これは Kotlin の <c>withContext(element) { ... }</c> と
/// 伝播範囲が一致する。よってラッパー型を介さず <c>CurrentRunSlot.Value = slot;</c> を実行の入口
/// （phase 5d の <c>Optimize</c>）で設定するだけで同じスコープ規則が成立する。
/// </summary>
public static partial class V6NativeOptimizer
{
    private static long NowMs() => System.Diagnostics.Stopwatch.GetTimestamp() * 1000L / System.Diagnostics.Stopwatch.Frequency;

    private static long ActualSeed(long seed) => seed == 0L ? DateTime.UtcNow.Ticks : seed;

    // ───────── [3.335.0/外部レビュー P1, Kotlin原本] 実行ごとの成果物入れ ─────────
    //   `lastAlternatives` などの可変 static は「直近の実行の値」しか持てず、実行が重なると
    //   （WorkManager の REPLACE で旧 Worker が協調キャンセルを待つ間・kill 後の再スケジュール）
    //   入口の初期化が相手の値を消し、書き込みも読み出しも混ざり得た。実行ごとに RunSlot を作り、
    //   AsyncLocal で呼び出し木の隅々まで運ぶ。static は「いちばん新しい実行のライブ表示」用として
    //   残す（新しい方が勝つのが正しい面）。
    //
    //   [5d/AdaptiveEliteArchive導入後に追加] Kotlin の RunSlot は alternatives/fusionElites/
    //   infeasible の3フィールドを持つ。fusionElites（AdaptiveElite 型、runAdaptivePortfolio専用の
    //   全epoch横断エリート統合結果）は AdaptiveEliteArchive.cs でこの型を導入した後の今、ここへ追加する
    //   （phase 5c 時点では対応する読み書き元＝optimize()/runAdaptivePortfolio/EliteIntegrationPolish が
    //   まだ移植されていなかったため意図的に省略していた）。
    public sealed class RunSlot
    {
        public long Id { get; }
        public RunSlot(long id) { Id = id; }

        private volatile IReadOnlyList<int[][]> _alternatives = Array.Empty<int[][]>();
        public IReadOnlyList<int[][]> Alternatives { get => _alternatives; set => _alternatives = value; }

        // [3.268.0/elite archive fusion, Kotlin原本] 全epochから圧縮した品質・距離・橋渡しエリート
        //   （最適化後の再結合/Fusion専用、PORTFOLIO実行時のみ非空）。
        private volatile IReadOnlyList<AdaptiveElite> _fusionElites = Array.Empty<AdaptiveElite>();
        public IReadOnlyList<AdaptiveElite> FusionElites { get => _fusionElites; set => _fusionElites = value; }

        private readonly object _lock = new();
        private volatile IReadOnlySet<string> _infeasible = new HashSet<string>();
        public IReadOnlySet<string> Infeasible => _infeasible;

        public void AddInfeasible(IReadOnlyCollection<string> fams)
        {
            if (fams.Count == 0) return;
            lock (_lock)
            {
                var next = new HashSet<string>(_infeasible);
                next.UnionWith(fams);
                _infeasible = next;
            }
        }
    }

    private static readonly AsyncLocal<RunSlot?> CurrentRunSlot = new();

    /// <summary>
    /// いちばん新しい <c>Optimize()</c> の実行 id（phase 5d の <c>Optimize</c> が設定する）。static
    /// への書き込みはこれと一致するときだけ行う。<c>long</c> は C# では <c>volatile</c> 修飾できない
    /// （原子性が全プラットフォームで保証されるのは参照型・<c>int</c> など一部の型のみ）ため、
    /// <see cref="_liveBestContention"/> と同じく <see cref="Interlocked"/> 経由でのみ読み書きする。
    /// </summary>
    private static long _newestRunId = 0L;

    private static RunSlot? GetRunSlot() => CurrentRunSlot.Value;

    /// <summary>static（＝新しい実行が勝つライブ表示側）へ書いてよいか。スロット無し＝直接呼び出しは従来どおり許す。</summary>
    private static bool OwnsStatics(RunSlot? slot) =>
        slot is null || slot.Id == Interlocked.Read(ref _newestRunId);

    /// <summary>
    /// 直近の並列探索で得た「他の案」（採用案以外の候補スケジュール、品質順・最大3件）。
    /// [3.335.0, Kotlin原本] **これは「いちばん新しい実行」の値**。呼び出し側は
    /// <see cref="V6OptimizerResult.Alternatives"/>（実行ごとの値）を読むこと。ここは表示・互換の
    /// ために残している。
    /// </summary>
    private static volatile IReadOnlyList<int[][]> _lastAlternatives = Array.Empty<int[][]>();
    public static IReadOnlyList<int[][]> LastAlternatives => _lastAlternatives;

    /// <summary>
    /// [3.268.0/elite archive fusion, Kotlin原本] 全epochから圧縮した品質・距離・橋渡しエリート
    /// （最適化後の再結合/Fusion専用、PORTFOLIO実行時のみ非空）。<see cref="_lastAlternatives"/>と同じ
    /// 「いちばん新しい実行」の値＝ライブ表示用の legacy static mirror。
    /// </summary>
    private static volatile IReadOnlyList<AdaptiveElite> _lastFusionElites = Array.Empty<AdaptiveElite>();
    public static IReadOnlyList<AdaptiveElite> LastFusionElites => _lastFusionElites;

    // [3.288.0/ログ強化=状態軸, Kotlin原本] この Optimize() 実行中に HF63 が「構造的に充足困難」と
    //   学習した族の集合（全 RunRsi 呼出＝直接RSI/RSI++/適応ポートフォリオの各ワーカーからの union）。
    //   エピローグの「残存分析」行が読む。Optimize() 入口でクリアする。並行ワーカーからの union
    //   更新のため lock で保護。
    private static volatile IReadOnlySet<string> _lastInfeasibleFamilies = new HashSet<string>();
    public static IReadOnlySet<string> LastInfeasibleFamilies => _lastInfeasibleFamilies;
    private static readonly object InfeasLock = new();

    public static void RecordInfeasible(IReadOnlyCollection<string> fams)
    {
        if (fams.Count == 0) return;
        lock (InfeasLock)
        {
            var next = new HashSet<string>(_lastInfeasibleFamilies);
            next.UnionWith(fams);
            _lastInfeasibleFamilies = next;
        }
    }

    /// <summary>[3.335.0, Kotlin原本] この実行のスロットへ記録し、いちばん新しい実行のときだけ static も更新する。</summary>
    private static void RecordInfeasibleScoped(IReadOnlyCollection<string> fams)
    {
        var slot = GetRunSlot();
        slot?.AddInfeasible(fams);
        if (OwnsStatics(slot)) RecordInfeasible(fams);
    }

    public static void ClearInfeasible()
    {
        lock (InfeasLock) { _lastInfeasibleFamilies = new HashSet<string>(); }
    }

    /// <summary>[3.288.0/ログ強化=回数軸, Kotlin原本] focus 足跡の連続圧縮（"c3n,c3n,c1" → "c3n×2→c1"）。マーカー([..])はそのまま挟む。</summary>
    public static string CompressFocusTrail(IReadOnlyList<string> trail)
    {
        var outSb = new System.Text.StringBuilder();
        int i = 0;
        while (i < trail.Count)
        {
            var t = trail[i];
            if (t.StartsWith('['))
            {
                if (outSb.Length > 0) outSb.Append('→');
                outSb.Append(t);
                i++;
                continue;
            }
            int j = i + 1;
            while (j < trail.Count && trail[j] == t) j++;
            if (outSb.Length > 0) outSb.Append('→');
            outSb.Append(t);
            if (j - i > 1) outSb.Append('×').Append(j - i);
            i = j;
        }
        return outSb.ToString();
    }

    /// <summary>
    /// [DefragLiveView移植, Kotlin原本] 実行中の最良盤面スナップショット（計算中ライブ表示用・読取専用）。
    /// 進捗の節目で更新。<see cref="PublishLiveBest"/> で CAS 管理する真のグローバル最良のときだけ更新する。
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<int>>? LiveBest => _liveBestRef.Value?.Board;

    /// <summary>
    /// [3.385.0, Kotlin原本] 評価と盤面を1つの不変オブジェクトにして1回の CAS で publish する
    /// （report だけを CAS し盤面を別に代入する2段構成だと、途中でプリエンプトされた劣る書き込みが
    /// あとから勝る書き込みを上書きしうる — 詳細は Kotlin 原本のコメント参照）。
    /// </summary>
    private sealed class LiveBestSnapshot
    {
        public ViolationReport Report { get; }
        public IReadOnlyList<IReadOnlyList<int>> Board { get; }
        public LiveBestSnapshot(ViolationReport report, IReadOnlyList<IReadOnlyList<int>> board)
        {
            Report = report;
            Board = board;
        }
    }

    private static readonly ThreadLocalRef<LiveBestSnapshot?> _liveBestRef = new(null);
    private static long _liveBestContention = 0L;

    /// <summary>[敵対的レビュー修正, Kotlin原本] liveBest を真にグローバルな最良のときだけ更新する。呼出元のローカル best/report が既存の liveBest より劣る/同値なら何もしない＝退行を防ぐ。</summary>
    public static void PublishLiveBest(ViolationReport report, int[][] schedule)
    {
        // 盤面コピーは「勝ち目がある」と分かってから1回だけ。負ける呼出（多数）はコピーを払わない。
        LiveBestSnapshot? snap = null;
        while (true)
        {
            var cur = _liveBestRef.Value;
            if (cur is not null && !UnifiedViolationChecker.BetterReport(report, cur.Report)) return;
            snap ??= new LiveBestSnapshot(report, schedule.Select(row => (IReadOnlyList<int>)row.ToList()).ToList());
            if (_liveBestRef.CompareAndSet(cur, snap)) return;
            // [3.387.0, Kotlin原本] ここへ来る＝別スレッドが同時に publish していた。競合カウンタは診断専用。
            Interlocked.Increment(ref _liveBestContention);
        }
    }

    /// <summary>[3.387.0, Kotlin原本] PublishLiveBest の CAS が競合で再試行した回数（この実行ぶん・診断表示のみ）。</summary>
    public static int LiveBestContentionCount() => (int)Interlocked.Read(ref _liveBestContention);

    /// <summary>テスト専用（Optimize() を回さずに publish の不変条件だけを検査するため）。</summary>
    public static void ResetLiveBestForTest()
    {
        _liveBestRef.Value = null;
        Interlocked.Exchange(ref _liveBestContention, 0L);
    }

    /// <summary>
    /// Minimal CAS-capable reference cell — C# has no built-in <c>AtomicReference&lt;T&gt;</c>
    /// (unlike Java/Kotlin's <c>java.util.concurrent.atomic.AtomicReference</c>, used pervasively
    /// throughout the Kotlin source); <see cref="Interlocked.CompareExchange{T}(ref T, T, T)"/>
    /// operates on a plain field via <c>ref</c>, so this thin wrapper exposes that as a
    /// <c>.Value</c>/<c>CompareAndSet</c> surface mirroring the Kotlin call sites exactly
    /// (<c>ref.get()</c>/<c>ref.compareAndSet(cur, next)</c>).
    /// </summary>
    private sealed class ThreadLocalRef<T> where T : class?
    {
        private T _value;
        public ThreadLocalRef(T initial) { _value = initial; }
        public T Value { get => Volatile.Read(ref _value!)!; set => Volatile.Write(ref _value!, value!); }
        public bool CompareAndSet(T comparand, T value) =>
            ReferenceEquals(Interlocked.CompareExchange(ref _value, value, comparand), comparand);
    }
}
