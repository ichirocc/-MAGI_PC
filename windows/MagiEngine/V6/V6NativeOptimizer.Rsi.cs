using System.Threading;
using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// Faithful port of Kotlin's <c>runRsi</c> (phase 5c: "runAlns系/runRsi系") and its complete
/// hypothesis-generation dependency chain (<c>rsiGenerateHypothesis</c>, <c>maxViolatedFamily</c>,
/// and the four "free repair" operators <c>applyCovUChains</c>/<c>applyCovOFree</c>/
/// <c>applyC41Free</c>/<c>applyC42Free</c> plus their shared <c>commitBestMove</c> evaluator).
///
/// [C#移植上の判断・可視性] Kotlin 原本の <c>runV5</c>/<c>runAlnsChains</c>/<c>runAlns</c>/
/// <c>runAlnsSingle</c>/<c>hf80PostPolish</c> は全て <c>private</c> だが、これらは全て既に本移植で
/// <c>internal static</c> へ意図的に格上げ済み（<c>InternalsVisibleTo("MagiEngine.Tests")</c> 経由で
/// 直接単体テストするため）。<c>runRsi</c>（Kotlin: <c>private suspend fun</c>）もこの確立済みの
/// 前例へ倣い <c>internal static</c> とする。一方 <c>applyCovUChains</c>/<c>commitBestMove</c> は
/// Kotlin 原本でも <c>private</c> のままで、他の <c>internal</c> 群（<c>applyCovOFree</c>/
/// <c>applyC41Free</c>/<c>applyC42Free</c>/<c>rsiGenerateHypothesis</c>/<c>maxViolatedFamily</c>）は
/// Kotlin 原本ですでに <c>internal</c>（＝可視性はそのまま忠実移植で足りる）。
/// </summary>
public static partial class V6NativeOptimizer
{
    /// <summary>
    /// [3.253.0, Kotlin原本] 実データ検証(golden_state.json/sample_state_v6.json)で判明した「Free」系
    /// リペア共通の欠陥への対処。候補（セル代入の束＝直接移動、または移動＋玉突き連鎖の複合手）を1つずつ
    /// 実際に一時適用し、UnifiedViolationChecker で全体評価、baseline(この手を試す直前の盤面)に対して
    /// 真に改善する(better()=hard→weighted→total辞書式で厳密改善)候補の中から最良の1件だけを選んで
    /// コミットする。改善する候補が1つも無ければ何もしない(null)＝そのセルは諦める（安全側・退化不能）。
    /// </summary>
    private static ViolationReport? CommitBestMove(
        MagiState state, int[][] sched,
        ViolationReport baseline, List<List<int[]>> candidates)
    {
        List<int[]>? bestOps = null;
        ViolationReport? bestRep = null;
        foreach (var ops in candidates)
        {
            var saved = new int[ops.Count];
            for (var idx = 0; idx < ops.Count; idx++) saved[idx] = sched[ops[idx][0]][ops[idx][1]];
            foreach (var mv in ops) sched[mv[0]][mv[1]] = mv[2];
            var rep = UnifiedViolationChecker.Check(state, sched);
            for (var idx = 0; idx < ops.Count; idx++) sched[ops[idx][0]][ops[idx][1]] = saved[idx];
            if (Better(rep, baseline) && (bestRep == null || Better(rep, bestRep)))
            {
                bestOps = ops;
                bestRep = rep;
            }
        }
        if (bestOps == null) return null;
        foreach (var mv in bestOps) sched[mv[0]][mv[1]] = mv[2];
        return bestRep;
    }

    /// <summary>
    /// [E11/多人数ブロック移動, Kotlin原本] 現盤面の全 covU セルを、同日・多人数の玉突き連鎖
    /// （<see cref="V6SearchOperators.FindCovUChain"/>）で充填する。sched を in-place 変更し、適用手数を
    /// 返す。連鎖は同日内交換＝被覆総量保存で、canDo/非wishLocked/c3n枝刈り済み。最終採否は呼び出し側の
    /// keep-best（ラウンド <see cref="Better"/> or エピローグの checker 照合）が担保。
    /// </summary>
    private static int ApplyCovUChains(MagiState state, int[][] sched, JavaRandom rng, Func<bool>? shouldStop = null)
    {
        var stop = shouldStop ?? (() => false);
        var p = ScheduleUtil.CachedProblem(state);
        if (p.S == 0 || p.T == 0) return 0;
        var applied = 0;
        var cnt = new int[p.K];
        for (var j = 0; j < p.T; j++)
        {
            if (stop()) return applied;
            for (var k = 0; k < p.K; k++) cnt[k] = 0;
            for (var i = 0; i < p.S; i++) { var kk = sched[i][j]; if (kk >= 0 && kk < p.K) cnt[kk]++; }
            for (var k = 0; k < p.K; k++)
            {
                if (p.CovUCell(k, j, cnt[k]) <= 0) continue;
                var chain = V6SearchOperators.FindCovUChain(p, sched, k, j, rng);
                if (chain == null) continue;
                foreach (var mv in chain) sched[mv[0]][mv[1]] = mv[2];
                applied++;
                // 同日に複数 covU があり得るので当日カウントを再計算。
                for (var kk = 0; kk < p.K; kk++) cnt[kk] = 0;
                for (var i = 0; i < p.S; i++) { var kk = sched[i][j]; if (kk >= 0 && kk < p.K) cnt[kk]++; }
            }
        }
        return applied;
    }

