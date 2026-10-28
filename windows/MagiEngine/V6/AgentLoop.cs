using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// 代行役（AX、backlog #13(f)）の中核: 勤務表の全体から 1 手の変更案を探し、<see cref="FixApplyGate"/> に合格した先頭の案を当てて
/// 探し直す、を案が尽きるまで繰り返す。案が尽きても必須違反が残れば、広域の研磨（widen＝「自動で整える」）を 1 回だけ試し、
/// その差分も同じゲートに通して当てたら探し直す（backlog #13(g)）。勤務の変更だけを扱い、希望・設定は変えない（それは人の判断待ちに回す）。
/// 入力の盤面は変えない。各手は正式なチェッカーで辞書式に改善し必須違反を新たに生まない＝結果は入力より悪くならない。
/// Kotlin <c>AgentLoop.kt</c> の移植。
/// </summary>
public static class AgentLoop
{
    public sealed record Step(string Label, IReadOnlyList<FixCell> Ops, int HardBefore, int HardAfter, int TotalBefore, int TotalAfter);

    public enum Stop { NoCandidate, RoundLimit, TimeLimit, Cancelled }

    public sealed class Result(int[][] schedule, IReadOnlyList<Step> steps, Stop stop, ViolationReport before, ViolationReport after)
    {
        public int[][] Schedule { get; } = schedule;
        public IReadOnlyList<Step> Steps { get; } = steps;
        public Stop StopReason { get; } = stop;
        public ViolationReport Before { get; } = before;
        public ViolationReport After { get; } = after;
    }

    public const int MaxRounds = 12;
    public const long SearchMs = 6_000L;
    public const int MaxWiden = 1;
    public const long WidenMinLeftMs = 5_000L;
    public const string WidenLabel = "自動で整える";

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>[C#移植上の注記] Kotlin の既定の探索は <c>ejectionChain = true</c> を渡すが、C# の <see cref="FixSuggester.Suggest"/> には
    /// その引数がまだ無い（玉突きの手を含まない探索になる）。</summary>
    public static async Task<Result> Run(
        MagiState state,
        int[][] schedule,
        long deadlineMs,
        int maxRounds = MaxRounds,
        long searchMs = SearchMs,
        Func<long>? now = null,
        Func<bool>? active = null,
        Func<MagiState, int[][], long, IReadOnlyList<FixSuggestion>>? suggest = null,
        Func<MagiState, int[][], long, Task<int[][]>>? widen = null)
    {
        now ??= NowMs;
        active ??= () => true;
        suggest ??= (st, s, ms) => FixSuggester.Suggest(st, s, maxResults: 8, deadlineMs: ms);
        var p = new Problem(state);
        var cur = schedule.Copy2D();
        var first = UnifiedViolationChecker.Check(state, cur);
        var report = first;
        var steps = new List<Step>();
        var stop = Stop.RoundLimit;
        var widened = 0;
        for (var round = 0; round < maxRounds; round++)
        {
            if (!active()) { stop = Stop.Cancelled; break; }
            var left = deadlineMs - now();
            if (left <= 0L) { stop = Stop.TimeLimit; break; }
            var cands = suggest(state, cur, Math.Min(searchMs, left));
            (FixSuggestion C, FixApplyGate.Outcome Ok)? hit = null;
            foreach (var c in cands)
            {
                var o = FixApplyGate.Apply(state, cur, c.Ops, p, report);
                if (o.Applied) { hit = (c, o); break; }
            }
            if (hit is { } h)
            {
                steps.Add(new Step(h.C.Label, h.C.Ops, h.Ok.Before.Hard, h.Ok.After!.Hard, h.Ok.Before.Total, h.Ok.After.Total));
                cur = h.Ok.Schedule!;
                report = h.Ok.After;
                continue;
            }
            var leftNow = deadlineMs - now();
            if (widen is null || widened >= MaxWiden || report.Hard <= 0 || leftNow < WidenMinLeftMs) { stop = Stop.NoCandidate; break; }
            widened++;
            // 広域の研磨が例外で終わっても、それまでに当てた手は残す（輪全体を捨てない）。
            int[][] w;
            try { w = await widen(state, cur.Copy2D(), leftNow); }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { stop = Stop.NoCandidate; break; }
            var ops = new List<FixCell>();
            for (var i = 0; i < cur.Length; i++)
                for (var j = 0; j < cur[i].Length; j++)
                    if (i < w.Length && w[i] is { } wr && j < wr.Length && wr[j] != cur[i][j]) ops.Add(new FixCell(i, j, wr[j]));
            var ok = FixApplyGate.Apply(state, cur, ops, p, report);
            if (!ok.Applied) { stop = Stop.NoCandidate; break; }
            steps.Add(new Step($"{WidenLabel}（{ops.Count}セル）", ops, ok.Before.Hard, ok.After!.Hard, ok.Before.Total, ok.After.Total));
            cur = ok.Schedule!;
            report = ok.After;
        }
        return new Result(cur, steps, stop, first, report);
    }
}
