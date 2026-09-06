using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// [V6HotfixPasses / フェーズ6, 循環交換系] Kotlin原本 <c>V6HotfixPasses.kt</c> の
/// <c>applyCyclicSwapPolish</c>／<c>applyC3SequencePolish</c>／<c>applyBlockRotationPolish</c>／
/// <c>applyWeeklyRebalancePolish</c>（いずれも <see cref="V6HotfixPasses.CyclicSwapResult"/> を
/// 返す「同日/複数日の割当を交換する」系の研磨パス群）を収める partial ファイル。
/// このピースには <see cref="V6HotfixPasses.ApplyCyclicSwapPolish"/>／
/// <see cref="V6HotfixPasses.ApplyC3SequencePolish"/>／<see cref="V6HotfixPasses.ApplyBlockRotationPolish"/>／
/// <see cref="V6HotfixPasses.ApplyWeeklyRebalancePolish"/> の4本すべてを収める。
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
                        if (sa == sb || !p.MayPlace(a, sb) || !p.MayPlace(b, sa)) continue;
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
                            if (p.MayPlace(a, sb) && p.MayPlace(b, sc) && p.MayPlace(c, sa))
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
                                if (!p.MayPlace(i, work[i2][j + t]) || !p.MayPlace(i2, work[i][j + t])) { feasible = false; break; }
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

    /// <summary>
    /// [Kotlin原本のKDoc2つを保存 — 2つ目が現行の一般化版の説明]
    ///
    /// [ソフト研磨・c3系強化] c3/c3m/c3mn(連続規則)で違反しているセルを起点に、3職員×連日(2-3日)の
    /// ブロック「回転」を試す。2者ブロック入替や同日k=3巡回では到達できない3者×窓の組替えを、各日の
    /// (日,シフト)人数を保ったまま（=被覆/HARD不変）行い、実目的(UnifiedViolationChecker)で改善時のみ
    /// 採用（keep-best＝退化なし）。重み・パラメータは不変。違反セル指向なので低コスト。
    /// 2回の2者交換に分解すると中間で悪化するため山登りでは越えられない局面を、回転1手で跨ぐのが狙い。
    ///
    /// [ソフト研磨・3者回転] 指定クラス(anchorClasses)で違反しているセルを起点に、3職員×連日(2-3日)の
    /// ブロック「回転」を試す。2者ブロック入替/同日k=3巡回では到達できない3者×窓の組替えを、各日の
    /// (日,シフト)人数を保ったまま（=被覆/HARD不変）行い、実目的(UnifiedViolationChecker)で改善時のみ
    /// 採用（keep-best＝退化なし）。c1・c3系どちらの違反起点にも使える汎用版。重み・パラメータ不変。
    /// 2回の2者交換に分解すると中間で悪化するため山登りでは越えられない局面を、回転1手で跨ぐのが狙い。
    /// </summary>
    public static CyclicSwapResult ApplyBlockRotationPolish(
        MagiState state, int[][] schedule, IReadOnlySet<string> anchorClasses, string tag,
        int maxPasses = 2, Func<bool>? shouldStop = null)
    {
        var stop = shouldStop ?? (() => false);
        // [3.326.0] 回数固定(lo==hi)だけが却下した候補試行を対象別に数える（緩和対象の提示用）。
        var pinBlocks = new PinBlockAttribution();
        var p = new Problem(state);
        var work = ScheduleUtil.NormalizeSchedule(schedule, p);
        var before = UnifiedViolationChecker.Check(state, work);
        var bestRep = before;
        var applied = 0;
        var skipped = 0; // [#5] 前フィルタでフル評価を省いた手数(有効性ログ用)
        // [監査で発見・3.270.0] p.wish[i][j]<0 は「希望が一切ない」判定で、実現不能な希望
        //   (canDo(i,wish)==false)まで動かせないと誤判定していた（3.183.0 LightMirrorOptimizer と
        //   同型のバグ）。実現不能な希望はpref計上上も定数=動かして良い＝canDoガード込みの
        //   wishLocked が正しい判定。安全側（isBetter/checkerが最終ゲート）で候補が広がるのみ。
        bool Movable(int i, int j) => !p.WishLocked(i, j);
        void Rotate(int a, int b, int c, int jj, int ww, int[] targetA, int[] targetB, int[] targetC)
        {
            for (var t = 0; t < ww; t++)
            {
                work[a][jj + t] = targetA[t];
                work[b][jj + t] = targetB[t];
                work[c][jj + t] = targetC[t];
            }
        }
        var windows = new[] { 2, 3 };
        var pass = 0;
        while (pass < maxPasses)
        {
            if (stop()) break;
            // 指定クラスで違反している職員(=回転の起点)を収集。無ければ即終了（コスト0）。
            // [実バグ修正/applyC1WindowPolishと同根] rep0.violations（1セル=最重1クラスのみ）だと、
            //   anchorClassesのマーク位置に更に重い他族が同居する場合そのセルの分類が上書きされ検出漏れ
            //   になる。cellFamilies（1セルの全クラス保持）に切替え、上書きされても検出できるようにする。
            //   起点が広がるだけの後方互換な修正（C1Rotate/C3Rotate 両呼出に共通して適用される）。
            var rep0 = pass == 0 ? before : UnifiedViolationChecker.Check(state, work);
            var anchorStaff = new HashSet<int>();
            foreach (var (key, fams) in rep0.CellFamilies)
            {
                if (!fams.Any(f => anchorClasses.Contains(f))) continue;
                var staffIdx = KotlinInterop.ToIntOrNull(key.Split(',')[0]);
                if (staffIdx is int idx) anchorStaff.Add(idx);
            }
            if (anchorStaff.Count == 0) break;
            var improved = false;
            foreach (var w in windows)
            {
                if (p.T < w) continue;
                for (var j = 0; j <= p.T - w; j++)
                {
                    if (stop()) break;
                    // この窓で全日movableな職員のみ回転対象（同一3名を各日で回す＝日内人数不変）。
                    var cand = Enumerable.Range(0, p.S)
                        .Where(i => Enumerable.Range(0, w).All(t => Movable(i, j + t)))
                        .ToList();
                    if (cand.Count < 3) continue;
                    foreach (var ai in cand)
                    {
                        // [予算ガード] 締切後は O(cand^3) の全候補フル評価を走り切らせない(HF66=2.65.0と同方針)。
                        if (stop()) break;
                        if (!anchorStaff.Contains(ai)) continue;
                        foreach (var bi in cand)
                        {
                            // [予算ガード] 内側スキャンでも締切確認しバーストを O(cand) 以内に抑える。
                            if (stop()) break;
                            if (bi == ai) continue;
                            foreach (var ci in cand)
                            {
                                if (ci == ai || ci == bi) continue;
                                // 回転 ai<-bi, bi<-ci, ci<-ai が各日で担当可能か。
                                var feasible = true;
                                for (var t = 0; t < w; t++)
                                {
                                    if (!p.MayPlace(ai, work[bi][j + t]) || !p.MayPlace(bi, work[ci][j + t]) || !p.MayPlace(ci, work[ai][j + t]))
                                    { feasible = false; break; }
                                }
                                if (!feasible) continue;
                                var sa = Enumerable.Range(0, w).Select(t => work[ai][j + t]).ToArray();
                                var sb = Enumerable.Range(0, w).Select(t => work[bi][j + t]).ToArray();
                                var sc = Enumerable.Range(0, w).Select(t => work[ci][j + t]).ToArray();
                                // [#5 差分前フィルタ] 同 sgrp かつ同 ssk の手のみ前判定(群/スキル群/被覆/pref不変
                                //   →関与3名の局所目的が改善しなければ全体目的も改善しえない)。採用はフル評価が担う=安全。
                                var canPre = p.Sgrp[ai] == p.Sgrp[bi] && p.Sgrp[bi] == p.Sgrp[ci] &&
                                    p.Ssk[ai] == p.Ssk[bi] && p.Ssk[bi] == p.Ssk[ci];
                                StaffObjective? preObjective = canPre
                                    ? ComputeStaffObjective(p, work, ai) + ComputeStaffObjective(p, work, bi) + ComputeStaffObjective(p, work, ci)
                                    : null;
                                // [厳密ピン保護] 3者回転もwindow内で各職員の自身のシフト回数を変えうるため、
                                //   staffRange厳密ピン(lo==hi)を崩す候補は不採用にする（keep-best/重みは不変）。
                                var workBeforeRotate = work.Copy2D();
                                Rotate(ai, bi, ci, j, w, sb, sc, sa);
                                if (canPre)
                                {
                                    var postObjective = ComputeStaffObjective(p, work, ai) + ComputeStaffObjective(p, work, bi) + ComputeStaffObjective(p, work, ci);
                                    if (preObjective != null && !postObjective.IsBetterThan(preObjective))
                                    {
                                        Rotate(ai, bi, ci, j, w, sa, sb, sc);
                                        skipped++;
                                        continue;
                                    }
                                }
                                var rep = UnifiedViolationChecker.Check(state, work);
                                if (IsBetter(rep, bestRep) && !pinBlocks.BlocksImproving(p, workBeforeRotate, work))
                                {
                                    bestRep = rep; applied++; improved = true;
                                }
                                else
                                {
                                    Rotate(ai, bi, ci, j, w, sa, sb, sc); // 巻き戻し
                                }
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
            new MirrorLog(tag: tag,
                message: $"{tag} 3者回転研磨: c1 {before.Breakdown.GetValueOrDefault("c1", 0)}->{bestRep.Breakdown.GetValueOrDefault("c1", 0)}" +
                    $" / c3 {before.Breakdown.GetValueOrDefault("c3", 0)}->{bestRep.Breakdown.GetValueOrDefault("c3", 0)}" +
                    $" / c3m {before.Breakdown.GetValueOrDefault("c3m", 0)}->{bestRep.Breakdown.GetValueOrDefault("c3m", 0)}" +
                    $" / c3mn {before.Breakdown.GetValueOrDefault("c3mn", 0)}->{bestRep.Breakdown.GetValueOrDefault("c3mn", 0)}" +
                    $" / total {before.Total}->{bestRep.Total} HARD {before.Hard}->{bestRep.Hard} 採用{applied}回 (差分前フィルタで省略{skipped}手)"),
        };
        return new CyclicSwapResult(work, before.Total, bestRep.Total, applied, logs, PinBlocks: pinBlocks);
    }

    // [3.317.0] 分散指標ベースの平準化2パス（applyGroupShiftEqualizePolish / applyWeeklyEqualizePolish）は
    //   ここにあったが撤去した。目的関数の fair/weekly は 3.72.0 以降 **L1偏差**で評価されるのに、この2パスは
    //   **分散**を下げる手を採っており、指標が目的関数と一致していなかった（3.84.0 で「目的関数外の整え＝冗長」
    //   と記録したまま未計測だった）。実データ3件で ablation を取り、採用0回・分散指標も1ミリも動かず・
    //   最終盤面も変わらないことを確認して撤去。L1 ベースの後継が役割を完全に代替している:
    //   fair → applyFairPolish(3.235.0) ／ weekly → applyWeeklyRebalancePolish(3.197.0 長方形交換)＋
    //   applyAlternatingSoftPolish(3.198.0 が weekly の限界費用を Hungarian の費用に含む)。

    /// <summary>
    /// [ソフト研磨・weekly（7日周期のシフト平準化）＝長方形交換] weekly は「職員が特定の曜日にばかり同じ
    /// シフトに入る」偏りで、L1偏差（<see cref="ScheduleUtil.WeeklyDevOfBucket"/>＝そのシフトの曜日別回数の
    /// round(回数/7) からの偏差和）で評価される。**同日2者スワップ（CyclicSwap / equalize 系）は同じ日の
    /// 中で入れ替えるだけなので、どの曜日に何が入るかを動かせない**。これが「weekly の研磨ができていない」
    /// 実害の根本（実機ログで weekly＝SOFT 残差の最大級）。
    ///
    /// そこで **被覆保存の 2職員×2日 長方形交換** を導入する: 職員 i がシフト x について「過剰曜日の日 j1
    /// で x・過少曜日の日 j2 で別のシフト y」、相手 i' が「j1 で別のシフト z・j2 で x」のとき、両者の
    /// j1/j2 を丸ごと入替える（i: j1→z / j2→x、i': j1→x / j2→y）。各日の各シフト人数は保存される
    /// （j1 の x は i→i'、j2 の x は i'→i へ移るだけ）ため covU/covO・群レンジ・pref は不変で、i の x が
    /// 過剰曜日→過少曜日へ移動して weekly が下がる。fair（群内シフト回数）や low/high/apt/c2 など
    /// per-staff 族も副次的に動く。
    /// [3.345.0] 休を通常のシフト種として扱う定義に合わせ、x/y/z を勤務・休で区別しない（旧: x=勤務・y=z=休
    /// の特殊形のみ＝休だけを「空き」とみなしていた）。旧形は新形の部分集合なので探索範囲は広がるだけ。
    /// **採否は実目的関数 <see cref="UnifiedViolationChecker.BetterReport"/> のみ**（hard→weighted→total、
    /// total は weekly/fair を含む）＝退化なし（keep-best）。dev>0 の (職員,シフト) のみ起点＋
    /// first-improvement で空探索は即終了。変更セルは wish 固定なら不動（4セルとも movable ガード）。
    /// covO/c42/c2 など per-day 族は同日 CyclicSwap（isBetter）が既に最適に研磨済みのため本パスの対象外
    /// （2.49.0 の「専用パスは冗長」の結論を踏襲）。
    /// </summary>
    public static CyclicSwapResult ApplyWeeklyRebalancePolish(
        MagiState state, int[][] schedule, int maxPasses = 2, Func<bool>? shouldStop = null)
    {
        var stop = shouldStop ?? (() => false);
        // [3.326.0] 回数固定(lo==hi)だけが却下した候補試行を対象別に数える（緩和対象の提示用）。
        var pinBlocks = new PinBlockAttribution();
        var p = new Problem(state);
        var work = ScheduleUtil.NormalizeSchedule(schedule, p);
        var before = UnifiedViolationChecker.Check(state, work);
        var bestRep = before;
        var applied = 0;
        // [監査で発見・3.270.0] p.wish[i][j]<0 は実現不能な希望まで動かせないと誤判定していた
        //   （3.183.0 LightMirrorOptimizer と同型のバグ）。wishLocked は canDo ガード込みで正しい。
        bool Movable(int i, int j) => !p.WishLocked(i, j);
        int WeekdayOf(int j) => (p.Dow0 + j) % 7;
        // [3.345.0] 職員×シフト×曜日のカウント（休も1シフト）。
        int[][] WdBucket(int i)
        {
            var wd = Enumerable.Range(0, p.K).Select(_ => new int[7]).ToArray();
            for (var j = 0; j < p.T; j++)
            {
                var k = work[i][j];
                if (k >= 0 && k < p.K) wd[k][WeekdayOf(j)]++;
            }
            return wd;
        }
        var pass = 0;
        while (pass < maxPasses)
        {
            if (stop()) break;
            var improved = false;
            for (var i = 0; i < p.S; i++)
            {
                if (stop()) break;
                var wdAll = WdBucket(i);
                for (var x = 0; x < p.K; x++)
                {
                    if (improved || stop()) break;
                    var wd = wdAll[x];
                    if (ScheduleUtil.WeeklyDevOfBucket(wd) == 0) continue;
                    var sum = 0; foreach (var w in wd) sum += w;
                    var tgt = (int)KotlinInterop.MathRound(sum / 7.0);
                    // シフト x が最も過剰な曜日と最も過少な曜日を1つずつ狙う。
                    var wOver = -1; var wUnder = -1; var maxOver = 0; var maxUnder = 0;
                    for (var w = 0; w < 7; w++)
                    {
                        if (wd[w] - tgt > maxOver) { maxOver = wd[w] - tgt; wOver = w; }
                        if (tgt - wd[w] > maxUnder) { maxUnder = tgt - wd[w]; wUnder = w; }
                    }
                    if (wOver < 0 || wUnder < 0) continue;
                    // i が過剰曜日に x に入っている日 / 過少曜日に x 以外に入っている日（どちらも movable）。
                    var overDays = Enumerable.Range(0, p.T)
                        .Where(it => WeekdayOf(it) == wOver && Movable(i, it) && work[i][it] == x).ToList();
                    var underDays = Enumerable.Range(0, p.T)
                        .Where(it => WeekdayOf(it) == wUnder && Movable(i, it) && work[i][it] != x && work[i][it] >= 0 && work[i][it] < p.K)
                        .ToList();
                    var done = false;
                    foreach (var j1 in overDays)
                    {
                        if (done || stop()) break;
                        foreach (var j2 in underDays)
                        {
                            // [レビュー#6 3.213.0] 内側ループにも締切確認（各候補がフル check を伴うため、
                            //   キャンセル後のバーストを1候補以内に抑える。HF66=2.65.0/BlockRotation=3.84.0 と同方針）。
                            if (done || stop()) break;
                            var y = work[i][j2];
                            for (var ip = 0; ip < p.S; ip++)
                            {
                                if (done || stop()) break;
                                if (ip == i) continue;
                                // 相手 i' は j1 で x 以外(z)・j2 で x、両日 movable。被覆保存には i←z(j1), i'←y(j2) が担当可であること。
                                if (!Movable(ip, j1) || !Movable(ip, j2)) continue;
                                if (work[ip][j2] != x) continue;
                                var z = work[ip][j1];
                                if (z == x || z < 0 || z >= p.K) continue;
                                if (!p.MayPlace(i, z) || !p.MayPlace(ip, y)) continue;
                                // 長方形交換を適用（被覆保存）→ フル評価 → 改善時のみ採用、不採用なら完全巻き戻し。
                                // [監査で発見・3.270.0] isBetter は hard→weightedScore→total の辞書式のため、
                                //   raw total が改善してもweightedScoreが悪化する組合せ(重い厳密ピン破りを軽い
                                //   weekly改善が数の上で上回る)がありうる。同型の全パスに既に適用済みの
                                //   exactPinRegression ガードをここにも追加（3.256.0の retrofit 漏れ）。
                                var workBeforeRect = work.Copy2D();
                                work[i][j1] = z; work[i][j2] = x; work[ip][j1] = x; work[ip][j2] = y;
                                var rep = UnifiedViolationChecker.Check(state, work);
                                if (IsBetter(rep, bestRep) && !pinBlocks.BlocksImproving(p, workBeforeRect, work))
                                {
                                    bestRep = rep; applied++; improved = true; done = true; break;
                                }
                                work[i][j1] = x; work[i][j2] = y; work[ip][j1] = z; work[ip][j2] = x;
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
            new MirrorLog(tag: "WeeklyRebalance",
                message: $"曜日平準化(長方形交換): total {before.Total}->{bestRep.Total} 採用{applied}回"),
        };
        return new CyclicSwapResult(work, before.Total, bestRep.Total, applied, logs, PinBlocks: pinBlocks);
    }
}
