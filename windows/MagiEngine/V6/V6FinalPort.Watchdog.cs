namespace MagiEngine.V6;

public static partial class V6FinalPort
{
    /// <summary>
    /// [3.230.0/停滞ウォッチドッグの分離] 「フェーズ公平猶予」と「真の頭打ち検知」を分離した判定を
    /// 純関数として抽出（壁時計に依存する周囲のコードから切り離してユニットテスト可能にする）。
    /// 旧実装は <c>max(lastBestImproveMs, lastPhaseChangeMs)</c> を単一のstallMs(=予算9/10、300s予算で
    /// 270s)と比較しており、20〜90秒間隔で頻発するフェーズ遷移のたびにタイマがリセットされ続け、270秒
    /// という長い閾値には実質的に一度も到達し得なかった（改善が本当に無くても検知できない）。
    /// 本関数は two-condition AND: ①現フェーズ自身が phaseGraceMs 以上経過（起動直後の誤検知防止のみ）
    /// ②最終改善から effStall 以上経過（フェーズ遷移でリセットしない＝真の頭打ち）。
    ///
    /// [3.408.0/実機ログで確定・ユーザー指示「フェーズ名を停滞判定に使うべきではない」]
    /// ①のフェーズ猶予が<b>並列ワーカーによって恒久的な拒否権になっていた</b>。適応ポートフォリオの
    /// 8ワーカーは1本のフェーズ文字列を共有するため <c>lastPhaseChangeMs</c> が絶えず更新され、①が
    /// ほぼ真にならない。実機ログ(2026-08-19)は
    /// 「停滞274s・実効閾値37s・発火=なし・未発火の理由=現フェーズ猶予未達(実測0s/7s)」＝
    /// <b>275秒まるごと無改善なのに一度も発火しない</b>という形でこれを記録している。
    /// フェーズ猶予は「始まったばかりのフェーズを即殺しない」ための<b>遅延</b>であって、
    /// 頭打ちの検知そのものを止めてよい根拠は無い。よって①を<b>遅延に降格</b>し、
    /// 無改善が閾値の <see cref="StallOverrideFactor"/> 倍に達したらフェーズ猶予に関わらず発火する。
    ///
    /// 代償は測ってある: 3.341.1 の実測で早期終了を<b>外す</b>と weighted 中央 −3.5%（p≈0.075＝有意でない）
    /// ＝発火を早めるとごく僅かに品質を落とし、時間と電池を大きく節約する。倍率2は
    /// 「本当に詰まっている run は閾値の2倍まで待つ」保守側の設定。
    /// </summary>
    internal const int StallOverrideFactor = 2;

    /// <summary>[backlog#35] 残りHARDが「解けないと証明済み」か＝covU は床以下で、非covU は c3n だけかつ c3n 壁（<paramref name="c3nWall"/>）。</summary>
    /// <remarks>[E0] <paramref name="wishReached"/>（<see cref="WishFloorReached"/>）も解けない残りと数え、c3w は希望どうしの衝突で証明された件数（<paramref name="c3wProven"/>）まで c3n と同列。既定は従来と同一。</remarks>
    internal static bool IsStructuralHardResidual(ViolationReport report, int hardFloor, Func<bool> c3nWall, bool wishReached = false, int c3wProven = 0)
    {
        if (report.Hard <= 0) return false;
        if (wishReached) return true;
        var covU = report.Breakdown.GetValueOrDefault("covU", 0);
        if (covU > hardFloor) return false;
        var nonCovU = report.Hard - covU;
        var c3w = report.Breakdown.GetValueOrDefault("c3w", 0);
        var c3wOk = c3w <= c3wProven ? c3w : 0;
        return nonCovU == 0 || (nonCovU == report.Breakdown.GetValueOrDefault("c3n", 0) + c3wOk && c3nWall());
    }

