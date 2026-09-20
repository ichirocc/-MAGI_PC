using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// Faithful port of Kotlin's <c>C2Polish</c> object.
///
/// [3.511.3/測定中, Kotlin原本] 個人合計(c2, SOFT, 重み4)専用の研磨パス。<see cref="V6HotfixPasses"/> から
/// 抽出せず新規（backlog #12(b)）。
///
/// c2 は「シフト単位のグローバル下限」（<see cref="Problem.Cons2"/>、<c>count[i][shiftIdx] &gt;= c.Count</c> を
/// CanDo 全職員に一律要求）で、判定は不足量でなく<b>違反有無の二値フラグ</b>
/// （Evaluator/MirrorCore/DeltaEvaluator すべて <c>if (count &lt; c.Count) soft += 1</c>）。
/// Fair/Apt と違い L1 偏差＝1 セル移動ごとに目的関数が単調に動くため 1 手ずつの keep-best 判定と相性が良いが、
/// c2 は不足量が 2 以上の職員に「1 日ずつ変換→毎回判定」を適用すると、しきい値に届くまでの中間手が違反件数を
/// 1 件も減らさず（他制約への副作用が無ければ weightedScore/total が同点）<see cref="UnifiedViolationChecker.BetterReport"/>
/// （同点は却下）に毎回却下される＝実質ノーオペになる。このパスは<b>不足分の日をまとめて集め、一括で適用してから
/// 1 回だけ判定</b>する（Fair の相互交換の「複数セル同時適用→1 回判定」を N セルへ一般化したもの）。
///
/// 自己変換のみ（本人の他シフトの日を shiftIdx へ振り替える）を対象にし、他職員から玉突きで借りる拡張は対象外
/// （まず最小差分で測る＝過剰実装を避ける。要るかは tools/loop の結果を見てから）。
///
/// [測定, Kotlin原本 3.582.0] backlog#26のtools/loopベンチ（<c>c2PolishReactivate</c>、5seed×46ケース）は
/// ゲート不合格＝既定OFF維持が確定している。C#側もこの結論を踏襲し、パス本体は Kotlin と同値の忠実な移植に
/// とどめる（探索動学は変えない）。
/// </summary>
internal static class C2Polish
{
    public static V6HotfixPasses.CyclicSwapResult ApplyC2Polish(
        MagiState state, int[][] schedule, int maxPasses = 3,
        Func<bool>? shouldStop = null, bool quantitativeRangeEval = false)
    {
        var stop = shouldStop ?? (() => false);
        var pinBlocks = new PinBlockAttribution();
        var p = new Problem(state, quantitativeRangeEval);
        var work = ScheduleUtil.NormalizeSchedule(schedule, p);
        var before = UnifiedViolationChecker.Check(state, work, quantitativeRangeEval);
        var bestRep = before;
        var applied = 0;

        // [3.270.0 と同型, Kotlin原本] WishLocked は CanDo ガード込みで「動かせるか」を正しく判定する。
        bool Movable(int i, int j) => !p.WishLocked(i, j);

        // [厳密ピン保護, Kotlin原本] 職員 i の shiftIdx への不足 deficit 分を、被覆非悪化・禁止連続なしの日から
        //   まとめて集め、集まった分だけ一括適用して 1 回だけ正式判定する。届かなければ全く触らない
        //   （中途半端な一部適用は c2 の二値性のため無意味＝必ず全部埋まる分だけを試す）。
        bool TryFillDeficit(int i, int shiftIdx, int deficit)
        {
            if (!p.MayPlace(i, shiftIdx)) return false;
            var days = new List<int>();
            for (var j = 0; j < p.T; j++)
            {
                if (days.Count >= deficit) break;
                if (stop()) return false;
                var fromK = work[i][j];
                if (fromK == shiftIdx || !Movable(i, j)) continue;
                if (p.MakesForbiddenRun(work, i, j, shiftIdx)) continue;
                int cntFrom = 0, cntTo = 0;
                for (var s = 0; s < p.S; s++) { if (work[s][j] == fromK) cntFrom++; if (work[s][j] == shiftIdx) cntTo++; }
                if (p.CovUCell(fromK, j, cntFrom - 1) > p.CovUCell(fromK, j, cntFrom)) continue;
                if (p.CovUCell(shiftIdx, j, cntTo + 1) > p.CovUCell(shiftIdx, j, cntTo)) continue;
                days.Add(j);
            }
            if (days.Count < deficit) return false;
            var workBefore = work.Copy2D();
            foreach (var j in days) work[i][j] = shiftIdx;
            var rep = UnifiedViolationChecker.Check(state, work, quantitativeRangeEval);
            var pinBad = V6SearchOperators.ExactPinRegression(p, workBefore, work);
            if (pinBad && UnifiedViolationChecker.BetterReport(rep, bestRep)) pinBlocks.Record(p, workBefore, work);
            if (UnifiedViolationChecker.BetterReport(rep, bestRep) && !pinBad) { bestRep = rep; applied++; return true; }
            foreach (var j in days) work[i][j] = workBefore[i][j];
            return false;
        }

        var pass = 0;
        while (pass < maxPasses)
        {
            if (stop()) break;
            var improved = false;
            var counts = ScheduleUtil.CountMatrix(p, work);
            foreach (var c in p.Cons2)
            {
                if (stop()) break;
                for (var i = 0; i < p.S; i++)
                {
                    if (stop()) break;
                    if (!p.CanDo(i, c.ShiftIdx)) continue;
                    var deficit = c.Count - counts[i][c.ShiftIdx];
                    if (deficit <= 0) continue;
                    if (TryFillDeficit(i, c.ShiftIdx, deficit)) improved = true;
                }
            }
            pass++;
            if (!improved) break;
        }

        var logs = new List<MirrorLog> { new(tag: "C2Polish", message: $"個人合計(c2)研磨: total {before.Total}->{bestRep.Total} 採用{applied}回") };
        return new V6HotfixPasses.CyclicSwapResult(work, before.Total, bestRep.Total, applied, logs, PinBlocks: pinBlocks);
    }
}
