using MagiEngine.Model;
using Range = MagiEngine.Model.Range;

namespace MagiEngine.V6;

/// <summary>
/// [S6] 「設定を緩めたら」試算（Android <c>RelaxTrial.kt</c>、<c>docs/s6_relax_trial.md</c> §3・§4）。
/// 残る必須違反 1 件（起点）について、個人の上限 0（<c>MayPlace</c> の除外）のどれが壁かを
/// 「全部外して解けるか → 使った壁だけ残す → 1 つずつ外して要らない壁を落とす」で見つけ、
/// 残った組を実際に緩めた state で同じ探索を回して、設定を残したままの対照との差を出す。
/// 入力は書かない。結果は数値と手順（盤面は持たない＝仮盤禁止）。設定値を自動で変えない（HF77）。
/// </summary>
public static class RelaxTrial
{
    /// <summary>個人の上限を <paramref name="NewHi"/> へ上げる 1 件（いまの上限は 0）。下限は触らない。</summary>
    public sealed record Relax(int Staff, int Shift, int NewHi);

    /// <summary>試算の手順 1 セル分（盤面は持たない）。</summary>
    public sealed record Move(int Staff, int Day, int From, int To);

    public abstract record Outcome;

    /// <summary>
    /// Staff/Day＝起点、WindowFirst..WindowLast＝手順を言葉で並べる日の範囲。Prerequisite＝手で置いてある上限 0 の勤務に合わせる分、
    /// Relaxes＝それとは別に起点の違反を解くのに要る上限 0。H0＝いま、Rk＝設定そのまま、RkH＝前提だけ、Rr＝両方緩めた見込み。
    /// </summary>
    public sealed record Result(
        int Staff, int Day, int WindowFirst, int WindowLast,
        IReadOnlyList<Relax> Prerequisite, IReadOnlyList<Relax> Relaxes,
        int H0, int Rk, int RkH, int Rr, IReadOnlyList<Move> Moves) : Outcome
    {
        public int PKeep => Math.Min(H0, Rk);
        public int PPrereq => Math.Min(H0, RkH);
        public int PRelax => Math.Min(H0, Rr);
        /// <summary>設定をどれも変えずにもう一度つくる場合と比べて減る見込み。</summary>
        public int Att => PKeep - PRelax;
        /// <summary>そのうち Relaxes（手置きに合わせる分を除く）に帰属する分。</summary>
        public int AttWalls => PPrereq - PRelax;

        public bool Equals(Result? o) => o is not null && Staff == o.Staff && Day == o.Day && WindowFirst == o.WindowFirst
            && WindowLast == o.WindowLast && Prerequisite.SequenceEqual(o.Prerequisite) && Relaxes.SequenceEqual(o.Relaxes)
            && H0 == o.H0 && Rk == o.Rk && RkH == o.RkH && Rr == o.Rr && Moves.SequenceEqual(o.Moves);
        public override int GetHashCode() => HashCode.Combine(Staff, Day, H0, Rk, RkH, Rr, Relaxes.Count, Moves.Count);
    }

    /// <summary>上限 0 を全部外しても、起点の違反を解く手が見つからなかった（0 件の証拠ではない）。</summary>
    public sealed record NoWallOutcome : Outcome;
    public static readonly Outcome NoWall = new NoWallOutcome();
    public sealed record Unavailable(string Reason) : Outcome;
    public sealed record StoppedOutcome : Outcome;
    public static readonly Outcome Stopped = new StoppedOutcome();

    private static readonly HashSet<string> HardCell = new() { "vio-c3n", "vio-c3w", "vio-pref", "vio-groupViol" };

    /// <summary>緩める候補の母集団: 担当できて、休み以外で、個人の上限が 0 の (職員, シフト)。</summary>
    public static List<(int Staff, int Shift)> UpperZeroWalls(MagiState state)
    {
        var p = ScheduleUtil.CachedProblem(state);
        var o = new List<(int, int)>();
        for (var i = 0; i < p.S; i++)
            for (var k = 0; k < p.K; k++)
                if (k != p.RestIdx && p.CanDo(i, k) && p.RangeHi[i][k] == 0) o.Add((i, k));
        return o;
    }

