using System.Globalization;
using System.Linq;
using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// ws1（初期設定）モデル操作: 問題定義（シフト・グループ・職員・期間・群×シフトのバケツ・use2フラグ）を
/// 編集する（<c>Ws1Ops.kt</c> の逐語移植）。次元（S/T/K）を変える操作は、index参照する全構造を一貫して
/// 再次元化し、結果が評価可能なままであることを保証する（Level Zero データモデルおよび数値プロトタイプで
/// 検証済み＝state の整合性・fullEval の計算可能性を維持）。
///
/// 各操作は現在の (state, 作業中の schedule) を受け取り新しい組を返す。<see cref="Ws1Result.Schedule"/> は
/// 返した state の次元と常に一致する。
/// </summary>
public sealed record Ws1Result(MagiState State, int[][] Schedule);

public static class Ws1Ops
{
    // Kotlin 原本の private `withSchedule`/`copyGrid` はここでは再実装しない。挙動は既に
    // ScheduleUtil.WithSchedule(state, sched) / sched.Copy2D() と完全に同一（フェーズ2〜7で移植済み）
    // なので、呼び出し側はそれらを直接使う（写せば必ず取り残される＝写さない）。

    /// <summary>
    /// Kotlin の <c>List&lt;T&gt;.getOrNull(idx)</c> 相当（範囲外・負の index は null）。参照型のみ対象
    /// （このファイルの呼出は全て <c>Staff</c>／行(<c>IReadOnlyList&lt;int&gt;</c> 等)のような参照型）。
    /// </summary>
    private static T? GetOrNull<T>(IReadOnlyList<T> list, int idx) where T : class =>
        idx >= 0 && idx < list.Count ? list[idx] : null;

    // ---- no dimension change -------------------------------------------------

    public static MagiState EditShift(MagiState state, int k, string name, string kigou, string need1, string need2)
    {
        if (k < 0 || k >= state.Shifts.Count) return state;
        var old = state.Shifts[k].Kigou;
        var s = new List<Shift>(state.Shifts) { [k] = new Shift(name, kigou, need1, need2) };
        // [記号変更の伝播] 制約はシフト記号(文字列)で参照するため、記号を変えたら参照行も一括置換し
        //   旧記号の幽霊行化(評価では無視されるが表示に残る)を防ぐ。index保存(staffRange/希望/apt/勤務表)は
        //   indexで参照するため自動追従＝対象外。
        return RenameShiftInConstraints(state with { Shifts = s }, old, kigou);
    }

    public static MagiState EditGroup(MagiState state, int g, string name, string kigou)
    {
        if (g < 0 || g >= state.Groups.Count) return state;
        var old = state.Groups[g].Kigou;
        var gl = new List<Group>(state.Groups) { [g] = new Group(name, kigou) };
        // [記号変更の伝播] cons41/cons42 は群記号で参照。cons41s/cons42s(スキル群)は別系統で対象外。
        return RenameGroupInConstraints(state with { Groups = gl }, old, kigou);
    }

