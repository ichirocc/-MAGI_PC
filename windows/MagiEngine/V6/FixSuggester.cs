using System.Globalization;
using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>改善手の種類（UIのチップ表示用）。Faithful port of Kotlin's <c>enum class FixKind</c>.</summary>
public enum FixKind { Change, ChangeMulti, Swap, SwapXDay, SwapMulti, Chain, Window }

/// <summary>1セルへの代入（move = これらを盤面にセットする）。Faithful port of Kotlin's <c>FixCell</c> data class.</summary>
public sealed record FixCell(int Staff, int Day, int ToShift);

/// <summary>
/// [改善提案] 1手で違反がどれだけ減るかを評価した候補。Faithful port of Kotlin's <c>FixSuggestion</c>
/// data class. move は ops（セル代入の集合）で表現し、適用は ops を順にセットするだけ（全種類を統一）。
///  - Change      : 1マスを別シフトへ
///  - ChangeMulti : 同一スタッフの2マスを同時変更（下限の競合など、1マスでは直せない不足に有効）
///  - Swap        : 同日2人を入れ替え（被覆不変）
///  - SwapXDay    : 別日どうしを入れ替え（被覆が両日で変化）
///  - SwapMulti   : 同日3人を巡回交換（2人交換が担当可否で塞がる時の打開）
///  - Chain       : 不足シフトを貪欲に最大3コマ補充（エジェクションチェーン／玉突き）
///  - Window      : 1日×最大4名を総当たりで最適割当（ミニ・マスヒューリスティクス）
///
/// [C#移植上の注記・レコード等価性] Kotlin の <c>data class</c> は <c>List&lt;FixCell&gt;</c>/
/// <c>List&lt;Pair&lt;String,Int&gt;&gt;</c> 型のフィールドについても、Java/Kotlin の <c>List</c> が
/// 要素ごとの構造的等価性を実装しているため（<c>AbstractList.equals()</c>）、全体として構造的等価性を
/// 持つ。対して C# の <see cref="IReadOnlyList{T}"/>/<see cref="List{T}"/> は既定で参照等価性のため、
/// この record の <c>Ops</c>/<c>Diff</c> フィールドは（record全体としては）参照等価性混じりになる。
/// 本ファイル・唯一の既存テスト（<c>FixSuggesterTest.cs</c>）とも <c>FixSuggestion</c> の
/// <c>Equals</c>/<c>==</c> を一切使わない（<c>Ops</c> を直接読んで独自の署名文字列を作る）ため実害は
/// 無いが、将来この record を等価比較に使う場合はこの相違に注意すること。
/// </summary>
public sealed record FixSuggestion(
    FixKind Kind,
    IReadOnlyList<FixCell> Ops,
    string Label,
    int DeltaHard,
    int DeltaTotal,
    IReadOnlyList<(string Family, int Delta)> Diff);

