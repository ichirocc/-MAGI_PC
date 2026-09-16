namespace MagiEngine.V6;

/// <summary>
/// 盤面の同値鍵（Kotlin <c>BoardKey</c> の移植）: <see cref="AdaptiveEliteArchive.ScheduleHash"/> を一次キーに、
/// 衝突時だけ全セルを比較する。distinct・重複除去で盤面を文字列へ連結しない（1 盤面 2〜3KB の生成を省く）。
/// </summary>
internal sealed class BoardKey : IEquatable<BoardKey>
{
    private readonly int[][] _work;
    private readonly long _h;

    public BoardKey(int[][] work)
    {
        _work = work;
        _h = AdaptiveEliteArchive.ScheduleHash(work);
    }

    public bool Equals(BoardKey? other) =>
        other is not null && _h == other._h && AdaptiveEliteArchive.SameSchedule(_work, other._work);

    public override bool Equals(object? obj) => Equals(obj as BoardKey);

    public override int GetHashCode() => (int)(_h ^ (long)((ulong)_h >> 32));
}
