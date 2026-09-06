namespace MagiEngine.V6;

/// <summary>
/// 盤面の同値比較: <see cref="AdaptiveEliteArchive.ScheduleHash"/> を一次キーに、衝突時だけ全セルを比較する
/// （ビームの重複排除用。旧: 全セルを区切り文字つき文字列へ連結していた）。Kotlin の BoardKey と同型。
/// </summary>
internal sealed class ScheduleEqualityComparer : IEqualityComparer<int[][]>
{
    public static readonly ScheduleEqualityComparer Instance = new();

    public bool Equals(int[][]? x, int[][]? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null || x.Length != y.Length) return false;
        for (var i = 0; i < x.Length; i++)
            if (!x[i].AsSpan().SequenceEqual(y[i])) return false;
        return true;
    }

    public int GetHashCode(int[][] obj)
    {
        var h = AdaptiveEliteArchive.ScheduleHash(obj);
        return unchecked((int)(h ^ (h >>> 32)));
    }
}
