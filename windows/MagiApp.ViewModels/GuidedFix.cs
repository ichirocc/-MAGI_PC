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
/// Section＝節キー（<c>yr_ws1</c>／<c>yr_cons</c>／<c>yr_count</c> は <c>EditView.ScrollToSection</c> が寄せる）、WishStaff＝希望で固定された本人。
/// 3.644.0: 「つくる前の確認」のセルを持たない行からも使う＝NeedShift（必要人数カレンダーで先に選ぶシフト）、CountStaff/CountShift（回数のマス。
/// Windows では個人の回数が「職員管理」の入口にあるため <see cref="GuidedFixRules.DoorFor"/> が入口を読み替える）、Label（ボタン文言の上書き）。
/// Day＝診断が指した日（希望の職員・必要人数のシフトと一緒に渡し、カレンダーでその日を選んだ状態で着地する。3.646.0 L01）。
/// </summary>
public sealed record EditLanding(int Scope, string? Section, int? WishStaff = null, int? NeedShift = null, int? CountStaff = null, int? CountShift = null, string? Label = null, int? Day = null);

/// <summary>
/// 編集タブへ着地したあと「元の確認へ戻る」ための呼出元（Kotlin <c>EditReturn</c>、3.646.0 L03）。Label＝見出し、Origin＝戻り先、Cell＝セルから来たとき。
/// 画面の状態＝保存しない。利用者が自分でタブを替えたら消える。
/// </summary>
public sealed record EditReturn(string Label, int Origin, (int I, int J)? Cell = null)
{
    public const int PreRun = 0;     // つくる前の確認（今のデータで作り直して出す）
    public const int Guided = 1;     // なおし方（人員不足の案内）
    public const int Analysis = 2;   // 分析タブの一覧
    public const int CellOrigin = 3; // 勤務表のセルのシート

    public static string Line(EditReturn r) => $"「{r.Label}」から来ました。直したら元の確認へ戻れます。";
    public const string ButtonText = "元の確認へ戻る";
}

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

    /// <summary>原因に対応する着地先。原因が特定できない不足は、その日のそのシフトの必要人数（旧 null＝編集タブを開くだけ。3.646.0 L01/L04）。</summary>
    public static EditLanding LandingFor(CoverageShortfall sf)
    {
        if (sf.WishPinned.Count > 0) return new EditLanding(0, null, sf.WishPinned[0], Day: sf.DayIndex);
        if (sf.Verdict == CoverageVerdict.Infeasible) return new EditLanding(2, "yr_ws1");
        if (sf.BlockedNow && sf.ForbidCount > 0) return new EditLanding(2, "yr_cons");
        return new EditLanding(0, null, NeedShift: sf.ShiftIndex, Day: sf.DayIndex);
    }

    public static string LandingButtonLabel(EditLanding? landing) => landing switch
    {
        { Label: { } l } => l,
        { WishStaff: not null } => "希望を見直す",
        { NeedShift: not null } => "必要人数を見直す",
        { Section: "yr_count" } => "回数の下限・上限を見直す",
        { Section: "yr_cons" } => "禁止の並びを見直す",
        { Section: "yr_ws1" } => "担当を見直す",
        _ => "データを見直す",
    };

    public const string LandingZeroCap = "入れない指定を見直す";
    public const string LandingWindow = "期間の制約を見直す";

    /// <summary>Windows の編集タブの入口。個人の回数（Android の ③ <c>yr_count</c>＝年間マスター）はこちらでは「職員管理」(1) にある。</summary>
    public static int DoorFor(EditLanding l) => l.Section == "yr_count" ? 1 : l.Scope;

    /// <summary>
    /// 証明つきの矛盾（コア）のうち希望を含まないものの着地先（Kotlin <c>landingForProofCore</c>）。上限0が原因とされた行は上限0のマスへ、
    /// そうでなければ必要人数（月次条件）→ 回数のマス → 期間の制約の順。どれも無ければ null（行は文だけ）。
    /// </summary>
    public static EditLanding? LandingForProofCore(IReadOnlyList<ConstraintMus.Item> core, bool zeroCap)
    {
        var zero = core.OfType<ConstraintMus.RangeCap>().FirstOrDefault(c => c.Hi == 0);
        if (zeroCap && zero is not null) return new EditLanding(2, "yr_count", CountStaff: zero.Staff, CountShift: zero.Shift, Label: LandingZeroCap);
        if (core.OfType<ConstraintMus.DayNeed>().FirstOrDefault() is { } need) return new EditLanding(0, null, NeedShift: need.Shift, Day: need.Day);
        if (core.OfType<ConstraintMus.RangeCap>().FirstOrDefault() is { } cap) return new EditLanding(2, "yr_count", CountStaff: cap.Staff, CountShift: cap.Shift);
        if (core.OfType<ConstraintMus.RangeFloor>().FirstOrDefault() is { } floor) return new EditLanding(2, "yr_count", CountStaff: floor.Staff, CountShift: floor.Shift);
        if (core.OfType<ConstraintMus.WindowRule>().Any()) return new EditLanding(2, "yr_cons", Label: LandingWindow);
        return null;
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
