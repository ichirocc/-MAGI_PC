using System.Threading;
using System.Threading.Tasks;
using MagiEngine;
using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>
/// [フェーズ9 ピース6] <c>MagiViewModel.kt</c> の診断/レポート集約パイプライン
/// （<c>Analysis</c>/<c>analyzeParallel</c>/<c>pushReport</c>/<c>makeUi</c>、およびそれらが依存する
/// <c>boardKey</c>/<c>stateKey</c>/<c>setPolishDiagnostics</c>/<c>compressDiagLogs</c> と付随キャッシュ
/// フィールド一式）の移植。
///
/// [なぜピース5より先に必要だったか] このピースは元のKotlinソースではファイル末尾（3300行台）に
/// あるが、<c>loadAsync</c>（ピース5の直後に予定していた次の範囲）を含め、盤面/入力を丸ごと差し替える
/// ほぼ全てのメソッドが <c>pushReport(...)</c> を呼ぶ——<c>pushReport</c> が存在しない限り、それらの
/// メソッドは意味のある形で移植できない。そのため、テキスト上の出現順ではなく依存関係を優先し、
/// このピースを <c>loadAsync</c> クラスタより先に移植する。
///
/// [Analysis を internal にした理由] Kotlin原本の <c>private data class Analysis</c> はテストが
/// 存在しないため一度も直接検証されていない。このC#移植では、重い並列解析
/// （<see cref="AnalyzeParallelAsync"/>）を経由せず <see cref="MakeUi"/> のフィールド写像そのものを
/// 直接検証できるようにするため、確立済みの規約（ピース5の各メンバ参照）に倣い internal とする。
/// </summary>
public sealed partial class MagiViewModel
{
    /// <summary>
    /// makeUi の重い解析4パスの出力を束ねる不変ホルダ。純関数の出力のみを保持するため、
    /// どのスレッドで生成しても安全（背景スレッドで作りメインへ受け渡せる）。
    /// </summary>
    internal sealed record Analysis(
        V6PortReport V6,
        V6SanityReport Sanity,
        CoverageDiagnosis? CoverageDiag,
        ForbiddenRunDiagnosis? ForbiddenDiag,
        IReadOnlyList<string> V6Logs,
        IReadOnlyList<string> RawDiagLogs);

    // ===== 診断ログ／研磨診断のキャッシュ（いずれも「直近1回ぶん」の観測。盤面から再計算できない） =====

    private IReadOnlyList<string> _rawDiagLogs = Array.Empty<string>();

    /// <summary>[3.408.0] <see cref="_rawDiagLogs"/> を作ったエンジン実行の通し番号。</summary>
    private int _lastDiagSerial;

    /// <summary>[3.408.0] <see cref="_lastRunDiagLogs"/> を作ったエンジン実行の通し番号。</summary>
    private int _lastRunDiagSerial;

    private IReadOnlyList<string> _lastRunDiagLogs = Array.Empty<string>();
    private string _lastRunDiagLabel = "";
    private long _lastRunDiagAtMs;

    /// <summary>
    /// [3.322.0] 直近の最適化で「窓の要件(c1)がなぜ直せなかったか」の構造化診断。
    /// 研磨が候補を作って却下した記録が唯一の根拠＝盤面から再計算できないため保持する
    /// （CoverageDiag/ForbiddenDiag が毎回作り直せるのとはここが違う）。
    /// </summary>
    private C1PlateauDiagnosis? _lastC1Plateau;

    /// <summary>
    /// [3.323.0] 直近の最適化で、厳密ピン(lo==hi)だけが止めた手の**計測できた**試行数。
    /// `isBetter` が採用を認めた手をピンのガードだけが却下した回数。ただし全件ではない
    /// （<see cref="PinBlockAttribution"/> の注記参照）。
    /// </summary>
    private int _lastObservedPinAttempts;

    /// <summary>[3.326.0] どのピン(職員,シフト)が何回止めたか。緩和対象の提示に使う。</summary>
    private PinBlockAttribution? _lastPinBlocks;

