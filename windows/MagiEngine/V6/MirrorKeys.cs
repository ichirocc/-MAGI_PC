namespace MagiEngine.V6;

/// <summary>
/// Faithful port of Kotlin's <c>MirrorKeys</c> (in <c>MirrorCore.kt</c>) — the single source of
/// truth for the 19 violation families' names, HARD/SOFT classification, and weights.
/// </summary>
public static class MirrorKeys
{
    public static readonly IReadOnlyList<string> Hard = new[] { "groupViol", "c3n", "covU", "pref", "c3w" };

    public static readonly IReadOnlyList<string> Soft = new[]
    {
        "c1", "c2", "c3", "c3m", "c3mn", "c41", "c42", "c41s", "c42s", "covO",
        "low", "high", "apt", "fair", "weekly",
    };

    // [3.542.0] c3w は末尾＝言語跨ぎ期待値ファイル・C++ kBreakdownNames の添字を既存19族から動かさない。
    public static readonly IReadOnlyList<string> All = new[]
    {
        "c1", "c2", "c3", "c3n", "c3m", "c3mn", "c41", "c42", "c41s", "c42s",
        "covU", "covO", "pref", "low", "high", "groupViol", "apt", "fair", "weekly", "c3w",
    };

    /// <summary>
    /// weightedScore の重み（単一の真実）。**宣言順 = 加算順**（<see cref="WeightedScore"/> の
    /// double 合計結果を Kotlin 側とビット単位で一致させるため、列挙順に依存する）。
    /// <see cref="Dictionary{TKey,TValue}"/> の列挙順は .NET の公開契約ではない（実装は現状
    /// 挿入順を保つが、将来変わらない保証はない）ため、順序を明示的に保持する配列を単一の真実にし、
    /// O(1) ルックアップ用の辞書はそこから派生させる。
    /// [3.522.0/HF77明示数値指示・全面見直し] Android tools/loop 34ケース×10seedのbaseline対比
    /// ベンチマークで決定（経緯は Android docs/history/3.4xx.md）。旧: groupViol(10000)>pref(9000)>
    /// covU(8000)>c3n(7000)>low(90)>c3mn(30)=c1(30)>high(25)>covO(5)>c3(3)>c3m(2)>他(1)。
    /// covOは0.5→1.0→5.0→10、c1は4→5→15→30→50、c3mnは12→15→30→90、highは45→25で不変＝
    /// いずれもHF77明示指示。
    /// </summary>
    private static readonly (string Key, double Weight)[] WeightsOrdered =
    {
        ("groupViol", 11000.0), ("covU", 10000.0), ("c3n", 9000.0), ("pref", 8000.0),
        ("low", 120.0), ("c3mn", 90.0), ("c1", 50.0), ("high", 25.0), ("covO", 10.0),
        ("c3", 15.0), ("c3m", 6.0),
        ("c41", 9.0), ("c42", 9.0), ("c41s", 10.0), ("c42s", 10.0),
        ("c2", 4.0), ("apt", 4.0), ("fair", 2.0), ("weekly", 2.0),
        // [3.542.0/ユーザー明示指示] 希望の前日に禁止。c3n と同格。
        ("c3w", 9000.0),
    };

    public static IReadOnlyList<(string Key, double Weight)> Weights => WeightsOrdered;

    private static readonly IReadOnlyDictionary<string, double> WeightsByKey =
        WeightsOrdered.ToDictionary(kv => kv.Key, kv => kv.Weight);

    /// <summary>
    /// [3.395.0/高速化 移植元] <see cref="All"/> の族名 → 添字。checker の <c>inc</c> はこの添字で
    /// <c>int[]</c> を加算する（ハッシュ探索＋Int のボクシングを避ける）。
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> Index =
        All.Select((k, i) => (k, i)).ToDictionary(x => x.k, x => x.i);

    /// <summary>
    /// [HF77明示指示/表示優先度] aptLow/aptHigh は apt の表示専用サブクラス（<see cref="Weights"/>
    /// 自体には追加しない＝重み表(WeightTableCard相当)には出さない）。markCount/cellFamilies の
    /// 重み優先比較では実体である apt の重み(1.0)をそのまま使う。
    /// </summary>
    public static double WeightOf(string family) => family switch
    {
        "aptLow" or "aptHigh" => WeightsByKey.TryGetValue("apt", out var w) ? w : 0.0,
        _ => WeightsByKey.TryGetValue(family, out var w2) ? w2 : 0.0,
    };
}
