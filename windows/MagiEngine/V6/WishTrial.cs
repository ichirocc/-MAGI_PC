using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// [S5] 「この希望を取り消したら」試算（Android <c>WishTrial.kt</c>、<c>docs/s5_wish_trial.md</c> §3・§4・§7）。
/// 同じ盤面で確実に消える分 a と、違反起点修復（VCR、既定 Params・候補プール空＝決定的）で減る見込み b を、
/// 希望を残したまま同じ修復を回した対照（Rk）を差し引いて出す。入力は書かない。結果は数値だけ（盤面を持たない＝仮盤禁止）。
/// </summary>
public static class WishTrial
{
    /// <summary>対照（希望を残したまま）。同じ (state, 盤面) なら希望の行によらず同じ＝呼び出し側で使い回せる。</summary>
    public sealed record Control(int H0, int Rk);

    public abstract record Outcome;
    public sealed record Result(int H0, int Hx, int Rk, int Rr, int A, int Att, int APrime, int B, int PKeep, int PCancel) : Outcome;
    /// <summary>試算できない盤面（未割当セル・休み無し設定など）。0 件とは言わない（I10）。</summary>
    public sealed record Unavailable(string Reason) : Outcome;
    /// <summary>止められた試算。部分結果を見込みにしない（I9）。</summary>
    public sealed record StoppedOutcome : Outcome;
    public static readonly Outcome Stopped = new StoppedOutcome();
    /// <summary><see cref="ControlOf"/> の成功値（Outcome として返すための包み）。</summary>
    public sealed record ControlOutcome(Control Control) : Outcome;

    /// <summary>対照 (H0, Rk)。試算できなければ <see cref="Unavailable"/>、止められたら <see cref="Stopped"/>。</summary>
    public static Outcome ControlOf(MagiState state, int[][] schedule, Func<bool>? shouldStop = null)
    {
        var stop = shouldStop ?? (() => false);
        if (UnavailableReason(state, schedule) is { } why) return new Unavailable(why);
        var h0 = UnifiedViolationChecker.Check(state, schedule.Copy2D()).Hard;
        if (VcrHard(state, schedule, stop) is not { } rk) return new Unavailable("休みシフトが設定されていません");
        if (stop()) return Stopped;
        return new ControlOutcome(new Control(h0, rk));
    }

    /// <summary>
    /// 希望 (staff, day) を取り消した試算。希望が無い・WishLocked でなければ null（試算の対象外）。
    /// <paramref name="control"/> を渡さなければここで計算する。
    /// </summary>
    public static Outcome? Trial(MagiState state, int[][] schedule, int staff, int day,
        Control? control = null, Func<bool>? shouldStop = null)
    {
        var stop = shouldStop ?? (() => false);
        var key = $"{staff},{day}";
        if (!state.Wishes.ContainsKey(key)) return null;
        var p = ScheduleUtil.CachedProblem(state);
        if (staff < 0 || staff >= p.S || day < 0 || day >= p.T || !p.WishLocked(staff, day)) return null;
        if (UnavailableReason(state, schedule) is { } why) return new Unavailable(why);
        Control ctl;
        if (control is not null) ctl = control;
        else
        {
            var c = ControlOf(state, schedule, stop);
            if (c is not ControlOutcome co) return c;
            ctl = co.Control;
        }
        var st2 = state with { Wishes = state.Wishes.Where(kv => kv.Key != key).ToDictionary(kv => kv.Key, kv => kv.Value) };
        var hx = UnifiedViolationChecker.Check(st2, schedule.Copy2D()).Hard;
        if (VcrHard(st2, schedule, stop) is not { } rr) return new Unavailable("休みシフトが設定されていません");
        if (stop()) return Stopped;
        var a = ctl.H0 - hx;
        var pKeep = Math.Min(ctl.H0, ctl.Rk);
        var pCancel = Math.Min(hx, rr);
        var att = pKeep - pCancel;
        return new Result(ctl.H0, hx, ctl.Rk, rr, a, att, Math.Min(a, Math.Max(0, att)), Math.Max(0, att - a), pKeep, pCancel);
    }

    /// <summary>WishLocked の希望のキー（"i,j"）。担当できない勤務の希望は入らない＝試算の対象外（§2.2）。</summary>
    public static IReadOnlySet<string> LockedWishKeys(MagiState state)
    {
        var p = ScheduleUtil.CachedProblem(state);
        var set = new HashSet<string>();
        foreach (var key in state.Wishes.Keys)
        {
            var parts = key.Split(',');
            var i = parts.Length > 0 && int.TryParse(parts[0].Trim(), out var a) ? a : -1;
            var j = parts.Length > 1 && int.TryParse(parts[1].Trim(), out var b) ? b : -1;
            if (i >= 0 && i < p.S && j >= 0 && j < p.T && p.WishLocked(i, j)) set.Add(key);
        }
        return set;
    }

    /// <summary>VCR は未割当セルがあると黙って何もしない（0 と区別できない）ので、先に弾く。</summary>
    private static string? UnavailableReason(MagiState state, int[][] schedule)
    {
        var p = ScheduleUtil.CachedProblem(state);
        if (schedule.Length != p.S || schedule.Any(r => r.Length != p.T)) return "勤務表の大きさが設定と合いません";
        if (schedule.Any(row => row.Any(v => v < 0 || v >= p.K))) return "未割当のセルがあります";
        return null;
    }

    /// <summary>本実行の入口と同じ clear のあと VCR（§4 の定数）。clear が例外（休み無し）なら null。
    /// C# の VCR 結果はレポートを持たないのでチェッカーで数え直す。</summary>
    private static int? VcrHard(MagiState state, int[][] schedule, Func<bool> shouldStop)
    {
        int[][] cleared;
        try { cleared = V6NativeOptimizer.ClearCappedCells(state, schedule.Copy2D()).Schedule; }
        catch (Exception) { return null; }
        var r = ViolationComponentRepair.Repair(state, cleared, Array.Empty<CombinatorialRepair.Candidate>(), new ViolationComponentRepair.Params(), shouldStop);
        return UnifiedViolationChecker.Check(state, r.NewSchedule).Hard;
    }
}