    // [記号変更の伝播] 制約は記号(kigou)文字列で参照するため、シフト/群/スキル群の記号を変えたら
    //   参照する制約行も一括置換し、旧記号の幽霊行化を防ぐ。old空 or old==newKigou は no-op。
    private static MagiState RenameShiftInConstraints(MagiState s, string old, string newKigou)
    {
        if (string.IsNullOrWhiteSpace(old) || old == newKigou) return s;
        List<string> Pat(IReadOnlyList<string> p) => p.Select(x => x == old ? newKigou : x).ToList();
        return s with
        {
            Cons1 = s.Cons1.Select(c => c.ShiftKigou == old ? c with { ShiftKigou = newKigou } : c).ToList(),
            Cons2 = s.Cons2.Select(c => c.ShiftKigou == old ? c with { ShiftKigou = newKigou } : c).ToList(),
            Cons3 = s.Cons3.Select(c => c with { Pattern = Pat(c.Pattern) }).ToList(),
            Cons3n = s.Cons3n.Select(c => c with { Pattern = Pat(c.Pattern) }).ToList(),
            Cons3m = s.Cons3m.Select(c => c with { Pattern = Pat(c.Pattern) }).ToList(),
            Cons3mn = s.Cons3mn.Select(c => c with { Pattern = Pat(c.Pattern) }).ToList(),
            Cons41 = s.Cons41.Select(c => c.ShiftKigou == old ? c with { ShiftKigou = newKigou } : c).ToList(),
            Cons41s = s.Cons41s.Select(c => c.ShiftKigou == old ? c with { ShiftKigou = newKigou } : c).ToList(),
            Cons42 = s.Cons42.Select(c => c with
            {
                S1Kigou = c.S1Kigou == old ? newKigou : c.S1Kigou,
                S2Kigou = c.S2Kigou == old ? newKigou : c.S2Kigou,
            }).ToList(),
            Cons42s = s.Cons42s.Select(c => c with
            {
                S1Kigou = c.S1Kigou == old ? newKigou : c.S1Kigou,
                S2Kigou = c.S2Kigou == old ? newKigou : c.S2Kigou,
            }).ToList(),
        };
    }

    private static MagiState RenameGroupInConstraints(MagiState s, string old, string newKigou)
    {
        if (string.IsNullOrWhiteSpace(old) || old == newKigou) return s;
        return s with
        {
            Cons41 = s.Cons41.Select(c => c.GroupKigou == old ? c with { GroupKigou = newKigou } : c).ToList(),
            Cons42 = s.Cons42.Select(c => c with
            {
                G1Kigou = c.G1Kigou == old ? newKigou : c.G1Kigou,
                G2Kigou = c.G2Kigou == old ? newKigou : c.G2Kigou,
            }).ToList(),
        };
    }

    /// <summary>スキル群の記号改名を制約（cons41s/cons42s）へ伝播する。外部（ViewModel 層）からも呼ぶため public。</summary>
    public static MagiState RenameSkillGroupInConstraints(MagiState s, string old, string newKigou)
    {
        if (string.IsNullOrWhiteSpace(old) || old == newKigou) return s;
        return s with
        {
            Cons41s = s.Cons41s.Select(c => c.GroupKigou == old ? c with { GroupKigou = newKigou } : c).ToList(),
            Cons42s = s.Cons42s.Select(c => c with
            {
                G1Kigou = c.G1Kigou == old ? newKigou : c.G1Kigou,
                G2Kigou = c.G2Kigou == old ? newKigou : c.G2Kigou,
            }).ToList(),
        };
    }

    public static MagiState EditStaff(MagiState state, int i, string name, int groupIdx)
    {
        if (i < 0 || i >= state.StaffList.Count) return state;
        int gi = Math.Clamp(groupIdx, 0, Math.Max(state.Groups.Count - 1, 0));
        var sl = new List<Staff>(state.StaffList);
        // [P1修正/レビュー指摘] 名前だけ直しても skillIdx が既定0へ戻らないよう、既存のコピーで保持する
        // （旧 `new Staff(name, gi)` は skillIdx を既定0へ戻し、スキル区分が無言で消えて
        //   cons41s/cons42s の評価が変わっていた）。
        sl[i] = sl[i] with { Name = name, GroupIdx = gi };
        return state with { StaffList = sl };
    }

    public static MagiState SetGroupShift(MagiState state, int g, int k, bool allowed)
    {
        if (g < 0 || g >= state.GroupShift.Count) return state;
        var grid = state.GroupShift.Select(row => row.ToList()).ToList();
        if (k < 0 || k >= grid[g].Count) return state;
        grid[g][k] = allowed ? 1 : 0;
        return state with { GroupShift = grid };
    }

