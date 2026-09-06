using System.Globalization;
using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// 充足不可(infeasible)＝担当可能な職員数が必要数に届かない / 充足可能(fixable)＝枠は足りるが最適化が未到達。
/// </summary>
public enum CoverageVerdict { Infeasible, Fixable }

/// <summary>人員不足(covU)が残る 1 つの (日, シフト) 枠の診断。読み取り専用・エンジン非変更。</summary>
public sealed record CoverageShortfall(
    int DayIndex,
    string DayLabel,
    int ShiftIndex,
    string ShiftSymbol,
    int Need,
    int Got,
    int Miss,
    /// <summary>その日そのシフトに法的に配置し得る最大職員数（担当可 かつ 別シフトへ希望固定されていない）。</summary>
    int Capacity,
    CoverageVerdict Verdict,
    string Reason,
    /// <summary>
    /// [3.344.0] <b>いまの希望・盤面のままでは埋められない</b>と実証した枠。
    ///
    /// <see cref="Verdict"/> は「担当できる人数 &gt;= 必要数」という静的判定なので、希望を1件でも変えれば
    /// 直りうる枠は Fixable のまま残す（3.263.0 の意図的な区別）。だがそのせいで、サマリが「充足可能3枠」と
    /// 言いながら各枠の説明は「現在の希望のままではどう組んでも解消できません」という矛盾したメッセージに
    /// なっていた。判定は Reason と同じ根拠（空き番が居るか／findCovUChain で玉突きが実在するか）で、
    /// 文字列でなく値として持つ。
    /// </summary>
    bool BlockedNow = false);

/// <summary>人員過剰(covO)が残る 1 つの (日, シフト) 枠の診断。読み取り専用・エンジン非変更。</summary>
public sealed record CoverageSurplus(
    int DayIndex,
    string DayLabel,
    int ShiftIndex,
    string ShiftSymbol,
    int Need,
    int Got,
    int Excess,
    /// <summary>
    /// [3.406.0] 構造的には動かせるのに、1人動かす手がどれも目的関数に負けるとき、いちばん重く悪化した族
    /// （<c>MirrorKeys</c> の生キー・負けなかった/試していないなら null）。画面は breakdownLabels 相当で
    /// 日本語にして出す（ログは C1Plateau と同じく生キーのまま）。
    /// <para>
    /// Kotlin 側は <c>blockedFamily: String? = null</c> として <c>reason</c> より前に宣言されるが（C# の
    /// positional record では既定値つきパラメータは末尾でなければならない）、production/test 双方の唯一の
    /// 呼び出し元は必ず全フィールドを明示的に渡すため、この移植では既定値を落として必須の位置引数にする
    /// （挙動は完全に同一、実際に使われない Kotlin 側の default に対する忠実さは失わない）。
    /// </para>
    /// </summary>
    string? BlockedFamily,
    /// <summary>この枠の在勤者中、他シフトへ動かせる／動かせない内訳と理由。</summary>
    string Reason,
    /// <summary>
    /// [3.492.0 移植元] この枠を本人希望で固定している在勤者（WishLocked かつ希望＝このシフト）。Reason の
    /// 「希望固定N人」の中身。画面は「誰の希望を取り消せば解消に近づくか」を名指しする（WinUI 側の表示は未配線）。
    /// </summary>
    IReadOnlyList<int>? PinnedStaff = null);