    /// <summary>
    /// [3.324.0/外部レビュー] 上の2つは「その盤面で研磨が却下した記録」であって盤面から再計算できない。
    /// よって**どの盤面に対する観測か**を指紋で持ち、盤面が変わったら自動的に黙る。
    /// 手編集・元に戻す・データ読込・CSV取込・初期解生成…と変更サイトごとにフックを足す方式は
    /// 必ずどこかを漏らすので、MakeUi 側で毎回突き合わせる自己無効化にする。
    /// </summary>
    private long _lastDiagBoardKey;

    /// <summary>[3.327.0→3.328.0] 診断を取ったときの入力の指紋。設定が編集されたら診断は失効する。</summary>
    private long _lastDiagStateKey;

    /// <summary>[テスト可視性のためinternal化] 盤面の内容から決まる指紋。S×T が小さい（30×31）ので毎回の計算コストは無視できる。</summary>
    internal static long BoardKey(int[][] schedule)
    {
        var h = 1125899906842597L;
        foreach (var row in schedule) foreach (var v in row) h = h * 31L + v;
        return h;
    }

    /// <summary>
    /// [3.328.0/外部レビュー → 3.330.0 で v6 へ移動] 勤務表の意味を決める入力すべての指紋。
    /// 実体は <see cref="StateFingerprint"/>（プラットフォーム非依存＝ホストでテストできる）。
    /// </summary>
    internal static long StateKey(MagiState st) => StateFingerprint.Of(st);

    /// <summary>
    /// [テスト可視性のためinternal化] 研磨診断を「この盤面のもの」として保存する。null/0 は診断なし。
    /// </summary>
    internal void SetPolishDiagnostics(
        C1PlateauDiagnosis? plateau,
        int observedPinBlockedAttempts,
        int[][] forSchedule,
        PinBlockAttribution? pinBlocks = null)
    {
        _lastC1Plateau = plateau;
        _lastObservedPinAttempts = observedPinBlockedAttempts;
        _lastPinBlocks = pinBlocks;
        var fresh = plateau is not null || observedPinBlockedAttempts > 0;
        _lastDiagBoardKey = fresh ? BoardKey(forSchedule) : 0L;
        _lastDiagStateKey = fresh ? (_state is not null ? StateKey(_state) : 0L) : 0L;
    }

    /// <summary>
    /// [テスト可視性のためinternal化] 診断ログのスパム抑制。RSI/ALNS の各ラウンド・各リスタート・
    /// EarlyChain などで同種の行が大量に出るため、(1) 連続する重複行を「×N」に畳み、(2) それでも
    /// 上限を超える場合は頭7割＋尾3割に圧縮する。全文が必要な場合は「ログ出力（テキスト/JSON）」で取得する想定。
    /// </summary>
    internal static List<string> CompressDiagLogs(IReadOnlyList<string> lines, int cap = 200)
    {
        if (lines.Count <= 1) return lines.ToList();
        var collapsed = new List<string>(lines.Count);
        var i = 0;
        while (i < lines.Count)
        {
            var j = i + 1;
            while (j < lines.Count && lines[j] == lines[i]) j++;
            var n = j - i;
            collapsed.Add(n > 1 ? $"{lines[i]}  ×{n}" : lines[i]);
            i = j;
        }
        if (collapsed.Count <= cap) return collapsed;
        var head = cap * 7 / 10;
        var tail = cap - head;
        var result = new List<string>(cap + 1);
        result.AddRange(collapsed.Take(head));
        result.Add($"… 中略 {collapsed.Count - head - tail} 行省略（全文は「ログ出力」で取得） …");
        result.AddRange(collapsed.Skip(collapsed.Count - tail));
        return result;
    }