    /// <summary>
    /// [3.204.0/covO専用repair, Kotlin原本] 人員過剰(covO)セルの在勤者のうち、他シフトへ移しても新たな
    /// 違反を生まない（希望固定でない・移すと禁止連続(c3n)を作らない・移動先で covO が悪化しない＝
    /// 受け皿あり）候補を1人見つけて実際に移す。被覆総量は保存しない（過剰シフトから1人引くだけ＝covOの
    /// み改善方向）。移動先が全て禁止連続(c3n)で塞がる場合は即諦めず
    /// <see cref="V6SearchOperators.TryFixForbiddenRunViaAdjacentDay"/> で隣接日調整を試す。sched を
    /// in-place 変更し、適用手数を返す。最終採否は呼び出し側の keep-best が担保＝退化なし。
    ///
    /// [3.391.0, Kotlin原本] 生の <c>wish==k</c> は実現不能な希望（担当できないシフトへの希望）まで
    /// 固定扱いにしていた。pref は実現可能な希望しか数えないので、その場合ここを動かしても pref は増えず
    /// 担当外セル＝groupViol(10000) が消える＝必須違反が厳密に減る手を丸ごと捨てていた。規約の
    /// <see cref="ScheduleUtil.WishLocked"/> へ統一（3.351.0 と同型）。
    /// </summary>
    internal static int ApplyCovOFree(MagiState state, int[][] sched, JavaRandom rng, Func<bool>? shouldStop = null)
    {
        var stop = shouldStop ?? (() => false);
        var p = ScheduleUtil.CachedProblem(state);
        if (p.S == 0 || p.T == 0) return 0;
        var applied = 0;
        for (var j = 0; j < p.T; j++)
        {
            if (stop()) return applied;
            for (var k = 0; k < p.K; k++)
            {
                while (true)
                {
                    if (stop()) return applied;
                    var cov = new int[p.K];
                    for (var i = 0; i < p.S; i++) { var kk = sched[i][j]; if (kk >= 0 && kk < p.K) cov[kk]++; }
                    if (p.CovOCell(k, j, cov[k]) <= 0) break;
                    var baseline = UnifiedViolationChecker.Check(state, sched);
                    var staffOnK = Enumerable.Range(0, p.S).Where(it => sched[it][j] == k).ToList();
                    var candidates = new List<List<int[]>>();
                    foreach (var i in staffOnK)
                    {
                        if (p.WishLocked(i, j) && p.Wish[i][j] == k) continue;   // 実現可能な本人希望＝動かすとpref未充足化
                        foreach (var m in p.AllowedShiftsForStaff(i).Where(it => it != k))
                        {
                            if (p.MakesForbiddenRun(sched, i, j, m))
                            {
                                var fix = V6SearchOperators.TryFixForbiddenRunViaAdjacentDay(p, sched, i, j, m, rng);
                                if (fix == null) continue;
                                var ops = new List<int[]>(fix) { new[] { i, j, m } };
                                candidates.Add(ops);
                            }
                            else
                            {
                                candidates.Add(new List<int[]> { new[] { i, j, m } });
                            }
                        }
                    }
                    if (candidates.Count == 0) break;
                    if (CommitBestMove(state, sched, baseline, candidates) == null) break;
                    applied++;
                }
            }
        }
        return applied;
    }