/// <summary>covU(人員不足)の原因診断。どの枠が「数学的に充足不可」か「充足可能だが未到達」かを切り分ける。</summary>
public sealed record CoverageDiagnosis(
    int TotalShortfall,
    int InfeasibleSlots,
    int FixableSlots,
    IReadOnlyList<CoverageShortfall> Shortfalls,
    /// <summary>[IIS/緩和案] 担当追加で解ける見込みの提案（データは変えない）。</summary>
    IReadOnlyList<string> Relaxations,
    /// <summary>[人員過剰(covO)の「なぜ減らないか」診断] Shortfalls と対の存在。データは変えない。</summary>
    int TotalSurplus,
    IReadOnlyList<CoverageSurplus> Surpluses)
{
    public bool HasShortage => TotalShortfall > 0;

    /// <summary>不足が全て「充足不可」＝このデータでは HARD=0 にできない（想定内の残存）。</summary>
    public bool AllInfeasible => HasShortage && FixableSlots == 0;

    /// <summary>[3.344.0] 「いまの希望のままでは埋められない」と実証した枠の数（AllInfeasible とは別軸）。</summary>
    public int BlockedNowSlots => Shortfalls.Count(s => s.BlockedNow);

    /// <summary>
    /// [3.344.0] 不足枠が全部「充足不可 or いまの希望のままでは不能」＝この希望・担当のままでは
    /// どう探索しても covU は減らない。AllInfeasible（データ上の不能）より広く、実データではこちらが真になる。
    /// </summary>
    public bool AllBlockedNow => HasShortage &&
        Shortfalls.All(s => s.Verdict == CoverageVerdict.Infeasible || s.BlockedNow);

    public bool HasSurplus => TotalSurplus > 0;

    /// <summary>診断ログ（エクスポートされる「MAGI ログ」に載る形式の文字列）。</summary>
    public IReadOnlyList<string> LogLines()
    {
        var outLines = new List<string>();
        if (HasShortage)
        {
            outLines.Add($"[W] CoverageDiag: 人員不足 合計{TotalShortfall} — 充足不可{InfeasibleSlots}枠 / 充足可能{FixableSlots}枠" +
                (BlockedNowSlots > 0 ? $"（うち{BlockedNowSlots}枠は いまの希望のままでは不能）" : "") +
                (AllBlockedNow ? " ＝この希望・担当のままでは人員不足は減りません" : ""));
            foreach (var s in Shortfalls.Take(8))
            {
                var v = s.Verdict == CoverageVerdict.Infeasible ? "充足不可" : "充足可能";
                outLines.Add($"[W] CoverageDiag: {s.DayLabel} {s.ShiftSymbol} 必要{s.Need}/現状{s.Got}(不足{s.Miss}) — {v}: {s.Reason}");
            }
            if (Shortfalls.Count > 8) outLines.Add($"[W] CoverageDiag: ほか{Shortfalls.Count - 8}枠");
            foreach (var r in Relaxations.Take(4)) outLines.Add($"[W] CoverageDiag 緩和案: {r}");
        }
        if (HasSurplus)
        {
            outLines.Add($"[W] CoverageDiag: 人員過剰 合計{TotalSurplus} — {Surpluses.Count}枠（なぜ減らないか）");
            foreach (var s in Surpluses.Take(8))
            {
                var fam = s.BlockedFamily is not null ? $"（主因 {s.BlockedFamily}）" : "";
                outLines.Add($"[W] CoverageDiag: {s.DayLabel} {s.ShiftSymbol} 必要{s.Need}/現状{s.Got}(過剰{s.Excess}) — {s.Reason}{fam}");
            }
            if (Surpluses.Count > 8) outLines.Add($"[W] CoverageDiag: ほか{Surpluses.Count - 8}枠（過剰）");
        }
        return outLines;
    }
}

public static partial class V6PortAnalyzer
{
    /// <summary>
    /// [Android 3.503.0] 診断が探索本体の関数を「実際に試す」ときの回数・予算。値は従来どおり（HF77: 変えていない）。
    /// 8 seed は rng 順に依存する網羅性の揺らぎを吸収する数（実データ 200 seed 総当たりと一致した最小の実用値、3.263.0）。
    /// 過剰プローブ 240 は checker 約 72µs で数 ms に収まる上限。
    /// </summary>
    private static class Probe
    {
        public const int ChainSeeds = 8;
        public const int SurplusProbeBudget = 240;
        public const long AdjacentSeed = 7L;
        public const int MinRelaxCandidates = 2;
    }

    /// <summary><c>FindCovUChain</c>（探索本体と同一関数）を <see cref="Probe.ChainSeeds"/> 通りの rng 順で試し、1 つでも成立すれば真。</summary>
    private static bool ChainFills(Problem p, int[][] board, int k, int j, int exclude = -1) =>
        Enumerable.Range(0, Probe.ChainSeeds).Any(seed => V6SearchOperators.FindCovUChain(p, board, k, j, new JavaRandom(seed), exclude: exclude) is not null);