    /// <summary>
    /// [マトリックス一括] 群 g の全シフトを一括で担当ON/OFF（行ヘッダ＝群名のタップ）。
    /// OFF のときも休(<see cref="ScheduleUtil.RestShiftIndex"/>)は残す＝担当可能シフトが1つも無い群は
    /// validate が拒否し（「groupShift[g] に担当可能シフトがありません」）、その群の職員は行ごと
    /// groupViol(HARD) になるため（3.418.0/3.442.0 と同じ理由）。Kotlin 原本 <c>Ws1Ops.setGroupShiftRow</c> と同値。
    /// </summary>
    public static MagiState SetGroupShiftRow(MagiState state, int g, bool allowed)
    {
        if (g < 0 || g >= state.GroupShift.Count) return state;
        var rest = ScheduleUtil.RestShiftIndex(state);
        var grid = state.GroupShift.Select(row => row.ToList()).ToList();
        for (int k = 0; k < grid[g].Count; k++) grid[g][k] = (allowed || k == rest) ? 1 : 0;
        return state with { GroupShift = grid };
    }

    /// <summary>
    /// [マトリックス一括] シフト k を全群へ一括で担当ON/OFF（列ヘッダ＝シフト名のタップ）。
    /// 休の列を OFF にする操作は**無変更で返す**（全群から休が消える＝上と同じ理由）。呼出側
    /// （ViewModel）は <c>ReferenceEquals</c> で拒否を検知して理由を案内する。
    /// </summary>
    public static MagiState SetGroupShiftColumn(MagiState state, int k, bool allowed)
    {
        if (state.GroupShift.Count == 0) return state;
        if (!allowed && k == ScheduleUtil.RestShiftIndex(state)) return state;
        var grid = state.GroupShift.Select(row => row.ToList()).ToList();
        if (k < 0 || grid.Any(row => k >= row.Count)) return state;
        foreach (var row in grid) row[k] = allowed ? 1 : 0;
        return state with { GroupShift = grid };
    }

    /// <summary>
    /// グループ別シフトの「適切回数 (groupShiftApt)」を1セル設定。Web版の
    /// 「グループ別 担当シフトと適切回数」エディタ相当。1人あたりの期間内目標回数（空欄＝目標なし）。
    /// groupShiftApt が未初期化/不揃いでも G×K に正規化してから設定する。
    /// </summary>
    public static MagiState SetGroupApt(MagiState state, int g, int k, string value)
    {
        if (g < 0 || g >= state.Groups.Count) return state;
        int kCount = state.Shifts.Count;
        if (k < 0 || k >= kCount) return state;
        var grid = new List<List<string>>();
        for (int gi = 0; gi < state.Groups.Count; gi++)
        {
            var row = gi < state.GroupShiftApt.Count ? state.GroupShiftApt[gi] : null;
            var newRow = new List<string>();
            for (int kk = 0; kk < kCount; kk++)
                newRow.Add(row is not null && kk < row.Count ? row[kk] : "");
            grid.Add(newRow);
        }
        grid[g][k] = value.Trim();
        return state with { GroupShiftApt = grid };
    }

    /// <summary>
    /// [apt強制リセット] グループ別シフトの適切回数(groupShiftApt)を全て空欄(=目標なし)に戻す。
    /// G×K に正規化したうえで全セルを "" にする。apt 由来のソフト違反は消えるが、
    /// 担当ON/OFF(groupShift)・回数レンジ・勤務表・シフト/グループ定義は一切変更しない。
    /// </summary>
    public static MagiState ResetGroupApt(MagiState state)
    {
        var grid = Enumerable.Range(0, state.Groups.Count)
            .Select(_ => (IReadOnlyList<string>)Enumerable.Repeat("", state.Shifts.Count).ToList())
            .ToList();
        return state with { GroupShiftApt = grid };
    }

    public static MagiState SetUse2(MagiState state, bool on) => state with { Use2Patterns = on };

    // ---- append (low-risk dimension change, no re-indexing) ------------------

