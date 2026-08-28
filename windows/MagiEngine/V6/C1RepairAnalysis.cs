namespace MagiEngine.V6;

/// <summary>
/// A6: 構造化されたc1窓違反（1窓=1件。checker の <c>inc("c1")</c> 意味論と一致）。
/// </summary>
public sealed record C1WindowViolation(int RuleIndex, int Staff, int Start, int WindowDays, int Shift, int Required, int Actual)
{
    public int Deficit => Math.Max(Required - Actual, 0);
}

/// <summary>A6: 窓内の各候補日の局所情報（探索はまだしない＝Analysisの仕事）。</summary>
public sealed record RepairOpportunity(
    int Day,
    /// <summary>この日を shift にすると解消される窓不足の数（重複窓ボーナス込み）。</summary>
    int Gain,
    /// <summary>この日 shift を増やすと covO / 別シフト covU が悪化する量。</summary>
    int CoverageRisk,
    /// <summary>この日 shift にすると c3n（禁止連続）を作る。</summary>
    bool PatternRisk,
    /// <summary>希望で他シフトに固定されている。</summary>
    bool WishConflict);

public sealed record Config(int MaxInvolvedStaff = 6, int MaxWindowDays = 16, int NodeBudget = 300_000, int PerDayBranchCap = 24);

/// <summary>A2/A3: 窓スコープ厳密探索の結果。</summary>
public sealed record ExactResult(
    /// <summary>関与職員の joint c1 の（探索した中で）最小値。</summary>
    int MinJointC1,
    /// <summary>変更前の joint c1。</summary>
    int BaselineJointC1,
    /// <summary>[[staff,day,newShift],...]（min を達成する差分）。改善なし = null。</summary>
    IReadOnlyList<int[]>? Patch,
    /// <summary>node予算内で全探索を完了したか（true のとき <see cref="MinJointC1"/> は証明）。</summary>
    bool Exhaustive,
    // [3.279.0/外部レビューC1-03] **対象窓（v.ruleIndex/v.start）だけ**の残 fire（0 or 1）の全葉最小。
    //   旧: 焦点職員の全ルール×全窓の残数だったため、対象窓が解消可能でも別窓（別シフトのルール含む）が
    //   残るだけで「この窓は壁」と誤認していた（provenWalls の false positive）。
    int FocusResidual);

/// <summary>A4: coverage入替（同シフト多重集合の並べ替え）でも解消できないと証明された窓（cross-staff の構造的壁）。</summary>
public sealed record CoverageNeutralWall(int Staff, int Shift, int Start, int WindowDays);

/// <summary>
/// [C1 Repair Analysis + Exact Window Repair / 3.273.0] C1（窓の要件）を「評価」と「修復」で完全分離する。
///
/// 設計原則（ユーザー合意 A1-A6 / v8第2段）:
///  - <b>評価器（checker/Evaluator/C++）は修復を考えない</b>。本モジュールは checker を一切変えず、
///    その出力（c1違反）を構造化し（A6=<see cref="C1WindowViolation"/>/<see cref="RepairOpportunity"/>）、
///    修復候補を生成する。
///  - <b>A2/A3=厳密窓修復</b>は OR-Tools 等の重量ネイティブ依存を持ち込まず、<b>純粋な分枝限定探索</b>で
///    窓スコープの部分問題（数職員×窓幅日）を解く。探索は<b>coverage保存</b>（各日のシフト多重集合を
///    関与職員の間で並べ替えるだけ）＝covU/covO構造的に不変。最終採用は必ず呼出側の checker+keep-best。
///  - <c>Exhaustive=true</c>（node予算内で全探索完了）のときのみ <see cref="ExactResult.MinJointC1"/> は
///    <b>証明された下限</b>（A3）。予算超過時は best-effort（探索多様化として安全＝keep-best が退化を防ぐ）。
/// </summary>
public static class C1RepairAnalysis
{
    /// <summary>
    /// A4: 不足窓のうち、厳密探索で「exhaustive かつ焦点職員の残c1&gt;0」＝どう入れ替えても解消不能と
    /// 証明されたものだけを返す（node予算超過=未証明は含めない＝誤検知ゼロ）。2b-2/MUS が扱わない
    /// 「実際のトークン希少性を全職員横断で厳密に勘定した」構造的不能の証明。
    /// </summary>
    public static List<CoverageNeutralWall> ProvenWalls(Problem p, int[][] schedule, Config? cfg = null)
    {
        cfg ??= new Config();
        var result = new List<CoverageNeutralWall>();
        var seen = new HashSet<long>();
        foreach (var v in Analyze(p, schedule))
        {
            // [3.279.0/外部レビューC1-04] 旧: staff×shift のみのキーで、同一職員・同一シフトの独立した
            //   複数の不足窓があっても最初の1窓しか証明探索せず、後続の真の壁を見逃していた。窓単位
            //   (staff×ruleIndex×start) でデデュープする（provenWalls はテスト/診断専用＝コスト増は許容）。
            long f = ((long)v.Staff * 100_000L + v.RuleIndex) * 100_000L + v.Start;
            if (!seen.Add(f)) continue;
            var r = SolveWindow(p, schedule, v, cfg);
            if (r.Exhaustive && r.FocusResidual > 0)
                result.Add(new CoverageNeutralWall(v.Staff, v.Shift, v.Start, v.WindowDays));
        }
        return result;
    }

