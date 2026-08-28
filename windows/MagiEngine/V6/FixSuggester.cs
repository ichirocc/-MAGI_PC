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
        var s = ScheduleUtil.NormalizeSchedule(schedule, p);
        // Kotlin原本の変数名は `base`（C#の予約語のため `baseReport` へ改名。挙動は不変）。
        var baseReport = UnifiedViolationChecker.Check(state, s);

        string Nm(int i) => i >= 0 && i < state.StaffList.Count ? state.StaffList[i].Name : $"#{i}";
        string Sym(int k)
        {
            if (k < 0) return "—";
            return k < state.Shifts.Count ? state.Shifts[k].Kigou : k.ToString();
        }
        string Dlab(int j)
        {
            try
            {
                if (!DateOnly.TryParseExact(state.StartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var parsed))
                    throw new FormatException($"'{state.StartDate}' is not a valid yyyy-MM-dd date");
                var d = parsed.AddDays(j);
                return $"{d.Month}/{d.Day}";
            }
            catch (Exception)
            {
                return $"{j + 1}日";
            }
        }
        List<(string Family, int Delta)> DiffOf(ViolationReport rep)
        {
            var outList = new List<(string Family, int Delta)>();
            foreach (var k in baseReport.Breakdown.Keys.Union(rep.Breakdown.Keys))
            {
                var d = (rep.Breakdown.TryGetValue(k, out var rv) ? rv : 0)
                    - (baseReport.Breakdown.TryGetValue(k, out var bv) ? bv : 0);
                if (d != 0) outList.Add((k, d));
            }
            return outList.OrderBy(pair => pair.Delta).ToList();
        }

        var found = new List<Quad>();
        var start = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var evals = 0;
        bool TimeUp() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - start > deadlineMs;

        // ops をその場で適用→評価→復元（割当を作らず高速）。改善なら候補に追加。
        void TryOps(FixKind kind, IReadOnlyList<FixCell> ops, string label)
        {
            var saved = new int[ops.Count];
            for (var idx = 0; idx < ops.Count; idx++) saved[idx] = s[ops[idx].Staff][ops[idx].Day];
            foreach (var op in ops) s[op.Staff][op.Day] = op.ToShift;
            var rep = UnifiedViolationChecker.Check(state, s);
            evals++;
            for (var idx = 0; idx < ops.Count; idx++) s[ops[idx].Staff][ops[idx].Day] = saved[idx];
            var better = UnifiedViolationChecker.BetterReport(rep, baseReport);
            if (better)
            {
                found.Add(new Quad(
                    new FixSuggestion(kind, ops, label, rep.Hard - baseReport.Hard, rep.Total - baseReport.Total, DiffOf(rep)),
                    rep.Hard - baseReport.Hard, rep.Total - baseReport.Total, rep.WeightedScore - baseReport.WeightedScore));
            }
        }

        var focus = focusStaff;
        bool InFocus(int i) => focus == null || i == focus;
        bool PairFocus(int i, int i2) => focus == null || i == focus || i2 == focus;

        // 違反に関与する staff / day / shift をターゲット集合として抽出。
        var countHot = new HashSet<int>();                 // 回数違反(low/high)のあるstaff
        var shortShift = new Dictionary<int, HashSet<int>>();  // staff -> 下限割れのシフト集合
        foreach (var (key, cls) in baseReport.CountViolations)
        {
            var pp = key.Split(',');
            var i = pp.Length > 0 ? KotlinInterop.ToIntOrNull(pp[0]) : null;
            if (i == null) continue;
            var k = pp.Length > 1 ? KotlinInterop.ToIntOrNull(pp[1]) : null;
            if (k == null) continue;
            countHot.Add(i.Value);
            if (cls == "vio-low")
            {
                if (!shortShift.TryGetValue(i.Value, out var set)) { set = new HashSet<int>(); shortShift[i.Value] = set; }
                set.Add(k.Value);
            }
        }
        var hotCells = new List<int>();                    // pack i*1000+j（セル違反）
        var hotDays = new HashSet<int>();
        foreach (var key in baseReport.Violations.Keys)
        {
            var pp = key.Split(',');
            var i = pp.Length > 0 ? KotlinInterop.ToIntOrNull(pp[0]) : null;
            if (i == null) continue;
            var j = pp.Length > 1 ? KotlinInterop.ToIntOrNull(pp[1]) : null;
            if (j == null) continue;
            hotCells.Add(i.Value * 1000 + j.Value);
            hotDays.Add(j.Value);
        }
        foreach (var key in baseReport.NeedViolations.Keys)
        {
            var pp = key.Split(',');
            if (pp.Length <= 1) continue;
            var j = KotlinInterop.ToIntOrNull(pp[1]);
            if (j != null) hotDays.Add(j.Value);
        }

        // ---- Phase 1: 単一マス変更（広く）----
        {
            for (var i = 0; i < p.S; i++)
            {
                if (!InFocus(i)) continue;
                var allowed = p.AllowedShiftsForStaff(i);
                for (var j = 0; j < p.T; j++)
                {
                    if (p.WishLocked(i, j)) continue;
                    var a = s[i][j];
                    foreach (var k in allowed)
                    {
                        if (k == a || TimeUp()) continue;
                        TryOps(FixKind.Change, new[] { new FixCell(i, j, k) },
                            $"{Nm(i)} {Dlab(j)} 「{Sym(a)}」→「{Sym(k)}」");
                    }
                }
            }
        }
        // ---- Phase 2: 同日2人交換 ----
        {
            for (var i = 0; i < p.S; i++)
            {
                for (var i2 = i + 1; i2 < p.S; i2++)
                {
                    if (!PairFocus(i, i2)) continue;
                    for (var j = 0; j < p.T; j++)
                    {
                        if (TimeUp()) break;
                        if (p.WishLocked(i, j) || p.WishLocked(i2, j)) continue;
                        var a = s[i][j];
                        var b = s[i2][j];
                        if (a == b || !p.CanDo(i, b) || !p.CanDo(i2, a)) continue;
                        TryOps(FixKind.Swap, new[] { new FixCell(i, j, b), new FixCell(i2, j, a) },
                            $"{Nm(i)} 「{Sym(a)}」 ↔ {Nm(i2)} 「{Sym(b)}」（{Dlab(j)}）");
                    }
                }
            }
        }
        // ---- Phase 3: 複数マス変更（同一スタッフ・下限割れ当事者にターゲット）----
        {
            var targetStaff = focus != null ? new List<int> { focus.Value } : countHot.ToList();
            foreach (var i in targetStaff)
            {
                if (TimeUp()) break;
                var allowed = p.AllowedShiftsForStaff(i);
                // 目標シフト = そのstaffの下限割れシフト ∪ 休(0)。なければ単一マス候補を流用するため全許可。
                var shortList = shortShift.TryGetValue(i, out var ss) ? ss.ToList() : new List<int>();
                var targets = (shortList.Count == 0 ? allowed.ToList() : shortList.Append(0).ToList())
                    .Distinct().ToList();
                var cells = new List<int>();
                for (var j = 0; j < p.T; j++) if (!p.WishLocked(i, j)) cells.Add(j);
                for (var a = 0; a < cells.Count; a++)
                {
                    if (TimeUp()) break;
                    for (var b = a + 1; b < cells.Count; b++)
                    {
                        if (TimeUp()) break;
                        var j1 = cells[a]; var j2 = cells[b];
                        var s1 = s[i][j1]; var s2 = s[i][j2];
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
        // ---- Phase 6: エジェクションチェーン（不足シフトを貪欲に最大3コマ充足。文書§2 玉突き）----
        {
            var chainStaff = focus != null ? new List<int> { focus.Value } : countHot.ToList();
            foreach (var i in chainStaff)
            {
                if (TimeUp()) break;
                if (!shortShift.TryGetValue(i, out var shorts)) continue;
                foreach (var x in shorts)
                {
                    if (TimeUp()) break;
                    var picked = new List<FixCell>();
                    var applied = new List<(int Day, int SavedShift)>();   // 復元用
                    var rounds = 0;
                    while (rounds < 3 && !TimeUp())
                    {
                        // 現在の積み上げ盤面のスコアを基準に、x へ変えて更に改善する可動コマを1つ選ぶ（単調改善を保証）
                        var curRep = UnifiedViolationChecker.Check(state, s); evals++;
                        var bestJ = -1;
                        var bestSaved = -1;
                        ViolationReport? bestRep = curRep;
                        var improved = false;
                        for (var j = 0; j < p.T; j++)
                        {
                            if (p.WishLocked(i, j)) continue;
                            var a = s[i][j];
                            if (a == x) continue;
                            s[i][j] = x;
                            var rep = UnifiedViolationChecker.Check(state, s); evals++;
                            s[i][j] = a;
                            var take = bestRep == null || UnifiedViolationChecker.BetterReport(rep, bestRep);
                            if (take) { improved = true; bestRep = rep; bestJ = j; bestSaved = a; }
                        }
                        if (!improved || bestJ < 0) break;
                        s[i][bestJ] = x;
                        picked.Add(new FixCell(i, bestJ, x));
                        applied.Add((bestJ, bestSaved));
                        rounds++;
                    }
                    foreach (var (j, sv) in applied) s[i][j] = sv;   // 復元
                    // 2コマ以上のときだけ採用（1コマは単一変更で既出）。base 改善を再確認。
                    if (picked.Count >= 2)
                    {
                        var saved = new int[picked.Count];
                        for (var idx = 0; idx < picked.Count; idx++) saved[idx] = s[picked[idx].Staff][picked[idx].Day];
                        foreach (var op in picked) s[op.Staff][op.Day] = op.ToShift;
                        var rep = UnifiedViolationChecker.Check(state, s); evals++;
                        for (var idx = 0; idx < picked.Count; idx++) s[picked[idx].Staff][picked[idx].Day] = saved[idx];
                        var better = UnifiedViolationChecker.BetterReport(rep, baseReport);
                        if (better)
                        {
                            found.Add(new Quad(
                                new FixSuggestion(FixKind.Chain, picked,
                                    $"（連鎖）{Nm(i)} の「{Sym(x)}」不足を{picked.Count}コマ補充",
                                    rep.Hard - baseReport.Hard, rep.Total - baseReport.Total, DiffOf(rep)),
                                rep.Hard - baseReport.Hard, rep.Total - baseReport.Total, rep.WeightedScore - baseReport.WeightedScore));
                        }
                    }
                }
            }
        }
        // ---- Phase 7: ミニ再最適化（1日×最大4名を総当たりで最適割当。文書§4 マスヒューリスティクスのミニ版）----
        {
            var wDays = focus != null ? Enumerable.Range(0, p.T).ToList() : hotDays.ToList();
            var windows = 0;
            foreach (var j in wDays)
            {
                if (TimeUp() || windows >= 5) break;
                var movable = new List<int>();
                for (var i = 0; i < p.S; i++) if (!p.WishLocked(i, j)) movable.Add(i);
                if (movable.Count < 2) continue;
                // 違反関与(countHot)を優先、focus があれば先頭に。最大4名。
                var ranked = movable.OrderByDescending(x => countHot.Contains(x)).ToList();
                var chosen0 = focus != null
                    ? ranked.Where(x => x == focus).Concat(ranked.Where(x => x != focus)).ToList()
                    : ranked;
                var n = Math.Min(4, chosen0.Count);
                var cells0 = chosen0.Take(4).ToList();
                var opts0 = cells0.Select(c => p.AllowedShiftsForStaff(c).ToList()).ToList();
                long Combos(int m)
                {
                    var c = 1L;
                    for (var t = 0; t < m; t++) c *= opts0[t].Count;
                    return c;
                }
                while (n > 2 && Combos(n) > 20000L) n--;
                if (n < 2 || Combos(n) > 20000L) continue;
                var cells = cells0.Take(n).ToList();
                var cellOpts = opts0.Take(n).ToList();
                var cur = new int[n];
                for (var c = 0; c < n; c++) cur[c] = s[cells[c]][j];
                windows++;
                var sizes = new int[n];
                for (var c = 0; c < n; c++) sizes[c] = cellOpts[c].Count;
                var idx = new int[n];
                ViolationReport? bestComboRep = null;   // [3.336.0 移植元] 3キー手書き→BetterReport 委譲
                int[]? bestCombo = null;
                while (true)
                {
                    for (var c = 0; c < n; c++) s[cells[c]][j] = cellOpts[c][idx[c]];
                    var rep = UnifiedViolationChecker.Check(state, s); evals++;
                    var better = UnifiedViolationChecker.BetterReport(rep, baseReport);
                    if (better)
                    {
                        var take = bestComboRep == null || UnifiedViolationChecker.BetterReport(rep, bestComboRep);
                        if (take)
                        {
                            bestComboRep = rep;
                            var combo = new int[n];
                            for (var c = 0; c < n; c++) combo[c] = cellOpts[c][idx[c]];
                            bestCombo = combo;
                        }
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
                for (var c = 0; c < n; c++) s[cells[c]][j] = cur[c];   // 復元
                if (bestComboRep != null && bestCombo != null)
                {
                    var ops = new List<FixCell>();
                    for (var c = 0; c < n; c++) if (bestCombo[c] != cur[c]) ops.Add(new FixCell(cells[c], j, bestCombo[c]));
                    if (ops.Count >= 2)
                    {
                        foreach (var op in ops) s[op.Staff][op.Day] = op.ToShift;
                        var rep = UnifiedViolationChecker.Check(state, s); evals++;
                        foreach (var op in ops) s[op.Staff][op.Day] = cur[cells.IndexOf(op.Staff)];
                        found.Add(new Quad(
                            new FixSuggestion(FixKind.Window, ops, $"（再最適化）{Dlab(j)} の{ops.Count}名を最適割当",
                                rep.Hard - baseReport.Hard, rep.Total - baseReport.Total, DiffOf(rep)),
                            rep.Hard - baseReport.Hard, rep.Total - baseReport.Total, rep.WeightedScore - baseReport.WeightedScore));
                    }
                }
            }
        }
        // ---- Phase 4: 同日3人巡回交換（被覆不変・違反日にターゲット）----
        {
            var days3 = focus != null ? Enumerable.Range(0, p.T).ToList() : hotDays.ToList();
            foreach (var j in days3)
            {
                if (TimeUp()) break;
                for (var a = 0; a < p.S; a++)
                {
                    if (TimeUp()) break;
                    if (p.WishLocked(a, j)) continue;
                    for (var b = 0; b < p.S; b++)
                    {
                        if (b == a || p.WishLocked(b, j)) continue;
                        for (var c = 0; c < p.S; c++)
                        {
                            if (c == a || c == b || p.WishLocked(c, j) || TimeUp()) continue;
                            if (focus != null && a != focus && b != focus && c != focus) continue;
                            // 重複列挙を避けるため a を最小に固定
                            if (a > b || a > c) continue;
                            var sa = s[a][j]; var sb = s[b][j]; var sc = s[c][j];
                            if (sa == sb && sb == sc) continue;
                            // 巡回: a<-sb, b<-sc, c<-sa
                            if (!p.CanDo(a, sb) || !p.CanDo(b, sc) || !p.CanDo(c, sa)) continue;
                            TryOps(FixKind.SwapMulti,
                                new[] { new FixCell(a, j, sb), new FixCell(b, j, sc), new FixCell(c, j, sa) },
                                $"（3人）{Nm(a)}・{Nm(b)}・{Nm(c)} を {Dlab(j)} で入替");
                        }
                    }
                }
            }
        }
        // ---- Phase 5: 別日交換（違反関与セルを起点）----
        {
            var anchors = new List<int>();
            anchors.AddRange(hotCells);
            var anchorStaff = focus != null ? new List<int> { focus.Value } : countHot.ToList();
            foreach (var i in anchorStaff)
                for (var j = 0; j < p.T; j++)
                    if (!p.WishLocked(i, j)) anchors.Add(i * 1000 + j);
            var seenAnchor = new HashSet<int>();
            foreach (var packed in anchors)
            {
                if (!seenAnchor.Add(packed) || TimeUp()) continue;
                var i1 = packed / 1000; var j1 = packed % 1000;
                if (i1 < 0 || i1 >= p.S || j1 < 0 || j1 >= p.T || p.WishLocked(i1, j1)) continue;
                var a = s[i1][j1];
                for (var i2 = 0; i2 < p.S; i2++)
                {
                    if (TimeUp()) break;
                    if (focus != null && i1 != focus && i2 != focus) continue;
                    for (var j2 = 0; j2 < p.T; j2++)
                    {
                        if (j2 == j1 && i2 == i1) continue;
                        if (p.WishLocked(i2, j2) || TimeUp()) continue;
                        var b = s[i2][j2];
                        if (a == b || !p.CanDo(i1, b) || !p.CanDo(i2, a)) continue;
                        var label = i1 == i2
                            ? $"{Nm(i1)} {Dlab(j1)}「{Sym(a)}」 ↔ {Dlab(j2)}「{Sym(b)}」（別日）"
                            : $"{Nm(i1)} {Dlab(j1)}「{Sym(a)}」 ↔ {Nm(i2)} {Dlab(j2)}「{Sym(b)}」（別日）";
                        TryOps(FixKind.SwapXDay, new[] { new FixCell(i1, j1, b), new FixCell(i2, j2, a) }, label);
                    }
                }
            }
        }

        // 効果順（必須減 > 合計減 > 重み減）。同型の手は1つに絞り多様性確保。
        // [3.287.0 keep-best統一 移植元] hard→weighted→total
        found = found.OrderBy(q => q.DHard).ThenBy(q => q.DWeighted).ThenBy(q => q.DTotal).ToList();
        // [セル限定] focusShift 指定時、押したセル(focus職員×focusシフト)に効く手だけに絞る。
        //   そのセルからシフトを移す(原状=focusShift)か、そのシフトへ移す(toShift=focusShift)手を採用。
        var fShift = focusShift;
        bool TouchesFocusCell(FixSuggestion sug)
        {
            if (fShift == null) return true;
            return sug.Ops.Any(c =>
            {
                if (focusStaff != null && c.Staff != focusStaff) return false;
                if (c.ToShift == fShift) return true;
                if (c.Staff < 0 || c.Staff >= s.Length) return false;
                var row = s[c.Staff];
                return c.Day >= 0 && c.Day < row.Length && row[c.Day] == fShift;
            });
        }
        // [重複排除の頑健化] 旧署名(kind名+ops列挙順)は3種の見落としで実質同一の盤面変化を複数回
        // 表示していた: ①SwapXDay は起点(i1,j1)/(i2,j2)どちらから見るかで ops が逆順生成され別署名化
        // ②同日の Swap と kind違いなだけの SwapXDay（Phase5 は j2==j1 も除外していない＝「別日」ラベルが
        // 実は同日）③SwapMulti の退化3巡回(1脚が無変化=実質2人交換)や Chain(同一shiftへの2コマ)が
        // ChangeMulti 等と同じ盤面変化になり得る、のいずれも kind をまたいで重複表示されていた。
        // ここに至る時点で s は Phase1-7 の全 TryOps が適用→復元を徹底しているため元の(normalize後)
        // 盤面に一致する＝「toShift==sの現在値」は実質no-opの脚と判定できる。no-op脚を除外し、
        // 残りを (staff,day) で正規化した順序で署名化することで、kind やops列挙順に依らず
        // 「最終的にどのセルがどの値になるか」という盤面変化の実体だけで重複を判定する。
        var seen = new HashSet<string>();
        var result = new List<FixSuggestion>();
        foreach (var q in found)
        {
            var sug = q.Sug;
            if (!TouchesFocusCell(sug)) continue;
            var realOps = sug.Ops.Where(op => op.ToShift != s[op.Staff][op.Day]).ToList();
            if (realOps.Count == 0) continue;   // 全脚が無変化＝実質no-op（表示する意味がない）
            var sig = string.Join("|",
                realOps.OrderBy(op => op.Staff).ThenBy(op => op.Day).Select(op => $"{op.Staff}.{op.ToShift}"));
            if (seen.Add(sig)) result.Add(sug);
            if (result.Count >= maxResults) break;
        }
        return result;
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
}