    /// <summary>
    /// [3.410.0/W-01・W-02] 記号がすでに他の行で使われているか。**追加と改名の入口で使う**。
    ///
    /// 制約（cons1/2/3系/41/42）はシフト・群を**記号の文字列**で参照するので、既存の記号へ改名すると
    /// <see cref="RenameShiftInConstraints"/> が旧記号の行を新記号へ一括置換し、**別の行のルールと合流する**。
    /// しかもこの合流は改名し直しても戻らない（戻すと相手側のルールまで巻き添えで改名される）＝
    /// 取り返しがつかない。検査8（3.106.0）は事後に「重複しています」と警告するが、そのときには
    /// もう合流が済んでいる。3.403.0 で「下限>上限」を入力時に止めたのと同じ理由で、ここで止める。
    ///
    /// 比較は <c>Trim()</c> 込み（P-11: <c>Problem.ShiftIdxOf</c> は完全一致・CSV 照合の <c>FirstWinsMap</c>
    /// は trim と揺れているので、**そもそも紛らわしい組を作らせない**方向へ倒す）。
    /// </summary>
    /// <param name="exceptIndex">改名では自分自身を除く（自分と同じ記号のままの確定を拒否しない）。</param>
    public static bool SymbolCollides(IReadOnlyList<string> existing, string kigou, int exceptIndex = -1)
    {
        var k = kigou.Trim();
        if (k.Length == 0) return false;
        for (int i = 0; i < existing.Count; i++)
            if (i != exceptIndex && existing[i].Trim() == k) return true;
        return false;
    }

    /// <summary>
    /// [3.429.0/R-03] シフト削除の確認ダイアログへ渡す影響件数。<c>Problem.ShiftIdxOf</c> と同じ厳密一致(==)で
    /// 数える（trim なし＝評価時の解決と完全に同じ基準）。読取専用・カウントのみ＝評価/削除の挙動には触れない。
    /// </summary>
    public static int ShiftRefCount(MagiState state, string kigou)
    {
        int n = 0;
        n += state.Cons1.Count(c => c.ShiftKigou == kigou);
        n += state.Cons2.Count(c => c.ShiftKigou == kigou);
        n += state.Cons3.Concat(state.Cons3n).Concat(state.Cons3m).Concat(state.Cons3mn)
            .Count(row => row.Pattern.Any(p => p == kigou));
        n += state.Cons41.Count(c => c.ShiftKigou == kigou);
        n += state.Cons42.Count(c => c.S1Kigou == kigou || c.S2Kigou == kigou);
        n += state.Cons41s.Count(c => c.ShiftKigou == kigou);
        n += state.Cons42s.Count(c => c.S1Kigou == kigou || c.S2Kigou == kigou);
        return n;
    }

    /// <summary>同上・グループ削除版（cons41/cons42 の groupKigou/g1Kigou/g2Kigou のみ＝スキル群は別分類のため対象外）。</summary>
    public static int GroupRefCount(MagiState state, string kigou)
    {
        int n = 0;
        n += state.Cons41.Count(c => c.GroupKigou == kigou);
        n += state.Cons42.Count(c => c.G1Kigou == kigou || c.G2Kigou == kigou);
        return n;
    }

    /// <summary>同上・スキル群削除版（cons41s/cons42s の groupKigou/g1Kigou/g2Kigou）。</summary>
    public static int SkillGroupRefCount(MagiState state, string kigou)
    {
        int n = 0;
        n += state.Cons41s.Count(c => c.GroupKigou == kigou);
        n += state.Cons42s.Count(c => c.G1Kigou == kigou || c.G2Kigou == kigou);
        return n;
    }

    public static MagiState AddShift(MagiState state, string name, string kigou, string need1, string need2)
    {
        var shifts = state.Shifts.Append(new Shift(name, kigou, need1, need2)).ToList();
        var gs = state.GroupShift.Select(row => (IReadOnlyList<int>)row.Append(0).ToList()).ToList(); // new shift not allowed by default
        var apt = state.GroupShiftApt.Count == 0
            ? state.GroupShiftApt
            : state.GroupShiftApt.Select(row => (IReadOnlyList<string>)row.Append("").ToList()).ToList();
        return state with { Shifts = shifts, GroupShift = gs, GroupShiftApt = apt };
    }