/// <summary>
/// 違反を減らす「1手」を列挙する。最適化エンジンと同じ評価（canDo 可否・希望ロック保護・
/// <see cref="UnifiedViolationChecker"/> による被覆込み (hard,weighted,total) 辞書式改善）。
/// Change / ChangeMulti / Swap / SwapXDay / SwapMulti / Chain / Window を統合し、効果順・同型重複排除で
/// 返す。読取専用。高コストな手（複数マス・別日・3人）は違反箇所にターゲットし、締切（deadlineMs）で
/// 打ち切る賢い探索。
///
/// Faithful port of Kotlin's <c>object FixSuggester { fun suggest(...) }</c> (<c>V6SwapSuggester.kt</c>).
///
/// [C#移植上の注記・非逐次フェーズ順] 内部の探索は Phase 1→2→3→6→7→4→5 という非逐次の順序で実行される
/// （Kotlin原本のコメント自身がこの並びを意図的なものと明記している）。この順序は
/// <c>maxResults</c>/<c>deadlineMs</c> による打ち切りが発生したときに「どの種類の手が優先的に見つかるか」
/// を左右するため、逐語的に保存する（読みやすさのために1→7の連番へ「整理」しない）。
///
/// [C#移植上の注記・Problem構築] Kotlin原本は <c>Problem(state)</c> を直接構築しており、他の多くの
/// 移植済みファイルが使う <see cref="ScheduleUtil.CachedProblem"/>（参照ベースのメモ化）を経由しない。
/// この関数固有の設計選択としてそのまま踏襲する（勝手にキャッシュ経由へ「最適化」しない）。
///
/// [C#移植上の注記・日付書式ヘルパー] ローカル関数 <c>Dlab</c> は「月/日」のみを返し曜日を含まない点で、
/// 既に移植済みの3つの "DayLabel" 系ヘルパー（<see cref="ScheduleUtil.FormatDay"/>・
/// <see cref="V6PortAnalyzer.DayLabel"/>・<see cref="V6SanityPort.SafeDayLabel"/>、いずれも曜日付き
/// "月/日(曜)" を返す）のどれとも異なる別関数。日付パースの厳密さ（<c>java.time.LocalDate.parse</c> と
/// 同じ厳格な <c>yyyy-MM-dd</c> パース）はそれら3関数と同じ様式に揃えたが、書式が単純に異なるため
/// 統合しない（<c>V6SanityPort.SafeDayLabel</c> の doc comment が述べる規律と同じ）。
///
/// [C#移植上の注記・HashSet列挙順] <c>countHot</c>/<c>shortShift[i]</c>/<c>hotDays</c> は Kotlin側で
/// <c>HashSet&lt;Int&gt;</c>（JVM実装依存のハッシュバケット順で列挙される）から <c>.toList()</c> される
/// 箇所が複数ある（Phase 3/6 の <c>targetStaff</c>/<c>chainStaff</c>、Phase 4/7 の <c>days3</c>/
/// <c>wDays</c>）。C#の <see cref="HashSet{T}"/> は異なる内部実装を持ち列挙順が一致する保証は無い
/// （.NET Core以降は削除の無い挿入のみのセットでは実務上は挿入順を保つ傾向があるが、契約ではない）。
/// この列挙順の相違は「どの職員/日を先に試すか」という探索の優先順位にのみ影響し、
/// <c>maxResults</c>/<c>deadlineMs</c> で打ち切られない限り最終的に見つかる手の集合そのものは一致する。
/// 唯一の既存テスト（<c>FixSuggesterTest.cs</c>）はこの相違の影響を受けない規模の固定盤面のため実害は無い。
/// </summary>
public static class FixSuggester
{
    /// <summary>探索の上限（Android 3.507.4 で集約。値は 2.4x 系からの据え置き）。</summary>
    internal static class Limits
    {
        public const int ChainRounds = 3;          // 連鎖で積み上げる最大コマ数
        public const int WindowStaff = 4;          // 再最適化で同時に動かす最大人数
        public const long WindowCombos = 20_000L;  // 再最適化 1 日あたりの総当たり上限（超えれば人数を減らす）
        public const int WindowDays = 5;           // 再最適化する最大日数
        public const int Pack = 1000;              // (staff, day) を staff*Pack+day で詰める（T<=31 の前提）
    }

    public static List<FixSuggestion> Suggest(
        MagiState state,
        int[][] schedule,
        int? focusStaff = null,
        int? focusShift = null,
        int maxResults = 8,
        long deadlineMs = 8000L)
    {
        var p = new Problem(state);
        if (p.S < 1 || p.T < 1) return new List<FixSuggestion>();
        return new Session(state, p, ScheduleUtil.NormalizeSchedule(schedule, p), focusStaff, focusShift, deadlineMs).Run(maxResults);
    }

    private sealed class Quad
    {
        public readonly FixSuggestion Sug;
        public readonly int DHard;
        public readonly int DTotal;
        public readonly double DWeighted;

        public Quad(FixSuggestion sug, int dHard, int dTotal, double dWeighted)
        {
            Sug = sug;
            DHard = dHard;
            DTotal = dTotal;
            DWeighted = dWeighted;
        }
    }

