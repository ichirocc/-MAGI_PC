using System.Linq;
using MagiEngine.Model;

namespace MagiEngine.V6;

public static partial class V6SanityPort
{
    /// <summary>
    /// [フェーズ7ピース12] Faithful port of Kotlin's <c>buildViolationDebug</c>
    /// (<c>V6SanityPort.kt:1043-1352</c>) — the per-run diagnostic log line list attached to a
    /// finished schedule. Every violation family is broken out "family by family, with location
    /// and actual values": supply/demand summary per shift, an upper/lower-bound sweep across
    /// every shift with a personal range set, raw coverage/count/cell violation detail (each
    /// capped at <see cref="DetailCap"/> shown rows to bound log growth, with the true fire count
    /// vs. location count reconciled against <paramref name="report"/>'s
    /// <c>Breakdown</c>/<c>*Families</c> maps when they diverge), a per-staff aggregate for large
    /// cell families the detail cap would otherwise truncate into illegibility, a c1
    /// per-staff-per-window-rule tally, and finally a weekly-family breakdown that separates the
    /// unavoidable "count isn't a multiple of 7" structural floor from the remainder that better
    /// weekday placement could still shave off.
    ///
    /// Read-only: consumes <paramref name="schedule"/>/<paramref name="report"/> purely for
    /// display and never mutates <paramref name="state"/>, so it carries no scoring implications.
    /// Depends only on this partial class's already-ported <see cref="ForcedCovU"/>/
    /// <see cref="SafeDayLabel"/>/<c>NeedDefined</c>/<c>EffectiveDemand</c>/<c>EffectiveCap</c>
    /// (all piece 2, <c>V6SanityPort.Core.cs</c>) plus <see cref="Problem"/>/<c>ScheduleUtil</c>/
    /// <see cref="ViolationReport"/> — it does NOT call <c>c3FamilyJp</c> or any part of
    /// <c>buildGuidance</c> (that helper's only 3 call sites, Kotlin source lines 377/510/530, all
    /// fall inside <c>buildGuidance</c> itself — piece 14's scope, not this one — despite
    /// <c>c3FamilyJp</c>'s Kotlin declaration sitting textually just above this function).
    /// </summary>
    public static List<string> BuildViolationDebug(MagiState state, int[][] schedule, ViolationReport report)
    {
        var p = new Problem(state);
        var s = ScheduleUtil.NormalizeSchedule(schedule, p);
        var outList = new List<string>();
        // [スパム対策] 各違反家族の詳細列挙の上限。1パターン把握には十分な件数に絞り、長大化を防ぐ
        //   （以前は 12〜15。c1/c3m など大量家族の1行が極端に伸びていた）。総数は「(N件)」で常に保持。
        const int DetailCap = 8;
        string Sym(int k) => k >= 0 && k < state.Shifts.Count ? state.Shifts[k].Kigou : k.ToString();
        string Nm(int i) => i >= 0 && i < state.StaffList.Count ? state.StaffList[i].Name : $"#{i}";
        string Day(int j) => SafeDayLabel(state.StartDate, j);

        // [構造HARD下限] データ起因で解消不能な必須違反(covU)の下限。最適化の hardFloor と同値。
        //   >0 なら「配布不可はデータ起因＝最適化は残りをSOFT研磨する」と判断できる（読み取り専用）。
        {
            var forced = ForcedCovU(state, p);
            var floor = forced.Sum(x => x.Amount);
            if (floor > 0)
            {
                outList.Add($"[W] 構造HARD下限: 担当者不足で covU={floor} が解消不能（配布不可はデータ起因）: " +
                    string.Join(" / ", forced.Select(x => $"{x.ShiftSymbol} {x.Cells}日 不足{x.Amount}")));
            }
            else
            {
                outList.Add("[I] 構造HARD下限: 0（担当者数の観点では各シフトが需要を満たせる。希望/禁止連続による構造的な人員不足は別途 CoverageDiag/設定ミス を参照）");
            }
        }

        // [3.282.0/新領域ログ監査] 違反詳細ヘッダの件数は「最重クラスで解決済みのセル位置数」で、
        //   breakdown の fire 数とは意味が異なる（c1=窓ごと計上だがmarkはrun先頭のみ・c3n=1 fireでも
        //   パターン全セルをmark・重い族に同一セルを奪われた軽い族は位置ごと消える等）。実機ログで
        //   「違反詳細 c1(11件)」vs「UnifiedCheck c1=12」の食い違いとして混乱を生んでいたため、
        //   fires(breakdown)を併記し両者が異なるときは「件数F・場所N箇所」と明示する。表示のみ・スコア不変。
        //
        // [2026-09-02/監査是正] Kotlin原本(V6SanityPort.kt:1178等)はこの byFam を LinkedHashMap で持ち、
        //   走査順=「[D] 違反詳細 …」各行が並ぶ順を挿入順のまま保証する。素の Dictionary は削除の無い
        //   使い方では現行 .NET 実装が挿入順を保つが、それは公開契約ではなく将来の BCL 変更で静かに
        //   崩れうる（ViolationChecker.cs の cellFams/countFams/needFams と同根の問題・同じ是正）。
        //   ここは Kotlin/C# 間のログ差分比較（CLAUDE.md のパリティ検証運用）を安定させるためだけの
        //   変更であり、集計値・重み・スコアリングには一切影響しない。
        void Emit(InsertionOrderDictionary<string, List<string>> byFam, int cap, IReadOnlyDictionary<string, int>? fires = null)
        {
            foreach (var (fam, items) in byFam)
            {
                var shown = string.Join(" ; ", items.Take(cap));
                var more = items.Count > cap ? $" …他{items.Count - cap}件" : "";
                var f = fires is not null && fires.TryGetValue(fam, out var fv) ? (int?)fv : null;
                var head = f is not null && f != items.Count ? $"件数{f}・場所{items.Count}箇所" : $"{items.Count}件";
                outList.Add($"[D] 違反詳細 {fam}({head}): {shown}{more}");
            }
        }

        // 0) 需給サマリ: シフトごとに「日次需要」と「個人下限/上限・適切回数(クランプ後)の供給圧力・現状配置」を
        //    対比し、過剰(covO=日数オーバー)/不足(covU)の構造的要因を一目で示す。読み取り専用（重み・データ不変）。
        //    例: Dﾃ 需要31 < 適切回数計35 → 各人をその回数へ近づける圧力が需要を超え、過剰配置(1日2人)が出る。
        //    注: 下限/上限/適切回数の「計」は設定済み職員のみの合計。未設定者がいると実効上限は無制限なので、
        //    上限計<需要でも不足とは限らない（不足の構造判定は全員に上限がある場合に限定する）。
        {
            var cnt = ScheduleUtil.CountMatrix(p, s);
            for (var k = 0; k < p.K; k++)
            {
                var demand = 0;
                for (var j = 0; j < p.T; j++) demand += EffectiveDemand(p, k, j);
                var doable = 0; var loSum = 0; var hiSum = 0; var aptSum = 0;
                var loCnt = 0; var hiCnt = 0; var aptCnt = 0; var cur = 0;
                for (var i = 0; i < p.S; i++)
                {
                    cur += cnt[i][k];
                    if (!p.CanDo(i, k)) continue;
                    doable++;
                    var lo = p.RangeLo[i][k]; var hi = p.RangeHi[i][k]; var t = p.Apt[i][k];
                    if (lo != int.MinValue) { loSum += lo; loCnt++; }
                    if (hi != int.MaxValue) { hiSum += hi; hiCnt++; }
                    if (t >= 0) { aptSum += t; aptCnt++; }
                }
                var hasRange = loCnt > 0 || hiCnt > 0;
                var hasApt = aptCnt > 0;
                if (demand == 0 && !hasRange && !hasApt) continue;   // 需給の概念が薄いシフトは省略
                var notes = new List<string>();
                // [3.274.0 監査で修正] 実際の過不足は**日次 covOCell/covUCell の合計**（source of truth）で示す。
                var covUreal = 0; var covOreal = 0;
                for (var j = 0; j < p.T; j++)
                {
                    var g = 0; for (var i = 0; i < p.S; i++) if (s[i][j] == k) g++;
                    covUreal += p.CovUCell(k, j, g);
                    covOreal += p.CovOCell(k, j, g);
                }
                if (covUreal > 0) notes.Add($"現状{cur}(需要{demand})→不足{covUreal}(covU)");
                if (covOreal > 0) notes.Add($"現状{cur}(需要{demand})→過剰{covOreal}(covO)");
                // 構造要因(過剰): 各人が下限/適切回数まで埋める圧力(=確実に埋まる量)の合計が、
                //   **covO を払わずに置ける上限**を超過。[3.372.0] 比較先を demand から seatsHi へ是正済み。
                var seatsHi = 0;
                for (var j = 0; j < p.T; j++)
                {
                    if (!NeedDefined(p, k, j)) continue;   // [3.409.22] need2 単独定義も席として数える
                    seatsHi += Math.Max(EffectiveCap(p, k, j), 0);
                }
                var pull = Math.Max(loSum, aptSum);
                var pullSrc = aptSum >= loSum ? "適切回数" : "下限";
                if (seatsHi > 0 && pull > seatsHi) notes.Add($"供給圧力{pull}({pullSrc})>置ける上限{seatsHi}");
                // 構造要因(不足): 全担当者に上限があり、その合計が需要未満のときのみ（未設定者は無制限なので除外）。
                if (demand > 0 && doable > 0 && hiCnt == doable && hiSum < demand)
                    notes.Add($"全{doable}名の上限計{hiSum}<需要{demand}→構造的に不足");
                string Cs(int sum, int c) => c == doable ? $"{sum}" : $"{sum}({c}/{doable}名)";
                var tag = notes.Any(x => x.Contains("過剰") || x.Contains("不足")) ? "需給注意" : "需給";
                var rangeStr = hasRange ? $" 下限計{Cs(loSum, loCnt)} 上限計{Cs(hiSum, hiCnt)}" : "";
                var aptStr = hasApt ? $" 適切回数計{Cs(aptSum, aptCnt)}" : "";
                outList.Add($"[D] {tag} {Sym(k)}: 需要{demand} 担当{doable}名{rangeStr}{aptStr} 現状{cur}" +
                    (notes.Count > 0 ? $" → {string.Join(" / ", notes)}" : ""));
            }
        }

        // 0b) 上下チェック(全シフト網羅): 下限/上限(staffRange)が設定された全シフトについて、個人別の
        //     下限割れ(low)/上限超過(high)を担当者ぶん洗い出す。違反詳細(low/high)は違反のみ列挙だが、
        //     こちらは設定済みシフトを網羅し違反0でも「上下OK」を出す。判定は UnifiedViolationChecker と一致
        //     （low: lo!=0 かつ canDo かつ 回数<lo / high: 回数>hi）。読み取り専用。
        {
            var cnt = ScheduleUtil.CountMatrix(p, s);
            for (var k = 0; k < p.K; k++)
            {
                var lows = new List<string>(); var highs = new List<string>();
                var hasBound = false;
                for (var i = 0; i < p.S; i++)
                {
                    if (!p.CanDo(i, k)) continue;
                    var lo = p.RangeLo[i][k]; var hi = p.RangeHi[i][k]; var n = cnt[i][k];
                    if (lo != int.MinValue && lo != 0) { hasBound = true; if (n < lo) lows.Add($"{Nm(i)} {n}<{lo}"); }
                    if (hi != int.MaxValue)
                    {
                        hasBound = true;
                        // [代用要員提示/grilling確定=美幸・上條・大島の実例] 上限超過している職員に、
                        //   このシフトを担当できる他の職員(代用要員候補)の人数を併記する。
                        if (n > hi)
                        {
                            var subCount = 0;
                            for (var it = 0; it < p.S; it++) if (it != i && p.CanDo(it, k)) subCount++;
                            highs.Add($"{Nm(i)} {n}>{hi}(代用可{subCount}名)");
                        }
                    }
                }
                if (!hasBound) continue;
                string Part(string label, List<string> xs) =>
                    xs.Count == 0
                        ? $"{label}0名"
                        : $"{label}{xs.Count}名({string.Join(" ", xs.Take(8))}{(xs.Count > 8 ? $" …他{xs.Count - 8}件" : "")})";
                var tag = lows.Count == 0 && highs.Count == 0 ? "上下OK" : "上下注意";
                outList.Add($"[D] {tag} {Sym(k)}: {Part("下限割れ", lows)} / {Part("上限超過", highs)}");
            }
        }

        // 1) 被覆: 必要数/現状数の実値（needViolations は k,j キー）。covU/covO のみ扱う。
        if (report.NeedViolations.Count > 0)
        {
            var cov = ScheduleUtil.Coverage(p, s);
            var byFam = new InsertionOrderDictionary<string, List<string>>();
            foreach (var (key, cls) in report.NeedViolations)
            {
                // [診断強化②③] c41/c41s は被覆ではなく「群(スキル)×シフトの人数制約」。専用集約(1b)へ回す。
                if (cls is "vio-c41" or "vio-c41s") continue;
                var parts = key.Split(',');
                if (parts.Length < 2) continue;
                var k = KotlinInterop.ToIntOrNull(parts[0]);
                var j = KotlinInterop.ToIntOrNull(parts[1]);
                if (k is null || j is null) continue;
                if (k.Value < 0 || k.Value >= p.K || j.Value < 0 || j.Value >= p.T) continue;
                // [3.409.22] 旧: 生の need1/need2 を出すため need2 単独定義セルで「必要-1~2」と誤表示していた。
                var lo = EffectiveDemand(p, k.Value, j.Value); var hi = EffectiveCap(p, k.Value, j.Value);
                var needStr = hi > lo ? $"{lo}~{hi}" : $"{lo}";
                var famKey = cls.StartsWith("vio-") ? cls[4..] : cls;
                if (!byFam.TryGetValue(famKey, out var list)) byFam[famKey] = list = new List<string>();
                list.Add($"{Day(j.Value)} {Sym(k.Value)} 必要{needStr}/現状{cov[j.Value][k.Value]}");
            }
            // [3.380.0/実機ログ起因] この呼出だけ fires を渡していなかった穴を是正済み。
            Emit(byFam, DetailCap, report.Breakdown);
        }

        // 1b) [診断強化②③＋スパム削減] c41/c41s = 日次・群(スキル)×シフトの人数が[下限,上限]に収まるか。
        //     cons 行ごとに「群/スキル × シフト・下限上限・違反日数・現状人数範囲」で集約する
        //     （124件→cons行数の数行に圧縮）。
        {
            void EmitCons(IReadOnlyList<C41> rows, string fam, Func<int, int> memberOf, Func<int, string> groupSym)
            {
                foreach (var c in rows)
                {
                    var vdays = 0; var minZ = int.MaxValue; var maxZ = 0;
                    for (var j = 0; j < p.T; j++)
                    {
                        var z = 0;
                        for (var i = 0; i < p.S; i++) if (memberOf(i) == c.GroupIdx && s[i][j] == c.ShiftIdx) z++;
                        if (z < c.L || z > c.U) { vdays++; if (z < minZ) minZ = z; if (z > maxZ) maxZ = z; }
                    }
                    if (vdays > 0)
                    {
                        var range = minZ == maxZ ? $"{minZ}" : $"{minZ}〜{maxZ}";
                        outList.Add($"[D] 違反詳細 {fam}: {groupSym(c.GroupIdx)}×{Sym(c.ShiftIdx)} {vdays}日違反 (下限{c.L}/上限{c.U}, 現状{range})");
                    }
                }
            }
            EmitCons(p.Cons41, "c41", i => p.Sgrp[i],
                g => g >= 0 && g < state.Groups.Count ? state.Groups[g].Kigou : $"群{g}");
            EmitCons(p.Cons41s, "c41s", i => p.Ssk[i],
                g => g >= 0 && g < state.SkillGroups.Count ? state.SkillGroups[g].Kigou : $"スキル{g}");
        }

        // 2) 回数: 回数/下限/上限（countViolations は i,k キー）
        //   [3.353.0] countFamilies があればそちら（重い族に隠れた軽い族も含む全クラス）を使う。
        if (report.CountViolations.Count > 0)
        {
            var cnt = ScheduleUtil.CountMatrix(p, s);
            var byFam = new InsertionOrderDictionary<string, List<string>>();
            var pairs = report.CountFamilies.Count > 0
                ? report.CountFamilies.SelectMany(kv => kv.Value.Select(cls => (Key: kv.Key, Cls: cls))).ToList()
                : report.CountViolations.Select(kv => (Key: kv.Key, Cls: kv.Value)).ToList();
            foreach (var (key, cls) in pairs)
            {
                var parts = key.Split(',');
                if (parts.Length < 2) continue;
                var i = KotlinInterop.ToIntOrNull(parts[0]);
                var k = KotlinInterop.ToIntOrNull(parts[1]);
                if (i is null || k is null) continue;
                if (i.Value < 0 || i.Value >= p.S || k.Value < 0 || k.Value >= p.K) continue;
                int? lo = p.RangeLo[i.Value][k.Value] != int.MinValue ? p.RangeLo[i.Value][k.Value] : null;
                int? hi = p.RangeHi[i.Value][k.Value] != int.MaxValue ? p.RangeHi[i.Value][k.Value] : null;
                // [実機ログ起因] aptLow/aptHigh は「目標(クランプ後)との偏差」が発火理由なので目標を併記。
                int? apt = (cls == "vio-aptLow" || cls == "vio-aptHigh") && p.Apt[i.Value][k.Value] >= 0
                    ? p.Apt[i.Value][k.Value] : null;
                var famKey = cls.StartsWith("vio-") ? cls[4..] : cls;
                if (!byFam.TryGetValue(famKey, out var list)) byFam[famKey] = list = new List<string>();
                list.Add($"{Nm(i.Value)} {Sym(k.Value)} 回数{cnt[i.Value][k.Value]}" +
                    (apt is not null ? $" 目標{apt}" : "") +
                    (lo is not null ? $" 下限{lo}" : "") +
                    (hi is not null ? $" 上限{hi}" : ""));
            }
            // 族名が breakdown のキーと一致するもの(low/high/c2)だけ突き合わせる。aptLow/aptHigh は
            //   breakdown に個別キーが無く実体は apt（重み1.0）＝両方へ同じ値を出すと二重に見えるので
            //   専用行で「合計と場所数」を1度だけ示す。
            Emit(byFam, DetailCap, report.Breakdown);
            var aptFires = report.Breakdown.TryGetValue("apt", out var af) ? af : 0;
            if (aptFires > 0)
            {
                var loN = byFam.TryGetValue("aptLow", out var loList) ? loList.Count : 0;
                var hiN = byFam.TryGetValue("aptHigh", out var hiList) ? hiList.Count : 0;
                outList.Add(
                    $"[D] 違反詳細 apt(件数{aptFires}・場所{loN + hiN}箇所): 目標割れ{loN}箇所 + 目標超過{hiN}箇所" +
                    "（件数=各行の|回数−目標|の合計＝1箇所で複数件になる）");
            }
        }

        // 3) セル違反: 誰の・何日・どのシフト（violations は i,j キー）
        if (report.Violations.Count > 0)
        {
            var byFam = new InsertionOrderDictionary<string, List<string>>();
            foreach (var (key, cls) in report.Violations)
            {
                var parts = key.Split(',');
                if (parts.Length < 2) continue;
                var i = KotlinInterop.ToIntOrNull(parts[0]);
                var j = KotlinInterop.ToIntOrNull(parts[1]);
                if (i is null || j is null) continue;
                if (i.Value < 0 || i.Value >= p.S || j.Value < 0 || j.Value >= p.T) continue;
                var famKey = cls.StartsWith("vio-") ? cls[4..] : cls;
                if (!byFam.TryGetValue(famKey, out var list)) byFam[famKey] = list = new List<string>();
                list.Add($"{Nm(i.Value)} {Day(j.Value)}={Sym(s[i.Value][j.Value])}");
            }
            Emit(byFam, DetailCap, report.Breakdown);
        }

        // 3.4) [3.355.0/ログ強化] DETAIL_CAP で切れる大きなセル族は「…他58件」で終わり、誰に集中して
        //   いるかが読めなかった。checker が出した場所（cellFamilies）を職員別に数え直すだけ＝規則の
        //   再実装をしないのでドリフトしない。
        {
            // [2026-09-02/監査是正] 外側(fam)のみ挿入順を保証すればよい。内側(byStaff)は下で
            //   OrderByDescending(kv.Value) により値で並び替えるため素の Dictionary のままで足りる
            //   （Kotlin原本 V6SanityPort.kt:1278 も外側だけ LinkedHashMap・内側は HashMap<Int,Int>）。
            var perFam = new InsertionOrderDictionary<string, Dictionary<int, int>>();
            foreach (var (key, list) in report.CellFamilies)
            {
                var iStr = key.Split(',')[0];
                var i = KotlinInterop.ToIntOrNull(iStr);
                if (i is null) continue;
                if (i.Value < 0 || i.Value >= p.S) continue;
                foreach (var cls in list)
                {
                    var famKey = cls.StartsWith("vio-") ? cls[4..] : cls;
                    if (!perFam.TryGetValue(famKey, out var byStaff)) perFam[famKey] = byStaff = new Dictionary<int, int>();
                    byStaff[i.Value] = byStaff.GetValueOrDefault(i.Value, 0) + 1;
                }
            }
            foreach (var (fam, byStaff) in perFam)
            {
                if (fam == "c1") continue;                       // c1 は下の「職員×窓ルール別」がより詳しい
                if (byStaff.Values.Sum() <= DetailCap) continue;  // 全件が上に出ているなら冗長
                var txt = string.Join(" / ",
                    byStaff.OrderByDescending(kv => kv.Value).Select(kv => $"{Nm(kv.Key)} {kv.Value}箇所"));
                outList.Add($"[D] {fam} 集約（職員別・場所数の全件）: {txt}");
            }
        }

        // 3.5) [c1族の職員×窓ルール別件数] 「違反詳細 c1(N件)」はDETAIL_CAPで打ち切られ、特定職員が
        //   どの窓ルールで何件かは埋もれる。全件を職員×ルール別に再集計し、打ち切りなしの1行サマリとして
        //   追加する。読取専用（重み・データ不変）。checker の inc と同一の「違反窓ごと」計上＝本行の合計は
        //   常に UnifiedCheck の c1 と一致する。
        if (report.Breakdown.TryGetValue("c1", out var c1v) && c1v > 0)
        {
            // [2026-09-02/監査是正] Kotlin原本(V6SanityPort.kt:1301)は外側(職員)・内側(ルール別件数)
            //   の両方を LinkedHashMap で持つ（内側は下で kv.Value をそのまま join し値で並べ替えない
            //   ため、perFam の byStaff と違い内側も順序保証が要る）。両方とも InsertionOrderDictionary へ。
            var perStaffRule = new InsertionOrderDictionary<int, InsertionOrderDictionary<string, int>>();
            foreach (var c in p.Cons1)
            {
                var ruleLabel = $"{Sym(c.ShiftIdx)}({c.Day1}日窓≥{c.Day2})";
                for (var i = 0; i < p.S; i++)
                {
                    if (!p.CanDo(i, c.ShiftIdx)) continue;
                    var j = 0;
                    while (j <= p.T - c.Day1)
                    {
                        var z = 0;
                        for (var l = 0; l < c.Day1; l++) if (s[i][j + l] == c.ShiftIdx) z++;
                        if (z < c.Day2)
                        {
                            if (!perStaffRule.TryGetValue(i, out var rules))
                                perStaffRule[i] = rules = new InsertionOrderDictionary<string, int>();
                            rules[ruleLabel] = rules.GetValueOrDefault(ruleLabel, 0) + 1;
                        }
                        j++;
                    }
                }
            }
            if (perStaffRule.Count > 0)
            {
                var lines = string.Join(" / ", perStaffRule.Select(kv =>
                    $"{Nm(kv.Key)} " + string.Join(", ", kv.Value.Select(r => $"{r.Key}{r.Value}件"))));
                outList.Add($"[D] c1内訳（職員×窓ルール別件数・全件）: {lines}");
            }
        }

        // 3.6) [3.355.0/ログ強化] weekly は実機で最大の族になりうるのに内訳が一切無かった。**回数が7の倍数で
        //   ないぶんは配置をどう変えても消せない**（目標=round(回数/7) なので余りが必ず偏差として残る）。
        //   その構造床と、曜日の寄せ方で減らせる残りを分けて示す。
        if (report.Breakdown.TryGetValue("weekly", out var weeklyV) && weeklyV > 0)
        {
            var cntW = ScheduleUtil.CountMatrix(p, s);
            var floor = 0;
            var worst = new List<(int I, int K, int Room)>();
            for (var i = 0; i < p.S; i++)
                for (var k = 0; k < p.K; k++)
                {
                    var c = cntW[i][k];
                    if (c <= 0) continue;
                    floor += ScheduleUtil.WeeklyFloorOfCount(c);
                }
            if (report.DistLocations.TryGetValue("weekly", out var weeklyLocs))
            {
                foreach (var loc in weeklyLocs)
                {
                    if (loc.Count < 3) continue;
                    var i = loc[0]; var k = loc[1]; var dev = loc[2];
                    var baseCount = i >= 0 && i < cntW.Length && k >= 0 && k < cntW[i].Length ? cntW[i][k] : 0;
                    var room = dev - ScheduleUtil.WeeklyFloorOfCount(baseCount);
                    if (room > 0) worst.Add((i, k, room));
                }
            }
            var total = weeklyV;
            var head = $"[D] weekly内訳: 合計{total}件 = 構造床{Math.Min(floor, total)}件(回数が7の倍数でない＝配置では消せない)" +
                $" + 曜日の寄せ方で減らせる{Math.Max(total - floor, 0)}件";
            var topTxt = string.Join(" ; ", worst.OrderByDescending(w => w.Room).Take(DetailCap)
                .Select(w => $"{Nm(w.I)} {Sym(w.K)} 余地{w.Room}"));
            outList.Add(topTxt.Length == 0 ? head : $"{head} / 余地の大きい順: {topTxt}");
        }

        if (outList.Count == 0) outList.Add("[D] 違反詳細: 制約違反はありません");
        return outList;
    }
}
