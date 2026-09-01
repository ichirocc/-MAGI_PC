using System.Collections.Generic;
using System.Linq;
using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>
/// [フェーズ9] <c>MagiViewModel.kt</c> の移植先——ピース8。
///
/// このファイルが担う範囲: 「盤面を直接編集する」「構造(<see cref="MagiState"/>)を直接編集する」
/// 系の全エディタ・クエリ、およびそれらが共有する構造編集の土台
/// （<see cref="ApplyStructure(MagiState)"/>/<see cref="ApplyStructureWithMessage"/>/
/// <see cref="MutateConstraints"/>/<see cref="StructuralEditBlocked"/>/<see cref="EditBlockedNow"/>/
/// <see cref="BusyEditMessage"/>）。
///
/// 含まれる編集面: 勤務表セル(setCell/setCells)・希望反映(applyWishes)・他の案の適用
/// (applyAlternative)・なおすのを手伝って(shortageFixCandidates)・日別必要人数(needDay)・
/// 個人別回数(staffRange)・グループ単位の回数(groupRange)・希望シフト(wishes)・シフト表示色
/// (colors)・見直し候補メモ(reviewMemo)・制約CRUD(cons1〜cons42s)・年間マスター閲覧(ws1)・
/// 目標の検算(aptBalances)・壁になっている禁止の並びを緩める(relaxForbiddenRule)・
/// 中断バナーの破棄(dismissInterrupted)・設定ミスのワンタップ修正(applySettingFix)。
///
/// 含まれない範囲（後続ピースで移植）: <c>Ws1Result</c> を介する ws1 の追加/改名/削除系
/// （<c>ws1EditShift</c>/<c>ws1AddStaff</c> 等）・
/// 改善提案(<c>findFixSuggestions</c>/<c>applyFixSuggestion</c>)・CSV入出力・
/// 最適化の実行制御(<c>runV6FullOptimize</c>/<c>runSoftPolish</c>/<c>stop</c>)・
/// バックグラウンド実行(<c>runInBackground</c>/<c>applyBgResult</c>)・初期解生成
/// (<c>generateSmartInitial</c>)。
///
/// [_ui.update{it.copy(...)} の置き換え方針] Piece5のクラスKDoc参照——このC#移植では
/// <c>Ui.X = ...;</c> という直接プロパティ代入へ置き換える。
/// </summary>
public sealed partial class MagiViewModel
{
    // ===== グリッド・希望・「他の案」・なおすのを手伝って =====

    public int[] AllowedShiftsFor(int i)
    {
        var st = _state;
        if (st is null) return System.Array.Empty<int>();
        return ScheduleUtil.CachedProblem(st).AllowedShiftsForStaff(i);
    }

    /// <summary>入力ガイド（月次/年次の入力手順）用の各項目の件数。</summary>
    public sealed record SetupCounts(
        int Days, int Staff, int Shifts, int Groups,
        int Wishes, int NeedDay, int Constraints, int Ranges, bool Use2);

    public SetupCounts GetSetupCounts()
    {
        var st = _state;
        if (st is null) return new SetupCounts(0, 0, 0, 0, 0, 0, 0, 0, false);
        var cons = st.Cons1.Count + st.Cons2.Count + st.Cons3.Count + st.Cons3n.Count +
            st.Cons3m.Count + st.Cons3mn.Count + st.Cons41.Count + st.Cons42.Count;
        return new SetupCounts(
            st.DayCount, st.StaffCount, st.ShiftCount, st.GroupCount,
            st.Wishes.Count, st.NeedDay1.Count + st.NeedDay2.Count, cons, st.StaffRange.Count, st.Use2Patterns);
    }

    /// <summary>担当外（そのスタッフのグループで担当不可）な希望の件数。希望で上書き時の確認に使う。</summary>
    public int WishOutOfScopeCount()
    {
        var st = _state;
        if (st is null) return 0;
        var p = new Problem(st);
        var n = 0;
        foreach (var (key, k) in st.Wishes)
        {
            var parts = key.Split(',');
            if (!int.TryParse(parts.ElementAtOrDefault(0), out var i)) continue;
            if (i >= 0 && i < p.S && k >= 0 && k < p.K && !p.CanDo(i, k)) n++;
        }
        return n;
    }

    /// <summary>
    /// 希望シフトを勤務表へ上書き反映（Web版の「希望で上書き」相当）。担当外の希望は
    /// <paramref name="includeOutOfScope"/>=true のときのみ反映。Undo・操作ログ付き。
    /// </summary>
    public void ApplyWishes(bool includeOutOfScope)
    {
        var st = _state;
        if (st is null) return;
        // [外部レビューH2] setCell/setCells/applyFixSuggestion と同じ理由で、ここも currentSchedule を
        //   直接書き換える＝running中は最適化ジョブの sched0 と同一参照のため良化採用時に上書き消失
        //   しうる。編集は必ず4入口(setCell/setCells/applyWishes/applyFixSuggestion)を通るガード対象。
        if (OptimizeInFlight()) { Ui.Message = BusyEditMessage(); Ui.MessageIsError = true; return; }
        var sched = _currentSchedule;
        if (sched is null) return;
        var p = new Problem(st);
        PushUndo();
        var applied = 0;
        var oos = 0;
        foreach (var (key, k) in st.Wishes)
        {
            var parts = key.Split(',');
            if (!int.TryParse(parts.ElementAtOrDefault(0), out var i)) continue;
            if (!int.TryParse(parts.ElementAtOrDefault(1), out var j)) continue;
            if (i < 0 || i >= p.S || j < 0 || j >= p.T || k < 0 || k >= p.K) continue;
            var can = p.CanDo(i, k);
            if (!can && !includeOutOfScope) continue;
            if (i < sched.Length && j < sched[i].Length && sched[i][j] != k)
            {
                sched[i][j] = k;
                applied++;
                if (!can) oos++;
            }
        }
        _currentSchedule = sched;
        _state = st.WithSchedule(sched);
        AutoSave();
        var note = oos > 0 ? $"（担当外 {oos}件含む）" : "";
        LogOp(oos > 0 ? "W" : "I", $"希望を勤務表へ反映 {applied}件{note}");
        Ui.MessageIsError = false;
        Ui.HasResult = true;
        Ui.Schedule = sched.Select(row => (IReadOnlyList<int>)row.ToList()).ToList();
        Ui.Message = $"希望を反映: {applied}件{note}";
        RefreshCheck();
    }

    private IReadOnlyList<int[][]> _alternativeScheds = System.Array.Empty<int[][]>();

    /// <summary>
    /// 直近の並列最適化で得た「他の案」を取り込み、サマリをUIへ反映。
    /// [3.335.0/外部レビュー P1] 「他の案」は可変 static でなく <c>HandleOptimize</c> の返り値から
    /// 受け取る（実行が重なると static は新しい実行の値に置き換わり、別の実行の候補を掴み得た）。
    /// </summary>
    internal async System.Threading.Tasks.Task CaptureAlternatives(IReadOnlyList<int[][]> source)
    {
        var st = _state;
        if (st is null) return;
        var alts = source.Select(a => a.Copy2D()).ToList();
        _alternativeScheds = alts;
        // [Main負荷回避] 他案（最大3件）の違反チェックは同期CPU → Default で実行してから反映。
        var summaries = await System.Threading.Tasks.Task.Run(() =>
            alts.Select((sch, idx) =>
            {
                var rep = UnifiedViolationChecker.Check(st, sch);
                return $"案{idx + 1}: 必須={rep.Hard} 合計={rep.Total}";
            }).ToList());
        Ui.Alternatives = summaries;
    }

    /// <summary>「他の案」を勤務表へ適用（Undo・操作ログ付き）。</summary>
    public void ApplyAlternative(int i)
    {
        var st = _state;
        if (st is null) return;
        // [外部レビューH2] applyWishes と同根＝currentSchedule/state を running 中に直接差し替えると
        //   最適化ジョブの完了時上書きと衝突しうる。
        if (OptimizeInFlight()) { Ui.Message = BusyEditMessage(); Ui.MessageIsError = true; return; }
        if (i < 0 || i >= _alternativeScheds.Count) return;
        var sch = _alternativeScheds[i].Copy2D();
        PushUndo();
        _currentSchedule = sch;
        _resultSchedule = sch;
        _state = st.WithSchedule(sch);
        AutoSave();
        LastApplyAlternativeTask = ApplyAlternativeCoreAsync(i, sch, st);
    }

    /// <summary>[テスト可視性のための追加] <see cref="ApplyAlternative"/> が起動する背景再チェック。</summary>
    internal System.Threading.Tasks.Task? LastApplyAlternativeTask { get; private set; }