    /// <summary>1 回の提案探索。盤面 <c>_s</c> は各手を適用→評価→復元するので、フェーズ間で常に入力（正規化後）に一致する。</summary>
    private sealed class Session
    {
        private readonly MagiState _state;
        private readonly Problem _p;
        private readonly int[][] _s;
        private readonly int? _focus;
        private readonly int? _focusShift;
        private readonly long _deadlineMs;
        private readonly ViolationReport _base;
        private readonly List<Quad> _found = new();
        private readonly long _start = EngineClock.NowMs();

        // 違反に関与する staff / day / shift のターゲット集合。
        private readonly HashSet<int> _countHot = new();                       // 回数違反(low/high)のある staff
        private readonly Dictionary<int, HashSet<int>> _shortShift = new();    // staff -> 下限割れのシフト集合
        private readonly List<int> _hotCells = new();                          // staff*Pack+day（セル違反）
        private readonly HashSet<int> _hotDays = new();

        public Session(MagiState state, Problem p, int[][] s, int? focus, int? focusShift, long deadlineMs)
        {
            _state = state; _p = p; _s = s; _focus = focus; _focusShift = focusShift; _deadlineMs = deadlineMs;
            _base = UnifiedViolationChecker.Check(state, s);
            foreach (var (key, cls) in _base.CountViolations)
            {
                var (i, k) = ParseKey(key);
                if (i is null || k is null) continue;
                _countHot.Add(i.Value);
                if (cls == "vio-low")
                {
                    if (!_shortShift.TryGetValue(i.Value, out var set)) { set = new HashSet<int>(); _shortShift[i.Value] = set; }
                    set.Add(k.Value);
                }
            }
            foreach (var key in _base.Violations.Keys)
            {
                var (i, j) = ParseKey(key);
                if (i is null || j is null) continue;
                _hotCells.Add(i.Value * Limits.Pack + j.Value);
                _hotDays.Add(j.Value);
            }
            foreach (var key in _base.NeedViolations.Keys)
            {
                var (_, j) = ParseKey(key);
                if (j is not null) _hotDays.Add(j.Value);
            }
        }

        private static (int?, int?) ParseKey(string key)
        {
            var pp = key.Split(',');
            var a = pp.Length > 0 ? KotlinInterop.ToIntOrNull(pp[0]) : null;
            var b = pp.Length > 1 ? KotlinInterop.ToIntOrNull(pp[1]) : null;
            return (a, b);
        }
        private string Nm(int i) => i >= 0 && i < _state.StaffList.Count ? _state.StaffList[i].Name : $"#{i}";
        private string Sym(int k)
        {
            if (k < 0) return "—";
            return k < _state.Shifts.Count ? _state.Shifts[k].Kigou : k.ToString();
        }
        private string Dlab(int j)
        {
            try
            {
                if (!DateOnly.TryParseExact(_state.StartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var parsed))
                    throw new FormatException($"'{_state.StartDate}' is not a valid yyyy-MM-dd date");
                var d = parsed.AddDays(j);
                return $"{d.Month}/{d.Day}";
            }
            catch (Exception)
            {
                return $"{j + 1}日";
            }
        }
        private List<(string Family, int Delta)> DiffOf(ViolationReport rep)
        {
            var outList = new List<(string Family, int Delta)>();
            foreach (var k in _base.Breakdown.Keys.Union(rep.Breakdown.Keys))
            {
                var d = (rep.Breakdown.TryGetValue(k, out var rv) ? rv : 0)
                    - (_base.Breakdown.TryGetValue(k, out var bv) ? bv : 0);
                if (d != 0) outList.Add((k, d));
            }
            return outList.OrderBy(pair => pair.Delta).ToList();
        }
        private bool TimeUp() => EngineClock.NowMs() - _start > _deadlineMs;
        private bool InFocus(int i) => _focus == null || i == _focus;
        private bool PairFocus(int i, int i2) => _focus == null || i == _focus || i2 == _focus;
        private List<int> TargetStaff() => _focus != null ? new List<int> { _focus.Value } : _countHot.ToList();
        private List<int> TargetDays() => _focus != null ? Enumerable.Range(0, _p.T).ToList() : _hotDays.ToList();

