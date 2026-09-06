using MagiEngine.Model;

namespace MagiEngine.V6;

public static partial class V6HotfixPasses
{
    /// <summary>
    /// [フェーズ6, ピース21/22] Kotlin原本 <c>DayAssignResult</c>（<c>applyDayAssignmentPolish</c>と
    /// <c>applyAlternatingSoftPolish</c>が共有する結果型）の忠実な移植。
    /// </summary>
    public sealed record DayAssignResult(
        int[][] NewSchedule,
        int BeforeTotal,
        int AfterTotal,
        int AppliedDays,
        IReadOnlyList<MirrorLog> Logs,
        /// <summary>[3.326.0] 回数固定だけが却下した候補試行（対象別）。</summary>
        PinBlockAttribution? PinBlocks = null);

    /// <summary>
    /// [ソフト研磨・厳密] 日ごと最小費用割当による研磨。各日の (日,シフト) 人数（=HARD充足）を固定したまま、
    /// 希望未固定(wish&lt;0)の職員を、その日の同一シフト集合へ「個人別回数(range)・適切回数(apt)の逸脱が
    /// 最小」に<b>厳密再割当</b>（Hungarian）。乱択でなく日内最適の候補を作り、全体が改善した日だけ採用
    /// （keep-best＝退化なし）。連続規則・希望・平準化など列横断の相互作用は採用判定
    /// （<see cref="UnifiedViolationChecker"/>）で担保する。
    /// </summary>
    public static DayAssignResult ApplyDayAssignmentPolish(
        MagiState state, int[][] schedule, Func<bool>? shouldStop = null)
    {
        var stop = shouldStop ?? (() => false);
        var pinBlocks = new PinBlockAttribution();
        var p = new Problem(state);
        var work = ScheduleUtil.NormalizeSchedule(schedule, p);
        var before = UnifiedViolationChecker.Check(state, work);
        var bestRep = before;
        var applied = 0;

        // 適切回数(apt)目標: state.GroupShiftApt[群][シフト] の整数（空=なし）。
        int? AptTarget(int i, int k)
        {
            if (i < 0 || i >= state.StaffList.Count) return null;
            var g = state.StaffList[i].GroupIdx;
            if (g < 0 || g >= state.GroupShiftApt.Count) return null;
            var row = state.GroupShiftApt[g];
            if (k < 0 || k >= row.Count) return null;
            return KotlinInterop.ToIntOrNull(row[k].Trim());
        }

        int[][] Cnt() => ScheduleUtil.CountMatrix(p, work);
        var counts = Cnt();

        for (var j = 0; j < p.T; j++)
        {
            if (stop()) break;
            var free = Enumerable.Range(0, p.S).Where(i => !p.WishLocked(i, j)).ToList();
            if (free.Count < 2) continue;
            var slots = free.Select(i => work[i][j]).ToList(); // 当日の同一シフト多重集合（人数固定）
            var n = free.Count;
            var costM = new long[n][];
            for (var r = 0; r < n; r++)
            {
                var i = free[r];
                var row = new long[n];
                for (var c = 0; c < n; c++)
                {
                    var k = slots[c];
                    if (k < 0 || k >= p.K || !p.MayPlace(i, k))
                    {
                        row[c] = MinCostAssignment.Inf;
                    }
                    else
                    {
                        var x0 = counts[i][k] - (work[i][j] == k ? 1 : 0); // この日を除いた現状カウント
                        var x1 = x0 + 1; // k を割当てた後
                        var lo = p.RangeLo[i][k];
                        var hi = EffectiveHi(p, i, k);
                        // [ソフト研磨・候補生成の重み整合] proxy を真の目的関数(low=90/high=45/apt=1)へ整合。
                        //   採否は従来どおり keep-best(IsBetter)が担うため退化なし＝スコアリング不変。
                        long RangePen(int x) =>
                            (lo != int.MinValue ? 90L * Math.Max(0, lo - x) : 0L) + 45L * Math.Max(0, x - hi);
                        var cost = RangePen(x1) - RangePen(x0); // range の限界費用
                        var t = AptTarget(i, k);
                        if (t != null) cost += Math.Abs(x1 - t.Value) - Math.Abs(x0 - t.Value); // apt の限界費用
                        row[c] = cost;
                    }
                }
                costM[r] = row;
            }
            // [3.278.0] 全INF行(担当可否ゼロの職員等)は実行可能な完全割当が無い＝nullでその日をスキップ。
            var assign = MinCostAssignment.Solve(costM);
            if (assign == null) continue;
            var cand = work.Copy2D();
            var changed = false;
            for (var r = 0; r < free.Count; r++)
            {
                var i = free[r];
                var k = slots[assign[r]];
                if (cand[i][j] != k) { cand[i][j] = k; changed = true; }
            }
            if (!changed) continue;
            var rep = UnifiedViolationChecker.Check(state, cand);
            // [厳密ピン保護] 日ブロック内Hungarian再割当は複数職員の回数を同時に変えうるため、
            //   staffRange厳密ピン(lo==hi)を新たに崩す日案は不採用にする（keep-best/重みは不変）。
            if (IsBetter(rep, bestRep) && !pinBlocks.BlocksImproving(p, work, cand))
            {
                work = cand; bestRep = rep; counts = Cnt(); applied++;
            }
        }

        var logs = new List<MirrorLog>
        {
            new MirrorLog(tag: "DayAssign",
                message: $"日ごと厳密割当: total {before.Total}->{bestRep.Total} 採用{applied}日"),
        };
        return new DayAssignResult(work, before.Total, bestRep.Total, applied, logs, PinBlocks: pinBlocks);
    }