    /// <summary>
    /// [3.209.0/c41・c41s専用repair, Kotlin原本] c41/c41s（群×日×シフトの人数レンジ[l,u]違反）に対する
    /// covO と同型の「動かせるか」判定。skill=false は cons41(sgrp)、skill=true は cons41s(ssk) を対象に
    /// する（DRY化）。希望固定でない・禁止連続(c3n)を作らない・移動元/移動先で covU/covO を悪化させない
    /// 候補のみ動かす。sched を in-place 変更し適用手数を返す。最終採否は呼び出し側の keep-best が
    /// 担保＝退化不能。
    /// </summary>
    internal static int ApplyC41Free(MagiState state, int[][] sched, JavaRandom rng, bool skill, Func<bool>? shouldStop = null)
    {
        var stop = shouldStop ?? (() => false);
        var p = ScheduleUtil.CachedProblem(state);
        if (p.S == 0 || p.T == 0) return 0;
        var rules = skill ? p.Cons41s : p.Cons41;
        if (rules.Count == 0) return 0;
        var grp = skill ? p.Ssk : p.Sgrp;
        var applied = 0;

        int GroupCount(C41 c, int j)
        {
            var z = 0;
            for (var i = 0; i < p.S; i++) if (grp[i] == c.GroupIdx && sched[i][j] == c.ShiftIdx) z++;
            return z;
        }

        foreach (var c in rules)
        {
            if (stop()) return applied;
            for (var j = 0; j < p.T; j++)
            {
                if (stop()) return applied;
                // 超過(z>u): 群在籍者を他シフトへ移す。
                while (GroupCount(c, j) > c.U)
                {
                    var baseline = UnifiedViolationChecker.Check(state, sched);
                    var onShift = Enumerable.Range(0, p.S).Where(it => grp[it] == c.GroupIdx && sched[it][j] == c.ShiftIdx).ToList();
                    var candidates = new List<List<int[]>>();
                    foreach (var i in onShift)
                    {
                        if (p.WishLocked(i, j) && p.Wish[i][j] == c.ShiftIdx) continue;   // 実現可能な本人希望＝対象外
                        foreach (var m in p.AllowedShiftsForStaff(i).Where(it => it != c.ShiftIdx))
                        {
                            if (p.MakesForbiddenRun(sched, i, j, m)) continue;
                            candidates.Add(new List<int[]> { new[] { i, j, m } });
                            // 玉突き連鎖版（離脱先を先に適用してから探索＝本人がまだ在籍中に見える誤判定を防ぐ既定の作法）。
                            var oldK = sched[i][j];
                            sched[i][j] = m;
                            var chain = V6SearchOperators.FindCovUChain(p, sched, c.ShiftIdx, j, rng, exclude: i);
                            sched[i][j] = oldK;
                            if (chain != null)
                            {
                                var ops = new List<int[]> { new[] { i, j, m } };
                                ops.AddRange(chain);
                                candidates.Add(ops);
                            }
                        }
                    }
                    if (candidates.Count == 0) break;
                    if (CommitBestMove(state, sched, baseline, candidates) == null) break;
                    applied++;
                }
                // 不足(z<l): 群内の他シフト在籍者を引き入れる。
                while (GroupCount(c, j) < c.L)
                {
                    var baseline = UnifiedViolationChecker.Check(state, sched);
                    var offShift = Enumerable.Range(0, p.S)
                        .Where(it => grp[it] == c.GroupIdx && sched[it][j] != c.ShiftIdx && p.CanDo(it, c.ShiftIdx))
                        .ToList();
                    var candidates = new List<List<int[]>>();
                    foreach (var i in offShift)
                    {
                        var old = sched[i][j];
                        if (old < 0 || old >= p.K || (p.WishLocked(i, j) && p.Wish[i][j] == old)) continue;   // 現シフトが実現可能な本人希望＝対象外
                        if (p.MakesForbiddenRun(sched, i, j, c.ShiftIdx)) continue;
                        candidates.Add(new List<int[]> { new[] { i, j, c.ShiftIdx } });
                        sched[i][j] = c.ShiftIdx;
                        var chain = V6SearchOperators.FindCovUChain(p, sched, old, j, rng, exclude: i);
                        sched[i][j] = old;
                        if (chain != null)
                        {
                            var ops = new List<int[]> { new[] { i, j, c.ShiftIdx } };
                            ops.AddRange(chain);
                            candidates.Add(ops);
                        }
                    }
                    if (candidates.Count == 0) break;
                    if (CommitBestMove(state, sched, baseline, candidates) == null) break;
                    applied++;
                }
            }
        }
        return applied;
    }

    /// <summary>
    /// [3.233.0/c41,c41s と同型の専用repair, Kotlin原本] c42(群ペア禁止: 群g1のs1×群g2のs2が同日に
    /// 同時発生禁止)の違反ペア(left∈g1×s1, right∈g2×s2)のどちらか一方を実際に他シフトへ動かして崩す。
    /// 移動先でcovOが悪化しない候補を探し、離脱元でcovUが悪化するなら findCovUChain で玉突き
    /// フォールバック（c41Free で判明済みの罠=「離脱を先にschedへ適用してから findCovUChain を呼ぶ」
    /// 順序を踏襲。逆順だと本人がまだ在籍中に見え常にnullが返る）。skill=false は cons42(sgrp)、
    /// skill=true は cons42s(ssk) を対象にする（DRY化）。sched を in-place 変更し適用手数を返す。
    /// 最終採否は呼び出し側のkeep-best（ラウンド <see cref="Better"/>）が担保＝退化不能。
    /// </summary>
    internal static int ApplyC42Free(MagiState state, int[][] sched, JavaRandom rng, bool skill, Func<bool>? shouldStop = null)
    {
        var stop = shouldStop ?? (() => false);
        var p = ScheduleUtil.CachedProblem(state);
        if (p.S == 0 || p.T == 0) return 0;
        var rules = skill ? p.Cons42s : p.Cons42;
        if (rules.Count == 0) return 0;
        var grp = skill ? p.Ssk : p.Sgrp;
        var applied = 0;

        // [3.253.0, commitBestMoveへ全面移行, Kotlin原本] 違反ペアの片側(left=g1×s1 / right=g2×s2)
        //   それぞれについて、構造的に安全な直接移動・玉突き連鎖の両方の候補を集め、commitBestMove が
        //   実チェッカーで全体評価する。C# では `out` パラメータ名は予約語のため `outList` へ改名。
        void GatherSide(IReadOnlyList<int> candidateStaff, int j, int fromShift, List<List<int[]>> outList)
        {
            foreach (var i in candidateStaff)
            {
                if (p.WishLocked(i, j) && p.Wish[i][j] == fromShift) continue;   // 実現可能な本人希望＝対象外
                foreach (var m in p.AllowedShiftsForStaff(i).Where(it => it != fromShift))
                {
                    if (p.MakesForbiddenRun(sched, i, j, m)) continue;
                    outList.Add(new List<int[]> { new[] { i, j, m } });
                    var oldK = sched[i][j];
                    sched[i][j] = m;
                    var chain = V6SearchOperators.FindCovUChain(p, sched, fromShift, j, rng, exclude: i);
                    sched[i][j] = oldK;
                    if (chain != null)
                    {
                        var ops = new List<int[]> { new[] { i, j, m } };
                        ops.AddRange(chain);
                        outList.Add(ops);
                    }
                }
            }
        }

        foreach (var c in rules)
        {
            if (stop()) return applied;
            for (var j = 0; j < p.T; j++)
            {
                if (stop()) return applied;
                while (true)
                {
                    var left = Enumerable.Range(0, p.S).Where(it => grp[it] == c.G1 && sched[it][j] == c.S1).ToList();
                    var right = Enumerable.Range(0, p.S).Where(it => grp[it] == c.G2 && sched[it][j] == c.S2).ToList();
                    if (left.Count == 0 || right.Count == 0) break;   // ペアが存在しない＝この日は解消済み
                    var baseline = UnifiedViolationChecker.Check(state, sched);
                    var candidates = new List<List<int[]>>();
                    GatherSide(left, j, c.S1, candidates);
                    GatherSide(right, j, c.S2, candidates);
                    if (candidates.Count == 0) break;
                    if (CommitBestMove(state, sched, baseline, candidates) == null) break;
                    applied++;
                }
            }
        }
        return applied;
    }