    /// <summary>
    /// [テスト可視性のためinternal化] makeUi が必要とする4つの重い解析を並列実行する。
    /// 4パス（Analyze/Build/DiagnoseCoverage/BuildViolationDebug）は同じ不変入力にのみ依存し
    /// 相互参照しない純関数なので、Task.Run で別スレッドへ逃がして同時実行でき、壁時計時間が
    /// sum(パス)→max(パス) に短縮される（最重量は全制約走査の BuildViolationDebug）。
    /// インスタンス状態には一切触れないため static（Kotlin原本は private suspend fun だが this を
    /// 使わない＝<see cref="OpDays"/> と同じ「純粋なら static」の規約）。
    /// </summary>
    /// <param name="ct">
    /// [キャンセルの扱い] <c>MagiEngine</c> 側の4診断関数はいずれも <see cref="CancellationToken"/> を
    /// 取らない（Phase 2-7 の時点で純粋な同期関数として確定済み）ため、<c>Task.Run</c> に渡した
    /// <paramref name="ct"/> は「まだ開始していないタスクの開始を止める」効果のみ（開始済みタスクは
    /// 最後まで走る）——これは Kotlin のコルーチンが <c>ensureActive()</c> を呼ばない限りキャンセル後も
    /// 走り切るのと同じ性質。<see cref="PushReportAsync"/> の <c>nonCancellable</c> は
    /// <see cref="CancellationToken.None"/> を渡すことで Kotlin の <c>withContext(NonCancellable)</c> と
    /// 同じ効果（呼び出し元の ct を無視して必ず完走する）を得る。
    /// </param>
    internal static async Task<Analysis> AnalyzeParallelAsync(
        MagiState st, int[][] schedule, ViolationReport report, CancellationToken ct = default)
    {
        var v6Task = Task.Run(() => V6PortAnalyzer.Analyze(st, schedule, report), ct);
        var sanityTask = Task.Run(() => V6SanityPort.Build(st, schedule), ct);
        // 人員不足(covU)または人員過剰(covO)が残る場合のみ原因診断（どの日/シフトが「充足不可」か
        // 「未到達」か／過剰がなぜ動かせないか）を算出しログに残す。
        var coverageTask = Task.Run(() =>
        {
            var d = V6PortAnalyzer.DiagnoseCoverage(st, schedule, report);
            return d.HasShortage || d.HasSurplus ? d : null;
        }, ct);
        // [3.280.0] 禁止連続(c3n)が残る場合のみ「なぜ崩せないか」診断（CoverageDiag の c3n 版）。
        var forbiddenTask = Task.Run(() =>
        {
            if (report.Breakdown.GetValueOrDefault("c3n", 0) <= 0) return null;
            var d = V6PortAnalyzer.DiagnoseForbiddenRuns(st, schedule);
            return d.HasRuns ? d : null;
        }, ct);
        // [デバッグ] 制約違反を家族ごとに「場所＋実値(必要/現状, 回数/下限上限, 誰/何日/シフト)」で出力。
        var vioDebugTask = Task.Run(() => V6SanityPort.BuildViolationDebug(st, schedule, report), ct);

        // v6Logs は sanity/coverageDiag/forbiddenDiag に依存 → 依存先だけ先に await（依存グラフを尊重）。
        // 内部の待ち合わせは Ui に一切触れないため ConfigureAwait(false) で構わない
        // （PushReportAsync 側の外側の await は意図的に既定のまま＝クラスKDoc/PushReportAsync参照）。
        var sanity = await sanityTask.ConfigureAwait(false);
        var coverageDiag = await coverageTask.ConfigureAwait(false);
        var forbiddenDiag = await forbiddenTask.ConfigureAwait(false);

        var v6Logs = new List<string> { $"[I] LoadDataBit: {sanity.LoadDataBitSummary}" };
        v6Logs.AddRange(sanity.Warns.Select(w => $"[W] SanityCheck: {w}"));
        v6Logs.AddRange(sanity.Notes.Select(n => $"[I] V6Port: {n}"));
        v6Logs.AddRange(sanity.DuplicateSeqConstraints.Take(4).Select(d => $"[W] DuplicateSeq: {d}"));
        v6Logs.AddRange(sanity.Guidance.Take(12).Select(g => $"[W] 設定ミス: {g.Where} — {g.Problem} → {g.Fix}"));
        if (coverageDiag is not null) v6Logs.AddRange(coverageDiag.LogLines());
        if (forbiddenDiag is not null) v6Logs.AddRange(forbiddenDiag.LogLines());

        var mappedDiag = report.Logs.Select(l => $"[{l.Level}] {l.Tag}: {l.Message}").ToList();
        var vioDebug = await vioDebugTask.ConfigureAwait(false);
        var v6 = await v6Task.ConfigureAwait(false);

        return new Analysis(
            V6: v6,
            Sanity: sanity,
            CoverageDiag: coverageDiag,
            ForbiddenDiag: forbiddenDiag,
            V6Logs: v6Logs,
            // 出力用の全文（非圧縮）。表示は圧縮版（CompressDiagLogs）を使う。
            RawDiagLogs: v6Logs.Concat(mappedDiag).Concat(vioDebug).ToList());
    }