    private async System.Threading.Tasks.Task ApplyAlternativeCoreAsync(int i, int[][] sch, MagiState fallbackSt)
    {
        // [3.392.0] 盤面は呼出元で既に差し替わっている。ここが例外で落ちると報告だけ届かず
        //   「盤面は変わったのに違反数は前の案のまま」になるので、必ず理由を残す。
        try
        {
            var rep = await System.Threading.Tasks.Task.Run(() => UnifiedViolationChecker.Check(_state ?? fallbackSt, sch));
            await PushReportAsync(_state ?? fallbackSt, sch, rep, transform: ui =>
            {
                ui.MessageIsError = false;
                ui.HasResult = true;
                ui.Message = $"他の案 {i + 1} を適用";
            });
            LogOp("I", $"他の案 {i + 1} を適用 必須={rep.Hard} 合計={rep.Total}");
        }
        catch (System.OperationCanceledException) { throw; }
        catch (System.Exception e)
        {
            LogOp("W", $"他の案 {i + 1} の適用後の再チェックに失敗: {e.GetType().Name}（盤面は適用済み・違反数は古い可能性）");
            Ui.MessageIsError = true;
            Ui.Message = $"他の案 {i + 1} を適用（違反数の再計算に失敗）";
        }
    }

    /// <summary>Set a specific shift in a cell (bottom-sheet picker).</summary>
    public void SetCell(int i, int j, int shift)
    {
        var st = _state;
        if (st is null) return;
        // [監査(未レビュー領域再監査) 実バグ修正] running中は currentSchedule が最適化ジョブの sched0 と
        //   同一の配列参照＝ここで in-place 変更すると、完了時の baseReport(旧盤面基準)と食い違うか、
        //   良化採用時に編集が無言で上書き消失する。ジョブ完了まで編集を拒否する。
        if (OptimizeInFlight()) { Ui.Message = BusyEditMessage(); Ui.MessageIsError = true; return; }
        var sched = _currentSchedule;
        if (sched is null) return;
        if (i < 0 || i >= sched.Length || j < 0 || j >= sched[i].Length) return;
        if (sched[i][j] == shift) return;
        PushUndo();
        sched[i][j] = shift;
        _currentSchedule = sched;
        _state = st.WithSchedule(sched);
        AutoSave();
        var staffName = i >= 0 && i < st.StaffList.Count ? st.StaffList[i].Name : i.ToString();
        var shiftKigou = shift >= 0 && shift < st.Shifts.Count ? st.Shifts[shift].Kigou : shift.ToString();
        Ui.MessageIsError = false;
        Ui.HasResult = true;
        Ui.Schedule = sched.Select(row => (IReadOnlyList<int>)row.ToList()).ToList();
        Ui.Message = $"{staffName} / {j + 1}日 を {shiftKigou} に変更";
        LogOp("I", $"編集: {OpNm(i)} {j + 1}日 → {OpSy(shift)}");
        RefreshCheck();
    }

    /// <summary>[プロ一括編集] 複数セル(i,j)を1シフトへ一括設定。Undoは1回・再チェックも1回（keep-best互換）。</summary>
    public void SetCells(IEnumerable<(int I, int J)> cells, int shift)
    {
        var st = _state;
        if (st is null) return;
        if (OptimizeInFlight()) { Ui.Message = BusyEditMessage(); Ui.MessageIsError = true; return; }
        var sched = _currentSchedule;
        if (sched is null) return;
        var changed = 0;
        var first = true;
        foreach (var (i, j) in cells)
        {
            if (i < 0 || i >= sched.Length || j < 0 || j >= sched[i].Length) continue;
            if (sched[i][j] == shift) continue;
            if (first) { PushUndo(); first = false; }
            sched[i][j] = shift;
            changed++;
        }
        if (changed == 0) return;
        _currentSchedule = sched;
        _state = st.WithSchedule(sched);
        AutoSave();
        var shiftKigou = shift >= 0 && shift < st.Shifts.Count ? st.Shifts[shift].Kigou : shift.ToString();
        Ui.MessageIsError = false;
        Ui.HasResult = true;
        Ui.Schedule = sched.Select(row => (IReadOnlyList<int>)row.ToList()).ToList();
        Ui.Message = $"{changed}マスを {shiftKigou} に一括変更";
        LogOp("I", $"一括編集: {changed}マス → {OpSy(shift)}");
        RefreshCheck();
    }

    /// <summary>[operator_ux §5] 「なおすのを手伝って」用：ある不足枠(日×シフト)に1タップで入れられる候補職員。</summary>
    public sealed record FixCandidate(int StaffIndex, string Name, string GroupSymbol, bool FromRest);

    public IReadOnlyList<FixCandidate> ShortageFixCandidates(int dayIndex, int shiftIndex)
    {
        var st = _state;
        if (st is null) return System.Array.Empty<FixCandidate>();
        var sched = _currentSchedule;
        if (sched is null) return System.Array.Empty<FixCandidate>();
        var p = new Problem(st);
        if (shiftIndex < 0 || shiftIndex >= p.K || dayIndex < 0 || dayIndex >= p.T) return System.Array.Empty<FixCandidate>();
        var rest = ScheduleUtil.RestShiftIndex(st); // [監査A5] 休は記号解決（raw"休"比較は「公」職場で全滅していた）
        var outList = new List<FixCandidate>();
        for (var i = 0; i < p.S; i++)
        {
            if (i >= sched.Length || dayIndex >= sched[i].Length) continue;
            if (!p.CanDo(i, shiftIndex)) continue;            // 担当できないシフトは出さない
            if (sched[i][dayIndex] == shiftIndex) continue;   // すでにそのシフト
            // [監査A5] 実現可能な希望のみ固定扱い（不可能希望のセルはエンジン同様に可動）。
            if (p.WishLocked(i, dayIndex) && p.Wish[i][dayIndex] != shiftIndex) continue;
            // [3.401.0] ここまでは「担当できる・希望で固定されていない」だけの判定で、押しても必須違反が
            //   減らない候補が混ざっていた。CoverageDiagnosis が「空き番」と数えるのと同じ2条件を足して、
            //   実際に動かせる人だけを出す。
            //   ① 移すと禁止連続(c3n)になる人は出さない。
            if (p.MakesForbiddenRun(sched, i, dayIndex, shiftIndex)) continue;
            //   ② 抜けると元のシフトに穴が空く人は「動かせる人」ではない（玉突きが要る＝この画面の手には余る）。
            var from = sched[i][dayIndex];
            if (from >= 0 && from < p.K)
            {
                var cnt = 0;
                for (var it = 0; it < p.S; it++)
                    if (it < sched.Length && dayIndex < sched[it].Length && sched[it][dayIndex] == from) cnt++;
                if (p.CovUCell(from, dayIndex, cnt - 1) > p.CovUCell(from, dayIndex, cnt)) continue;
            }
            var g = i < st.StaffList.Count ? st.StaffList[i].GroupIdx : -1;
            var name = i < st.StaffList.Count ? st.StaffList[i].Name : $"#{i}";
            var groupSym = g >= 0 && g < st.Groups.Count ? st.Groups[g].Kigou : "";
            outList.Add(new FixCandidate(i, name, groupSym, sched[i][dayIndex] == rest));
        }
        // 休みの人（動かしやすい）を先頭に。
        return outList.OrderBy(c => c.FromRest ? 0 : 1).ToList();
    }

    // ---- constraint editing (ws3-5) -------------------------------------------

    /// <summary>A constraint family with its rows rendered for display (key used for add/remove).
    /// [3.427.0] 旧 <c>subs</c>（行ごとの読み下し文）は撤去: ペア禁止系の行タイトル自体を読める形
    /// にしたため、行＋文の二重表示が冗長になった。</summary>
    public sealed record ConstraintFamilyView(string Key, string Title, IReadOnlyList<string> Rows);

    public IReadOnlyList<string> ShiftKigouList() => _state?.Shifts.Select(s => s.Kigou).ToList() ?? new List<string>();

    // ---- ws2: 日別の必要人数（例外） needDay1/needDay2 の疎な上書きを編集 ----
    public sealed record NeedDayView(int K, int J, string Kigou, string P1, string P2);

    public IReadOnlyList<NeedDayView> NeedDayOverrides()
    {
        var st = _state;
        if (st is null) return System.Array.Empty<NeedDayView>();
        var keys = st.NeedDay1.Keys.Union(st.NeedDay2.Keys).ToHashSet();
        var views = new List<NeedDayView>();
        foreach (var key in keys)
        {
            var parts = key.Split(',');
            if (parts.Length != 2) continue;
            if (!int.TryParse(parts[0], out var k)) continue;
            if (!int.TryParse(parts[1], out var j)) continue;
            var kigou = k >= 0 && k < st.Shifts.Count ? st.Shifts[k].Kigou : k.ToString();
            views.Add(new NeedDayView(k, j, kigou, st.NeedDay1.GetValueOrDefault(key, ""), st.NeedDay2.GetValueOrDefault(key, "")));
        }
        return views.OrderBy(v => v.J).ThenBy(v => v.K).ToList();
    }

