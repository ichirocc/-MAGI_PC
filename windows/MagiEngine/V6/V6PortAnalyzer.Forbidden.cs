using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>禁止連続(c3n)違反 run の1セルの脱出可否分類。</summary>
public enum ForbiddenCellEscape
{
    /// <summary>安全な代替シフトが存在（適用すれば HARD が厳密に減る＝探索未到達の可能性）。</summary>
    Free,
    /// <summary>直接は離脱元シフトが covU 化するが、玉突き連鎖（<see cref="V6SearchOperators.FindCovUChain"/>）で埋め直せることを実証済み。</summary>
    Chain,
    /// <summary>代替は全て新たな禁止連続を作るが、隣接日調整（<see cref="V6SearchOperators.TryFixForbiddenRunViaAdjacentDay"/>）で崩せることを実証済み。</summary>
    Adjacent,
    /// <summary>本人の希望で固定（動かすと pref(9000)&gt;c3n(7000) の悪化＝isBetter が正しく却下する）。</summary>
    Pinned,
    /// <summary>全ての代替が塞がっている（新たな禁止連続・covU受け皿なし・代替シフトなし）。</summary>
    Blocked,
}

/// <summary>禁止連続(c3n)違反 run 1件ぶんのセル別診断。</summary>
public sealed record ForbiddenRunCell(
    int DayIndex,
    string DayLabel,
    string ShiftSymbol,
    ForbiddenCellEscape Escape,
    /// <summary>分類の根拠（代替の内訳件数など）。</summary>
    string Detail);

/// <summary>禁止連続(c3n)違反 run 1件の診断（<see cref="CoverageShortfall"/>/<see cref="CoverageSurplus"/> と対の存在）。</summary>
public sealed record ForbiddenRunDiag(
    int StaffIndex,
    string StaffName,
    int StartDay,
    /// <summary>例: "Cｱ→Aｱ"（禁止パターンの記号列）。</summary>
    string SeqLabel,
    IReadOnlyList<ForbiddenRunCell> Cells,
    /// <summary>run 全体の判定と次の一手の案内。</summary>
    string Hint)
{
    public bool Escapable => Cells.Any(c =>
        c.Escape == ForbiddenCellEscape.Free || c.Escape == ForbiddenCellEscape.Chain ||
        c.Escape == ForbiddenCellEscape.Adjacent);
}

/// <summary>
/// [3.280.0] 禁止連続(c3n, HARD)の「なぜ崩せないか」診断。CoverageDiag（covU/covO）と対。
/// 実機で c3n=1 が HARD 専任ワーカー67エポックでも不動だった事例（2026-12 データ・アリフ Cｱ→Aｱ）で、
/// 「構造的に不能」か「探索漏れ」かをログから判別できなかった穴を埋める。読取専用・スコア不変。
/// </summary>
public sealed record ForbiddenRunDiagnosis(
    int TotalRuns,
    IReadOnlyList<ForbiddenRunDiag> Runs)
{
    public bool HasRuns => TotalRuns > 0;

    /// <summary>全 run が構造的に塞がっている（＝このデータ・希望のままでは c3n を 0 にできない）。</summary>
    public bool AllBlocked => HasRuns && Runs.All(r => !r.Escapable);

    /// <summary>診断ログ（エクスポートされる「MAGI ログ」に載る形式の文字列）。</summary>
    public IReadOnlyList<string> LogLines()
    {
        if (!HasRuns) return Array.Empty<string>();
        var lines = new List<string>();
        lines.Add($"[W] ForbiddenDiag: 禁止連続(c3n) {TotalRuns}件 — なぜ崩せないか");
        foreach (var r in Runs.Take(6))
        {
            var cellsTxt = string.Join(" / ", r.Cells.Select(c =>
            {
                var tag = c.Escape switch
                {
                    ForbiddenCellEscape.Free => "崩せる",
                    ForbiddenCellEscape.Chain => "玉突きで崩せる",
                    ForbiddenCellEscape.Adjacent => "隣接日調整で崩せる",
                    ForbiddenCellEscape.Pinned => "希望固定",
                    ForbiddenCellEscape.Blocked => "塞がり",
                    _ => c.Escape.ToString(),
                };
                return $"{c.DayLabel}={c.ShiftSymbol}:{tag}({c.Detail})";
            }));
            lines.Add($"[W] ForbiddenDiag: {r.StaffName} {r.SeqLabel} — {cellsTxt}");
            lines.Add($"[W] ForbiddenDiag: {r.StaffName} {r.SeqLabel} → {r.Hint}");
        }
        if (Runs.Count > 6) lines.Add($"[W] ForbiddenDiag: ほか{Runs.Count - 6}件");
        return lines;
    }
}

