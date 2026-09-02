using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>
/// [フェーズ9] <c>MagiViewModel.kt</c> の移植先——次ピース（Kotlin原本 行2877〜3278付近）。
///
/// このファイルが担う範囲: 出力クラスタ（<c>exportCsv</c>/<c>exportStaffCsv</c>/<c>exportWishesCsv</c>/
/// <c>exportConstraintsCsv</c>/<c>environmentLine</c>/<c>runTag</c>/<c>exportLogs</c>/<c>exportLogsJson</c>）、
/// 取込クラスタ（<c>periodNote</c>/<c>importCsvSmart</c>/<c>importRosterAs</c>/<c>importCsv</c>/
/// <c>looksLikeScheduleCsv</c>/<c>componentImportMismatchHint</c>/<c>importStaffCsv</c>/<c>importWishesCsv</c>/
/// <c>importConstraintsCsv</c>）、および付随のファイル入出力通知ヘルパー（<c>notifySave</c>/
/// <c>notifyOpenFailure</c>/<c>ioReason</c>）。
///
/// <c>exportJson</c>/<c>notify</c>/<c>clearMessage</c>/<c>compressDiagLogs</c>/<c>Analysis</c>/
/// <c>analyzeParallel</c> は Piece6/Piece7 で既に移植済み（<see cref="MagiViewModel.Diagnostics"/>/
/// <see cref="MagiViewModel.Persistence"/>）のためここでは扱わない。
///
/// [environmentLine の Android 依存解消について] Kotlin原本は <c>ctx.packageManager.getPackageInfo</c>
/// （アプリ版数）・<c>android.os.Build.MANUFACTURER</c>/<c>.MODEL</c>/<c>VERSION.RELEASE</c>/
/// <c>.SDK_INT</c>（端末・OS情報）・<c>NativeBridge.available</c>/<c>.ABI_VERSION</c>
/// （JNI経由のC++高速化ブリッジの可否）を参照する。この移植では:
///   - アプリ版数・端末情報は Android 専用 API のため <see cref="AppVersionInfo"/>/<see cref="DeviceInfo"/>
///     という注入可能なプロパティへ差し替えた（<c>DataDir</c>（Piece7）と同じパターン）。WinUI3ホストは
///     起動時に実際のパッケージ情報・実機情報で上書きする。既定値は .NET のクロスプラットフォームAPI
///     （テスト環境・Linux上でも動く）から取るため、ホスト未設定でも意味のある値を返す。
///   - OS情報は <see cref="OsInfo"/>（既定 <c>RuntimeInformation.OSDescription</c>）。
///   - ネイティブ加速は、この移植計画で <c>magi_native.cpp</c> が永続的にスコープ外と確定しているため
///     「ネイティブ加速」という概念自体が存在しない＝常に固定文字列（<see cref="NativeInfo"/>）を返す
///     （実行時に判定する対象が無いため、Kotlin側の if/else 分岐に相当するものは無い）。
/// </summary>
public sealed partial class MagiViewModel
{
    // ===== 環境情報（environmentLine が使う注入可能プロパティ） =====

    /// <summary>
    /// アプリのバージョン文字列（表示名＋ビルド番号、例 "3.360.0 (526)"）。WinUI3ホストが起動時に
    /// 実際のパッケージ情報（<c>Package.Current.Id.Version</c> 相当）で上書きする。
    /// Kotlin原本の <c>ctx.packageManager.getPackageInfo(...)</c> に相当（Android専用APIのため
    /// 注入可能なプロパティへ差し替え）。既定値はホスト未設定（テスト環境等）向けの代替。
    /// </summary>
    public string AppVersionInfo { get; set; } = "不明";

    /// <summary>
    /// 実行端末の説明。WinUI3ホストが起動時に実機情報で上書きする。Kotlin原本の
    /// <c>android.os.Build.MANUFACTURER</c>/<c>.MODEL</c> に相当。既定値は .NET のクロスプラットフォーム
    /// API（<see cref="Environment.MachineName"/>）から取る（サンドボックス環境等で取得に失敗しても
    /// 例外にしない＝Kotlin原本の <c>runCatching{}.getOrDefault("不明")</c> と同じ安全側フォールバック）。
    /// </summary>
    public string DeviceInfo { get; set; } = SafeMachineName();

