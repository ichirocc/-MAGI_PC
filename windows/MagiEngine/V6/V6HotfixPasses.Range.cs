using System.Globalization;
using MagiEngine.Model;

namespace MagiEngine.V6;

public static partial class V6HotfixPasses
{
    // [手M用] 手M(tryExactDayMatching)専用の到達不能を表す巨大コスト。
    private const long DAY_MATCH_INF = 1_000_000_000_000L;

    /// <summary>
    /// [手M用] 最小費用完全割当（Hungarian法、O(n^3)）。<paramref name="cost"/>[i][j] = i行をj列へ割り当てる費用。
    /// 到達不能な組合せは <paramref name="inf"/> 以上として渡す。返り値[i] = 行iへ割り当てた列。
    /// 実行不能（最短交互路が構築できない/到達不能な列しか残らない等）なら null。
    /// </summary>
    private static int[]? MinCostPerfectAssignment(long[][] cost, long inf = DAY_MATCH_INF)
    {
        var n = cost.Length;
        if (n == 0 || cost.Any(row => row.Length != n)) return null;
        var u = new long[n + 1];
        var v = new long[n + 1];
        var p = new int[n + 1];
        var way = new int[n + 1];

        for (var i = 1; i <= n; i++)
        {
            p[0] = i;
            var j0 = 0;
            var minv = new long[n + 1];
            for (var idx = 0; idx <= n; idx++) minv[idx] = inf;
            var used = new bool[n + 1];
            do
            {
                used[j0] = true;
                var i0 = p[j0];
                var delta = inf;
                var j1 = -1;
                for (var j = 1; j <= n; j++)
                {
                    if (used[j]) continue;
                    var raw = cost[i0 - 1][j - 1];
                    if (raw < inf / 2)
                    {
                        var cur = raw - u[i0] - v[j];
                        if (cur < minv[j])
                        {
                            minv[j] = cur;
                            way[j] = j0;
                        }
                    }
                    // raw が到達不能でも、別の交互木ノードから既に入った minv は比較対象。
                    if (minv[j] < delta)
                    {
                        delta = minv[j];
                        j1 = j;
                    }
                }
                if (j1 < 0 || delta >= inf / 2) return null;
                for (var j = 0; j <= n; j++)
                {
                    if (used[j])
                    {
                        u[p[j]] += delta;
                        v[j] -= delta;
                    }
                    else if (j > 0 && minv[j] < inf / 2)
                    {
                        minv[j] -= delta;
                    }
                }
                j0 = j1;
            } while (p[j0] != 0);

            do
            {
                var j1 = way[j0];
                p[j0] = p[j1];
                j0 = j1;
            } while (j0 != 0);
        }

        var outp = new int[n];
        for (var i = 0; i < n; i++) outp[i] = -1;
        for (var j = 1; j <= n; j++) if (p[j] > 0) outp[p[j] - 1] = j - 1;
        if (outp.Any(x => x < 0)) return null;
        for (var i = 0; i < n; i++) if (cost[i][outp[i]] >= inf / 2) return null;
        return outp;
    }

    /// <summary>[手M] 対象日1日ぶんの候補案（当該日の全職員シフトtoken配置＋評価）。</summary>
    private sealed record DayPlan(int Day, int[] Shifts, ViolationReport Report, int Changed, long Heuristic);

    /// <summary>[手F] 対象日1日ぶんの候補案（フローで解いた割当＋隣接日調整の追加手＋評価）。</summary>
    private sealed record FlowPlan(int Day, int[] Assignment, ViolationReport Report, int Changed, long FlowCost, IReadOnlyList<int[]> Extras);