    /// <summary>Faithful port of Kotlin's <c>internal fun watchdogStagnationFired(...)</c>. See <see cref="StallOverrideFactor"/> for the design rationale.</summary>
    internal static bool WatchdogStagnationFired(
        long now, long startMs, long minRunMs,
        long lastPhaseChangeMs, long phaseGraceMs,
        long lastBestImproveMs, long effStall)
    {
        if (now - startMs <= minRunMs) return false;
        var stalled = now - lastBestImproveMs;
        if (stalled <= effStall) return false;
        // フェーズ猶予は遅延であって拒否権ではない（並列ワーカーのフェーズ更新で永久に塞がれない）。
        return now - lastPhaseChangeMs > phaseGraceMs || stalled > effStall * StallOverrideFactor;
    }

    /// <summary>
    /// [3.281.0/停滞レビューA] ウォッチドッグの実効停滞閾値の選択を純関数として抽出（ユニットテスト用）。
    /// 従来: 「bestHard&lt;=hardFloor(構造的covU床) かつ 非covU HARD=0」のときだけ短い stallHardMs＝
    /// c3n が1件でも残ると常に stallMs(=予算9/10)で、300s予算では発火に270s必要＝<b>構造的に発火不能</b>
    /// だった（実機ログ: 125s以降150s無改善のまま探索275sを完走・追加精製0）。covU には
    /// structuralHardFloor という「解けないHARD」の静的判定があるのに c3n には無い非対称が根本原因。
    /// 新規: 残る非covU HARD が <b>c3n のみ</b>で、かつ 3.280.0 ForbiddenDiag が全 run の塞がりを
    /// <b>証明</b>した（c3nWallProven）場合も plateau とみなし stallHardMs へ移行する。証明つきのため
    /// 誤発火なし・早期終了は時間/電池の節約のみで品質は keep-best が担保（退化不能）。
    /// </summary>
    /// <summary>[E0] 希望衝突の床に「到達」＝HARD がちょうど床 <paramref name="floor"/> で、残る HARD が全て希望由来（<paramref name="allWishOrigin"/>＝<c>V6SanityPort.HardAllWishOrigin</c>）。
    /// 床を超えていれば未到達（検査もしない）。構造的 covU の床とは混ぜない。</summary>
    internal static bool WishFloorReached(int hard, int floor, Func<bool> allWishOrigin) =>
        floor > 0 && hard == floor && allWishOrigin();

    /// <summary>ログ表記は Kotlin の enum 名（OFF/E0A/E0B）に揃える。</summary>
    internal static string WishFloorModeName(WishFloorMode m) => m switch { WishFloorMode.E0A => "E0A", WishFloorMode.E0B => "E0B", _ => "OFF" };

    internal static long EffectiveStallMs(
        int bestHard, int hardFloor, int nonCovUHard, bool nonCovUAllC3n,
        bool c3nWallProven, long stallHardMs, long stallMs, bool wishReached = false)
    {
        var basePlateau = (bestHard <= hardFloor && nonCovUHard == 0) || wishReached;
        var c3nWallPlateau = nonCovUHard > 0 && nonCovUAllC3n
            && bestHard <= hardFloor + nonCovUHard && c3nWallProven;
        return basePlateau || c3nWallPlateau ? stallHardMs : stallMs;
    }