    /// <summary>
    /// Faithful port of Kotlin's <c>rsiGenerateHypothesis</c>. <c>focus</c> 族に応じた破壊/専用修復
    /// オペレータで <paramref name="baseSched"/> の摂動版を生成する（RSI 各ラウンドの仮説）。
    /// 返り値の採否はラウンド境界の <see cref="Better"/>（keep-best）が担保するため、この関数自体は
    /// 退化不能（Kotlin: <c>base</c>/<c>out</c> は C# の予約語のため <c>baseSched</c>/<c>outSched</c> へ
    /// 改名。<c>fixed</c> も予約語のため <c>fixedSched</c> へ改名）。
    /// </summary>
    internal static int[][] RsiGenerateHypothesis(
        MagiState state, int[][] baseSched, ViolationReport report, string focus, JavaRandom rng,
        Func<bool>? shouldStop = null)
    {
        var stop = shouldStop ?? (() => false);
        var outSched = baseSched.Copy2D();
        var p = ScheduleUtil.CachedProblem(state);
        switch (focus)
        {
            // [E11, Kotlin原本] covU は「勤務→勤務」の多人数連鎖で充填（既存 DestroyRepairDay は休→勤務
            //   のみ＝候補が過剰シフト/連鎖からしか引けない局面を踏めない）。
            case "covU":
                for (var r = 0; r < 6; r++) DestroyRepairDay(state, outSched, rng);
                ApplyCovUChains(state, outSched, rng, stop);
                break;
            case "c41":
                for (var r = 0; r < 6; r++) DestroyRepairDay(state, outSched, rng);
                ApplyC41Free(state, outSched, rng, skill: false, shouldStop: stop);
                break;
            case "c41s":
                for (var r = 0; r < 6; r++) DestroyRepairDay(state, outSched, rng);
                ApplyC41Free(state, outSched, rng, skill: true, shouldStop: stop);
                break;
            case "c42":
                for (var r = 0; r < 6; r++) DestroyRepairDay(state, outSched, rng);
                ApplyC42Free(state, outSched, rng, skill: false, shouldStop: stop);
                break;
            case "c42s":
                for (var r = 0; r < 6; r++) DestroyRepairDay(state, outSched, rng);
                ApplyC42Free(state, outSched, rng, skill: true, shouldStop: stop);
                break;
            // [実機ログ起因=apt未focus, Kotlin原本] destroyRepairStaff の marginal cost(StaffCountPenaltyAt)
            //   は既に apt(重み1) を織込み済みのため、low/high/c2 と同じ経路へ合流するだけで apt 専用の
            //   新規オペレータ不要（weekly/fair も同根の理由で同経路）。
            case "low": case "high": case "c2": case "apt": case "weekly": case "fair":
            {
                var reps = DestroyRepairStaffReps(p.S, p.T);
                for (var r = 0; r < reps; r++) DestroyRepairStaff(state, outSched, rng);
                break;
            }
            case "covO":
                for (var r = 0; r < 6; r++) DestroyRepairDay(state, outSched, rng);
                ApplyCovOFree(state, outSched, rng, stop);
                break;
            // [実機ログ起因, Kotlin原本] groupViol/pref は hf67 の作用対象(群外修正・希望反映)。c3n(禁止
            //   連続=HARD)には hf67 は一切作用しないため、そちらは else 分岐(destroyRepairViolations)へ回る。
            case "groupViol": case "pref":
            {
                var fixedSched = Hf67HardRepair(state, outSched, rng).Schedule;
                for (var i = 0; i < p.S; i++)
                    for (var j = 0; j < p.T; j++)
                        outSched[i][j] = fixedSched[i][j];
                break;
            }
            default:
                for (var r = 0; r < 12; r++) DestroyRepairViolations(state, outSched, report, rng);
                break;
        }
        return outSched;
    }

