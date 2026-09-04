using System.Collections.Generic;
using System.Linq;
using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiApp.ViewModels;

/// <summary>
/// [フェーズ9] <c>MagiViewModel.kt</c> の移植先——ピース9。
///
/// このファイルが担う範囲: <c>Ws1Result</c>（構造+勤務表を同時に返す変更）を介する
/// 年間マスター(ws1)の追加/改名/削除系一式（<c>ws1EditShift</c>〜<c>ws1RemoveGroup</c>）、
/// スキルグループCRUD、対象月のナビゲーション（<c>setMonth</c>/<c>shiftMonth</c>/
/// <c>setNextMonth</c>）、セル(i,j)の違反が指す窓/連の範囲を返す<c>violationRange</c>、
/// および参照件数クエリ（<c>ws1ShiftRefCount</c>等）。あわせて、この一連が共有する構造編集の
/// 土台のうち Piece8 にまだ無かった2つ——<see cref="ApplyStructure(Ws1Result)"/>/
/// <see cref="ApplyStructureWithMessage(Ws1Result, string)"/>——をここへ追加する
/// （Piece8 の <c>ApplyStructure(MagiState)</c>/<c>ApplyStructureWithMessage(MagiState, string)</c>
/// と対になる、<see cref="Ws1Result"/> 版のオーバーロード）。
///
/// [ApplyStructureWithMessage(Ws1Result, string) の呼出元について] Kotlin原本(行2613)の唯一の
/// 呼出元は行3191（CSV取込のうち職員一覧の取込。Piece9の範囲=行2599-2865の外＝未移植の後続ピース）
/// だが、この定義自体は行2599-2865の一体の塊（ws1初期設定セクション）の一部としてここで移植する
/// （後続ピースがすぐ使える形で先に用意しておく）。async本体は Piece8 の
/// <c>ApplyStructureWithMessageCoreAsync</c> と完全に同一構造のため、そちらへ委譲する（複製しない）。
///
/// [editRev について] Piece4のクラスKDoc/Piece8の同旨コメント参照——WinUI3では不要なため移植しない。
/// </summary>
public sealed partial class MagiViewModel
{
    // ===== Ws1Result を介する構造編集の土台 =====

    private void ApplyStructure(Ws1Result r)
    {
        if (StructuralEditBlocked()) return;
        PushUndo();
        _state = r.State;
        // Ws1Result.Schedule を防御コピーして取り込む（Piece8 の ApplyStructure(MagiState) 内コメント
        // 参照——currentSchedule を以降 in-place 編集する経路があるため、全 schedule 取り込み口を
        // Copy2D() で統一して別名共有を断つ）。
        _currentSchedule = r.Schedule.Copy2D();
        Ui.StructureEdited = true;
        RefreshCheck();
        AutoSave();
    }

    /// <summary>
    /// Ws1Result(状態+勤務表)を適用し、再チェック後に独自メッセージを表示（スタッフ新規追加など
    /// 行数変化を伴う取込で使用）。async本体は Piece8 の
    /// <see cref="ApplyStructureWithMessageCoreAsync"/> と完全に同一構造のため、そちらへ委譲する。
    /// [テスト可視性のための追加] Kotlin原本(行2613)の唯一の呼出元は後続ピース（未移植のCSV取込、
    /// 行3191）にあるため、Piece9の時点ではこの経路を呼ぶ公開APIが存在しない。<c>internal</c> にして
    /// テストから直接叩けるようにする（<see cref="LastApplyStructureWithMessageTask"/> と同じ理由）。
    /// </summary>
    internal void ApplyStructureWithMessage(Ws1Result r, string doneMessage)
    {
        if (StructuralEditBlocked()) return;
        PushUndo();
        _state = r.State;
        var sched = r.Schedule.Copy2D();
        _currentSchedule = sched;
        AutoSave();
        var seq = ++_checkSeq;
        _checkCts?.Cancel();
        Ui.MessageIsError = false;
        Ui.Running = true;
        Ui.StructureEdited = true;
        Ui.Message = $"{doneMessage}（違反チェック中…）";
        var cts = new System.Threading.CancellationTokenSource();
        _checkCts = cts;
        LastApplyStructureWithMessageTask = ApplyStructureWithMessageCoreAsync(r.State, sched, doneMessage, seq, cts.Token);
    }