    /// <summary>
    /// [3.422.0/Part B・3.424.0で基準を是正] 「通常」分岐（HARD がまだ構造床に届いていない＝解ける
    /// 可能性がある局面）の停滞閾値 <c>stallMs</c> の算出（純関数＝<see cref="EffectiveStallMs"/>/
    /// <see cref="WatchdogStagnationFired"/> と同じ理由でユニットテスト可能にする）。
    ///
    /// 意味論: <b>予算×割合</b>（旧来の <c>budgetMs*9/10</c> と既定で厳密に同一）。ただしその値が
    /// 探索区間(<paramref name="searchWindowMs"/>) 内で一度も発火し得ない帯（後処理予約の下限クランプが
    /// 探索区間を大きく削る中程度の予算＝実測60秒帯。判定は <c>raw &gt;= searchWindowMs</c>＝
    /// <c>stalled &gt; effStall</c> が探索終了まで真になれない）だけ、<b>探索区間×割合</b>へ
    /// フォールバックする。
    ///
    /// [3.424.0/code-review指摘の是正] 3.422.0 の初版は無条件に <c>searchWindowMs*fraction</c> として
    /// おり、到達可能だった帯まで無計測で厳格化していた（300s予算: 270s→247.5s＝−8.3%）。計測が支持
    /// しない既定変更はしない（2.55.0/3.310.1/3.341.1）ため、予算基準を復元し到達不能帯だけを直す形へ。
    /// 60秒帯（3.423.0 の A/B で測った帯）はフォールバック側＝挙動不変。
    ///
    /// <paramref name="fraction"/> は既定で <see cref="PolishGate.NormalStallFraction"/> を読む
    /// （<c>fraction ?? PolishGate.NormalStallFraction</c>＝Kotlinのデフォルト引数「呼び出し時評価」を
    /// 表す、<c>V6HotfixPasses.AdaptiveBlockSwap.cs</c> の <c>filterC3nIncrease</c> と同じ配線パターン。
    /// C#は非定数式を引数の既定値にできないため <c>double?</c> + null合体演算子で表す）。
    /// <b>(0,1) 排他・有限のみ受け付ける</b>: fraction&gt;=1.0 は「閾値&gt;=探索区間」＝Part A が直した
    /// 到達不能バグの再現、NaN は「20秒床への暗黙の崩落」＝最凶の早期終了へ静かに化けるため、丸めず
    /// 落とす（<see cref="GlsPenalty.Decay"/> の <c>ArgumentException</c> ガードと同じ型）。
    /// </summary>
    internal static long NormalStallMs(long budgetMs, long searchWindowMs, double? fraction = null)
    {
        var f = fraction ?? PolishGate.NormalStallFraction;
        if (!(double.IsFinite(f) && f > 0.0 && f < 1.0))
            throw new ArgumentException($"normalStallFraction は (0,1) の有限値のみ: {f}");
        var raw = Math.Max((long)(budgetMs * f), 20_000L);
        if (raw < searchWindowMs) return raw;
        return Math.Max((long)(searchWindowMs * f), 20_000L);
    }

    /// <summary>[Kotlin原本 <c>V6FinalPort.WatchdogBudget</c>] 停滞ウォッチドッグの時間の切り方（Android <c>docs/stall_escape.md</c> §5.1 の表）。
    /// 純関数＝<c>StallEscapeSpecTest</c> が表の値を固定する。</summary>
    internal sealed record WatchdogBudget(
        long MinRunMs, long PostReserveMs, long SearchDeadlineMs, long SearchWindowMs,
        long StallMs, long StallHardMs, long PhaseGraceMs);

    internal static WatchdogBudget WatchdogBudgetOf(long budgetMs, long startMs, long hardDeadlineMs, double? fraction = null)
    {
        var minRunMs = Math.Min(Math.Clamp(budgetMs / 6, 8_000L, 45_000L), budgetMs);
        var postReserveMs = Math.Min(Math.Clamp(budgetMs / 12, 8_000L, 25_000L), budgetMs / 2);
        var searchDeadlineMs = Math.Max(hardDeadlineMs - postReserveMs, startMs + minRunMs);
        var searchWindowMs = searchDeadlineMs - startMs;
        return new WatchdogBudget(
            MinRunMs: minRunMs, PostReserveMs: postReserveMs, SearchDeadlineMs: searchDeadlineMs, SearchWindowMs: searchWindowMs,
            StallMs: NormalStallMs(budgetMs, searchWindowMs, fraction),
            StallHardMs: Math.Max(budgetMs / 8, 15_000L),
            PhaseGraceMs: Math.Clamp(budgetMs / 40, 2_000L, 15_000L));
    }