public static partial class V6PortAnalyzer
{
    /// <summary>
    /// [3.280.0] 禁止連続(c3n)の「なぜ崩せないか」診断。<see cref="DiagnoseCoverage"/> と同じ設計思想:
    /// エンジンは変更せず現在の解だけを読み取り、違反 run の各セルについて「単一セル変更で崩せるか」を
    /// HARD 意味論（c3n 正味減・pref・covU 離脱穴）で厳密に分類する。
    ///  - Free: 安全な代替あり＝適用すれば HARD が厳密に減る（isBetter は必ず採用）＝探索未到達の可能性。
    ///  - Chain: 離脱元が covU 化するが FindCovUChain（探索本体と同一関数・8 seed）で埋め直せることを実証。
    ///  - Adjacent: 代替は全て新たな禁止連続を作るが、隣接日調整（TryFixForbiddenRunViaAdjacentDay=
    ///    探索本体と同一関数）で崩せることを実証。
    ///  - Pinned: 本人希望どおりのセルで、かつどの代替も<b>正味の HARD を減らせない</b>＝isBetter が正しく却下。
    ///    （3.311.0 で厳密化。旧実装は希望が一致した時点で無条件に固定扱いしており、1セルが複数の
    ///    禁止連続 fire に関与する局面で偽の壁を作っていた。）
    ///  - Blocked: 全代替が「新たな禁止連続」か「covU 受け皿なし」＝この希望・担当のままでは崩せない。
    /// 3.263.0 の教訓（「玉突きが必要」と楽観的に言うだけでは壁を誤解させる）に従い、Chain/Adjacent は
    /// 実際に探索本体の関数で成立を確認してからそう名乗る。読取専用・スコアリング不変。
    /// </summary>
    public static ForbiddenRunDiagnosis DiagnoseForbiddenRuns(
        MagiState state,
        int[][]? schedule = null)
    {
        var sched = schedule ?? state.Schedule.ToIntArray2D();
        var p = ScheduleUtil.CachedProblem(state);
        var norm = ScheduleUtil.NormalizeSchedule(sched, p);
        var cov = ScheduleUtil.Coverage(p, norm);
        var runs = new List<ForbiddenRunDiag>();
        var seen = new HashSet<string>();   // 重複ルール（DuplicateSeq）由来の同一 run を1件に集約
        foreach (var c in p.Cons3n)
        {
            var seq = c.Seq;
            var d = seq.Length;
            if (d == 0 || d > p.T) continue;
            var seqLabel = string.Join("→", seq.Select(k => ShiftSym(state, k)));
            for (var i = 0; i < p.S; i++)
            {
                var j0 = 0;
                while (j0 <= p.T - d)
                {
                    var z = 0;
                    for (var l = 0; l < d; l++) if (norm[i][j0 + l] == seq[l]) z++;
                    if (z != d) { j0++; continue; }   // checker の forbidden 窓完全一致と同一意味論
                    var key = $"{i},{j0},{seqLabel}";
                    if (!seen.Add(key)) { j0++; continue; }
                    var cells = new List<ForbiddenRunCell>();
                    for (var l = 0; l < d; l++)
                    {
                        var j = j0 + l;
                        var cur = norm[i][j];
                        cells.Add(DiagnoseForbiddenCell(state, p, norm, cov, i, j, cur));
                    }
                    var pinnedDays = string.Join("・",
                        cells.Where(x => x.Escape == ForbiddenCellEscape.Pinned).Select(x => x.DayLabel));
                    string hint;
                    if (cells.Any(x => x.Escape == ForbiddenCellEscape.Free))
                    {
                        hint = "安全に崩せる手が存在します（適用すれば必須違反が減る＝探索未到達の可能性。" +
                            "勤務表の該当セルから『直し方を探す』で解消を試せます）";
                    }
                    else if (cells.Any(x => x.Escape == ForbiddenCellEscape.Chain || x.Escape == ForbiddenCellEscape.Adjacent))
                    {
                        hint = "単独の1手では崩せず、玉突き連鎖/隣接日調整の多段手でのみ崩せます" +
                            "（探索は候補を持っています＝再実行で解消し得ます）";
                    }
                    // [3.284.0/外部レビュー] 「証明」の強さを分ける: 全セル希望固定は「本人希望どおりの並びが
                    //   禁止パターンを構成」＝辞書式意味論(pref9000>c3n7000)の下で証明相当。それ以外の塞がり
                    //   (受け皿なし等)は「現在の探索手(単独変更・玉突き連鎖・隣接日調整)を検証して全て不成立」
                    //   という強い証拠であり、全勤務表空間の数学的な非充足証明ではない＝断定を避けた表現にする。
                    else if (cells.All(x => x.Escape == ForbiddenCellEscape.Pinned))
                    {
                        hint = $"本人希望どおりの並びが禁止パターンを構成しています（希望固定: {pinnedDays}）。" +
                            "希望を変えない限りどう組んでもこの禁止連続は残ります。どちらか1件の希望を調整してください";
                    }
                    else
                    {
                        hint = "全セルが塞がっています" +
                            (pinnedDays.Length > 0 ? $"（希望固定: {pinnedDays}）" : "") +
                            "。単独変更・玉突き連鎖・隣接日調整のすべてを検証して不成立＝現在の希望・担当のままでは" +
                            "崩せる見込みがありません。周辺の希望を1件調整するか、担当を追加してください";
                    }
                    var staffName = i >= 0 && i < state.StaffList.Count ? state.StaffList[i].Name : $"#{i}";
                    runs.Add(new ForbiddenRunDiag(i, staffName, j0, seqLabel, cells, hint));
                    j0++;
                }
            }
        }
        // 構造的に塞がっている run（＝業務担当者の対処が必要）を先頭へ。Kotlin の sortWith は安定ソート、
        // C# の OrderBy も安定ソートなので直接対応する（false=escapable でない、が true より前に来る）。
        runs = runs.OrderBy(r => r.Escapable).ToList();
        return new ForbiddenRunDiagnosis(runs.Count, runs);
    }