    // ===== ws1 initial setup: 編集 =====

    public void Ws1EditShift(int k, string name, string kigou, string need1, string need2)
    {
        var st = _state;
        if (st is null) return;
        if (SymbolTaken(st.Shifts.Select(x => x.Kigou).ToList(), kigou, "シフト", exceptIndex: k)) return;
        // [3.416.0] 休シフトの改名禁止（旧R-04ガード）は方針「休は通常のシフト定義」により撤回済み。
        //   改名は他シフトと同じ経路——記号「休」が無くなった場合の帰結は検査2gが案内する。
        LogOp("I", $"シフト編集: {OpSy(k)} → {name.Trim()}({kigou.Trim()}) 最低{DashIfBlank(need1)}/上限{DashIfBlank(need2)}");
        ApplyStructure(Ws1Ops.EditShift(st, k, name.Trim(), kigou.Trim(), need1.Trim(), need2.Trim()));
    }

    /// <summary>[必要人数カレンダー] シフト既定のneed1/need2だけをその場で編集する（name/kigouは不変）。
    /// Ws1EditShiftの狭い版——NeedCalendarCardの「基本の必要人数」インライン編集用。</summary>
    public void SetShiftNeed(int k, string need1, string need2)
    {
        var st = _state;
        if (st is null) return;
        var sh = k >= 0 && k < st.Shifts.Count ? st.Shifts[k] : null;
        if (sh is null) return;
        LogOp("I", $"必要人数編集: {OpSy(k)} → 最低{DashIfBlank(need1)}/上限{DashIfBlank(need2)}");
        ApplyStructure(Ws1Ops.EditShift(st, k, sh.Name, sh.Kigou, need1.Trim(), need2.Trim()));
    }

    public void Ws1EditGroup(int g, string name, string kigou)
    {
        var st = _state;
        if (st is null) return;
        if (SymbolTaken(st.Groups.Select(x => x.Kigou).ToList(), kigou, "グループ", exceptIndex: g)) return;
        LogOp("I", $"グループ編集: [{g}] → {name.Trim()}({kigou.Trim()})");
        ApplyStructure(Ws1Ops.EditGroup(st, g, name.Trim(), kigou.Trim()));
    }

    public void Ws1EditStaff(int i, string name, int groupIdx)
    {
        var st = _state;
        if (st is null) return;
        LogOp("I", $"職員編集: {OpNm(i)} → {name.Trim()} / グループ[{groupIdx}]");
        ApplyStructure(Ws1Ops.EditStaff(st, i, name.Trim(), groupIdx));
    }

    public void Ws1SetGroupShift(int g, int k, bool allowed)
    {
        var st = _state;
        if (st is null) return;
        var ns = Ws1Ops.SetGroupShift(st, g, k, allowed);
        if (ReferenceEquals(ns, st))
        {
            // [レビュー指摘 2026-09-04] 単一セルでも休は外せない（列一括と同じ理由・同じ案内）。
            if (!allowed && k == ScheduleUtil.RestShiftIndex(st))
                Notify("「休」はどのグループからも外せません（担当できるシフトが無い群を作らないため）", "W");
            return;
        }
        LogOp("I", $"担当可否: グループ[{g}] × {OpSy(k)} → {(allowed ? "担当できる" : "担当しない")}");
        ApplyStructure(ns);
    }

