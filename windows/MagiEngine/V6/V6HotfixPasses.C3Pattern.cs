using MagiEngine.Model;

namespace MagiEngine.V6;

public static partial class V6HotfixPasses
{
    /// <summary>
    /// [C3PatternPolish・玉突き連鎖の横展開その4] cons3/cons3m のうち複数シフトMUST/Wantパターン
    /// （非single-shift、<c>C3Run.IsSingleShiftSeq</c>が偽の規則）専用の研磨パス。ユーザー指示
    /// 「c42/c42s以外にも『動かせるか』専用オペレータの欠如が無いか棚卸しする」で発見（棚卸し結果は
    /// ユーザー承認済み）。3.216.0(C3RunPolish)は単一シフト連(run-deficitモデル)のみを対象とし、
    /// 複数シフトパターンは「既存機構(2者ブロック交換/3者回転)のまま対象外（安全側・挙動不変）」と
    /// 明記して見送っていた。既存の2-3者交換/回転は「相手が対になるパターンを持つ」という相互条件を
    /// 要求し、交換相手が構造的に存在しない（誰も対になる並びを持たない）局面では解消できない、
    /// c41/c42/covO/apt/fair と同型の穴。
    ///
    /// <c>MirrorCore.CheckC3Family</c> の非forbidden複数シフト分岐は「schedule[i][j]==seq[0] かつ
    /// 残り(d-1)日が全部一致しない(z&lt;d-1)」を1件の違反として窓先頭セル(i,j)へ計上する。このモデル
    /// では「日jのseq[0]を別シフトへ変え、パターンの起点自体を崩す」だけで当該違反インスタンスが
    /// 消える（残り日が完成するよう複数日を同時に組み替える方向＝パターン完成は、複数日の依存関係が
    /// 絡み正しさの保証が難しいため意図的にスコープ外＝既存の2-3者交換/回転パスに委ねる。見送っても
    /// 既存機構が担当を続けるだけ＝安全側）。C3mnPolish(3.214.0)と同一の「1セル付け替え＋
    /// FindCovUChain玉突き」パターンをそのまま適用する。採否はisBetter(hard→weighted→total)
    /// keep-best＝退化不能。
    /// </summary>
    public static CyclicSwapResult ApplyC3PatternPolish(
        MagiState state, int[][] schedule, int maxPasses = 3, Func<bool>? shouldStop = null, long seed = 0xC3B4L)
    {
        var stop = shouldStop ?? (() => false);
        // [3.326.0] 回数固定(lo==hi)だけが却下した候補試行を対象別に数える（緩和対象の提示用）。
        var pinBlocks = new PinBlockAttribution();
        var p = new Problem(state);
        var work = ScheduleUtil.NormalizeSchedule(schedule, p);
        var before = UnifiedViolationChecker.Check(state, work);
        var bestRep = before;
        var applied = 0;
        var rules = new List<C3>();
        foreach (var c in p.Cons3) if (c.Seq.Length > 1 && !C3Run.IsSingleShiftSeq(c.Seq)) rules.Add(c);
        foreach (var c in p.Cons3m) if (c.Seq.Length > 1 && !C3Run.IsSingleShiftSeq(c.Seq)) rules.Add(c);
        if (rules.Count == 0)
        {
            return new CyclicSwapResult(work, before.Total, bestRep.Total, 0,
                new[] { new MirrorLog(tag: "C3PatternPolish", message: "複数シフトc3/c3mパターンなし=スキップ") });
        }
        var rng = new JavaRandom(seed);
        var rejectCulprits = new RejectCulpritStats();
        // [監査で発見・3.270.0] p.wish[i][j]<0 は実現不能な希望まで動かせないと誤判定していた
        //   （3.183.0 LightMirrorOptimizer と同型のバグ）。wishLocked は canDo ガード込みで正しい。
        bool Movable(int i, int j) => !p.WishLocked(i, j);

        // アンカー: 各規則(seq,d)で「schedule[i][j]==seq[0]かつ残りd-1日が全一致しない(z<d-1)」窓の
        //   先頭(i,j,seq[0])。CheckC3Familyの非forbidden複数シフト分岐と同一の意味論。
        List<(int I, int J, int CurK)> CollectAnchors()
        {
            var outp = new List<(int, int, int)>();
            foreach (var c in rules)
            {
                var seq = c.Seq; var d = seq.Length;
                if (d > p.T) continue;
                for (var i = 0; i < p.S; i++)
                {
                    var j = 0;
                    while (j <= p.T - d)
                    {
                        if (work[i][j] == seq[0])
                        {
                            var z = 0;
                            for (var l = 1; l < d; l++) if (work[i][j + l] == seq[l]) z++;
                            if (z < d - 1) outp.Add((i, j, seq[0]));
                        }
                        j++;
                    }
                }
            }
            return outp;
        }
        var initialCount = CollectAnchors().Count;

        var pass = 0;
        while (pass < maxPasses)
        {
            if (stop()) break;
            var improved = false;
            var anchors = CollectAnchors();
            if (anchors.Count == 0) break;
            foreach (var (i, j, curK) in anchors)
            {
                if (stop()) break;
                if (!Movable(i, j) || work[i][j] != curK) continue;
                var done = false;
                foreach (var alt in p.AllowedShiftsForStaff(i))
                {
                    if (done || stop()) break;
                    if (alt == curK) continue;
                    if (p.MakesForbiddenRun(work, i, j, alt)) continue;
                    var cnt = 0;
                    for (var s = 0; s < p.S; s++) if (work[s][j] == curK) cnt++;
                    var needsChain = p.CovUCell(curK, j, cnt - 1) > p.CovUCell(curK, j, cnt);
                    // [厳密ピン保護] i(・玉突き相手)の回数変更がstaffRange厳密ピン(lo==hi)を新たに崩す
                    //   候補は不採用にする（keep-best/重みは不変・追加ガードのみ）。
                    var workBeforePattern = work.Copy2D();
                    work[i][j] = alt;
                    if (!needsChain)
                    {
                        var rep = UnifiedViolationChecker.Check(state, work);
                        if (IsBetter(rep, bestRep) && !pinBlocks.BlocksImproving(p, workBeforePattern, work))
                        { bestRep = rep; applied++; improved = true; done = true; }
                        else work[i][j] = curK;
                        continue;
                    }
                    // [玉突き連鎖] i の離脱で curK の被覆が悪化する → 玉突きで埋め直す（盤面不変・巻き戻し可能）。
                    var chain = V6SearchOperators.FindCovUChain(p, work, curK, j, rng, exclude: i,
                        rangeAvoid: (st, fk) => ExceedsOwnRangeHi(p, work, st, fk));
                    if (chain == null) { work[i][j] = curK; continue; }
                    var oldVals = chain.Select(mv => work[mv[0]][mv[1]]).ToArray();
                    foreach (var mv in chain) work[mv[0]][mv[1]] = mv[2];
                    var rep2 = UnifiedViolationChecker.Check(state, work);
                    var pinBad = V6SearchOperators.ExactPinRegression(p, workBeforePattern, work);
                    if (pinBad && IsBetter(rep2, bestRep)) pinBlocks.Record(p, workBeforePattern, work);
                    if (IsBetter(rep2, bestRep) && !pinBad) { bestRep = rep2; applied++; improved = true; done = true; }
                    else
                    {
                        rejectCulprits.Record(rep2, bestRep, pinBad);
                        for (var idx = 0; idx < chain.Count; idx++) work[chain[idx][0]][chain[idx][1]] = oldVals[idx];
                        work[i][j] = curK;
                    }
                }
            }
            pass++;
            if (!improved) break;
        }
        var remaining = CollectAnchors();
        var stuckNames = remaining
            .Select(a => a.I >= 0 && a.I < state.StaffList.Count ? state.StaffList[a.I].Name : $"#{a.I}")
            .Distinct()
            .ToList();
        var c3Before = before.Breakdown.GetValueOrDefault("c3", 0);
        var c3After = bestRep.Breakdown.GetValueOrDefault("c3", 0);
        var c3mBefore = before.Breakdown.GetValueOrDefault("c3m", 0);
        var c3mAfter = bestRep.Breakdown.GetValueOrDefault("c3m", 0);
        var msg = $"連続規則(c3/c3m複数シフトパターン)玉突き研磨: 窓不成立 {initialCount}->{remaining.Count}" +
            $" / c3 {c3Before}->{c3After}" +
            $" / c3m {c3mBefore}->{c3mAfter}" +
            $" / total {before.Total}->{bestRep.Total} HARD {before.Hard}->{bestRep.Hard} 採用{applied}回";
        if (applied == 0 && initialCount > 0) msg += " [頭打ち=改善手なし]";
        msg += rejectCulprits.Summary();
        if (stuckNames.Count > 0) msg += $" 残存: {string.Join(", ", stuckNames)}";
        var logs = new[] { new MirrorLog(tag: "C3PatternPolish", message: msg) };
        return new CyclicSwapResult(work, before.Total, bestRep.Total, applied, logs,
            ObservedPinBlockedAttempts: pinBlocks.Attempts, PinBlocks: pinBlocks);
    }
}