    /// <summary>Add a group (index G). groupShift/apt gain a row; staff group indices stay valid.
    ///  [review #5] The new group is allowed the 休 (rest) shift so it passes validation
    ///  (every group needs &gt;=1 doable shift); otherwise add -&gt; save -&gt; reload would be rejected.</summary>
    public static MagiState AddGroup(MagiState state, string name, string kigou)
    {
        int k = state.Shifts.Count;
        int rest = ScheduleUtil.RestShiftIndex(state);
        var groups = state.Groups.Append(new Group(name, kigou)).ToList();
        var newRow = Enumerable.Range(0, k).Select(idx => idx == rest ? 1 : 0).ToList();
        var gs = state.GroupShift.Append((IReadOnlyList<int>)newRow).ToList();
        IReadOnlyList<IReadOnlyList<string>> apt;
        if (state.GroupShiftApt.Count == 0)
        {
            apt = state.GroupShiftApt;
        }
        else
        {
            var newAptRow = Enumerable.Repeat("", k).ToList();
            apt = state.GroupShiftApt.Append((IReadOnlyList<string>)newAptRow).ToList();
        }
        return state with { Groups = groups, GroupShift = gs, GroupShiftApt = apt };
    }

    /// <summary>
    /// 空きマスを埋めるシフト index（[3.418.0] 新職員の行・伸ばした日・消したシフトのマス）。
    ///
    /// [3.418.0] 旧: 埋める側は一律 <c>restShiftIndex</c> で、**その職員がそのシフトを担当できるかを見て
    /// いなかった**。担当可否から休を外した群（UI の担当可否チップで実際にできる操作）に職員を足す／
    /// 期間を伸ばす／シフトを消すと、**埋めたマス全部が groupViol(HARD 重み10000)** になった
    /// （31日なら1クリックで必須違反31件）。最適化を回せば <c>hf67HardRepair</c> が正規化するが、
    /// その前に画面が真っ赤になる＝利用者には理由が分からない。
    ///
    /// 休を担当できるならそのまま休（需要が無く「まだ決めていない」を表すのに最も無難で、実データ3件は
    /// 全群が休を担当できるため**挙動は変わらない**）。できなければ、その群が担当できる先頭のシフト。
    /// どちらも無い（担当可能シフトが1つも無い群）なら休へ倒す＝ここで throw すると、その不整合を
    /// 直しに来た編集操作そのものがクラッシュする。この state は検査2k/2l が別途指摘する。
    ///
    /// [3.419.0] 判断そのものはこの関数が唯一の持ち場（<c>Problem.InitialAssignment</c> と規則を共有する）。
    /// </summary>
    /// <remarks>
    /// [3.442.0/H3] CSV取込(<c>StaffCsvIO.ParseUpsert</c>)も同じ判断を読むため internal 化した。
    /// 写すと必ず片方が取り残される（3.418.0/3.419.0 でこの規則を1箇所へ寄せたのと同じ理由）。
    /// </remarks>
    internal static int FillShift(IReadOnlyList<int>? groupShiftRow, int rest)
    {
        if (groupShiftRow is null) return rest;
        var allowed = new List<int>();
        for (int idx = 0; idx < groupShiftRow.Count; idx++)
            if (groupShiftRow[idx] == 1) allowed.Add(idx);
        return ScheduleUtil.FillShiftIndex(allowed.ToArray(), rest);
    }

