namespace MagiEngine.V6;

/// <summary>
/// Web版 ALNS の Guided Local Search (GLS) ペナルティ（Voudouris &amp; Tsang）。移植。
///
/// <c>penalty[i][j][k]</c> を保持し、受理判定の拡張コストに <c>lambda * Σ penalty(セルの割当)</c> を
/// 加える。停滞時に「違反に寄与している割当」を <c>util = severity/(1+penalty)</c> が最大のものから
/// penalty+1 して、探索をその局所最適から遠ざける。グローバル最良は生スコアで別管理する前提（本クラスは
/// 受理バイアスのみ）。
/// </summary>
public sealed class GlsPenalty
{
    private readonly int _staff;
    private readonly int _days;
    private readonly int _shifts;

    public double Lambda { get; }

    private readonly Dictionary<int, int> _pen = new();
    private int _kicks;

    public GlsPenalty(int staff, int days, int shifts, double lambda = 200.0)
    {
        _staff = staff;
        _days = days;
        _shifts = shifts;
        Lambda = lambda;
    }

    private int Key(int i, int j, int k) => (i * _days + j) * _shifts + k;

    public int PenaltyOf(int i, int j, int k) => _pen.TryGetValue(Key(i, j, k), out var v) ? v : 0;

    public int KickCount() => _kicks;

    /// <summary>受理判定に加える拡張コスト寄与 = lambda * Σ penalty(i, j, schedule[i][j])。</summary>
    public double Augment(int[][] schedule)
    {
        if (_pen.Count == 0) return 0.0;
        long sum = 0L;
        for (int i = 0; i < _staff; i++)
        {
            if (i >= schedule.Length) continue;
            var row = schedule[i];
            for (int j = 0; j < _days; j++)
            {
                if (j >= row.Length) continue;
                int k = row[j];
                if (k >= 0 && k < _shifts && _pen.TryGetValue(Key(i, j, k), out var pv)) sum += pv;
            }
        }
        return Lambda * sum;
    }

    /// <summary>
    /// 違反セル集合のうち util = severity/(1+penalty) が最大の割当を1つ選び penalty+1。
    /// </summary>
    /// <param name="severity">既定は全セル一律 1.0（Kotlin原本の <c>{ _, _ -&gt; 1.0 }</c> デフォルト値。
    /// C# の record/method 既定引数はコンパイル時定数のみ許容するためラムダを直接デフォルトにできず、
    /// null許容＋メソッド本体内での null 合体演算子という同種のパターンを用いる
    /// — <see cref="SaParams.EffectiveShouldStop"/> と同じ移植上の判断）。</param>
    /// <returns>強化したら true（候補が無ければ false）。</returns>
    public bool PenalizeWorst(
        int[][] schedule,
        IReadOnlyCollection<(int I, int J)> cells,
        Func<int, int, double>? severity = null)
    {
        var sev = severity ?? ((_, _) => 1.0);
        int bestKey = -1;
        double bestUtil = -1.0;
        foreach (var (i, j) in cells)
        {
            if (i < 0 || i >= _staff || j < 0 || j >= _days) continue;
            if (i >= schedule.Length) continue;
            var row = schedule[i];
            if (j >= row.Length) continue;
            int k = row[j];
            if (k < 0 || k >= _shifts) continue;
            double util = sev(i, j) / (1.0 + PenaltyOf(i, j, k));
            if (util > bestUtil) { bestUtil = util; bestKey = Key(i, j, k); }
        }
        if (bestKey < 0) return false;
        _pen[bestKey] = (_pen.TryGetValue(bestKey, out var cur) ? cur : 0) + 1;
        _kicks++;
        return true;
    }

    /// <summary>
    /// [GLS aging] 全 penalty を keepPercent%（整数床）へ減衰する。長期停滞で penalty が肥大化し、受理
    /// バイアスが過度に固着して脱出が逆に難しくなるのを防ぐ（Voudouris &amp; Tsang の penalty aging 相当）。
    /// 0 になった項目は除去。グローバル最良は生スコア管理のため、減衰は探索の受理動学のみに作用し解の質を
    /// 退化させない。
    /// </summary>
    /// <returns>減衰後に残った非ゼロ項目数。</returns>
    public int Decay(int keepPercent = 80)
    {
        // [レビュー#8 3.213.0, Kotlin原本コメント] 値域契約の明示。100超は aging でなくペナルティ増幅に
        // なる（現行呼出は固定80のみ）。
        if (keepPercent is < 0 or > 100)
            throw new ArgumentException($"keepPercent must be in 0..100: {keepPercent}");
        foreach (var key in _pen.Keys.ToList())
        {
            int nv = _pen[key] * keepPercent / 100;
            if (nv <= 0) _pen.Remove(key); else _pen[key] = nv;
        }
        return _pen.Count;
    }
}
