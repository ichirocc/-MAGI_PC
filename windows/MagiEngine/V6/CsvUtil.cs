using System.Linq;
using System.Text;

namespace MagiEngine.V6;

/// <summary>
/// [フェーズ7ピース10] <c>ScheduleCsvBridge.kt</c>（842行）のファイルスコープ private ヘルパ群
/// （417〜513行: <c>appendCsvRow</c>／<c>csvEscapeCell</c>／<c>nameMatchKey</c>／<c>firstWinsMap</c>／
/// <c>CsvParse</c>／<c>parseCsvRows</c>／<c>parseCsvFull</c>／<c>csvBody</c>）の移植先。
///
/// Kotlin原本ではこれら8個の宣言はファイルスコープ private（同一ファイル内の6つの <c>object</c>
/// 全てから見える）。C# 側は <see cref="V6HotfixPasses"/>（単一の巨大 Kotlin <c>object</c>を
/// <c>partial class</c>で分割）とは事情が違い、<c>RosterCsvImport</c>/<c>FlatRosterCsvImport</c>/
/// <c>ScheduleCsvBridge</c>（フェーズ7ピース10）と <c>StaffCsvIO</c>/<c>WishesCsvIO</c>/
/// <c>ConstraintsCsvIO</c>（フェーズ7ピース11）という**Kotlin側でも元から別々の object 名**を持つ
/// 6個のトップレベル static class へ分かれる。ファイルスコープ private をそのまま複製すると6箇所で
/// 無意味な重複が生まれるため、このリポジトリの既存規約（<see cref="ScheduleUtil"/>／
/// <see cref="KotlinInterop"/>／<see cref="MirrorKeys"/> と同じ「横断ヘルパは専用の
/// internal static class へ集約する」パターン）に従い、<c>internal</c>
/// （同一アセンブリ内の全クラスから可視・アセンブリ外へは非公開）としてここへ集約する。
///
/// <c>CsvBody</c>（<see cref="CsvBody"/>）はフェーズ7ピース11の3クラス（<c>StaffCsvIO</c>/
/// <c>WishesCsvIO</c>/<c>ConstraintsCsvIO</c>）のみが使う（ピース10側の3クラス
/// <c>RosterCsvImport</c>/<c>FlatRosterCsvImport</c>/<c>ScheduleCsvBridge</c> からは移植元
/// Kotlin コードで一度も呼ばれないことを確認済み）ため、ピース11でこのファイルへ追記した。
/// </summary>
internal static class CsvUtil
{
    internal static void AppendCsvRow(StringBuilder outSb, IReadOnlyList<string> values)
    {
        for (var idx = 0; idx < values.Count; idx++)
        {
            if (idx > 0) outSb.Append(',');
            outSb.Append(CsvEscapeCell(values[idx]));
        }
        outSb.Append('\n');
    }

    internal static string CsvEscapeCell(string value)
    {
        var mustQuote = false;
        foreach (var ch in value)
        {
            if (ch == ',' || ch == '"' || ch == '\n' || ch == '\r') { mustQuote = true; break; }
        }
        var escaped = value.Replace("\"", "\"\"");
        return mustQuote ? $"\"{escaped}\"" : escaped;
    }

    /// <summary>
    /// 氏名照合用キー: 全角(U+3000)/半角を含む空白を全て除去する。これにより外部CSVの
    /// "山本 昌幸"(空白あり) と 状態側の "山本昌幸"(空白なし) を同一人物として照合できる
    /// （取込で1人分しか入らない/氏名不一致で弾かれる事故を防ぐ）。
    /// </summary>
    internal static string NameMatchKey(string s) =>
        new(s.Where(c => !char.IsWhiteSpace(c)).ToArray());

    /// <summary>
    /// [P1/重複解決の一致] 先勝ちの index マップ。Kotlin の <c>associateBy</c> は後勝ちで、制約評価
    /// （<c>Problem</c> の <c>IndexOfFirst</c>=先勝ち）と食い違うため、CSV照合は必ずこちらを使う。
    /// </summary>
    internal static IReadOnlyDictionary<string, int> FirstWinsMap(int n, Func<int, string> key)
    {
        var m = new Dictionary<string, int>();
        for (var i = 0; i < n; i++)
        {
            var k = key(i);
            if (!m.ContainsKey(k)) m[k] = i;
        }
        return m;
    }

    /// <summary>
    /// [3.413.0/I-08 移植元] CSV の解析結果。旧実装は行だけを返し、引用符が閉じないまま入力が
    /// 終わっても何も検出しなかった（<c>inQuote</c> が true のまま抜ける）。この場合、開いた引用符
    /// 以降の全文が1セルへ吸い込まれ残りの行が丸ごと消えるのに、呼出側からは「短いCSV」と
    /// 区別が付かない。走査器を2つ作ると必ずドリフトするので、既存のループから両方を返す形にして
    /// <see cref="ParseCsvRows"/> はその行だけを取り出す薄い委譲にする（既存の呼出は無変更）。
    /// </summary>
    internal sealed class CsvParse
    {
        internal IReadOnlyList<IReadOnlyList<string>> Rows { get; }
        internal bool UnclosedQuote { get; }

