using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>[Android 3.727.1] 勤務表 CSV（重ね合わせ）を反映する前の確認（Kotlin <c>CsvImportPreview.kt</c>、ユーザー提示のスマホ設計案「読み込み内容を確認」）。</summary>
public static class CsvImportPreview
{
    public const string CsvPreviewTitle = "この内容で取り込みますか？";
    public const string CsvPreviewConfirm = "取り込む";
    public const string CsvPreviewCancel = "取り込まない";

    /// <summary>確認の行（確認ダイアログは 3 点まで＝<see cref="CellSheetLogic.ConfirmMaxPoints"/>）: 取り込む人数と変わるセル／必須違反の前後／注意（あれば 1 行にまとめる）。</summary>
    public static IReadOnlyList<string> CsvImportPreviewLines(ScheduleRunResult res, int total, int[][] baseBoard, int hardNow)
    {
        var changed = 0;
        for (var i = 0; i < baseBoard.Length; i++)
            for (var j = 0; j < baseBoard[i].Length; j++)
                if (i < res.Schedule.Length && j < res.Schedule[i].Length && res.Schedule[i][j] != baseBoard[i][j]) changed++;
        var skipped = total - res.Matched;
        var notes = new List<string>();
        if (skipped > 0) notes.Add($"{skipped}名は氏名が合わずスキップ");
        if (res.UnknownCells > 0) notes.Add($"読めない記号 {res.UnknownCells}セル（{string.Join("・", res.UnknownSymbols)}）は取り込まない");
        if (res.HeaderDateMismatches > 0) notes.Add($"見出しの日付が今の期間と{res.HeaderDateMismatches}列ズレている（列の位置で取り込む）");
        if (res.AmbiguousNames.Count > 0) notes.Add($"同じ名前の職員が複数いる{string.Join("・", res.AmbiguousNames.Select(n => $"{n}さん"))}の行は取り込まない");
        if (res.DuplicateRowNames.Count > 0) notes.Add($"同じ職員の行が複数ある（後の行で上書き）: {string.Join("・", res.DuplicateRowNames)}");
        var lines = new List<string>
        {
            $"{res.Matched}/{total}名の勤務を取り込みます（変わるセル {changed}）",
            $"必須違反：{hardNow} → {res.Report.Hard}",
        };
        if (notes.Count > 0) lines.Add("⚠ " + string.Join("／", notes));
        return lines;
    }
}