    /// <summary>
    /// [3.343.0] 多段手（隣接日調整）を当てた行が、<b>正味の HARD</b> で改善しているか。
    /// c3n も pref も HARD の件数和なので同じ単位で足して比べる。TryFixForbiddenRunViaAdjacentDay と
    /// その内部の FindCovUChain は他職員の希望固定を守るため、行 [i] だけを見れば足りる。
    /// </summary>
    private static bool NetHardImproves(
        Problem p, int[][] before, int[][] after, int i, int c3nBefore)
    {
        var afterRow = new int[p.T];
        for (var t = 0; t < p.T; t++) afterRow[t] = after[i][t];
        var c3nAfter = C1DeltaPrefilter.StaffC3nFires(p, afterRow);
        return c3nAfter + PrefMissesOf(p, after, i) < c3nBefore + PrefMissesOf(p, before, i);
    }

    /// <summary>職員 [i] の行で「実現可能な希望どおりでない」日数（＝pref の HARD 件数）。</summary>
    private static int PrefMissesOf(Problem p, int[][] board, int i)
    {
        var n = 0;
        for (var d = 0; d < p.T; d++)
            if (p.WishLocked(i, d) && p.Wish[i][d] != board[i][d]) n++;
        return n;
    }

    /// <summary>記号解決の共有ヘルパー（範囲外は index の文字列表現）。DiagnoseForbiddenRuns/Cell が共用。</summary>
    private static string ShiftSym(MagiState state, int k) =>
        k >= 0 && k < state.Shifts.Count ? state.Shifts[k].Kigou : k.ToString();

