using MagiEngine.Model;

namespace MagiEngine.V6;

public static partial class V6HotfixPasses
{
    /// <summary>
    /// [C3nPolish・禁止連続(c3n, HARD重み7000)専用の研磨パス] ユーザー指示「C3nは前後日と当日も他の勤務
    /// シフトに変更できるようにアルゴリズムを賢く昇華する」（3.303.0・AskUserQuestion で「両方＝範囲拡張＋
    /// 当日も可変」を選択）。
    ///
    /// <see cref="ApplyC3mnPolish"/>(3.214.0) と同型だが、決定的に違うのが<b>候補セルの取り方</b>:
    /// - C3mnPolish は違反セル (i,j) <b>その1セルだけ</b>を別シフトへ変える。
    /// - 本パスは違反パターンが<b>またぐ全日</b>（<c>Dﾃ→休→A4</c>なら3日ぶん全部＝前日・当日・翌日）を
    ///   候補にする。禁止連続は「並び」なので、どの1日を崩してもパターンは壊れる。にもかかわらず既存機構は
    ///   当日1セルか隣接1日しか触っておらず、3連の先頭に構造的に届いていなかった。
    ///
    /// 候補数は (パターン長 × 担当可能シフト数) 倍に増えるため、フル checker を呼ぶ前に
    /// <see cref="C3nRowScan"/> で「その手で c3n の正味 fire が実際に減るか」を先に判定して枝刈りする。
    /// 64日以内は popcount、長期日程は同じ意味のスカラー走査へ自動退避する。
    /// 最終採否は checker + isBetter + exactPinRegression が担保する。
    /// 崩した先で被覆が悪化するなら <see cref="V6SearchOperators.FindCovUChain"/> の玉突き連鎖で
    /// 埋め直すのは既存パスと同じ。
    /// </summary>
    public static CyclicSwapResult ApplyC3nPolish(
        MagiState state, int[][] schedule, int maxPasses = 3, Func<bool>? shouldStop = null, long seed = 0xC3EL)
    {
        var stop = shouldStop ?? (() => false);
        // [3.326.0] 回数固定(lo==hi)だけが却下した候補試行を対象別に数える（緩和対象の提示用）。
        var pinBlocks = new PinBlockAttribution();
        var p = new Problem(state);
        var work = ScheduleUtil.NormalizeSchedule(schedule, p);
        var before = UnifiedViolationChecker.Check(state, work);
        var bestRep = before;
        var applied = 0;
        if (p.Cons3n.Count == 0)
        {
            return new CyclicSwapResult(work, before.Total, bestRep.Total, 0,
                new[] { new MirrorLog(tag: "C3nPolish", message: "cons3nなし=スキップ") });
        }
        var rng = new JavaRandom(seed);
        bool Movable(int i, int j) => !p.WishLocked(i, j);
        var combinable = new List<CombinatorialRepair.Candidate>();
        var rejectCulprits = new RejectCulpritStats();
        var screened = 0;      // C3n枝刈りで checker を呼ばずに落とした候補数
        var evaluated = 0;     // 実際に checker を呼んだ候補数
        var patternDays = 0;   // 候補にしたセルの延べ数（当日1セルに留まらないことの実測）
        // [3.356.0/実機ログ起因] 「候補日延べ4 正式評価0 C3n枝刈り0」だけでは、なぜ1件も評価まで
        //   進まなかったのかが読めなかった（実データではアリフの2セルとも本人希望で固定されていた）。
        //   候補日から外れた理由を数える。
        var blockedWish = 0;   // 希望で固定されていて動かせなかった日
        var blockedCell = 0;   // 割当が範囲外（-1 等）で対象外だった日
        var pass = 0;
        while (pass < maxPasses)
        {
            if (stop()) break;
            var improved = false;
            var rep0 = pass == 0 ? before : UnifiedViolationChecker.Check(state, work);
            // アンカー = c3n 違反セル。cellFamilies を使うのは violations(最重1クラス)だと同一セルに
            //   より重い族が乗ったとき取りこぼすため（3.205.0 の anchor-shadowing と同じ理由）。
            var anchors = new List<(int I, int J)>();
            foreach (var (key, fams) in rep0.CellFamilies)
            {
                if (!fams.Contains("vio-c3n")) continue;
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
                if (i < 0 || i >= p.S || j < 0 || j >= p.T) continue;
                var done = false;
                // [当日も可変＋範囲拡張] 違反パターンがまたぐ全日を候補にする（j 自身を含む）。
                var c3nScan = new C3nRowScan(p, work[i]);
                var firesNow = c3nScan.Fires();
                if (firesNow == 0) continue;
                var candidateDays = c3nScan.CoveringDays(j);
                var days = new List<int>(candidateDays.Length);
                foreach (var day in candidateDays) days.Add(day);
                if (days.Count == 0) days.Add(j);
                days = days.OrderBy(d => Math.Abs(d - j)).ToList(); // 当日に近い日から（波及が小さい順）
                patternDays += days.Count;
                foreach (var j2 in days)
                {
                    if (done || stop()) break;
                    if (!Movable(i, j2)) { blockedWish++; continue; }
                    var curK = work[i][j2];
                    if (curK < 0 || curK >= p.K) { blockedCell++; continue; }
                    foreach (var alt in p.AllowedShiftsForStaff(i))
                    {
                        if (done || stop()) break;
                        if (alt == curK) continue;
                        // [C3n枝刈り] この1手で c3n の正味 fire が減らないなら checker を呼ばない。
                        //   減らない手は hard が下がらず、この HARD 族専用パスとしては意味がない。
                        if (c3nScan.FiresAfterSet(j2, alt) >= firesNow) { screened++; continue; }
                        var cnt = 0;
                        for (var s = 0; s < p.S; s++) if (work[s][j2] == curK) cnt++;
                        var needsChain = p.CovUCell(curK, j2, cnt - 1) > p.CovUCell(curK, j2, cnt);
                        var workBeforeMove = work.Copy2D();
                        work[i][j2] = alt;
                        var hint = $"{(i >= 0 && i < state.StaffList.Count ? state.StaffList[i].Name : $"#{i}")}" +
                            $"({(curK >= 0 && curK < state.Shifts.Count ? state.Shifts[curK].Kigou : curK.ToString())})";
                        if (!needsChain)
                        {
                            evaluated++;
                            var rep = UnifiedViolationChecker.Check(state, work);
                            var pinBad = V6SearchOperators.ExactPinRegression(p, workBeforeMove, work);
                            if (pinBad && IsBetter(rep, bestRep)) pinBlocks.Record(p, workBeforeMove, work);
                            if (IsBetter(rep, bestRep) && !pinBad) { bestRep = rep; applied++; improved = true; done = true; }
                            else
                            {
                                rejectCulprits.Record(rep, bestRep, pinBad);
                                combinable.Add(new CombinatorialRepair.Candidate(new List<int[]> { new[] { i, j2, alt } }, "C3nAlt", hint));
                                work[i][j2] = curK;
                            }
                            continue;
                        }
                        // [玉突き連鎖] 崩した側の被覆が欠けるなら埋め直す（盤面不変・巻き戻し可能）。
                        var chain = V6SearchOperators.FindCovUChain(p, work, curK, j2, rng, exclude: i,
                            rangeAvoid: (st, fk) => ExceedsOwnRangeHi(p, work, st, fk));
                        if (chain == null) { work[i][j2] = curK; continue; }
                        var oldVals = chain.Select(mv => work[mv[0]][mv[1]]).ToArray();
                        foreach (var mv in chain) work[mv[0]][mv[1]] = mv[2];
                        evaluated++;
                        var rep2 = UnifiedViolationChecker.Check(state, work);
                        var pinBad2 = V6SearchOperators.ExactPinRegression(p, workBeforeMove, work);
                        if (pinBad2 && IsBetter(rep2, bestRep)) pinBlocks.Record(p, workBeforeMove, work);
                        if (IsBetter(rep2, bestRep) && !pinBad2) { bestRep = rep2; applied++; improved = true; done = true; }
                        else
                        {
                            rejectCulprits.Record(rep2, bestRep, pinBad2);
                            for (var idx = 0; idx < chain.Count; idx++) work[chain[idx][0]][chain[idx][1]] = oldVals[idx];
                            work[i][j2] = curK;
                            combinable.Add(new CombinatorialRepair.Candidate(
                                new List<int[]> { new[] { i, j2, alt } }.Concat(chain).ToList(), "C3nAlt", hint));
                        }
                    }
                }
            }
            pass++;
            if (!improved) break;
        }
        var c3nCombStats = new CombinatorialRepair.Stats();
        bestRep = CombinatorialRepair.CombineAndApply(
            state, work, bestRep, Enumerable.Reverse(combinable).ToList(), IsBetter,
            shouldStop: stop, stats: c3nCombStats, p: p);
        applied += c3nCombStats.CombosAccepted;
        var stuckNames = StuckStaffNames(state, bestRep.CellFamilies, "vio-c3n");
        var c3nCombSummary = c3nCombStats.Summary();
        var c3nBefore = before.Breakdown.GetValueOrDefault("c3n", 0);
        var c3nAfter = bestRep.Breakdown.GetValueOrDefault("c3n", 0);
        var msg = $"禁止連続(c3n)研磨: c3n {c3nBefore}->{c3nAfter} / total {before.Total}->{bestRep.Total} " +
            $"HARD {before.Hard}->{bestRep.Hard} 採用{applied}回" +
            $" 候補日延べ{patternDays}(パターン全域・当日含む) 正式評価{evaluated} C3n枝刈り{screened}";
        if (blockedWish > 0) msg += $" 希望固定で候補外{blockedWish}日";
        if (blockedCell > 0) msg += $" 割当が範囲外{blockedCell}日";
        if (applied == 0 && c3nBefore > 0) msg += " [頭打ち=改善手なし]";
        msg += rejectCulprits.Summary();
        if (stuckNames.Count > 0) msg += $" 残存: {string.Join(", ", stuckNames)}";
        if (c3nCombSummary.Length > 0) msg += $" / {c3nCombSummary}";
        var logs = new[] { new MirrorLog(tag: "C3nPolish", message: msg) };
        return new CyclicSwapResult(work, before.Total, bestRep.Total, applied, logs,
            ObservedPinBlockedAttempts: pinBlocks.Attempts, PinBlocks: pinBlocks);
    }
}