    /// <summary>
    /// 人員不足(covU)の枠ごとの原因診断。エンジンは変更せず、現在の解だけを読み取り、
    /// 各不足枠について「担当可能な職員の最大数(capacity)」を数え、必要数に届くかで判定する。
    ///  - capacity &lt; need → Infeasible（どう割り当ててもこの枠は埋まらない＝データ上充足不可）
    ///  - capacity &gt;= need → Fixable（枠は足りる。他シフトに就いている人を移せば理論上は解消し得るが、
    ///    並び/回数などの制約に阻まれ最適化が未到達）
    /// [Android 3.503.0] 不足（<see cref="DiagnoseShortfalls"/>）・緩和案（<see cref="BuildRelaxations"/>）・過剰（<see cref="DiagnoseSurpluses"/>）に分割。出力は不変。
    /// </summary>
    public static CoverageDiagnosis DiagnoseCoverage(
        MagiState state,
        int[][]? schedule = null,
        ViolationReport? report = null)
    {
        var sched = schedule ?? state.Schedule.ToIntArray2D();
        var rep = report ?? UnifiedViolationChecker.Check(state, sched);
        var p = ScheduleUtil.CachedProblem(state);
        var norm = ScheduleUtil.NormalizeSchedule(sched, p);
        var cov = ScheduleUtil.Coverage(p, norm);
        var (list, total, infeasible, fixable) = DiagnoseShortfalls(state, p, norm, cov);
        var relaxations = BuildRelaxations(state, p, norm, list);
        var (surplusList, totalSurplus) = DiagnoseSurpluses(state, p, norm, cov, rep);
        return new CoverageDiagnosis(total, infeasible, fixable, list, relaxations, totalSurplus, surplusList);
    }