    /// <summary>
    /// Faithful port of Kotlin's <c>maxViolatedFamily</c>. HARD 族(groupViol/covU/pref/c3n)を件数に
    /// 関わらず SOFT より先に focus する。apt/covO はそれぞれ周期的な保証枠（3ラウンドに1回・最終
    /// ラウンド）で「件数最大」選択に構造的に絶対勝てない問題を回避する。他は非avoid族の件数最大。
    /// 最後に「件数最大選択が weekly を選んだが apt にも残りがあるなら apt を優先する」という
    /// ユーザー明示指示(2026-07-20)を適用する。
    ///
    /// [C#移植上の判断] Kotlin の <c>avoid: Set&lt;String&gt; = emptySet()</c> は C# のレコード
    /// 位置パラメータ既定値（コンパイル時定数のみ）にできないため、<c>IReadOnlySet&lt;string&gt;? avoid
    /// = null</c> ＋ 本体先頭での <c>avoid ??= new HashSet&lt;string&gt;();</c> という、このコードベース
    /// 既存の nullable-default 慣用へ揃える。
    /// </summary>
    internal static string MaxViolatedFamily(ViolationReport report, IReadOnlySet<string>? avoid = null, int round = -1, int roundsTotal = -1)
    {
        avoid ??= new HashSet<string>();
        var order = new[]
        {
            "groupViol", "covU", "pref", "c3n", "low", "high", "c41", "c41s", "c2", "covO",
            "c42", "c42s", "apt", "weekly", "fair", "c1", "c3", "c3m", "c3mn",
        };
        // [D1/A1, Kotlin原本] 解ける HARD 族は件数に関わらず SOFT より先に focus する。avoid(HF63=構造的に
        //   充足困難)に入る HARD は「解けない」ため除外し無駄打ちを避ける（残予算は下段の SOFT 研磨へ）。
        //   hard=0 のとき no-op＝全 soft の一般ケースは従来と不変。
        foreach (var key in order)
        {
            if (!MirrorKeys.Hard.Contains(key) || avoid.Contains(key)) continue;
            if (report.Breakdown.GetValueOrDefault(key, 0) > 0) return key;
        }
        // [3.204.0/3.207.0, Kotlin原本] covO は日×シフトのセル単独違反のため件数が常に一桁台に留まり、
        //   c1/c42/c3mn/weekly のような数十件規模の族に「件数最大」選択で恒久的に絶対勝てない。周期的な
        //   保証枠(3ラウンドに1回)を設け、count>0かつavoid対象でなければ下段の最大値選択より優先する。
        //   最終ラウンドも保証枠に加え、周期枠が典型的な短いRSIフェーズで丸ごと空振りする問題を解消する。
        var finalRound = roundsTotal > 0 && round == roundsTotal - 1;
        // [3.208.0/3.239.0, Kotlin原本] apt も covO と全く同じ欠陥を抱えていた（covOとは別の周期
        //   round%3==1、covOのround%3==2と衝突しない）。最終ラウンドで両方が候補になる場合のみ、実際の
        //   件数を比較し「より少ない方（より構造的に不利＝件数最大選択に絶対勝てない方）」を優先する。
        var aptEligible = round >= 0 && !avoid.Contains("apt") && report.Breakdown.GetValueOrDefault("apt", 0) > 0 && (round % 3 == 1 || finalRound);
        var covOEligible = round >= 0 && !avoid.Contains("covO") && report.Breakdown.GetValueOrDefault("covO", 0) > 0 && (round % 3 == 2 || finalRound);
        if (aptEligible && covOEligible)
            return report.Breakdown.GetValueOrDefault("covO", 0) <= report.Breakdown.GetValueOrDefault("apt", 0) ? "covO" : "apt";
        if (aptEligible) return "apt";
        if (covOEligible) return "covO";
        // 解ける HARD が無い(全て 0 か avoid)＝以降は SOFT。従来どおり非avoidの族から件数最大を返す。
        // [E8, Kotlin原本] 件数0の族は focus しない。該当なしは "total" を返し、rsiGenerateHypothesis の
        //   else 分岐＝全違反セル hint の汎用修復ラウンドとして時間を有効化する。
        var best = "total";
        var bestCount = 0;
        foreach (var key in order)
        {
            if (avoid.Contains(key)) continue;
            var n = report.Breakdown.GetValueOrDefault(key, 0);
            if (n > bestCount)
            {
                bestCount = n;
                best = key;
            }
        }
        // [ユーザー明示指示(2026-07-20)「weeklyをaptより優先順位を下げる」, Kotlin原本] weekly は件数が
        //   大きくなりやすく apt より小さくても件数最大選択で恒常的に勝ってしまっていた。件数比較で
        //   weekly が選ばれた場合でも、apt に残り(avoid対象でなければ)があれば apt を優先する。
        if (best == "weekly" && !avoid.Contains("apt") && report.Breakdown.GetValueOrDefault("apt", 0) > 0)
            best = "apt";
        return best;
    }

