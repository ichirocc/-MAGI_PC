using MagiEngine.Model;

namespace MagiEngine.V6;

public static partial class V6HotfixPasses
{
    /// <summary>
    /// [フェーズ6, ピース26] Kotlin原本 <c>HF80Result</c>（top-level data class, 17-27行）の忠実な移植。
    /// 戦略的振動（強い摂動→局所改善→採否）による停滞脱出パス <see cref="ApplyHF80StrategicOscillation"/>
    /// の戻り値。他の HF6x パスと異なり Hard/Score を Before/After で分けて持つ（振動サイクルごとの
    /// 改善追跡のため。<c>Score</c> は生の重み付き合計であって整数の <c>Total</c> ではない）。
    /// </summary>
    public sealed record HF80Result(
        int[][] NewSchedule,
        int BeforeHard,
        int AfterHard,
        double BeforeScore,
        double AfterScore,
        int Cycles,
        bool Applied,
        string Reason,
        IReadOnlyList<MirrorLog> Logs);

    /// <summary>
    /// [フェーズ6, ピース26/Kotlin 3.451.0] Kotlin原本 <c>localBestImprovement</c> の忠実な移植。
    /// <see cref="ApplyHF80StrategicOscillation"/> 専用の内側探索ヘルパー——強摂動で崩した盤面を、
    /// ランダム単一セル変更の局所改善（改善したら採用・そうでなければ据え置き）で立て直す。
    ///
    /// [大量アロケート対策・二層構成] <c>UnifiedViolationChecker.Check</c> は Map/List フィールドを
    /// 多数持つ重い <c>ViolationReport</c> を毎回新規アロケートする（Kotlin原本のコメントによれば、
    /// 実機ログで1パスにつき1,000回超呼ばれ既定ヒープを使い切った OOM を確認済み）。内側の当落判定は
    /// ここでは <see cref="Evaluator.FullEval"/>（Mapを一切作らない packed <c>long</c>）に切り替え、
    /// <c>V6NativeOptimizer</c> の SA(native) 経路と同じ二層構成にする——最終採否は必ず呼出元
    /// <see cref="ApplyHF80StrategicOscillation"/> が <c>UnifiedViolationChecker.Check</c> +
    /// <see cref="IsBetter"/> で再評価する（外側ゲートは1サイクルにつき1回のみ・cycle≤3回）ため、
    /// 内側の当落基準を変えても最終的に <see cref="HF80Result"/> へ採用される盤面の品質は退化しない
    /// （このコードベース全体の「候補生成は近似でよい・最終採否は必ずchecker+isBetter」契約と同型）。
    /// </summary>
    private static int[][] LocalBestImprovement(
        MagiState state, int[][] schedule, int tries, JavaRandom rng, Func<bool>? shouldStop = null)
    {
        var stop = shouldStop ?? (() => false);
        var p = new Problem(state);
        var ev = new Evaluator(p);
        var best = schedule.Copy2D();
        var bestScore = ev.FullEval(best);
        var t = 0;
        var maxTry = Math.Max(0, tries);
        while (t < maxTry)
        {
            if (stop()) break;
            if (p.S > 0 && p.T > 0)
            {
                var cand = best.Copy2D();
                var i = rng.NextInt(p.S);
                var j = rng.NextInt(p.T);
                if (!p.WishLocked(i, j))
                {
                    var allowed = p.AllowedShiftsForStaff(i);
                    if (allowed.Length > 0)
                    {
                        cand[i][j] = allowed[rng.NextInt(allowed.Length)];
                        var score = ev.FullEval(cand);
                        if (score < bestScore)
                        {
                            best = cand;
                            bestScore = score;
                        }
                    }
                }
            }
            t++;
        }
        return best;
    }

    /// <summary>
    /// [フェーズ6, ピース26] Kotlin原本 <c>applyHF80StrategicOscillation</c>（<c>V6HotfixPasses.kt</c>
    /// 由来）の忠実な移植。戦略的振動＝強い摂動（サイクルが進むほど強度を増す＝<c>0.03 + cycle*0.02</c>）で
    /// 盤面を意図的に崩し、<see cref="LocalBestImprovement"/> で局所改善してから、真の目的関数(checker)で
    /// 直前の <c>best</c> と比較し採用可否を決める停滞脱出パス。keep-best
    /// （<see cref="IsBetter"/> を満たさなければそのサイクルの結果を捨て、次サイクルへ直前の
    /// <c>best</c> をそのまま持ち越す＝退化しない）。
    ///
    /// [C#化の注記] Kotlinの既定引数 <c>seed: Long = System.nanoTime()</c> は非定数式のため
    /// <c>long? seed = null</c> ＋ 本体内 null合体（<c>System.Diagnostics.Stopwatch.GetTimestamp()</c>、
    /// このコードベース全体で確立済みの <c>System.nanoTime()</c> 対応）へ移した。ログ本文の
    /// <c>applied</c>（bool）は Kotlin の <c>Boolean.toString()</c>（小文字 "true"/"false"）に合わせ、
    /// C# 既定の "True"/"False" ではなく明示的に小文字化して埋め込む。
    /// </summary>
    public static HF80Result ApplyHF80StrategicOscillation(
        MagiState state, int[][] schedule, int maxCycles = 3, long? seed = null, Func<bool>? shouldStop = null)
    {
        var stop = shouldStop ?? (() => false);
        var p = new Problem(state);
        var rng = new JavaRandom(seed ?? System.Diagnostics.Stopwatch.GetTimestamp());
        var before = UnifiedViolationChecker.Check(state, schedule);
        var best = ScheduleUtil.NormalizeSchedule(schedule, p);
        var bestReport = before;
        var applied = false;
        var usedCycles = 0;
        var cycleMax = Math.Max(0, maxCycles);
        var cycle = 0;
        while (cycle < cycleMax)
        {
            if (stop()) break;
            var cand = best.Copy2D();
            var strength = Math.Max(1, (int)(p.S * p.T * (0.03 + cycle * 0.02)));
            var t = 0;
            while (t < strength)
            {
                if (p.S > 0 && p.T > 0)
                {
                    var i = rng.NextInt(p.S);
                    var j = rng.NextInt(p.T);
                    if (!p.WishLocked(i, j))
                    {
                        var allowed = p.AllowedShiftsForStaff(i);
                        if (allowed.Length > 0) cand[i][j] = allowed[rng.NextInt(allowed.Length)];
                    }
                }
                t++;
            }
            var polished = LocalBestImprovement(state, cand, 250 + cycle * 120, rng, stop);
            var rep = UnifiedViolationChecker.Check(state, polished);
            usedCycles = cycle + 1;
            if (IsBetter(rep, bestReport))
            {
                best = polished;
                bestReport = rep;
                applied = true;
            }
            cycle++;
        }
        var reason = applied ? "strategic oscillation accepted" : "no improving oscillation";
        var logs = new List<MirrorLog>
        {
            new MirrorLog(tag: "HF80",
                message: $"SO applied={(applied ? "true" : "false")} HARD {before.Hard}->{bestReport.Hard} " +
                    $"score {(long)before.WeightedScore}->{(long)bestReport.WeightedScore} cycles={usedCycles}"),
        };
        return new HF80Result(best, before.Hard, bestReport.Hard, before.WeightedScore, bestReport.WeightedScore, usedCycles, applied, reason, logs);
    }
}
