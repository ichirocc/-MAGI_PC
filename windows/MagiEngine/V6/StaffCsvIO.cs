using System.Linq;
using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// [フェーズ7ピース11] Kotlin原本 <c>StaffCsvIO</c>（<c>ScheduleCsvBridge.kt</c> 538〜674行）の移植。
///
/// スタッフ一覧CSV「氏名,グループ,スキル」の往復。<see cref="Parse"/> は氏名一致で所属群・スキルを
/// **更新のみ**（追加/削除はしない）、<see cref="ParseUpsert"/> は未知の氏名を新規スタッフとして
/// 追加し勤務表に1行足す（upsert）。
/// </summary>
public static class StaffCsvIO
{
    /// <summary>
    /// スタッフ一覧 upsert の結果（新規追加分の勤務表行も反映済み）。
    /// </summary>
    /// <param name="State">更新後の状態。</param>
    /// <param name="Schedule">更新後の勤務表（新規行を含む）。</param>
    /// <param name="Updated">既存氏名一致で更新した件数。</param>
    /// <param name="Added">新規追加した件数。</param>
    /// <param name="UnknownGroups">
    /// 空でないのに既存のグループ記号と一致しなかったセル（記号→件数）。新規職員は先頭グループへ、
    /// 既存職員は現状維持へ**黙って**落ちるため、呼出側が必ず知らせる。
    /// </param>
    /// <param name="UnknownSkills">同じくスキル群。未所属(-1)へ落ちる。</param>
    public sealed record StaffUpsertResult(
        MagiState State, int[][] Schedule, int Updated, int Added,
        IReadOnlyDictionary<string, int>? UnknownGroups = null,
        IReadOnlyDictionary<string, int>? UnknownSkills = null)
    {
        public IReadOnlyDictionary<string, int> UnknownGroups { get; init; } = UnknownGroups ?? EmptyCounts;
        public IReadOnlyDictionary<string, int> UnknownSkills { get; init; } = UnknownSkills ?? EmptyCounts;
        private static readonly IReadOnlyDictionary<string, int> EmptyCounts = new Dictionary<string, int>();
    }

    /// <summary>Kotlin の <c>r.getOrElse(idx) { "" }.trim()</c> と同じ「範囲外は空文字」読み取り。</summary>
    private static string Cell(IReadOnlyList<string> r, int idx) => (idx >= 0 && idx < r.Count ? r[idx] : "").Trim();

    /// <summary>
    /// [Ws1Ops.kt 230〜234行 移植元] <c>Ws1Ops.fillShift</c> の逐語移植（この3行のためだけに
    /// <c>Ws1Ops.kt</c> 全体を移植しない＝フェーズ9のスコープのまま）。
    ///
    /// 空き日を何で埋めるか＝ <paramref name="rest"/> をその群が担当できるならそれ、できなければ
    /// 担当できる先頭のシフト（どちらも無ければ <paramref name="rest"/> のまま。ここで例外を投げると
    /// この不整合を直しに来た編集操作自体が落ちるため、あえて投げない）。
    /// </summary>
    private static int FillShift(IReadOnlyList<int>? groupShiftRow, int rest)
    {
        if (groupShiftRow is null) return rest;
        var allowed = new List<int>();
        for (var k = 0; k < groupShiftRow.Count; k++) if (groupShiftRow[k] == 1) allowed.Add(k);
        return ScheduleUtil.FillShiftIndex(allowed.ToArray(), rest);
    }

    public static string Build(MagiState state)
    {
        var sb = new System.Text.StringBuilder();
        CsvUtil.AppendCsvRow(sb, new List<string> { "氏名", "グループ", "スキル" });
        foreach (var s in state.StaffList)
        {
            CsvUtil.AppendCsvRow(sb, new List<string>
            {
                s.Name,
                s.GroupIdx >= 0 && s.GroupIdx < state.Groups.Count ? state.Groups[s.GroupIdx].Kigou : "",
                s.SkillIdx >= 0 && s.SkillIdx < state.SkillGroups.Count ? state.SkillGroups[s.SkillIdx].Kigou : "",
            });
        }
        return sb.ToString();
    }