        internal CsvParse(IReadOnlyList<IReadOnlyList<string>> rows, bool unclosedQuote)
        {
            Rows = rows;
            UnclosedQuote = unclosedQuote;
        }
    }

    internal static IReadOnlyList<IReadOnlyList<string>> ParseCsvRows(string raw) => ParseCsvFull(raw).Rows;

    internal static CsvParse ParseCsvFull(string raw)
    {
        // UTF-8 BOM(U+FEFF) 除去: 付いていると先頭セルが "(BOM)ユニット" 等になり、Trim()でも消えず
        //   ヘッダ判定(== "ユニット" 等)が失敗して取り込めなくなる。Excel/UTF-8出力由来で頻出。
        var text = raw.Length > 0 && raw[0] == (char)0xFEFF ? raw.Substring(1) : raw;
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var inQuote = false;
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (inQuote && c == '"' && i + 1 < text.Length && text[i + 1] == '"')
            {
                cell.Append('"');
                i++;
            }
            else if (c == '"')
            {
                inQuote = !inQuote;
            }
            else if (!inQuote && c == ',')
            {
                row.Add(cell.ToString());
                cell.Clear();
            }
            else if (!inQuote && (c == '\n' || c == '\r'))
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(cell.ToString());
                cell.Clear();
                rows.Add(new List<string>(row));
                row.Clear();
            }
            else
            {
                cell.Append(c);
            }
            i++;
        }
        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            rows.Add(new List<string>(row));
        }
        return new CsvParse(rows, inQuote);
    }

    /// <summary>
    /// [フェーズ7ピース11] コンポーネント別CSV（<see cref="StaffCsvIO"/>/<see cref="WishesCsvIO"/>/
    /// <see cref="ConstraintsCsvIO"/>）の本体行を返す。
    ///
    /// 旧実装は各 parse が「1行だけのCSVを無条件に拒否」し、かつヘッダ判定を「先頭が既知の値か」という
    /// 間接的な推測に頼っていた。<c>Build</c> が出す実ヘッダ（氏名 / 種別 …）で明示的に判定し、
    /// それ以外は全行を本体として扱う。1行データも取り込める。
    /// </summary>
    internal static IReadOnlyList<IReadOnlyList<string>> CsvBody(
        IReadOnlyList<IReadOnlyList<string>> rows, string headerFirstCell)
    {
        if (rows.Count == 0) return Array.Empty<IReadOnlyList<string>>();
        var head = (rows[0].Count > 0 ? rows[0][0] : "").Trim();
        return head == headerFirstCell ? rows.Skip(1).ToList() : rows;
    }

    /// <summary>
    /// [監査再検証で判明した再発/3.410.0・3.413.0系の再修正] 「最初に出現した順」を保ったまま件数を
    /// 数える集計器。Kotlin原本（<c>ScheduleCsvBridge.kt</c> 376/622-623行）は未知記号の集計に
    /// <c>LinkedHashMap</c>&lt;String, Int&gt; を使う。<c>LinkedHashMap</c> は挿入順で列挙するため、
    /// ①件数で降順ソートした際の同数タイブレークは「ファイル中で最初に見つかった記号」順になり
    /// （<c>sortedByDescending</c> は安定ソート）、②単純に先頭N件を取る呼出（<c>entries.take(N)</c>）は
    /// そのまま「最初に出会った異なる記号から順にN件」を返す。C#の <c>Dictionary&lt;TKey,TValue&gt;</c>
    /// はBCLとして列挙順を契約保証しない（現在のCoreCLR実装はキー削除が無ければ挿入順を保つが、これは
    /// 実装詳細であり将来のランタイム変更やリハッシュで壊れ得る——<see cref="FirstWinsMap"/>とは違い
    /// ここは「同点の見せ方」というUI/ログ表示に直結するため、契約に依らず明示的に順序を保証する）。
    /// 実装は List(挿入順)+Dictionary(O(1)検索) の組で、<c>FlatRosterCsvImport</c> の
    /// symList/symSeen と同じ手法。<see cref="IReadOnlyDictionary{TKey,TValue}"/> を実装するため、
    /// 既存の <c>Dictionary&lt;string,int&gt;</c> を受け渡していた箇所（戻り値の型・呼出側の
    /// Count/Take/Values.Sum()/インデクサ）へそのまま差し込める。
    /// </summary>
    internal sealed class OrderedCounter : IReadOnlyDictionary<string, int>
    {
        private readonly List<string> _order = new();
        private readonly Dictionary<string, int> _counts = new();

        internal void Increment(string key)
        {
            if (_counts.TryGetValue(key, out var cur)) _counts[key] = cur + 1;
            else { _counts[key] = 1; _order.Add(key); }
        }

        public int Count => _order.Count;
        public int this[string key] => _counts[key];
        public IEnumerable<string> Keys => _order;
        public IEnumerable<int> Values => _order.Select(k => _counts[k]);
        public bool ContainsKey(string key) => _counts.ContainsKey(key);
        public bool TryGetValue(string key, out int value) => _counts.TryGetValue(key, out value);

        public IEnumerator<KeyValuePair<string, int>> GetEnumerator()
        {
            foreach (var k in _order) yield return new KeyValuePair<string, int>(k, _counts[k]);
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