    /// <summary>不足枠ごとに capacity と「なぜ今動かせないか」の 5 分類（在勤/空き番/玉突き/希望固定/禁止連続）。Infeasible→miss 降順。</summary>
    private static (List<CoverageShortfall> List, int Total, int Infeasible, int Fixable) DiagnoseShortfalls(MagiState state, Problem p, int[][] norm, int[][] cov)
    {
        // [なぜ埋まらないか / 三連・五連など任意長対応] 職員 i を日 j にシフト newK へ動かすと
        //   禁止連続(c3n)を作るか。Problem.MakesForbiddenRun が任意長ルールを一般判定する。
        bool C3nAt(int i, int j, int newK) => p.MakesForbiddenRun(norm, i, j, newK);

        var list = new List<CoverageShortfall>();
        var infeasible = 0;
        var fixable = 0;
        var total = 0;
        for (var j = 0; j < p.T; j++)
        {
            for (var k = 0; k < p.K; k++)
            {
                // [監査/実バグ修正] need1 のみを見て miss=need1-got を計算していたため、need1 が未設定で
                //   need2 単独定義のセル（Problem.CovUCell の「片方定義=その値」対応セル）が丸ごと
                //   スキップされ、本物の covU(HARD) 違反が診断から完全に消えていた（need2<need1 の
                //   OR救済無視という既知の理論的エッジケースより広く、通常のデータでも起こり得る）。
                //   CovUCell（source of truth）を直接使い、need1/need2 双方の OR 意味論に一致させる。
                var got = cov[j][k];
                var miss = p.CovUCell(k, j, got);
                if (miss <= 0) continue;
                var need = got + miss;   // [表示用] 実際に不足を生んだ実効しきい値（CovUCellのOR選択と整合）
                total += miss;
                var capacity = 0;
                for (var i = 0; i < p.S; i++)
                {
                    if (!p.MayPlace(i, k)) continue;
                    // [3.391.0] 生の `w != k` は実現不能な希望（担当できないシフトへの希望）まで
                    //   「別シフトへ固定」として capacity から外していた。実現不能な希望は凍結しない
                    //   （WishLocked の規約）ので、その職員はこの枠へ回せる。過小な capacity は
                    //   verdict を Fixable→Infeasible へ倒し「データ上、充足不可」という誤った断定を生む。
                    if (p.WishLocked(i, j) && p.Wish[i][j] != k) continue;   // 実現可能な希望が別シフト → この枠には回せない
                    capacity++;
                }
                var verdict = capacity < need ? CoverageVerdict.Infeasible : CoverageVerdict.Fixable;
                if (verdict == CoverageVerdict.Infeasible) infeasible++; else fixable++;
                var sym = k >= 0 && k < state.Shifts.Count ? state.Shifts[k].Kigou : k.ToString();
                // [3.344.0] reason と同じ根拠で「いまの希望のままでは埋められない」かを値として持つ。
                var blockedNow = false;
                string reason;
                if (verdict == CoverageVerdict.Infeasible)
                {
                    reason = $"担当可能な職員が{capacity}人で必要数{need}に届きません（データ上、充足不可）";
                }
                else
                {
                    // [なぜ埋まらないか] 「移せる候補」(canDo・別シフト希望でない)を、なぜ今動かせないかで
                    //   5分類する。既に在勤=capacity には入るが移動候補ではない / 空き番=休/過剰から直接移せる /
                    //   玉突き=引くと別のcovU / 希望固定=本人の希望で固定 / 禁止連続=移すと c3n。読取専用・スコア不変。
                    //   [敵対的レビュー修正] already を明示計上し free+cascade+pinned+forbid+already==capacity を
                    //   保証（旧: already を素通り=capacity と内訳合計が一致せず表示が混乱を招いた）。
                    // 「希望固定」は上の事前フィルタ（別シフトへの実現可能な希望＝capacity 対象外）で既に除外済み＝ここでは常に 0。
                    const int pinned = 0;
                    var already = 0; var free = 0; var cascade = 0; var forbid = 0;
                    for (var i = 0; i < p.S; i++)
                    {
                        if (!p.MayPlace(i, k)) continue;
                        var m = norm[i][j];
                        // [3.391.0] 上の capacity と同じ事前フィルタ＝同じ条件に揃える（WishLocked）。
                        if (p.WishLocked(i, j) && p.Wish[i][j] != k) continue;   // 実現可能な希望が別シフト=capacity 対象外
                        if (m == k) { already++; continue; }                    // 既にこのシフト=移す対象でない
                        if (C3nAt(i, j, k)) { forbid++; continue; }
                        // m から1人引くと covU が増える=玉突き（多人数入替=連鎖でしか解けない）。
                        if (m is >= 0 && m < p.K && p.CovUCell(m, j, cov[j][m] - 1) > p.CovUCell(m, j, cov[j][m]))
                            cascade++;
                        else
                            free++;
                    }
                    // [3.263.0, 深い停滞調査(600秒改善ゼロ)で判明] 「玉突き」は1ホップ判定
                    // （このセルへの直接移動は別のcovUを生む）に過ぎず、その先が実際に埋まる保証が
                    // 無かった。実データ検証(findCovUChainを200 seed総当たり)で「玉突き候補はいる
                    // のに、その先を埋める人が全員その日の希望で固定されており実際は誰一人動かせ
                    // ない」という真の壁を確認済み（pref(重み9000)>covU(重み8000)のため、希望を
                    // 破ってまでcovUを直す手はisBetterが正しく却下する＝バグではない）。診断が
                    // 「玉突きが必要」と楽観的に言うだけでは、この壁を「もっと粘れば直る」との
                    // 誤解を招くため、findCovUChain（探索本体と同一の関数）で実在を確認してから
                    // 案内を出し分ける。複数seedを試すのは、rng順（候補の並べ替え）に依存する
                    // 網羅性の揺らぎを吸収し安定した判定にするため（実データで200 seed総当たりし
                    // 全て不成立だった局面を確認済み・8 seedは診断呼出コストとのバランス）。
                    var chainVerified = cascade > 0 && ChainFills(p, norm, k, j);
                    blockedNow = free == 0 && !(cascade > 0 && chainVerified);
                    string hint;
                    if (free > 0)
                        hint = $"空き番{free}人を{sym}へ移せば充足（最適化が未到達＝勤務表でこのセルの『直し方を探す』で解消可）";
                    else if (cascade > 0 && chainVerified)
                        hint = "空き番が無く、過剰シフトからの多人数入替（玉突き=ブロック移動）が必要";
                    else if (cascade > 0)
                        hint = $"玉突き候補{cascade}人はいますが、移動先の受け皿もすべて希望固定/禁止連続で塞がっており、" +
                            "現在の希望のままではどう組んでも解消できません。希望を1件調整するか担当を追加してください";
                    else
                        hint = "候補が希望/禁止連続で塞がっており、希望を1件調整するか担当を追加すると解消に近づく";
                    reason = $"担当可能{capacity}人（うち在勤中{already}人）・今動かせる空き番{free}人（玉突き{cascade}・希望固定{pinned}・禁止連続{forbid}）。{hint}";
                }
                list.Add(new CoverageShortfall(j, DayLabel(state.StartDate, j), k, sym, need, got, miss, capacity,
                    verdict, reason, blockedNow));
            }
        }
        // Kotlin の sortWith は安定ソート。C# List<T>.Sort は不安定なので LINQ の OrderBy 系（安定）で揃える。
        list = list
            .OrderByDescending(s => s.Verdict == CoverageVerdict.Infeasible)
            .ThenByDescending(s => s.Miss)
            .ToList();

        return (list, total, infeasible, fixable);
    }

