using System;
using System.Collections.Generic;
using System.Linq;
using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>上段/中段に出す1行。Count は族の件数、weekly/fair だけ L1 偏差なので Unit が "pt" になる。</summary>
/// <param name="Family">族キー（<see cref="MirrorKeys.All"/>）。settingIssues 由来の行は null。</param>
/// <param name="Promoted">診断が「データを直さない限り消えない」と判定して上段へ上げた行か。</param>
/// <param name="Staff">タップで修復フローへ渡す職員（null=全体探索 or 導線なし）。</param>
public sealed record TriageRow(string Label, int Count, string Unit, string Detail, string? Family = null, bool Promoted = false, int? Staff = null);

/// <summary>
/// [3.471.0/分析タブ再構築] 分析タブの1画面トリアージを組み立てる UI 非依存の純関数（Kotlin原本 <c>AnalysisTriage.kt</c>）。
/// 分類の軸は「族が HARD か SOFT か」ではなく「データを直さない限り消えないか」。判定は新しいロジックを作らず、
/// 既にある診断の結論（settingIssues / forbiddenDiag / c1Plateau / coverageDiag）だけを読む。
/// 実行前は c1Plateau/forbiddenDiag が無いので SOFT 族は「エンジン探索対象（未計算）」であって「手動修正は不要」ではない。
/// 表示のみ・読み取り専用＝スコアリング/エンジンは一切不変。
/// </summary>
/// <param name="Blockers">上段: 必須違反＋診断が構造的な壁と判定した族。</param>
/// <param name="Issues">上段: 設定の破綻（settingIssues を種類ごとに集約）。</param>
/// <param name="Searching">中段: エンジンが挑戦する残りの族。</param>
/// <param name="OkFamilies">下段: 0件の族（ゼロサプレッションして畳む）。</param>
/// <param name="BusyFamilies">下段: 残っている族。</param>
/// <param name="SearchNote">中段の注記（断定しない文言）。</param>
public sealed record AnalysisTriage(
    bool Computed,
    IReadOnlyList<TriageRow> Blockers,
    IReadOnlyList<TriageRow> Issues,
    IReadOnlyList<TriageRow> Searching,
    IReadOnlyList<string> OkFamilies,
    IReadOnlyList<string> BusyFamilies,
    string SearchNote)
{
    public bool HasAnything => Blockers.Count > 0 || Issues.Count > 0 || Searching.Count > 0;

    /// <summary>weekly/fair は件数でなく L1 偏差の合計。単位を分けないと「186件」と読めてしまう。</summary>
    private static string UnitOf(string family) => family is "fair" or "weekly" ? "pt" : "件";

    /// <summary>SettingIssue の種類 → 画面に出す見出し（英字符号を出さない）。</summary>
    private static string IssueKindLabel(IssueKind kind) => kind switch
    {
        IssueKind.Wish => "希望の設定",
        IssueKind.Constraint => "決まりの設定",
        IssueKind.Demand => "必要人数の設定",
        IssueKind.Range => "回数の設定",
        _ => kind.ToString(),
    };

    /// <summary>同種を1行に畳む＝「古泉・Dﾃ」「山本・Dﾃ」…を「回数の設定 8件（古泉/山本 ほか）」にする。名前は先頭2件＋「ほか」まで。</summary>
    private static List<TriageRow> AggregateIssues(IReadOnlyList<SettingIssue> issues) =>
        issues.GroupBy(i => i.Kind)
            .Select(g =>
            {
                var list = g.ToList();
                var heads = string.Join("/", list.Take(2).Select(i => i.Where.Length > 18 ? i.Where[..18] : i.Where));
                var more = list.Count > 2 ? " ほか" : "";
                return new TriageRow(IssueKindLabel(g.Key), list.Count, "件", heads + more);
            })
            .OrderByDescending(r => r.Count)
            .ToList();

    /// <summary>上段へ上げてよい SOFT 族。診断が実際に観測・検証した結論だけを根拠にする（観測が無いときは上げない）。</summary>
    private static Dictionary<string, string> PromotedSoftFamilies(
        bool computed, C1PlateauDiagnosis? c1Plateau, CoverageDiagnosis? coverage, Func<string, string> labelOf)
    {
        var o = new Dictionary<string, string>();
        if (!computed) return o;
        if (c1Plateau is not null && c1Plateau.HasEntries)
        {
            var e = c1Plateau.Entries[0];
            o["c1"] = $"{e.Label}ほか{c1Plateau.Entries.Count}件 — {e.RecommendedAction(labelOf)}";
        }
        var stuckSurplus = coverage?.Surpluses.Where(s => s.BlockedFamily is not null).ToList() ?? new List<CoverageSurplus>();
        if (stuckSurplus.Count > 0)
        {
            var fam = labelOf(stuckSurplus[0].BlockedFamily!);
            o["covO"] = $"{stuckSurplus.Count}枠は動かす手が「{fam}」に負けて採用されません。{fam}の設定を緩めると動きます。";
        }
        return o;
    }

    /// <summary>必須違反の行に添える「なぜ残るか」。診断が無い/観測が無いときは空文字＝何も主張しない。</summary>
    private static string HardDetail(string family, CoverageDiagnosis? coverage, ForbiddenRunDiagnosis? forbidden) => family switch
    {
        "c3n" => forbidden is null || !forbidden.HasRuns ? ""
            : forbidden.AllBlocked ? $"この希望・担当のままでは崩せません（{forbidden.TotalRuns}件すべて）。"
            : forbidden.Runs.FirstOrDefault(r => !r.Escapable) is { } run ? $"「{run.SeqLabel}」は崩せません（{run.StaffName}）。"
            : "崩す手が残っています。",
        "covU" => coverage is null || !coverage.HasShortage ? ""
            : coverage.AllBlockedNow ? $"いまの希望のままでは、どう組んでも埋まりません（{coverage.TotalShortfall}人）。"
            : coverage.BlockedNowSlots > 0 ? $"{coverage.BlockedNowSlots}枠はいまの希望のままでは埋まりません。"
            : "担当を動かせば埋まる見込みです。",
        _ => "",
    };

    /// <param name="labelOf">族キー→表示名（下流の語彙＝AnalysisView.BreakdownLabels が正なので UI 層から受ける）。</param>
    public static AnalysisTriage Build(UiState ui, Func<string, string> labelOf)
    {
        // [3.475.0/論理監査] 「計算済み」はエンジンが走った事実で決める（HasResult は手編集でも立つ）。
        var computed = ui.EngineRan;
        var counts = ui.Breakdown;
        var promoted = PromotedSoftFamilies(computed, ui.C1Plateau, ui.CoverageDiag, labelOf);

        var blockers = new List<TriageRow>();
        var searching = new List<TriageRow>();
        var ok = new List<string>();
        var busy = new List<string>();

        // 重い順に並べる＝重み階層（groupViol > pref > covU > c3n > low > high > …）と表示順を一致させる。
        var hard = new HashSet<string>(MirrorKeys.Hard);
        foreach (var fam in MirrorKeys.All.OrderByDescending(MirrorKeys.WeightOf))
        {
            var n = counts.TryGetValue(fam, out var c) ? c : 0;
            if (n <= 0) { ok.Add(labelOf(fam)); continue; }
            busy.Add(labelOf(fam));
            var row = new TriageRow(labelOf(fam), n, UnitOf(fam), "", fam);
            if (hard.Contains(fam)) blockers.Add(row with { Detail = HardDetail(fam, ui.CoverageDiag, ui.ForbiddenDiag) });
            else if (promoted.TryGetValue(fam, out var why)) blockers.Add(row with { Detail = why, Promoted = true });
            else searching.Add(row);
        }

        var note = computed
            ? "計算後も残っている項目です。構造的に残ると判定されたものは上へ移しています。"
            : "実行前の概算です。期間の制約・禁止の並びなどの構造的な要因により、計算後も残る場合があります。";
        return new AnalysisTriage(computed, blockers, AggregateIssues(ui.SettingIssues), searching, ok, busy, note);
    }
}