    public void SetNeedDay(int k, int j, string p1, string p2)
    {
        var st = _state;
        if (st is null) return;
        var key = $"{k},{j}";
        var nd1 = new Dictionary<string, string>(st.NeedDay1);
        var nd2 = new Dictionary<string, string>(st.NeedDay2);
        if (string.IsNullOrWhiteSpace(p1)) nd1.Remove(key); else nd1[key] = p1.Trim();
        if (string.IsNullOrWhiteSpace(p2)) nd2.Remove(key); else nd2[key] = p2.Trim();
        LogOp("I", $"需要設定: {OpSy(k)} {j + 1}日 → P1={(string.IsNullOrEmpty(p1) ? "-" : p1)} P2={(string.IsNullOrEmpty(p2) ? "-" : p2)}");
        ApplyStructure(st with { NeedDay1 = nd1, NeedDay2 = nd2 });
    }

    public void RemoveNeedDay(int k, int j)
    {
        var st = _state;
        if (st is null) return;
        var key = $"{k},{j}";
        LogOp("I", $"需要削除: {OpSy(k)} {j + 1}日");
        ApplyStructure(st with { NeedDay1 = Without(st.NeedDay1, key), NeedDay2 = Without(st.NeedDay2, key) });
    }

    // ---- ws5: 個人別の回数（LimMin/LimMax） staffRange["i,k"]=Range(lo,hi) を編集 ----

    public void SetStaffRange(int i, int k, string lo, string hi)
    {
        var st = _state;
        if (st is null) return;
        var key = $"{i},{k}";
        var m = new Dictionary<string, MagiEngine.Model.Range>(st.StaffRange);
        if (string.IsNullOrWhiteSpace(lo) && string.IsNullOrWhiteSpace(hi)) m.Remove(key);
        else m[key] = new MagiEngine.Model.Range(lo.Trim(), hi.Trim());
        var summary = string.IsNullOrWhiteSpace(lo) && string.IsNullOrWhiteSpace(hi)
            ? "削除"
            : $"{(string.IsNullOrEmpty(lo) ? "?" : lo)}〜{(string.IsNullOrEmpty(hi) ? "?" : hi)}";
        LogOp("I", $"個人レンジ: {OpNm(i)} {OpSy(k)} → {summary}");
        ApplyStructure(st with { StaffRange = m });
    }

    /// <summary>
    /// [3.326.0] 回数固定(lo==hi)の幅を1段だけ広げる。**利用者のタップでのみ動く**（HF77: 数値の変更は
    /// 業務判断）。幅の決め打ちを避けるため下限側・上限側を別々に選ばせ、押した内容は操作ログへ残す。
    /// <see cref="ApplyStructure(MagiState)"/> 経由なので「元に戻す」で戻せる。
    /// </summary>
    /// <param name="loDelta">下限へ足す量（負で緩める）。</param>
    /// <param name="hiDelta">上限へ足す量（正で緩める）。</param>
    public void RelaxStaffRangePin(int i, int k, int loDelta, int hiDelta)
    {
        if (OptimizeInFlight())
        {
            Ui.MessageIsError = true;
            Ui.Message = $"{BusyWhat()}の実行中は回数を変更できません。終わってから試してください。";
            return;
        }
        var st = _state;
        if (st is null) return;
        if (!st.StaffRange.TryGetValue($"{i},{k}", out var cur)) return;
        if (!int.TryParse(cur.Lo.Trim(), out var lo)) return;
        if (!int.TryParse(cur.Hi.Trim(), out var hi)) return;
        var newLo = System.Math.Max(lo + loDelta, 0);
        var newHi = System.Math.Max(hi + hiDelta, newLo);
        if (newLo == lo && newHi == hi) return;
        LogOp("I", $"回数固定を緩和: {OpNm(i)} {OpSy(k)} {lo}〜{hi} → {newLo}〜{newHi}（もう一度つくると効果が分かります）");
        SetStaffRange(i, k, newLo.ToString(), newHi.ToString());
    }

    public void RemoveStaffRange(int i, int k)
    {
        var st = _state;
        if (st is null) return;
        LogOp("I", $"個人レンジ削除: {OpNm(i)} {OpSy(k)}");
        ApplyStructure(st with { StaffRange = Without(st.StaffRange, $"{i},{k}") });
    }

    // ---- グループ単位の回数（一括）: 既存 staffRange をグループ所属職員に展開する。
    //   新しい制約種別やスコア評価器の変更は不要（low/high は既に重み90/45で最適化対象）＝退行リスクなし。
    //   業務担当者が値を入力しボタンで適用する operator ツール（HF77準拠）。 ----

    public IReadOnlyList<string> GroupLabels() =>
        _state?.Groups.Select(g => g.Kigou.Length > 0 && g.Kigou != g.Name ? $"{g.Name}·{g.Kigou}" : g.Name).ToList()
        ?? new List<string>();

    public int GroupMemberCount(int g) => _state?.StaffList.Count(s => s.GroupIdx == g) ?? 0;

    /// <summary>グループの全メンバーが担当できるシフトの積集合（下限を全員が満たせる範囲に限定し構造的floorを防ぐ）。</summary>
    public IReadOnlyCollection<int> AllowedShiftsForGroup(int g)
    {
        var st = _state;
        if (st is null) return System.Array.Empty<int>();
        var members = Enumerable.Range(0, st.StaffList.Count).Where(i => st.StaffList[i].GroupIdx == g).ToList();
        if (members.Count == 0) return System.Array.Empty<int>();
        return members.Select(i => new HashSet<int>(AllowedShiftsFor(i)))
            .Aggregate((a, b) => { a.IntersectWith(b); return a; });
    }

    /// <summary>グループ g 所属の全職員に、ws5 個人別[lo,hi](staffRange, low/high 重み90/45=強い境界) を一括設定し、
    /// さらに ws1 C のグループ別 適切回数(groupShiftApt, apt 重み1=弱い目標) も同時に書く。
    /// apt は「最低=最高」の単一値のときのみ設定（範囲指定や空欄時はクリア）＝Excelの ws1 C→ws5 展開を1操作で再現。</summary>
    public void SetGroupRange(int g, int k, string lo, string hi)
    {
        var st0 = _state;
        if (st0 is null) return;
        var members = Enumerable.Range(0, st0.StaffList.Count).Where(i => st0.StaffList[i].GroupIdx == g).ToList();
        if (members.Count == 0) return;
        var loT = lo.Trim();
        var hiT = hi.Trim();
        if (string.IsNullOrEmpty(loT) && string.IsNullOrEmpty(hiT)) return;
        // [共有ws5・スキップ方式] ws5(個人レンジ)へ直接書く。ただし既に個人値が在るメンバーは上書きせず保持する。
        var m = new Dictionary<string, MagiEngine.Model.Range>(st0.StaffRange);
        var wrote = 0;
        var skipped = 0;
        foreach (var i in members)
        {
            var key = $"{i},{k}";
            if (m.TryGetValue(key, out var ex) && (ex.Lo.Length > 0 || ex.Hi.Length > 0)) { skipped++; continue; }
            m[key] = new MagiEngine.Model.Range(loT, hiT);
            wrote++;
        }
        // ws1 C: グループ別 適切回数（弱い目標）。単一値(最低=最高)のときのみ設定。
        var aptVal = loT == hiT ? loT : "";
        var stNew = Ws1Ops.SetGroupApt(st0 with { StaffRange = m }, g, k, aptVal);
        var gname = g < st0.Groups.Count ? st0.Groups[g].Name : $"#{g}";
        LogOp("I", $"グループ一括: {gname} {OpSy(k)} → ws5={(loT.Length == 0 ? "?" : loT)}〜{(hiT.Length == 0 ? "?" : hiT)} (書込{wrote}名/スキップ{skipped}名・既存個人値は保持)");
        ApplyStructure(stNew);
    }

    /// <summary>[共有ws5・スキップ方式] グループ既定の解除: 表示中レンジ(lo,hi)と一致するメンバーのws5だけ削除する。
    /// 個人で別値にした職員(レンジが違う)は保持する。サマリの×から呼ぶ。</summary>
    public void ClearGroupRange(int g, int k, string lo, string hi)
    {
        var st0 = _state;
        if (st0 is null) return;
        var members = Enumerable.Range(0, st0.StaffList.Count).Where(i => st0.StaffList[i].GroupIdx == g).ToList();
        if (members.Count == 0) return;
        var loT = lo.Trim();
        var hiT = hi.Trim();
        var m = new Dictionary<string, MagiEngine.Model.Range>(st0.StaffRange);
        var cleared = 0;
        foreach (var i in members)
        {
            var key = $"{i},{k}";
            if (!m.TryGetValue(key, out var r)) continue;
            if (r.Lo.Trim() == loT && r.Hi.Trim() == hiT) { m.Remove(key); cleared++; }
        }
        if (cleared == 0) return;
        var stNew = Ws1Ops.SetGroupApt(st0 with { StaffRange = m }, g, k, "");
        var gname = g < st0.Groups.Count ? st0.Groups[g].Name : $"#{g}";
        // [3.409.11] チップ内の小さな✕1回で**N名ぶん**の個人設定が消えるのに、画面には
        //   チップが1つ消えるだけで、何人ぶん消えたかが出ていなかった。実際の効果を件数つきで返す。
        Notify($"{gname}「{OpSy(k)}」のグループ上下限を解除しました（{cleared}名ぶん・「元に戻す」で戻せます）");
        ApplyStructure(stNew);
    }