        /// <summary>ops を当てた盤面の report（必ず元へ戻す）。</summary>
        private ViolationReport EvalOps(IReadOnlyList<FixCell> ops)
        {
            var saved = new int[ops.Count];
            for (var idx = 0; idx < ops.Count; idx++) saved[idx] = _s[ops[idx].Staff][ops[idx].Day];
            foreach (var op in ops) _s[op.Staff][op.Day] = op.ToShift;
            var rep = UnifiedViolationChecker.Check(_state, _s);
            for (var idx = 0; idx < ops.Count; idx++) _s[ops[idx].Staff][ops[idx].Day] = saved[idx];
            return rep;
        }
        private void Record(FixKind kind, IReadOnlyList<FixCell> ops, string label, ViolationReport rep)
        {
            _found.Add(new Quad(
                new FixSuggestion(kind, ops, label, rep.Hard - _base.Hard, rep.Total - _base.Total, DiffOf(rep)),
                rep.Hard - _base.Hard, rep.Total - _base.Total, rep.WeightedScore - _base.WeightedScore));
        }
        /// <summary>ops をその場で適用→評価→復元。base より良ければ候補に追加。</summary>
        private void TryOps(FixKind kind, IReadOnlyList<FixCell> ops, string label)
        {
            var rep = EvalOps(ops);
            if (UnifiedViolationChecker.BetterReport(rep, _base)) Record(kind, ops, label, rep);
        }

        public List<FixSuggestion> Run(int maxResults)
        {
            SingleChanges();
            SameDaySwaps();
            MultiChanges();
            Chains();
            Windows();
            Rotations3();
            CrossDaySwaps();
            return Collect(maxResults);
        }

        /// <summary>Phase 1: 単一マス変更（広く）。</summary>
        private void SingleChanges()
        {
            for (var i = 0; i < _p.S; i++)
            {
                if (!InFocus(i)) continue;
                var allowed = _p.AllowedShiftsForStaff(i);
                for (var j = 0; j < _p.T; j++)
                {
                    if (_p.WishLocked(i, j)) continue;
                    var a = _s[i][j];
                    foreach (var k in allowed)
                    {
                        if (k == a || TimeUp()) continue;
                        TryOps(FixKind.Change, new[] { new FixCell(i, j, k) }, $"{Nm(i)} {Dlab(j)} 「{Sym(a)}」→「{Sym(k)}」");
                    }
                }
            }
        }

        /// <summary>Phase 2: 同日 2 人交換。</summary>
        private void SameDaySwaps()
        {
            for (var i = 0; i < _p.S; i++)
            {
                for (var i2 = i + 1; i2 < _p.S; i2++)
                {
                    if (!PairFocus(i, i2)) continue;
                    for (var j = 0; j < _p.T; j++)
                    {
                        if (TimeUp()) break;
                        if (_p.WishLocked(i, j) || _p.WishLocked(i2, j)) continue;
                        var a = _s[i][j];
                        var b = _s[i2][j];
                        if (a == b || !_p.MayPlace(i, b) || !_p.MayPlace(i2, a)) continue;
                        TryOps(FixKind.Swap, new[] { new FixCell(i, j, b), new FixCell(i2, j, a) },
                            $"{Nm(i)} 「{Sym(a)}」 ↔ {Nm(i2)} 「{Sym(b)}」（{Dlab(j)}）");
                    }
                }
            }
        }

