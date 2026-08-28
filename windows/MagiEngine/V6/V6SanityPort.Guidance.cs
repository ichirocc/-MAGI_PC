using System.Linq;
using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// The category of a <see cref="SettingIssue"/> — faithful port of Kotlin's <c>IssueKind</c> enum
/// (<c>V6SanityPort.kt:22</c>). Also drives <see cref="V6SanityPort.BuildGuidance"/>'s final sort
/// order (Wish &lt; Demand &lt; Range &lt; Constraint, with a "配布不可" <see cref="SettingIssue.Where"/>
/// prefix always sorted first regardless of kind).
/// </summary>
public enum IssueKind { Wish, Constraint, Demand, Range }

/// <summary>
/// A one-tap remediation a <see cref="SettingIssue"/> can offer, faithful port of Kotlin's
/// <c>SettingFixAction</c> enum (<c>V6SanityPort.kt:24</c>). <see cref="V6SanityPort.BuildGuidance"/>
/// only CONSTRUCTS issues carrying this data (plus the auxiliary key/label fields on
/// <see cref="SettingIssue"/> that a fix action needs); the code that actually ACTS on a tap — e.g.
/// deleting a duplicate constraint row for <see cref="DeleteDupSeq"/> (Kotlin's
/// <c>MagiViewModel.relaxForbiddenRule</c>) — is UI/ViewModel-layer territory (phase 9+), not
/// <c>MagiEngine</c> scope.
/// </summary>
public enum SettingFixAction { None, RemoveWish, DeleteDupSeq, ZeroRangeLo, ClampRangeLo, CapDemand, ClampGroupRangeLo }

/// <summary>
/// One entry in <see cref="V6SanityPort.BuildGuidance"/>'s advisory list — faithful port of Kotlin's
/// <c>SettingIssue</c> data class (<c>V6SanityPort.kt:26-40</c>). <see cref="Kind"/> categorizes it,
/// <see cref="Where"/>/<see cref="Problem"/>/<see cref="Fix"/> are the three lines a settings-mistake
/// card shows (location, what's wrong, how to fix it by hand), and the remaining fields are the
/// payload a one-tap <see cref="SettingFixAction"/> needs (only the fields relevant to
/// <see cref="Action"/> are ever non-null/non-default for a given instance — this mirrors the
/// Kotlin source's single flat data class with mostly-null optional fields rather than a
/// discriminated union, preserved verbatim rather than "improved" into one).
/// </summary>
public sealed record SettingIssue(
    IssueKind Kind,
    string Where,
    string Problem,
    string Fix,
    SettingFixAction Action = SettingFixAction.None,
    string ActionLabel = "",
    string? WishKey = null,
    string? SeqFamily = null,
    string? SeqKey = null,
    string? RangeKey = null,
    string? NewLo = null,
    int? DemandShiftIdx = null,
    int? DemandCap = null,
    string? GroupRangeFamily = null,
    C41Row? GroupRangeRow = null);