    public sealed record GroupRangeView(int G, int K, string GroupName, string Kigou, string Lo, string Hi, int Members, int Shared);

    /// <summary>「グループ単位の回数」適用済み一覧。グループ全メンバーが同一の非空レンジを持つ (g,k) のみ＝
    /// 一括適用された(個別に変更されていない)グループ上下限を再構成して表示する。×で全員分をクリア。</summary>
    public IReadOnlyList<GroupRangeView> GroupRangeSummary()
    {
        var st = _state;
        if (st is null) return System.Array.Empty<GroupRangeView>();
        var outList = new List<GroupRangeView>();
        for (var g = 0; g < st.Groups.Count; g++)
        {
            var members = Enumerable.Range(0, st.StaffList.Count).Where(i => st.StaffList[i].GroupIdx == g).ToList();
            if (members.Count == 0) continue;
            for (var k = 0; k < st.Shifts.Count; k++)
            {
                // [緩和] メンバーの非空レンジを (lo,hi) で集計し、最多共有レンジを代表として出す。完全一致で
                //   なくても「2名以上が共有」なら表示し N/M名 を添える。
                var counts = new Dictionary<(string Lo, string Hi), int>();
                foreach (var i in members)
                {
                    if (!st.StaffRange.TryGetValue($"{i},{k}", out var r)) continue;
                    if (r.Lo.Length == 0 && r.Hi.Length == 0) continue;
                    var key = (r.Lo, r.Hi);
                    counts[key] = counts.GetValueOrDefault(key, 0) + 1;
                }
                if (counts.Count == 0) continue;
                var best = counts.OrderByDescending(c => c.Value).First();
                if (best.Value >= 2 || members.Count == 1)
                    outList.Add(new GroupRangeView(g, k, st.Groups[g].Name, st.Shifts[k].Kigou, best.Key.Lo, best.Key.Hi, members.Count, best.Value));
            }
        }
        return outList.OrderBy(v => v.G).ThenBy(v => v.K).ToList();
    }

    /// <summary>[直せる導線] 集計セル(職員別)の違反詳細用しきい値: 下限/上限(staffRange)・目標(apt実効)。未設定は null。</summary>
    public (int? Lo, int? Hi, int? Apt) StaffCellLimits(int i, int k)
    {
        var st = _state;
        if (st is null) return (null, null, null);
        var p = ScheduleUtil.CachedProblem(st);
        if (i < 0 || i >= p.S || k < 0 || k >= p.K) return (null, null, null);
        int? lo = p.RangeLo[i][k] is var l && (l == int.MinValue || l == 0) ? null : l;
        int? hi = p.RangeHi[i][k] is var h && h == int.MaxValue ? null : h;
        int? apt = p.Apt[i][k] is var a && a < 0 ? null : a;
        return (lo, hi, apt);
    }

    /// <summary>[直せる導線] 集計セル(日別)の必要数レンジ lo..hi（need1/need2 の OR）。どちらも未定義なら null。
    ///
    /// [3.391.0/need1直参照の第5世代] 旧実装は「need1 が未設定なら null」で、need2 だけで需要が定義された
    /// セルを「対象外」として null を返す穴があった。しきい値は <see cref="Problem.CovUCell"/>/
    /// <see cref="Problem.CovOCell"/> の選択と厳密に一致させる: 両方定義なら lo=min・hi=max（小さい方で
    /// 不足が立ち、大きい方を超えて初めて過剰が立つ）、片方だけなら双方その値。</summary>
    public (int Lo, int Hi)? NeedCellLimits(int k, int j)
    {
        var st = _state;
        if (st is null) return null;
        var p = ScheduleUtil.CachedProblem(st);
        if (k < 0 || k >= p.K || j < 0 || j >= p.T) return null;
        var n1 = p.Need1[k][j];
        var n2 = p.Use2 ? p.Need2[k][j] : -1;
        if (n1 < 0 && n2 < 0) return null;
        var lo = n1 >= 0 && n2 >= 0 ? System.Math.Min(n1, n2) : System.Math.Max(n1, n2);
        var hi = System.Math.Max(n1, n2);
        return (lo, hi);
    }

    /// <summary>[回数センター] 個人別の回数(上下限)と適切回数(apt)を職員×シフトで統合した一覧。
    /// staffRange または apt(実効=担当可＆クランプ後)が効くセルのみ返す。AptEff=実効目標(-1=なし),
    /// AptRaw=群目標の生値(-1=なし。AptEff と異なればクランプされている)。HasRange=個人別の上下限あり。</summary>
    public sealed record CountRuleView(
        int I, int K, string StaffName, string Kigou,
        string Lo, string Hi, int AptEff, int AptRaw, bool HasRange);

    public IReadOnlyList<CountRuleView> StaffCountRules()
    {
        var st = _state;
        if (st is null) return System.Array.Empty<CountRuleView>();
        // [レビュー#1] 再描画毎に呼ばれるため cachedProblem で Problem 再構築(高コスト)を避ける。
        var p = ScheduleUtil.CachedProblem(st);
        var rows = new List<CountRuleView>();
        for (var i = 0; i < p.S; i++)
        {
            if (i >= st.StaffList.Count) continue;
            var g = st.StaffList[i].GroupIdx;
            for (var k = 0; k < p.K; k++)
            {
                st.StaffRange.TryGetValue($"{i},{k}", out var r);
                var hasRange = r is not null && (r.Lo.Length > 0 || r.Hi.Length > 0);
                var aptEff = p.Apt[i][k];
                if (!hasRange && aptEff < 0) continue;
                var aptRawStr = g < st.GroupShiftApt.Count && k < st.GroupShiftApt[g].Count ? st.GroupShiftApt[g][k].Trim() : "";
                var aptRaw = int.TryParse(aptRawStr, out var ar) ? ar : -1;
                rows.Add(new CountRuleView(
                    i, k, st.StaffList[i].Name, k < st.Shifts.Count ? st.Shifts[k].Kigou : k.ToString(),
                    r?.Lo ?? "", r?.Hi ?? "", aptEff, aptEff >= 0 ? aptRaw : -1, hasRange));
            }
        }
        return rows.OrderBy(v => v.I).ThenBy(v => v.K).ToList();
    }

    // [3.286.0 冗長性B] 旧「回数設定画面」(CountSettingsCard, 2.60〜2.63世代)の集約ビューは
    //   画面本体の撤去後も呼出0のまま残存していた孤児クラスタのため、この移植では対応しない。

    // ---- ws3 移植: 希望シフト wishes["i,j"]=シフトindex（採点=pref/hard1。割当やcons3系とは別。UIのみ・モデル/エンジン不変）----
    public sealed record WishView(int I, int J, string StaffName, int Day, string Kigou, int K);

    public IReadOnlyList<WishView> WishOverrides()
    {
        var st = _state;
        if (st is null) return System.Array.Empty<WishView>();
        var views = new List<WishView>();
        foreach (var (key, k) in st.Wishes)
        {
            var parts = key.Split(',');
            if (parts.Length != 2) continue;
            if (!int.TryParse(parts[0], out var i)) continue;
            if (!int.TryParse(parts[1], out var j)) continue;
            var name = i < st.StaffList.Count ? st.StaffList[i].Name : i.ToString();
            var kigou = k >= 0 && k < st.Shifts.Count ? st.Shifts[k].Kigou : k.ToString();
            views.Add(new WishView(i, j, name, j + 1, kigou, k));
        }
        return views.OrderBy(v => v.I).ThenBy(v => v.J).ToList();
    }

    public void SetWish(int i, int j, int k)
    {
        var st = _state;
        if (st is null) return;
        var m = new Dictionary<string, int>(st.Wishes) { [$"{i},{j}"] = k };
        LogOp("I", $"希望設定: {OpNm(i)} {j + 1}日 → {OpSy(k)}");
        ApplyStructure(st with { Wishes = m });
    }

    public void RemoveWish(int i, int j)
    {
        var st = _state;
        if (st is null) return;
        LogOp("I", $"希望削除: {OpNm(i)} {j + 1}日");
        ApplyStructure(st with { Wishes = Without(st.Wishes, $"{i},{j}") });
    }

