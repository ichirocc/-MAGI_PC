using System.Globalization;
using System.Text.RegularExpressions;

namespace MagiApp.ViewModels;

/// <summary>[3.694.0] 操作ログと書き出しに足す語の整形（Kotlin <c>OpLogText</c>）。VM の状態を読まない純関数だけを置き、
/// 形式はここで固定する（期間は ISO の日付・指紋は 16 桁の 16 進・小数は InvariantCulture）。
/// [3.697.0] 画面の操作の行と「操作の要約（迷いの指標）」もここ。</summary>
public static class OpLogText
{
    /// <summary>操作ログのリングの上限（<c>LogOp</c> が捨て始める件数。Kotlin <c>OP_LOG_CAP</c>）。</summary>
    public const int OpLogCap = 1000;

    /// <summary>64bit の指紋を 16 桁の 16 進で（負の値は 2 の補数のまま。例: -1 → ffffffffffffffff）。</summary>
    public static string Hex16(long v) => v.ToString("x16", CultureInfo.InvariantCulture);

    /// <summary>期間の表記「2026-10-01〜2026-10-31（31日）」。<paramref name="startDate"/>・<paramref name="endDate"/> は state の ISO 文字列をそのまま使う。</summary>
    public static string Period(string startDate, string endDate, int days) => $"{startDate}〜{endDate}（{days}日）";

    /// <summary>「状態指紋=… ・ 盤面指紋=…」。開始・完了・読込・下書きの行が同じ並びで出す。</summary>
    public static string Keys(long stateKey, long boardKey) => $"状態指紋={Hex16(stateKey)} ・ 盤面指紋={Hex16(boardKey)}";

    /// <summary>リングから捨てた件数（<c>max(0, size - cap)</c>）。</summary>
    public static int Dropped(int size, int cap) => Math.Max(0, size - cap);

    /// <summary>書き出しの見出しの直後の注記の後半。「実行#」を含めない（既存の書き出しテストが不在を照合する）。</summary>
    public static string DropNote(int dropped) => $"上限{OpLogCap}件を超えて捨てた古い操作は {dropped} 件です。";

    /// <summary>書き出しの見出しの直後の注記。<paramref name="startedAt"/> は記録の開始時刻（yyyy/MM/dd HH:mm:ss）。</summary>
    public static string Note(string startedAt, int dropped) => $"※この記録は {startedAt} からの分です。{DropNote(dropped)}";

    // ===== [3.697.0] 画面の操作（迷いの指標）。利用者の操作ごとに 1 行＝時刻からしか推定できなかった迷いを行で読む =====
    public static readonly IReadOnlyList<string> TabNames = new[] { "ホーム", "勤務表", "編集", "分析", "設定" };

    /// <summary>画面側が <c>MagiViewModel.LogUi</c> に渡す操作。文字列はここ（<see cref="UiLine"/>）だけが組む。</summary>
    public abstract record UiOp
    {
        public sealed record Tab(int Index) : UiOp;
        public sealed record TourStart(int Total) : UiOp;
        /// <summary><paramref name="At"/> は最後に見ていた件の添字（0 起点）。</summary>
        public sealed record TourEnd(int At) : UiOp;
        public sealed record PreRunOpen : UiOp;
        public sealed record PreRunCheckConditions : UiOp;
        public sealed record PreRunClose : UiOp;
        public sealed record PreDistOpen : UiOp;
        public sealed record PreDistExport : UiOp;
        public sealed record PreDistBack : UiOp;
        public sealed record GuidedOpen(string DayLabel, string ShiftSymbol, int Candidates) : UiOp;
        public sealed record GuidedClose : UiOp;
    }