    /// <summary>
    /// [ソフト研磨・交互最適化(Alternating Optimization / 交代最適化)] 全変数を同時に解かず「1ブロックずつ
    /// 順に最適化して巡回する」座標降下法（block coordinate descent）をソフト制約研磨に導入する新アルゴリズム。
    /// ブロック＝各日(列): その日の (シフト人数=被覆) を固定したまま、希望未固定(wish&lt;0)の職員を
    /// 「個人別回数(range 90/45)・適切回数(apt 1)・<b>曜日平準化(weekly 1)</b>」の限界費用が最小になるよう
    /// <b>最小費用割当(Hungarian＝割当LP＝凸最適化)</b>で最適再配置し、日 j を 0..T-1 と巡回して
    /// 1スイープで1日も変化しなくなるまで（＝座標降下の不動点）反復する。
    ///
    /// 既存 <see cref="ApplyDayAssignmentPolish"/>（range/apt のみ・単発）を、①weekly を費用に含め
    /// ②反復収束（交互）まで一般化したもの。weekly を費用に入れる意味＝その日の「休スロット」を誰に
    /// 割り当てるかで各職員の曜日別勤務数が変わる（被覆は不変）。「その曜日に働き過ぎの職員へ休を、
    /// 少なすぎる職員へ勤務を」割り当てる候補を Hungarian が同日内で<b>同時最適</b>に生成し、曜日偏りを直す。
    /// 同日内の最適再配置＝rectangle（クロス日の2職員×2日）とは別種の被覆保存手＝相補的。
    /// 採否は実目的関数 IsBetter（hard→weighted→total, keep-best）＝退化なし。fair 等の他 soft は IsBetter
    /// が担保する（費用に無い族も採用判定で悪化しないことを保証）。
    /// </summary>
    public static DayAssignResult ApplyAlternatingSoftPolish(
        MagiState state, int[][] schedule, int maxSweeps = 4, Func<bool>? shouldStop = null)
    {
        var stop = shouldStop ?? (() => false);
        var pinBlocks = new PinBlockAttribution();
        var p = new Problem(state);
        var work = ScheduleUtil.NormalizeSchedule(schedule, p);
        var before = UnifiedViolationChecker.Check(state, work);
        var bestRep = before;
        var applied = 0;

        int? AptTarget(int i, int k)
        {
            if (i < 0 || i >= state.StaffList.Count) return null;
            var g = state.StaffList[i].GroupIdx;
            if (g < 0 || g >= state.GroupShiftApt.Count) return null;
            var row = state.GroupShiftApt[g];
            if (k < 0 || k >= row.Count) return null;
            return KotlinInterop.ToIntOrNull(row[k].Trim());
        }

        // [3.345.0] weekly の wd バケットは職員×シフト×曜日（休も1シフト＝特別扱いしない）。
        //   目標は WeeklyDevOfBucket が内部で round(そのシフトの回数/7) として持つ。被覆保存の再配置ごとに更新。
        int[][] WdOf(int i)
        {
            var wdArr = new int[p.K][];
            for (var k = 0; k < p.K; k++) wdArr[k] = new int[7];
            for (var j = 0; j < p.T; j++)
            {
                var k = work[i][j];
                if (k >= 0 && k < p.K) wdArr[k][(p.Dow0 + j) % 7]++;
            }
            return wdArr;
        }

        int[][][] BuildWd()
        {
            var result = new int[p.S][][];
            for (var i = 0; i < p.S; i++) result[i] = WdOf(i);
            return result;
        }

        var wd = BuildWd();
        int[][] Cnt() => ScheduleUtil.CountMatrix(p, work);
        var counts = Cnt();
        var sweep = 0;
        var lastSweep = 0;

        while (sweep < maxSweeps)
        {
            if (stop()) break;
            var changedInSweep = false;
            for (var j = 0; j < p.T; j++)
            {
                if (stop()) break;
                var free = Enumerable.Range(0, p.S).Where(i => !p.WishLocked(i, j)).ToList();
                if (free.Count < 2) continue;
                var slots = free.Select(i => work[i][j]).ToList();
                var n = free.Count;
                var wdj = (p.Dow0 + j) % 7;
                var costM = new long[n][];
                for (var r = 0; r < n; r++)
                {
                    var i = free[r];
                    var row = new long[n];
                    for (var c = 0; c < n; c++)
                    {
                        var k = slots[c];
                        if (k < 0 || k >= p.K || !p.MayPlace(i, k))
                        {
                            row[c] = MinCostAssignment.Inf;
                        }
                        else
                        {
                            var x0 = counts[i][k] - (work[i][j] == k ? 1 : 0);
                            var x1 = x0 + 1;
                            var lo = p.RangeLo[i][k];
                            var hi = EffectiveHi(p, i, k);
                            // range/apt は ApplyDayAssignmentPolish と同一の目的関数整合 proxy（90/45/1）。
                            long RangePen(int x) =>
                                (lo != int.MinValue ? 90L * Math.Max(0, lo - x) : 0L) + 45L * Math.Max(0, x - hi);
                            var cost = RangePen(x1) - RangePen(x0);
                            var t = AptTarget(i, k);
                            if (t != null) cost += Math.Abs(x1 - t.Value) - Math.Abs(x0 - t.Value);
                            // [3.345.0] weekly 限界費用: 当日を k にしたときの、職員 i の「シフト k」の曜日
                            //   バケットの L1 偏差変化（重み1）。当日の元シフトを失う項は行(i)ごとの定数＝
                            //   割当の argmin を変えないため省く（列ごとに効く項だけを費用に入れる）。
                            var b = wd[i][k];
                            var had = work[i][j] == k ? 1 : 0;
                            b[wdj] -= had; // 当日を除いた状態
                            var devBefore = ScheduleUtil.WeeklyDevOfBucket(b);
                            b[wdj] += 1;
                            var devAfter = ScheduleUtil.WeeklyDevOfBucket(b);
                            b[wdj] += had - 1; // 復元
                            cost += devAfter - devBefore;
                            row[c] = cost;
                        }
                    }
                    costM[r] = row;
                }
                // [3.278.0] 全INF行(担当可否ゼロの職員等)は実行可能な完全割当が無い＝nullでその日をスキップ。
                var assign = MinCostAssignment.Solve(costM);
                if (assign == null) continue;
                var cand = work.Copy2D();
                var changed = false;
                for (var r = 0; r < free.Count; r++)
                {
                    var i = free[r];
                    var k = slots[assign[r]];
                    if (cand[i][j] != k) { cand[i][j] = k; changed = true; }
                }
                if (!changed) continue;
                var rep = UnifiedViolationChecker.Check(state, cand);
                // [厳密ピン保護] 日ブロック内Hungarian再割当は複数職員の回数を同時に変えうるため、
                //   staffRange厳密ピン(lo==hi)を新たに崩す日案は不採用にする（keep-best/重みは不変）。
                if (IsBetter(rep, bestRep) && !pinBlocks.BlocksImproving(p, work, cand))
                {
                    work = cand; bestRep = rep; counts = Cnt();
                    wd = BuildWd();
                    applied++; changedInSweep = true;
                }
            }
            sweep++; lastSweep = sweep;
            if (!changedInSweep) break; // 座標降下の不動点＝この巡回で1日も改善しない
        }

        var logs = new List<MirrorLog>
        {
            new MirrorLog(tag: "AltOptPolish",
                message: $"交互最適化(日ブロック・weekly込み割当): total {before.Total}->{bestRep.Total} " +
                    $"採用{applied}日 ({lastSweep}スイープ)"),
        };
        return new DayAssignResult(work, before.Total, bestRep.Total, applied, logs, PinBlocks: pinBlocks);
    }
}