    /// <summary>[マトリックス一括] 群 g の全シフトを一括ON/OFF（行ヘッダのタップ）。OFF でも休は残る。</summary>
    public void Ws1SetGroupShiftRow(int g, bool allowed)
    {
        var st = _state;
        if (st is null) return;
        var name = g >= 0 && g < st.Groups.Count ? st.Groups[g].Name : $"[{g}]";
        LogOp("I", $"担当可否(一括): グループ {name} の全シフト → {(allowed ? "担当できる" : "担当しない（休は残す）")}");
        ApplyStructure(Ws1Ops.SetGroupShiftRow(st, g, allowed));
    }

    /// <summary>[マトリックス一括] シフト k を全群へ一括ON/OFF（列ヘッダのタップ）。休の列は OFF にできない。</summary>
    public void Ws1SetGroupShiftColumn(int k, bool allowed)
    {
        var st = _state;
        if (st is null) return;
        var ns = Ws1Ops.SetGroupShiftColumn(st, k, allowed);
        if (ReferenceEquals(ns, st))
        {
            if (!allowed && k == ScheduleUtil.RestShiftIndex(st))
                Notify("「休」はどのグループからも外せません（担当できるシフトが無い群を作らないため）", "W");
            return;
        }
        LogOp("I", $"担当可否(一括): {OpSy(k)} を全グループ → {(allowed ? "担当できる" : "担当しない")}");
        ApplyStructure(ns);
    }

    /// <summary>グループ別シフトの適切回数（1人あたり期間内目標。空欄＝目標なし）を設定。</summary>
    public void Ws1SetGroupApt(int g, int k, string value)
    {
        var st = _state;
        if (st is null) return;
        var t = value.Trim();
        LogOp("I", $"適切回数: グループ[{g}] × {OpSy(k)} → {(t.Length == 0 ? "未設定" : t)}");
        ApplyStructure(Ws1Ops.SetGroupApt(st, g, k, value));
    }

    /// <summary>
    /// [apt強制リセット] 適切回数(apt)を全グループ×全シフトで空欄(目標なし)に戻す。
    /// apt由来のソフト違反が消える。担当ON/OFF・回数レンジ・勤務表は不変、表も保持。元に戻すで復帰可。
    /// </summary>
    public void Ws1ResetGroupApt()
    {
        var st = _state;
        if (st is null) return;
        var cleared = st.GroupShiftApt.Sum(row => row.Count(x => x.Trim().Length > 0));
        LogOp("I", $"apt強制リセット: 適切回数を全空欄に（{cleared} 件クリア）");
        // Ws1Ops.ResetGroupApt は MagiState を返す→Piece8の ApplyStructureWithMessage(MagiState,...) へ解決される。
        ApplyStructureWithMessage(Ws1Ops.ResetGroupApt(st), $"適切回数(apt)を全リセットしました（{cleared} 件 → 0）");
    }

    public void Ws1SetUse2(bool on)
    {
        var st = _state;
        if (st is null) return;
        LogOp("I", $"設定変更: 上限人数(2パターン目) → {(on ? "使う" : "使わない")}");
        ApplyStructure(Ws1Ops.SetUse2(st, on));
    }

    /// <summary>
    /// [3.410.0/W-01・W-02] 記号の重複を入力時に断る。既存の記号へ改名すると制約行が一括置換されて
    /// 別の行と合流し、改名し直しても戻らない（検査8が事後に警告するが、そのときには手遅れ）。
    /// </summary>
    private bool SymbolTaken(IReadOnlyList<string> existing, string kigou, string what, int exceptIndex = -1)
    {
        if (!Ws1Ops.SymbolCollides(existing, kigou, exceptIndex)) return false;
        var k = kigou.Trim();
        Notify($"記号「{k}」はすでに別の{what}で使われています（制約の参照が混ざるため、別の記号にしてください）", "W");
        return true;
    }

    // ===== ws1 initial setup: 追加 =====