    /// <summary>[一括] スタッフ(null=全員)×日群に希望 k を一括設定。Undo1回・再チェック1回。</summary>
    public void SetWishesForDays(int? staffIdx, IReadOnlyList<int> days, int k)
    {
        var st = _state;
        if (st is null) return;
        if (days.Count == 0 || k < 0 || k >= st.Shifts.Count) return;
        var m = new Dictionary<string, int>(st.Wishes);
        var staffRange = staffIdx is not null ? new[] { staffIdx.Value } : Enumerable.Range(0, st.StaffList.Count).ToArray();
        foreach (var i in staffRange)
            foreach (var j in days)
                if (i >= 0 && i < st.StaffList.Count && j >= 0 && j < st.DayCount) m[$"{i},{j}"] = k;
        LogOp("I", $"希望一括: {(staffIdx is not null ? OpNm(staffIdx.Value) : "全員")} {OpDays(days)} → {OpSy(k)}");
        ApplyStructure(st with { Wishes = m });
    }

    /// <summary>[一括] スタッフ(null=全員)×日群の希望を一括削除。</summary>
    public void ClearWishesForDays(int? staffIdx, IReadOnlyList<int> days)
    {
        var st = _state;
        if (st is null) return;
        if (days.Count == 0) return;
        var m = new Dictionary<string, int>(st.Wishes);
        var staffRange = staffIdx is not null ? new[] { staffIdx.Value } : Enumerable.Range(0, st.StaffList.Count).ToArray();
        foreach (var i in staffRange)
            foreach (var j in days)
                m.Remove($"{i},{j}");
        if (m.Count == st.Wishes.Count) return;
        LogOp("I", $"希望クリア: {(staffIdx is not null ? OpNm(staffIdx.Value) : "全員")} {OpDays(days)}");
        ApplyStructure(st with { Wishes = m });
    }

    /// <summary>[一括] すべての希望を削除。</summary>
    public void ClearAllWishes()
    {
        var st = _state;
        if (st is null) return;
        if (st.Wishes.Count == 0) return;
        LogOp("I", "希望全クリア");
        ApplyStructure(st with { Wishes = new Dictionary<string, int>() });
    }

    // ---- colors: シフトの表示色 shiftColors[kigou]="#rrggbb"（表示専用）----
    public sealed record ShiftColorView(string Kigou, string Name, string Hex, bool Custom);

    public IReadOnlyList<ShiftColorView> ShiftColorList()
    {
        var st = _state;
        if (st is null) return System.Array.Empty<ShiftColorView>();
        return st.Shifts.Select((sh, i) =>
        {
            var ov = st.ShiftColors.GetValueOrDefault(sh.Kigou);
            return new ShiftColorView(sh.Kigou, sh.Name, ShiftAppearance.ResolveShiftColor(ov, i), !string.IsNullOrWhiteSpace(ov));
        }).ToList();
    }

    public void SetShiftColor(string kigou, string hex)
    {
        var st = _state;
        if (st is null || kigou.Length == 0) return;
        var m = new Dictionary<string, string>(st.ShiftColors) { [kigou] = hex.Trim() };
        ApplyStructure(st with { ShiftColors = m });
    }

    public void ResetShiftColor(string kigou)
    {
        var st = _state;
        if (st is null) return;
        ApplyStructure(st with { ShiftColors = Without(st.ShiftColors, kigou) });
    }

    /// <summary>[違反色] 違反セルの枠/マーカー色。予約キー "__vio__" に保存（状態スキーマ非変更）。</summary>
    public void SetViolationColor(string hex)
    {
        var st = _state;
        if (st is null || hex.Length == 0) return;
        var m = new Dictionary<string, string>(st.ShiftColors) { ["__vio__"] = hex.Trim() };
        ApplyStructure(st with { ShiftColors = m });
    }

    public void ResetViolationColor()
    {
        var st = _state;
        if (st is null) return;
        ApplyStructure(st with { ShiftColors = Without(st.ShiftColors, "__vio__") });
    }

    /// <summary>[違反色] 要調整(ソフト違反)の枠/マーカー色。予約キー "__vioSoft__"（空=既定の橙）。</summary>
    public void SetViolationSoftColor(string hex)
    {
        var st = _state;
        if (st is null || hex.Length == 0) return;
        var m = new Dictionary<string, string>(st.ShiftColors) { ["__vioSoft__"] = hex.Trim() };
        ApplyStructure(st with { ShiftColors = m });
    }

    public void ResetViolationSoftColor()
    {
        var st = _state;
        if (st is null) return;
        ApplyStructure(st with { ShiftColors = Without(st.ShiftColors, "__vioSoft__") });
    }

    /// <summary>[違反色/族別] 違反種別（族）ごとの個別色。予約キー "__vioFam_&lt;fam&gt;__"（例: __vioFam_c3n__）。
    /// 未設定の族は重大度色（__vio__/__vioSoft__）へフォールバック。</summary>
    public void SetViolationFamilyColor(string fam, string hex)
    {
        var st = _state;
        if (st is null || hex.Length == 0 || fam.Length == 0) return;
        var m = new Dictionary<string, string>(st.ShiftColors) { [$"__vioFam_{fam}__"] = hex.Trim() };
        ApplyStructure(st with { ShiftColors = m });
    }

    public void ResetViolationFamilyColor(string fam)
    {
        var st = _state;
        if (st is null) return;
        ApplyStructure(st with { ShiftColors = Without(st.ShiftColors, $"__vioFam_{fam}__") });
    }

    // ---- [見直し候補] 月次の修正から「基本ルールの見直し候補」を積む軽量メモ（セッション内のみ・state 非保存） ----
    public void AddReviewMemo(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Ui.MessageIsError = false;
        Ui.ReviewMemos = Ui.ReviewMemos.Append(text.Trim()).ToList();
        Ui.Message = "見直し候補に追加しました";
    }

    public void RemoveReviewMemo(int index)
    {
        var l = Ui.ReviewMemos;
        if (index < 0 || index >= l.Count) return;
        Ui.ReviewMemos = l.Where((_, j) => j != index).ToList();
    }

    public IReadOnlyList<string> GroupKigouList() => _state?.Groups.Select(g => g.Kigou).ToList() ?? new List<string>();

    /// <summary>[冗長除去/データ密度] 1日人数の上下限 [l〜u] を意味で圧縮して短く表す。見出しが「人数(上下限)」の
    /// 文脈を担うので、行は記号のみで足りる。l==u=ちょうどN / 下限のみ=N以上 / 上限のみ=N以下 / 両方=l〜u。</summary>
    private static string BoundLabel(string l, string u)
    {
        var lo = l.Length == 0 ? null : l;
        var hi = u.Length == 0 ? null : u;
        if (lo is not null && hi is not null && lo == hi) return $"ちょうど{lo}";
        if (lo is not null && hi is not null) return $"{lo}〜{hi}";
        if (lo is not null) return $"{lo} 以上";
        if (hi is not null) return $"{hi} 以下";
        return "制限なし";
    }

    public IReadOnlyList<ConstraintFamilyView> ConstraintFamilies()
    {
        var st = _state;
        if (st is null) return System.Array.Empty<ConstraintFamilyView>();
        static string Seq(IReadOnlyList<string> p)
        {
            var body = string.Join(" -> ", p.Where(x => x.Length > 0));
            return body.Length == 0 ? "(空)" : body;
        }
        return new List<ConstraintFamilyView>
        {
            // [用語統一/下流→上流] 節タイトルは違反チップ(breakdownLabels)の語彙を正として一致させる。
            new("cons1", "窓の要件（○日間に△回以上）",
                st.Cons1.Select(c => $"{c.ShiftKigou}   {c.Day1}日で{c.Day2}回以上").ToList()),
            new("cons2", "個人の合計（回数）",
                st.Cons2.Select(c => $"{c.ShiftKigou}   合計{c.Count}回以上").ToList()),
            new("cons3", "必須の並び", st.Cons3.Select(c => Seq(c.Pattern)).ToList()),
            new("cons3n", "禁止の並び", st.Cons3n.Select(c => Seq(c.Pattern)).ToList()),
            new("cons3m", "推奨の並び", st.Cons3m.Select(c => Seq(c.Pattern)).ToList()),
            new("cons3mn", "回避の並び", st.Cons3mn.Select(c => Seq(c.Pattern)).ToList()),
            new("cons41", "群のレンジ（1日の人数の下限〜上限）",
                st.Cons41.Select(c => $"{c.GroupKigou}・{c.ShiftKigou}   {BoundLabel(c.L, c.U)}").ToList()),
            // [3.409.18] 「禁止/不可」はラベルとして実態（最軽量のソフト条件）と逆の約束をするため
            //   「できるだけ守る」を見出しへ明示。
            new("cons42", "群ペア禁止（同じ日に不可・できるだけ守る）",
                st.Cons42.Select(c => $"{c.G1Kigou}の{c.S1Kigou} ✕ {c.G2Kigou}の{c.S2Kigou}").ToList()),
        };
    }

