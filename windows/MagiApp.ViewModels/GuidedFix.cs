using System.Collections.Generic;
using System.Linq;
using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>
/// 「なおし方を見る」の判断部分（Kotlin原本 <c>GuidedFixDialog</c> の分岐、3.401.0）。WinUI から切り離してテストする。
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
        var target = GuidedFixRules.GuidedFixTarget(shortfalls);
        var blocked = shortfalls.Where(sf => sf.Miss > 0 && sf.BlockedNow && sf.Verdict != CoverageVerdict.Infeasible).ToList();
        var infeasible = shortfalls.Where(sf => sf.Verdict == CoverageVerdict.Infeasible).ToList();
        return new GuidedFixPlan(target, blocked, infeasible);
    }
}

/// <summary>
/// 原因に対応する設定の着地先（Kotlin原本 <c>EditLanding</c>、3.642.0）。Scope＝編集タブの入口（0=月次条件／1=職員管理／2=年間マスター）、
/// Section＝節キー（ラベルの出し分けに使う。Windows の編集タブは節へ移動しないため、着地は入口まで）、WishStaff＝希望で固定された本人。
/// </summary>
public sealed record EditLanding(int Scope, string? Section, int? WishStaff = null);

/// <summary>
/// 「なおし方を見る」のホームとダイアログが共有する判断（Kotlin原本 <c>guidedFixTarget</c>/<c>landingFor</c>/<c>landingButtonLabel</c>、3.642.0）。
/// 対象枠をここ1か所で決め、ホームの文言とダイアログが必ず同じ枠を指すようにする。
/// </summary>
public static class GuidedFixRules
{
    /// <summary>候補の既定表示件数。超えたら「すべて表示」で9人目以降も選べる（Kotlin原本 <c>GUIDED_FIX_PREVIEW</c>）。</summary>
    public const int GuidedFixPreview = 8;

    /// <summary>対象枠＝Fixable かつ miss&gt;0 かつ BlockedNow でない最初の枠。</summary>
    public static CoverageShortfall? GuidedFixTarget(IEnumerable<CoverageShortfall> shortfalls) =>
        shortfalls.FirstOrDefault(sf => sf.Verdict == CoverageVerdict.Fixable && sf.Miss > 0 && !sf.BlockedNow);

    /// <summary>原因に対応する着地先。null＝原因が分からない＝編集タブの先頭（従来どおり）。</summary>
    public static EditLanding? LandingFor(CoverageShortfall sf)
    {
        if (sf.WishPinned.Count > 0) return new EditLanding(0, null, sf.WishPinned[0]);
        if (sf.Verdict == CoverageVerdict.Infeasible) return new EditLanding(2, "yr_ws1");
        if (sf.BlockedNow && sf.ForbidCount > 0) return new EditLanding(2, "yr_cons");
        return null;
    }

    public static string LandingButtonLabel(EditLanding? landing) => landing switch
    {
        { WishStaff: not null } => "希望を見直す",
        { Section: "yr_cons" } => "禁止の並びを見直す",
        { Section: "yr_ws1" } => "担当を見直す",
        _ => "データを見直す",
    };
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