    /// <summary>
    /// [テスト可視性のためinternal化] 重い解析(<see cref="AnalyzeParallelAsync"/>)を実行し、その結果を
    /// <see cref="Ui"/> へ反映する共通経路。全 <see cref="MakeUi"/> 呼び出しをこの1経路へ集約する。
    /// </summary>
    /// <param name="nonCancellable">
    /// 停止(keep-best)経路から呼ぶ場合 true＝呼び出し元の <paramref name="ct"/> を無視し必ず解析を
    /// 完了させる（Kotlin原本の <c>withContext(NonCancellable)</c> に相当。<see cref="AnalyzeParallelAsync"/>
    /// のKDoc参照）。
    /// </param>
    /// <param name="runLabel">[3.379.0] エンジン実行の結果を押すときだけ非 null（"最適化" 等）。診断を退避する印。</param>
    /// <param name="transform">
    /// [_ui.update{it.copy(...)} の置き換え方針] Kotlin原本は <c>transform(base)</c> の結果を土台に
    /// <c>makeUi</c> が上書きする。この移植では「土台への変更」を <see cref="MakeUi"/> の**前**に
    /// <see cref="Ui"/> へ直接適用する（クラスKDoc参照）——結果として観測できる状態は同一になる
    /// （<see cref="MakeUi"/> が明示的に書き換えるフィールドと <paramref name="transform"/> が書き換える
    /// フィールドは、Kotlin原本の全呼び出し元で重複しない）。
    /// </param>
    /// <remarks>
    /// [ConfigureAwait の意図的な非対称] この <c>await</c> は既定のまま（ConfigureAwait を指定しない）。
    /// 将来 WinUI3 の UI スレッドから呼ばれたとき、後続（<paramref name="transform"/> の適用と
    /// <see cref="MakeUi"/> による <see cref="Ui"/> への書き込み＝<c>PropertyChanged</c> の発火）が
    /// 呼び出し元と同じスレッド（UIスレッド）へ戻ってから走るようにするため。
    /// <see cref="AnalyzeParallelAsync"/> 内部の <c>Task.Run</c> 群は <see cref="Ui"/> に一切触れないため
    /// <c>ConfigureAwait(false)</c> で構わない（そちらの KDoc 参照）が、ここは意図的に区別する。
    /// テスト（同期コンテキスト無し）では両者の挙動に差は無い。
    /// </remarks>
    internal async Task PushReportAsync(
        MagiState st, int[][] schedule, ViolationReport report,
        bool nonCancellable = false, string? runLabel = null,
        Action<UiState>? transform = null, CancellationToken ct = default)
    {
        var analysis = await AnalyzeParallelAsync(st, schedule, report, nonCancellable ? CancellationToken.None : ct);
        _rawDiagLogs = analysis.RawDiagLogs;
        _lastDiagSerial = _activeRunSerial;
        if (runLabel is not null)
        {
            _lastRunDiagLogs = analysis.RawDiagLogs;
            _lastRunDiagLabel = runLabel;
            _lastRunDiagAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _lastRunDiagSerial = _activeRunSerial;
        }
        transform?.Invoke(Ui);
        MakeUi(st, schedule, report, analysis);
    }

