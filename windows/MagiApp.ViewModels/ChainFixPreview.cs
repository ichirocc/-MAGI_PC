using System.Collections.Generic;
using System.Linq;
using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>
/// [3.644.0/UX-03] 複数人の入替（玉突き）を当てる前の一覧＝だれの・どの日の・何を何に変えるか（Kotlin <c>ChainFixPreview.kt</c>）。
/// 必須違反の増減は画面が同じ提案から <see cref="NextActionGuide.FixImpactLines"/> で出す（族名の日本語表は WinUI 側にあるため、Kotlin が
/// 持つ hardLine/caution はここでは持たない）。
/// </summary>
public sealed record ChainFixPreview(FixSuggestion Suggestion, string Title, IReadOnlyList<string> Changes)
{
    public static ChainFixPreview Of(FixSuggestion s, int[][] snapshot, IReadOnlyList<string> staffNames, IReadOnlyList<string> shiftSymbols, string startDate)
    {
        string Name(int i) => i >= 0 && i < staffNames.Count ? staffNames[i] : $"職員{i + 1}";
        string Sym(int k) => k >= 0 && k < shiftSymbols.Count ? shiftSymbols[k] : $"{k}";
        var changes = s.Ops.Select(op =>
        {
            var inRange = op.Staff >= 0 && op.Staff < snapshot.Length && op.Day >= 0 && op.Day < snapshot[op.Staff].Length;
            return $"{Name(op.Staff)} {DayText.Short(startDate, op.Day)} {(inRange ? Sym(snapshot[op.Staff][op.Day]) : "?")} → {Sym(op.ToShift)}";
        }).ToList();
        var people = s.Ops.Select(o => o.Staff).Distinct().Count();
        return new ChainFixPreview(s, $"複数人の入れ替え（{people} 人・{s.Ops.Count} マス）", changes);
    }
}