    // ---- A6: 解析（純 read-only） ---------------------------------------------------------------

    /// <summary>checker の c1 窓走査を忠実に再現し、不足窓を構造化列挙（全窓・アンカー集約なし）。</summary>
    public static List<C1WindowViolation> Analyze(Problem p, int[][] schedule)
    {
        var result = new List<C1WindowViolation>();
        var s = ScheduleUtil.NormalizeSchedule(schedule, p);
        for (int ri = 0; ri < p.Cons1.Count; ri++)
        {
            var c = p.Cons1[ri];
            int x = c.ShiftIdx;
            if (x < 0 || x >= p.K || c.Day1 < 1 || c.Day1 > p.T || c.Day2 < 1) continue;
            for (int i = 0; i < p.S; i++)
            {
                if (!p.CanDo(i, x)) continue;
                int j = 0;
                while (j <= p.T - c.Day1)
                {
                    int z = 0;
                    for (int l = 0; l < c.Day1; l++) if (s[i][j + l] == x) z++;
                    if (z < c.Day2) result.Add(new C1WindowViolation(ri, i, j, c.Day1, x, c.Day2, z));
                    j++;
                }
            }
        }
        return result;
    }

    /// <summary>窓内の各候補日（現在 shift でない・movable な日）の局所情報を作る（探索なし）。</summary>
    public static List<RepairOpportunity> Opportunities(Problem p, int[][] schedule, C1WindowViolation v)
    {
        var s = ScheduleUtil.NormalizeSchedule(schedule, p);
        var result = new List<RepairOpportunity>();
        for (int d = v.Start; d < v.Start + v.WindowDays; d++)
        {
            if (s[v.Staff][d] == v.Shift) continue;
            bool wishConflict = p.WishLocked(v.Staff, d);
            bool patternRisk = p.MakesForbiddenRun(s, v.Staff, d, v.Shift);
            // gain = この日を shift にすると実際に解消する（この職員の）不足窓数。
            //   [3.278.0/監査修正] ①窓開始の下界 lo は**ルールごとの窓幅 c.Day1** から取る（旧: v.WindowDays
            //   固定＝同一シフトのより長い別ルール窓（休の5日窓＋15日窓型）が d を含んでいても切り捨てられ過小）。
            //   ②+1 で充足に変わるのは z == c.Day2 - 1 の窓のみ（旧: z < c.Day2＝1手では解消しない深い不足窓も
            //   計上する過大）。checker は不足窓ごとに 1 fire 計上するため「解消数」＝ちょうど閾値未満1の窓数。
            int gain = 0;
            foreach (var c in p.Cons1)
            {
                if (c.ShiftIdx != v.Shift || c.Day1 < 1 || c.Day1 > p.T) continue;
                int lo = Math.Max(d - c.Day1 + 1, 0);
                int hiBound = Math.Min(d, p.T - c.Day1);
                for (int ws = lo; ws <= hiBound; ws++)
                {
                    if (d < ws || d >= ws + c.Day1) continue;
                    int z = 0;
                    for (int l = 0; l < c.Day1; l++) if (s[v.Staff][ws + l] == v.Shift) z++;
                    if (z == c.Day2 - 1) gain++;
                }
            }
            int old = s[v.Staff][d];
            int cntNew = 0;
            for (int i = 0; i < p.S; i++) if (s[i][d] == v.Shift) cntNew++;
            // [3.278.0/監査で実証されたクラッシュの修正] old は -1（normalizeSchedule のセンチネル＝削除済み
            //   シフト等の残存 index）になり得る。無検証で covUCell(-1,…)＝need1[-1][j] を読むと AIOOBE。
            //   範囲外セルからの離脱は何も不足させないため除去項は 0。
            int removalRisk = 0;
            if (old >= 0 && old < p.K)
            {
                int cntOld = 0;
                for (int i = 0; i < p.S; i++) if (s[i][d] == old) cntOld++;
                removalRisk = p.CovUCell(old, d, cntOld - 1) - p.CovUCell(old, d, cntOld);
            }
            int covRisk = (p.CovOCell(v.Shift, d, cntNew + 1) - p.CovOCell(v.Shift, d, cntNew)) + removalRisk;
            result.Add(new RepairOpportunity(d, gain, covRisk, patternRisk, wishConflict));
        }
        return result;
    }

