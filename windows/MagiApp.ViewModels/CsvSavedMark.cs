using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>
/// [3.644.0/UX-05・仕様 5.4] 勤務表 CSV の「保存済み」の印。書き出した文字列そのものと、その時の state・盤面の写しを持つ（Android 3.727.1:
/// ハッシュ・64 ビットの指紋だけで「同じ」と言わない＝衝突すると違う内容を保存済みと言い得た）。保存の成功は
/// 書き出し時の文脈に結ぶ＝書き出しのあと（ピッカーや書き込みの間）に盤面が変わっても、変わった盤面を「保存済み」と言わない。Kotlin <c>CsvSavedMark</c>。
/// </summary>
public sealed class CsvSavedMark
{
    private sealed record Ctx(MagiState St, int[][] Board, string Text);

    private readonly Func<MagiState, int[][], string> _buildCsv;
    private Ctx? _exportCtx;
    private Ctx? _savedCtx;
    private (MagiState St, int[][] Board)? _checkKey;
    private string? _checkText;

    public CsvSavedMark(Func<MagiState, int[][], string>? buildCsv = null) => _buildCsv = buildCsv ?? ScheduleCsvBridge.Build;

    public void NoteExport(MagiState st, int[][] sched, string csv) => _exportCtx = new Ctx(st, sched.Copy2D(), csv);

    /// <summary>保存の成功。書き出した文字列の文脈に結ぶ。無ければ今の内容（旧経路の互換）。</summary>
    public void NoteSaved(MagiState? st, int[][]? sched) =>
        _savedCtx = _exportCtx ?? (st is not null && sched is not null ? new Ctx(st, sched.Copy2D(), _buildCsv(st, sched)) : null);

    /// <summary>別のデータや別の月に移ったら、前の CSV の保存は今の内容と関係がない＝印を消す。</summary>
    public void Clear() { _exportCtx = null; _savedCtx = null; }

    /// <summary>今の内容が保存した CSV と同じか（保存の印が無ければ null）。state と盤面が同じなら作らずに同じ、違えば CSV をもう一度作って比べる
    /// （設定だけの変更は CSV を変えない）。</summary>
    public bool? SavedMatches(MagiState? st, int[][]? sched)
    {
        if (_savedCtx is not { } c) return null;
        if (st is null || sched is null) return false;
        if (ReferenceEquals(c.St, st) && SameBoard(c.Board, sched)) return true;
        if (_checkKey is not { } k || !ReferenceEquals(k.St, st) || !SameBoard(k.Board, sched))
        {
            _checkKey = (st, sched.Copy2D());
            _checkText = _buildCsv(st, sched);
        }
        return _checkText == c.Text;
    }

    private static bool SameBoard(int[][] a, int[][] b) => a.Length == b.Length && a.Zip(b).All(r => r.First.AsSpan().SequenceEqual(r.Second));

    /// <summary>保存した内容と今の内容を区別する 1 行（保存の印または時刻が無ければ null）。</summary>
    public string? Line(string? at, MagiState? st, int[][]? sched)
    {
        if (at is null) return null;
        if (SavedMatches(st, sched) is not { } m) return null;
        return m ? $"この内容で勤務表CSVを保存済みです（{at}）。" : $"{at} に保存した勤務表CSVは、今の内容と違います（配るなら保存し直してください）。";
    }
}