    private static ForbiddenRunCell DiagnoseForbiddenCell(
        MagiState state, Problem p, int[][] norm, int[][] cov,
        int i, int j, int cur)
    {
        var label = DayLabel(state.StartDate, j);
        var curSym = ShiftSym(state, cur);
        // [3.311.0] 希望どおりのセルでも即 Pinned にはしない。
        //   旧実装は wishLocked && wish == cur で HARD 差分を一切見ずに早期 return しており、
        //   その根拠（「pref(9000) の増加が c3n(7000) の減少を上回る」）は、そのセルが c3n fire
        //   1件にしか関与しない場合しか成り立たない。例: 禁止「A→A」・行 A,A,A の中央セルは
        //   2件の fire に関与し、B へ動かすと c3n 2→0 / pref 0→1 ＝ betterReport の第1キー hard が
        //   2→1 と厳密に改善する（weighted も 14000→9000）。つまり isBetter は採用する＝固定ではない。
        //   偽の Pinned は run 全体を「構造壁」と誤診し、3.281.0 の短い停滞タイムアウトを早期に
        //   発火させうる。そこで pref の増加分を c3n の正味減と同じ土俵で勘定する。
        var prefCost = p.WishLocked(i, j) && p.Wish[i][j] == cur ? 1 : 0;
        // 行 fires の正味減判定（C1DeltaPrefilter.StaffC3nFires を共用）。
        var row = new int[p.T];
        for (var t = 0; t < p.T; t++) row[t] = norm[i][t];
        var firesBefore = C1DeltaPrefilter.StaffC3nFires(p, row);
        int C3nAfter(int m)
        {
            row[j] = m;
            var afterVal = C1DeltaPrefilter.StaffC3nFires(p, row);
            row[j] = cur;
            return afterVal;
        }
        // 離脱で (cur, j) に covU 穴が空くか。空くなら FindCovUChain（探索本体と同一関数）で埋まるか実証する。
        var cnt = cur >= 0 && cur < p.K ? cov[j][cur] : 0;
        var departureHole = cur >= 0 && cur < p.K && p.CovUCell(cur, j, cnt - 1) > p.CovUCell(cur, j, cnt);
        bool ChainFills(int[][] board) => Enumerable.Range(0, 8)
            .Any(seed => V6SearchOperators.FindCovUChain(p, board, cur, j, new JavaRandom(seed), exclude: i) is not null);

        var c3nBlocked = 0;
        var noReceiver = 0;
        var prefBlocked = 0;   // c3n は減るが、希望を破る代金（pref +1）を払えない代替の数
        int? chainOk = null;   // Chain が成立した代替シフト
        int? adjOk = null;     // Adjacent が成立した代替シフト
        var alts = 0;
        foreach (var m in p.AllowedShiftsForStaff(i))
        {
            if (m == cur) continue;
            alts++;
            var after = C3nAfter(m);
            // 正味 HARD が減るか（希望を破る手は pref が 1 増える。hard は族横断の件数和なので同じ単位）。
            var netOk = after + prefCost < firesBefore;
            // 「新たな禁止連続を作る（＝そもそも c3n が減らない）」かどうかは pref 代とは別問題。
            //   両者を混ぜると、c3n は減るのに pref 代を払えないだけの代替まで隣接日調整へ流れてしまう。
            var createsNewRun = after >= firesBefore;
            if (netOk)
            {
                if (!departureHole)
                {
                    return new ForbiddenRunCell(j, label, curSym, ForbiddenCellEscape.Free,
                        $"代替「{ShiftSym(state, m)}」へ変更可");
                }
                // 離脱穴あり → 実際に動かした盤面で連鎖が埋まるかを実証。
                if (chainOk == null)
                {
                    var tmp = norm.Copy2D();
                    tmp[i][j] = m;
                    if (ChainFills(tmp)) chainOk = m; else noReceiver++;
                }
                else noReceiver++;   // 既に Chain 成立済み＝以降の重い連鎖検証は省略（分類は不変）
            }
            else if (!createsNewRun)
            {
                // c3n 自体は減るが、希望を破る代金を払うと正味では減らない＝希望が本当に効いている。
                prefBlocked++;
            }
            else
            {
                // この代替は新たな禁止連続を作る → 隣接日調整（探索本体と同一関数）で崩せるか実証。
                if (adjOk == null && chainOk == null)
                {
                    var tmp = norm.Copy2D();
                    var extra = V6SearchOperators.TryFixForbiddenRunViaAdjacentDay(p, tmp, i, j, m, new JavaRandom(7L));
                    if (extra != null)
                    {
                        // 隣接日の手＋本セルの変更を適用し、本セルの離脱穴が残るなら連鎖で埋まるかまで確認。
                        foreach (var mv in extra) tmp[mv[0]][mv[1]] = mv[2];
                        tmp[i][j] = m;
                        // [3.343.0] 多段手でも正味の HARD が減るかまで見る。3.311.0 で Pinned 判定へ
                        //   prefCost を入れたとき、この分岐（隣接日調整）には入れ忘れていた。隣接日調整は
                        //   この職員の複数日を動かすので、本セルだけでなく行全体の希望違反が増えうる。
                        //   実データで「本人希望のセルを休へ変えれば崩せる」と誤って Adjacent を出しており
                        //   （c3n −1 に対し pref +1 ＝ 正味 0・weighted は 9000−7000=+2000 悪化で採用され得ない）、
                        //   利用者に「探索が見つけていないだけ」という誤った期待を与えていた。さらに
                        //   3.281.0 の停滞打ち切り（全 run 塞がりなら短い閾値）が発火せず時間も余計に使う。
                        if (NetHardImproves(p, norm, tmp, i, firesBefore) &&
                            (!departureHole || ChainFills(tmp)))
                        {
                            adjOk = m;
                        }
                        else if (PrefMissesOf(p, tmp, i) > PrefMissesOf(p, norm, i))
                        {
                            // 並びは崩せるが、希望を破る代金のほうが高い＝希望が本当に効いている。
                            prefBlocked++;
                        }
                        else
                        {
                            c3nBlocked++;
                        }
                    }
                    else c3nBlocked++;
                }
                else c3nBlocked++;
            }
        }
        if (chainOk is int chainOkV)
        {
            return new ForbiddenRunCell(j, label, curSym, ForbiddenCellEscape.Chain,
                $"「{ShiftSym(state, chainOkV)}」へ変更＋玉突き連鎖で成立");
        }
        if (adjOk is int adjOkV)
        {
            return new ForbiddenRunCell(j, label, curSym, ForbiddenCellEscape.Adjacent,
                $"「{ShiftSym(state, adjOkV)}」へ変更＋隣接日調整で成立");
        }
        if (alts == 0)
        {
            return new ForbiddenRunCell(j, label, curSym, ForbiddenCellEscape.Blocked, "代替シフトなし");
        }
        // [3.311.0] 希望どおりのセルで、かつどの代替も正味 HARD を減らせなかったときだけ
        //   「希望固定」と名乗る（旧: 希望が一致した時点で無条件に固定扱い）。
        if (prefBlocked > 0)
        {
            return new ForbiddenRunCell(j, label, curSym, ForbiddenCellEscape.Pinned,
                $"本人希望={curSym}（動かしても正味の必須違反が減らない）");
        }
        return new ForbiddenRunCell(j, label, curSym, ForbiddenCellEscape.Blocked,
            $"代替{alts}件全滅: 新たな禁止連続{c3nBlocked}・covU受け皿なし{noReceiver}");
    }
}