    /// <summary>[Kotlin原本 <c>V6FinalPort.progressImproved</c>] 進捗監視の「改善」判定（§3.2）。採否の <c>BetterReport</c> とは別契約＝weightedScore にだけ 1e-6 の許容差。</summary>
    internal static bool ProgressImproved(int h, double wgt, int t, int bh, double bWeighted, int bTotal) =>
        h < bh || (h == bh && wgt < bWeighted - 1e-6) || (h == bh && wgt <= bWeighted + 1e-6 && t < bTotal);

    /// <summary>[Kotlin原本 <c>V6FinalPort.c3nWallSameReport</c>] 生存盤面の報告が最良の報告と同じ参照か（§5.3）。
    /// publishLiveBest と進捗報告は同じ報告参照を渡すので、参照が同じなら「同じ採用手が出した同じ盤面」。
    /// 値が等しいだけの別の報告は別の盤面かもしれないので使わない。</summary>
    internal static bool C3nWallSameReport(ViolationReport? live, ViolationReport? best) =>
        live is not null && best is not null && ReferenceEquals(live, best);

    /// <summary>[Kotlin原本 <c>V6FinalPort.WatchdogBest</c>] 層 A の最良追跡と停滞ラッチ（§5.2）。並列ワーカーから読むため Volatile、
    /// 更新は呼び出し側の progressLock 内。</summary>
    internal sealed class WatchdogBest
    {
        private int _bestHard = int.MaxValue;
        private long _lastBestImproveMs, _lastBestImproveIters, _lastBeatInputMs = -1, _stagnationDurationMs = -1, _stagnationIters = -1;
        private int _bestNonCovUHard = int.MaxValue, _bestVersion;
        private bool _bestNonCovUAllC3n, _stagnationFired, _stagnationByOverride, _stagnationWall;
        private ViolationReport? _bestReport;
        internal int BTotal = int.MaxValue;
        internal double BWeighted = double.MaxValue;

        internal WatchdogBest(long startMs) { _lastBestImproveMs = startMs; }

        internal int BestHard => Volatile.Read(ref _bestHard);
        internal long LastBestImproveMs => Volatile.Read(ref _lastBestImproveMs);
        internal long LastBestImproveIters => Volatile.Read(ref _lastBestImproveIters);
        internal long LastBeatInputMs => Volatile.Read(ref _lastBeatInputMs);
        internal int BestNonCovUHard => Volatile.Read(ref _bestNonCovUHard);
        internal bool BestNonCovUAllC3n => Volatile.Read(ref _bestNonCovUAllC3n);
        internal int BestVersion => Volatile.Read(ref _bestVersion);
        internal ViolationReport? BestReport => Volatile.Read(ref _bestReport);
        internal bool StagnationFired => Volatile.Read(ref _stagnationFired);
        internal long StagnationDurationMs => Volatile.Read(ref _stagnationDurationMs);
        internal long StagnationIters => Volatile.Read(ref _stagnationIters);
        internal bool StagnationByOverride => Volatile.Read(ref _stagnationByOverride);
        internal bool StagnationWall => Volatile.Read(ref _stagnationWall);

