namespace MagiEngine.V6;

/// <summary>
/// Kotlin <c>mapParallel</c> の移植: 純関数 <paramref name="f"/> を並列に適用し、<b>入力順</b>で返す。
/// 少数なら逐次（並列化の起動費のほうが高い）。後処理の研磨は探索本体が終わったあと単一スレッドで走るため、
/// 候補の評価だけ空いたコアへ配る。
/// </summary>
internal static class ParallelEval
{
    /// <summary>締切・停止を見る塊の大きさ（Kotlin <c>PARALLEL_EVAL_CHUNK</c>）。行き過ぎは 1 塊ぶんに収まる。</summary>
    public const int Chunk = 64;

    public static R[] MapParallel<T, R>(IReadOnlyList<T> items, Func<T, R> f, int minParallel = 8)
    {
        var outArr = new R[items.Count];
        if (items.Count < minParallel)
        {
            for (var i = 0; i < items.Count; i++) outArr[i] = f(items[i]);
            return outArr;
        }
        Parallel.For(0, items.Count, i => outArr[i] = f(items[i]));
        return outArr;
    }
}
