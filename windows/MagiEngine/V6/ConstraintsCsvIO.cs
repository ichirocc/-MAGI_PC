using System.Linq;
using MagiEngine.Model;
// System.Range (built-in C# 8+ slice type) collides by simple name with MagiEngine.Model.Range.
using Range = MagiEngine.Model.Range;

namespace MagiEngine.V6;

/// <summary>
/// [フェーズ7ピース11] Kotlin原本 <c>ConstraintsCsvIO</c>（<c>ScheduleCsvBridge.kt</c> 731〜842行）の移植。
///
/// 各制約: 種別タグ付き行（種別,a,b,c,d,e）。取込時は制約一式＋個人レンジを置換。
/// 氏名/群/シフトは記号・氏名で照合。
/// </summary>
public static class ConstraintsCsvIO
{
    private static string Cell(IReadOnlyList<string> r, int idx) => (idx >= 0 && idx < r.Count ? r[idx] : "").Trim();

    public static string Build(MagiState state)
    {
        var sb = new System.Text.StringBuilder();
        CsvUtil.AppendCsvRow(sb, new List<string> { "種別", "a", "b", "c", "d", "e" });
        foreach (var c in state.Cons1) CsvUtil.AppendCsvRow(sb, new List<string> { "連勤", c.Day1, c.ShiftKigou, c.Day2 });
        foreach (var c in state.Cons2) CsvUtil.AppendCsvRow(sb, new List<string> { "回数下限", c.ShiftKigou, c.Count });
        foreach (var c in state.Cons3) CsvUtil.AppendCsvRow(sb, Prepend("MUST連続", c.Pattern));
        foreach (var c in state.Cons3n) CsvUtil.AppendCsvRow(sb, Prepend("禁止連続", c.Pattern));
        foreach (var c in state.Cons3m) CsvUtil.AppendCsvRow(sb, Prepend("希望連続", c.Pattern));
        foreach (var c in state.Cons3mn) CsvUtil.AppendCsvRow(sb, Prepend("回避連続", c.Pattern));
        foreach (var c in state.Cons41) CsvUtil.AppendCsvRow(sb, new List<string> { "群回数", c.GroupKigou, c.ShiftKigou, c.L, c.U });
        foreach (var c in state.Cons41s) CsvUtil.AppendCsvRow(sb, new List<string> { "スキル群回数", c.GroupKigou, c.ShiftKigou, c.L, c.U });
        foreach (var c in state.Cons42) CsvUtil.AppendCsvRow(sb, new List<string> { "群組合せ禁止", c.G1Kigou, c.S1Kigou, c.G2Kigou, c.S2Kigou });
        foreach (var c in state.Cons42s) CsvUtil.AppendCsvRow(sb, new List<string> { "スキル群組合せ禁止", c.G1Kigou, c.S1Kigou, c.G2Kigou, c.S2Kigou });
        foreach (var kv in state.StaffRange)
        {
            var p = kv.Key.Split(',');
            var i = p.Length > 0 ? KotlinInterop.ToIntOrNull(p[0]) : null;
            var k = p.Length > 1 ? KotlinInterop.ToIntOrNull(p[1]) : null;
            if (i is null || k is null) continue;
            if (i < 0 || i >= state.StaffList.Count) continue;
            var name = state.StaffList[i.Value].Name;
            if (k < 0 || k >= state.Shifts.Count) continue;
            var sym = state.Shifts[k.Value].Kigou;
            CsvUtil.AppendCsvRow(sb, new List<string> { "個人レンジ", name, sym, kv.Value.Lo, kv.Value.Hi });
        }
        return sb.ToString();
    }

    private static List<string> Prepend(string tag, IReadOnlyList<string> pattern)
    {
        var list = new List<string>(pattern.Count + 1) { tag };
        list.AddRange(pattern);
        return list;
    }