    /// <summary>
    /// OS説明。Kotlin原本の <c>android.os.Build.VERSION.RELEASE</c>/<c>.SDK_INT</c> に相当。既定値は
    /// <c>RuntimeInformation.OSDescription</c>（.NETのクロスプラットフォームAPI、Linux上でも動く＝
    /// テスト可能）。
    /// </summary>
    public string OsInfo { get; set; } = System.Runtime.InteropServices.RuntimeInformation.OSDescription;

    /// <summary>
    /// Kotlin原本の <c>NativeBridge.available</c>/<c>.ABI_VERSION</c> 分岐（JNI経由のC++高速化
    /// ブリッジの有効性）に相当する行。この移植では <c>magi_native.cpp</c> は永続的にスコープ外
    /// （移植計画で確定済み）のため、ネイティブ加速という概念自体が存在しない＝常にこの固定文字列。
    /// </summary>
    private const string NativeInfo = "非使用（フルマネージドC#実装）";

    private static string SafeMachineName()
    {
        try { return Environment.MachineName; }
        catch { return "不明"; }
    }

    /// <summary>
    /// [3.360.0相当] 書き出したログが「どの版・どの端末で走ったか」を1行で残す。CPU コア数を出すのは、
    /// フェーズ5で移植した並列ワーカー設定のコア数クランプが**黙って設定を切り下げる**ため
    /// （設定値だけを見ても実際の並列度が読めない）。
    /// </summary>
    public string EnvironmentLine()
    {
        var cores = Environment.ProcessorCount;
        return $"版: {AppVersionInfo} ・ {DeviceInfo} ・ {OsInfo}" +
            $" ・ CPU {cores}コア(いまの並列ワーカー設定={Ui.Workers}) ・ ネイティブ={NativeInfo}";
    }

    /// <summary>[3.408.0相当] 実行の帰属表示。0＝実行外（違反チェック等）。</summary>
    private static string RunTag(int serial) => serial > 0 ? $"実行#{serial}" : "実行外";

    // ===== コンポーネント別エクスポート（取込種別と対。出力→編集→取込で往復可） =====

    public string? ExportCsv()
    {
        var st = _state;
        var sched = _currentSchedule;
        return st is null || sched is null ? null : ScheduleCsvBridge.Build(st, sched);
    }

    public string? ExportStaffCsv() => _state is null ? null : StaffCsvIO.Build(_state);
    public string? ExportWishesCsv() => _state is null ? null : WishesCsvIO.Build(_state);
    public string? ExportConstraintsCsv() => _state is null ? null : ConstraintsCsvIO.Build(_state);