    /// <summary>勤務表に手で置いてある上限 0 の勤務（希望で固定したセルは除く）。本実行の入口の clear がこれを外す。</summary>
    public static List<Relax> HandPlaced(MagiState state, int[][] schedule) =>
        UsedWalls(ScheduleUtil.CachedProblem(state), UpperZeroWalls(state), schedule);

    /// <summary>relaxes を当てた state（確定操作と同じ関数）。上限だけを書き換え、下限の欄はそのまま。</summary>
    public static MagiState Apply(MagiState state, IReadOnlyList<Relax> relaxes)
    {
        if (relaxes.Count == 0) return state;
        var m = state.StaffRange.ToDictionary(kv => kv.Key, kv => kv.Value);
        foreach (var r in relaxes)
        {
            var key = $"{r.Staff},{r.Shift}";
            var cur = m.TryGetValue(key, out var c) ? c : new Range("", "");
            m[key] = new Range(cur.Lo, r.NewHi.ToString());
        }
        return state with { StaffRange = m };
    }

    /// <summary>起点 (staff, day) の必須違反について、壁になっている上限 0 の組を探し、その組で試算する。起点が必須違反でなければ null。</summary>
    public static Outcome? Discover(MagiState state, int[][] schedule, int staff, int day, int maxRelaxes = 3, Func<bool>? shouldStop = null)
    {
        var stop = shouldStop ?? (() => false);
        if (UnavailableReason(state, schedule) is { } why) return new Unavailable(why);
        var rep0 = UnifiedViolationChecker.Check(state, schedule.Copy2D());
        if (AnchorWindow(state, rep0, staff, day) is not { } window) return null;
        var walls = UpperZeroWalls(state);
        if (walls.Count == 0) return NoWall;
        var p0 = ScheduleUtil.CachedProblem(state);
        var pre = UsedWalls(p0, walls, schedule);
        if (Search(state, schedule, window, stop) is not { } control) return UnavailableOrStopped(stop);
        var stH = Apply(state, pre);
        Found controlH;
        if (pre.Count == 0) controlH = control;
        else if (Search(stH, schedule, window, stop) is { } ch) controlH = ch;
        else return UnavailableOrStopped(stop);
        // 上限 0 を全部外した state（上限の欄を空＝未設定）で解けるか。解けた盤面が使った上限 0 だけを候補に残す。
        var wallKeys = walls.Select(w => $"{w.Staff},{w.Shift}").ToHashSet();
        var oracle = state with { StaffRange = state.StaffRange.ToDictionary(kv => kv.Key, kv => wallKeys.Contains(kv.Key) ? new Range(kv.Value.Lo, "") : kv.Value) };
        if (Search(oracle, schedule, window, stop) is not { } best) return UnavailableOrStopped(stop);
        var target = Math.Min(rep0.Hard, best.Hard);
        if (target >= Math.Min(rep0.Hard, controlH.Hard) || HardAt(oracle, best.Board, staff, day)) return NoWall;
        var preKeys = pre.Select(r => (r.Staff, r.Shift)).ToHashSet();
        var set = UsedWalls(p0, walls, best.Board).Where(r => !preKeys.Contains((r.Staff, r.Shift))).ToList();
        // 1 つずつ外して、同じだけ減るなら要らない壁（順序は職員→シフトで固定＝決定的）。
        foreach (var r in set.ToList())
        {
            if (stop()) return Stopped;
            var without = set.Where(x => x != r).ToList();
            if (Search(Apply(stH, without), schedule, window, stop) is not { } s) return UnavailableOrStopped(stop);
            if (Math.Min(rep0.Hard, s.Hard) <= target && !HardAt(stH, s.Board, staff, day)) set = without;
        }
        if (set.Count > maxRelaxes) return NoWall;
        if (Search(Apply(stH, set), schedule, window, stop) is not { } rr) return UnavailableOrStopped(stop);
        if (stop()) return Stopped;
        var moves = rr.Hard < rep0.Hard ? Diff(schedule, rr.Board) : new List<Move>();
        return new Result(staff, day, window.First, window.Last, pre, set, rep0.Hard, control.Hard, controlH.Hard, rr.Hard, moves);
    }