    /// <summary>
    /// [緩和案/IIS] 構造的に充足不可なシフトについて、担当追加(クロストレーニング)で解ける見込みを提示する。
    /// 候補は未活用(需要のあるシフトへの稼働が少ない)職員を優先。これは担当追加の「提案」であってデータは一切変更しない（採否は業務担当者が判断）。HF77準拠。
    /// </summary>
    private static List<string> BuildRelaxations(MagiState state, Problem p, int[][] norm, List<CoverageShortfall> shortfalls)
    {
        var relaxations = new List<string>();
            // [同根修正] need1 単独判定だと need2 単独定義シフトの需要を見落とす（上の miss 計算と同じ穴）。
            var demandShifts = Enumerable.Range(0, p.K)
                .Where(kk => Enumerable.Range(0, p.T).Any(jj => p.Need1[kk][jj] > 0 || (p.Use2 && p.Need2[kk][jj] > 0)))
                .ToHashSet();
            int DemandLoad(int i) => Enumerable.Range(0, p.T).Count(jj => demandShifts.Contains(norm[i][jj]));

            var infeasByShift = shortfalls.Where(s => s.Verdict == CoverageVerdict.Infeasible)
                .GroupBy(s => s.ShiftIndex)
                .Select(g => (Shift: g.Key, PeakMiss: g.Max(s => s.Miss)))
                .OrderByDescending(x => x.PeakMiss);

            foreach (var (k, peakMiss) in infeasByShift)
            {
                var sym = k >= 0 && k < state.Shifts.Count ? state.Shifts[k].Kigou : k.ToString();
                var cands = Enumerable.Range(0, p.S)
                    .Where(i => !p.CanDo(i, k))
                    .OrderBy(DemandLoad)
                    .Take(Math.Max(peakMiss + 1, Probe.MinRelaxCandidates))
                    .Select(i => i >= 0 && i < state.StaffList.Count ? state.StaffList[i].Name : $"#{i}")
                    .ToList();
                if (cands.Count > 0)
                {
                    relaxations.Add($"「{sym}」は担当可能者が不足（ピーク不足{peakMiss}人）。{sym} を {string.Join("・", cands)}（稼働が少なめ）に担当追加すると解消に近づきます");
                }
            }
        return relaxations;
    }