        /// <summary>Phase 3: 同一スタッフの 2 マス同時変更（下限割れ当事者にターゲット）。</summary>
        private void MultiChanges()
        {
            foreach (var i in TargetStaff())
            {
                if (TimeUp()) break;
                var allowed = _p.AllowedShiftsForStaff(i);
                // 目標シフト = そのstaffの下限割れシフト ∪ 休（記号で解決した RestIdx）。なければ置けるシフト全部。
                //   [Android 3.475.0 の同期] 旧移植は休を index 0 決め打ちにし、担当可否も見ていなかった。
                var shortList = _shortShift.TryGetValue(i, out var ss) ? ss.ToList() : new List<int>();
                var targets = (shortList.Count == 0 ? allowed.ToList() : shortList.Append(_p.RestIdx).ToList())
                    .Distinct().Where(k => allowed.Contains(k)).ToList();
                var cells = new List<int>();
                for (var j = 0; j < _p.T; j++) if (!_p.WishLocked(i, j)) cells.Add(j);
                for (var a = 0; a < cells.Count; a++)
                {
                    if (TimeUp()) break;
                    for (var b = a + 1; b < cells.Count; b++)
                    {
                        if (TimeUp()) break;
                        var j1 = cells[a]; var j2 = cells[b];
                        var s1 = _s[i][j1]; var s2 = _s[i][j2];
                        foreach (var k1 in targets)
                        {
                            if (k1 == s1) continue;
                            foreach (var k2 in targets)
                            {
                                if (k2 == s2 || TimeUp()) continue;
                                TryOps(FixKind.ChangeMulti, new[] { new FixCell(i, j1, k1), new FixCell(i, j2, k2) },
                                    $"{Nm(i)} {Dlab(j1)}「{Sym(s1)}」→「{Sym(k1)}」＋{Dlab(j2)}「{Sym(s2)}」→「{Sym(k2)}」");
                            }
                        }
                    }
                }
            }
        }

        /// <summary>Phase 6: エジェクションチェーン（不足シフトを貪欲に最大 ChainRounds コマ充足。文書§2 玉突き）。</summary>
        private void Chains()
        {
            foreach (var i in TargetStaff())
            {
                if (TimeUp()) break;
                if (!_shortShift.TryGetValue(i, out var shorts)) continue;
                foreach (var x in shorts)
                {
                    if (TimeUp()) break;
                    if (!_p.MayPlace(i, x)) continue;   // [3.507.4] 上限 0 のシフトは最適化器と同じく置かない
                    var picked = new List<FixCell>();
                    var applied = new List<(int Day, int SavedShift)>();   // 復元用
                    while (picked.Count < Limits.ChainRounds && !TimeUp())
                    {
                        // 現在の積み上げ盤面のスコアを基準に、x へ変えて更に改善する可動コマを1つ選ぶ（単調改善を保証）
                        var bestRep = UnifiedViolationChecker.Check(_state, _s);
                        var bestJ = -1;
                        var bestSaved = -1;
                        for (var j = 0; j < _p.T; j++)
                        {
                            if (_p.WishLocked(i, j)) continue;
                            var a = _s[i][j];
                            if (a == x) continue;
                            _s[i][j] = x;
                            var rep = UnifiedViolationChecker.Check(_state, _s);
                            _s[i][j] = a;
                            if (UnifiedViolationChecker.BetterReport(rep, bestRep)) { bestRep = rep; bestJ = j; bestSaved = a; }
                        }
                        if (bestJ < 0) break;
                        _s[i][bestJ] = x;
                        picked.Add(new FixCell(i, bestJ, x));
                        applied.Add((bestJ, bestSaved));
                    }
                    foreach (var (j, sv) in applied) _s[i][j] = sv;   // 復元
                    // 2コマ以上のときだけ採用（1コマは単一変更で既出）。base 改善を再確認。
                    if (picked.Count >= 2) TryOps(FixKind.Chain, picked, $"（連鎖）{Nm(i)} の「{Sym(x)}」不足を{picked.Count}コマ補充");
                }
            }
        }

