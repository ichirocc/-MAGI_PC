using System.Collections.Generic;
using System.Linq;
using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>
/// 「なおすのを手伝って」の判断部分（Kotlin原本 <c>GuidedFixDialog</c> の分岐、3.401.0）。WinUI から切り離してテストする。
/// Target＝Fixable かつ miss&gt;0 かつ BlockedNow でない最初の枠（BlockedNow は同じ画面の診断が「いまの希望のままでは埋まらない」と
/// 言っている枠＝ここで「動かせる人がいます」と言うと矛盾する）。Target が無くても Blocked/Infeasible が残るなら AllDone にしない。
/// </summary>
public sealed record GuidedFixPlan(
    CoverageShortfall? Target,
    IReadOnlyList<CoverageShortfall> Blocked,
    IReadOnlyList<CoverageShortfall> Infeasible)
{
    public bool AllDone => Target is null && Blocked.Count == 0 && Infeasible.Count == 0;
    public string Title => AllDone ? "直し終わりました！" : "なおすのを手伝います";

    public static GuidedFixPlan Build(CoverageDiagnosis? diag)
    {
        var shortfalls = diag?.Shortfalls ?? System.Array.Empty<CoverageShortfall>();
        var target = shortfalls.FirstOrDefault(sf => sf.Verdict == CoverageVerdict.Fixable && sf.Miss > 0 && !sf.BlockedNow);
        var blocked = shortfalls.Where(sf => sf.Miss > 0 && sf.BlockedNow && sf.Verdict != CoverageVerdict.Infeasible).ToList();
        var infeasible = shortfalls.Where(sf => sf.Verdict == CoverageVerdict.Infeasible).ToList();
        return new GuidedFixPlan(target, blocked, infeasible);
    }
}

/// <summary>
/// 候補ボタンの有効/無効の状態機械。押したら <see cref="UiState.CheckRev"/> が押下時より進む（＝押下後の再検査が反映された）まで
/// 全候補を無効にする。Schedule の変更だけでは解除しない（候補は押す前の盤面で「抜けても穴が空かない」と判定したもの）。
/// 閉じたあとは通知を無視する。
/// </summary>
public sealed class GuidedFixFlow
{
    private long _waitRev = -1;
    public bool Pending { get; private set; }
    public bool Closed { get; private set; }
    public bool CandidatesEnabled => !Pending && !Closed;

    /// <summary>候補を押した。<paramref name="checkRev"/>＝押下時点の <see cref="UiState.CheckRev"/>。</summary>
    public void Press(long checkRev) { if (Closed) return; Pending = true; _waitRev = checkRev; }

    /// <summary>盤面だけが変わった通知。無効のまま（true を返すのは組み直しが要るとき＝常に true、ただし閉じていれば false）。</summary>
    public bool OnScheduleChanged() => !Closed;

    /// <summary>検査結果が反映された通知。押下時より新しい世代なら候補を再有効化する。戻り値＝組み直しが要るか。</summary>
    public bool OnCheckReflected(long checkRev)
    {
        if (Closed) return false;
        if (Pending && checkRev > _waitRev) Pending = false;
        return true;
    }

    public void Close() { Closed = true; }
}