/// <summary>
/// [フェーズ7ピース14/15統合] Faithful port of Kotlin's settings-mistake advisor —
/// <c>buildGuidance</c> (<c>V6SanityPort.kt:354-1027</c>, ~670 lines), its sibling helper
/// <c>c3FamilyJp</c> (<c>:1028-1034</c>), and <c>findDuplicateSeqConstraints</c>/
/// <c>collectDuplicateSeq</c> (<c>:1444-1464</c>, pulled forward into this file from their textual
/// position near the end of the Kotlin source because <c>buildGuidance</c> calls them directly).
///
/// The original 12-phase plan split this into two pieces — 14 for <c>buildGuidance</c>'s main body,
/// 15 for a <c>V6SanityPort.GuidanceMus.cs</c> covering its <see cref="ConstraintMus"/>-backed
/// section 9. Reading the full Kotlin function verbatim showed section 9 is inlined directly in the
/// middle of one cohesive function, not dispatched to a separately-callable method — C# partial
/// classes split at MEMBER granularity, not mid-function, so no natural split point exists. Both
/// pieces are merged into this single file instead of writing an uncompilable stub.
///
/// <see cref="V6SanityPort.BuildGuidance"/> depends on: this partial class's own already-ported
/// members (<see cref="V6SanityPort.DetectImpossibleWishes"/>, phase 4; <see cref="V6SanityPort.ForcedCovU"/>/
/// <see cref="V6SanityPort.OtherShiftCapSum"/>/<see cref="V6SanityPort.AptBalances"/>/
/// <see cref="V6SanityPort.RestCapacity"/>/<see cref="V6SanityPort.RangeOrderConflict"/>/
/// <see cref="V6SanityPort.SafeDayLabel"/>/private <c>NeedDefined</c>/<c>EffectiveDemand</c>/
/// <c>EffectiveCap</c>, all piece 2's <c>V6SanityPort.Core.cs</c> — visible here without
/// qualification because C# partial-class members, UNLIKE Kotlin's file-private semantics, are
/// shared across every file of the same partial class); <see cref="Problem"/> (including its
/// diagnostic-only fields <c>C3OverT</c>/<c>C1OverT</c>/<c>C3UnknownShift</c>/<c>UnresolvedRows</c>/
/// <c>OutOfRangeGroupStaff</c>); <see cref="Evaluator"/>; <see cref="ConstraintMus"/> (piece 13,
/// <c>AnalyzeStaffConflicts</c>/<c>AnalyzeDayConflicts</c>/<c>CachedMinDays</c> plus its <c>Item</c>
/// subtype hierarchy); and <c>ScheduleUtil</c>'s <c>CanDo</c>/<c>WishLocked</c>/<c>NormalizeSchedule</c>/
/// <c>ToIntArray2D</c> extension helpers.
///
/// Two <c>getOrNull</c>-with-fallback lookups in the Kotlin source do NOT use the standard
/// <c>Sym</c>/<c>Nm</c>-helper fallback ("index if the record is missing") and are translated
/// inline instead: the group-apt validation loop's shift-symbol fallback is <c>"#$k"</c> (not
/// <c>k.toString()</c>), and the out-of-range-group-staff name lookup handles BOTH a missing staff
/// record AND a present-but-blank name (<c>?.name?.ifBlank { "#$i" } ?: "#$i"</c>) — <c>Nm</c> only
/// handles the former. Several other lookups (section 2h's staffRange/needDay parsing, section 4's
/// nullable-index name/symbol resolution) also fall outside the standard pattern because their
/// fallback string differs (the raw dictionary key, an empty string, or Kotlin's null-interpolates-
/// as-the-literal-text-"null" behavior for a still-<c>null</c> nullable int — C#'s
/// <c>Nullable&lt;int&gt;.ToString()</c> returns <c>""</c> for a null value where Kotlin's string
/// template renders <c>"null"</c>, so those specific sites spell out <c>?? "null"</c> explicitly to
/// preserve the exact fallback text) and are likewise written inline rather than forced through a
/// shared helper that doesn't actually match their behavior.
/// </summary>
public static partial class V6SanityPort
{
    /// <summary>
    /// Faithful port of Kotlin's <c>buildGuidance</c> — see this file's class-level doc comment for
    /// scope and dependencies. Every section below carries forward its Kotlin source's own inline
    /// <c>//</c> comment (kept in Japanese, matching this codebase's established convention of
    /// preserving original business-rationale prose rather than translating or paraphrasing it) so
    /// the section numbering (1, 2, 2b, 2b-2, 2b-3, 2c, 2d, 2m, 2e, 2f, 2g, 2j, 2h, 2i, 2k, 2l, 3, 4,
    /// 5, 6, 6b, 6d, 6c, 7, 8, 9) and the "why", not just the "what", stay traceable back to the
    /// Kotlin original. The out-of-numeric-order placement of 2m between 2d and 2e is preserved
    /// verbatim — it reflects the Kotlin source's own commit history (2m was inserted later) and
    /// reordering it would only obscure that history for no benefit.
    /// </summary>
    public static List<SettingIssue> BuildGuidance(MagiState state, Problem? p = null)
    {
        p ??= new Problem(state);
        var outList = new List<SettingIssue>();

        string Sym(int k) => k >= 0 && k < state.Shifts.Count ? state.Shifts[k].Kigou : k.ToString();
        string Nm(int i) => i >= 0 && i < state.StaffList.Count ? state.StaffList[i].Name : $"#{i}";

        // 1) 希望シフトの設定ミス（担当外・範囲外など）
        foreach (var w in DetectImpossibleWishes(state, p))
        {
            var where = $"{w.StaffName} {SafeDayLabel(state.StartDate, w.DayIndex)} 希望「{w.ShiftSymbol}」";
            string fix = w.Reason.Contains("担当不可")
                ? $"この希望を取り消すか、設定で{w.StaffName}さんの担当に「{w.ShiftSymbol}」を追加してください"
                : w.Reason.Contains("範囲外")
                    ? "希望のシフト記号・日付が勤務表の範囲内かを確認してください"
                    : "希望の入力（i,j形式）を確認してください";
            var canOneTap = w.Reason.Contains("担当不可");
            outList.Add(new SettingIssue(IssueKind.Wish, where, $"実現できない希望です（{w.Reason}）", fix,
                Action: canOneTap ? SettingFixAction.RemoveWish : SettingFixAction.None,
                ActionLabel: canOneTap ? "この希望を取消" : "",
                WishKey: canOneTap ? $"{w.StaffIndex},{w.DayIndex}" : null));
        }

        // 2) 連続パターン制約の重複（例: c3n:Dﾃ→A4）
        foreach (var d in FindDuplicateSeqConstraints(state))
        {
            var parts0 = d.Split(':', 2);
            var famRaw = parts0[0];
            var fam = C3FamilyJp(famRaw);
            var seq = parts0.Length > 1 ? parts0[1] : "";
            outList.Add(new SettingIssue(IssueKind.Constraint, $"連続パターン「{seq}」({fam})",
                "同じパターンが2重に登録されています", $"連続パターン設定で「{seq}」の重複行を1つ削除してください",
                Action: SettingFixAction.DeleteDupSeq, ActionLabel: "重複を1つ削除",
                SeqFamily: famRaw, SeqKey: seq));
        }

        // 2b) [監査#8 / Web HF557 A4 の native 移植] 連勤・回数窓制約(cons1)の不能設定
        //   d1>期間: 窓が期間を超え、判定が一度も走らず無言で無効。 d2>d1: 物理的に不可能で全員・全窓が発火し続ける。
        foreach (var c in p.Cons1)
        {
            var sym = Sym(c.ShiftIdx);
            if (c.Day1 > p.T)
            {
                outList.Add(new SettingIssue(IssueKind.Constraint, $"連勤/休制約「{sym} {c.Day1}日で{c.Day2}回以上」",
                    $"窓{c.Day1}日が期間{p.T}日を超えるため、この制約は一度も判定されません（無言で無効です）",
                    $"制約設定（連勤・回数）で日数を期間{p.T}日以下に直すか、この行を削除してください"));
            }
            else if (c.Day2 > c.Day1)
            {
                outList.Add(new SettingIssue(IssueKind.Constraint, $"連勤/休制約「{sym} {c.Day1}日で{c.Day2}回以上」",
                    $"{c.Day1}日の窓に{c.Day2}回は物理的に不可能で、全員・全期間が違反になり続けます",
                    $"制約設定（連勤・回数）で回数を{c.Day1}回以下に直すか、この行を削除してください"));
            }
        }

        // 2b-2) [壁/covO-tension 分類] c1 窓制約の充足可否。需要 = 各 canDo 職員 × day2 × floor(T/day1)（disjoint窓の下界）。
        //   [3.364.0 訂正・実データ計測起因] 非休シフトの供給に per-day 上限(need2/need1)の総和を使うのは誤り。
        //   need2/need1 は covO の SOFT 目標(1日あたりの過剰配置しきい値=重み1)であって物理上限ではなく、最適化は covO を
        //   払って上限を超えて配置できる。かつ day2<=day1 ガードより「物理供給(担当nCanDo人×T日) >= 需要」が**常に成立**する
        //   ので、非休の c1 窓は原理的に構造的不能にはならない（旧実装は golden の Dﾃ を「構造的に残る」と誤断定していたが、
        //   実データの手作り表は Dﾃ を上限超えの35回配置しており供給31は実上限でないことを実測で確認）。
        //   → 休のみ「S*T−Σ最小work需要」が実在の物理上限＝供給<需要なら真の壁。非休は上限が窓ルールに届かない場合のみ、
        //     c1 充足に過剰配置(covO)が要る旨を「解消不能ではないトレードオフ」として正直に案内する。read-only・スコア不変。
        {
            var workMinDemand = 0;
            // [3.409.22] 旧: need1 直読み＝need2 単独定義の需要を 0 と数え、休の供給を過大評価していた
            //   （＝真の壁を見逃す側。false wall は作らないので実害は軽いが値が不正確だった）。
            //   effectiveDemand はセルごとの真の最小＝過大にはならない（3.76.0「false wall を出さない」と両立）。
            for (var k = 0; k < p.K; k++) for (var j = 0; j < p.T; j++) workMinDemand += EffectiveDemand(p, k, j);
            foreach (var c in p.Cons1)
            {
                var si = c.ShiftIdx;
                // 退化ケース(窓>期間 / 回数>窓)は 2b が別途案内。ここは通常窓のみ。
                if (c.Day1 <= 0 || c.Day2 <= 0 || c.Day1 > p.T || c.Day2 > c.Day1) continue;
                var disjoint = p.T / c.Day1;
                if (disjoint <= 0) continue;
                var nCanDo = 0;
                for (var s = 0; s < p.S; s++) if (p.CanDo(s, si)) nCanDo++;
                if (nCanDo == 0) continue;   // 担当者ゼロは別の案内対象
                var demand = nCanDo * c.Day2 * disjoint;
                var sym = Sym(si);
                if (si == p.RestIdx)
                {
                    // 休は「作業に回さないセル数(S*T−最小work需要)」が実在の物理上限＝供給<需要なら真の構造的不能。
                    var supply = p.S * p.T - workMinDemand;
                    if (supply < demand)
                    {
                        outList.Add(new SettingIssue(IssueKind.Constraint, $"窓ルール「{sym} を{c.Day1}日で{c.Day2}回以上」",
                            $"「{sym}」の供給{supply}に対し必要{demand}(=担当{nCanDo}人×{c.Day2}回×{disjoint}窓)で{demand - supply} 不足。" +
                                "どう組んでもこの窓違反(c1)は構造的に残ります（最適化では消せません）。",
                            $"作業シフトの最低人数を下げて「{sym}」に回せる余地を増やすか、窓ルールの回数を下げる／日数を延ばす(制約設定)。"));
                    }
                }
                else
                {
                    // 非休は物理供給(担当nCanDo人×T日)>=需要が常に成立＝壁ではない。per-day 上限(need2/need1)の総和が
                    //   窓ルールに届かない場合のみ、c1 充足に過剰配置(covO)が要る旨をトレードオフとして案内。
                    // [3.409.23/監査SANITY-5] 上限が**1日でも未設定**なら、その日は covO が構造的に発火しない
                    //   ＝「1日あたり上限」という前提そのものが成立しない。旧実装は未設定(-1)を
                    //   `coerceAtLeast(0)` で 0 に潰して合算していたため、一部の日だけ need を設定した
                    //   シフトで不足量が過大に出た（実測: 6日中 day0 のみ need1=1 → 「7回ぶんの過剰配置が
                    //   要ります」。実際に上限があるのは1日だけ）。しかも助言が指す罰(covO)がそのシフトには
                    //   存在しないので、従っても何も変わらない。前提が崩れている以上、案内しないのが正しい。
                    var capSum = 0;
                    var capKnown = true;
                    for (var j = 0; j < p.T; j++)
                    {
                        var cap = EffectiveCap(p, si, j);
                        if (cap < 0) { capKnown = false; break; }   // 未設定＝無制限
                        capSum += cap;
                    }
                    if (capKnown && capSum < demand)
                    {
                        var shortfall = demand - capSum;
                        outList.Add(new SettingIssue(IssueKind.Constraint, $"窓ルール「{sym} を{c.Day1}日で{c.Day2}回以上」",
                            $"「{sym}」の1日あたり上限の合計({capSum})が窓ルールの必要回数({demand})に{shortfall}回ぶん届かず、" +
                                "c1 を満たすには一部の日で上限を超える配置(過剰配置)が要ります。構造的に不能ではなく、最適化は過剰配置を少し払って解消できます。",
                            $"「{sym}」の1日あたり上限を上げるか、{shortfall}回ぶんの過剰配置を許容してください。"));
                    }
                }
            }
        }

        // 2b-3) [壁/ダイヤル分類・個人版/ドッグフーディングで発見、3.262.0で厳密化] 2b-2は全体供給(集計)
        //   のみ判定するため、「集計では担当者が大勢いて足りているのに、特定の1人だけは自分の個人上限
        //   (staffRange上限)のせいで自分自身の窓ルールを満たせない」局面を見逃していた（例: Aｱ担当可能者
        //   は全体で10人いても、ある1人だけAｱ個人上限が低く「14日窓でAｱ≥1」を自分では満たせない）。
        //   [3.262.0] 旧実装は2b-2と同じ非重複窓の粗い下界(day2×floor(T/day1))を使っていたが、これは
        //   スライド窓の真の必要量を過小評価する（実データ検証: 「15日窓4回以上」の粗い下界=8だが、
        //   実際に0違反へ到達するには9〜11日必要な職員が複数おり、粗い下界では「上限8/9で足りている」
        //   と誤って見逃していた＝false negative）。`SmartInitialScheduler.minDaysForFullCompliance`
        //   （構築本体の`solveConstructionDp`を無制限capで呼び、0違反を達成する最小日数を求める）へ
        //   置換し、同一シフトの複数規則(例: 休の5日窓＋15日窓)も**同時充足**の真の必要量として厳密判定。
        {
            var rulesByShift = new Dictionary<int, List<C1>>();
            foreach (var c in p.Cons1)
            {
                if (c.ShiftIdx < 0 || c.ShiftIdx >= p.K || c.Day1 <= 0 || c.Day2 <= 0 || c.Day1 > p.T || c.Day2 > c.Day1) continue;
                if (!rulesByShift.TryGetValue(c.ShiftIdx, out var list)) rulesByShift[c.ShiftIdx] = list = new List<C1>();
                list.Add(c);
            }
            foreach (var (shiftIdx, rules) in rulesByShift)
            {
                // [3.272.0] ConstraintMus.cachedMinDays（同じ純関数のプロセス全域キャッシュ）経由に統一。
                //   buildGuidance はセル編集ごとに走るため、重いDP（15日窓で数百ms）を毎回払わない。
                if (ConstraintMus.CachedMinDays(p.T, rules.Select(r => (r.Day1, r.Day2)).ToList()) is not int minDays) continue;
                var sym = Sym(shiftIdx);
                var ruleDesc = string.Join(" かつ ", rules.Select(r => $"{r.Day1}日で{r.Day2}回以上"));
                for (var i = 0; i < p.S; i++)
                {
                    if (!p.CanDo(i, shiftIdx)) continue;
                    var hi = p.RangeHi[i][shiftIdx];
                    if (hi == int.MaxValue || hi >= minDays) continue;
                    var name = Nm(i);
                    outList.Add(new SettingIssue(IssueKind.Range, $"{name}さんの「{sym}」個人上限と窓ルールの衝突",
                        $"窓ルール「{sym} を{ruleDesc}」を同時に満たすには最低{minDays}回が必要ですが、" +
                            $"{name}さんの「{sym}」個人上限は{hi}回です。この人だけではどう配置しても窓ルールを満たせません",
                        $"{name}さんの「{sym}」個人上限を{minDays}回以上に上げるか、窓ルールの回数を下げてください"));
                }
            }
        }

        // 2c) [監査#5] 担当可能者ゼロの回数制約(cons2) — canDoガード後は事実上無効になるため案内する。
        foreach (var c in p.Cons2)
        {
            var eligible = 0;
            for (var s = 0; s < p.S; s++) if (p.CanDo(s, c.ShiftIdx)) eligible++;
            if (eligible == 0)
            {
                var sym = Sym(c.ShiftIdx);
                outList.Add(new SettingIssue(IssueKind.Constraint, $"回数制約「{sym} を{c.Count}回以上」",
                    "このシフトを担当できる職員がいないため、この制約は事実上無効です",
                    "担当設定（グループ×シフト）で担当者を追加するか、この行を削除してください"));
            }
        }

        // 2d) [監査#9] 期間より長い連続パターン — パース段階で除外済み（Problem.c3OverT）。理由を案内する。
        foreach (var (fam, seqStr) in p.C3OverT)
        {
            var famJp = C3FamilyJp(fam);
            var negative = fam == "c3n" || fam == "c3mn";
            outList.Add(new SettingIssue(IssueKind.Constraint, $"連続パターン「{seqStr}」({famJp})",
                negative
                    ? $"パターン長が期間{p.T}日を超えるため期間内に発生し得ず、この制約は無効です"
                    : $"パターン長が期間{p.T}日を超えるため物理的に充足できず、この制約は無効です",
                $"連続パターン設定でパターンを{p.T}日以下に短縮するか、この行を削除してください"));
        }

        // 2m) [3.412.0/P-04] 期間より長い窓の要件 — 行は解決できるが `MirrorCore.checkC1Family` が
        //   `c.day1 > p.T` で無言に飛ばすため、評価もされず画面にも何も出ない状態だった。
        //   連続パターン(2d)と同じ形で理由を案内する。read-only・評価不変。
        foreach (var row in p.C1OverT)
        {
            outList.Add(new SettingIssue(IssueKind.Constraint, $"窓の要件「{row}」",
                $"窓の日数が期間{p.T}日を超えるため、この決まりは評価されません（今の勤務表では常に無視されます）",
                $"窓の日数を{p.T}日以下にするか、この行を削除してください"));
        }

        // 2e) [3.309.0] 存在しないシフト記号を含む連続パターン — パース段階で無言除外されていた。
        //   シフトの改名・削除でこうなる。禁止(c3n)なら HARD 制約が黙って無効化されるため必ず案内する。
        foreach (var (fam, seqStr) in p.C3UnknownShift)
        {
            var famJp = C3FamilyJp(fam);
            outList.Add(new SettingIssue(IssueKind.Constraint, $"連続パターン「{seqStr}」({famJp})",
                "〈〉で囲んだ記号が今のシフト一覧にないため、この行は評価されていません" +
                    "（シフトを改名・削除するとこうなります）",
                "連続パターン設定でこの行の記号を今あるシフトに直すか、行を削除してください"));
        }

        // 2f) [3.320.0] 3.309.0 は連続パターンだけを直したが、同じ無言除外が残り6族にもあった
        foreach (var (famJp, rowStr) in p.UnresolvedRows)
        {
            outList.Add(new SettingIssue(IssueKind.Constraint, $"{famJp}「{rowStr}」",
                "この行は評価されていません。〈〉で囲んだ記号が今の一覧にないか、日数・回数が空か数値でない" +
                    "ためです（シフトや群を改名・削除するとこうなります）",
                "制約設定でこの行を今ある記号・正しい数値に直すか、行を削除してください"));
        }

        // 2g) 「休」記号なし
        if (!state.Shifts.Any(s => s.Kigou == "休"))
        {
            var head = state.Shifts.FirstOrDefault()?.Kigou ?? "(シフト未登録)";
            outList.Add(new SettingIssue(IssueKind.Constraint, "「休」のシフトがありません",
                $"記号が「休」のシフトが無いため、先頭の「{head}」を休として扱っています" +
                    "（曜日の偏りや休み関連の診断がこの前提で動きます）",
                "シフト設定で休みのシフトの記号を「休」にしてください"));
        }

        // 2j) 期間/職員数の上限
        if (state.DayCount > 31)
        {
            var slow = state.DayCount > 64 ? "。64日を超えるとビット演算の高速経路が使えず探索が遅くなります" : "";
            outList.Add(new SettingIssue(IssueKind.Demand, "対象期間が1か月を超えています",
                $"対象期間が{state.DayCount}日あります。想定は1か月（31日）以内です{slow}",
                "基本情報で開始日・終了日を1か月以内にするか、月ごとに分けて作成してください"));
        }
        if (state.StaffCount > 30)
        {
            outList.Add(new SettingIssue(IssueKind.Demand, "職員数が想定を超えています",
                $"職員が{state.StaffCount}名います。想定は30名以内です",
                "職員を分けて作成するか、この規模で使う場合は計算時間が延びることを見込んでください"));
        }

        // 2h) 数値でない設定値
        bool BadNum(string v) => !string.IsNullOrWhiteSpace(v) && KotlinInterop.ToIntOrNull(v.Trim()) is null;

        foreach (var (key, r) in state.StaffRange)
        {
            if (!BadNum(r.Lo) && !BadNum(r.Hi)) continue;
            var idx = key.Split(',');
            var nm = idx.Length > 0 && KotlinInterop.ToIntOrNull(idx[0]) is int i0 && i0 >= 0 && i0 < state.StaffList.Count
                ? state.StaffList[i0].Name : key;
            var sy = idx.Length > 1 && KotlinInterop.ToIntOrNull(idx[1]) is int k0 && k0 >= 0 && k0 < state.Shifts.Count
                ? state.Shifts[k0].Kigou : "";
            outList.Add(new SettingIssue(IssueKind.Constraint, $"個人の回数「{nm} {sy}」",
                $"下限「{r.Lo}」上限「{r.Hi}」に数値でない値があります。その側は**制限なし**として" +
                    "扱われるため、意図より弱い条件で計算されます",
                "個人の回数で数値を入れ直すか、制限しないなら空欄にしてください"));
        }
        foreach (var sh in state.Shifts)
        {
            if (!BadNum(sh.Need1) && !BadNum(sh.Need2)) continue;
            outList.Add(new SettingIssue(IssueKind.Constraint, $"必要人数「{sh.Kigou}」",
                $"最低人数「{sh.Need1}」上限人数「{sh.Need2}」に数値でない値があります。その側は" +
                    "**未設定（要件なし）**として扱われます",
                "必要人数で数値を入れ直すか、設定しないなら空欄にしてください"));
        }
        void CheckRange(string famJp, string fam, IReadOnlyList<C41Row> rows)
        {
            foreach (var c in rows)
            {
                if (BadNum(c.L) || BadNum(c.U))
                {
                    outList.Add(new SettingIssue(IssueKind.Constraint, $"{famJp}「{c.GroupKigou} {c.ShiftKigou}」",
                        $"下限「{c.L}」上限「{c.U}」に数値でない値があります。その側は**制限なし**として" +
                            "扱われるため、意図より弱い条件で計算されます",
                        "制約設定で数値を入れ直すか、制限しないなら空欄にしてください"));
                    continue;
                }
                if (RangeOrderConflict(c.L, c.U) is (int lo, int hi))
                {
                    outList.Add(new SettingIssue(IssueKind.Constraint, $"{famJp}「{c.GroupKigou} {c.ShiftKigou}」",
                        $"下限{lo} > 上限{hi} で矛盾しています。この組み合わせは期間の全日が違反になり、" +
                            "勤務表をどう組んでも消えません",
                        "制約設定で下限≤上限に直してください",
                        Action: SettingFixAction.ClampGroupRangeLo,
                        ActionLabel: $"下限を{hi}に下げる",
                        NewLo: hi.ToString(), GroupRangeFamily: fam, GroupRangeRow: c));
                }
            }
        }
        CheckRange("群のレンジ", "c41", state.Cons41);
        CheckRange("スキル群のレンジ", "c41s", state.Cons41s);

        void CheckNeedDayNumeric(IReadOnlyDictionary<string, string> map, string jp)
        {
            var bad = map.Where(e => BadNum(e.Value)).OrderBy(e => e.Key, StringComparer.Ordinal).ToList();
            if (bad.Count == 0) return;
            var where = string.Join("・", bad.Take(3).Select(e =>
            {
                var kj = e.Key.Split(',');
                var sy = kj.Length > 0 && KotlinInterop.ToIntOrNull(kj[0]) is int k0 && k0 >= 0 && k0 < state.Shifts.Count
                    ? state.Shifts[k0].Kigou : e.Key;
                var d = kj.Length > 1 && KotlinInterop.ToIntOrNull(kj[1]) is int d0
                    ? SafeDayLabel(state.StartDate, d0) : "";
                return $"{sy} {d}「{e.Value}」";
            }));
            outList.Add(new SettingIssue(IssueKind.Constraint, $"日別の{jp}",
                $"{bad.Count}件（{where}{(bad.Count > 3 ? " ほか" : "")}）が数値ではありません。" +
                    "その日は**シフトの既定値**で計算されます（例外を設定したつもりでも効きません）",
                "日別の必要人数で数値を入れ直すか、例外にしないなら削除してください"));
        }
        CheckNeedDayNumeric(state.NeedDay1, "最低人数");
        CheckNeedDayNumeric(state.NeedDay2, "上限人数");

        {
            var bad = new List<string>();
            for (var g = 0; g < state.GroupShiftApt.Count; g++)
            {
                var row = state.GroupShiftApt[g];
                for (var k = 0; k < row.Count; k++)
                {
                    var v = row[k];
                    if (!BadNum(v)) continue;
                    var gk = g < state.Groups.Count ? state.Groups[g].Kigou : $"#{g}";
                    var sk = k < state.Shifts.Count ? state.Shifts[k].Kigou : $"#{k}";
                    bad.Add($"{gk} {sk}「{v}」");
                }
            }
            if (bad.Count > 0)
            {
                outList.Add(new SettingIssue(IssueKind.Constraint, "適切回数（1人あたりの目標）",
                    $"{bad.Count}件（{string.Join("・", bad.Take(3))}{(bad.Count > 3 ? " ほか" : "")}）が" +
                        "数値ではありません。**目標なし**として扱われます",
                    "回数設定で数値を入れ直すか、目標にしないなら空欄にしてください"));
            }
        }

        // 2i) skillIdx範囲外
        if (state.SkillGroups.Count > 0)
        {
            var bad = new List<(Staff Staff, int Index)>();
            for (var idx = 0; idx < state.StaffList.Count; idx++)
            {
                var st2 = state.StaffList[idx];
                if (st2.SkillIdx != -1 && (st2.SkillIdx < 0 || st2.SkillIdx >= state.SkillGroups.Count))
                    bad.Add((st2, idx));
            }
            if (bad.Count > 0)
            {
                var names = string.Join("・", bad.Take(4).Select(t =>
                    string.IsNullOrWhiteSpace(t.Staff.Name) ? $"#{t.Index}" : t.Staff.Name));
                outList.Add(new SettingIssue(IssueKind.Constraint, "スキル群の割当",
                    $"{bad.Count}名（{names}{(bad.Count > 4 ? " ほか" : "")}）のスキル群が今の一覧の範囲外です。" +
                        "この職員はスキル群の制約から外れて計算されます",
                    "職員管理でスキル群を選び直すか、所属させないなら「(なし)」にしてください"));
            }
        }

        // 2k) groupIdx範囲外
        if (p.OutOfRangeGroupStaff.Count > 0)
        {
            var bad = p.OutOfRangeGroupStaff;
            var names = string.Join("・", bad.Take(4).Select(i =>
            {
                if (i >= 0 && i < state.StaffList.Count)
                {
                    var nm = state.StaffList[i].Name;
                    return string.IsNullOrWhiteSpace(nm) ? $"#{i}" : nm;
                }
                return $"#{i}";
            }));
            var headKigou = state.Groups.Count > 0 ? state.Groups[0].Kigou : null;
            var head = string.IsNullOrWhiteSpace(headKigou) ? "先頭のグループ" : headKigou;
            outList.Add(new SettingIssue(IssueKind.Constraint, "グループの割当",
                $"{bad.Count}名（{names}{(bad.Count > 4 ? " ほか" : "")}）のグループが今の一覧の範囲外です。" +
                    $"計算では「{head}」に所属しているものとして扱っています＝担当できるシフトが意図と違います",
                "職員管理でグループを選び直してください"));
        }

        // 2l) 担当シフトゼロの群
        for (var g = 0; g < state.Groups.Count; g++)
        {
            var row = g < state.GroupShift.Count ? state.GroupShift[g] : new List<int>();
            if (row.Any(v => v == 1)) continue;
            var members = state.StaffList.Count(s => s.GroupIdx == g);
            if (members == 0) continue;
            var gname = state.Groups[g].Kigou;
            gname = string.IsNullOrWhiteSpace(gname) ? $"#{g}" : gname;
            outList.Add(new SettingIssue(IssueKind.Constraint, "担当できるシフト",
                $"グループ「{gname}」（{members}名）は担当できるシフトが1つもありません。この職員は休しか置けず、" +
                    "必要人数のある日はすべて人員不足になります",
                "年間マスターの「担当できるシフト（群×シフト）」で担当するシフトを選んでください"));
        }

        // 3) 需要>担当可能人数
        for (var j = 0; j < p.T; j++)
        {
            for (var k = 0; k < p.K; k++)
            {
                var need = EffectiveDemand(p, k, j);
                if (need <= 0) continue;
                var capable = 0;
                for (var i = 0; i < p.S; i++) if (p.CanDo(i, k)) capable++;
                if (need > capable)
                {
                    var sym = Sym(k);
                    outList.Add(new SettingIssue(IssueKind.Demand, $"{SafeDayLabel(state.StartDate, j)} {sym}",
                        $"必要{need}人ですが担当できるのは{capable}人だけです",
                        $"担当できる職員を増やすか、必要人数を{capable}人以下に下げてください",
                        Action: SettingFixAction.CapDemand, ActionLabel: $"必要数を{capable}人に下げる",
                        DemandShiftIdx: k, DemandCap: capable));
                }
            }
        }

        // 4) staffRange設定ミス
        foreach (var (key, r) in state.StaffRange)
        {
            var parts = key.Split(',');
            int? i = parts.Length > 0 ? KotlinInterop.ToIntOrNull(parts[0]) : null;
            int? k = parts.Length > 1 ? KotlinInterop.ToIntOrNull(parts[1]) : null;
            var lo = KotlinInterop.ToIntOrNull(r.Lo.Trim());
            var name = (i is int in1 && in1 >= 0 && in1 < state.StaffList.Count)
                ? state.StaffList[in1].Name : $"#{(i?.ToString() ?? "null")}";
            var sym = (k is int kn1 && kn1 >= 0 && kn1 < state.Shifts.Count)
                ? state.Shifts[kn1].Kigou : (k?.ToString() ?? "null");
            if (i is not int iVal || k is not int kVal || iVal < 0 || iVal >= p.S || kVal < 0 || kVal >= p.K)
            {
                outList.Add(new SettingIssue(IssueKind.Range, $"回数設定 {key}", "対象職員/シフトが範囲外です", "設定で正しい職員・シフトに付け直してください"));
                continue;
            }
            if (RangeOrderConflict(r.Lo, r.Hi) is (int cLo, int cHi))
            {
                outList.Add(new SettingIssue(IssueKind.Range, $"{name} の「{sym}」回数", $"下限{cLo} > 上限{cHi} で矛盾しています", "設定で下限≤上限に直してください",
                    Action: SettingFixAction.ClampRangeLo, ActionLabel: $"下限を{cHi}に下げる", RangeKey: key, NewLo: cHi.ToString()));
            }
            if (lo is int loVal && loVal > 0 && !p.CanDo(iVal, kVal))
            {
                outList.Add(new SettingIssue(IssueKind.Range, $"{name} の「{sym}」回数", $"担当できないシフトに下限{loVal}が設定されています", $"下限を0にするか、{name}さんの担当に「{sym}」を追加してください",
                    Action: SettingFixAction.ZeroRangeLo, ActionLabel: "下限を0にする", RangeKey: key, NewLo: "0"));
            }
            if (lo is int loVal2 && loVal2 > p.T)
            {
                outList.Add(new SettingIssue(IssueKind.Range, $"{name} の「{sym}」回数", $"下限{loVal2}が期間日数({p.T}日)を超えています", $"下限を{p.T}以下に直してください",
                    Action: SettingFixAction.ClampRangeLo, ActionLabel: $"下限を{p.T}に下げる", RangeKey: key, NewLo: p.T.ToString()));
            }
        }

        // 5) 下限合計>期間日数
        for (var i = 0; i < p.S; i++)
        {
            var sumLo = 0;
            for (var k = 0; k < p.K; k++)
            {
                var lo = p.RangeLo[i][k];
                if (lo != int.MinValue && lo > 0) sumLo += lo;
            }
            if (sumLo > p.T)
            {
                var name = Nm(i);
                outList.Add(new SettingIssue(IssueKind.Range, $"{name} の回数下限の合計",
                    $"各シフトの下限の合計が{sumLo}で、期間日数({p.T}日)を超えています",
                    $"どれかのシフトの下限を下げてください（合計を{p.T}以下に）"));
            }
        }

        // 6) シフト単位過拘束
        for (var k = 0; k < p.K; k++)
        {
            var seatsLo = 0;
            var seatsHi = 0;
            var hasDemand = false;
            for (var j = 0; j < p.T; j++)
            {
                if (!NeedDefined(p, k, j)) continue;
                hasDemand = true;
                seatsLo += Math.Max(EffectiveDemand(p, k, j), 0);
                seatsHi += Math.Max(EffectiveCap(p, k, j), 0);
            }
            if (!hasDemand && k != p.RestIdx) continue;
            var sym = Sym(k);
            var capable = 0;
            var loSum = 0;
            var capSum = 0;
            var allCapped = true;
            for (var i = 0; i < p.S; i++)
            {
                if (!p.CanDo(i, k)) continue;
                capable++;
                var lo = p.RangeLo[i][k];
                var hi = p.RangeHi[i][k];
                if (lo != int.MinValue && lo > 0) loSum += lo;
                if (hi != int.MaxValue) capSum += hi; else allCapped = false;
            }
            var loCapacity = k == p.RestIdx ? RestCapacity(p) : seatsHi;
            if (loSum > loCapacity)
            {
                outList.Add(new SettingIssue(IssueKind.Demand, $"「{sym}」の回数下限の合計",
                    k == p.RestIdx
                        ? $"担当者の下限の合計が{loSum}回ですが、他シフトの個人下限を差し引いた「{sym}」の" +
                            $"最大可能日数の合計は{loCapacity}回しかありません。全員の下限は同時に満たせず、下限割れが必ず出ます"
                        : $"担当者の下限の合計が{loSum}回ですが、必要数の合計は{loCapacity}回しかありません。" +
                            "全員の下限は同時に満たせず、過剰配置か下限割れが必ず出ます",
                    k == p.RestIdx ? $"「{sym}」の個人下限を下げるか、他シフトの個人下限を見直してください"
                        : $"「{sym}」の個人下限を下げるか、必要人数を増やしてください"));
            }
            if (capable > 0 && allCapped && capSum < seatsLo)
            {
                var gap = seatsLo - capSum;
                outList.Add(new SettingIssue(IssueKind.Demand, $"「{sym}」の必要人数",
                    $"必要数の合計は{seatsLo}回ですが、担当者の上限の合計は{capSum}回しかありません。" +
                        $"個人上限を守る限り{gap}回ぶんは埋まりません（実際には人員不足と上限超過が" +
                        "合わせて{gap}回ぶん必ず残ります。どちらに出るかは他の条件との兼ね合いで決まります）",
                    $"「{sym}」の個人上限を上げる/担当者を増やすか、必要人数を下げてください"));
            }
        }
        foreach (var b in AptBalances(state, p))
        {
            if (!b.Overloaded) continue;
            outList.Add(new SettingIssue(IssueKind.Demand, $"「{b.Kigou}」の適切回数の合計",
                b.IsRest
                    ? $"適切回数(レパートリー目標)の合計が{b.AptSum}回ですが、他シフトの個人下限を差し引いた「{b.Kigou}」の" +
                        $"最大可能日数の合計は{b.Capacity}回しかありません。全員の目標は同時に満たせず、目標割れか過剰配置が必ず出ます"
                    : $"適切回数(レパートリー目標)の合計が{b.AptSum}回ですが、必要数の合計は{b.Capacity}回しかありません。" +
                        "全員の目標は同時に満たせず、目標割れか過剰配置が必ず出ます",
                b.IsRest ? $"「{b.Kigou}」の適切回数を下げるか、他シフトの個人下限を見直してください"
                    : $"「{b.Kigou}」の適切回数を下げるか、必要人数を増やしてください"));
        }

        // 6b) [幻のapt目標検知] 担当できるシフトの構成上、他の担当シフトの個人上限を守る限り強制される
        //   最低回数 > apt目標、という「達成不能な適切回数」を検出する。
        for (var i = 0; i < p.S; i++)
        {
            var name = Nm(i);
            for (var k = 0; k < p.K; k++)
            {
                var t = p.Apt[i][k];
                if (t < 0 || !p.CanDo(i, k)) continue;
                var otherHiSum = OtherShiftCapSum(p, i, k);
                var forcedMin = p.T - otherHiSum;
                if (forcedMin > t)
                {
                    var sym = Sym(k);
                    outList.Add(new SettingIssue(IssueKind.Range, $"{name} の「{sym}」適切回数",
                        $"担当できるシフトの構成上、他の担当シフトの個人上限（合計{otherHiSum}回）を守る限り「{sym}」は最低{forcedMin}回になります（{p.T}日を埋めきれないぶんが必ず回ってくる）。" +
                            $"適切回数{t}回との差{forcedMin - t}回は、個人上限を破って別のシフトへ逃がさない限り消えません（上限超過は上限違反として同じだけ残ります）",
                        $"「{sym}」の適切回数を{forcedMin}回以上にするか空欄にする、または他シフトの担当・上限を見直してください"));
                }
            }
        }

        // 6d) [希望固定>apt目標] 実現可能な固定希望の件数がそのシフトの適切回数目標を超えていれば、
        //   希望どおりに配置する限り目標超過は最適化では消せない。
        for (var i = 0; i < p.S; i++)
        {
            var name = Nm(i);
            for (var k = 0; k < p.K; k++)
            {
                var t = p.Apt[i][k];
                if (t < 0 || !p.CanDo(i, k)) continue;
                var wished = 0;
                for (var j = 0; j < p.T; j++) if (p.WishLocked(i, j) && p.Wish[i][j] == k) wished++;
                if (wished > t)
                {
                    var sym = Sym(k);
                    outList.Add(new SettingIssue(IssueKind.Range, $"{name} の「{sym}」適切回数と希望",
                        $"「{sym}」の希望が{wished}件あり、適切回数の目標{t}回を超えています。希望どおりに配置する限り" +
                            $"「{sym}」は必ず{wished}回以上になるため、差{wished - t}回ぶんの超過は最適化では消せません",
                        $"「{sym}」の適切回数を{wished}回以上にするか、{name}さんの「{sym}」の希望を{wished - t}件減らしてください"));
                }
            }
        }

        // 6c) [幻のhigh超過+代用要員] 6bと同じ「担当構成が強制する最低回数」ロジックを個人上限(staffRange
        //   の hi)にも適用し、構造的に上限を守れない場合はそのシフトを担当から外し代用要員へ置き換える
        //   ことを提案する。
        for (var i = 0; i < p.S; i++)
        {
            var name = Nm(i);
            for (var k = 0; k < p.K; k++)
            {
                var hi = p.RangeHi[i][k];
                if (hi == int.MaxValue || !p.CanDo(i, k)) continue;
                var otherHiSum = OtherShiftCapSum(p, i, k);
                var forcedMin = p.T - otherHiSum;
                if (forcedMin > hi)
                {
                    var sym = Sym(k);
                    var substitutes = new List<string>();
                    for (var s = 0; s < p.S; s++)
                        if (s != i && p.CanDo(s, k)) substitutes.Add(Nm(s));
                    var subText = substitutes.Count == 0 ? "代用できる他の担当者がいません"
                        : $"代用要員候補: {string.Join("・", substitutes)}";
                    outList.Add(new SettingIssue(IssueKind.Range, $"{name}さんの「{sym}」上限と担当構成の衝突",
                        $"担当できるシフトの構成上、「{sym}」は最低{forcedMin}回になります（他の担当シフトの上限合計{otherHiSum}回では{p.T}日を埋めきれません）が、{name}さんの「{sym}」上限は{hi}回です。この人が担当を続ける限り上限超過は必ず出ます。{subText}",
                        $"{name}さんを「{sym}」の担当から外し代用要員に置き換えるか、上限を{forcedMin}回以上に上げてください"));
                }
            }
        }

        // 7) 配布不可・forcedCovU
        foreach (var fc in ForcedCovU(state, p))
        {
            outList.Add(new SettingIssue(IssueKind.Demand, $"「{fc.ShiftSymbol}」の担当者不足（配布不可の原因）",
                $"{fc.Cells}日で、担当できる人数より必要人数が多く、人員不足(covU)が必ず出ます（不足の合計{fc.Amount}）。この不足は最適化では解消できません",
                $"「{fc.ShiftSymbol}」を担当できる職員を増やすか、その日の必要人数を下げてください"));
        }

        // 8) 重複定義
        {
            // [Kotlin/.NET 差異メモ] `groupingBy{it}.eachCount()` は Kotlin の HashMap ベースで、その
            //   反復順は Kotlin 自身の契約でも保証されない（ある JVM 版数で安定して見えるだけ）。
            //   C# の GroupBy は入力の最初の出現順で確定的にグループを生成する — 同値のどちらの実装も
            //   「重複がある」という中身は変えず、複数の重複グループが同時にあるときの表示順序だけが
            //   Kotlin より安定する側に倒れる、という差異のみ。
            List<string> Dups(IEnumerable<string> items) =>
                items.Where(s => !string.IsNullOrWhiteSpace(s))
                    .GroupBy(s => s)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .ToList();

            foreach (var d in Dups(state.StaffList.Select(s => CsvUtil.NameMatchKey(s.Name))))
            {
                outList.Add(new SettingIssue(IssueKind.Constraint, $"職員名の重複「{d}」",
                    "同名（空白を除き一致）の職員が複数います。制約とCSV取込は最初の1人に解決され、2人目以降は区別できません",
                    "氏名を一意にしてください（例: 姓名の間や末尾に識別子を付ける）"));
            }
            foreach (var d in Dups(state.Shifts.Select(s => s.Kigou.Trim())))
            {
                outList.Add(new SettingIssue(IssueKind.Constraint, $"シフト記号の重複「{d}」",
                    "同じ記号のシフトが複数あります。制約とCSV取込は最初の1件に解決され、2件目以降は参照されません",
                    "シフト記号を一意にしてください"));
            }
            foreach (var d in Dups(state.Groups.Select(g => g.Kigou.Trim())))
            {
                outList.Add(new SettingIssue(IssueKind.Constraint, $"グループ記号の重複「{d}」",
                    "同じ記号のグループが複数あります。制約とCSV取込は最初の1件に解決されます",
                    "グループ記号を一意にしてください"));
            }
            foreach (var d in Dups(state.SkillGroups.Select(g => g.Kigou.Trim())))
            {
                outList.Add(new SettingIssue(IssueKind.Constraint, $"スキルグループ記号の重複「{d}」",
                    "同じ記号のスキルグループが複数あります。制約とCSV取込は最初の1件に解決されます",
                    "スキルグループ記号を一意にしてください"));
            }
        }

        // 9) ConstraintMus（証明つき矛盾。piece 13 参照）
        {
            string ItemLabel(ConstraintMus.Item it) => it switch
            {
                ConstraintMus.WishPin wp => $"希望「{Nm(wp.Staff)} {SafeDayLabel(state.StartDate, wp.Day)}={Sym(wp.Shift)}」",
                ConstraintMus.RangeCap rc => $"個人上限「{Sym(rc.Shift)}を最大{rc.Hi}回」",
                ConstraintMus.RangeFloor rf => $"個人下限「{Sym(rf.Shift)}を最低{rf.Lo}回」",
                ConstraintMus.WindowRule wr => $"窓ルール「{Sym(wr.Shift)}を{wr.WindowDays}日で{wr.MinCount}回以上」",
                ConstraintMus.DayNeed dn => $"必要人数「{Sym(dn.Shift)}に{dn.Need}人」",
                _ => throw new ArgumentOutOfRangeException(nameof(it), it, "unknown ConstraintMus.Item subtype"),
            };
            string RelaxHint(ConstraintMus.Item it) => it switch
            {
                ConstraintMus.WishPin wp => $"{Nm(wp.Staff)}さんの{SafeDayLabel(state.StartDate, wp.Day)}の希望を調整する",
                ConstraintMus.RangeCap rc => $"「{Sym(rc.Shift)}」の個人上限を上げる",
                ConstraintMus.RangeFloor rf => $"「{Sym(rf.Shift)}」の個人下限を下げる",
                ConstraintMus.WindowRule wr => $"窓ルール「{Sym(wr.Shift)} {wr.WindowDays}日で{wr.MinCount}回以上」の回数を下げる",
                ConstraintMus.DayNeed dn => $"{Sym(dn.Shift)}の必要人数を下げる",
                _ => throw new ArgumentOutOfRangeException(nameof(it), it, "unknown ConstraintMus.Item subtype"),
            };
            bool HasWish(IReadOnlyList<ConstraintMus.Item> core) => core.Any(it => it is ConstraintMus.WishPin);

            foreach (var sc in ConstraintMus.AnalyzeStaffConflicts(p).Where(x => HasWish(x.Core)).OrderBy(x => x.Core.Count).Take(3))
            {
                var name = Nm(sc.Staff);
                var labels = string.Join(" ・ ", sc.Core.Select(ItemLabel));
                var hints = string.Join(" / ", sc.Core.Take(2).Select(RelaxHint));
                outList.Add(new SettingIssue(IssueKind.Wish, $"{name}さんの希望と条件の組合せ",
                    $"次の{sc.Core.Count}件は同時に成立しません（証明つき）: {labels}",
                    $"いずれか1件を緩めてください（例: {hints}）"));
            }
            foreach (var dc in ConstraintMus.AnalyzeDayConflicts(p).Where(x => HasWish(x.Core)).OrderBy(x => x.Core.Count).Take(3))
            {
                var labels = string.Join(" ・ ", dc.Core.Select(ItemLabel));
                var wishItem = dc.Core.FirstOrDefault(it => it is ConstraintMus.WishPin);
                var wishHint = wishItem is null ? null : RelaxHint(wishItem);
                outList.Add(new SettingIssue(IssueKind.Wish, $"{SafeDayLabel(state.StartDate, dc.Day)} の必要人数と固定希望の衝突",
                    $"固定された希望の組合せでは、この日の必要人数を満たせません。次の{dc.Core.Count}件は同時に成立しません（証明つき）: {labels}",
                    "この日の希望を1件調整するか、必要人数を下げてください" + (wishHint is null ? "" : $"（例: {wishHint}）")));
            }
        }

        // SOFT違反の合計が過大
        {
            var soft = new Evaluator(p).FullEvalParts(ScheduleUtil.NormalizeSchedule(state.Schedule.ToIntArray2D(), p))[1];
            if (soft >= 900_000L)
            {
                outList.Add(new SettingIssue(IssueKind.Constraint, $"SOFT違反の合計が過大（{soft}）",
                    "調整項(SOFT)の合計がスコア上限 1,000,000 に接近しており、必須(HARD)違反ゼロを最優先する評価が崩れる恐れがあります",
                    "解消不能な制約（回数>日数の連勤条件など）や、多数の同時禁止(C42)・広すぎる範囲制約を見直して調整項を減らしてください"));
            }
        }

        return outList
            .OrderBy(iss => iss.Where.Contains("配布不可") ? 0
                : iss.Kind == IssueKind.Wish ? 1
                : iss.Kind == IssueKind.Demand ? 2
                : iss.Kind == IssueKind.Range ? 3
                : 4)
            .ToList();
    }