    public void Ws1AddShift(string name, string kigou, string need1, string need2)
    {
        var st = _state;
        if (st is null) return;
        if (kigou.Trim().Length == 0) return;
        if (SymbolTaken(st.Shifts.Select(x => x.Kigou).ToList(), kigou, "シフト")) return;
        LogOp("I", $"シフト追加: {name.Trim()}({kigou.Trim()}) 最低{DashIfBlank(need1)}/上限{DashIfBlank(need2)}");
        ApplyStructure(Ws1Ops.AddShift(st, name.Trim(), kigou.Trim(), need1.Trim(), need2.Trim()));
    }

    public void Ws1AddGroup(string name, string kigou)
    {
        var st = _state;
        if (st is null) return;
        if (kigou.Trim().Length == 0) return;
        if (SymbolTaken(st.Groups.Select(x => x.Kigou).ToList(), kigou, "グループ")) return;
        LogOp("I", $"グループ追加: {name.Trim()}({kigou.Trim()})");
        ApplyStructure(Ws1Ops.AddGroup(st, name.Trim(), kigou.Trim()));
    }

    public void Ws1AddStaff(string name, int groupIdx)
    {
        var st = _state;
        if (st is null) return;
        var sched = _currentSchedule;
        if (sched is null) return;
        LogOp("I", $"職員追加: {name.Trim()} / グループ[{groupIdx}]");
        ApplyStructure(Ws1Ops.AddStaff(st, sched, name.Trim(), groupIdx));
    }

    public void Ws1ResizeDays(int newT)
    {
        var st = _state;
        if (st is null) return;
        var sched = _currentSchedule;
        if (sched is null) return;
        LogOp("I", $"期間変更: {st.DayCount}日 → {newT}日");
        ApplyStructure(Ws1Ops.ResizeDays(st, sched, newT));
    }

    // ===== 対象月のナビゲーション =====

    /// <summary>[対象月の選択] 開始日を指定年月の1日にし、その月の日数へ整える（endDate/希望/必要人数も追従）。</summary>
    public void SetMonth(int year, int month1To12)
    {
        var st = _state;
        if (st is null) return;
        var sched = _currentSchedule;
        if (sched is null) return;
        DateOnly first;
        try
        {
            first = new DateOnly(year, month1To12, 1);
        }
        catch (System.ArgumentOutOfRangeException)
        {
            return;
        }
        LogOp("I", $"期間変更: {year}年{month1To12}月");
        var startDate = first.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var days = System.DateTime.DaysInMonth(year, month1To12);
        ApplyStructure(Ws1Ops.ResizeDays(st with { StartDate = startDate }, sched, days));
    }

    /// <summary>現在の開始日から相対的に月を移動（-1=前月 / +1=翌月）。開始日が不明なら端末の今月を起点。</summary>
    public void ShiftMonth(int delta)
    {
        DateOnly firstOfBase;
        if (DateOnly.TryParseExact(
                Ui.StartDate, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var parsed))
        {
            firstOfBase = new DateOnly(parsed.Year, parsed.Month, 1);
        }
        else
        {
            var today = DateOnly.FromDateTime(System.DateTime.Now);
            firstOfBase = new DateOnly(today.Year, today.Month, 1);
        }
        var m = firstOfBase.AddMonths(delta);
        SetMonth(m.Year, m.Month);
    }

    /// <summary>[実機指摘] 月末に「来月」の勤務表を作る業務のため、ワンタップは来月が適切。</summary>
    public void SetNextMonth()
    {
        var next = DateOnly.FromDateTime(System.DateTime.Now).AddMonths(1);
        SetMonth(next.Year, next.Month);
    }

    // ===== スキルグループ（年次マスター・新C41s/C42s 専用） =====

    public IReadOnlyList<Group> SkillGroups() => _state?.SkillGroups ?? System.Array.Empty<Group>();