    /// <returns>更新後stateと取込件数を持つ <see cref="ComponentImport"/>、または null（解析不能/0件）。</returns>
    public static ComponentImport? Parse(string text, MagiState state)
    {
        // [3.413.0/I-08] 引用符が閉じないCSVは残りの行が丸ごと消える＝**全置換の取込では
        //   「消えた」ことが取込結果からは分からない**。書式の誤りとして断る。
        var parsed0 = CsvUtil.ParseCsvFull(text);
        if (parsed0.UnclosedQuote) return null;
        var rows = parsed0.Rows;
        if (rows.Count == 0) return null;
        var nameToI = CsvUtil.FirstWinsMap(state.StaffList.Count, i => CsvUtil.NameMatchKey(state.StaffList[i].Name));

        // [3.336.0/外部レビュー P2] 空セルで打ち切るので `MUST連続,A,,B` は ["A"] になり、**B が黙って
        //   消えたまま accepted に数えられた**（3.333.0 で他の族に入れた「評価されない行を受理しない」
        //   の取り残し）。穴が空いた行は書式の誤りとして呼び出し側で弾けるよう、別に判定する。
        static List<string> Pat(IReadOnlyList<string> r)
        {
            var result = new List<string>();
            for (var idx = 1; idx <= 5; idx++)
            {
                var v = Cell(r, idx);
                if (v.Length == 0) break;
                result.Add(v);
            }
            return result;
        }

        /// <summary>途中に空セルがあり、その後ろにまだ中身がある＝並びが途切れている（書式の誤り）。</summary>
        static bool PatHasGap(IReadOnlyList<string> r)
        {
            var cells = new string[5];
            for (var idx = 0; idx < 5; idx++) cells[idx] = Cell(r, idx + 1);
            var last = -1;
            for (var idx = 0; idx < 5; idx++) if (cells[idx].Length != 0) last = idx;
            if (last < 0) return false;
            for (var idx = 0; idx < last; idx++) if (cells[idx].Length == 0) return true;
            return false;
        }

        var cons1 = new List<C1Row>(); var cons2 = new List<C2Row>();
        var cons3 = new List<C3Row>(); var cons3n = new List<C3Row>();
        var cons3m = new List<C3Row>(); var cons3mn = new List<C3Row>();
        var cons41 = new List<C41Row>(); var cons41s = new List<C41Row>();
        var cons42 = new List<C42Row>(); var cons42s = new List<C42Row>();
        var ranges = new Dictionary<string, Range>();
        var n = 0;
        // [3.314.0] ヘッダ判定を Build() が出す実ヘッダ「種別」の一致へ（旧: 既知キーワード集合との
        //   照合で、キーワードを増やすたびに取込側も直す必要があった）。
        var body = CsvUtil.CsvBody(rows, "種別");
        var bad = 0;
        var sample = "";
        void Reject(IReadOnlyList<string> r)
        {
            bad++;
            if (sample.Length == 0) sample = string.Join(",", r).Take(60);
        }
        foreach (var r in body)
        {
            if (r.All(cell => string.IsNullOrWhiteSpace(cell))) continue; // 書式上の空行は無視
            switch (Cell(r, 0))
            {
                case "連勤": cons1.Add(new C1Row(Cell(r, 1), Cell(r, 2), Cell(r, 3))); n++; break;
                case "回数下限": cons2.Add(new C2Row(Cell(r, 1), Cell(r, 2))); n++; break;
                case "MUST連続":
                {
                    var p = Pat(r);
                    if (p.Count > 0 && !PatHasGap(r)) { cons3.Add(new C3Row(p)); n++; } else Reject(r);
                    break;
                }
                case "禁止連続":
                {
                    var p = Pat(r);
                    if (p.Count > 0 && !PatHasGap(r)) { cons3n.Add(new C3Row(p)); n++; } else Reject(r);
                    break;
                }
                case "希望連続":
                {
                    var p = Pat(r);
                    if (p.Count > 0 && !PatHasGap(r)) { cons3m.Add(new C3Row(p)); n++; } else Reject(r);
                    break;
                }
                case "回避連続":
                {
                    var p = Pat(r);
                    if (p.Count > 0 && !PatHasGap(r)) { cons3mn.Add(new C3Row(p)); n++; } else Reject(r);
                    break;
                }
                case "群回数": cons41.Add(new C41Row(Cell(r, 1), Cell(r, 2), Cell(r, 3), Cell(r, 4))); n++; break;
                case "スキル群回数": cons41s.Add(new C41Row(Cell(r, 1), Cell(r, 2), Cell(r, 3), Cell(r, 4))); n++; break;
                case "群組合せ禁止": cons42.Add(new C42Row(Cell(r, 1), Cell(r, 3), Cell(r, 2), Cell(r, 4))); n++; break;
                case "スキル群組合せ禁止": cons42s.Add(new C42Row(Cell(r, 1), Cell(r, 3), Cell(r, 2), Cell(r, 4))); n++; break;
                case "個人レンジ":
                {
                    var hasI = nameToI.TryGetValue(CsvUtil.NameMatchKey(Cell(r, 1)), out var i);
                    var sym = Cell(r, 2);
                    var k = -1;
                    for (var idx = 0; idx < state.Shifts.Count; idx++)
                    {
                        if (state.Shifts[idx].Kigou.Trim() == sym) { k = idx; break; }
                    }
                    // [3.329.0/外部レビュー H-02] 氏名・記号が今のデータに無い行は黙って捨てない。
                    //   捨てたまま置換すると、その職員の個人レンジが**消える**。
                    if (hasI && k >= 0) { ranges[$"{i},{k}"] = new Range(Cell(r, 3), Cell(r, 4)); n++; }
                    else Reject(r);
                    break;
                }
                // 未知の種別も黙って捨てない（種別の綴り違いで制約一式が消えるのを防ぐ）。
                default: Reject(r); break;
            }
        }
        if (n == 0 && bad == 0) return null;
        var candidate = state with
        {
            Cons1 = cons1, Cons2 = cons2, Cons3 = cons3, Cons3n = cons3n,
            Cons3m = cons3m, Cons3mn = cons3mn, Cons41 = cons41, Cons41s = cons41s,
            Cons42 = cons42, Cons42s = cons42s, StaffRange = ranges,
        };
        // [3.333.0/外部レビュー Critical] 種別が既知なだけの行を**無条件に受理**していた。
        //   例えば `連勤,,,` は C1Row("","","") として n に数えられ、Problem は
        //   `d1>0 && si>=0 && d2>0` で捨てる＝**評価されない行で既存の有効な制約を全置換**できた
        //   （実質「制約なし」で最適化される）。3.329.0 の中止条件は未知の氏名・記号しか見ておらず、
        //   構造的に空/不正な行を通していた。
        //
        //   判定は Problem を単一ソースにする（各族の条件をここへ複製すると必ずドリフトする）。
        //   この取込は制約族を**すべて置換**するので、候補stateの未解決行は必ずこのCSV由来。
        //   連続パターン(cons3系)の未解決記号は別のリスト(C3UnknownShift)に入るので両方見る。
        IReadOnlyList<(string Family, string Text)> unresolved;
        try
        {
            var pc = new Problem(candidate);
            unresolved = pc.UnresolvedRows.Concat(pc.C3UnknownShift).ToList();
        }
        catch (Exception)
        {
            unresolved = Array.Empty<(string, string)>();
        }
        if (unresolved.Count > 0)
        {
            bad += unresolved.Count;
            if (sample.Length == 0)
            {
                var first = unresolved[0];
                sample = $"{first.Family}「{first.Text}」".Take(60);
            }
        }
        return new ComponentImport(candidate, n, bad, sample);
    }
}
