namespace MagiApp.ViewModels;

/// <summary>「今月の作成条件」の希望の行（Kotlin <c>ChecklistLogic.kt</c>、3.643.0）: 登録の有無だけを数え、「集め終わったか」は判定しない。</summary>
public sealed record WishEntryCounts(int Entered, int ExtOnly, int NoInput)
{
    /// <param name="wishKeys">通常希望のセル鍵 "i,j"</param><param name="extKeys">拡張希望で禁止のあるセル鍵 "i,j"</param>
    public static WishEntryCounts Of(int staffN, IEnumerable<string> wishKeys, IEnumerable<string> extKeys)
    {
        static HashSet<int> StaffOf(IEnumerable<string> keys, int n) =>
            keys.Select(k => int.TryParse(k.Split(',')[0], out var i) ? i : -1).Where(i => i >= 0 && i < n).ToHashSet();
        var regular = StaffOf(wishKeys, staffN);
        var ext = StaffOf(extKeys, staffN);
        var entered = regular.Union(ext).Count();
        return new WishEntryCounts(entered, ext.Except(regular).Count(), Math.Max(0, staffN - entered));
    }

    public string Text() => $"入力あり {Entered}名" + (ExtOnly > 0 ? $"（拡張希望のみ {ExtOnly}名）" : "") + $"・未入力 {NoInput}名";
}
