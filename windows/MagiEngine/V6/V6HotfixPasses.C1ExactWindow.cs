using System.Text;
using MagiEngine.Model;

namespace MagiEngine.V6;

public static partial class V6HotfixPasses
{
    /// <summary>
    /// [フェーズ6, ピース27/Kotlin A1+A2+A3, 3.273.0] C1厳密窓修復パス。<see cref="C1RepairAnalysis"/>
    /// の窓スコープ厳密探索（coverage保存 permutation の分枝限定）で、局所/ビーム系が届かない
    /// 多日多職員連動手を拾う。
    ///  - A1 解析駆動ディスパッチ: 「exhaustive で min==baseline」と証明されたスパンは、その
    ///    (焦点職員, シフト, スパン内容ハッシュ) を memo し、内容が変わらない限り二度と厳密探索しない
    ///    （死に候補の刈込）。
    ///  - 採否は必ず本物の checker + isBetter + ExactPinRegression（keep-best＝退化不能）。厳密探索が
    ///    返す patch はあくまで候補（node予算超過時は best-effort＝多様化として安全）。
    /// </summary>
    public static CyclicSwapResult ApplyC1ExactWindowRepair(
        MagiState state, int[][] schedule, Config? cfg = null, Func<bool>? shouldStop = null)
    {
        var stop = shouldStop ?? (() => false);
        cfg ??= new Config();
        // [3.326.0] 回数固定(lo==hi)だけが却下した候補試行を対象別に数える（緩和対象の提示用）。
        var pinBlocks = new PinBlockAttribution();
        var p = new Problem(state);
        var work = ScheduleUtil.NormalizeSchedule(schedule, p);
        var before = UnifiedViolationChecker.Check(state, work);
        var bestRep = before;
        var applied = 0;
        var solved = 0;
        var provenWalls = 0;
        if (p.Cons1.Count == 0 || before.Breakdown.GetValueOrDefault("c1", 0) == 0)
        {
            return new CyclicSwapResult(work, before.Total, before.Total, 0,
                new List<MirrorLog> { new MirrorLog(tag: "C1ExactRepair", message: "c1対象なし=スキップ") });
        }
        // [A1] 証明済み「解消不能スパン」のmemo（キー=焦点職員,シフト,スパン内容ハッシュ）。
        var deadSpans = new HashSet<string>();
        var rejectCulprits = new RejectCulpritStats();
        string SpanKey(int staff, int shift, List<int> days)
        {
            var sb = new StringBuilder().Append(staff).Append('|').Append(shift).Append('|');
            foreach (var d in days) for (var i = 0; i < p.S; i++) sb.Append(work[i][d]).Append(',');
            return sb.ToString();
        }
        // 焦点ごとに1回だけ厳密探索する。
        // [3.314.0] キーを (職員, シフト) → **(職員, シフト, スパン開始)** へ。同一職員・同一シフトの
        //   複数の独立した不足窓が、離れていても同一対象とみなされ探索されないままスキップされる旧欠陥を
        //   避ける。同一スパンの重複は下の deadSpans（スパン内容ハッシュ）が引き続き弾く。
        var seenFocus = new HashSet<string>();
        foreach (var v in C1RepairAnalysis.Analyze(p, work))
        {
            if (stop()) break;
            var span = Math.Min(cfg.MaxWindowDays, p.T);
            var startD = Math.Max(Math.Min(v.Start, p.T - span), 0);
            if (!seenFocus.Add($"{v.Staff}|{v.Shift}|{startD}")) continue;
            var days = Enumerable.Range(startD, span).ToList();
            var key = SpanKey(v.Staff, v.Shift, days);
            if (deadSpans.Contains(key)) continue;
            var res = C1RepairAnalysis.SolveWindow(p, work, v, cfg);
            solved++;
            if (res.Patch == null)
            {
                // 改善候補なし。exhaustive なら「coverage保存では解消不能」と証明済み＝memo。
                if (res.Exhaustive) { deadSpans.Add(key); provenWalls++; }
                continue;
            }
            var workBefore = work.Copy2D();
            foreach (var op in res.Patch) work[op[0]][op[1]] = op[2];
            var rep = UnifiedViolationChecker.Check(state, work);
            // [3.321.0] このパスだけ却下理由をまったく残しておらず、ログは applied==0 のとき
            //   一律「頭打ち=改善手なし」としか言えなかった。他の研磨パスと同じ RejectCulpritStats で分類する。
            var pinBad = V6SearchOperators.ExactPinRegression(p, workBefore, work);
            if (pinBad && IsBetter(rep, bestRep)) pinBlocks.Record(p, workBefore, work);
            if (IsBetter(rep, bestRep) && !pinBad)
            {
                bestRep = rep;
                applied++;
            }
            else
            {
                rejectCulprits.Record(rep, bestRep, pinBad);
                for (var mi = 0; mi < work.Length; mi++) work[mi] = workBefore[mi];
            }
        }
        var c1b = before.Breakdown.GetValueOrDefault("c1", 0);
        var c1a = bestRep.Breakdown.GetValueOrDefault("c1", 0);
        var logs = new List<MirrorLog>
        {
            new MirrorLog(tag: "C1ExactRepair",
                message: $"期間要件(c1)厳密窓修復: c1 {c1b}->{c1a} / total {before.Total}->{bestRep.Total} " +
                    $"HARD {before.Hard}->{bestRep.Hard} 採用{applied}回 探索{solved}回 証明済み壁{provenWalls}件" +
                    rejectCulprits.Summary() +
                    // [3.321.0] 旧: applied==0 を一律「改善手なし」としていたが、patch が出て却下された場合と
                    //   patch がそもそも出ない場合を区別できなかった。前者は上の内訳が語るのでここは後者だけ。
                    (applied == 0 && c1b > 0 && rejectCulprits.Rejected == 0 ? " [頭打ち=候補が出ない]" : "")),
        };
        return new CyclicSwapResult(work, before.Total, bestRep.Total, applied, logs,
            ObservedPinBlockedAttempts: pinBlocks.Attempts, PinBlocks: pinBlocks);
    }
}