    /// <summary>巡回の終了は開始時の総数 <paramref name="tourTotal"/> と組にする（前・次ごとには出さない）。</summary>
    public static string UiLine(UiOp op, int tourTotal = 0) => op switch
    {
        UiOp.Tab t => $"画面: {(t.Index >= 0 && t.Index < TabNames.Count ? TabNames[t.Index] : $"#{t.Index}")}",
        UiOp.TourStart t => $"巡回 開始: 必須違反 {t.Total}か所",
        UiOp.TourEnd t => $"巡回 終了: {t.At + 1}/{tourTotal}",
        UiOp.PreRunOpen => "作成前の確認: 開く",
        UiOp.PreRunCheckConditions => "作成前の確認: 条件を確認する",
        UiOp.PreRunClose => "作成前の確認: 閉じる",
        UiOp.PreDistOpen => "配布前の確認: 開く",
        UiOp.PreDistExport => "配布前の確認: 書き出す",
        UiOp.PreDistBack => "配布前の確認: 戻る",
        UiOp.GuidedOpen g => $"人員不足の手伝い: 開く {g.DayLabel.Split('(')[0]} {g.ShiftSymbol} 候補 {g.Candidates}人",
        UiOp.GuidedClose => "人員不足の手伝い: 閉じる（適用なし）",
        _ => throw new ArgumentOutOfRangeException(nameof(op)),
    };

    /// <summary>セル 1 つの手直し。<paramref name="md"/> は画面の M/D（<c>DayText.Short</c>）、空欄は <see cref="EmptyCell"/>。</summary>
    public static string CellChangeLine(string name, string md, string from, string to) => $"手で変更: {name} {md} {from}→{to}";
    public const string EmptyCell = "空";
    public static string CellsChangeLine(int count, string symbol) => $"手で変更: {count}セル → {symbol}（一括）";

    /// <summary>置き換える前の必須（<c>BestHard</c>）と下書きの後の必須（「初期解生成 完了」と同じ数）。件数そのまま＝単位の表記ではない。</summary>
    public static string DraftReplaceLine(long hardBefore, int hardAfter) => $"下書きで置き換え（確認済み）: 必須 {hardBefore}件→{hardAfter}件";

    /// <summary>起点が対象も職員も持たないとき（ホームの「変更案を探す」）。</summary>
    public const string FixOriginAll = "全体";
    public static string FixSearchStartLine(string origin) => $"案を探す: {origin}";
    public static string FixSearchEndLine(int count, long elapsedMs) => count > 0 ? $"案: {count}件（{Sec(elapsedMs)}秒）" : $"案: なし（{Sec(elapsedMs)}秒）";

    public static string PreviewOpenLine(string label) => $"変更の確認: 開く {label}";
    public static string PreviewDismissLine(string label) => $"変更の確認: 今は変更しない {label}";
    public static string ConsultKeepLine(string label) => $"相談に残す: {label}";
    /// <summary>「改善手を適用」の末尾＝変更の確認を開いてから当てるまで。開いていなければ呼ばない。</summary>
    public static string WithSeenSeconds(string line, long elapsedMs) => $"{line}{Sep}案を見てから {Sec(elapsedMs)}秒";

    private const string Sep = " ・ ";
    private static long Sec(long ms) => ms / 1000;

    public const string HesitationHead = "==== 操作の要約（迷いの指標）====";
    private const string None = "—";
    /// <summary>「結果の直後」＝「最適化 完了」からこの時間内の「下書きで置き換え（確認済み）」。</summary>
    public const long RightAfterMs = 60_000L;