    /// <returns>更新後state と一致件数、または null（解析不能/一致0件）。</returns>
    public static (MagiState State, int Matched)? Parse(string text, MagiState state)
    {
        // [3.413.0/I-08] 引用符が閉じないCSVは残りの行が丸ごと消える＝**全置換の取込では
        //   「消えた」ことが取込結果からは分からない**。書式の誤りとして断る。
        var parsed0 = CsvUtil.ParseCsvFull(text);
        if (parsed0.UnclosedQuote) return null;
        var rows = parsed0.Rows;
        if (rows.Count == 0) return null;
        var nameToI = CsvUtil.FirstWinsMap(state.StaffList.Count, i => CsvUtil.NameMatchKey(state.StaffList[i].Name));
        var gByK = CsvUtil.FirstWinsMap(state.Groups.Count, i => state.Groups[i].Kigou.Trim());
        var skByK = CsvUtil.FirstWinsMap(state.SkillGroups.Count, i => state.SkillGroups[i].Kigou.Trim());
        var newStaff = state.StaffList.ToList();
        var matched = 0;
        // [3.314.0] ヘッダ判定を Build() が出す実ヘッダ「氏名」の一致へ。旧:「先頭が既知の職員名か」
        //   という間接的な推測で、**未知の職員名で始まるヘッダ無CSVの先頭行を黙って捨てて**いた。
        var body = CsvUtil.CsvBody(rows, "氏名");
        foreach (var r in body)
        {
            var name = Cell(r, 0);
            if (name.Length == 0) continue;
            if (!nameToI.TryGetValue(CsvUtil.NameMatchKey(name), out var i)) continue;
            matched++;
            var gi = gByK.TryGetValue(Cell(r, 1), out var gv) ? gv : newStaff[i].GroupIdx;
            var si = skByK.TryGetValue(Cell(r, 2), out var sv) ? sv : newStaff[i].SkillIdx;
            newStaff[i] = newStaff[i] with { GroupIdx = gi, SkillIdx = si };
        }
        if (matched == 0) return null;
        return (state with { StaffList = newStaff }, matched);
    }