    /// <summary>Faithful port of Kotlin's private <c>c3FamilyJp</c>.</summary>
    private static string C3FamilyJp(string fam) => fam switch
    {
        "c3" => "必須の並び",
        "c3n" => "禁止の並び",
        "c3m" => "推奨の並び",
        "c3mn" => "回避の並び",
        _ => fam,
    };

    /// <summary>
    /// Faithful port of Kotlin's private <c>findDuplicateSeqConstraints</c> — pulled forward from
    /// its Kotlin source position (<c>V6SanityPort.kt:1444-1464</c>, textually near the end of the
    /// file) into this piece because <see cref="BuildGuidance"/> (section 2) calls it directly.
    /// </summary>
    private static List<string> FindDuplicateSeqConstraints(MagiState state)
    {
        var outList = new List<string>();
        CollectDuplicateSeq("c3", state.Cons3, outList);
        CollectDuplicateSeq("c3n", state.Cons3n, outList);
        CollectDuplicateSeq("c3m", state.Cons3m, outList);
        CollectDuplicateSeq("c3mn", state.Cons3mn, outList);
        return outList;
    }

    /// <summary>Faithful port of Kotlin's private <c>collectDuplicateSeq</c>.</summary>
    private static void CollectDuplicateSeq(string name, IReadOnlyList<C3Row> rows, List<string> outList)
    {
        var seen = new HashSet<string>();
        foreach (var r in rows)
        {
            var parts = new List<string>();
            foreach (var item in r.Pattern)
            {
                if (string.IsNullOrWhiteSpace(item)) break;
                parts.Add(item);
            }
            var key = string.Join("→", parts);
            if (string.IsNullOrWhiteSpace(key)) continue;
            if (!seen.Add(key)) outList.Add($"{name}:{key}");
        }
    }
}