    public void AddSkillGroup(string name, string kigou)
    {
        var st = _state;
        if (st is null) return;
        if (kigou.Trim().Length == 0) return;
        if (SymbolTaken(st.SkillGroups.Select(x => x.Kigou).ToList(), kigou, "スキル区分")) return;
        LogOp("I", $"スキル区分追加: {name.Trim()}({kigou.Trim()})");
        ApplyStructure(st with { SkillGroups = st.SkillGroups.Append(new Group(name.Trim(), kigou.Trim())).ToList() });
    }

    public void EditSkillGroup(int g, string name, string kigou)
    {
        var st = _state;
        if (st is null) return;
        if (SymbolTaken(st.SkillGroups.Select(x => x.Kigou).ToList(), kigou, "スキル区分", exceptIndex: g)) return;
        var old = g >= 0 && g < st.SkillGroups.Count ? st.SkillGroups[g].Kigou : "";
        var renamed = st with
        {
            SkillGroups = st.SkillGroups.Select((x, i) => i == g ? new Group(name.Trim(), kigou.Trim()) : x).ToList()
        };
        LogOp("I", $"スキル区分編集: [{g}] → {name.Trim()}({kigou.Trim()})");
        // [記号変更の伝播] スキル群記号を変えたら cons41s/cons42s の参照も一括置換(幽霊行防止)
        ApplyStructure(Ws1Ops.RenameSkillGroupInConstraints(renamed, old, kigou.Trim()));
    }

    public void RemoveSkillGroup(int g)
    {
        var st = _state;
        if (st is null) return;
        // 移行規則は Ws1Ops.RemoveSkillGroup（担当グループの RemoveGroup と対）。
        LogOp("I", $"スキル区分削除: [{g}]");
        ApplyStructure(Ws1Ops.RemoveSkillGroup(st, g));
    }

    public void SetStaffSkill(int i, int skillIdx)
    {
        var st = _state;
        if (st is null) return;
        LogOp("I", $"スキル割当: {OpNm(i)} → 区分[{skillIdx}]");
        ApplyStructure(st with
        {
            StaffList = st.StaffList.Select((s, idx) => idx == i ? s with { SkillIdx = skillIdx } : s).ToList()
        });
    }

    // ===== 削除確認・参照件数クエリ =====

    /// <summary>グループを削除できるか（2グループ以上あれば可。所属者がいても先頭グループへ移動して削除）。</summary>
    public bool Ws1CanRemoveGroup(int g)
    {
        var st = _state;
        if (st is null) return false;
        return g >= 0 && g < st.Groups.Count && st.Groups.Count > 1;
    }

    /// <summary>グループgの所属人数（削除確認の警告表示用）。</summary>
    public int Ws1GroupMemberCount(int g) => _state?.StaffList.Count(s => s.GroupIdx == g) ?? 0;

    /// <summary>[3.429.0/R-03] 削除確認ダイアログで見せる影響件数（Ws1Ops.ShiftRefCount/GroupRefCount へ委譲）。
    /// 対象のシフト/グループを参照する制約行数。0件なら影響なし。</summary>
    public int Ws1ShiftRefCount(int k)
    {
        var st = _state;
        if (st is null || k < 0 || k >= st.Shifts.Count) return 0;
        return Ws1Ops.ShiftRefCount(st, st.Shifts[k].Kigou);
    }

    public int Ws1GroupRefCount(int g)
    {
        var st = _state;
        if (st is null || g < 0 || g >= st.Groups.Count) return 0;
        return Ws1Ops.GroupRefCount(st, st.Groups[g].Kigou);
    }

    public int Ws1SkillGroupRefCount(int g)
    {
        var st = _state;
        if (st is null || g < 0 || g >= st.SkillGroups.Count) return 0;
        return Ws1Ops.SkillGroupRefCount(st, st.SkillGroups[g].Kigou);
    }

    // ===== 窓ハイライト =====