        /// <summary>改善報告。<see cref="ProgressImproved"/> で改善なら最良・時刻・反復数・非 covU 内訳・世代を更新し、停滞ラッチを降ろす（3.346.0）。</summary>
        internal bool Observe(ViolationReport report, long nowMs, long observedIters, Func<bool> beatsInput, int wishC3wProven)
        {
            if (!ProgressImproved(report.Hard, report.WeightedScore, report.Total, BestHard, BWeighted, BTotal)) return false;
            Volatile.Write(ref _bestHard, report.Hard); BTotal = report.Total; BWeighted = report.WeightedScore;
            Volatile.Write(ref _bestReport, report);
            Volatile.Write(ref _lastBestImproveMs, nowMs); Volatile.Write(ref _lastBestImproveIters, observedIters);
            if (beatsInput()) Volatile.Write(ref _lastBeatInputMs, nowMs);
            Volatile.Write(ref _stagnationFired, false); Volatile.Write(ref _stagnationDurationMs, -1L);
            Volatile.Write(ref _stagnationIters, -1L); Volatile.Write(ref _stagnationByOverride, false);
            Volatile.Write(ref _stagnationWall, false);
            var gv = report.Breakdown.GetValueOrDefault("groupViol", 0);
            var pf = report.Breakdown.GetValueOrDefault("pref", 0);
            var c3n = report.Breakdown.GetValueOrDefault("c3n", 0);
            var c3w = report.Breakdown.GetValueOrDefault("c3w", 0);
            Volatile.Write(ref _bestNonCovUHard, gv + pf + c3n + c3w);
            Volatile.Write(ref _bestNonCovUAllC3n, gv == 0 && pf == 0 && c3w <= wishC3wProven && c3n > 0);
            Interlocked.Increment(ref _bestVersion);
            return true;
        }

        /// <summary>停滞発火。<paramref name="byOverride"/>＝フェーズ猶予の中で閾値の 2 倍に達して発火した（§5.4）。<paramref name="wall"/>＝c3n 壁の短縮を使った。</summary>
        internal void Fire(long nowMs, long observedIters, bool byOverride, bool wall = false)
        {
            Volatile.Write(ref _stagnationDurationMs, nowMs - LastBestImproveMs);
            Volatile.Write(ref _stagnationIters, observedIters);
            Volatile.Write(ref _stagnationByOverride, byOverride);
            Volatile.Write(ref _stagnationWall, wall);
            Volatile.Write(ref _stagnationFired, true);
        }

        /// <summary>判定時の世代 <paramref name="expectedVersion"/> のままだった場合だけ発火を確定する（§5.4）。判定後に改善が届いていれば捨てる。
        /// 呼び出し側は progressLock 内で呼ぶ（改善の <see cref="Observe"/> と排他にする）。</summary>
        internal bool FireIfGeneration(int expectedVersion, long nowMs, long observedIters, bool byOverride, bool wall)
        {
            if (BestVersion != expectedVersion) return false;
            // 一つのラッチは一度だけ確定する（判定が真のままの後続呼び出しで、時刻と壁の記録を上書きしない）。
            if (!StagnationFired) Fire(nowMs, observedIters, byOverride, wall);
            return true;
        }
    }

    /// <summary>盤面の内容（完全一致）で鍵をとる 1 件キャッシュ。返す値は必ずその盤面を診断した結果（別盤面の結果は返さない）。
    /// [Kotlin原本 <c>V6FinalPort.BoardKeyedFlag</c>]</summary>
    internal sealed class BoardKeyedFlag
    {
        private sealed record Entry(IReadOnlyList<IReadOnlyList<int>> Board, bool Value);

        private Entry? _entry;

        internal bool Get(IReadOnlyList<IReadOnlyList<int>> board, Func<IReadOnlyList<IReadOnlyList<int>>, bool> eval)
        {
            var e = Volatile.Read(ref _entry);
            if (e is not null && (ReferenceEquals(e.Board, board) || SameBoard(e.Board, board))) return e.Value;
            var v = eval(board);
            Volatile.Write(ref _entry, new Entry(board, v));
            return v;
        }

        private static bool SameBoard(IReadOnlyList<IReadOnlyList<int>> a, IReadOnlyList<IReadOnlyList<int>> b)
        {
            if (a.Count != b.Count) return false;
            for (var r = 0; r < a.Count; r++)
                if (!a[r].SequenceEqual(b[r])) return false;
            return true;
        }
    }

