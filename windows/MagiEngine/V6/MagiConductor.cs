namespace MagiEngine.V6;

/// <summary>
/// 停滞脱出アクション。Noop=何もしない（停滞前の既定）/ Reheat=最良へ戻して再加熱 /
/// StrongPerturb=最良から大きく摂動して脱出 / ScaleTemp=盤面を最良へ戻さず現在解のまま次ラダーへ。
/// [HF77訂正 3.213.0, Kotlin原本コメント] ScaleTemp は専用の温度倍率を持たない（全アクション共通で
/// 次ラダーが t0 から再加熱）。Reheat との差は「盤面を best へ戻すか（Reheat）/ 現在解を保つか
/// （ScaleTemp）」のみ。
/// </summary>
public enum ConductorAction { Noop, Reheat, StrongPerturb, ScaleTemp }

/// <summary>
/// Port of Kotlin's <c>MagiConductor</c> (itself a port of the Web version's Python-integrated
/// core) — a UCB1 multi-armed-bandit stagnation-escape strategy selector.
///
/// While the iteration count since the last best-score improvement
/// (<see cref="ItersSinceImprove"/>) stays below <see cref="StagThreshold"/>, <see cref="SelectAction"/>
/// returns <see cref="ConductorAction.Noop"/>. Once reached, it autonomously selects an escape
/// strategy {Reheat, StrongPerturb, ScaleTemp} via UCB1 and online-learns each strategy's
/// effectiveness (reward) via <see cref="UpdateReward"/> — a strict upgrade over a fixed reheat
/// policy. <see cref="SelectAction"/> is itself deterministic (no RNG dependency of its own).
/// </summary>
public sealed class MagiConductor
{
    private static readonly int NA = Enum.GetValues<ConductorAction>().Length;

    /// <summary>Iteration count since the last best-score improvement, above which <see cref="SelectAction"/> stops returning <see cref="ConductorAction.Noop"/>.</summary>
    public int StagThreshold { get; set; }

    private readonly int[] _counts;
    private readonly double[] _values;
    private int _total;

    public int ItersSinceImprove { get; private set; }

    public MagiConductor(int stagThreshold = 3000)
    {
        StagThreshold = stagThreshold;
        _counts = new int[NA];
        Array.Fill(_counts, 1);
        _values = new double[NA];
    }

    /// <summary>1反復ごとに呼ぶ。最良が更新されたら停滞カウンタをリセット、そうでなければ +1。</summary>
    public void UpdateStagnation(bool improved) => ItersSinceImprove = improved ? 0 : ItersSinceImprove + 1;

    /// <summary>
    /// [ネイティブ加速] チャンク実行後にまとめて反映。<paramref name="improvedInChunk"/>=チャンク内で
    /// 最良更新があったか、<paramref name="tailIters"/>=最後の更新以降の反復数（無更新チャンクなら
    /// 全反復数）。逐次 <see cref="UpdateStagnation"/> と等価。
    ///
    /// [C#移植上の判断] このメソッドの唯一の呼出元（Kotlin原本の <c>runWorkerNative</c>）は
    /// magi_native.cpp 経由のネイティブ加速専用パスで、この移植のスコープ外（計画書の明示的な
    /// スコープ外決定）。それでもメソッド自体は <see cref="UpdateStagnation"/> と等価な純粋状態遷移
    /// （副作用なし・ネイティブ依存なし）なので、API 面の完全性のため Kotlin 原本どおり移植した。
    /// </summary>
    public void UpdateStagnationBulk(bool improvedInChunk, int tailIters) =>
        ItersSinceImprove = improvedInChunk ? tailIters : ItersSinceImprove + tailIters;

    /// <summary>停滞しきい値未満は Noop。到達後は UCB1 で 1..3（Noop以外）から選ぶ。</summary>
    public ConductorAction SelectAction()
    {
        if (ItersSinceImprove < StagThreshold) return ConductorAction.Noop;
        int best = 1;
        double bestUcb = double.NegativeInfinity;
        for (int a = 1; a < NA; a++)
        {
            double ucb = _values[a] + Math.Sqrt(2.0 * Math.Log(_total + 1) / _counts[a]);
            if (ucb > bestUcb) { bestUcb = ucb; best = a; }
        }
        _counts[best]++;
        _total++;
        return (ConductorAction)best;
    }

    /// <summary>選択アクションの効果（reward）をオンライン学習（指数移動平均, alpha=0.1）。</summary>
    public void UpdateReward(ConductorAction action, double reward)
    {
        int a = (int)action;
        _values[a] += 0.1 * (reward - _values[a]);
    }

    /// <summary>検査用。</summary>
    public double ValueOf(ConductorAction action) => _values[(int)action];

    /// <summary>検査用。</summary>
    public int CountOf(ConductorAction action) => _counts[(int)action];
}
