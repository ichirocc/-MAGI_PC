using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// [V6HotfixPasses / フェーズ6, C3mn（回避の並び）研磨] Kotlin原本 <c>V6HotfixPasses.kt</c> の
/// <c>applyC3mnPolish</c>（cons3mn＝禁止ではない「回避」パターンの研磨）＋その共有ヘルパ
/// <c>exceedsOwnRangeHi</c>／<c>stuckStaffNames</c> を収める partial ファイル。
/// </summary>
public static partial class V6HotfixPasses
{
    /// <summary>
    /// [頭打ち調査・findCovUChainのrangeAvoid用] 候補(staff)がfillShiftを1つ得ると自身のstaffRange上限
    /// (rangeHi)を新たに超えるか。桒澤美幸のAｱ超過が研磨後も残る実例を追跡した結果、findCovUChainの
    /// 候補選定がコスト無視（構造的に妥当な最初の1件で確定）なため、「別の職員の新規high違反」で
    /// 相殺され isBetter に却下される手を引き続けて頭打ちになるケースを確認。C3mnPolish/RangePolish/
    /// C3RunPolishの3箇所で findCovUChain 呼出に渡し、そのような候補を後回し（除外はしない）にする。
    /// </summary>
    private static bool ExceedsOwnRangeHi(Problem p, int[][] work, int staff, int fillShift)
    {
        var hi = p.RangeHi[staff][fillShift];
        if (hi == int.MaxValue) return false;
        var c = 0;
        for (var jj = 0; jj < p.T; jj++) if (work[staff][jj] == fillShift) c++;
        return c + 1 > hi;
    }

    /// <summary>[ログから職員が分かるように] cellFamiliesに famKey を含むセルの職員名を重複なく列挙（登場順）。</summary>
    private static List<string> StuckStaffNames(
        MagiState state, IReadOnlyDictionary<string, IReadOnlyList<string>> cellFamilies, string famKey)
    {
        var outSet = new HashSet<string>();
        var outList = new List<string>();
        foreach (var (key, fams) in cellFamilies)
        {
            if (!fams.Contains(famKey)) continue;
            var idx = KotlinInterop.ToIntOrNull(key.Split(',')[0]);
            if (idx == null) continue;
            var name = idx.Value >= 0 && idx.Value < state.StaffList.Count ? state.StaffList[idx.Value].Name : $"#{idx.Value}";
            if (outSet.Add(name)) outList.Add(name);
        }
        return outList;
    }