    /// <summary>[スキルグループ専用ルール] C41s/C42s。スキルグループ定義の直下に co-locate して表示する。</summary>
    public IReadOnlyList<ConstraintFamilyView> SkillConstraintFamilies()
    {
        var st = _state;
        if (st is null) return System.Array.Empty<ConstraintFamilyView>();
        return new List<ConstraintFamilyView>
        {
            new("cons41s", "スキル群のレンジ（1日の人数の下限〜上限）",
                st.Cons41s.Select(c => $"{c.GroupKigou}・{c.ShiftKigou}   {BoundLabel(c.L, c.U)}").ToList()),
            new("cons42s", "スキル群ペア禁止（同じ日に不可・できるだけ守る）",
                st.Cons42s.Select(c => $"{c.G1Kigou}の{c.S1Kigou} ✕ {c.G2Kigou}の{c.S2Kigou}").ToList()),
        };
    }

    public IReadOnlyList<string> SkillGroupKigouList() => _state?.SkillGroups.Select(g => g.Kigou).ToList() ?? new List<string>();

    public void AddCons41s(string groupKigou, string shiftKigou, string l, string u)
    {
        var st = _state;
        if (st is null) return;
        LogOp("I", $"制約追加(スキル群回数): {groupKigou} {shiftKigou} {l.Trim()}〜{u.Trim()}");
        MutateConstraints(st with { Cons41s = st.Cons41s.Append(new C41Row(groupKigou, shiftKigou, l.Trim(), u.Trim())).ToList() });
    }

    public void AddCons42s(string g1, string g2, string s1, string s2)
    {
        var st = _state;
        if (st is null) return;
        LogOp("I", $"制約追加(スキル群組合せ禁止): {g1}{s1} & {g2}{s2}");
        MutateConstraints(st with { Cons42s = st.Cons42s.Append(new C42Row(g1, g2, s1, s2)).ToList() });
    }

    public void AddCons1(string day1, string shiftKigou, string day2)
    {
        var st = _state;
        if (st is null) return;
        LogOp("I", $"制約追加(連勤/休): {day1.Trim()}日に{shiftKigou}{day2.Trim()}回以上");
        MutateConstraints(st with { Cons1 = st.Cons1.Append(new C1Row(day1.Trim(), shiftKigou, day2.Trim())).ToList() });
    }

    public void AddCons2(string shiftKigou, string count)
    {
        var st = _state;
        if (st is null) return;
        LogOp("I", $"制約追加(cons2): {shiftKigou} {count.Trim()}");
        MutateConstraints(st with { Cons2 = st.Cons2.Append(new C2Row(shiftKigou, count.Trim())).ToList() });
    }

    public void AddCons41(string groupKigou, string shiftKigou, string l, string u)
    {
        var st = _state;
        if (st is null) return;
        LogOp("I", $"制約追加(群回数): {groupKigou} {shiftKigou} {l.Trim()}〜{u.Trim()}");
        MutateConstraints(st with { Cons41 = st.Cons41.Append(new C41Row(groupKigou, shiftKigou, l.Trim(), u.Trim())).ToList() });
    }

    public void AddCons42(string g1, string g2, string s1, string s2)
    {
        var st = _state;
        if (st is null) return;
        LogOp("I", $"制約追加(群組合せ禁止): {g1}{s1} & {g2}{s2}");
        MutateConstraints(st with { Cons42 = st.Cons42.Append(new C42Row(g1, g2, s1, s2)).ToList() });
    }

    public void AddCons3(string family, IReadOnlyList<string> pattern)
    {
        var st = _state;
        if (st is null) return;
        // Level Zero loads cons3 by reading day columns until the first blank (truncate at
        // first blank, max 5 days), not by removing all blanks. Match that here.
        var pat = pattern.Select(p => p.Trim()).TakeWhile(p => p.Length > 0).Take(5).ToList();
        if (pat.Count == 0) return;
        LogOp("I", $"制約追加({family}): {string.Join("→", pat)}");
        MagiState? next = family switch
        {
            "cons3" => st with { Cons3 = st.Cons3.Append(new C3Row(pat)).ToList() },
            "cons3n" => st with { Cons3n = st.Cons3n.Append(new C3Row(pat)).ToList() },
            "cons3m" => st with { Cons3m = st.Cons3m.Append(new C3Row(pat)).ToList() },
            "cons3mn" => st with { Cons3mn = st.Cons3mn.Append(new C3Row(pat)).ToList() },
            _ => null,
        };
        MutateConstraints(next);
    }

    public void RemoveConstraint(string family, int index)
    {
        var st = _state;
        if (st is null) return;
        // [3.271.0, 実機ログ起因] index を先に検証する。旧: 検証なしで先にログ→mutate のため、
        //   リスト縮小後の古い index（連続タップ等）でも幻ログ＋無駄な undo/保存/再検査が走っていた。
        int? size = family switch
        {
            "cons1" => st.Cons1.Count,
            "cons2" => st.Cons2.Count,
            "cons3" => st.Cons3.Count,
            "cons3n" => st.Cons3n.Count,
            "cons3m" => st.Cons3m.Count,
            "cons3mn" => st.Cons3mn.Count,
            "cons41" => st.Cons41.Count,
            "cons42" => st.Cons42.Count,
            "cons41s" => st.Cons41s.Count,
            "cons42s" => st.Cons42s.Count,
            _ => null,
        };
        if (size is null) return;
        if (index < 0 || index >= size)
        {
            LogOp("W", $"制約削除を無視: {family}[{index}] は存在しません（削除済みの行への連続タップ等）");
            return;
        }
        LogOp("I", $"制約削除: {family}[{index}]");
        static List<T> Without<T>(IReadOnlyList<T> l, int i) => l.Where((_, idx) => idx != i).ToList();
        MagiState? next = family switch
        {
            "cons1" => st with { Cons1 = Without(st.Cons1, index) },
            "cons2" => st with { Cons2 = Without(st.Cons2, index) },
            "cons3" => st with { Cons3 = Without(st.Cons3, index) },
            "cons3n" => st with { Cons3n = Without(st.Cons3n, index) },
            "cons3m" => st with { Cons3m = Without(st.Cons3m, index) },
            "cons3mn" => st with { Cons3mn = Without(st.Cons3mn, index) },
            "cons41" => st with { Cons41 = Without(st.Cons41, index) },
            "cons42" => st with { Cons42 = Without(st.Cons42, index) },
            "cons41s" => st with { Cons41s = Without(st.Cons41s, index) },
            "cons42s" => st with { Cons42s = Without(st.Cons42s, index) },
            _ => null,
        };
        MutateConstraints(next);
    }

    /// <summary>[制約編集/実機指摘「登録した制約の変更ができない」] 行の生値（編集ダイアログのプリフィル用）。
    /// 値の並びは追加ダイアログの入力順と同じ:
    /// cons1=[日数,シフト,回数] / cons2=[シフト,回数] / cons3系=並び(最大5) /
    /// cons41(s)=[群,シフト,下限,上限] / cons42(s)=[群1,シフト1,群2,シフト2]。</summary>
    public IReadOnlyList<string>? ConstraintRowValues(string family, int index)
    {
        var st = _state;
        if (st is null) return null;
        return family switch
        {
            "cons1" => index < st.Cons1.Count ? new List<string> { st.Cons1[index].Day1, st.Cons1[index].ShiftKigou, st.Cons1[index].Day2 } : null,
            "cons2" => index < st.Cons2.Count ? new List<string> { st.Cons2[index].ShiftKigou, st.Cons2[index].Count } : null,
            "cons3" => index < st.Cons3.Count ? st.Cons3[index].Pattern.ToList() : null,
            "cons3n" => index < st.Cons3n.Count ? st.Cons3n[index].Pattern.ToList() : null,
            "cons3m" => index < st.Cons3m.Count ? st.Cons3m[index].Pattern.ToList() : null,
            "cons3mn" => index < st.Cons3mn.Count ? st.Cons3mn[index].Pattern.ToList() : null,
            "cons41" => index < st.Cons41.Count ? new List<string> { st.Cons41[index].GroupKigou, st.Cons41[index].ShiftKigou, st.Cons41[index].L, st.Cons41[index].U } : null,
            "cons41s" => index < st.Cons41s.Count ? new List<string> { st.Cons41s[index].GroupKigou, st.Cons41s[index].ShiftKigou, st.Cons41s[index].L, st.Cons41s[index].U } : null,
            "cons42" => index < st.Cons42.Count ? new List<string> { st.Cons42[index].G1Kigou, st.Cons42[index].S1Kigou, st.Cons42[index].G2Kigou, st.Cons42[index].S2Kigou } : null,
            "cons42s" => index < st.Cons42s.Count ? new List<string> { st.Cons42s[index].G1Kigou, st.Cons42s[index].S1Kigou, st.Cons42s[index].G2Kigou, st.Cons42s[index].S2Kigou } : null,
            _ => null,
        };
    }

