using System.Globalization;

namespace MagiApp.ViewModels;

/// <summary>[3.694.0] 操作ログと書き出しに足す語の整形（Kotlin <c>OpLogText</c>）。VM の状態を読まない純関数だけを置き、
/// 形式はここで固定する（期間は ISO の日付・指紋は 16 桁の 16 進・小数は InvariantCulture）。</summary>
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
}
