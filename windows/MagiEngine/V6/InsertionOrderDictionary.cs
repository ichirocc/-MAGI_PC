using System.Diagnostics.CodeAnalysis;

namespace MagiEngine.V6;

/// <summary>
/// [順序保証・監査是正 移植元] Kotlin の <c>LinkedHashMap</c>（挿入順保持を"公開契約"として保証する）に
/// 忠実な、挿入順保持辞書の最小実装。<see cref="Dictionary{TKey,TValue}"/> は <c>.Remove</c> を一切呼ばない
/// 使い方の下では現行の .NET 実装が挿入順を保つが、それは公開契約ではない（将来の BCL 変更や、
/// どこかに <c>.Remove()</c> が足された場合に静かに崩れうる）。<see cref="ViolationChecker"/> が返す
/// 族マップ（<c>Violations</c>/<c>CellFamilies</c> 等）は Range/Apt/C3Run/C3n/C3mn の貪欲修復パスが
/// この列挙順のまま候補を処理するため、順序の違いは「正しさ」でなく「keep-best が収束する具体的な
/// 局所最適」に効く＝Kotlin 側との厳密パリティ（golden fixture 一致）に直結する。よってここは
/// "たまたま今は保たれている挙動" ではなく型で保証する。
///
/// 削除は現状どの書き込みサイト（<c>ViolationChecker.Mark*</c> と <c>BuildFamilyMaps</c>）にも無いため
/// <c>Remove</c>/<c>Clear</c> は実装しない（write-once の用途にちょうど合わせた最小実装。必要になったら
/// 追加する）。
/// </summary>
internal sealed class InsertionOrderDictionary<TKey, TValue> : IReadOnlyDictionary<TKey, TValue>
    where TKey : notnull
{
    private readonly Dictionary<TKey, TValue> _map = new();
    private readonly List<TKey> _order = new();

    public TValue this[TKey key]
    {
        get => _map[key];
        set
        {
            if (!_map.ContainsKey(key)) _order.Add(key);
            _map[key] = value;
        }
    }

    public IEnumerable<TKey> Keys => _order;
    public IEnumerable<TValue> Values => _order.Select(k => _map[k]);
    public int Count => _map.Count;

    public bool ContainsKey(TKey key) => _map.ContainsKey(key);

    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value) => _map.TryGetValue(key, out value);

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        foreach (var k in _order) yield return new KeyValuePair<TKey, TValue>(k, _map[k]);
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