    /// <summary>[窓ハイライト③] セル(i,j)の違反が c1/c3/c3m のとき、その違反が指す窓/連の範囲
    /// (開始日..終了日、0起点・両端含む)を返す。c1=最初に不足している窓 / c3・c3m=複数シフト窓なら
    /// 未完成パターンの窓、単一シフト連なら連の実範囲。該当なし・他族は null（読み取り専用・表示のみ）。</summary>
    public (int Start, int End)? ViolationRange(int i, int j)
    {
        var st = _state;
        if (st is null) return null;
        var sched = _currentSchedule;
        if (sched is null) return null;
        if (!Ui.ViolationCells.TryGetValue($"{i},{j}", out var cls)) return null;
        var p = ScheduleUtil.CachedProblem(st);
        if (i < 0 || i >= p.S || j < 0 || j >= p.T) return null;
        switch (cls)
        {
            case "vio-c1":
                foreach (var c in p.Cons1)
                {
                    if (!p.CanDo(i, c.ShiftIdx) || j + c.Day1 > p.T) continue;
                    var z = 0;
                    for (var l = 0; l < c.Day1; l++)
                    {
                        if (sched[i][j + l] == c.ShiftIdx) z++;
                    }
                    if (z < c.Day2) return (j, j + c.Day1 - 1);
                }
                break;
            case "vio-c3":
            case "vio-c3m":
            {
                var k0 = sched[i][j];
                var lists = cls == "vio-c3" ? p.Cons3 : p.Cons3m;
                foreach (var c in lists)
                {
                    var seq = c.Seq;
                    if (seq.Length < 2 || seq[0] != k0 || j + seq.Length > p.T) continue;
                    var ok = true;
                    for (var l = 1; l < seq.Length; l++)
                    {
                        if (sched[i][j + l] != seq[l]) { ok = false; break; }
                    }
                    if (!ok) return (j, j + seq.Length - 1); // 未完成パターン=この窓が違反
                }
                var end = j;
                while (end + 1 < p.T && sched[i][end + 1] == k0) end++; // 単一シフト連の実範囲
                if (end > j) return (j, end);
                break;
            }
        }
        return null;
    }

    // ===== ws1 initial setup: 削除 =====

    public void Ws1RemoveShift(int k)
    {
        var st = _state;
        if (st is null) return;
        var sched = _currentSchedule;
        if (sched is null) return;
        // [3.416.0/方針「休は通常のシフト定義」] 旧: 休シフトの削除を入口で拒否（3.106.0）。撤廃＝
        //   休も他シフトと同じ編集規則。削除セルは残りの一覧の「休」（無ければ先頭シフト）へ。
        LogOp("I", $"シフト削除: {OpSy(k)}（このシフトのマスは休（無ければ先頭シフト）へ・希望も削除）");
        ApplyStructure(Ws1Ops.RemoveShift(st, sched, k));
    }

    public void Ws1RemoveStaff(int i)
    {
        var st = _state;
        if (st is null) return;
        var sched = _currentSchedule;
        if (sched is null) return;
        LogOp("I", $"職員削除: {OpNm(i)}（勤務行・希望・個人の回数も削除）");
        ApplyStructure(Ws1Ops.RemoveStaff(st, sched, i));
    }

    public void Ws1RemoveGroup(int g)
    {
        var st = _state;
        if (st is null) return;
        if (g < 0 || g >= st.Groups.Count || st.Groups.Count <= 1) return;
        // 所属者は先頭グループへ移る＝担当できるシフトが黙って変わるので、人数を必ず記録する。
        var moved = st.StaffList.Count(s => s.GroupIdx == g);
        var suffix = moved > 0 ? $"（所属{moved}名は先頭グループへ移動＝担当できるシフトが変わります）" : "";
        LogOp("I", $"グループ削除: [{g}]{suffix}");
        ApplyStructure(Ws1Ops.RemoveGroup(st, g));
    }

    /// <summary>Kotlin原本の <c>.trim().ifBlank { "-" }</c> パターン。</summary>
    private static string DashIfBlank(string s)
    {
        var t = s.Trim();
        return t.Length == 0 ? "-" : t;
    }
}