    /// <summary>Add a staff (index S). The working schedule gains a row of the group's fill shift.
    ///  [3.329.0/外部レビュー H-01] 旧コメントは「休/idx0」と両者を同一視していたが、**休は記号で解決する**
    ///  （<see cref="ScheduleUtil.RestShiftIndex"/>）。休が先頭でないデータでは、新しい職員の空き日が丸ごと
    ///  勤務シフトになっていた。</summary>
    public static Ws1Result AddStaff(MagiState state, int[][] sched, string name, int groupIdx)
    {
        int t = sched.Length > 0 ? sched[0].Length : state.DayCount;
        int gi = Math.Clamp(groupIdx, 0, Math.Max(state.Groups.Count - 1, 0));
        var staff = state.StaffList.Append(new Staff(name, gi)).ToList();
        int fill = FillShift(GetOrNull(state.GroupShift, gi), ScheduleUtil.RestShiftIndex(state));
        var copied = sched.Copy2D();
        var newSched = new int[copied.Length + 1][];
        Array.Copy(copied, newSched, copied.Length);
        newSched[copied.Length] = Enumerable.Repeat(fill, t).ToArray();
        var ns = state with { StaffList = staff };
        return new Ws1Result(ns.WithSchedule(newSched), newSched);
    }

    // ---- period resize -------------------------------------------------------