    /// <summary>操作ログ・診断ログの平文ファイル書き出し（Web版の「ログ出力」に相当）。</summary>
    public string? ExportLogs()
    {
        var ops = Ui.OpLog;
        var runsInLog = _opLog.Select(e => e.Run).Where(r => r > 0).Distinct().OrderBy(r => r).ToList();
        var runSpan = runsInLog.Count == 0 ? "" : $"・実行#{runsInLog[0]}〜#{runsInLog[^1]}";
        // 出力は全文（非圧縮）。画面表示は圧縮版だが、監査用にはロスレスの _rawDiagLogs を使う。
        var logs = _rawDiagLogs.Count > 0 ? _rawDiagLogs : Ui.Logs;
        if (ops.Count == 0 && logs.Count == 0) return null;
        var ts = DateTime.Now.ToString("yyyy\\/MM\\/dd HH:mm:ss", CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        sb.Append("MAGI ログ (Native)  出力: ").Append(ts).Append('\n');
        sb.Append(EnvironmentLine()).Append('\n');
        sb.Append($"状態: {Ui.Staff}名/{Ui.Days}日 ・ 必須={Ui.BestHard} 合計={Ui.TotalViolations}\n");
        sb.Append($"\n==== 操作ログ（新しい順 {ops.Count}件{runSpan}）====\n");
        foreach (var line in ops) sb.Append(line).Append('\n');
        sb.Append($"\n==== 診断ログ（{RunTag(_lastDiagSerial)}の全文 {logs.Count}件）====\n");
        // [3.408.0相当] 操作ログは履歴・診断ログは直近1回ぶん。実行が2回以上あるとき、両者を続けて
        //   読むと前の実行の「グローバル最良更新」と直近の「全体最良更新=0回」が同一実行の矛盾に見える。
        //   どの行がどの実行かは行頭の #N で分かる、と明示する。
        if (runsInLog.Count > 1)
        {
            sb.Append($"※操作ログは複数回の実行を含みます（行頭 #N）。この診断ログは {RunTag(_lastDiagSerial)} のものだけです。\n");
        }
        foreach (var line in logs) sb.Append(line).Append('\n');
        // [3.379.0相当] 最適化のあとに1回でも編集/再チェックすると診断が丸ごと入れ替わるため、
        //   直近のエンジン実行ぶんを別セクションで必ず残す（同一なら重複させない）。
        var run = _lastRunDiagLogs;
        if (run.Count > 0 && !run.SequenceEqual(logs))
        {
            var at = DateTimeOffset.FromUnixTimeMilliseconds(_lastRunDiagAtMs)
                .ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            sb.Append($"\n==== 直近の{_lastRunDiagLabel}の診断ログ（{RunTag(_lastRunDiagSerial)}・{at} 時点・全文 {run.Count}件）====\n");
            sb.Append("※上の診断ログはその後の編集/再チェックで作り直された最新版です。こちらは実行時のもの。\n");
            foreach (var line in run) sb.Append(line).Append('\n');
        }
        return sb.ToString();
    }

    private static readonly JsonSerializerOptions LogJsonOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    /// <summary>操作ログ・診断ログ・現在の違反サマリを構造化JSONで書き出す（監査用）。</summary>
    public string? ExportLogsJson()
    {
        if (Ui.OpLog.Count == 0 && Ui.Logs.Count == 0) return null;
        var o = new JsonObject
        {
            ["exportedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ["environment"] = EnvironmentLine(),   // [3.360.0相当] 版・端末・コア数・ネイティブ可否（テキスト版と同一）
            ["staff"] = Ui.Staff,
            ["days"] = Ui.Days,
            ["shifts"] = Ui.Shifts,
            ["hard"] = Ui.BestHard,
            ["soft"] = Ui.BestSoft,
            ["total"] = Ui.TotalViolations,
            ["satisfaction"] = Ui.Satisfaction,
            // [N6相当] satisfaction は 0-100 の進捗スコア（違反減少度）であり希望充足率ではない。
            //   外部AI/人間の誤読が実際に発生したため意味を同梱する。
            ["satisfactionMeaning"] = "0-100の進捗スコア（必須・合計違反の減少度）。希望充足率ではありません",
        };

        var opArr = new JsonArray();
        foreach (var line in Ui.OpLog) opArr.Add(line);
        o["opLog"] = opArr;

        var logs = _rawDiagLogs.Count > 0 ? _rawDiagLogs : Ui.Logs;
        var diagArr = new JsonArray();
        foreach (var line in logs) diagArr.Add(line);
        o["diagLog"] = diagArr;

        // [3.408.0相当] 帰属の鍵。opLog の行頭 #N と対応する。これが無いと、複数回実行したあとの
        //   書き出しで前の実行の「グローバル最良更新」と直近の「全体最良更新=0回」が同一実行の矛盾に見える。
        o["diagRun"] = _lastDiagSerial;

        var runsArr = new JsonArray();
        foreach (var r in _opLog.Select(e => e.Run).Where(r => r > 0).Distinct().OrderBy(r => r)) runsArr.Add(r);
        o["runsInOpLog"] = runsArr;

        // [3.379.0相当] テキスト版と同じ理由＝最適化後の編集で diagLog は作り直されるため実行時のぶんも残す。
        if (_lastRunDiagLogs.Count > 0)
        {
            o["lastRunLabel"] = _lastRunDiagLabel;
            o["lastRunSerial"] = _lastRunDiagSerial;
            o["lastRunAt"] = _lastRunDiagAtMs;
            var lastRunArr = new JsonArray();
            foreach (var line in _lastRunDiagLogs) lastRunArr.Add(line);
            o["lastRunDiagLog"] = lastRunArr;
        }

        var breakdownObj = new JsonObject();
        foreach (var (k, v) in Ui.Breakdown) breakdownObj[k] = v;
        o["breakdown"] = breakdownObj;

        return o.ToJsonString(LogJsonOptions);
    }

    // ===== CSV取込 =====

    /// <summary>
    /// [3.414.0/I-02相当] CSV取込は期間を推定して黙って確定していた（<see cref="RosterCsvImport"/> は
    /// タイトルに年月が無ければ当年1月、<see cref="FlatRosterCsvImport"/> は曜日行から当年で最初に
    /// 一致する月・曜日行が無ければ当年1月）。期間は勤務表の根幹で、間違っていれば曜日の平準化も
    /// 日付表示もずれるのに、画面には「N名 / M日」しか出ず推定したことすら伝わらなかった。何日から
    /// 取り込んだかを必ず出す。挙動は不変＝知らせるだけで、違っていれば設定タブで直せる。
    /// </summary>
    private static string PeriodNote(string startDate) =>
        $"｜期間は「{startDate}」から として取り込みました（CSVに年月が無い場合は推定です。設定タブで直せます）";

    /// <summary>
    /// CSV取込の振り分け。病院などの「勤務表テンプレCSV」(ユニット/スタッフ/凡例を含む完全な1ヶ月表) は
    /// 新規データセットとして丸ごと取り込む（<see cref="RosterCsvImport"/>）。それ以外は、既存データへ
    /// 勤務表だけを重ねる従来の取込（<see cref="ImportCsv"/>）に回す（既存データが無ければ案内のみ）。
    /// </summary>
    public void ImportCsvSmart(string rawText)
    {
        var text = MojibakeRepair.Repair(rawText);
        if (RosterCsvImport.Detect(text))
        {
            MagiState? st;
            try { st = RosterCsvImport.Parse(text); } catch { st = null; }
            if (st is not null)
            {
                // 凡例(記号一覧)が無いとシフトが「休」1種のみになり全セルが公休化する。
                // 取り込まず原因をオペレーターに表示する（Excel保存で凡例が消えるケース）。
                if (st.ShiftCount <= 1)
                {
                    Ui.MessageIsError = true;
                    Ui.Message = "CSV取込失敗: シフト記号（凡例）が見つかりません。テンプレCSV末尾の『記号 / 時刻 …』一覧が削除されていないかご確認ください（Excelで開いて保存すると消える場合があります）。元のファイルをそのまま取り込んでください。";
                    LogOp("W", $"勤務表CSV取込 中止: 凡例なし（シフト{st.ShiftCount}種のみ→全公休化を防止）");
                    return;
                }
                LogOp("I", $"勤務表CSVを新規取込: {st.StaffCount}名 / {st.DayCount}日 / {st.ShiftCount}シフト / {st.GroupCount}ユニット / 期間{st.StartDate}〜{st.EndDate}");
                Load(StateJsonSerializer.Serialize(st, st.Schedule.ToIntArray2D()), PeriodNote(st.StartDate));
                return;
            }
            // テンプレらしいが解析不能 → 既存取込にフォールバック（または案内）。
        }
        // ユニット列形式（凡例なし: ユニット,No,役職,氏名,1,2,…）の勤務表CSV → 新規データセットとして取込。
        if (FlatRosterCsvImport.Detect(text))
        {
            MagiState? st;
            try { st = FlatRosterCsvImport.Parse(text); } catch { st = null; }
            if (st is not null)
            {
                // [3.414.0/I-02相当] この形式は必ず期間を推定する（曜日行から当年で最初に一致する月・
                //   曜日行が無ければ当年1月）。何日から取り込んだかを必ず出す（挙動は不変）。
                LogOp("I", $"勤務表CSV(ユニット列形式)を新規取込: {st.StaffCount}名 / {st.DayCount}日 / {st.ShiftCount}シフト / {st.GroupCount}ユニット / 期間{st.StartDate}〜{st.EndDate}（推定）");
                Load(StateJsonSerializer.Serialize(st, st.Schedule.ToIntArray2D()), PeriodNote(st.StartDate));
                return;
            }
            Ui.MessageIsError = true;
            Ui.Message = "CSV取込失敗: ユニット列形式と判定しましたが解析できませんでした。ヘッダ行（ユニット, No, 役職, 氏名, 1, 2, …）と氏名列をご確認ください。";
            LogOp("W", "勤務表CSV(ユニット列形式)取込 失敗: 解析不能");
            return;
        }
        if (_state is null)
        {
            Ui.MessageIsError = true;
            Ui.Message = "このCSVを読み込めませんでした。先に『データを開く』で基本データを読み込むか、勤務表テンプレCSVをご利用ください。";
            return;
        }
        // [3.282.0相当] 修復済みテキストをそのまま渡す（旧: rawText を渡し ImportCsv 内で二重に repair＝
        //   結果は同一だが無駄な再修復と非対称があった）。
        ImportCsv(text);
    }

    /// <summary>
    /// 勤務表テンプレCSVを、利用者の選択（勤務表 or 希望シフト）で新規データとして取り込む。
    ///  - asWishes=false: 本表を初期割り当て(勤務表)として読み込む。
    ///  - asWishes=true : 本表をスタッフの希望として読み込み、勤務表は空(全公休)で開始（最適化で尊重）。
    /// </summary>
    public void ImportRosterAs(string rawText, bool asWishes)
    {
        var text = MojibakeRepair.Repair(rawText);
        MagiState? st;
        try { st = RosterCsvImport.Parse(text, asWishes); } catch { st = null; }
        if (st is null)
        {
            Ui.MessageIsError = true;
            Ui.Message = "このCSVを読み込めませんでした。形式をご確認ください。";
            return;
        }
        if (st.ShiftCount <= 1)
        {
            Ui.MessageIsError = true;
            Ui.Message = "CSV取込失敗: シフト記号（凡例）が見つかりません。テンプレCSV末尾の『記号 / 時刻 …』一覧が削除されていないかご確認ください（Excelで保存すると消える場合があります）。";
            LogOp("W", $"{(asWishes ? "希望シフト" : "勤務表")}CSV取込 中止: 凡例なし（シフト{st.ShiftCount}種のみ）");
            return;
        }
        var kind = asWishes ? "希望シフト" : "勤務表";
        LogOp("I", $"{kind}として新規取込: {st.StaffCount}名 / {st.DayCount}日 / {st.ShiftCount}シフト / {st.GroupCount}ユニット / 期間{st.StartDate}〜{st.EndDate}" +
            (asWishes ? $"（希望{st.Wishes.Count}件）" : ""));
        Load(StateJsonSerializer.Serialize(st, st.Schedule.ToIntArray2D()), PeriodNote(st.StartDate));
    }

    /// <summary>[テスト可視性のための追加] 直近の <see cref="ImportCsv"/> 呼出しが背後で走らせる Task。</summary>
    internal Task? LastImportCsvTask { get; private set; }

    public void ImportCsv(string rawText)
    {
        // [3.404.0相当] 旧: 入口ガードが無く、「job = viewModelScope.launch」が走行中の最適化の参照を
        //   キャンセルせずに上書きしていた＝その最適化は「やめる」で止められないゾンビになる
        //   （3.271.0 が GenerateSmartInitial で直したのと同型の取り残し）。
        if (RunBlockedByInFlight("CSV取込")) return;
        var st = _state;
        var sched = _currentSchedule;
        if (st is null || sched is null) return;
        var text = MojibakeRepair.Repair(rawText);
        var repaired = MojibakeRepair.WasDecoded(rawText, text);
        Ui.MessageIsError = false;
        Ui.Running = true;
        Ui.Message = "CSV取込中…";
        var boardToken = BeginBoardJob("CSV取込");
        var cts = new CancellationTokenSource();
        _job = cts;
        LastImportCsvTask = ImportCsvCoreAsync(text, repaired, st, sched, boardToken, cts.Token);
    }

    private async Task ImportCsvCoreAsync(
        string text, bool repaired, MagiState st, int[][] sched, int boardToken, CancellationToken ct)
    {
        try
        {
            // [3.282.0相当] JSON側(LoadAsync)と同じ是正: BOM除去だけの健全なCSVで誤警告しない。
            if (repaired)
            {
                LogOp("W", "文字化け（二重エンコード）を自動修復してCSVを取り込みました。元のファイル自体は修復されません");
            }
            var res = await Task.Run(() => ScheduleCsvBridge.Parse(text, st, sched), ct);
            // 取込失敗の明示: 氏名が1件も一致しなければ適用せず、オペレーターに原因を表示する。
            if (res.Matched == 0)
            {
                Ui.MessageIsError = true;
                Ui.Running = false;
                Ui.Message = "CSV取込失敗: 一致する職員名がありませんでした（0名）。CSVの1列目の氏名が現在のデータと一致しているか、列レイアウト（氏名, 1日目, 2日目, …）をご確認ください。";
                LogOp("W", "CSV取込 失敗: 職員名が0件一致のため取込を中止しました（氏名/列レイアウトを確認）");
                return;
            }
            PushUndo();
            _currentSchedule = res.Schedule.Copy2D();
            AutoSave();
            _resultSchedule = res.Schedule.Copy2D();
            _state = st.WithSchedule(res.Schedule);
            var total = st.StaffCount;
            // [3.410.0/I-01相当] シフト一覧に無い記号は取り込めない。旧: 黙って読み飛ばしていたため、
            //   誤字や凡例漏れが「休のまま」「元のまま」として静かに混入した。件数と記号を必ず出す。
            // [3.413.0/I-08相当] 引用符が閉じないCSVは残りの行が丸ごと消える＝「氏名不一致でスキップ」と
            //   区別が付かず部分的な成功に見える。必ず名指しする。
            var quoteWarn = res.UnclosedQuote
                ? "｜⚠ 引用符（\"）が閉じていません。ここから後ろの行は読めていません"
                : "";
            var unk = res.UnknownCells > 0
                ? $"｜読めない記号 {res.UnknownCells}セル({string.Join("・", res.UnknownSymbols)})は取り込めませんでした"
                : "";
            var msg = res.Matched >= 1 && res.Matched < total
                ? $"CSV取込完了: {res.Matched}/{total}名を更新（{total - res.Matched}名は氏名不一致でスキップ）｜必須={res.Report.Hard} 合計={res.Report.Total}{unk}{quoteWarn}"
                : $"CSV取込完了: {res.Matched}名を更新｜必須={res.Report.Hard} 合計={res.Report.Total}{unk}{quoteWarn}";
            await PushReportAsync(_state ?? st, res.Schedule, res.Report, transform: ui =>
            {
                ui.MessageIsError = res.UnknownCells > 0 || res.UnclosedQuote;
                ui.Running = false;
                ui.HasResult = true;
                ui.Message = msg;
            }, ct: ct);
            if (res.Matched >= 1 && res.Matched < total)
            {
                LogOp("W", $"CSV取込 一部のみ反映: {res.Matched}/{total}名一致（{total - res.Matched}名は氏名不一致）");
            }
            if (res.UnknownCells > 0)
            {
                LogOp("W", $"CSV取込 読めない記号 {res.UnknownCells}セル: {string.Join("・", res.UnknownSymbols)}（シフト一覧に無い記号）");
            }
            LogOp("I", $"CSV取込 完了 {res.Matched}名一致 必須={res.Report.Hard} 合計={res.Report.Total}");
        }
        catch (OperationCanceledException)
        {
            Ui.MessageIsError = false;
            Ui.Running = false;
            Ui.Message = "CSV取込を中止しました";   // [3.404.0相当]
            throw;
        }
        catch (Exception e)
        {
            Ui.Running = false;
            Ui.Message = $"CSVを取り込めませんでした（{e.GetType().Name}）";
            Ui.MessageIsError = true;
        }
        finally
        {
            EndBoardJob(boardToken);
        }
    }

    /// <summary>取込種別を取り違えた可能性の判定: 勤務表(スケジュール)CSVらしいか。</summary>
    private static bool LooksLikeScheduleCsv(string t)
    {
        var lines = t.Split('\n');
        if (lines.Length == 0) return false;
        // ScheduleCsvBridge.Build のヘッダ「スタッフ \ 日付,…」、または集計ブロック「集計,…」。
        var head = lines[0].Trim();
        if (head.StartsWith("スタッフ") && head.Contains("日付")) return true;
        return lines.Any(l => l.TrimStart().StartsWith("集計,"));
    }

    /// <summary>希望/制約の取込が0件のとき、別形式CSVの取り違えを推定して利用者向けヒントを返す（無ければ空）。</summary>
    private static string ComponentImportMismatchHint(string repairedText)
    {
        if (RosterCsvImport.Detect(repairedText) || FlatRosterCsvImport.Detect(repairedText))
            return "これは勤務表全体（テンプレ/ユニット列形式）のCSVのようです。取込種別で『データ全体（新規）』を選んでください。";
        if (LooksLikeScheduleCsv(repairedText))
            return "これは勤務表（スケジュール）CSVのようで、希望・制約は含まれていません。専用CSVを、出力タブの『希望』『制約』ボタンで出して取り込んでください。";
        return "";
    }

    /// <summary>[コンポーネント別取込] スタッフ一覧CSV（氏名,グループ,スキル）。既存は所属群/スキルを更新、未知の氏名は新規追加（勤務表に休の行を追加）。</summary>
    public void ImportStaffCsv(string rawText)
    {
        var st = _state;
        var sched = _currentSchedule;
        if (st is null || sched is null)
        {
            Ui.MessageIsError = false;
            Ui.Message = "先にデータを開いてください（職員一覧は既存データに追加/更新します）";
            return;
        }
        var text = MojibakeRepair.Repair(rawText);
        StaffCsvIO.StaffUpsertResult? res;
        try { res = StaffCsvIO.ParseUpsert(text, st, sched); } catch { res = null; }
        if (res is null)
        {
            var hint = ComponentImportMismatchHint(text);
            var tail = hint.Length == 0 ? "形式『氏名,グループ,スキル』（1行=1名）をご確認ください。" : hint;
            Ui.MessageIsError = true;
            Ui.Message = $"職員一覧の取込失敗（追加0・更新0）。{tail}";
            LogOp("W", "職員一覧CSV取込 失敗: 0件");
            return;
        }
        var parts = new List<string>();
        if (res.Added > 0) parts.Add($"{res.Added}名を新規追加");
        if (res.Updated > 0) parts.Add($"{res.Updated}名を更新");

        // [3.413.0/I-07相当] 空でないのに解決できなかった群/スキル記号を必ず知らせる。旧: 新規は先頭
        //   グループ、既存は現状維持へ黙って落ち、空欄と誤記が見分けられなかった。所属グループは
        //   担当できるシフトを決めるので、誤記が通ると説明のつかない盤面になる。
        var badG = res.UnknownGroups;
        var badS = res.UnknownSkills;
        var warn = new List<string>();
        if (badG.Count > 0)
            warn.Add($"グループ記号 {string.Join("・", badG.Take(3).Select(kv => $"「{kv.Key}」{kv.Value}件"))}{(badG.Count > 3 ? "ほか" : "")}");
        if (badS.Count > 0)
            warn.Add($"スキル記号 {string.Join("・", badS.Take(3).Select(kv => $"「{kv.Key}」{kv.Value}件"))}{(badS.Count > 3 ? "ほか" : "")}");

        var tailWarn = warn.Count == 0
            ? ""
            : $"。⚠ 見つからない{string.Join("／", warn)}（新規は先頭グループ・既存は元のまま。記号をご確認ください）";
        var msg = "職員一覧を取込: " + string.Join("・", parts) + tailWarn;
        LogOp(warn.Count == 0 ? "I" : "W",
            $"職員一覧CSV取込: 追加{res.Added} 更新{res.Updated}" +
            (badG.Count > 0 ? $" 未知グループ{badG.Values.Sum()}件" : "") +
            (badS.Count > 0 ? $" 未知スキル{badS.Values.Sum()}件" : ""));
        ApplyStructureWithMessage(new Ws1Result(res.State, res.Schedule), msg);
    }

    /// <summary>[コンポーネント別取込] 希望シフトCSV（氏名,日,希望シフト）。氏名一致で希望を全置換。</summary>
    public void ImportWishesCsv(string rawText)
    {
        var st = _state;
        if (st is null)
        {
            Ui.MessageIsError = false;
            Ui.Message = "先にデータを開いてください（希望シフトは既存データに重ねます）";
            return;
        }
        var text = MojibakeRepair.Repair(rawText);
        ComponentImport? res;
        try { res = WishesCsvIO.Parse(text, st); } catch { res = null; }
        if (res is null)
        {
            var hint = ComponentImportMismatchHint(text);
            var tail = hint.Length == 0
                ? "形式は『氏名,日,希望シフト』（例: 古泉 健一,5,休）です。氏名・シフト記号が一致しているかご確認ください。"
                : hint;
            Ui.MessageIsError = true;
            Ui.Message = $"希望シフトの取込失敗（取り込める行が0件）。{tail}";
            LogOp("W", $"希望シフトCSV取込 失敗: 0件{(hint.Length == 0 ? "" : "（別形式CSVの取り違えの可能性）")}");
            return;
        }
        // [3.329.0/外部レビューH-02相当] この取込は既存の希望を全置換する。中身のある行を1つでも
        //   解釈できなかったら置換しない（旧: 誤記の行を黙って捨て、1行でも有効なら残りの希望を消していた）。
        if (res.Rejected > 0)
        {
            Ui.MessageIsError = false;
            Ui.Message = $"希望シフトの取込を中止しました（読めない行が{res.Rejected}件）。この取込は既存の希望を置き換えるため、全部読めたときだけ実行します。例: {res.Sample}";
            LogOp("W", $"希望シフトCSV取込 中止: 読めない行{res.Rejected}件（取込可{res.Accepted}件）例: {res.Sample}");
            return;
        }
        LogOp("I", $"希望シフトCSV取込: {res.Accepted}件を反映（全置換）");
        ApplyStructureWithMessage(res.State, $"希望シフトを取込: {res.Accepted}件を反映（既存の希望は置換）");
    }

    /// <summary>[コンポーネント別取込] 各制約CSV（種別タグ付き）。制約一式＋個人レンジを置換。</summary>
    public void ImportConstraintsCsv(string rawText)
    {
        var st = _state;
        if (st is null)
        {
            Ui.MessageIsError = false;
            Ui.Message = "先にデータを開いてください（各制約は既存データに重ねます）";
            return;
        }
        var text = MojibakeRepair.Repair(rawText);
        ComponentImport? res;
        try { res = ConstraintsCsvIO.Parse(text, st); } catch { res = null; }
        if (res is null)
        {
            var hint = ComponentImportMismatchHint(text);
            var tail = hint.Length == 0
                ? "1列目の種別（連勤/禁止連続/群組合せ禁止/個人レンジ 等）をご確認ください。例: 連勤,5,休,14 ／ 個人レンジ,古泉 健一,A4,6,8"
                : hint;
            Ui.MessageIsError = true;
            Ui.Message = $"各制約の取込失敗（取り込める行が0件）。{tail}";
            LogOp("W", $"各制約CSV取込 失敗: 0件{(hint.Length == 0 ? "" : "（別形式CSVの取り違えの可能性）")}");
            return;
        }
        // [3.329.0/外部レビューH-02相当] 制約一式と個人レンジを全置換するので、希望と同じ扱いにする。
        if (res.Rejected > 0)
        {
            Ui.MessageIsError = false;
            Ui.Message = $"各制約の取込を中止しました（読めない行が{res.Rejected}件）。この取込は既存の制約・個人レンジを置き換えるため、全部読めたときだけ実行します。例: {res.Sample}";
            LogOp("W", $"各制約CSV取込 中止: 読めない行{res.Rejected}件（取込可{res.Accepted}件）例: {res.Sample}");
            return;
        }
        LogOp("I", $"各制約CSV取込: {res.Accepted}件を反映（制約一式を置換）");
        ApplyStructureWithMessage(res.State, $"各制約を取込: {res.Accepted}件を反映（既存の制約・個人レンジは置換）");
    }

    // ===== ファイル入出力の通知ヘルパー =====

    /// <summary>
    /// [Result&lt;*&gt; の代替] Kotlin原本の <c>kotlin.Result&lt;*&gt;</c>（ペイロード型は
    /// <see cref="NotifySave"/>/<see cref="NotifyOpenFailure"/> のどちらでも使われないため消去済み）に
    /// 相当する最小の成功/失敗値。WinUI3側の実ファイル保存・読込コード（<c>SettingsView.xaml.cs</c>、
    /// <c>FileOpenPicker</c>/<c>FileSavePicker</c> 経由・2026-09-02にエラーハンドリングを実装済み）が
    /// この型を組み立てて渡す。
    /// </summary>
    public readonly struct IoOutcome
    {
        public bool Success { get; }
        public Exception? Error { get; }
        private IoOutcome(bool success, Exception? error) { Success = success; Error = error; }
        public static IoOutcome Ok() => new(true, null);
        public static IoOutcome Fail(Exception? error) => new(false, error);
    }

    /// <summary>
    /// ファイル書き込みの結果を1行で返す。成功も必ず返すのが肝で、旧実装は成功時も無反応だったため
    /// 「保存できたのか」を画面で確かめる手段が無かった。
    /// </summary>
    public void NotifySave(IoOutcome result, string what)
    {
        if (result.Success) Notify($"{what}を保存しました");
        else Notify($"{what}を保存できませんでした（{IoReason(result.Error)}）", "W");
    }

    /// <summary>ファイル読み込みの失敗を1行で返す（成功時は呼ばない＝読み込めた事実は中身の表示が示す）。</summary>
    public void NotifyOpenFailure(IoOutcome result, string what)
    {
        Notify($"{what}を開けませんでした（{IoReason(result.Error)}）", "W");
    }

    /// <summary>
    /// 例外を利用者の言葉へ。生の例外文を画面へ出さない（3.147.0/3.191.0相当の方針）が、詳しい原因は
    /// Notify が LogOp へ流すので書き出したログには残る。
    /// [SecurityException→UnauthorizedAccessException] Kotlin原本は Android の
    /// <c>java.lang.SecurityException</c> を見るが、.NETの実際のファイルI/O APIが権限拒否で投げるのは
    /// <see cref="UnauthorizedAccessException"/>（CAS由来のSecurityExceptionは現行.NETでは実質使われない）
    /// のため、こちらへ差し替えている。
    /// </summary>
    private static string IoReason(Exception? e) => e switch
    {
        null => "内容が空でした",
        UnauthorizedAccessException => "アクセスが許可されていません",
        FileNotFoundException => "ファイルが見つからないか、書き込みが許可されていません",
        _ when e.Message.Contains("space", StringComparison.OrdinalIgnoreCase) => "保存先の空き容量が足りません",
        _ => e.GetType().Name,
    };
}
