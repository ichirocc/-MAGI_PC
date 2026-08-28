using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// [V6HotfixPasses / フェーズ6, 循環交換系] Kotlin原本 <c>V6HotfixPasses.kt</c> の
/// <c>applyCyclicSwapPolish</c>／<c>applyC3SequencePolish</c>／<c>applyBlockRotationPolish</c>／
/// <c>applyWeeklyRebalancePolish</c>（いずれも <see cref="V6HotfixPasses.CyclicSwapResult"/> を
/// 返す「同日/複数日の割当を交換する」系の研磨パス群）を収める partial ファイル。
/// このピースでは最初の1本 <see cref="V6HotfixPasses.ApplyCyclicSwapPolish"/> のみ移植する。
/// </summary>
public static partial class V6HotfixPasses
{
    /// <summary>
    /// [Kotlin原本] <c>applyCyclicSwapPolish</c>。同日の2職員スワップ(k=2)・3職員ローテーション(k=3)を
    /// 全日・全職員ペアについて試し、keep-best（<see cref="UnifiedViolationChecker.BetterReport"/>）で
    /// 改善するときだけ採用する。日ごとのシフト多重集合＝被覆(covU/covO)は構造的に不変（同日内の
    /// 値の入替えのみ）。<c>maxPasses</c> 回まで巡回し、1巡で1件も改善しなければ早期終了する。
    /// </summary>
    public static CyclicSwapResult ApplyCyclicSwapPolish(
        MagiState state, int[][] schedule, int maxPasses = 4, Func<bool>? shouldStop = null)
    {
        var stop = shouldStop ?? (() => false);
        // [3.326.0] 回数固定(lo==hi)だけが却下した候補試行を対象別に数える（緩和対象の提示用）。
        var pinBlocks = new PinBlockAttribution();
        var p = new Problem(state);
        var work = ScheduleUtil.NormalizeSchedule(schedule, p);
        var before = UnifiedViolationChecker.Check(state, work);
        var bestRep = before;
        var applied = 0;
        // [監査で発見・3.270.0] p.wish[i][j]<0 は「希望が一切ない」判定で、実現不能な希望
        //   (canDo(i,wish)==false)まで動かせないと誤判定していた（3.183.0 LightMirrorOptimizer と
        //   同型のバグ）。実現不能な希望はpref計上上も定数=動かして良い＝canDoガード込みの
        //   wishLocked が正しい判定。安全側（isBetter/checkerが最終ゲート）で候補が広がるのみ。
        bool Movable(int i, int j) => !p.WishLocked(i, j);
        var pass = 0;
        while (pass < maxPasses)
        {
            if (stop()) break;
            var improved = false;
            for (var j = 0; j < p.T; j++)
            {
                if (stop()) break;
                // --- k=2: 2職員スワップ（同日・被覆不変）---
                for (var a = 0; a < p.S; a++)
                {
                    // [監査(未レビュー領域再監査)] HF66(2.65.0)/BlockRotationPolish(3.84.0)と同型の予算超過対策。
                    //   旧: 日(j)ループ先頭のみで確認していたため、1日分のO(S^2)スキャンが締切後も走り切っていた。
                    if (stop()) break;
                    if (!Movable(a, j)) continue;
                    for (var b = a + 1; b < p.S; b++)
                    {
                        if (!Movable(b, j)) continue;
                        var sa = work[a][j];
                        var sb = work[b][j];
                        if (sa == sb || !p.CanDo(a, sb) || !p.CanDo(b, sa)) continue;
                        // [厳密ピン保護] 異なるシフト同士の同日交換はa/bの自身のシフト回数を変えるため、
                        //   staffRange厳密ピン(lo==hi)を新たに崩す候補は不採用にする（keep-best/重み不変）。
                        var workBeforeSwap2 = work.Copy2D();
                        work[a][j] = sb; work[b][j] = sa;
                        var rep = UnifiedViolationChecker.Check(state, work);
                        if (UnifiedViolationChecker.BetterReport(rep, bestRep) && !pinBlocks.BlocksImproving(p, workBeforeSwap2, work))
                        {
                            bestRep = rep; applied++; improved = true;
                        }
                        else
                        {
                            work[a][j] = sa; work[b][j] = sb;
                        }
                    }
                }
                // --- k=3: 3職員ローテーション（同日・被覆不変）---
                for (var a = 0; a < p.S; a++)
                {
                    if (stop()) break;
                    if (!Movable(a, j)) continue;
                    for (var b = a + 1; b < p.S; b++)
                    {
                        if (!Movable(b, j)) continue;
                        for (var c = b + 1; c < p.S; c++)
                        {
                            if (!Movable(c, j)) continue;
                            if (stop()) break;
                            var sa = work[a][j];
                            var sb = work[b][j];
                            var sc = work[c][j];
                            if (sa == sb && sb == sc) continue;
                            // a←sb, b←sc, c←sa（feasibleなら適用→評価→不採用なら巻き戻し）
                            if (p.CanDo(a, sb) && p.CanDo(b, sc) && p.CanDo(c, sa))
                            {
                                var workBeforeRotate3 = work.Copy2D();
                                work[a][j] = sb; work[b][j] = sc; work[c][j] = sa;
                                var rep = UnifiedViolationChecker.Check(state, work);
                                if (UnifiedViolationChecker.BetterReport(rep, bestRep) && !pinBlocks.BlocksImproving(p, workBeforeRotate3, work))
                                {
                                    bestRep = rep; applied++; improved = true;
                                    continue;
                                }
                                work[a][j] = sa; work[b][j] = sb; work[c][j] = sc;
                            }
                        }
                    }
                }
            }
            pass++;
            if (!improved) break;
        }
        var logs = new[]
        {
            new MirrorLog(tag: "CyclicSwap",
                message: $"循環交換(k=2,3)研磨: total {before.Total}->{bestRep.Total} 採用{applied}回"),
        };
        return new CyclicSwapResult(work, before.Total, bestRep.Total, applied, logs, PinBlocks: pinBlocks);
    }
}