    /// <summary>Resize the period to <paramref name="newT"/> days: schedule columns padded with 休 or
    ///  truncated; out-of-range needDay/wishes dropped; endDate recomputed from startDate.
    ///  [3.329.0/外部レビュー H-01] 追加された日も休の**記号解決**で埋める（旧: index 0 直書き）。</summary>
    public static Ws1Result ResizeDays(MagiState state, int[][] sched, int newT)
    {
        int t = Math.Clamp(newT, 1, 31);
        int rest = ScheduleUtil.RestShiftIndex(state);
        var newSched = new int[sched.Length][];
        for (int i = 0; i < sched.Length; i++)
        {
            // [3.418.0] 伸ばした日も**その職員の群が担当できる**シフトで埋める（旧: 一律 休）。
            var st = GetOrNull(state.StaffList, i);
            int gi = st?.GroupIdx ?? -1;
            int fill = FillShift(GetOrNull(state.GroupShift, gi), rest);
            var row = new int[t];
            for (int j = 0; j < t; j++)
                row[j] = j < sched[i].Length ? sched[i][j] : fill;
            newSched[i] = row;
        }

        static int DayOf(string key)
        {
            var parts = key.Split(',');
            return parts.Length > 1 ? (KotlinInterop.ToIntOrNull(parts[1]) ?? -1) : -1;
        }

        var need1 = state.NeedDay1.Where(kv => DayOf(kv.Key) is >= 0 and var d && d < t)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        var need2 = state.NeedDay2.Where(kv => DayOf(kv.Key) is >= 0 and var d && d < t)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        var wishes = state.Wishes.Where(kv => DayOf(kv.Key) is >= 0 and var d && d < t)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        string end;
        if (DateOnly.TryParseExact(state.StartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var startDateParsed))
        {
            end = startDateParsed.AddDays(t - 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        else
        {
            end = state.EndDate;
        }

        var ns = state with { NeedDay1 = need1, NeedDay2 = need2, Wishes = wishes, EndDate = end };
        return new Ws1Result(ns.WithSchedule(newSched), newSched);
    }

    // ---- remove (re-indexing; verified against a numeric prototype) ----------

    /// <summary>Remap a "a,b"-keyed map after removing index <paramref name="removed"/> from axis 0 (a)
    ///  or axis 1 (b): drop keys whose axis index == removed, decrement those greater.</summary>
    private static IReadOnlyDictionary<string, TV> ReindexKeys<TV>(IReadOnlyDictionary<string, TV> m, int axis, int removed)
    {
        var result = new Dictionary<string, TV>();
        foreach (var (key, v) in m)
        {
            var parts = key.Split(',');
            var a = KotlinInterop.ToIntOrNull(parts[0]);
            if (a is null) continue;
            if (parts.Length < 2) continue;
            var b = KotlinInterop.ToIntOrNull(parts[1]);
            if (b is null) continue;
            int idx = axis == 0 ? a.Value : b.Value;
            if (idx == removed) continue;
            if (idx > removed) idx -= 1;
            result[axis == 0 ? $"{idx},{b.Value}" : $"{a.Value},{idx}"] = v;
        }
        return result;
    }

    // [3.392.0] `CanRemoveGroup` は移植しない。呼出0のうえ RemoveGroup の実挙動と矛盾していた
    //   （「所属者がいたら削除不可」と返すが、RemoveGroup は所属者を先頭グループへ移して削除する）。
    //   実際の可否判定は UI 層（フェーズ9・ViewModel 相当）が持つ（2グループ以上あれば可）。

    /// <summary>Remove shift <paramref name="k"/>: drop the shift; schedule cells ==k -&gt; the
    ///  post-deletion default shift（削除後一覧の「休」があればそれ、無ければ担当可能な先頭シフトへ＝
    ///  <see cref="FillShift"/>）, &gt;k decremented; wish values ==k dropped, &gt;k decremented;
    ///  groupShift/apt lose column k; needDay (axis k) and staffRange (axis k) re-indexed. Constraints
    ///  referencing the removed kigou simply stop resolving (kept verbatim). No-op only if it's the
    ///  last remaining shift（[3.416.0/方針「休は通常のシフト定義」] 旧: 休シフト自体の削除も禁止していたが撤廃済み）。</summary>
    public static Ws1Result RemoveShift(MagiState state, int[][] sched, int k)
    {
        if (k < 0 || k >= state.Shifts.Count || state.Shifts.Count <= 1) return new Ws1Result(state, sched);
        var shifts = state.Shifts.Where((_, i) => i != k).ToList();
        // [3.416.0/方針「休は通常のシフト定義」] 旧: 休シフト自体の削除を no-op で禁止（3.106.0）していたが
        //   撤廃＝休も他シフトと同じ編集規則。削除セルの行き先は**削除後の一覧**で解決した既定シフト
        //   （「休」があればそれ、無ければ先頭）。k が休以外なら旧 newRest（削除後の休index追従＝3.106.0 の
        //   本体であるハードコード0バグの修正）と厳密に一致し、k が休自身でも範囲内の正しい既定へ落ちる
        //   （旧式 `rest>k ? rest-1 : rest` は k==rest のとき削除済みindexを指し、末尾削除では範囲外だった）。
        int newRest = ScheduleUtil.RestShiftIndex(state with { Shifts = shifts });
        var gs = state.GroupShift.Select(row => row.Where((_, i) => i != k).ToList()).ToList();
        var apt = state.GroupShiftApt.Count == 0
            ? state.GroupShiftApt
            : state.GroupShiftApt.Select(row => (IReadOnlyList<string>)row.Where((_, i) => i != k).ToList()).ToList();
        var newSched = new int[sched.Length][];
        for (int r = 0; r < sched.Length; r++)
        {
            // [3.418.0] 消したシフトのマスも**その職員の群が担当できる**シフトへ（旧: 一律 休）。
            //   担当可否は列を消したあとの `gs` で見る（index がずれているため）。
            var st = GetOrNull(state.StaffList, r);
            int gi = st?.GroupIdx ?? -1;
            int fill = FillShift(GetOrNull(gs, gi), newRest);
            var row = new int[sched[r].Length];
            for (int j = 0; j < row.Length; j++)
            {
                int v = sched[r][j];
                row[j] = v == k ? fill : (v > k ? v - 1 : v);
            }
            newSched[r] = row;
        }
        var wishes = new Dictionary<string, int>();
        foreach (var (key, v) in state.Wishes)
        {
            if (v == k) continue;
            wishes[key] = v > k ? v - 1 : v;
        }
        var ns = state with
        {
            Shifts = shifts,
            GroupShift = gs.Select(row => (IReadOnlyList<int>)row).ToList(),
            GroupShiftApt = apt,
            Wishes = wishes,
            NeedDay1 = ReindexKeys(state.NeedDay1, 0, k),
            NeedDay2 = ReindexKeys(state.NeedDay2, 0, k),
            StaffRange = ReindexKeys(state.StaffRange, 1, k),
        };
        return new Ws1Result(ns.WithSchedule(newSched), newSched);
    }

    /// <summary>Remove staff <paramref name="i"/>: drop the staff and its schedule row; wishes/staffRange
    ///  (axis i) re-indexed. No-op if only one staff remains.</summary>
    public static Ws1Result RemoveStaff(MagiState state, int[][] sched, int i)
    {
        if (i < 0 || i >= state.StaffList.Count || state.StaffList.Count <= 1) return new Ws1Result(state, sched);
        var staff = state.StaffList.Where((_, idx) => idx != i).ToList();
        var newSched = new List<int[]>(Math.Max(sched.Length - 1, 0));
        for (int r = 0; r < sched.Length; r++)
            if (r != i) newSched.Add((int[])sched[r].Clone());
        var arr = newSched.ToArray();
        var ns = state with
        {
            StaffList = staff,
            Wishes = ReindexKeys(state.Wishes, 0, i),
            StaffRange = ReindexKeys(state.StaffRange, 0, i),
        };
        return new Ws1Result(ns.WithSchedule(arr), arr);
    }

    /// <summary>Remove group <paramref name="g"/>: allowed whenever 2+ groups exist. groupShift/apt lose
    ///  the row; staff in the removed group are reassigned to the first remaining group (new index 0);
    ///  staff group indices &gt; g are decremented (skillIdx preserved). Constraints referencing the
    ///  removed group kigou simply stop resolving.</summary>
    public static MagiState RemoveGroup(MagiState state, int g)
    {
        if (g < 0 || g >= state.Groups.Count || state.Groups.Count <= 1) return state;
        var groups = state.Groups.Where((_, idx) => idx != g).ToList();
        var gs = state.GroupShift.Where((_, idx) => idx != g).ToList();
        var apt = state.GroupShiftApt.Count == 0
            ? state.GroupShiftApt
            : state.GroupShiftApt.Where((_, idx) => idx != g).ToList();
        var staff = state.StaffList.Select(s =>
        {
            int ni = s.GroupIdx == g ? 0 : (s.GroupIdx > g ? s.GroupIdx - 1 : s.GroupIdx); // 所属者は先頭グループへ移動
            return ni == s.GroupIdx ? s : new Staff(s.Name, ni, s.SkillIdx);
        }).ToList();
        return state with { Groups = groups, GroupShift = gs, GroupShiftApt = apt, StaffList = staff };
    }

    /// <summary>
    /// [3.330.0/外部レビュー] スキル群 <paramref name="g"/> を削除する。担当グループの <see cref="RemoveGroup"/>
    /// と対になる操作。
    ///
    /// **所属者は <c>-1</c>（未所属）へ**。旧実装（3.328.0まで）は <c>0</c>（先頭の群）へ寄せており、
    ///  - 無関係な先頭の群の制約が黙って掛かる
    ///  - 最後の1群を消すと全員 0 になり、あとで群を1つ足すと全員がそこに所属した扱いになる
    /// という2つの取り違えを起こしていた（3.70.0 が「(なし)=-1」を正規の値として用意済み）。
    /// 後ろの群は1つ詰める。残った cons41s/cons42s の参照外れは <see cref="Problem"/> 解決時に無視される。
    /// </summary>
    public static MagiState RemoveSkillGroup(MagiState state, int g)
    {
        if (g < 0 || g >= state.SkillGroups.Count) return state;
        var skillGroups = state.SkillGroups.Where((_, idx) => idx != g).ToList();
        var staff = state.StaffList.Select(s =>
        {
            int ni = s.SkillIdx == g ? -1 : (s.SkillIdx > g ? s.SkillIdx - 1 : s.SkillIdx); // 所属していた群が無くなった＝未所属
            return ni == s.SkillIdx ? s : s with { SkillIdx = ni };
        }).ToList();
        return state with { SkillGroups = skillGroups, StaffList = staff };
    }
}
