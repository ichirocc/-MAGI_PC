using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// [V6HotfixPasses / フェーズ6, 循環交換系] Kotlin原本 <c>V6HotfixPasses.kt</c> の
/// <c>applyCyclicSwapPolish</c>／<c>applyC3SequencePolish</c>／<c>applyBlockRotationPolish</c>／
/// <c>applyWeeklyRebalancePolish</c>（いずれも <see cref="V6HotfixPasses.CyclicSwapResult"/> を
/// 返す「同日/複数日の割当を交換する」系の研磨パス群）を収める partial ファイル。
/// このピースには <see cref="V6HotfixPasses.ApplyCyclicSwapPolish"/> と
/// <see cref="V6HotfixPasses.ApplyC3SequencePolish"/> の2本を収める（<c>ApplyBlockRotationPolish</c>／
/// <c>ApplyWeeklyRebalancePolish</c> はまだ未移植）。
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

    /// <summary>
    /// [Kotlin原本] <c>applyC3SequencePolish</c>。c3/c3m/c3mn(連続規則)で違反しているセルを起点に、
    /// 2職員×連日(2〜3日)の「ブロック交換」を試す。窓内の全日で担当可否とcanDo条件を満たす場合のみ
    /// 適用し、実目的(<see cref="UnifiedViolationChecker"/>)で改善時のみ採用（keep-best＝退化なし）。
    /// 同 sgrp/ssk の参加者ペアには <see cref="ComputeStaffObjective"/> による差分前フィルタを掛け、
    /// 部分目的が改善しない手をフル checker を呼ばずに省く（近似・keep-bestの正しさには無関係）。
    /// </summary>
    public static CyclicSwapResult ApplyC3SequencePolish(
        MagiState state, int[][] schedule, int maxPasses = 3, Func<bool>? shouldStop = null)
    {
        var stop = shouldStop ?? (() => false);
        // [3.326.0] 回数固定(lo==hi)だけが却下した候補試行を対象別に数える（緩和対象の提示用）。
        var pinBlocks = new PinBlockAttribution();
        var p = new Problem(state);
        var work = ScheduleUtil.NormalizeSchedule(schedule, p);
        var before = UnifiedViolationChecker.Check(state, work);
        var bestRep = before;
        var applied = 0;
        var skipped = 0; // [#5] 前フィルタでフル評価を省いた手数
        // [監査で発見・3.270.0] p.wish[i][j]<0 は実現不能な希望まで動かせないと誤判定していた
        //   （3.183.0 LightMirrorOptimizer と同型のバグ）。wishLocked は canDo ガード込みで正しい。
        bool Movable(int i, int j) => !p.WishLocked(i, j);
        void SwapBlock(int a, int b, int jj, int ww)
        {
            for (var t = 0; t < ww; t++)
            {
                (work[a][jj + t], work[b][jj + t]) = (work[b][jj + t], work[a][jj + t]);
            }
        }
        var windows = new[] { 2, 3 }; // 連続2日・3日（c3は最大5連日だが2-3日窓でほぼ捕捉）
        var pass = 0;
        while (pass < maxPasses)
        {
            if (stop()) break;
            var improved = false;
            // [違反セル指向] c3系で違反している職員のみを起点に絞る。c3は職員ごと→2者交換で改善する手は
            //   必ず違反職員を含む＝取りこぼし無し(ロスレス)。空なら即終了でコスト0。
            // [実バグ修正/applyC1WindowPolishと同根] rep0.violations（1セル=最重1クラスのみ）だと、
            //   c3系のマーク位置に c3n(HARD) 等の更に重い違反も同居する場合、そのセルの分類が上書きされ
            //   "vio-c3/c3m/c3mn"が消える。該当職員の全マーク位置が同様にシャドーイングされていると
            //   anchorStaffから丸ごと漏れ、一度も研磨が試されない。cellFamilies（1セルの全クラス保持）
            //   に切替え、上書きされても検出できるようにする。起点が広がるだけの後方互換な修正。
            var rep0 = pass == 0 ? before : UnifiedViolationChecker.Check(state, work);
            var anchorStaff = new HashSet<int>();
            foreach (var (key, fams) in rep0.CellFamilies)
            {
                if (!fams.Any(f => f is "vio-c3" or "vio-c3m" or "vio-c3mn")) continue;
                var staffIdx = KotlinInterop.ToIntOrNull(key.Split(',')[0]);
                if (staffIdx is int idx) anchorStaff.Add(idx);
            }
            if (anchorStaff.Count == 0) break;
            foreach (var w in windows)
            {
                if (p.T < w) continue;
                for (var j = 0; j <= p.T - w; j++)
                {
                    if (stop()) break;
                    for (var i = 0; i < p.S; i++)
                    {
                        // [監査(未レビュー領域再監査)] O(S^2)内側スキャンにも締切確認を追加（HF66/BlockRotationPolishと同型）。
                        if (stop()) break;
                        if (Enumerable.Range(0, w).Any(t => !Movable(i, j + t))) continue;
                        for (var i2 = i + 1; i2 < p.S; i2++)
                        {
                            if (!anchorStaff.Contains(i) && !anchorStaff.Contains(i2)) continue; // 違反職員を含む対のみ
                            if (Enumerable.Range(0, w).Any(t => !Movable(i2, j + t))) continue;
                            var feasible = true;
                            var same = true;
                            for (var t = 0; t < w; t++)
                            {
                                if (!p.CanDo(i, work[i2][j + t]) || !p.CanDo(i2, work[i][j + t])) { feasible = false; break; }
                                if (work[i][j + t] != work[i2][j + t]) same = false;
                            }
                            if (!feasible || same) continue;
                            // [#5 差分前フィルタ] 同 sgrp かつ同 ssk の2者ブロック交換のみ前判定。
                            var canPre = p.Sgrp[i] == p.Sgrp[i2] && p.Ssk[i] == p.Ssk[i2];
                            StaffObjective? preObjective = canPre
                                ? ComputeStaffObjective(p, work, i) + ComputeStaffObjective(p, work, i2)
                                : null;
                            // [厳密ピン保護] ブロック交換はwindow内の日ごとにi/i2の自身のシフト回数を変えうる
                            //   （2者間で異なるシフトが混在する日がある限り）。staffRange厳密ピン(lo==hi)を
                            //   崩す候補は不採用にする（keep-best/重みは不変・追加ガードのみ）。
                            var workBeforeBlock = work.Copy2D();
                            SwapBlock(i, i2, j, w);
                            if (canPre)
                            {
                                var postObjective = ComputeStaffObjective(p, work, i) + ComputeStaffObjective(p, work, i2);
                                if (preObjective != null && !postObjective.IsBetterThan(preObjective))
                                {
                                    SwapBlock(i, i2, j, w);
                                    skipped++;
                                    continue;
                                }
                            }
                            var rep = UnifiedViolationChecker.Check(state, work);
                            if (IsBetter(rep, bestRep) && !pinBlocks.BlocksImproving(p, workBeforeBlock, work))
                            {
                                bestRep = rep; applied++; improved = true;
                            }
                            else
                            {
                                SwapBlock(i, i2, j, w); // 巻き戻し
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
            new MirrorLog(tag: "C3Polish",
                message: $"連続規則c3系研磨(2者ブロック): c3 {before.Breakdown.GetValueOrDefault("c3", 0)}->{bestRep.Breakdown.GetValueOrDefault("c3", 0)}" +
                    $" / c3m {before.Breakdown.GetValueOrDefault("c3m", 0)}->{bestRep.Breakdown.GetValueOrDefault("c3m", 0)}" +
                    $" / c3mn {before.Breakdown.GetValueOrDefault("c3mn", 0)}->{bestRep.Breakdown.GetValueOrDefault("c3mn", 0)}" +
                    $" / total {before.Total}->{bestRep.Total} HARD {before.Hard}->{bestRep.Hard} 採用{applied}回 (差分前フィルタで省略{skipped}手)"),
        };
        return new CyclicSwapResult(work, before.Total, bestRep.Total, applied, logs, PinBlocks: pinBlocks);
    }
}