    /// <summary>
    /// Faithful port of Kotlin's <c>runRsi</c> — RSI(反復ラウンド探索). 各ラウンドで
    /// <see cref="MaxViolatedFamily"/> が選んだ focus 族に対し <see cref="RsiGenerateHypothesis"/> で
    /// 摂動した仮説を <see cref="RunAlns"/>（偶数ラウンド）/ <see cref="RunV5"/>（奇数ラウンド）へ渡し、
    /// ラウンド境界の keep-best（<see cref="Better"/>）で最良を積み上げる。HF63
    /// (<see cref="Hf63Infeasibility"/>) が構造的に充足困難と学習した族は focus から除外し、静的な covU
    /// 構造床（<see cref="V6SanityPort.StructuralHardFloor"/>）に達している間は covU も除外する。HARD 残が
    /// deprioritize されてもなお狙える SOFT 族が残る限り早期終了せずピボットし続け、狙える族が尽きたときと
    /// <paramref name="shouldStop"/> の両方が終了条件になる。
    ///
    /// [C#移植上の判断・キャンセルの第3の型] 他の探索駆動関数（<see cref="RunAlnsSingle"/>/
    /// <see cref="Hf80PostPolish"/> は shouldStop() と <see cref="CancellationToken"/> を1つの
    /// 非throwポーリングへ統一・<see cref="RunAlnsChains"/>/<see cref="RunMultiWorker"/>/
    /// workers&gt;1 時の <see cref="RunAlns"/> は個々の並列単位は決してthrowせず外側コーディネータが
    /// 収集後に1回だけthrow）とは異なり、Kotlin 原本の <c>runRsi</c> はラウンドループの先頭で
    /// <c>if (shouldStop()) break</c>（非throw）に続けて <c>coroutineContext.ensureActive()</c>
    /// （throw）を**両方**呼ぶ——これは <see cref="RunAlns"/>/<see cref="RunV5"/> の内部が
    /// Workers に応じて型1/型2いずれの意味論を取るかに関わらず、<c>runRsi</c> 自身のラウンド境界での
    /// 明示的な外側キャンセル伝播であり、内部呼出と冗長ではない。よって
    /// <c>cancellationToken.ThrowIfCancellationRequested()</c> をラウンドループの先頭で
    /// <c>shouldStop()</c> の直後に呼ぶ（真にキャンセルされていれば次のラウンド境界で確実にthrowしうる、
    /// という Kotlin 原本と同じ非対称をそのまま再現する）。
    /// </summary>
    internal static async Task<V6OptimizerResult> RunRsi(
        MagiState state,
        int[][] initial,
        V6OptimizerOptions options,
        int budgetSec,
        Func<bool>? shouldStop = null,
        Action<string, ViolationReport?, long, long>? onProgress = null,
        Hf63Infeasibility? sharedHf63 = null,
        CancellationToken cancellationToken = default)
    {
        var started = NowMs();
        var stop = shouldStop ?? (() => false);
        var rng = new JavaRandom(ActualSeed(options.Seed) ^ 0x451L);
        var best = ScheduleUtil.NormalizeSchedule(initial, ScheduleUtil.CachedProblem(state));
        var bestReport = UnifiedViolationChecker.Check(state, best);
        var iters = 0L;
        var rounds = Math.Max(2, Math.Min(8, budgetSec / 30 + 2));
        var per = Math.Max(1, budgetSec / rounds);
        var logs = new List<MirrorLog>();
        // [HF63, Kotlin原本] ラウンド境界で改善ストリームを追跡し、構造的に充足困難な族を focus 対象から
        //   外す。sharedHf63 が渡されればエポックを跨いで停滞学習が持続する（呼出元＝適応ポートフォリオの
        //   ワーカーが phase 5d/5e で配線）。省略時は従来どおり新規。
        var hf63 = sharedHf63 ?? new Hf63Infeasibility();
        // [3.288.0/ログ強化=回数軸, Kotlin原本] 戦略変更（focus遷移・E9冷却・HF63降格）を1行に集約する
        //   ための足跡。スパム対応: HF63/ピボット行は「内容が変わったときだけ」出す。
        var focusTrail = new List<string>();
        var e9Cooldowns = 0;
        HashSet<string>? lastLoggedAvoid = null;
        string? lastLoggedPivot = null;
        // [HARD=0非到達への配慮/静的covU床, Kotlin原本] 構造的 covU 下限（有資格者を全員就けても埋まらない
        //   席=forcedCovU）は最適化中に不変。covU がこの床に達したら「これ以上 covU は下げられない」と
        //   静的に確定するので、HF63 の動的検知(約3ラウンド無改善を要する)を待たず round 0 から即 focus
        //   除外し、RSI の残ラウンドを解ける族(他HARD/SOFT)へ回す。
        int covUFloor;
        try { covUFloor = V6SanityPort.StructuralHardFloor(state, ScheduleUtil.CachedProblem(state)); }
        catch (Exception) { covUFloor = 0; }
        var stagnantRounds = 0;   // [N4] Better() 無改善の連続ラウンド数
        // [E9/状況適応, Kotlin原本] 直前ラウンドが「完全空振り」(候補不採用＋focus族の件数も不変)だった
        //   focus を次の1ラウンドだけ回避する軽い冷却。
        string? cooldownFocus = null;
        // [レビュー#5 3.213.0/3.231.0, Kotlin原本] HF63 の停滞加算を「直前ラウンドで実際に focus した族」
        //   に限定し、effortIters を rounds に応じて動的に決める（詰んだ族の deprioritize が「残り最低2
        //   ラウンドを振り向けに残せる」タイミングで完了するようにする）。
        var effortIters = RsiHf63EffortIters(rounds);
        string? lastFocus = null;
        for (var round = 0; round < rounds; round++)
        {
            if (stop()) break;
            cancellationToken.ThrowIfCancellationRequested();
            // [監査修正, Kotlin原本] HF63 は Web の per-iter 前提(5000 iter 無改善で infeasible)。ラウンド
            //   粒度の呼出に effortIters/round を渡し、閾値5000到達を有限ラウンド分の focus 投入無改善に
            //   引き伸ばす（class は Web 忠実移植のまま・呼出側で粒度を補正）。
            hf63.UpdateFromBreakdownFocused(bestReport.Breakdown, lastFocus, effortIters);
            // [12h見直し, Kotlin原本] 動的(HF63)と静的(covU床)の avoid を分離して保持する。N4 早期脱出の
            //   発火条件は HF63 の動的検知のみでゲートし、静的covU床除外を混ぜて「旧N4の厳密な部分集合」
            //   保証を破らないようにする。
            var dynamicAvoid = hf63.InfeasibleBreakdownKeys();
            // [実機ログ起因/SOFT誤deprioritize, Kotlin原本] focus の deprioritize は真に構造的な HARD
            //   （covU 床/c3n/pref/groupViol）のみに限定し、SOFT は常に focusable に保つ。N4 早期終了の
            //   武装判定は従来どおり dynamicAvoid（全族）で行い、pivot 可否は avoid(HARD) で判定する。
            var avoid = new HashSet<string>(dynamicAvoid.Where(it => MirrorKeys.Hard.Contains(it)));
            // [静的covU床, Kotlin原本] 合法配置では covU >= covUFloor（下限）。担当外配置(groupViol)が
            //   混在すると covU が床を下回り得るが、その間 covU を focus しても無意味なので `<=` で除外。
            if (covUFloor > 0 && bestReport.Breakdown.GetValueOrDefault("covU", 0) <= covUFloor) avoid.Add("covU");
            // [E9, Kotlin原本] 冷却は focus 選択にのみ合流（HF63 ログ・N4 発火条件には混ぜない＝恒久判定と区別）。
            var focusAvoid = cooldownFocus != null ? new HashSet<string>(avoid) { cooldownFocus } : avoid;
            var focus = MaxViolatedFamily(bestReport, focusAvoid, round, rounds);
            if (avoid.Count > 0 && (lastLoggedAvoid == null || !avoid.SetEquals(lastLoggedAvoid)))
            {
                // [3.288.0/スパム対応, Kotlin原本] 集合が変化したラウンドのみログ（旧: 毎ラウンド同文）。
                logs.Add(new MirrorLog(tag: "HF63", message: $"deprioritize {string.Join(",", avoid)} → focus={focus} (round {round + 1})", iter: iters));
                lastLoggedAvoid = new HashSet<string>(avoid);
                focusTrail.Add("[HF63降格:" + string.Join("+", avoid) + "]");
            }
            if (cooldownFocus != null)
            {
                logs.Add(new MirrorLog(tag: "RSIFocus", message: $"直前ラウンド空振りのため {cooldownFocus} を1ラウンド休止 → focus={focus} (round {round + 1})", iter: iters));
                e9Cooldowns++;
            }
            focusTrail.Add(focus);
            var focusedBefore = bestReport.Breakdown.GetValueOrDefault(focus, 0);
            lastFocus = focus;   // [レビュー#5] 次ラウンド頭の HF63 更新へ「このラウンドの投入先」を渡す
            var hypothesis = RsiGenerateHypothesis(state, best, bestReport, focus, rng, stop);
            // [HF361/528/541移植, Kotlin原本] EarlyChain: Web 内部V5の停滞(reheat)フック(L11705-)に対応する
            //   RSI ラウンド境界で発火。Chain3/4 は常時、Rect/BlkN は optFlags.rectSwap(既定ON)に従う。
            var phase = round % 2 == 0
                ? await RunAlns(state, hypothesis, options with { Restarts = 1 }, per, stop, onProgress, cancellationToken).ConfigureAwait(false)
                : await RunV5(state, hypothesis, options, per, stop, onProgress, cancellationToken).ConfigureAwait(false);
            iters += phase.Iterations;
            var candSched = phase.Schedule;
            var candReport = phase.Report;
            {
                var lr = V6LateOperators.Improve(state, candSched, candReport, rng, started + budgetSec * 1000L, rectEnabled: options.RectSwap);
                if (lr.Chain3 + lr.Chain4 + lr.Rect + lr.BlkN > 0)
                {
                    candSched = lr.Schedule;
                    candReport = lr.Report;
                    logs.Add(new MirrorLog(tag: "EarlyChain",
                        message: $"早期循環フック改善 (Chain3={lr.Chain3} Chain4={lr.Chain4} Rect={lr.Rect} BlkN={lr.BlkN}) round={round + 1} HARD={candReport.Hard} total={candReport.Total}",
                        iter: iters));
                    logs.AddRange(lr.Logs);
                }
            }
            if (Better(candReport, bestReport))
            {
                best = candSched.Copy2D();
                bestReport = candReport;
                stagnantRounds = 0;
                cooldownFocus = null;   // [E9] 進展あり＝冷却解除
            }
            else
            {
                stagnantRounds++;
                // [E9, Kotlin原本] 完全空振り(不採用＋focus族の件数が減っていない)なら次ラウンドだけこの
                //   focus を休止。候補が focus 族を減らしていた(=方向は有望だが総合で負けた)場合は冷却しない。
                cooldownFocus = focus != "total" && candReport.Breakdown.GetValueOrDefault(focus, 0) >= focusedBefore ? focus : null;
            }
            // [3.288.0/スパム対応, Kotlin原本] ラウンド行は「改善したラウンド」と「最終ラウンド」だけに絞る。
            if (stagnantRounds == 0 || round == rounds - 1)
            {
                logs.Add(new MirrorLog(tag: "RunMAGI_RSI",
                    message: $"round={round + 1}/{rounds} focus={focus} best HARD={bestReport.Hard} total={bestReport.Total}"
                        + (stagnantRounds > 0 ? $"（無改善{stagnantRounds}R）" : "（改善）"),
                    iter: iters));
            }
            if (OwnsStatics(GetRunSlot())) PublishLiveBest(bestReport, best);   // [DefragLiveView] 計算中ライブ盤面
            onProgress?.Invoke($"RSI {focus}", bestReport, iters, NowMs() - started);
            // [N4改, Kotlin原本] focus枯渇の早期終了。発火条件を hf63 が infeasible 族を検出済み(avoid非空)
            //   ＝「達成可能な focus を撃ち尽くした」ときに限定する。これは旧条件の厳密な部分集合のため、
            //   旧N4より早く止まることはない＝品質は退化しない。
            // [ユーザー指示/HARD残でもSOFT focus, Kotlin原本] 停滞した HARD(covU等)を deprioritize しても
            //   なお狙える族が残るなら、早期終了せず残ラウンドを SOFT 最適化に振り向ける。keep-best(Better()
            //   は hard 非悪化を要求)が HARD 悪化を防ぐ＝HARD残のまま SOFT を最適化しても安全。本当に狙える
            //   族が尽きた(pivot=="total" or 件数0)ときだけ従来どおり空転停止する。
            if (stagnantRounds >= 2 && dynamicAvoid.Count > 0)
            {
                var pivot = MaxViolatedFamily(bestReport, avoid, round, rounds);   // avoid=dynamicAvoid＋静的covU床
                if (pivot == "total" || bestReport.Breakdown.GetValueOrDefault(pivot, 0) == 0)
                {
                    logs.Add(new MirrorLog(tag: "RunMAGI_RSI",
                        message: $"早期終了: 狙える族が枯渇(deprioritize={avoid.Count}族)＋{stagnantRounds}R無改善（残{rounds - round - 1}Rの空転を停止）",
                        iter: iters));
                    break;
                }
                if (pivot != lastLoggedPivot)
                {
                    // [3.288.0/スパム対応, Kotlin原本] pivot が変わったときだけログ（旧: 停滞が続く限り毎ラウンド同文）。
                    logs.Add(new MirrorLog(tag: "RunMAGI_RSI",
                        message: $"HARD残({string.Join(",", dynamicAvoid)})を回避しSOFTへピボット継続 → 次focus候補={pivot}（HARD非悪化はkeep-bestが担保）",
                        iter: iters));
                    lastLoggedPivot = pivot;
                }
            }
        }
        // [3.288.0/ログ強化=回数軸, Kotlin原本] 戦略変更の1行サマリ（focus遷移を連続圧縮）。2手以上あるときだけ出す＝スパムなし。
        if (focusTrail.Count(it => !it.StartsWith('[')) >= 2)
        {
            var hf63Note = hf63.InfeasibleFamilies();
            var hf63Suffix = hf63Note.Count > 0 ? " / HF63降格={" + string.Join(",", hf63Note) + "}" : "";
            logs.Add(new MirrorLog(tag: "戦略変更",
                message: $"RSI focus遷移: {CompressFocusTrail(focusTrail)}" + (e9Cooldowns > 0 ? $" / E9冷却{e9Cooldowns}回" : "") + hf63Suffix,
                iter: iters));
        }
        // [3.288.0/ログ強化=状態軸, Kotlin原本] このRSI実行でHF63が「構造的に充足困難」と学習した族を実行
        //   横断で集約（エピローグの残存分析行が読む。ワーカー並行呼出があるため synchronized 集約）。
        RecordInfeasibleScoped(hf63.InfeasibleFamilies());
        return new V6OptimizerResult(best, bestReport with { Logs = logs.Concat(bestReport.Logs).ToList() }, V6Algorithm.Rsi, logs, iters, NowMs() - started);
    }
}