        /// <summary>Phase 7: ミニ再最適化（1 日 × 最大 WindowStaff 名を総当たりで最適割当。文書§4 マスヒューリスティクスのミニ版）。</summary>
        private void Windows()
        {
            var windows = 0;
            foreach (var j in TargetDays())
            {
                if (TimeUp() || windows >= Limits.WindowDays) break;
                var movable = new List<int>();
                for (var i = 0; i < _p.S; i++) if (!_p.WishLocked(i, j)) movable.Add(i);
                if (movable.Count < 2) continue;
                // 違反関与(countHot)を優先、focus があれば先頭に。
                var ranked = movable.OrderByDescending(x => _countHot.Contains(x)).ToList();
                var chosen0 = _focus != null
                    ? ranked.Where(x => x == _focus).Concat(ranked.Where(x => x != _focus)).ToList()
                    : ranked;
                var n = Math.Min(Limits.WindowStaff, chosen0.Count);
                var cells0 = chosen0.Take(Limits.WindowStaff).ToList();
                var opts0 = cells0.Select(c => _p.AllowedShiftsForStaff(c).ToList()).ToList();
                long Combos(int m)
                {
                    var c = 1L;
                    for (var t = 0; t < m; t++) c *= opts0[t].Count;
                    return c;
                }
                while (n > 2 && Combos(n) > Limits.WindowCombos) n--;
                if (n < 2 || Combos(n) > Limits.WindowCombos) continue;
                var cells = cells0.Take(n).ToList();
                var cellOpts = opts0.Take(n).ToList();
                var cur = new int[n];
                for (var c = 0; c < n; c++) cur[c] = _s[cells[c]][j];
                windows++;
                var sizes = new int[n];
                for (var c = 0; c < n; c++) sizes[c] = cellOpts[c].Count;
                var idx = new int[n];
                ViolationReport? bestComboRep = null;
                int[]? bestCombo = null;
                while (true)
                {
                    for (var c = 0; c < n; c++) _s[cells[c]][j] = cellOpts[c][idx[c]];
                    var rep = UnifiedViolationChecker.Check(_state, _s);
                    if (UnifiedViolationChecker.BetterReport(rep, _base) && (bestComboRep == null || UnifiedViolationChecker.BetterReport(rep, bestComboRep)))
                    {
                        bestComboRep = rep;
                        var combo = new int[n];
                        for (var c = 0; c < n; c++) combo[c] = cellOpts[c][idx[c]];
                        bestCombo = combo;
                    }
                    var cc = 0;
                    while (cc < n)
                    {
                        idx[cc]++;
                        if (idx[cc] < sizes[cc]) break;
                        idx[cc] = 0;
                        cc++;
                    }
                    if (cc == n || TimeUp()) break;
                }
                for (var c = 0; c < n; c++) _s[cells[c]][j] = cur[c];   // 復元
                if (bestComboRep != null && bestCombo != null)
                {
                    var ops = new List<FixCell>();
                    for (var c = 0; c < n; c++) if (bestCombo[c] != cur[c]) ops.Add(new FixCell(cells[c], j, bestCombo[c]));
                    // 最良組合せの report をそのまま使う（旧: 同じ盤面を作り直してもう 1 回評価していた）。
                    if (ops.Count >= 2) Record(FixKind.Window, ops, $"（再最適化）{Dlab(j)} の{ops.Count}名を最適割当", bestComboRep);
                }
            }
        }

        /// <summary>Phase 4: 同日 3 人巡回交換（被覆不変・違反日にターゲット）。a を最小に固定して重複列挙を避ける。</summary>
        private void Rotations3()
        {
            foreach (var j in TargetDays())
            {
                if (TimeUp()) break;
                for (var a = 0; a < _p.S; a++)
                {
                    if (TimeUp()) break;
                    if (_p.WishLocked(a, j)) continue;
                    for (var b = a + 1; b < _p.S; b++)
                    {
                        if (_p.WishLocked(b, j)) continue;
                        for (var c = a + 1; c < _p.S; c++)
                        {
                            if (c == b || _p.WishLocked(c, j) || TimeUp()) continue;
                            if (_focus != null && a != _focus && b != _focus && c != _focus) continue;
                            var sa = _s[a][j]; var sb = _s[b][j]; var sc = _s[c][j];
                            if (sa == sb && sb == sc) continue;
                            // 巡回: a<-sb, b<-sc, c<-sa
                            if (!_p.MayPlace(a, sb) || !_p.MayPlace(b, sc) || !_p.MayPlace(c, sa)) continue;
                            TryOps(FixKind.SwapMulti,
                                new[] { new FixCell(a, j, sb), new FixCell(b, j, sc), new FixCell(c, j, sa) },
                                $"（3人）{Nm(a)}・{Nm(b)}・{Nm(c)} を {Dlab(j)} で入替");
                        }
                    }
                }
            }
        }