    /// <summary>
    /// [氏名,グループ,スキル] を upsert で取込: 既存氏名は所属群/スキルを更新、未知の氏名は
    /// 新規スタッフとして追加し勤務表に1行足す。空き日を何で埋めるかは <see cref="FillShift"/>
    /// （その群が休を担当できるなら休、できなければ担当できる先頭のシフト）が決める＝3.442.0/H3。
    /// 旧KDocの「休(0)」は3.329.0の記号解決化より前の記述だった。氏名は空白無視で照合。
    /// 群/スキルは記号(kigou)照合、未知なら新規は先頭群/未所属(-1)・既存は現状維持。
    /// [3.413.0/I-07] 未知の記号は <see cref="StaffUpsertResult.UnknownGroups"/>/
    /// <see cref="StaffUpsertResult.UnknownSkills"/> に記録する（空欄＝指定なしと、誤記＝解決できなかった、を
    /// 呼出側が区別できるようにするため）。
    /// </summary>
    /// <returns><see cref="StaffUpsertResult"/>、または null（解析不能/更新0かつ追加0）。</returns>
    public static StaffUpsertResult? ParseUpsert(string text, MagiState state, int[][] sched)
    {
        // [3.413.0/I-08] 引用符が閉じないCSVは残りの行が丸ごと消える＝**全置換の取込では
        //   「消えた」ことが取込結果からは分からない**。書式の誤りとして断る。
        var parsed0 = CsvUtil.ParseCsvFull(text);
        if (parsed0.UnclosedQuote) return null;
        var rows = parsed0.Rows;
        if (rows.Count == 0) return null;
        var nameToI = CsvUtil.FirstWinsMap(state.StaffList.Count, i => CsvUtil.NameMatchKey(state.StaffList[i].Name));
        var gByK = CsvUtil.FirstWinsMap(state.Groups.Count, i => state.Groups[i].Kigou.Trim());
        var skByK = CsvUtil.FirstWinsMap(state.SkillGroups.Count, i => state.SkillGroups[i].Kigou.Trim());
        var newStaff = state.StaffList.ToList();
        var t = sched.Length > 0 ? sched[0].Length : state.DayCount;
        var extraRows = new List<int[]>();
        var seenNew = new Dictionary<string, int>();
        var updated = 0;
        var added = 0;
        // [3.413.0/I-07] 空でないのに解決できなかった群/スキル記号を数える。旧実装は「新規＝先頭グループ・
        //   既存＝現状維持」で、**空欄と誤記が見分けられない**まま黙って落ちていた。所属グループは担当できる
        //   シフトを決めるので、誤記が通ると「なぜこの人がこの勤務に入るのか」が説明できない盤面になる。
        //   3.410.0 の勤務表CSV未知記号と同じ形で知らせる。
        var unknownG = new Dictionary<string, int>();
        var unknownS = new Dictionary<string, int>();
        // [3.314.0] 実ヘッダ「氏名」の一致で判定する。この経路は未知名を**新規追加**するため、旧実装は
        //   「ヘッダ文字列を職員として登録しない」保守のために既知名一致のときだけ先頭行を本体へ入れて
        //   おり、**先頭が新規職員のヘッダ無CSVはその1件を黙って捨てて**いた。厳密なヘッダ判定なら
        //   その保守は不要で、取りこぼしも起きない。
        var body = CsvUtil.CsvBody(rows, "氏名");
        foreach (var r in body)
        {
            var rawName = Cell(r, 0);
            if (rawName.Length == 0) continue;
            var key = CsvUtil.NameMatchKey(rawName);
            var gRaw = Cell(r, 1);
            var sRaw = Cell(r, 2);
            var hasGi = gByK.TryGetValue(gRaw, out var gi);
            var hasSi = skByK.TryGetValue(sRaw, out var si);
            if (!hasGi && gRaw.Length != 0) unknownG[gRaw] = (unknownG.TryGetValue(gRaw, out var gc) ? gc : 0) + 1;
            if (!hasSi && sRaw.Length != 0) unknownS[sRaw] = (unknownS.TryGetValue(sRaw, out var sc) ? sc : 0) + 1;
            if (nameToI.TryGetValue(key, out var existing))
            {
                var cur = newStaff[existing];
                newStaff[existing] = cur with { GroupIdx = hasGi ? gi : cur.GroupIdx, SkillIdx = hasSi ? si : cur.SkillIdx };
                updated++;
            }
            else if (seenNew.TryGetValue(key, out var dup))
            {
                var cur = newStaff[dup];
                newStaff[dup] = cur with { GroupIdx = hasGi ? gi : cur.GroupIdx, SkillIdx = hasSi ? si : cur.SkillIdx };
            }
            else
            {
                seenNew[key] = newStaff.Count;
                // [3.329.0/外部レビュー H-01/M-01] 新しい職員の空き日は**休の記号解決**で埋める
                //   （旧: index 0 直書きで、休が先頭でないデータでは全日が勤務になっていた）。
                //   未知のスキル群は 0（先頭の群）でなく **-1（未所属）**へ（3.70.0の「(なし)」）。
                // [3.442.0/H3] さらに**その群が休を担当できるか**まで見る。休を担当可否から外した群
                //   （UIの担当可否チップで実際にできる操作）へCSVで職員を足すと、旧実装は全日を休で
                //   埋めて**行まるごとgroupViol(HARD 10000)**になっていた（31日なら1回の取込で必須違反31件）。
                //   3.418.0がWs1Opsの3経路で直したのと同じ穴の、CSV側の取り残し。未知の群は先頭グループへ
                //   落ちるので、そこが休を持たない場合も同様に効く。
                var gIdx = hasGi ? gi : 0;
                var groupShiftRow = gIdx >= 0 && gIdx < state.GroupShift.Count ? state.GroupShift[gIdx] : null;
                var fill = FillShift(groupShiftRow, ScheduleUtil.RestShiftIndex(state));
                newStaff.Add(new Staff(rawName, gIdx, hasSi ? si : -1));
                extraRows.Add(Enumerable.Repeat(fill, t).ToArray());
                added++;
            }
        }
        if (updated == 0 && added == 0) return null;
        var newSched = new int[sched.Length + extraRows.Count][];
        for (var i = 0; i < sched.Length; i++) newSched[i] = (int[])sched[i].Clone();
        for (var i = 0; i < extraRows.Count; i++) newSched[sched.Length + i] = extraRows[i];
        var ns = (state with { StaffList = newStaff }).WithSchedule(newSched);
        return new StaffUpsertResult(ns, newSched, updated, added, unknownG, unknownS);
    }
}