    /// <summary>
    /// [人員過剰(covO)の「なぜ減らないか」診断] covU診断(空き番/玉突き/希望固定/禁止連続)の対。在勤者を他シフトへ動かせば消えるはずの過剰が、
    /// なぜ最適化で解消されないかを枠ごとに示す。covO は全19族中もっとも軽い(重み1.0)ため、動かした先で他の族が1点でも悪化すると isBetter に負ける
    /// ＝件数自体は「動かせるか」の構造診断であり「動かせるのに動いていない」ことの説明にはならない。[3.406.0] だから同じ目的関数で実際に 1 手試してから言う。
    /// </summary>
    private static (List<CoverageSurplus> List, int Total) DiagnoseSurpluses(MagiState state, Problem p, int[][] norm, int[][] cov, ViolationReport report)
    {
        bool C3nAt(int i, int j, int newK) => p.MakesForbiddenRun(norm, i, j, newK);
        var surplusList = new List<CoverageSurplus>();
        var totalSurplus = 0;
        // [3.406.0] 「動かせる」を目的関数で実際に試すための作業盤面と予算。checker は約72µs(3.395.0)なので
        //   実データ規模（過剰11枠×候補数人）なら数msに収まるが、上限を切って UI の再チェックを重くしない。
        var probe = norm.Copy2D();
        var probeBudget = Probe.SurplusProbeBudget;
        for (var j = 0; j < p.T; j++)
        {
            for (var k = 0; k < p.K; k++)
            {
                var got = cov[j][k];
                var excess = p.CovOCell(k, j, got);
                if (excess <= 0) continue;
                var need = got - excess;
                totalSurplus += excess;
                var sym = k >= 0 && k < state.Shifts.Count ? state.Shifts[k].Kigou : k.ToString();
                var pinned = 0; var forbid = 0; var cascade = 0; var free = 0;
                var pinnedIdx = new List<int>();
                // [3.406.0] 構造的に動かせる(free)ことと、最適化が採ることは別。covO は最も軽い族(重み1.0)で、
                //   移動先で他の族が1点でも悪化すると betterReport に負ける——すぐ上のコメント自身が
                //   「動かせるのに動いていない」ことの説明にはならないと書いているのに、下の hint は
                //   「最適化が未到達＝『直し方を探す』で解消可」と断言していた（3.401.0 の GuidedFix、
                //   3.344.0 の covU 側と同じ「診断が守れない約束をする」型）。実機ログ(2026-08-19)では
                //   covO 焦点の修復が275秒走ってなお 8件が残り、断言が実測に裏切られている。
                //   そこで同じ目的関数で実際に1手試してから言う。
                var freeImproving = 0;
                var probedAny = false;
                var famHits = new Dictionary<string, int>();   // 「主因」＝試した手のうち最も多く最重悪化を出した族
                for (var i = 0; i < p.S; i++)
                {
                    if (norm[i][j] != k) continue;   // このシフトの在勤者だけが移動候補
                    // [3.391.0] 実現不能な希望は凍結しない＝「希望固定で動かせない」と案内するのは誤り
                    //   （むしろ動かすと担当外セル=groupViol も同時に消える）。WishLocked へ統一。
                    if (p.WishLocked(i, j) && p.Wish[i][j] == k) { pinned++; pinnedIdx.Add(i); continue; }   // 実現可能な本人希望＝動かすとpref化
                    var alts = p.AllowedShiftsForStaff(i).Where(m => m != k).ToArray();
                    if (alts.Length == 0) { forbid++; continue; }      // 担当可能な代替シフトが無い
                    var hasRoom = false; var blockedByC3n = true;
                    foreach (var m in alts)
                    {
                        if (C3nAt(i, j, m)) continue;
                        blockedByC3n = false;
                        // m へ1人足しても covO が増えない＝受け皿あり。
                        if (p.CovOCell(m, j, cov[j][m] + 1) <= p.CovOCell(m, j, cov[j][m])) { hasRoom = true; break; }
                    }
                    if (hasRoom)
                    {
                        free++;
                        // 実際に1人動かして目的関数が改善するかを、最適化と同じ betterReport で確かめる。
                        foreach (var m in alts)
                        {
                            if (probeBudget <= 0) break;
                            if (C3nAt(i, j, m)) continue;
                            if (p.CovOCell(m, j, cov[j][m] + 1) > p.CovOCell(m, j, cov[j][m])) continue;
                            probeBudget--;
                            probedAny = true;
                            probe[i][j] = m;
                            var after = UnifiedViolationChecker.Check(state, probe);
                            probe[i][j] = k;
                            if (UnifiedViolationChecker.BetterReport(after, report)) { freeImproving++; break; }
                            var worst = V6SearchOperators.WorstWorsenedFamily(after, report);
                            if (worst is not null) famHits[worst] = famHits.GetValueOrDefault(worst) + 1;
                        }
                    }
                    else if (!blockedByC3n)
                    {
                        cascade++;   // 代替はあるが、どこも受け皿がない＝玉突きが必要
                    }
                    else
                    {
                        forbid++;    // 代替は全て禁止連続で塞がる
                    }
                }
                string hint;
                if (freeImproving > 0)
                    hint = $"在勤{freeImproving}人は他シフトへ移すだけで全体が良くなります（勤務表でこのセルの『直し方を探す』で解消できます）";
                else if (free > 0 && probedAny)
                    hint = "移せる先はありますが、1人動かす手はどれも他の条件を悪化させるため最適化は採用しません" +
                        "（この過剰を減らすには、その条件を緩めるか、過剰を受け入れる必要があります）";
                else if (free > 0)
                    hint = "移せる先はありますが、目的関数での確認は打ち切りました（枠が多いため）";
                else if (cascade > 0)
                    hint = "移動先はどこも定員一杯で、過剰シフトからの多人数入替（玉突き）が必要";
                else
                    hint = "在籍者は希望固定/禁止連続で動かせず、希望を1件調整するか担当を減らすと解消に近づく";

                var blockedFamily = freeImproving == 0 && probedAny && famHits.Count > 0
                    ? famHits.MaxBy(kv => kv.Value).Key
                    : null;
                surplusList.Add(new CoverageSurplus(j, DayLabel(state.StartDate, j), k, sym, need, got, excess,
                    blockedFamily,
                    $"在勤者中 動かせる{free}人・玉突き必要{cascade}人・希望固定{pinned}人・禁止連続{forbid}人。{hint}",
                    pinnedIdx));
            }
        }
        surplusList = surplusList.OrderByDescending(s => s.Excess).ToList();

        return (surplusList, totalSurplus);
    }