    /// <summary>操作ログの行から数えた迷いの指標。<see cref="HesitationOf"/> が組む（Kotlin <c>hesitation()</c>。C# は型と同名の関数を置けない）。</summary>
    public sealed record Hesitation(
        int ManualChanges, int Undo, int Redo,
        int Searches, int SearchesWithResult, int SearchesEmpty, int Applied, int NotNow, int Consulted,
        int AutoRuns, int Drafts, int DraftsRightAfterResult,
        int ClosedWithoutApply,
        long? LongestGapMs, long LongestGapFromMs, long LongestGapToMs,
        long? MedianSeenSec)
    {
        public IReadOnlyList<string> Lines()
        {
            static string Hm(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
            return new[]
            {
                $"手で変更 {ManualChanges}回{Sep}元に戻す {Undo}回{Sep}やり直し {Redo}回",
                $"案を探した {Searches}回（案あり {SearchesWithResult}・なし {SearchesEmpty}）{Sep}適用 {Applied}回{Sep}今は変更しない {NotNow}回{Sep}相談に残す {Consulted}回",
                $"自動作成 {AutoRuns}回{Sep}下書き {Drafts}回（結果の直後 {DraftsRightAfterResult}回）",
                $"確認の画面を閉じた（適用なし）{ClosedWithoutApply}回",
                LongestGapMs is null ? $"最長の空白 {None}" : $"最長の空白 {LongestGapMs.Value / 60_000}分（{Hm(LongestGapFromMs)}〜{Hm(LongestGapToMs)}）",
                $"案を見てから適用までの中央値 {(MedianSeenSec is { } m ? $"{m}秒" : None)}",
            };
        }
    }

    private static readonly Regex SeenSecRe = new(@"案を見てから (\d+)秒", RegexOptions.Compiled);

    /// <summary><paramref name="lines"/> は <c>OpLogEntry.Message</c>（時刻・レベルの接頭辞なし）、<paramref name="times"/> は同じ添字の時刻。順は問わない。</summary>
    public static Hesitation HesitationOf(IReadOnlyList<string> lines, IReadOnlyList<long> times)
    {
        if (lines.Count != times.Count) throw new ArgumentException("lines and times must have the same length");
        var order = Enumerable.Range(0, lines.Count).OrderBy(i => times[i]).ToList();
        var msgs = order.Select(i => lines[i]).ToList();
        var ts = order.Select(i => times[i]).ToList();
        var lastResultAt = long.MinValue;
        var rightAfter = 0;
        for (var i = 0; i < msgs.Count; i++)
        {
            if (msgs[i].StartsWith("最適化 完了", StringComparison.Ordinal)) lastResultAt = ts[i];
            if (msgs[i].StartsWith("下書きで置き換え（確認済み）", StringComparison.Ordinal) && lastResultAt != long.MinValue && ts[i] - lastResultAt <= RightAfterMs) rightAfter++;
        }
        long? gap = null; var gapFrom = 0L; var gapTo = 0L;
        for (var i = 1; i < ts.Count; i++) { var d = ts[i] - ts[i - 1]; if (gap is null || d > gap) { gap = d; gapFrom = ts[i - 1]; gapTo = ts[i]; } }
        var seen = msgs.Where(m => m.StartsWith("改善手を適用:", StringComparison.Ordinal))
            .Select(m => SeenSecRe.Match(m)).Where(m => m.Success)
            .Select(m => long.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : (long?)null)
            .Where(v => v is not null).Select(v => v!.Value).OrderBy(v => v).ToList();
        long? median = seen.Count == 0 ? null : seen.Count % 2 == 1 ? seen[seen.Count / 2] : (seen[seen.Count / 2 - 1] + seen[seen.Count / 2]) / 2;
        int N(string prefix) => msgs.Count(m => m.StartsWith(prefix, StringComparison.Ordinal));
        return new Hesitation(
            ManualChanges: N("手で変更:"), Undo: N("元に戻す"), Redo: N("やり直し"),
            Searches: N("案を探す:"), SearchesWithResult: N("案: ") - N("案: なし"), SearchesEmpty: N("案: なし"),
            Applied: N("改善手を適用:"), NotNow: N("変更の確認: 今は変更しない"), Consulted: N("相談に残す:"),
            AutoRuns: N("最適化 開始"), Drafts: N("下書きで置き換え（確認済み）"), DraftsRightAfterResult: rightAfter,
            ClosedWithoutApply: N("人員不足の手伝い: 閉じる") + N("作成前の確認: 閉じる") + N("配布前の確認: 戻る"),
            LongestGapMs: gap, LongestGapFromMs: gapFrom, LongestGapToMs: gapTo,
            MedianSeenSec: median);
    }

    public static IReadOnlyList<string> HesitationSummary(IReadOnlyList<string> lines, IReadOnlyList<long> times) => HesitationOf(lines, times).Lines();
}