    /// <summary>
    /// c3n 壁の証明（<c>docs/stall_escape.md</c> §5.3・[Kotlin原本 <c>V6FinalPort.C3nWallProof</c>]）。依存を注入し、段の境界・診断中の入れ替わりを単体で検査できる形にする。
    /// <see cref="Bound"/>（既定）: 生存盤面の報告が最良の報告と同じ参照のときだけ診断を使う。対応が取れない間は、同じ最良版で
    /// 対応が取れていた判定だけを持ち越す（段の境界で生存盤面が空になっても判定を失わないため）。
    /// <see cref="Legacy"/>（測定の基準腕）: HEAD と同じ。版ごとに一度、生存盤面を一致の検査なしに診断する。
    /// </summary>
    internal sealed class C3nWallProof
    {
        private sealed record Verdict(int Version, bool Proven);

        private readonly Func<int> _bestVersion;
        private readonly Func<ViolationReport?> _bestReport;
        private readonly Func<V6NativeOptimizer.LiveBestSnapshot?> _liveSnapshot;
        private readonly Func<IReadOnlyList<IReadOnlyList<int>>, bool> _diagnose;
        private readonly BoardKeyedFlag _cache = new();
        private Verdict? _carried;
        private Verdict? _legacyCache;
        private object? _lastCounted;
        private int _checks, _mismatch;

        internal C3nWallProof(
            Func<int> bestVersion,
            Func<ViolationReport?> bestReport,
            Func<V6NativeOptimizer.LiveBestSnapshot?> liveSnapshot,
            Func<IReadOnlyList<IReadOnlyList<int>>, bool> diagnose)
        {
            _bestVersion = bestVersion;
            _bestReport = bestReport;
            _liveSnapshot = liveSnapshot;
            _diagnose = diagnose;
        }

        /// <summary>生存盤面の更新ごとの確認回数と、その報告が最良の報告と対応しなかった回数（ログ用。呼び出しごとには数えない）。</summary>
        internal int Checks => Volatile.Read(ref _checks);
        internal int Mismatch => Volatile.Read(ref _mismatch);

        internal bool Bound()
        {
            var v = _bestVersion();
            var bestRep = _bestReport();
            var snap = _liveSnapshot();
            if (snap is not null && bestRep is not null)
            {
                if (!ReferenceEquals(Interlocked.Exchange(ref _lastCounted, snap), snap))
                {
                    Interlocked.Increment(ref _checks);
                    if (!C3nWallSameReport(snap.Report, bestRep)) Interlocked.Increment(ref _mismatch);
                }
                if (C3nWallSameReport(snap.Report, bestRep))
                {
                    var proven = _cache.Get(snap.Board, _diagnose);
                    // 診断の間に生存盤面・最良・版のどれかが入れ替わったら、この判定は使わない（次の呼出で判定し直す）。
                    if (!ReferenceEquals(_liveSnapshot(), snap) || !ReferenceEquals(_bestReport(), bestRep) || _bestVersion() != v)
                        return false;
                    Volatile.Write(ref _carried, new Verdict(v, proven));
                    return proven;
                }
            }
            var carried = Volatile.Read(ref _carried);
            return carried is not null && carried.Version == v && carried.Proven;
        }

        internal bool Legacy()
        {
            var v = _bestVersion();
            if (Volatile.Read(ref _legacyCache)?.Version != v)
            {
                var board = _liveSnapshot()?.Board;
                Volatile.Write(ref _legacyCache, new Verdict(v, board is not null && _diagnose(board)));
            }
            return Volatile.Read(ref _legacyCache)?.Proven ?? false;
        }

        /// <summary>最終盤面の診断（残存分析の注記用）。盤面の内容で鍵をとる。</summary>
        internal bool DiagnoseBoard(IReadOnlyList<IReadOnlyList<int>> board) => _cache.Get(board, _diagnose);
    }
}