    /// <summary>背景探索の起点（§2.2・§8）: 必須違反セル（希望どうしの衝突に入るセルを除く）を職員→日の順に、連続した必須セルは先頭の 1 つだけ、最大 max 件。</summary>
    public static List<(int Staff, int Day)> Anchors(MagiState state, int[][] schedule, int max = 3)
    {
        var rep = UnifiedViolationChecker.Check(state, schedule.Copy2D());
        var self = V6SanityPort.WishSelfConflicts(state).SelectMany(g => g.WishKeys).ToHashSet();
        var p = ScheduleUtil.CachedProblem(state);
        var o = new List<(int, int)>();
        for (var i = 0; i < p.S; i++)
        {
            bool Ok(int d) => rep.CellFamilies.TryGetValue($"{i},{d}", out var f) && f.Any(HardCell.Contains) && !self.Contains($"{i},{d}");
            var j = 0;
            while (j < p.T && o.Count < max)
            {
                if (!Ok(j)) { j++; continue; }
                o.Add((i, j));
                while (j < p.T && Ok(j)) j++;
            }
        }
        return o;
    }

    /// <summary>Anchors を順に試算し、必須違反が減る見込み（Att &gt; 0）の最初の組。どれも無ければ NoWall。</summary>
    public static Outcome FirstWall(MagiState state, int[][] schedule, int maxAnchors = 3, Func<bool>? shouldStop = null)
    {
        var stop = shouldStop ?? (() => false);
        if (UnavailableReason(state, schedule) is { } why) return new Unavailable(why);
        foreach (var (i, j) in Anchors(state, schedule, maxAnchors))
        {
            if (stop()) return Stopped;
            switch (Discover(state, schedule, i, j, shouldStop: stop))
            {
                case Result r when r.Att > 0 && r.Relaxes.Count > 0: return r;
                case StoppedOutcome: return Stopped;
                case Unavailable u: return u;
            }
        }
        return stop() ? Stopped : NoWall;
    }

    /// <summary>確定の盤面（§6 の 3）: 試算時の盤面に手順を当てる。どれかのセルが手順の From と違うか、手動固定（<paramref name="pinned"/>、#41）なら null。入力は書かない。</summary>
    public static int[][]? ApplyMoves(int[][] board, IReadOnlyList<Move> moves, Func<int, int, bool> pinned)
    {
        var nb = board.Copy2D();
        foreach (var m in moves)
        {
            if (m.Staff < 0 || m.Staff >= nb.Length || m.Day < 0 || m.Day >= nb[m.Staff].Length || nb[m.Staff][m.Day] != m.From || pinned(m.Staff, m.Day)) return null;
            nb[m.Staff][m.Day] = m.To;
        }
        return nb;
    }

    private static bool HardAt(MagiState state, int[][] board, int staff, int day) =>
        UnifiedViolationChecker.Check(state, board.Copy2D()).CellFamilies.TryGetValue($"{staff},{day}", out var f) && f.Any(HardCell.Contains);

    /// <summary>起点の窓: 起点の職員の、起点の日を含む連続した必須違反セルの日の範囲 ±1。起点が必須違反でなければ null。</summary>
    internal static (int First, int Last)? AnchorWindow(MagiState state, ViolationReport rep, int staff, int day)
    {
        var p = ScheduleUtil.CachedProblem(state);
        bool IsHard(int j) => rep.CellFamilies.TryGetValue($"{staff},{j}", out var f) && f.Any(HardCell.Contains);
        if (staff < 0 || staff >= p.S || day < 0 || day >= p.T || !IsHard(day)) return null;
        int a = day, b = day;
        while (a - 1 >= 0 && IsHard(a - 1)) a--;
        while (b + 1 < p.T && IsHard(b + 1)) b++;
        return (Math.Max(0, a - 1), Math.Min(p.T - 1, b + 1));
    }