        /// <summary>Phase 5: 別日交換（違反関与セルを起点）。同日は Phase 2 が網羅済みなので飛ばす。</summary>
        private void CrossDaySwaps()
        {
            var anchors = new List<int>(_hotCells);
            foreach (var i in TargetStaff())
                for (var j = 0; j < _p.T; j++)
                    if (!_p.WishLocked(i, j)) anchors.Add(i * Limits.Pack + j);
            var seenAnchor = new HashSet<int>();
            foreach (var packed in anchors)
            {
                if (!seenAnchor.Add(packed) || TimeUp()) continue;
                var i1 = packed / Limits.Pack; var j1 = packed % Limits.Pack;
                if (i1 < 0 || i1 >= _p.S || j1 < 0 || j1 >= _p.T || _p.WishLocked(i1, j1)) continue;
                var a = _s[i1][j1];
                for (var i2 = 0; i2 < _p.S; i2++)
                {
                    if (TimeUp()) break;
                    if (_focus != null && i1 != _focus && i2 != _focus) continue;
                    for (var j2 = 0; j2 < _p.T; j2++)
                    {
                        if (j2 == j1) continue;
                        if (_p.WishLocked(i2, j2) || TimeUp()) continue;
                        var b = _s[i2][j2];
                        if (a == b || !_p.MayPlace(i1, b) || !_p.MayPlace(i2, a)) continue;
                        var label = i1 == i2
                            ? $"{Nm(i1)} {Dlab(j1)}「{Sym(a)}」 ↔ {Dlab(j2)}「{Sym(b)}」（別日）"
                            : $"{Nm(i1)} {Dlab(j1)}「{Sym(a)}」 ↔ {Nm(i2)} {Dlab(j2)}「{Sym(b)}」（別日）";
                        TryOps(FixKind.SwapXDay, new[] { new FixCell(i1, j1, b), new FixCell(i2, j2, a) }, label);
                    }
                }
            }
        }

        /// <summary>効果順（hard→weighted→total＝BetterReport と同順）に並べ、セル限定と盤面変化の実体による重複排除で絞る。</summary>
        private List<FixSuggestion> Collect(int maxResults)
        {
            var found = _found.OrderBy(q => q.DHard).ThenBy(q => q.DWeighted).ThenBy(q => q.DTotal).ToList();
            var fShift = _focusShift;
            // [セル限定] 押したセル(focus職員×focusシフト)からシフトを移す手か、そのシフトへ移す手だけ。
            bool TouchesFocusCell(FixSuggestion sug)
            {
                if (fShift == null) return true;
                return sug.Ops.Any(c =>
                {
                    if (_focus != null && c.Staff != _focus) return false;
                    if (c.ToShift == fShift) return true;
                    if (c.Staff < 0 || c.Staff >= _s.Length) return false;
                    var row = _s[c.Staff];
                    return c.Day >= 0 && c.Day < row.Length && row[c.Day] == fShift;
                });
            }
            // 署名＝無変化の脚を除いた (staff, day, toShift) の正規順。kind や ops の列挙順に依らず「最終的にどのセルが
            // どの値になるか」で重複を判定する（経緯は Android history 3.202.0/3.475.0。旧移植は署名に日が無く別の日の同じ手を同一視していた）。
            var seen = new HashSet<string>();
            var result = new List<FixSuggestion>();
            foreach (var q in found)
            {
                var sug = q.Sug;
                if (!TouchesFocusCell(sug)) continue;
                var realOps = sug.Ops.Where(op => op.ToShift != _s[op.Staff][op.Day]).ToList();
                if (realOps.Count == 0) continue;
                var sig = string.Join("|",
                    realOps.OrderBy(op => op.Staff).ThenBy(op => op.Day).Select(op => $"{op.Staff}.{op.Day}.{op.ToShift}"));
                if (seen.Add(sig)) result.Add(sug);
                if (result.Count >= maxResults) break;
            }
            return result;
        }
    }
}
