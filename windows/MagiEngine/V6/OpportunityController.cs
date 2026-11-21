using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// 残り違反に合わせて PORTFOLIO の役割を選ぶ制御（Kotlin <c>OpportunityController</c>、Android 3.727.122、<see cref="PolishGate.RogsController"/>）。
/// 役割を替えるワーカーに、「いまの最良盤面に残っている違反の種類（局面）」で過去に 1 秒あたり最も前進した役割を渡す。
/// 前進はワーカー自身の最良の前進（<see cref="GainOf"/>）。試していない役割を先に試し、あとは UCB で選ぶ。W0・W4 は従来どおり（呼び出し側）。
/// 表は全ワーカーで共有し、呼び出し側の lock の中で読み書きする。
/// </summary>
internal sealed class OpportunityController
{
    private sealed class Arm { public int N; public double Sec; public double Gain; }
    private readonly IReadOnlyList<HypothesisEpochRole> _roles;
    private readonly Dictionary<string, Dictionary<HypothesisEpochRole, Arm>> _table = new();

    /// <summary>必須 1 件を重みに直す値（必須の重みの桁＝covU の重みに合わせる）。</summary>
    public static readonly double HardUnit = MirrorKeys.WeightOf("covU");
    public const double Explore = 0.5;

    public OpportunityController(IReadOnlyList<HypothesisEpochRole> roles) => _roles = roles;

    /// <summary>局面: 必須が残れば重みが最大の必須の種類、無ければ重みの寄与が最大の要調整の種類。</summary>
    public string PhaseOf(ViolationReport r)
    {
        var keys = r.Hard > 0 ? MirrorKeys.Hard : r.Breakdown.Keys.Where(k => !MirrorKeys.Hard.Contains(k)).ToList();
        string? top = null; var topV = double.MinValue;
        foreach (var k in keys)
        {
            var v = r.Breakdown.GetValueOrDefault(k) * MirrorKeys.WeightOf(k);
            if (v > topV) { topV = v; top = k; }
        }
        return top is null || r.Breakdown.GetValueOrDefault(top) == 0 ? (r.Hard > 0 ? "hard" : "soft") : top;
    }

    /// <summary>エポック 1 回の結果を記録する。before→after はワーカー自身の最良。</summary>
    public void Record(string phase, HypothesisEpochRole role, double seconds, ViolationReport before, ViolationReport after)
    {
        if (!_table.TryGetValue(phase, out var arms)) { arms = new(); _table[phase] = arms; }
        if (!arms.TryGetValue(role, out var a)) { a = new Arm(); arms[role] = a; }
        a.N++; a.Sec += Math.Max(seconds, 0.001);
        a.Gain += GainOf(before, after);
    }

    /// <summary>次の役割。current は続けて選ばない（同じ役割で停滞したから替える）。</summary>
    public HypothesisEpochRole Choose(string phase, HypothesisEpochRole current)
    {
        var arms = _table.GetValueOrDefault(phase) ?? new Dictionary<HypothesisEpochRole, Arm>();
        var cands = _roles.Where(r => r != current).ToList();
        foreach (var r in cands) if (!arms.TryGetValue(r, out var a0) || a0.N == 0) return r;
        double total = cands.Sum(r => arms[r].N);
        var best = Math.Max(cands.Max(r => arms[r].Gain / arms[r].Sec), 1e-9);
        HypothesisEpochRole pick = cands[0]; var pickV = double.MinValue;
        foreach (var r in cands)
        {
            var a = arms[r];
            var v = a.Gain / a.Sec / best + Explore * Math.Sqrt(Math.Log(total + 1.0) / a.N);
            if (v > pickV) { pickV = v; pick = r; }
        }
        return pick;
    }

    /// <summary>ログ用: 局面ごとの役割の試行回数と 1 秒あたりの前進。</summary>
    public string Summary() => string.Join(" ; ", _table.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e =>
        e.Key + ": " + string.Join(" ", e.Value.OrderByDescending(x => x.Value.Gain / x.Value.Sec).Select(x =>
            $"{AdaptiveHypothesisEpochPolicy.RoleName(x.Key)}={x.Value.N}回/{x.Value.Gain / x.Value.Sec:F0}毎秒"))));

    /// <summary>前進の量。重みは必須の重みを含むので、必須が減って要調整が増えた手は必須の減り×<see cref="HardUnit"/> で数える。</summary>
    public static double GainOf(ViolationReport before, ViolationReport after) =>
        Math.Max(0.0, Math.Max(before.WeightedScore - after.WeightedScore, (before.Hard - after.Hard) * HardUnit));
}