    /// <summary>
    /// [テスト可視性のためinternal化] <see cref="AnalyzeParallelAsync"/> による重い並列解析を経由せず、
    /// このフィールド写像そのものを直接検証できるようにする。
    /// </summary>
    internal void MakeUi(MagiState st, int[][] schedule, ViolationReport report, Analysis analysis)
    {
        var v6 = analysis.V6;
        var sanity = analysis.Sanity;
        var coverageDiag = analysis.CoverageDiag;
        var v6Logs = analysis.V6Logs;
        // [3.324.0] 研磨診断は観測した盤面のものか（盤面が変わっていれば出さない）。
        // [3.327.0] 盤面**と**制約の両方が観測時と一致するときだけ診断を出す。
        var diagFresh = _lastDiagBoardKey != 0L && _lastDiagBoardKey == BoardKey(schedule) &&
            _lastDiagStateKey == StateKey(st);
        var mappedDiag = report.Logs.Select(l => $"[{l.Level}] {l.Tag}: {l.Message}").ToList();
        // 満足度(0-100): 初期からの違反削減率。HARD未解決の間は上限を抑える。
        var initTotal = Math.Max(Ui.InitHard + Ui.InitSoft, 1L);
        var ratio = Math.Clamp(1.0 - report.Total / (double)initTotal, 0.0, 1.0);
        var sat = report.Hard > 0
            ? (int)(ratio * 55)
            : Math.Clamp(40 + (int)(ratio * 60), 0, 100);

        Ui.Staff = st.StaffCount;
        Ui.Days = st.DayCount;
        Ui.Shifts = st.ShiftCount;
        Ui.Groups = st.GroupCount;
        Ui.Use2 = st.Use2Patterns;
        Ui.BestHard = report.Hard;
        Ui.BestSoft = report.Soft;
        Ui.TotalViolations = report.Total;
        Ui.WeightedScore = report.WeightedScore;

        var breakdown = UiState.EmptyBreakdown().ToDictionary(kv => kv.Key, kv => kv.Value);
        foreach (var kv in report.Breakdown) breakdown[kv.Key] = kv.Value;
        Ui.Breakdown = breakdown;

        Ui.ViolationCells = report.Violations;
        Ui.NeedViolations = report.NeedViolations;
        Ui.CountViolations = report.CountViolations;
        Ui.ViolationCellFamilies = report.CellFamilies;
        Ui.CountFamilies = report.CountFamilies;
        Ui.NeedFamilies = report.NeedFamilies;
        Ui.DistLocations = report.DistLocations;
        Ui.Logs = v6Logs.Concat(CompressDiagLogs(mappedDiag)).ToList();
        Ui.StaffNames = st.StaffList.Select(s => s.Name).ToList();
        Ui.StaffGroupSymbols = st.StaffList
            .Select(s => KigouFormat.ToHankakuKigou(
                s.GroupIdx >= 0 && s.GroupIdx < st.Groups.Count ? st.Groups[s.GroupIdx].Kigou : ""))
            .ToList();
        Ui.ShiftSymbols = st.Shifts.Select(sh => KigouFormat.ToHankakuKigou(sh.Kigou)).ToList();
        Ui.ShiftColorHex = st.Shifts
            .Select((sh, i) => ShiftAppearance.ResolveShiftColor(st.ShiftColors.GetValueOrDefault(sh.Kigou), i))
            .ToList();
        Ui.ShiftTextHex = st.Shifts
            .Select((sh, i) => ShiftAppearance.PickTextColor(
                ShiftAppearance.ResolveShiftColor(st.ShiftColors.GetValueOrDefault(sh.Kigou), i)))
            .ToList();
        Ui.ViolationColorHex = st.ShiftColors.GetValueOrDefault("__vio__", "");
        Ui.ViolationSoftColorHex = st.ShiftColors.GetValueOrDefault("__vioSoft__", "");
        Ui.ViolationFamilyColorHex = st.ShiftColors
            .Where(kv => kv.Key.StartsWith("__vioFam_", StringComparison.Ordinal) &&
                         kv.Key.EndsWith("__", StringComparison.Ordinal))
            .ToDictionary(kv => kv.Key["__vioFam_".Length..^"__".Length], kv => kv.Value);
        Ui.Schedule = schedule.Select(row => (IReadOnlyList<int>)row.ToList()).ToList();
        Ui.Wishes = st.Wishes;
        Ui.V6 = v6;
        Ui.Satisfaction = sat;
        // 研磨の限界: 必須は解決済みだが微調整が残る → 手修正の検討を促す
        Ui.PolishExhausted = report.Hard == 0 && report.Total > 0;
        // 解決したらガチャ助言は消す（未解決なら何もしない＝可変モデルでは「据え置き」に一致する）
        if (report.Hard == 0) Ui.CopilotHint = null;
        // 担当外など実現不能な希望（Web版の担当外希望警告に相当）
        Ui.ImpossibleWishCount = sanity.ImpossibleWishes.Count;
        // 人員不足(covU)の原因診断（充足不可/充足可能の切り分け）。不足が無ければ null。
        Ui.CoverageDiag = coverageDiag;
        // [3.280.0] 禁止連続(c3n)の「なぜ崩せないか」診断。c3n=0 なら null。
        Ui.ForbiddenDiag = analysis.ForbiddenDiag;
        // [3.322.0] c1 頭打ちの構造化診断。再計算できない（研磨中の却下記録が唯一の根拠）ので
        //   ViewModel が保持し、いま c1 が残っているときだけ見せる（解消済みなら黙る）。
        // [3.324.0/外部レビュー] 診断は観測した盤面のものだけ出す。盤面が変わっていれば黙る
        //   （手編集・元に戻す・読込・初期解生成など、あらゆる変更で自動的に外れる）。
        Ui.C1Plateau = diagFresh && report.Breakdown.GetValueOrDefault("c1", 0) > 0 ? _lastC1Plateau : null;
        Ui.ObservedPinBlockedAttempts = diagFresh ? _lastObservedPinAttempts : 0;
        // [3.326.0] 緩和の対象候補。どのピンが何回止めたかを名前つきで渡す（多い順）。
        Ui.PinTargets = diagFresh ? BuildPinTargets(st, _lastPinBlocks) : Array.Empty<PinTargetView>();
        Ui.SettingIssues = sanity.Guidance;
        Ui.StartDate = st.StartDate;
    }