    /// <summary>
    /// [RangePolish・個人回数(staffRange low/high, 重み90/45)専用の研磨パス] 桒澤美幸の実例（唯一の代替
    /// 要員が現在のシフトを担当できず直接交換相手が存在しない局面）を受け、玉突き連鎖（<see cref="V6SearchOperators.FindCovUChain"/>）
    /// だけでなく、手M(<see cref="MinCostPerfectAssignment"/>による当日の完全割当の組み替え)・手F
    /// (<see cref="FlexibleDayFlow"/>による当日の人数構成そのものを変える最小費用フロー) を追加した研磨パス。
    ///
    /// アンカーは <c>countViolations</c>（"i,k"→"vio-low"/"vio-high"）から違反している(staff,shift)ペアを
    /// 列挙する。HIGH(超過)はそのシフトの保有日を他の担当可能シフトへ、LOW(不足)は保有していない日のうち
    /// 1日をそのシフトへ、それぞれ動かす。同一シフトでHIGH/LOWが両方あれば <see cref="TryPairSwap"/> で
    /// 直接ペアスワップ（同日2者の役割入替＝被覆完全保存）を最優先で試す。
    ///
    /// 付け替えで元シフトの被覆(covU)が悪化する場合は玉突き連鎖（<see cref="TryRelocate"/> 内で
    /// <see cref="V6SearchOperators.FindCovUChain"/>）で埋め直す。1回の付け替えで解消しない上限超過は
    /// 手M→手F の順に日単位の完全割当/フローへ拡張し、同一(i,k)が複数日で超過している場合はこの1パス内で
    /// 上限まで反復して落とす。採否は常に checker + isBetter + exactPinRegression(厳密ピン保護)。
    /// </summary>
    public static CyclicSwapResult ApplyRangePolish(
        MagiState state, int[][] schedule, int maxPasses = 3, Func<bool>? shouldStop = null, long seed = 0x8A9EL)
    {
        var stop = shouldStop ?? (() => false);
        // [3.326.0] 回数固定(lo==hi)だけが却下した候補試行を対象別に数える（緩和対象の提示用）。
        var pinBlocks = new PinBlockAttribution();
        var p = new Problem(state);
        var work = ScheduleUtil.NormalizeSchedule(schedule, p);
        var before = UnifiedViolationChecker.Check(state, work);
        var bestRep = before;
        var applied = 0;
        var rng = new JavaRandom(seed);
        // [監査で発見・3.270.0] p.wish[i][j]<0 は実現不能な希望まで動かせないと誤判定していた
        //   （3.183.0 LightMirrorOptimizer と同型のバグ）。wishLocked は canDo ガード込みで正しい。
        bool Movable(int i, int j) => !p.WishLocked(i, j);
        // [ログから職員が分かるように] 対象(staff,shift)の表示名。
        string Label(int i, int k) =>
            $"{(i >= 0 && i < state.StaffList.Count ? state.StaffList[i].Name : $"#{i}")} " +
            $"{(k >= 0 && k < state.Shifts.Count ? state.Shifts[k].Kigou : k.ToString())}";
        var fixedNames = new List<string>();
        var dayMatchingApplied = 0;
        var flexibleDayApplied = 0;

        // [頭打ちの理由を可視化] 対象(staff,shift)ごとに、何が原因で付け替えが不成立だったかを集計。
        //   希望固定=movableで即除外・禁止連続=makesForbiddenRunで即除外・候補なし=findCovUChainがnull・
        //   range後回し=findCovUChainは成立したが使った候補がrangeAvoid該当(=自身の新規high違反を招く)
        //   だった・不採用=chainは成立したがisBetterに拒否された、の5分類。最も多い理由を「残存:」へ表示。
        var blockStats = new Dictionary<(int, int), Dictionary<string, int>>();
        // [不採用の主因, 3.302.0] C1Polish と同じく、拒否された候補が重み付きで最も壊した族を併記する。
        var culpritStats = new Dictionary<(int, int), Dictionary<string, int>>();
        // [3.358.0/実機ログ起因] 「希望固定×16」「禁止連続×9」は**どの日か**が出ず、直しに行けなかった
        //   （ForbiddenDiag は同じ理由で日付を名指ししている＝そちらだけ行動につながる形だった）。
        //   日で決まる2理由だけ実日付を集める。件数は延べ・日は重複なし。
        var blockDays = new Dictionary<(int, int), Dictionary<string, HashSet<int>>>();
        // [3.358.0] 日番号を「M/D」へ。startDate が読めなければ「N日目」で妥協する（ログ専用）。
        DateOnly? start0 = DateOnly.TryParseExact(state.StartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var startParsed) ? startParsed : null;
        string DayLabel(int j)
        {
            if (start0.HasValue)
            {
                var d = start0.Value.AddDays(j);
                return $"{d.Month}/{d.Day}";
            }
            return $"{j + 1}日目";
        }
        void RecordBlock(
            (int, int) target, string reason,
            ViolationReport? after = null, ViolationReport? before2 = null, int? day = null)
        {
            if (!blockStats.TryGetValue(target, out var inner)) { inner = new Dictionary<string, int>(); blockStats[target] = inner; }
            inner[reason] = inner.GetValueOrDefault(reason) + 1;
            if (day != null)
            {
                if (!blockDays.TryGetValue(target, out var dayInner)) { dayInner = new Dictionary<string, HashSet<int>>(); blockDays[target] = dayInner; }
                if (!dayInner.TryGetValue(reason, out var daySet)) { daySet = new HashSet<int>(); dayInner[reason] = daySet; }
                daySet.Add(day.Value);
            }
            if (after != null && before2 != null)
            {
                var culprit = V6SearchOperators.WorstWorsenedFamily(after, before2);
                if (culprit != null)
                {
                    if (!culpritStats.TryGetValue(target, out var cinner)) { cinner = new Dictionary<string, int>(); culpritStats[target] = cinner; }
                    cinner[culprit] = cinner.GetValueOrDefault(culprit) + 1;
                }
            }
        }
        // [汎用玉突き結合フレームワーク, 3.249.0] tryRelocate が単独では不採用だった候補を蓄積し
        //   末尾で束ねる（手M/手Fは既にそれ自体が多職員同時最適化のため対象外＝スコープ限定）。
        var combinable = new List<CombinatorialRepair.Candidate>();

        // [玉突き連鎖つき1セル付け替え] day j の staff i を fromK から toK へ動かす。fromK 側の被覆が
        //   悪化するなら findCovUChain で埋め直す。採用ならtrue（bestRep/appliedは呼び出し側で更新済み）。
        bool TryRelocate((int, int) target, int i, int j, int fromK, int toK)
        {
            if (!Movable(i, j)) { RecordBlock(target, "希望固定", day: j); return false; }
            if (p.MakesForbiddenRun(work, i, j, toK)) { RecordBlock(target, "禁止連続", day: j); return false; }
            var cnt = 0;
            for (var s = 0; s < p.S; s++) if (work[s][j] == fromK) cnt++;
            var needsChain = p.CovUCell(fromK, j, cnt - 1) > p.CovUCell(fromK, j, cnt);
            // [厳密ピン保護] i(・玉突き相手)の回数変更がstaffRange厳密ピン(lo==hi)を新たに崩す候補は
            //   不採用にする（keep-best/重みは不変・追加ガードのみ）。
            var workBeforeRelocate = work.Copy2D();
            work[i][j] = toK;
            if (!needsChain)
            {
                var rep = UnifiedViolationChecker.Check(state, work);
                if (IsBetter(rep, bestRep) && !pinBlocks.BlocksImproving(p, workBeforeRelocate, work)) { bestRep = rep; applied++; return true; }
                work[i][j] = fromK;
                combinable.Add(new CombinatorialRepair.Candidate(
                    new List<int[]> { new[] { i, j, toK } }, "tryRelocate", Label(target.Item1, target.Item2)));
                if (IsBetter(rep, bestRep)) RecordBlock(target, C1PlateauDiagnosis.REASON_PIN);
                else RecordBlock(target, "不採用", after: rep, before2: bestRep);
                return false;
            }
            var chain = V6SearchOperators.FindCovUChain(p, work, fromK, j, rng, exclude: i,
                rangeAvoid: (st, fk) => ExceedsOwnRangeHi(p, work, st, fk));
            if (chain == null) { work[i][j] = fromK; RecordBlock(target, "候補なし"); return false; }
            var usedAvoided = chain.Any(mv => ExceedsOwnRangeHi(p, work, mv[0], mv[2]));
            var oldVals = chain.Select(mv => work[mv[0]][mv[1]]).ToArray();
            foreach (var mv in chain) work[mv[0]][mv[1]] = mv[2];
            var rep2 = UnifiedViolationChecker.Check(state, work);
            if (IsBetter(rep2, bestRep) && !pinBlocks.BlocksImproving(p, workBeforeRelocate, work)) { bestRep = rep2; applied++; return true; }
            for (var idx = 0; idx < chain.Count; idx++) work[chain[idx][0]][chain[idx][1]] = oldVals[idx];
            work[i][j] = fromK;
            combinable.Add(new CombinatorialRepair.Candidate(
                new List<int[]> { new[] { i, j, toK } }.Concat(chain).ToList(), "tryRelocate", Label(target.Item1, target.Item2)));
            if (usedAvoided) RecordBlock(target, "range後回し");
            else if (IsBetter(rep2, bestRep)) RecordBlock(target, C1PlateauDiagnosis.REASON_PIN);
            else RecordBlock(target, "不採用", after: rep2, before2: bestRep);
            return false;
        }

        // [複数ターゲット同時解決=ユーザー指示「賢く深く網羅的に」・grilling確定] 同一シフトkについて
        //   high(超過)のhiとlow(不足)のloが両方存在する場合、findCovUChainの玉突き探索を経由せず、
        //   直接のペアスワップ(hiのk保有日を1日、loへ振替え・loの元シフトをhiが引き受ける)を最優先で
        //   試す。被覆(covU/covO)は完全保存(同日2者の役割入替のみ)のため、玉突き連鎖が構造的に見つから
        //   ない(=「候補なし」)局面でも確実に解決できる（桒澤美幸のAｱ超過×他職員のAｱ不足のような、
        //   同一シフトの過不足ペアに直接効く。RangePolishの`findCovUChain`頭打ちを回避する第2の経路）。
        bool TryPairSwap(int hi, int k, int lo)
        {
            for (var j = 0; j < p.T; j++)
            {
                if (stop()) return false;
                if (work[hi][j] != k || !Movable(hi, j) || !Movable(lo, j)) continue;
                var loK = work[lo][j];
                if (loK == k || loK < 0 || loK >= p.K) continue;
                if (!p.CanDo(hi, loK) || !p.CanDo(lo, k)) continue;
                if (p.MakesForbiddenRun(work, hi, j, loK) || p.MakesForbiddenRun(work, lo, j, k)) continue;
                var workBeforeSwap = work.Copy2D();
                work[hi][j] = loK; work[lo][j] = k;
                var rep = UnifiedViolationChecker.Check(state, work);
                if (IsBetter(rep, bestRep) && !pinBlocks.BlocksImproving(p, workBeforeSwap, work)) { bestRep = rep; applied++; return true; }
                work[hi][j] = k; work[lo][j] = loK;
            }
            return false;
        }

        /// <summary>
        /// 手M: 対象日の全職員を最小費用完全割当で同時に組み替える。
        /// - <paramref name="hi"/> は当該日の <paramref name="k"/> を必ず手放す。
        /// - receiver は当該日の <paramref name="k"/> を必ず受け取る。
        /// - その日のシフトtokenは並べ替えるだけなので日別人数は完全保存。
        /// - receiverごとに完全割当を解き、full checkerで最良の1案だけ採用。
        /// </summary>
        bool TryExactDayMatching((int, int) target, int hi, int k)
        {
            if (p.S <= 1 || p.T <= 0) return false;

            var counts = new int[p.S][];
            for (var idx = 0; idx < p.S; idx++) counts[idx] = new int[p.K];
            for (var i = 0; i < p.S; i++)
                for (var j = 0; j < p.T; j++)
                {
                    var kk = work[i][j];
                    if (kk >= 0 && kk < p.K) counts[i][kk]++;
                }
            var flex = new int[p.S];
            for (var idx = 0; idx < p.S; idx++) flex[idx] = p.AllowedShiftsForStaff(idx).Length;

            long RangePenalty(int i, int kk, int count)
            {
                var outv = 0L;
                var lo = p.RangeLo[i][kk];
                var hiLim = p.RangeHi[i][kk];
                if (lo != int.MinValue && count < lo) outv += (long)(lo - count) * 90L;
                if (hiLim != int.MaxValue && count > hiLim) outv += (long)(count - hiLim) * 45L;
                return outv;
            }

            long RowCost(int i, int oldK, int newK)
            {
                var outv = 0L;
                for (var kk = 0; kk < p.K; kk++)
                {
                    var c = counts[i][kk];
                    if (newK != oldK)
                    {
                        if (kk == oldK) c--;
                        if (kk == newK) c++;
                    }
                    outv += RangePenalty(i, kk, c);
                    var apt = p.Apt[i][kk];
                    if (apt >= 0) outv += c >= apt ? c - apt : apt - c;
                }
                // 同品質なら短い循環を優先し、不要な大規模入替えを避ける。
                if (newK != oldK) outv += 2L;
                // target shiftの偏在を軽く抑える。明示rangeが無い一般代用者同士のtie-break。
                if (newK == k) outv += counts[i][k];
                return outv;
            }

            int ReceiverRoom(int i)
            {
                var hiLim = p.RangeHi[i][k];
                return hiLim == int.MaxValue ? 10_000 : hiLim - counts[i][k];
            }

            DayPlan? bestPlan = null;
            var trials = 0;
            // 実データ10名×31日では全候補を網羅。大規模データでも後処理予算を食い潰さない上限。
            const int maxTrials = 128;

            for (var j = 0; j < p.T; j++)
            {
                if (stop() || trials >= maxTrials) break;
                if (work[hi][j] != k || !Movable(hi, j)) continue;
                var tokens = new int[p.S];
                for (var idx = 0; idx < p.S; idx++) tokens[idx] = work[idx][j];
                // [3.278.0/監査修正] -1(正規化センチネル)トークンを含む日は当該列が全行INF＝Hungarianが必ず
                //   null になるのに、旧実装は receiver 1件ごとに trials(上限128)を浪費していた。日ごと事前スキップ。
                if (tokens.Any(t => t < 0 || t >= p.K)) continue;

                var rawReceivers = Enumerable.Range(0, p.S).Where(r =>
                    r != hi &&
                    work[r][j] != k &&
                    Movable(r, j) &&
                    p.CanDo(r, k) &&
                    ReceiverRoom(r) > 0).ToList();
                if (rawReceivers.Count == 0) continue;
                var maxFlex = rawReceivers.Max(r => flex[r]);
                var receivers = rawReceivers
                    .OrderByDescending(r => bestRep.CountViolations.GetValueOrDefault($"{r},{k}") == "vio-low" ? 1 : 0)
                    .ThenByDescending(r => flex[r] >= maxFlex - 1 ? 1 : 0)
                    .ThenByDescending(r => ReceiverRoom(r))
                    .ThenBy(r => counts[r][k])
                    .ThenBy(r => r)
                    .ToList();

                foreach (var receiver in receivers)
                {
                    if (stop() || trials++ >= maxTrials) break;
                    var cost = new long[p.S][];
                    for (var idx = 0; idx < p.S; idx++)
                    {
                        cost[idx] = new long[p.S];
                        for (var jdx = 0; jdx < p.S; jdx++) cost[idx][jdx] = DAY_MATCH_INF;
                    }
                    for (var i = 0; i < p.S; i++)
                    {
                        var oldK = work[i][j];
                        for (var tokenIdx = 0; tokenIdx < p.S; tokenIdx++)
                        {
                            var newK = tokens[tokenIdx];
                            if (newK < 0 || newK >= p.K) continue;
                            if (i == hi && newK == k) continue;
                            if (i == receiver && newK != k) continue;
                            if (newK != oldK)
                            {
                                // [3.417.0] 旧: 記号が「希」のシフトを割当先から外していた（3.278.0）。撤去の根拠は
                                //   TryFlexibleDayFlow 側の同種箇所に記載（HF77: コメント≠実装／実測で中立／
                                //   別の職場では黙って効かない、の3点）。
                                if (!Movable(i, j) || !p.CanDo(i, newK)) continue;
                                work[i][j] = newK;
                                var badRun = p.MakesForbiddenRun(work, i, j, newK);
                                work[i][j] = oldK;
                                if (badRun) continue;
                            }
                            cost[i][tokenIdx] = RowCost(i, oldK, newK);
                        }
                    }

                    var assignment = MinCostPerfectAssignment(cost);
                    if (assignment == null) continue;
                    var newDay = new int[p.S];
                    for (var idx = 0; idx < p.S; idx++) newDay[idx] = tokens[assignment[idx]];
                    if (newDay[hi] == k || newDay[receiver] != k) continue;
                    var changed = 0;
                    var heuristic = 0L;
                    // [厳密ピン保護] 完全割当は当日のトークンを全職員で並べ替えるため、複数職員の回数を
                    //   同時に変えうる。staffRange厳密ピン(lo==hi)を新たに崩す日案は不採用にする。
                    var workBeforeDayMatch = work.Copy2D();
                    for (var i = 0; i < p.S; i++)
                    {
                        if (newDay[i] != tokens[i]) changed++;
                        heuristic += cost[i][assignment[i]];
                        work[i][j] = newDay[i];
                    }
                    var rep = UnifiedViolationChecker.Check(state, work);
                    var pinBad = V6SearchOperators.ExactPinRegression(p, workBeforeDayMatch, work);
                    for (var i = 0; i < p.S; i++) work[i][j] = tokens[i];

                    if (!IsBetter(rep, bestRep) || pinBad) continue;
                    var oldBest = bestPlan;
                    var betterPlan = oldBest == null ||
                        IsBetter(rep, oldBest.Report) ||
                        (rep.Hard == oldBest.Report.Hard &&
                            rep.Total == oldBest.Report.Total &&
                            Math.Abs(rep.WeightedScore - oldBest.Report.WeightedScore) <= 1e-6 &&
                            (heuristic < oldBest.Heuristic ||
                                (heuristic == oldBest.Heuristic && changed < oldBest.Changed)));
                    if (betterPlan) bestPlan = new DayPlan(j, newDay, rep, changed, heuristic);
                }
            }

            var plan = bestPlan;
            if (plan == null)
            {
                RecordBlock(target, "日割当候補なし");
                return false;
            }
            for (var i = 0; i < p.S; i++) work[i][plan.Day] = plan.Shifts[i];
            bestRep = plan.Report;
            applied++;
            dayMatchingApplied++;
            return true;
        }

        /// <summary>
        /// 手F: 日別シフト多重集合も変えられる最小費用フロー。
        ///
        /// 手Mは「その日に既に存在するシフトtokenの並替え」なので、美幸Aｱ→B1のように
        /// その日にB1 tokenが存在しないケースを表現できない。手Fは各職員から担当可能シフトへ辺を張り、
        /// シフト側の1人目・2人目…にcovU/covOの限界費用を与える。これにより
        ///   美幸 Aｱ→B1 ＋ 別職員 休/C系→Aｱ
        /// のような、日別人数を変える置換を1回の厳密最適化で作る。
        ///
        /// - 希望/管理者固定セルは現在シフト以外へ移動不可。
        /// - 変更先はcanDo必須。
        /// - c3nはmakesForbiddenRunで辺を除外。ただし直接は禁止連続でも、隣接日(j±1)を本人が
        ///   調整すれば崩せる場合は<see cref="V6SearchOperators.TryFixForbiddenRunViaAdjacentDay"/>で救済し、
        ///   辺を生かす（受取職員自身の隣接日にも同じ救済が及ぶ＝対称）。
        /// - staffRange low/high、apt、変更セル数を職員辺費用へ入れる。
        /// - covU/covOは人数qに対する凸罰則の限界費用としてシフト→sink辺へ入れる。
        /// - 最終採否は必ずUnifiedViolationChecker＋isBetter。近似費用だけでは採用しない
        ///   （隣接日の追加手・玉突きも含めた盤面全体で1回評価）。
        /// </summary>
        bool TryFlexibleDayFlow((int, int) target, int victim, int forbiddenK, int[] candidateDays)
        {
            if (p.S <= 0 || p.K <= 0) return false;
            var counts = new int[p.S][];
            for (var idx = 0; idx < p.S; idx++) counts[idx] = new int[p.K];
            for (var i = 0; i < p.S; i++)
                for (var j = 0; j < p.T; j++)
                {
                    var kk = work[i][j];
                    if (kk >= 0 && kk < p.K) counts[i][kk]++;
                }

            long RangeAndAptCost(int i, int oldK, int newK)
            {
                var outv = 0L;
                for (var kk = 0; kk < p.K; kk++)
                {
                    var c = counts[i][kk];
                    if (newK != oldK)
                    {
                        if (kk == oldK) c--;
                        if (kk == newK) c++;
                    }
                    var lo = p.RangeLo[i][kk];
                    var hi = p.RangeHi[i][kk];
                    if (lo != int.MinValue && c < lo) outv += (long)(lo - c) * 90L;
                    if (hi != int.MaxValue && c > hi) outv += (long)(c - hi) * 45L;
                    var a = p.Apt[i][kk];
                    if (a >= 0) outv += Math.Abs(c - a);
                }
                if (newK != oldK) outv += 2L;
                return outv;
            }

            // [HF77明示指示 2026-08-27] covO 重み 1→5。MirrorKeys の重み階層と整合させた限界費用のため同時に変更。
            long DayPenalty(int k, int j, int q) =>
                p.CovUCell(k, j, q) * 8000L + p.CovOCell(k, j, q) * 5L;

            FlowPlan? bestPlan = null;
            var days = candidateDays.Where(d => d >= 0 && d < p.T).Distinct().ToList();
            foreach (var j in days)
            {
                if (stop()) break;
                if (work[victim][j] != forbiddenK || !Movable(victim, j)) continue;
                var oldDay = new int[p.S];
                for (var idx = 0; idx < p.S; idx++) oldDay[idx] = work[idx][j];
                // [3.246.0 隣接日連動] (i,newK)ペア単位で「直接は禁止連続だが隣接日調整で救済できるか」を
                //   メモ化。j±1は本ループの間ずっと不変(day-jのtrialは他日を触らない)なので日jの間は再利用可。
                var adjacentFix = new Dictionary<(int, int), List<int[]>>();

                // primary costを1024倍し、下位10bitだけを決定的tie-breakに使う。
                // 8試行してc42/c1等の非分離制約に対する代替案もfull checkerへ渡す。
                for (var trial = 0; trial < 8; trial++)
                {
                    if (stop()) break;
                    var staffCost = new long[p.S][];
                    for (var idx = 0; idx < p.S; idx++)
                    {
                        staffCost[idx] = new long[p.K];
                        for (var kdx = 0; kdx < p.K; kdx++) staffCost[idx][kdx] = FlexibleDayFlow.INF;
                    }
                    for (var i = 0; i < p.S; i++)
                    {
                        var oldK = oldDay[i];
                        for (var newK = 0; newK < p.K; newK++)
                        {
                            if (i == victim && newK == forbiddenK) continue;
                            var changed = newK != oldK;
                            if (changed)
                            {
                                // [3.417.0] 記号「希」を割当先から外すガードを撤去。根拠3点:
                                //   ①**主張が実装されていない**: コメントは「最適化が自由生成しない」と書いていたが、
                                //     このガードは研磨3箇所にしかなく、探索本体（SA/ALNS の randomAllowedCell・
                                //     destroyRepair・findTargetedFix 等）は allowedShiftsForStaff から選ぶので
                                //     素通り＝方針として機能していなかった（HF77: コメント≠実装）。
                                //   ②**実測で中立**: 「希」を含む唯一の実データ blocked_covu_state（希望10件＝
                                //     盤面10セル）でガードは1686回発火するが、外すと後処理研磨の結果は
                                //     hard=4/total=311/weighted=34149 と**バイト一致**。弾いていた候補は目的
                                //     関数側でも全て負けていた。
                                //   ③**別の職場では黙って効かない**: 記号が「希望」「W」等なら同じ意図でも
                                //     一切適用されない。
                                if (!Movable(i, j) || !p.CanDo(i, newK)) continue;
                                work[i][j] = newK;
                                var badRun = p.MakesForbiddenRun(work, i, j, newK);
                                work[i][j] = oldK;
                                if (badRun)
                                {
                                    if (!adjacentFix.TryGetValue((i, newK), out var fix))
                                    {
                                        fix = V6SearchOperators.TryFixForbiddenRunViaAdjacentDay(p, work, i, j, newK, rng)
                                            ?? new List<int[]>();
                                        adjacentFix[(i, newK)] = fix;
                                    }
                                    if (fix.Count == 0) continue;
                                }
                            }
                            else if (i == victim && !p.CanDo(i, newK))
                            {
                                // groupViol対象をそのまま残す辺は禁止。他職員の固定済み不正セルは
                                // この1手の実行可能性を壊さないため現状維持だけ許す。
                                continue;
                            }
                            var primary = RangeAndAptCost(i, oldK, newK);
                            var tie = (long)((i * 131 + newK * 31 + trial * 17) & 1023);
                            staffCost[i][newK] = primary * 1024L + tie;
                        }
                    }

                    var marginal = new long[p.K][];
                    for (var k = 0; k < p.K; k++)
                    {
                        marginal[k] = new long[p.S];
                        for (var q0 = 0; q0 < p.S; q0++)
                        {
                            var q = q0 + 1;
                            marginal[k][q0] = (DayPenalty(k, j, q) - DayPenalty(k, j, q - 1)) * 1024L;
                        }
                    }
                    var solved = FlexibleDayFlow.Solve(staffCost, marginal);
                    if (solved == null) continue;
                    if (solved.Assignment[victim] == forbiddenK) continue;

                    // 選ばれた(i,newK)のうち禁止連続の隣接日救済が要ったものを1件の候補として合流。
                    var extras = new List<int[]>();
                    for (var i = 0; i < p.S; i++)
                    {
                        var newK = solved.Assignment[i];
                        if (newK == oldDay[i]) continue;
                        if (adjacentFix.TryGetValue((i, newK), out var fixList)) extras.AddRange(fixList);
                    }

                    var changedCount = 0;
                    // [厳密ピン保護] 柔軟日フローも当日の人数構成と隣接日調整(extras)を同時に変えるため、
                    //   複数職員の回数を同時に変えうる。staffRange厳密ピン(lo==hi)を新たに崩す案は不採用。
                    var workBeforeFlow = work.Copy2D();
                    for (var i = 0; i < p.S; i++)
                    {
                        if (solved.Assignment[i] != oldDay[i]) changedCount++;
                        work[i][j] = solved.Assignment[i];
                    }
                    var extraOld = extras.Select(mv => work[mv[0]][mv[1]]).ToArray();
                    foreach (var mv in extras) work[mv[0]][mv[1]] = mv[2];
                    var rep = UnifiedViolationChecker.Check(state, work);
                    var pinBad = V6SearchOperators.ExactPinRegression(p, workBeforeFlow, work);
                    for (var idx = 0; idx < extras.Count; idx++) work[extras[idx][0]][extras[idx][1]] = extraOld[idx];
                    for (var i = 0; i < p.S; i++) work[i][j] = oldDay[i];
                    if (!IsBetter(rep, bestRep) || pinBad) continue;

                    var oldBest = bestPlan;
                    var betterPlan = oldBest == null ||
                        IsBetter(rep, oldBest.Report) ||
                        (rep.Hard == oldBest.Report.Hard &&
                            rep.Total == oldBest.Report.Total &&
                            Math.Abs(rep.WeightedScore - oldBest.Report.WeightedScore) <= 1e-6 &&
                            (changedCount < oldBest.Changed ||
                                (changedCount == oldBest.Changed && solved.Cost < oldBest.FlowCost)));
                    if (betterPlan) bestPlan = new FlowPlan(j, solved.Assignment, rep, changedCount, solved.Cost, extras);
                }
            }

            var plan = bestPlan;
            if (plan == null)
            {
                RecordBlock(target, "柔軟日割当候補なし");
                return false;
            }
            for (var i = 0; i < p.S; i++) work[i][plan.Day] = plan.Assignment[i];
            foreach (var mv in plan.Extras) work[mv[0]][mv[1]] = mv[2];
            bestRep = plan.Report;
            applied++;
            flexibleDayApplied++;
            return true;
        }

        var pass = 0;
        while (pass < maxPasses)
        {
            if (stop()) break;
            var improved = false;

            // [手F/groupViol] staffRangeのhigh表示に依存せず、担当不可セルを直接対象にする。
            // 添付データの美幸AｱはstaffRange[3,4]が無くてもgroupShift上で担当不可なのでここで5日全て拾う。
            var groupTargets = new List<(int I, int J, int K)>();
            for (var i = 0; i < p.S; i++)
                for (var j = 0; j < p.T; j++)
                {
                    var k = work[i][j];
                    if (k >= 0 && k < p.K && !p.CanDo(i, k)) groupTargets.Add((i, j, k));
                }
            foreach (var (i, j, k) in groupTargets)
            {
                if (stop()) break;
                if (work[i][j] != k || p.CanDo(i, k)) continue;
                var target = (i, k);
                if (!Movable(i, j))
                {
                    RecordBlock(target, "担当不可セルが希望/管理者固定");
                    continue;
                }
                if (TryFlexibleDayFlow(target, i, k, new[] { j }))
                {
                    improved = true;
                    fixedNames.Add($"{Label(i, k)} {j + 1}日");
                }
            }

            // [3.278.0/監査修正] pass 0 でも直前の groupTargets ループ(手F)が盤面を変更済み(improved)なら
            //   before は陳腐＝解消済みターゲットへの空振り・新規違反の見落としを防ぐため再検査する。
            var rep0 = (pass == 0 && !improved) ? before : UnifiedViolationChecker.Check(state, work);
            var highTargets = new List<(int, int)>();
            var lowTargets = new List<(int, int)>();
            foreach (var (key, cls) in rep0.CountViolations)
            {
                var parts = key.Split(',');
                var i = KotlinInterop.ToIntOrNull(parts.Length > 0 ? parts[0] : null);
                if (i == null) continue;
                var k = KotlinInterop.ToIntOrNull(parts.Length > 1 ? parts[1] : null);
                if (k == null) continue;
                if (cls == "vio-high") highTargets.Add((i.Value, k.Value));
                else if (cls == "vio-low") lowTargets.Add((i.Value, k.Value));
            }
            if (highTargets.Count == 0 && lowTargets.Count == 0) break;

            // HIGH(超過): shift k の保有日を他の担当可能シフトへ動かす。
            foreach (var (i, k) in highTargets)
            {
                if (stop()) break;
                var target = (i, k);
                var done = false;
                // [手M→手F] まず日別人数を保存する完全割当。無ければ日別人数も最適化するフローへ拡張。
                // 同じ(i,k)が上限を複数回超えていても、この1パス内で上限まで反復して落とす。
                var hiLim = p.RangeHi[i][k];
                var guard = 0;
                while (hiLim != int.MaxValue && work[i].Count(x => x == k) > hiLim && guard++ < p.T)
                {
                    var fixedOne = TryExactDayMatching(target, i, k) ||
                        TryFlexibleDayFlow(target, i, k,
                            Enumerable.Range(0, p.T).Where(j => work[i][j] == k && Movable(i, j)).ToArray());
                    if (!fixedOne) break;
                    improved = true;
                    done = true;
                    fixedNames.Add(Label(i, k));
                }
                if (done) continue;
                // [複数ターゲット同時解決] まず同一シフトkのlow(不足)職員との直接ペアスワップを試す
                //   （findCovUChain経由の玉突きより優先＝被覆完全保存で確実に解決できる）。
                foreach (var (lo, lk) in lowTargets)
                {
                    if (done || stop()) break;
                    if (lk != k || lo == i) continue;
                    if (TryPairSwap(i, k, lo)) { improved = true; done = true; fixedNames.Add(Label(i, k)); }
                }
                if (done) continue;
                for (var j = 0; j < p.T; j++)
                {
                    if (done || stop()) break;
                    if (work[i][j] != k) continue;
                    foreach (var alt in p.AllowedShiftsForStaff(i))
                    {
                        if (done || stop()) break;
                        if (alt == k) continue;
                        if (TryRelocate(target, i, j, k, alt)) { improved = true; done = true; fixedNames.Add(Label(i, k)); }
                    }
                }
            }
            // LOW(不足): shift k を保有していない日のうち1日をshift kへ動かす。
            foreach (var (i, k) in lowTargets)
            {
                if (stop()) break;
                if (!p.CanDo(i, k)) continue;
                var target = (i, k);
                var done = false;
                // [複数ターゲット同時解決] まず同一シフトkのhigh(超過)職員との直接ペアスワップを試す
                //   （HIGHループで既に解決済みのペアはtryPairSwap内でその日を再訪しても無害＝重複コスト
                //   のみ）。
                foreach (var (hi, hk) in highTargets)
                {
                    if (done || stop()) break;
                    if (hk != k || hi == i) continue;
                    if (TryPairSwap(hi, k, i)) { improved = true; done = true; fixedNames.Add(Label(i, k)); }
                }
                if (done) continue;
                for (var j = 0; j < p.T; j++)
                {
                    if (done || stop()) break;
                    var oldK = work[i][j];
                    if (oldK == k || oldK < 0 || oldK >= p.K) continue;
                    if (TryRelocate(target, i, j, oldK, k)) { improved = true; done = true; fixedNames.Add(Label(i, k)); }
                }
            }
            pass++;
            if (!improved) break;
        }
        // [汎用玉突き結合フレームワーク, 3.249.0] stuckNames より前に実行し、結合で解消した箇所が
        //   「残存」に残らないようにする。
        var rangeCombStats = new CombinatorialRepair.Stats();
        bestRep = CombinatorialRepair.CombineAndApply(
            state, work, bestRep, Enumerable.Reverse(combinable).ToList(), IsBetter,
            shouldStop: stop, stats: rangeCombStats, p: p);
        applied += rangeCombStats.CombosAccepted;
        // [ログから職員が分かるように・頭打ちの理由を可視化] 研磨後もなお残っている(staff,shift)を、
        //   最も多かった頭打ち理由(希望固定/禁止連続/候補なし/range後回し/不採用)付きで列挙。
        var stuckNames = bestRep.CountViolations
            .Where(kv => kv.Value == "vio-high" || kv.Value == "vio-low")
            .Select(kv =>
            {
                var parts = kv.Key.Split(',');
                var i = KotlinInterop.ToIntOrNull(parts.Length > 0 ? parts[0] : null);
                if (i == null) return null;
                var k = KotlinInterop.ToIntOrNull(parts.Length > 1 ? parts[1] : null);
                if (k == null) return null;
                blockStats.TryGetValue((i.Value, k.Value), out var reasons);
                if (reasons == null || reasons.Count == 0) return Label(i.Value, k.Value);
                var top = reasons.OrderByDescending(r => r.Value).First();
                // [不採用の主因, 3.302.0] C1Polish と同型。「不採用」のときだけ主因族を上位2件併記。
                var culprits = "";
                if (top.Key == "不採用" && culpritStats.TryGetValue((i.Value, k.Value), out var cmap))
                {
                    var joined = string.Join(" ", cmap.OrderByDescending(c => c.Value).Take(2).Select(c => $"{c.Key}:{c.Value}"));
                    if (joined.Length > 0) culprits = $" 主因 {joined}";
                }
                // [3.358.0] 日で決まる理由（希望固定・禁止連続）は実日付を出す＝そのまま直しに行ける。
                var daysList = blockDays.TryGetValue((i.Value, k.Value), out var dinner) && dinner.TryGetValue(top.Key, out var dset)
                    ? dset.OrderBy(d => d).ToList() : new List<int>();
                var dayTxt = daysList.Count == 0 ? "" :
                    ": " + string.Join("・", daysList.Take(6).Select(DayLabel)) +
                        (daysList.Count > 6 ? $"ほか{daysList.Count - 6}日" : "");
                return $"{Label(i.Value, k.Value)}({top.Key}×{top.Value}{dayTxt}{culprits})";
            })
            .Where(s => s != null)
            .Select(s => s!)
            .ToList();
        var rangeCombSummary = rangeCombStats.Summary();
        var lowBefore = before.Breakdown.GetValueOrDefault("low", 0);
        var lowAfter = bestRep.Breakdown.GetValueOrDefault("low", 0);
        var highBefore = before.Breakdown.GetValueOrDefault("high", 0);
        var highAfter = bestRep.Breakdown.GetValueOrDefault("high", 0);
        var msg = $"個人回数(low/high)玉突き研磨: low {lowBefore}->{lowAfter} / high {highBefore}->{highAfter} " +
            $"/ total {before.Total}->{bestRep.Total} HARD {before.Hard}->{bestRep.Hard} 採用{applied}回" +
            $"（日割当:{dayMatchingApplied} / 柔軟日割当:{flexibleDayApplied}）";
        if (applied == 0 && lowBefore + highBefore > 0) msg += " [頭打ち=改善手なし]";
        if (fixedNames.Count > 0) msg += $" 対象: {string.Join(", ", fixedNames)}";
        if (stuckNames.Count > 0) msg += $" 残存: {string.Join(", ", stuckNames)}";
        if (rangeCombSummary.Length > 0) msg += $" / {rangeCombSummary}";
        var logs = new[] { new MirrorLog(tag: "RangePolish", message: msg) };
        return new CyclicSwapResult(work, before.Total, bestRep.Total, applied, logs,
            ObservedPinBlockedAttempts: pinBlocks.Attempts, PinBlocks: pinBlocks);
    }
}