    /// <summary>
    /// <c>V6PortAnalyzer.kt</c> 末尾のトップレベル関数 <c>dayLabel</c> の移植。
    /// <see cref="V6SanityPort.SafeDayLabel"/> とほぼ同一（<c>java.time.LocalDate.parse</c> の厳格パース・
    /// 月曜始まりの曜日インデックス・失敗時 <c>"{offset+1}日"</c> への退避）だが、<b>負のオフセットを
    /// 拒否する <c>require(offset &gt;= 0)</c> ガードを持たない</b>という1点だけが異なる別関数。
    /// 意図的に統合しない（<c>SafeDayLabel</c> と <c>ScheduleUtil.FormatDay</c> を別関数のまま保った
    /// のと同じ規律）。この差は <c>diagnoseCoverage</c>/<c>diagnoseForbiddenCell</c>/<c>buildDayRisks</c>
    /// のいずれの呼び出しでも 0 以上のループ変数しか渡らないため実際には踏まれないが、真に負の値が
    /// 渡ればここだけ <c>AddDays</c> で本物の逆算日付を返す（ガードで即座に諦めない）。
    /// テストで直接ロックできるよう internal（Kotlin 側に直接のユニットテストは無いが、この相違点は
    /// 重要なので C# 側で新規に固定する）。
    /// </summary>
    internal static string DayLabel(string startDate, int offset)
    {
        try
        {
            if (!DateOnly.TryParseExact(startDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed))
                throw new FormatException($"'{startDate}' is not a valid yyyy-MM-dd date");
            var d = parsed.AddDays(offset);
            var weekday = "月火水木金土日"[((int)d.DayOfWeek + 6) % 7];
            return $"{d.Month}/{d.Day}({weekday})";
        }
        catch (Exception)
        {
            return $"{offset + 1}日";
        }
    }
}