    /// <summary>
    /// [3.326.0 の由来] <see cref="_lastPinBlocks"/> の (職員,シフト)→試行数 を、いま固定されている
    /// （lo==hi の値が今も一致する）ものだけに絞って名前つきビューへ変換する。緩めたあと
    /// (lo != hi) も「N回に固定」と表示し続けるのは事実に反するため、毎回の呼び出し時点の
    /// <see cref="Problem.RangeLo"/>/<see cref="Problem.RangeHi"/> と突き合わせる。
    /// </summary>
    private static IReadOnlyList<PinTargetView> BuildPinTargets(MagiState st, PinBlockAttribution? pinBlocks)
    {
        if (pinBlocks is null) return Array.Empty<PinTargetView>();
        var pr = ScheduleUtil.CachedProblem(st);
        var result = new List<PinTargetView>();
        foreach (var (i, k, n) in pinBlocks.ByTarget())
        {
            if (i < 0 || i >= pr.RangeLo.Length || k < 0 || k >= pr.RangeLo[i].Length) continue;
            var lo = pr.RangeLo[i][k];
            var hi = pr.RangeHi[i][k];
            if (lo == int.MinValue || hi == int.MaxValue || lo != hi) continue;
            result.Add(new PinTargetView(
                Staff: i,
                Shift: k,
                StaffName: i >= 0 && i < st.StaffList.Count ? st.StaffList[i].Name : $"#{i}",
                ShiftKigou: k >= 0 && k < st.Shifts.Count ? st.Shifts[k].Kigou : $"{k}",
                PinnedCount: lo,
                Attempts: n));
        }
        return result;
    }
}
