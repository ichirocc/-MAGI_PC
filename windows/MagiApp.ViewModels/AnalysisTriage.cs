using System;
using System.Collections.Generic;
using System.Linq;
using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>上段/中段に出す1行。Count は族の件数、weekly/fair だけ L1 偏差なので Unit が "pt" になる。</summary>
/// <param name="Family">族キー（<see cref="MirrorKeys.All"/>）。settingIssues 由来の行は null。</param>
/// <param name="Promoted">診断が「データを直さない限り消えない」と判定して上段へ上げた行か。</param>
/// <param name="Staff">タップで修復フローへ渡す職員（null=全体探索 or 導線なし）。</param>
/// <param name="Kind">settingIssues 由来の行の種類（「設定へ」の着地先＝<see cref="VioBuckets.YearSectionForIssueKind"/>）。族の行は null。</param>
public sealed record TriageRow(string Label, int Count, string Unit, string Detail, string? Family = null, bool Promoted = false, int? Staff = null, IssueKind? Kind = null);

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
    /// <summary>族の数の単位。weekly/fair は L1 偏差なので "pt"、ほかは件（配布前確認も同じ単位を使う）。</summary>
    /// <summary>族の数え方（報告の内訳の単位）。回数の下限・上限・適切回数は回の差、人員は人の差、公平化・曜日の偏りは pt、ほかは件。</summary>
    public static string UnitOf(string family) => family switch
    {
        "fair" or "weekly" => "pt",
        "low" or "high" or "apt" => "回",
        "covU" or "covO" => "人",
        _ => "件",
    };

    private static readonly string[] UnitOrder = { "件", "人", "回", "pt" };

    /// <summary>単位ごとの合計（例「3件・2人・6回」）。単位の違う量は 1 つの数に足さない（統合仕様 INV-07）。全部 0 なら null。</summary>
    public static string? UnitTotals(IReadOnlyDictionary<string, int> breakdown, IEnumerable<string> families, bool includePt = true)
    {
        var by = families.GroupBy(UnitOf).ToDictionary(g => g.Key, g => g.Sum(f => breakdown.GetValueOrDefault(f, 0)));
        var parts = UnitOrder.Where(u => includePt || u != "pt")
            .Where(u => by.TryGetValue(u, out var n) && n > 0).Select(u => $"{by[u]}{u}").ToList();
        return parts.Count == 0 ? null : string.Join("・", parts);
    }

    /// <summary>必須違反の数を単位ごとに（例「3件・2人」）。内訳が無いときだけ件で言う。</summary>
    public static string HardText(IReadOnlyDictionary<string, int>? breakdown, long n) =>
        (breakdown is null ? null : UnitTotals(breakdown, MirrorKeys.Hard)) ?? $"{n}件";

    /// <summary>実行中の上部の札。必須違反は単位ごとに（件と人を足さない＝INV-07、3.693.0）。必須 0 のときは数を出さない。</summary>
    public static string RunningBadgeLabel(IReadOnlyDictionary<string, int>? breakdown, long bestHard) =>
        bestHard > 0L ? $"組んでいます 必須{HardText(breakdown, bestHard)}" : "組んでいます";

    /// <summary>族ごとの増減を単位ごとに足した言い方（例「1件減る・2人増える」）。同じ単位の中だけで足す。全部 0 なら null。</summary>
    public static string? UnitDeltaText(IEnumerable<(string Family, int Delta)> diff, IEnumerable<string> families)
    {
        var fams = families as IReadOnlyCollection<string> ?? families.ToList();
        var by = new Dictionary<string, int>();
        foreach (var (k, d) in diff)
            if (fams.Contains(k) && d != 0) by[UnitOf(k)] = by.GetValueOrDefault(UnitOf(k), 0) + d;
        var parts = UnitOrder.Where(u => by.TryGetValue(u, out var d) && d != 0)
            .Select(u => $"{Math.Abs(by[u])}{u}{(by[u] < 0 ? "減る" : "増える")}").ToList();
        return parts.Count == 0 ? null : string.Join("・", parts);
    }

    /// <summary>族ごとの増減（例「下限 −6回・禁止の並び +1件」）。合計の数は出さない。</summary>
    public static string FamilyDeltaList(IEnumerable<(string Family, int Delta)> diff, Func<string, string> labelOf) =>
        string.Join("・", diff.Where(x => x.Delta != 0).Select(x => $"{labelOf(x.Family)} {(x.Delta < 0 ? $"−{-x.Delta}" : $"+{x.Delta}")}{UnitOf(x.Family)}"));

    /// <summary>必須違反と要調整を単位ごとに（通知・ログの 1 行）。</summary>
    public static string ReportCountsText(IReadOnlyDictionary<string, int> breakdown) =>
        $"必須違反 {UnitTotals(breakdown, MirrorKeys.Hard) ?? "なし"}・要調整 {UnitTotals(breakdown, MirrorKeys.Soft) ?? "なし"}";

    /// <summary>ホームのでき具合に添える残り。要調整は単位ごとに（pt の公平化・曜日の偏りは出さない）。
    /// pt だけ残ってもでき具合は 100% でないので「解消済み」とは言わない。</summary>
    public static string HomeRemainingLabel(long bestHard, int shortDays, IReadOnlyDictionary<string, int> breakdown, int wishHard = 0)
    {
        var softText = UnitTotals(breakdown, MirrorKeys.Soft, includePt: false);
        var ptN = MirrorKeys.Soft.Where(f => UnitOf(f) == "pt").Sum(f => breakdown.GetValueOrDefault(f, 0));
        if (bestHard > 0L) return $"必須 残り{UnitTotals(breakdown, MirrorKeys.Hard) ?? $"{bestHard}件"}" + (wishHard > 0 ? $"（うち希望のまま {Math.Min(wishHard, bestHard)}件）" : "");
        if (shortDays > 0) return $"人員不足の日 残り{shortDays}日";
        if (softText is not null) return $"必須は解消・要調整 {softText}";
        if (ptN > 0) return "必須は解消・残りは偏りのみ";
        return "解消済み";
    }

    /// <summary>ホームの主ボタンの 1 行に収める字数（全幅のボタンで折り返さない上限）。</summary>
    public const int HomeButtonMaxChars = 18;
    public const string HomeWishButton = "ぶつかっている希望を見る";

    /// <summary>
    /// ホームの主ボタンに最初の対象（名前・日）を前置きする（「アリフ 10/8の希望を見る」）。対象が無いとき、または
    /// 前置きで <see cref="HomeButtonMaxChars"/> を超えるときは元の文言のまま（対象は「次に確認する問題」の行が名指しする）。
    /// </summary>
    public static string HomeTargetLabel(string @base, string? name, string? day)
    {
        var t = string.Join(" ", new[] { name, day }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (t.Length == 0) return @base;
        var label = @base == HomeWishButton ? $"{t}の希望を見る" : $"{t}の{@base}";
        return label.Length <= HomeButtonMaxChars ? label : @base;
    }

    /// <summary>前回の作成と同じ設定（状態指紋）から同じ盤面（盤面指紋）が出たか。初回は false。</summary>
    public static bool IsSameRunResult((long State, long Board)? prev, (long State, long Board) now) => prev is { } p && p == now;

    /// <summary>盤面か設定が変わったら出さない（C# は editRev を持たないので設定は状態指紋で照合）。</summary>
    public static bool SameResultVisible(SameResultNote? note, long stateKey, IReadOnlyList<IReadOnlyList<int>> schedule) =>
        note is not null && note.StateKey == stateKey && note.Schedule.Count == schedule.Count && note.Schedule.Zip(schedule).All(p => p.First.SequenceEqual(p.Second));

    public static string SameResultNotice(int hard) =>
        hard > 0 ? $"前回と同じ結果です。条件を変えない限り、この必須違反{hard}件は消えません。" : "前回と同じ結果です。";

    /// <summary>案があれば途切れる（null）。起点・盤面・設定のどれかが違えば 1 から数え直す。[3.716.0] 自動の探索（<paramref name="auto"/>）は数えも途切れさせもしない（人の操作の回数だけ）。</summary>
    public static EmptySearchStreak? NextEmptySearchStreak(EmptySearchStreak? prev, string origin, long board, long state, int found, bool auto = false)
    {
        if (auto) return prev;
        if (found > 0) return null;
        if (prev is not null && prev.Origin == origin && prev.Board == board && prev.State == state) return prev with { Count = prev.Count + 1 };
        return new EmptySearchStreak(origin, board, state, 1);
    }

    public const string RepeatedEmptyLine = "続けて案がありませんでした";
    public const string NoFixTitle = "案はありません";
    /// <summary>[3.714.0] 案なしの主ボタン（残っている問題の巡回の先頭へ）と閉じる。</summary>
    public const string NoFixListButton = "問題一覧";
    public const string NoFixCloseButton = "閉じる";
    public const string NoFixLine = "設定か希望の確認が必要です";
    /// <summary>[3.716.0] 締切で打ち切られた探索の 1 行（設定の確認を求めない。理由の TIME の行は本文に残る）。</summary>
    public const string NoFixTimeoutLine = "時間内に見つかりませんでした";

    /// <summary>案なしの説明の 1 行: 締切で打ち切られたら時間の行、続けて空なら続けて空の行、そうでなければ定型の 1 行（3.711.0 の行を残す）。</summary>
    public static string NoFixLineOf(bool repeated, bool timedOut = false) => timedOut ? NoFixTimeoutLine : repeated ? RepeatedEmptyLine : NoFixLine;

    /// <summary>続けて 0 件の対象の行き先。希望がぶつかるセルなら希望、設定ミスの診断が出ていれば設定、どちらも無ければボタンなし。</summary>
    public static RepeatedEmptyAction? RepeatedEmptyActionOf(bool wishOnTarget, bool settingIssueKnown) =>
        wishOnTarget ? RepeatedEmptyAction.Wishes : settingIssueKnown ? RepeatedEmptyAction.Settings : null;

    public static string RepeatedEmptyActionLabel(RepeatedEmptyAction a) => a == RepeatedEmptyAction.Wishes ? "希望を見る" : "設定の確認を見る";

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
                return new TriageRow(IssueKindLabel(g.Key), list.Count, "件", heads + more, Kind: g.Key);
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
            var more = c1Plateau.Entries.Count - 1;
            o["c1"] = e.Label + (more > 0 ? $"ほか{more}件" : "") + $"。{e.RecommendedAction(labelOf)}";
        }
        var stuckSurplus = coverage?.Surpluses.Where(s => s.BlockedFamily is not null).ToList() ?? new List<CoverageSurplus>();
        if (stuckSurplus.Count > 0)
        {
            var first = stuckSurplus[0];
            var fam = labelOf(first.BlockedFamily!);
            var slots = $"{first.DayLabel}の「{first.ShiftSymbol}」" + (stuckSurplus.Count > 1 ? $"など{stuckSurplus.Count}枠" : "");
            // 曜日の偏り・グループ内のばらつきには緩める設定が無い＝手で動かす案内にする。
            var remedy = first.BlockedFamily is "weekly" or "fair" ? "勤務表で手で動かすと減らせます。" : $"{fam}の設定を緩めると動きます。";
            o["covO"] = $"{slots}は、動かす手は「{fam}」が優先されるため採用されません。{remedy}";
        }
        return o;
    }

    /// <summary>崩せない禁止の並びの直す場所（<c>ForbiddenRunDiagnosis</c> の案内と同じく、希望の調整か担当の追加）。</summary>
    private const string C3nWallFix = "希望（編集タブ＞月次条件）か担当（編集タブ＞年間マスター①）を見直してください。";

    /// <summary>必須違反の行に添える「なぜ残るか」。診断が無い/観測が無いときは空文字＝何も主張しない。</summary>
    private static string HardDetail(string family, CoverageDiagnosis? coverage, ForbiddenRunDiagnosis? forbidden, string startDate) => family switch
    {
        "c3n" => forbidden is null || !forbidden.HasRuns ? ""
            : forbidden.AllBlocked ? $"この希望・担当のままでは崩せません（{forbidden.TotalRuns}件すべて）。{C3nWallFix}"
            : forbidden.Runs.FirstOrDefault(r => !r.Escapable) is { } run
                ? $"{run.StaffName}さんの {DayText.Range(startDate, run.StartDay, run.StartDay + Math.Max(run.Cells.Count, 1) - 1)} の「{run.SeqLabel}」は、この希望・担当のままでは崩せません。{C3nWallFix}"
            : "崩す手が残っています。下の行から直し方を探せます。",
        "covU" => coverage is null || !coverage.HasShortage ? ""
            : coverage.AllBlockedNow ? $"いまの希望のままでは、試した入れ替えの範囲で埋まりませんでした（{coverage.TotalShortfall}人・自動作成で見つかる場合があります）。"
            : coverage.ProvenBlockedSlots > 0 ? $"{coverage.ProvenBlockedSlots}枠はいまの希望のままでは埋まりません。"
            : coverage.SearchMissSlots > 0 ? $"{coverage.SearchMissSlots}枠は試した入れ替えでは埋まりませんでした。"
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
            if (hard.Contains(fam)) blockers.Add(row with { Detail = HardDetail(fam, ui.CoverageDiag, ui.ForbiddenDiag, ui.StartDate) });
            else if (promoted.TryGetValue(fam, out var why)) blockers.Add(row with { Detail = why, Promoted = true });
            else searching.Add(row);
        }

        var note = computed
            ? "作成後も残っている項目です。設定を変えないと消えないものは上に分けています。"
            : ui.RelaxedBoard
                ? "設定を緩めて手順を当てた盤面の概算です（再作成する前）。"
                : "実行前の概算です。期間の制約・禁止の並びなど、設定の組み合わせによっては、つくったあとも残る場合があります。";
        return new AnalysisTriage(computed, blockers, AggregateIssues(ui.SettingIssues), searching, ok, busy, note);
    }

    /// <summary>設定の見直しの行の全幅ボタン（節の名を言う）。Kotlin <c>issueSettingsLabel</c>。</summary>
    public static string IssueSettingsLabel(IssueKind? kind) => kind switch
    {
        IssueKind.Wish => "希望の設定を開く",
        IssueKind.Demand => "必要人数の設定を開く",
        null => "設定を開く",
        _ => SettingsLabel.For(VioBuckets.YearSectionForIssueKind(kind) ?? ""),
    };

    /// <summary>分析タブの一文（「必須3件 · 要調整12件」、Kotlin <c>analysisCountLine</c>）。</summary>
    public static string AnalysisCountLine(string hardLabel, string softLabel) => $"必須{hardLabel} · 要調整{softLabel}";

    /// <summary>
    /// 分析タブの状態の段（Kotlin <c>analysisSummary</c>）。一文は上部の札（<see cref="CellSheetLogic.BoardStatusLines"/>）と同じ区別で決める。
    /// tourCount＝巡回できる必須違反の数、fixResolved＝変更案で直せる必須違反の数（null＝未探索・古い＝出さない）。
    /// </summary>
    public static AnalysisSummary AnalysisSummaryOf(UiState ui, int tourCount, int? fixResolved)
    {
        var hard = ui.BestHard;
        var evaluated = ui.CheckRev > 0 && !ui.Running;
        var sentence =
            ui.Running ? "勤務表をつくっています。表示は途中の値です。"
            : !ui.HasResult ? "まだ作っていません。"
            : !ui.EngineRan && !ui.RanBeforeEdit ? "手で編集した勤務表です（自動作成はまだ）。"
            : hard > 0 || ui.BestSoft > 0 ? AnalysisCountLine(hard > 0 ? HardText(ui.Breakdown, hard) : "0件", UnitTotals(ui.Breakdown, MirrorKeys.Soft) ?? "0件")
            : "必須違反も要調整もありません。";
        var points = new List<string>();
        if (ui.HasResult || ui.Running)
        {
            // [3.713.0] 件数は一文（「必須3件 · 要調整12件」）が言う＝点は変更案で直せる数だけ。
            if (evaluated && hard > 0 && fixResolved is { } n) points.Add($"変更案で直せる必須違反 {n}か所");
        }
        var shortDays = ui.CoverageDiag?.Shortfalls.Where(x => x.Miss > 0).Select(x => x.DayIndex).Distinct().Count() ?? 0;
        var active = hard > 0 && !ui.Running && ui.HasResult;
        var primary = active && tourCount > 0 ? AnalysisPrimary.Tour : active && shortDays > 0 ? AnalysisPrimary.Guided : AnalysisPrimary.None;
        var label = primary switch
        {
            AnalysisPrimary.Tour => FixOutcomeText.GuidedTourButton(tourCount),
            AnalysisPrimary.Guided => $"なおし方を見る（{shortDays}日）",
            _ => null,
        };
        return new AnalysisSummary(sentence, points, primary, label);
    }
}

/// <summary>分析タブの主ボタン（1 つだけ）。巡回できるなら巡回、人員不足だけならなおし方、それ以外は出さない。</summary>
public enum AnalysisPrimary { Tour, Guided, None }

/// <summary>分析タブの状態の段: 一文・3 点まで・主ボタン（<see cref="AnalysisTriage.AnalysisSummaryOf"/>）。</summary>
public sealed record AnalysisSummary(string Sentence, IReadOnlyList<string> Points, AnalysisPrimary Primary, string? PrimaryLabel);

/// <summary>同じ依頼（起点）・同じ盤面と設定で続いた案 0 件の回数。</summary>
public sealed record EmptySearchStreak(string Origin, long Board, long State, int Count);

public enum RepeatedEmptyAction { Wishes, Settings }

/// <summary>前回と同じ結果の印。出したときの盤面と状態指紋を持ち、どちらかが変われば出さない。</summary>
public sealed record SameResultNote(int Hard, long StateKey, IReadOnlyList<IReadOnlyList<int>> Schedule);