    // ---- A2/A3: 窓スコープ厳密探索（coverage保存 permutation の分枝限定） -----------------------

    /// <summary>1つの不足窓を起点に、窓を含む日スパンをまたぐ coverage保存 permutation で joint c1 を最小化する。</summary>
    public static ExactResult SolveWindow(Problem p, int[][] schedule, C1WindowViolation v, Config? cfg = null)
    {
        cfg ??= new Config();
        var s = ScheduleUtil.NormalizeSchedule(schedule, p);
        // [多日連動] days は単一窓幅でなく、窓を含む maxWindowDays 幅の連続スパン。狭いと「別日で連動して
        //   初めて解ける」多職員手（同日swapの合成では到達不能）を表現できないため（実測でこの拡張が必須）。
        int span = Math.Min(cfg.MaxWindowDays, p.T);
        int startD = Math.Max(Math.Min(v.Start, p.T - span), 0);
        var days = Enumerable.Range(startD, span).ToList();

        // 関与職員 M = i0 ∪ スパン内で shift x を持つ職員（cap 内）。coverage保存はこの M の日別多重集合を
        //   M 内で並べ替えることで担保（M外・スパン外は固定）。
        // [移植メモ] Kotlin の LinkedHashSet と同じ「挿入順を保持する集合」を、List(順序)+HashSet(O(1)判定)の
        //   組で明示的に再現する（.NET の HashSet<T> の列挙順は未規定のため依存しない）。v.Staff が必ず
        //   先頭（index 0）に来るという下の focusResidualOf の前提はこの List の先頭挿入で担保される。
        var mList = new List<int> { v.Staff };
        var mSeen = new HashSet<int> { v.Staff };
        foreach (int d in days)
            for (int i = 0; i < p.S; i++)
                if (s[i][d] == v.Shift && i != v.Staff && mSeen.Add(i))
                    mList.Add(i);
        // 余力: x を担当できる職員を加える（3者以上の連動を可能に）。
        // [3.314.0] 旧実装は同群限定で、別群を経由する3者循環を見落としたまま exhaustive=true ＝「証明済み
        //   壁」を名乗っていた。coverage 保存の並べ替えが要求するのは受け手の canDo だけで、DFS の Place() は
        //   配置ごとに p.CanDo(i, sh) を検査するため、群をまたいで M に加えても不正な解は生まれない。
        //   あわせて cap で候補を切り捨てたら exhaustive を名乗らない（旧: break で打ち切ったあとも
        //   「探索し尽くした」と主張しており、真部分集合しか見ていないのに壁を証明していた）。
        bool truncated = false;
        for (int i = 0; i < p.S; i++)
        {
            if (mSeen.Contains(i) || !p.CanDo(i, v.Shift)) continue;
            if (mList.Count >= cfg.MaxInvolvedStaff) { truncated = true; break; }
            mSeen.Add(i);
            mList.Add(i);
        }
        if (mList.Count > cfg.MaxInvolvedStaff) return new ExactResult(0, 0, null, false, 0);
        var m = mList.ToArray();

        // 関与する cons1 規則（M のいずれかが担当可で、窓と交差しうるもの）
        var rules = p.Cons1.Where(c =>
            c.ShiftIdx >= 0 && c.ShiftIdx < p.K && c.Day1 >= 1 && c.Day1 <= p.T && c.Day2 >= 1 &&
            m.Any(i => p.CanDo(i, c.ShiftIdx))).ToList();

        // 各職員の行（窓外固定・窓内可変）。joint c1 は M 全員の全 cons1 fire 合計。
        var rows = new int[m.Length][];
        for (int mi = 0; mi < m.Length; mi++) rows[mi] = (int[])s[m[mi]].Clone();

        int JointC1()
        {
            int total = 0;
            for (int mi = 0; mi < m.Length; mi++)
            {
                int i = m[mi];
                foreach (var c in rules)
                {
                    if (!p.CanDo(i, c.ShiftIdx)) continue;
                    int jj = 0;
                    while (jj <= p.T - c.Day1)
                    {
                        int z = 0;
                        for (int l = 0; l < c.Day1; l++) if (rows[mi][jj + l] == c.ShiftIdx) z++;
                        if (z < c.Day2) total++;
                        jj++;
                    }
                }
            }
            return total;
        }
        int baseline = JointC1();
        // [3.279.0/外部レビューC1-03] 焦点職員 i0 の**対象窓（v.Start〜v.Start+v.WindowDays）だけ**の残 fire。
        //   旧: 全ルール×全窓の残数＝対象窓が解消可能でも別窓が残るだけで「この窓は壁」と誤認していた。
        int fi = Array.IndexOf(m, v.Staff);
        int FocusResidualOf(int[][] arr)
        {
            // [3.279.1] fi<0 は現行構造では到達不能（m は v.Staff を先頭に構築される）。将来 m の構築が
            //   変わった場合に焦点残を誤らせない防御として残置（0=「壁と主張しない」安全側）。
            if (fi < 0) return 0;
            int z = 0;
            for (int l = 0; l < v.WindowDays; l++)
            {
                int d = v.Start + l;
                if (d >= 0 && d < p.T && arr[fi][d] == v.Shift) z++;
            }
            return z < v.Required ? 1 : 0;
        }
        if (baseline == 0) return new ExactResult(0, 0, null, true, 0);

        // [3.315.0] 探索の目的関数を実際の採否と揃える。
        // 旧実装は joint c1 だけを最小化しており、厳密ピン（staffRange lo==hi）も禁止連続(c3n)も見て
        // いなかった。実データ計測では patch は出るのに採用は 0 で、却下の内訳は全件が
        // 「ピン破り」か「c3n 増」だった。つまり探索が、実採否が必ず却下する方向へ最適化していた。
        // どちらも M 内の行だけで厳密に判定できる（ピン=行の回数／c3n=行ローカルの完全一致窓）ので、
        // 葉（完全配置）で検査して採用候補から外す。これは候補生成の絞り込みであって
        // 目的関数・重みの変更ではない（MirrorCore/Evaluator/C++ は無変更・HF77 非該当）。
        //
        // ピンの意味論は exactPinRegression と同一＝「目標から遠ざかる変更のみ禁止」。既に外れて
        // いるデータ側の不整合はそのままでよく、現状維持と接近は妨げない。
        var pins = new List<int[]>(); // [mi, k, target, baseDist]
        for (int mi = 0; mi < m.Length; mi++)
        {
            int i = m[mi];
            for (int k = 0; k < p.K; k++)
            {
                int lo = p.RangeLo[i][k];
                int hi = p.RangeHi[i][k];
                if (lo == int.MinValue || hi == int.MaxValue || lo != hi) continue;
                int c = 0;
                for (int j = 0; j < p.T; j++) if (s[i][j] == k) c++;
                pins.Add(new[] { mi, k, lo, Math.Abs(c - lo) });
            }
        }
        int baseC3n = 0;
        if (p.Cons3n.Count > 0)
            for (int mi = 0; mi < m.Length; mi++) baseC3n += C1DeltaPrefilter.StaffC3nFires(p, rows[mi]);

        /// <summary>葉が実採否を通り得るか（ピンを遠ざけない・M 内の c3n 合計を増やさない）。</summary>
        bool AcceptableLeaf()
        {
            foreach (var pn in pins)
            {
                int mi = pn[0], k = pn[1];
                int c = 0;
                for (int j = 0; j < p.T; j++) if (rows[mi][j] == k) c++;
                if (Math.Abs(c - pn[2]) > pn[3]) return false;
            }
            if (p.Cons3n.Count > 0)
            {
                int f = 0;
                for (int mi = 0; mi < m.Length; mi++)
                {
                    f += C1DeltaPrefilter.StaffC3nFires(p, rows[mi]);
                    if (f > baseC3n) return false;
                }
            }
            return true;
        }

        // 各日 d の M 多重集合（並べ替え対象）と、その日の固定要素（希望ロック職員は自分の希望へ固定）。
        int nodes = 0;
        bool budgetHit = false;
        int best = baseline;
        int[][]? bestRows = null;
        // [3.274.0 監査で発見・修正] A4 provenWalls 用の焦点残の最小値。探索した全ての葉（=完全配置）で
        //   焦点残を測り最小を追跡する。旧実装は探索後に「復元されない rows（＝最後の葉）」から焦点残を
        //   計算しており、①rows が探索の副作用で汚染 ②min-joint と min-focus は別物、の2重の誤りで
        //   列挙順依存の false wall を生んでいた（provenWallsの「証明つき/誤検知ゼロ」契約違反）。
        //   葉ごとに測って最小を取ることで、exhaustive時に「coverage入替でも焦点は解消不能」を厳密に証明する。
        int minFocusResidual = FocusResidualOf(rows); // rows は現時点で元の盤面（=baseline配置）

        // DFS: days を1つずつ、その日の多重集合を M へ割り当てる全単射を枚挙。
        void AssignDay(int dayIdx, Action onComplete)
        {
            if (dayIdx == days.Count) { onComplete(); return; }
            int d = days[dayIdx];
            // その日の M の現在シフト多重集合
            var multiset = new List<int>(m.Length);
            for (int mi = 0; mi < m.Length; mi++) multiset.Add(s[m[mi]][d]);
            // 各 M 職員が取り得るシフト（希望ロックは自分の希望のみ）
            // 割当は「多重集合の要素を各職員へ配る全単射」＝バックトラッキング。
            var used = new bool[multiset.Count];
            // 分岐順序: i0 は shift を優先、他は現状維持を優先（有望領域を先に）。
            int[] OrderedSlots(int mi) => Enumerable.Range(0, multiset.Count)
                .OrderByDescending(si =>
                {
                    int sh = multiset[si];
                    int pri = 0;
                    if (m[mi] == v.Staff && sh == v.Shift) pri += 100;
                    if (sh == s[m[mi]][d]) pri += 10; // 現状維持
                    return pri;
                })
                .ToArray();

            int branchCount = 0;
            void Place(int mi)
            {
                if (budgetHit) return;
                if (mi == m.Length) { AssignDay(dayIdx + 1, onComplete); return; }
                int i = m[mi];
                int wl = p.WishLocked(i, d) ? p.Wish[i][d] : -1;
                // [3.279.0/外部レビューC1-10] 同一シフトが多重集合に複数あるとスロット単位の列挙が同値部分木を
                //   重複探索し、node予算を浪費して不必要に exhaustive=false になっていた。同じシフト値は
                //   職員 mi につき1回だけ試す（残り多重集合が同じ＝部分木は同値、の標準的な重複排除）。
                // multiset には正規化由来の -1 があり得るため index は sh+1（[-1, K-1]→[0, K]）。
                var tried = new bool[p.K + 1];
                foreach (int si in OrderedSlots(mi))
                {
                    if (used[si]) continue;
                    int sh = multiset[si];
                    if (tried[sh + 1]) continue;
                    if (!p.CanDo(i, sh)) continue;
                    if (wl >= 0 && sh != wl) continue;
                    if (mi == 0 && branchCount >= cfg.PerDayBranchCap) { budgetHit = true; break; }
                    tried[sh + 1] = true;
                    used[si] = true;
                    rows[mi][d] = sh;
                    if (mi == 0) branchCount++;
                    if (++nodes > cfg.NodeBudget) { budgetHit = true; used[si] = false; return; }
                    Place(mi + 1);
                    used[si] = false;
                    if (budgetHit) return;
                }
            }
            Place(0);
        }

        AssignDay(0, () =>
        {
            // [3.315.0] best/patch の更新だけを AcceptableLeaf でゲートする。minFocusResidual は
            //   従来どおり全葉で測る＝A4 provenWalls（「coverage入替でどう並べても焦点は解消不能」）の
            //   意味論は完全に不変。ここを制約下の最小に変えると壁判定が増える方向へ動き、3.76.0 の
            //   「false wall を出さない」原則に触れるため意図的に分ける。
            if (AcceptableLeaf())
            {
                int jc = JointC1();
                if (jc < best)
                {
                    best = jc;
                    bestRows = new int[m.Length][];
                    for (int mi = 0; mi < m.Length; mi++) bestRows[mi] = (int[])rows[mi].Clone();
                }
            }
            // 葉(=完全配置)ごとに焦点残を測り最小を追跡（rows はこの時点で全 days 割当済み）。
            int fr = FocusResidualOf(rows);
            if (fr < minFocusResidual) minFocusResidual = fr;
        });

        List<int[]>? patch = null;
        if (bestRows is { } br)
        {
            var diff = new List<int[]>();
            for (int mi = 0; mi < m.Length; mi++)
                foreach (int d in days)
                    if (br[mi][d] != s[m[mi]][d]) diff.Add(new[] { m[mi], d, br[mi][d] });
            patch = diff.Count == 0 ? null : diff;
        }
        // exhaustive のとき minFocusResidual は「coverage入替でどう並べても焦点はこれ以上減らせない」証明値。
        return new ExactResult(best, baseline, patch, !budgetHit && !truncated, minFocusResidual);
    }
}