    /// <summary>[制約編集] 行を同じ位置で置き換える。values の並びは <see cref="ConstraintRowValues"/> と同一。
    /// cons3系は追加(<see cref="AddCons3"/>)と同じ正規化（先頭から最初の空白まで・最大5）。</summary>
    public void UpdateConstraint(string family, int index, IReadOnlyList<string> values)
    {
        var st = _state;
        if (st is null) return;
        var v = values.Select(x => x.Trim()).ToList();
        string G(int i) => i < v.Count ? v[i] : "";
        static List<T> Replaced<T>(IReadOnlyList<T> l, int i, T x) => l.Select((e, idx) => idx == i ? x : e).ToList();

        MagiState? next;
        switch (family)
        {
            case "cons1":
                if (index < 0 || index >= st.Cons1.Count) return;
                next = st with { Cons1 = Replaced(st.Cons1, index, new C1Row(G(0), G(1), G(2))) };
                break;
            case "cons2":
                if (index < 0 || index >= st.Cons2.Count) return;
                next = st with { Cons2 = Replaced(st.Cons2, index, new C2Row(G(0), G(1))) };
                break;
            case "cons41":
                if (index < 0 || index >= st.Cons41.Count) return;
                next = st with { Cons41 = Replaced(st.Cons41, index, new C41Row(G(0), G(1), G(2), G(3))) };
                break;
            case "cons41s":
                if (index < 0 || index >= st.Cons41s.Count) return;
                next = st with { Cons41s = Replaced(st.Cons41s, index, new C41Row(G(0), G(1), G(2), G(3))) };
                break;
            case "cons42":
                if (index < 0 || index >= st.Cons42.Count) return;
                next = st with { Cons42 = Replaced(st.Cons42, index, new C42Row(G(0), G(2), G(1), G(3))) };
                break;
            case "cons42s":
                if (index < 0 || index >= st.Cons42s.Count) return;
                next = st with { Cons42s = Replaced(st.Cons42s, index, new C42Row(G(0), G(2), G(1), G(3))) };
                break;
            case "cons3": case "cons3n": case "cons3m": case "cons3mn":
                var pat = v.TakeWhile(x => x.Length > 0).Take(5).ToList();
                if (pat.Count == 0) return;
                next = family switch
                {
                    "cons3" => index < st.Cons3.Count ? st with { Cons3 = Replaced(st.Cons3, index, new C3Row(pat)) } : null,
                    "cons3n" => index < st.Cons3n.Count ? st with { Cons3n = Replaced(st.Cons3n, index, new C3Row(pat)) } : null,
                    "cons3m" => index < st.Cons3m.Count ? st with { Cons3m = Replaced(st.Cons3m, index, new C3Row(pat)) } : null,
                    _ => index < st.Cons3mn.Count ? st with { Cons3mn = Replaced(st.Cons3mn, index, new C3Row(pat)) } : null,
                };
                if (next is null) return;
                break;
            default:
                return;
        }
        LogOp("I", $"制約変更: {family}[{index}] → {string.Join(" ", v.Where(x => x.Length > 0))}");
        MutateConstraints(next);
    }

    /// <summary>
    /// [中断バナーの破棄] Kotlin原本 <c>dismissInterrupted()</c>（行230-233）の移植。UI状態の2フラグを
    /// リセットし、続けて <c>clearBgFiles("中断の破棄")</c>（<c>work/RunFiles.kt</c> 相当・プロセスkill
    /// 耐性のための途中状態ファイル削除）を呼ぶ。Phase 10 で <see cref="Work.RunFiles"/> ＋
    /// <c>MagiViewModel.RunMarker.cs</c> を移植したため、この2行目も本来の意味で機能する
    /// （破棄したのに途中状態ファイルが残ると、次回起動が同じ「中断されました」を再び掴む）。
    /// </summary>
    public void DismissInterrupted()
    {
        Ui.InterruptedRun = false;
        Ui.InterruptedInfo = null;
        ClearBgFiles("中断の破棄");   // [C1] 破棄で途中状態ファイルを削除
    }

    /// <summary>
    /// [設定ミスのワンタップ修正] Kotlin原本 <c>applySettingFix(issue: SettingIssue)</c>
    /// （行1946-2015）の移植。<see cref="SettingIssue.Action"/> ごとに新しい <see cref="MagiState"/> を
    /// 組み立て、組み立てられた場合のみ操作ログ＋<see cref="ApplyStructure(MagiState)"/>（Undo・自動保存・
    /// 再チェック付き）を呼ぶ。各分岐のガード（Kotlinの <c>?: return</c>）は
    /// <see cref="ComputeSettingFixState"/> 内の早期 <c>return null</c> として保持する
    /// （どちらも「何もしない」という同一の観測可能な結果になる——Kotlin原本の <c>return</c> は
    /// <c>applySettingFix</c> 全体を抜けるが、その後に残るのは <c>if (ns != null)</c> だけなので
    /// null を返して素通りさせるのと結果は同じ）。
    /// </summary>
    public void ApplySettingFix(SettingIssue issue)
    {
        var s = _state;
        if (s is null) return;
        var ns = ComputeSettingFixState(s, issue);
        if (ns is not null)
        {
            LogOp("I", $"設定ミスの修正を適用: {issue.Action} @ {issue.Where}");
            ApplyStructure(ns);
        }
    }

    private static MagiState? ComputeSettingFixState(MagiState s, SettingIssue issue)
    {
        switch (issue.Action)
        {
            case SettingFixAction.RemoveWish:
            {
                var key = issue.WishKey;
                if (key is null) return null;
                if (!s.Wishes.ContainsKey(key)) return null;
                return s with { Wishes = Without(s.Wishes, key) };
            }
            case SettingFixAction.DeleteDupSeq:
            {
                var fam = issue.SeqFamily;
                var key = issue.SeqKey;
                if (fam is null || key is null) return null;
                static List<C3Row> DelOne(IReadOnlyList<C3Row> rows, string key)
                {
                    var done = false;
                    var res = new List<C3Row>(rows.Count);
                    foreach (var row in rows)
                    {
                        var joined = string.Join("→", row.Pattern.Where(x => x.Trim().Length > 0));
                        if (!done && joined == key) { done = true; continue; }
                        res.Add(row);
                    }
                    return res;
                }
                return fam switch
                {
                    "c3" => s with { Cons3 = DelOne(s.Cons3, key) },
                    "c3n" => s with { Cons3n = DelOne(s.Cons3n, key) },
                    "c3m" => s with { Cons3m = DelOne(s.Cons3m, key) },
                    "c3mn" => s with { Cons3mn = DelOne(s.Cons3mn, key) },
                    _ => null,
                };
            }
            case SettingFixAction.ZeroRangeLo:
            case SettingFixAction.ClampRangeLo:
            {
                var key = issue.RangeKey;
                if (key is null) return null;
                var cur = s.StaffRange.TryGetValue(key, out var r) ? r : new MagiEngine.Model.Range("", "");
                var m = new Dictionary<string, MagiEngine.Model.Range>(s.StaffRange)
                {
                    [key] = new MagiEngine.Model.Range(issue.NewLo ?? cur.Lo, cur.Hi),
                };
                return s with { StaffRange = m };
            }
            case SettingFixAction.ClampGroupRangeLo:
            {
                // 行は List なので index でなく**内容一致**で指す（DeleteDupSeq と同じ理由＝診断から
                //   タップまでに並びが変わっても別の行を壊さない）。同じ内容が複数あるときは先頭1件だけ直す。
                var row = issue.GroupRangeRow;
                var lo = issue.NewLo;
                if (row is null || lo is null) return null;
                static List<C41Row> ClampOne(IReadOnlyList<C41Row> rows, C41Row row, string lo)
                {
                    var list = rows.ToList();
                    var i = list.IndexOf(row);
                    if (i < 0) return list;
                    list[i] = row with { L = lo };
                    return list;
                }
                return issue.GroupRangeFamily switch
                {
                    "c41" => s with { Cons41 = ClampOne(s.Cons41, row, lo) },
                    "c41s" => s with { Cons41s = ClampOne(s.Cons41s, row, lo) },
                    _ => null,
                };
            }
            case SettingFixAction.CapDemand:
            {
                var k = issue.DemandShiftIdx;
                var cap = issue.DemandCap;
                if (k is null || cap is null) return null;
                if (k.Value < 0 || k.Value >= s.Shifts.Count) return null;
                var sh = s.Shifts[k.Value];
                var n1Ok = int.TryParse(sh.Need1.Trim(), out var n1);
                var n2Ok = int.TryParse(sh.Need2.Trim(), out var n2);
                var newN1 = n1Ok && n1 > cap.Value ? cap.Value.ToString() : sh.Need1;
                var newN2 = n2Ok && n2 > cap.Value ? cap.Value.ToString() : sh.Need2;
                if (newN1 == sh.Need1 && newN2 == sh.Need2) return null;
                var list = s.Shifts.ToList();
                list[k.Value] = sh with { Need1 = newN1, Need2 = newN2 };
                return s with { Shifts = list };
            }
            case SettingFixAction.None:
            default:
                return null;
        }
    }

    // ===== 実行中ガード（構造編集向け）・構造編集の土台 =====