    internal sealed record Found(int Hard, int[][] Board);

    /// <summary>探索（決定的・止められる）: clear → VCR と、窓の日ごとの同日 2 人交換で必須を増やさないもの 1 つを先に当ててから VCR。最良を返す。clear が例外なら null。</summary>
    internal static Found? Search(MagiState state, int[][] schedule, (int First, int Last) window, Func<bool> shouldStop)
    {
        var p = ScheduleUtil.CachedProblem(state);
        int[][] b0;
        try { b0 = V6NativeOptimizer.ClearCappedCells(state, schedule.Copy2D()).Schedule; }
        catch (Exception) { return null; }
        var baseHard = UnifiedViolationChecker.Check(state, b0.Copy2D()).Hard;
        var best = Vcr(state, b0, shouldStop);
        var bestRep = UnifiedViolationChecker.Check(state, best.Copy2D());
        for (var j = window.First; j <= window.Last; j++)
            for (var x = 0; x < p.S; x++)
                for (var y = x + 1; y < p.S; y++)
                {
                    if (shouldStop()) return null;
                    int kx = b0[x][j], ky = b0[y][j];
                    if (kx == ky || p.WishLocked(x, j) || p.WishLocked(y, j) || !p.MayPlace(x, ky) || !p.MayPlace(y, kx)) continue;
                    var nb = b0.Copy2D(); nb[x][j] = ky; nb[y][j] = kx;
                    if (UnifiedViolationChecker.Check(state, nb.Copy2D()).Hard > baseHard) continue;
                    var r = Vcr(state, nb, shouldStop);
                    var rep = UnifiedViolationChecker.Check(state, r.Copy2D());
                    if (UnifiedViolationChecker.BetterReport(rep, bestRep)) { best = r; bestRep = rep; }
                }
        return new Found(bestRep.Hard, best);
    }

    private static int[][] Vcr(MagiState state, int[][] board, Func<bool> shouldStop) =>
        ViolationComponentRepair.Repair(state, board.Copy2D(), Array.Empty<CombinatorialRepair.Candidate>(), new ViolationComponentRepair.Params(), shouldStop).NewSchedule;

    /// <summary>board が上限 0 の (職員, シフト) を使っている組（希望で固定したセルは除く）。上げ幅は最小の 1。</summary>
    private static List<Relax> UsedWalls(Problem p, List<(int Staff, int Shift)> walls, int[][] board) =>
        walls.Where(w => Enumerable.Range(0, p.T).Any(j => board[w.Staff][j] == w.Shift && !(p.WishLocked(w.Staff, j) && p.LockTo(w.Staff, j) == w.Shift)))
            .Select(w => new Relax(w.Staff, w.Shift, 1)).ToList();

    private static List<Move> Diff(int[][] a, int[][] b)
    {
        var o = new List<Move>();
        for (var j = 0; j < a[0].Length; j++)
            for (var i = 0; i < a.Length; i++)
                if (a[i][j] != b[i][j]) o.Add(new Move(i, j, a[i][j], b[i][j]));
        return o;
    }

    private static Outcome UnavailableOrStopped(Func<bool> stop) => stop() ? Stopped : new Unavailable("休みシフトが設定されていません");

    private static string? UnavailableReason(MagiState state, int[][] schedule)
    {
        var p = ScheduleUtil.CachedProblem(state);
        if (schedule.Length != p.S || schedule.Any(r => r.Length != p.T)) return "勤務表の大きさが設定と合いません";
        if (schedule.Any(row => row.Any(v => v < 0 || v >= p.K))) return "未割当のセルがあります";
        return null;
    }
}
