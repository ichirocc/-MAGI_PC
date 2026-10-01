using System.Collections.Generic;
using System.Linq;
using MagiEngine;
using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>設定の見直しカードのワンタップ修正の純粋部分（状態 → 新しい状態 / 何も変えないなら null）。ViewModel は適用とログだけ持つ。Kotlin <c>SettingFixLogic</c> の移植。</summary>
public static class SettingFixLogic
{
    public static MagiState? Apply(MagiState s, SettingIssue issue)
    {
        switch (issue.Action)
        {
            case SettingFixAction.RemoveWish:
            {
                var key = issue.WishKey;
                if (key is null) return null;
                if (!s.Wishes.ContainsKey(key)) return null;
                var d = new Dictionary<string, int>(s.Wishes);
                d.Remove(key);
                return s with { Wishes = d };
            }
            case SettingFixAction.DeleteDupSeq:
            {
                var fam = issue.SeqFamily;
                var key = issue.SeqKey;
                if (fam is null || key is null) return null;
                // 診断と同じ鍵（最初の空白まで・詰めない）。一致する最初の1行だけ消し、無ければ何も変えない（null）。
                static List<C3Row>? DelOne(IReadOnlyList<C3Row> rows, string key)
                {
                    var i = -1;
                    for (var n = 0; n < rows.Count; n++)
                        if (V6SanityPort.C3SeqKey(rows[n].Pattern) == key) { i = n; break; }
                    return i < 0 ? null : rows.Where((_, idx) => idx != i).ToList();
                }
                switch (fam)
                {
                    case "c3": { var r = DelOne(s.Cons3, key); return r is null ? null : s with { Cons3 = r }; }
                    case "c3n": { var r = DelOne(s.Cons3n, key); return r is null ? null : s with { Cons3n = r }; }
                    case "c3m": { var r = DelOne(s.Cons3m, key); return r is null ? null : s with { Cons3m = r }; }
                    case "c3mn": { var r = DelOne(s.Cons3mn, key); return r is null ? null : s with { Cons3mn = r }; }
                    default: return null;
                }
            }
            case SettingFixAction.ZeroRangeLo:
            case SettingFixAction.ClampRangeLo:
            {
                var key = issue.RangeKey;
                if (key is null) return null;
                var cur = s.StaffRange.TryGetValue(key, out var r) ? r : new MagiEngine.Model.Range("", "");
                var m = new Dictionary<string, MagiEngine.Model.Range>(s.StaffRange)
                {
                    [key] = new MagiEngine.Model.Range(issue.NewLo ?? cur.Lo, cur.Hi),
                };
                return s with { StaffRange = m };
            }
            case SettingFixAction.ClampGroupRangeLo:
            {
                // 行は List なので index でなく**内容一致**で指す（DeleteDupSeq と同じ理由＝診断から
                //   タップまでに並びが変わっても別の行を壊さない）。同じ内容が複数あるときは先頭1件だけ直す。
                var row = issue.GroupRangeRow;
                var lo = issue.NewLo;
                if (row is null || lo is null) return null;
                static List<C41Row> ClampOne(IReadOnlyList<C41Row> rows, C41Row row, string lo)
                {
                    var list = rows.ToList();
                    var i = list.IndexOf(row);
                    if (i < 0) return list;
                    list[i] = row with { L = lo };
                    return list;
                }
                return issue.GroupRangeFamily switch
                {
                    "c41" => s with { Cons41 = ClampOne(s.Cons41, row, lo) },
                    "c41s" => s with { Cons41s = ClampOne(s.Cons41s, row, lo) },
                    _ => null,
                };
            }
            case SettingFixAction.CapDemand:
            {
                var k = issue.DemandShiftIdx;
                var cap = issue.DemandCap;
                if (k is null || cap is null) return null;
                if (k.Value < 0 || k.Value >= s.Shifts.Count) return null;
                var sh = s.Shifts[k.Value];
                var j = issue.DemandDayIdx;
                if (j is not null)
                {
                    // 日付つきの不足はその日の例外だけを書く（標準の必要数を下げると例外の無い他の日まで動く）。
                    //   P2 は実際に使っていて（Use2Patterns）その日の実効値が上限を超えるときだけ。
                    var key = $"{k.Value},{j.Value}";
                    int? Eff(IReadOnlyDictionary<string, string> map, string std) =>
                        map.TryGetValue(key, out var v) && KotlinInterop.ToIntOrNull(v.Trim()) is int o ? o : KotlinInterop.ToIntOrNull(std.Trim());
                    var e1 = Eff(s.NeedDay1, sh.Need1);
                    var e2 = Eff(s.NeedDay2, sh.Need2);
                    var w1 = e1 is int a && a > cap.Value;
                    var w2 = s.Use2Patterns && e2 is int b && b > cap.Value;
                    if (!w1 && !w2) return null;
                    static IReadOnlyDictionary<string, string> With(IReadOnlyDictionary<string, string> m, string key, string v) =>
                        new Dictionary<string, string>(m) { [key] = v };
                    return s with
                    {
                        NeedDay1 = w1 ? With(s.NeedDay1, key, cap.Value.ToString()) : s.NeedDay1,
                        NeedDay2 = w2 ? With(s.NeedDay2, key, cap.Value.ToString()) : s.NeedDay2,
                    };
                }
                else
                {
                    var n1 = KotlinInterop.ToIntOrNull(sh.Need1.Trim());
                    var n2 = KotlinInterop.ToIntOrNull(sh.Need2.Trim());
                    var newN1 = n1 is int a && a > cap.Value ? cap.Value.ToString() : sh.Need1;
                    var newN2 = n2 is int b && b > cap.Value ? cap.Value.ToString() : sh.Need2;
                    if (newN1 == sh.Need1 && newN2 == sh.Need2) return null;
                    var list = s.Shifts.ToList();
                    list[k.Value] = sh with { Need1 = newN1, Need2 = newN2 };
                    return s with { Shifts = list };
                }
            }
            case SettingFixAction.None:
            default:
                return null;
        }
    }
}