    /// <summary>
    /// [3.405.0] 盤面セルを編集できない状態なら、理由を出して true を返す。**画面がシートを開く前に
    /// 同じ判定を使う**ためのもの（<see cref="SetCell"/> 等が使う文言と1文字も違わないよう同じ定数を読む）。
    /// 旧: セルはいつでもタップでき、シートは「タップで割当を即変更。」と言い切ってから拒否していた＝
    /// 形が約束したことを守れていなかった。開かなければ約束は嘘にならない。
    /// </summary>
    public bool EditBlockedNow()
    {
        if (!OptimizeInFlight()) return false;
        Ui.Message = BusyEditMessage();
        Ui.MessageIsError = true;
        return true;
    }

    private string BusyEditMessage() => $"{BusyWhat()}の実行中は編集できません（完了後にもう一度お試しください）";

    private bool StructuralEditBlocked()
    {
        if (!OptimizeInFlight()) return false;
        LogOp("W", $"{BusyWhat()}の実行中のため設定変更を取り消しました（終わってから、または「やめる」の後にどうぞ）");
        Ui.MessageIsError = true;
        Ui.Message = $"{BusyWhat()}の実行中は設定を変更できません。終わるか「やめる」を押してからにしてください。";
        return true;
    }

    private void MutateConstraints(MagiState? newState)
    {
        if (newState is null) return;
        if (StructuralEditBlocked()) return;
        PushUndo();
        _state = newState;
        Ui.ConstraintsEdited = true;
        RefreshCheck();
        AutoSave();
    }

    // ---- ws1 initial setup ----------------------------------------------------

    /// <summary>Snapshot of the ws1 (初期設定) data for the editor. Recomputed per call (cheap).</summary>
    public sealed record Ws1View(
        string StartDate, string EndDate, int Days, bool Use2,
        IReadOnlyList<Shift> Shifts, IReadOnlyList<Group> Groups, IReadOnlyList<Staff> Staff,
        IReadOnlyList<IReadOnlyList<int>> GroupShift,
        IReadOnlyList<IReadOnlyList<string>> GroupShiftApt);

    public Ws1View? Ws1()
    {
        var st = _state;
        if (st is null) return null;
        var days = _currentSchedule is { Length: > 0 } sc ? sc[0].Length : st.DayCount;
        return new Ws1View(st.StartDate, st.EndDate, days, st.Use2Patterns, st.Shifts, st.Groups, st.StaffList, st.GroupShift, st.GroupShiftApt);
    }

    private void ApplyStructure(MagiState ns)
    {
        if (StructuralEditBlocked()) return;
        PushUndo();
        _state = ns;
        Ui.StructureEdited = true;
        RefreshCheck();
        AutoSave();
    }

    /// <summary>[テスト可視性のための追加] 直近の <see cref="ApplyStructureWithMessage(MagiState, string)"/>
    /// 呼出しが起動する背景再チェック（構造未読込・スケジュール未読込の即応パスでは null のまま）。</summary>
    internal System.Threading.Tasks.Task? LastApplyStructureWithMessageTask { get; private set; }

    /// <summary>
    /// 構造変更(ns)を適用し、再チェック後に独自の完了メッセージを表示（コンポーネント別取込・apt全リセット等で使用）。
    /// <see cref="RefreshCheck"/> と同じ <c>_checkSeq</c>/<c>_checkCts</c> を共有する——「いま生きている
    /// 自動チェックは常に1つだけ」という取消の意味論を保つため。
    /// </summary>
    private void ApplyStructureWithMessage(MagiState ns, string doneMessage)
    {
        if (StructuralEditBlocked()) return;
        PushUndo();
        _state = ns;
        AutoSave();
        var sched = _currentSchedule?.Copy2D();
        if (sched is null)
        {
            Ui.MessageIsError = false;
            Ui.StructureEdited = true;
            Ui.Message = doneMessage;
            LastApplyStructureWithMessageTask = null;
            return;
        }
        var seq = ++_checkSeq;
        _checkCts?.Cancel();
        Ui.MessageIsError = false;
        Ui.Running = true;
        Ui.StructureEdited = true;
        Ui.Message = $"{doneMessage}（違反チェック中…）";
        var cts = new System.Threading.CancellationTokenSource();
        _checkCts = cts;
        LastApplyStructureWithMessageTask = ApplyStructureWithMessageCoreAsync(ns, sched, doneMessage, seq, cts.Token);
    }

    private async System.Threading.Tasks.Task ApplyStructureWithMessageCoreAsync(
        MagiState ns, int[][] sched, string doneMessage, long seq, System.Threading.CancellationToken ct)
    {
        try
        {
            var r = await System.Threading.Tasks.Task.Run(() => V6FinalPort.HandleCheck(ns, sched), ct);
            if (seq != _checkSeq) return;
            await PushReportAsync(ns, r.Schedule, r.Report, transform: ui =>
            {
                ui.MessageIsError = false;
                ui.Running = OptimizeInFlight();
                ui.Message = $"{doneMessage}｜必須={r.Report.Hard} 合計={r.Report.Total}";
            }, ct: ct);
        }
        catch (System.OperationCanceledException)
        {
            if (seq == _checkSeq)
            {
                Ui.MessageIsError = false;
                Ui.Running = OptimizeInFlight();
                Ui.Message = $"{doneMessage}（チェックを停止）";
            }
            throw;
        }
        catch (System.Exception e)
        {
            if (seq == _checkSeq)
            {
                Ui.MessageIsError = true;
                Ui.Running = OptimizeInFlight();
                Ui.Message = $"{doneMessage}（チェック失敗: {e.GetType().Name}）";
            }
        }
    }

    /// <summary>[目標の検算] シフトごとの「適切回数(apt)の合計 vs それを受け止められる上限」。
    /// <see cref="V6SanityPort.AptBalances(MagiState, Problem?)"/> をそのまま返す＝設定ミス診断（検査6-C）と
    /// 同じ単一ソース。盤面を参照しないので、勤務表を作る前（未計算）でも目標を触るたびに正しい値が出る。</summary>
    public IReadOnlyList<AptBalance> AptBalances()
    {
        var st = _state;
        if (st is null) return System.Array.Empty<AptBalance>();
        try { return V6SanityPort.AptBalances(st); } catch { return System.Array.Empty<AptBalance>(); }
    }

    /// <summary>
    /// [壁になっている禁止の並びを緩める] ForbiddenDiag が「崩せない」と判定した禁止連続(c3n)ルールを、
    /// その場で削除する。制約画面まで行って該当行を探す必要をなくすための導線。
    ///
    /// データを変えるのは**利用者の明示操作**（HF77 に抵触しない）。<see cref="ApplyStructureWithMessage"/>
    /// 経由なので Undo 可・自動再診断・自動保存される。同じ並びが重複登録されている場合は全件まとめて消す。
    ///
    /// キーは <c>Problem.ResolveC3</c> と同じ意味論（**最初の空白まで**を本体とする）で作る。
    /// </summary>
    public void RelaxForbiddenRule(string seqLabel)
    {
        if (OptimizeInFlight())
        {
            Ui.MessageIsError = true;
            Ui.Message = $"{BusyWhat()}の実行中は設定を変更できません（完了後にもう一度お試しください）";
            return;
        }
        var s = _state;
        if (s is null) return;
        static string Key(C3Row row)
        {
            var end = row.Pattern.ToList().FindIndex(x => x.Length == 0);
            var body = end >= 0 ? row.Pattern.Take(end) : row.Pattern;
            return string.Join("→", body);
        }
        var remain = s.Cons3n.Where(r => Key(r) != seqLabel).ToList();
        var removed = s.Cons3n.Count - remain.Count;
        if (removed == 0)
        {
            Ui.MessageIsError = true;
            Ui.Message = $"禁止の並び「{seqLabel}」は見つかりませんでした";
            return;
        }
        LogOp("I", $"禁止の並びを削除: {seqLabel}（{removed}件）");
        ApplyStructureWithMessage(s with { Cons3n = remain }, $"禁止の並び「{seqLabel}」を削除しました（{removed}件・元に戻せます）");
    }

    // ===== 小さな辞書ユーティリティ（Kotlin の `Map<K,V> - key` に相当） =====

    private static IReadOnlyDictionary<string, string> Without(IReadOnlyDictionary<string, string> m, string key)
    {
        if (!m.ContainsKey(key)) return m;
        var d = new Dictionary<string, string>(m);
        d.Remove(key);
        return d;
    }

    private static IReadOnlyDictionary<string, int> Without(IReadOnlyDictionary<string, int> m, string key)
    {
        if (!m.ContainsKey(key)) return m;
        var d = new Dictionary<string, int>(m);
        d.Remove(key);
        return d;
    }

    private static IReadOnlyDictionary<string, MagiEngine.Model.Range> Without(IReadOnlyDictionary<string, MagiEngine.Model.Range> m, string key)
    {
        if (!m.ContainsKey(key)) return m;
        var d = new Dictionary<string, MagiEngine.Model.Range>(m);
        d.Remove(key);
        return d;
    }
}