    /// <summary>
    /// [C3mnPolish・玉突き連鎖の横展開] cons3mn(回避パターン, SOFT重み30)専用の研磨パス。
    /// grilling(2026-07-19)で確定: 対象はc3mnのみ(c3nはHARDで既存のRSI focus優先/keep-bestが担当済み・
    /// 同一パスに混ぜると役割が重複し測定しづらくなる)。既存の<see cref="V6SearchOperators.FindCovUChain"/>
    /// （玉突き連鎖BFS、深さ5まで）をそのまま再利用し、C1Polish(3.158.0)の「手B/E11」ブロックと同型の構成にする。
    ///
    /// 動機（金沢勇輝の実例, 実機ログ2026-07-19）: cons3n(HARD)がDﾃ直後の主要シフトを軒並み禁止するため、
    /// Dﾃを複数回持つ職員はDﾃを連続させるのが安全側になりやすく、cons3mnの「N連続回避」パターンに
    /// ヒットしたまま残ることがある。休を追加すればhigh違反(weight90)の方が高くつく局面では崩せないため、
    /// 「その職員自身のDﾃ/休の回数を変えずに、そのセルだけ他シフトへ動かす」手が必要——これはまさに
    /// findCovUChainが対応する「直接候補が全員(希望固定/禁止連続/被覆)でブロックされる」局面と同型。
    ///
    /// アンカー: [レビュー3.111.0系]と同じ理由でcellFamilies(1セル=重み降順の全クラス)から"vio-c3mn"を含む
    /// セルを起点にする（violations単一クラスマップだと、より重い違反が同居するセルで見落としうるため）。
    /// 各アンカーセル(i,j)について、i の担当可能シフトへ付け替える(c3n新規発生はmakesForbiddenRunで事前枝刈り)。
    /// 付け替えで元シフトの被覆が悪化するなら<see cref="V6SearchOperators.FindCovUChain"/>で玉突き連鎖を試す
    /// (C1Polish手Bと同一パターン)。採否は既存のisBetter(hard→weighted→total)keep-best＝退化不能。
    /// 完了条件はユニットテストのみ(grilling決定)。
    /// </summary>
    public static CyclicSwapResult ApplyC3mnPolish(
        MagiState state, int[][] schedule, int maxPasses = 3, Func<bool>? shouldStop = null, long seed = 0xC3AL)
    {
        var stop = shouldStop ?? (() => false);
        // [3.326.0] 回数固定(lo==hi)だけが却下した候補試行を対象別に数える（緩和対象の提示用）。
        var pinBlocks = new PinBlockAttribution();
        var p = new Problem(state);
        var work = ScheduleUtil.NormalizeSchedule(schedule, p);
        var before = UnifiedViolationChecker.Check(state, work);
        var bestRep = before;
        var applied = 0;
        if (p.Cons3mn.Count == 0)
        {
            return new CyclicSwapResult(work, before.Total, bestRep.Total, 0,
                new[] { new MirrorLog(tag: "C3mnPolish", message: "cons3mnなし=スキップ") });
        }
        var rng = new JavaRandom(seed);
        // [監査で発見・3.270.0] p.wish[i][j]<0 は実現不能な希望まで動かせないと誤判定していた
        //   （3.183.0 LightMirrorOptimizer と同型のバグ）。wishLocked は canDo ガード込みで正しい。
        bool Movable(int i, int j) => !p.WishLocked(i, j);
        // [汎用玉突き結合フレームワーク, 3.249.0] 単独では不採用だった候補を蓄積し末尾で束ねる。
        var combinable = new List<CombinatorialRepair.Candidate>();
        var rejectCulprits = new RejectCulpritStats();
        var pass = 0;
        while (pass < maxPasses)
        {
            if (stop()) break;
            var improved = false;
            var rep0 = pass == 0 ? before : UnifiedViolationChecker.Check(state, work);
            var anchors = new List<(int I, int J)>();
            foreach (var (key, fams) in rep0.CellFamilies)
            {
                if (!fams.Contains("vio-c3mn")) continue;
                var parts = key.Split(',');
                var i = KotlinInterop.ToIntOrNull(parts.Length > 0 ? parts[0] : null);
                if (i == null) continue;
                var j = KotlinInterop.ToIntOrNull(parts.Length > 1 ? parts[1] : null);
                if (j == null) continue;
                anchors.Add((i.Value, j.Value));
            }
            if (anchors.Count == 0) break;
            foreach (var (i, j) in anchors)
            {
                if (stop()) break;
                if (!Movable(i, j)) continue;
                var curK = work[i][j];
                if (curK < 0 || curK >= p.K) continue;
                var done = false;
                foreach (var alt in p.AllowedShiftsForStaff(i))
                {
                    if (done || stop()) break;
                    if (alt == curK) continue;
                    if (p.MakesForbiddenRun(work, i, j, alt)) continue;
                    var cnt = 0;
                    for (var s = 0; s < p.S; s++) if (work[s][j] == curK) cnt++;
                    var needsChain = p.CovUCell(curK, j, cnt - 1) > p.CovUCell(curK, j, cnt);
                    // [監査で発見・3.270.0] isBetter は hard→weightedScore→total の辞書式のため、raw
                    //   total 改善だけでweightedScoreが悪化する組合せ(厳密ピン破り)がありうる。同型の
                    //   全パスに既に適用済みの exactPinRegression ガードをここにも追加（3.256.0 retrofit漏れ）。
                    var workBeforeMove = work.Copy2D();
                    work[i][j] = alt;
                    if (!needsChain)
                    {
                        var rep = UnifiedViolationChecker.Check(state, work);
                        var pinBad = V6SearchOperators.ExactPinRegression(p, workBeforeMove, work);
                        if (pinBad && IsBetter(rep, bestRep)) pinBlocks.Record(p, workBeforeMove, work);
                        if (IsBetter(rep, bestRep) && !pinBad) { bestRep = rep; applied++; improved = true; done = true; }
                        else
                        {
                            rejectCulprits.Record(rep, bestRep, pinBad);
                            var hint = $"{(i >= 0 && i < state.StaffList.Count ? state.StaffList[i].Name : $"#{i}")}" +
                                $"({(curK >= 0 && curK < state.Shifts.Count ? state.Shifts[curK].Kigou : curK.ToString())})";
                            combinable.Add(new CombinatorialRepair.Candidate(new List<int[]> { new[] { i, j, alt } }, "C3mnAlt", hint));
                            work[i][j] = curK;
                        }
                        continue;
                    }
                    // [玉突き連鎖] i の離脱で curK の被覆が悪化する → 玉突きで埋め直す（盤面不変・巻き戻し可能）。
                    var chain = V6SearchOperators.FindCovUChain(p, work, curK, j, rng, exclude: i,
                        rangeAvoid: (st, fk) => ExceedsOwnRangeHi(p, work, st, fk));
                    if (chain == null) { work[i][j] = curK; continue; }
                    var oldVals = chain.Select(mv => work[mv[0]][mv[1]]).ToArray();
                    foreach (var mv in chain) work[mv[0]][mv[1]] = mv[2];
                    var rep2 = UnifiedViolationChecker.Check(state, work);
                    var pinBad2 = V6SearchOperators.ExactPinRegression(p, workBeforeMove, work);
                    if (pinBad2 && IsBetter(rep2, bestRep)) pinBlocks.Record(p, workBeforeMove, work);
                    if (IsBetter(rep2, bestRep) && !pinBad2) { bestRep = rep2; applied++; improved = true; done = true; }
                    else
                    {
                        rejectCulprits.Record(rep2, bestRep, pinBad2);
                        for (var idx = 0; idx < chain.Count; idx++) work[chain[idx][0]][chain[idx][1]] = oldVals[idx];
                        work[i][j] = curK;
                        var hint = $"{(i >= 0 && i < state.StaffList.Count ? state.StaffList[i].Name : $"#{i}")}" +
                            $"({(curK >= 0 && curK < state.Shifts.Count ? state.Shifts[curK].Kigou : curK.ToString())})";
                        combinable.Add(new CombinatorialRepair.Candidate(
                            new List<int[]> { new[] { i, j, alt } }.Concat(chain).ToList(), "C3mnAlt", hint));
                    }
                }
            }
            pass++;
            if (!improved) break;
        }
        // [汎用玉突き結合フレームワーク, 3.249.0] stuckNames より前に実行し、結合で解消した箇所が
        //   「残存」に残らないようにする。
        var rejectedOut = new List<CombinatorialRepair.Candidate>();
        var c3mnCombStats = new CombinatorialRepair.Stats();
        bestRep = CombinatorialRepair.CombineAndApply(
            state, work, bestRep, Enumerable.Reverse(combinable).ToList(), IsBetter,
            shouldStop: stop, stats: c3mnCombStats, p: p, leftover: rejectedOut);
        applied += c3mnCombStats.CombosAccepted;
        var stuckNames = StuckStaffNames(state, bestRep.CellFamilies, "vio-c3mn");
        var c3mnCombSummary = c3mnCombStats.Summary();
        var c3mnBefore = before.Breakdown.GetValueOrDefault("c3mn", 0);
        var c3mnAfter = bestRep.Breakdown.GetValueOrDefault("c3mn", 0);
        var msg = $"回避パターン(c3mn)研磨: c3mn {c3mnBefore}->{c3mnAfter} / total {before.Total}->{bestRep.Total} " +
            $"HARD {before.Hard}->{bestRep.Hard} 採用{applied}回";
        if (applied == 0 && c3mnBefore > 0) msg += " [頭打ち=改善手なし]";
        msg += rejectCulprits.Summary();
        if (stuckNames.Count > 0) msg += $" 残存: {string.Join(", ", stuckNames)}";
        if (c3mnCombSummary.Length > 0) msg += $" / {c3mnCombSummary}";
        var logs = new[] { new MirrorLog(tag: "C3mnPolish", message: msg) };
        return new CyclicSwapResult(work, before.Total, bestRep.Total, applied, logs,
            ObservedPinBlockedAttempts: pinBlocks.Attempts, PinBlocks: pinBlocks, RejectedCandidates: rejectedOut);
    }
}
