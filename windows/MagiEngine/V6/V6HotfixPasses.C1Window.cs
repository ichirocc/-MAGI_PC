using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// [V6HotfixPasses / フェーズ6, C1（期間要件）研磨] Kotlin原本 <c>V6HotfixPasses.kt</c> の
/// <c>applyC1WindowPolish</c>（cons1＝D日窓にシフトXをN回以上、という期間要件の研磨）＋
/// その専用ヘルパ <c>inDeficientC1Window</c>／<c>c1RowFires</c> を収める partial ファイル。
/// </summary>
public static partial class V6HotfixPasses
{
    /// <summary>day j を含む有効窓のどれかで、職員 i のシフト X が N 回未満（=c1不足）か。</summary>
    private static bool InDeficientC1Window(Problem p, int[][] work, int i, int x, int d, int n, int j)
    {
        if (d <= 0) return false;
        var w = Math.Max(0, j - d + 1);
        var wEnd = Math.Min(j, p.T - d);
        while (w <= wEnd)
        {
            var z = 0;
            for (var l = 0; l < d; l++) if (work[i][w + l] == x) z++;
            if (z < n) return true;
            w++;
        }
        return false;
    }

    /// <summary>職員 i の c1 fire 総数（全 cons1・canDo ガード。checker/MirrorCore と同一のスライド窓意味論）。</summary>
    private static int C1RowFires(Problem p, int[][] work, int i)
    {
        var fires = 0;
        foreach (var c in p.Cons1)
        {
            var x = c.ShiftIdx; var d = c.Day1; var n = c.Day2;
            if (x < 0 || x >= p.K || d <= 0 || !p.CanDo(i, x)) continue;
            var w = 0;
            while (w <= p.T - d)
            {
                var z = 0;
                for (var l = 0; l < d; l++) if (work[i][w + l] == x) z++;
                if (z < n) fires++;
                w++;
            }
        }
        return fires;
    }

    /// <summary>
    /// [ソフト研磨・C1] 期間要件 cons1（D日窓にシフトXをN回以上・職員ごと）の研磨。
    /// c1不足の (職員 i, 窓) を見つけ、その窓内で i が X でない日 j に対し、その日に X をしている提供者 i' と
    /// **同日スワップ**（i←X, i'←iの旧シフト＝被覆不変・HARD維持）して i の X を増やす。実目的関数で評価し
    /// 改善時のみ採用（keep-best＝退化なし）。汎用循環交換と違い**c1不足の窓に的を絞る**ので c1 を効率的に削る。
    /// [E11/多人数ブロック移動を反映] 同日スワップの直接相手 i' が見つからない/不採用のときは諦めず、
    /// i を X へ直接動かし、空いた旧シフト a の穴を <see cref="V6SearchOperators.FindCovUChain"/>（covU の玉突き連鎖）
    /// と同じ機構で埋め直す（a に need1 が無い/余裕があるなら連鎖不要でそのまま採用判定）。i の移動＋連鎖手をまとめて
    /// 1候補として実目的関数で評価し、改善時のみ採用（不採用時は連鎖手も含め正しく全巻き戻し）。
    ///
    /// [C1研磨アルゴリズムの再設計/回数保存移設の追加] 手A(同日スワップ)/手B(直接移動+連鎖)はどちらも
    /// 「i の X 回数を+1する」count-changing 手しか生成できない。golden_state の残差解剖(Python実測)では
    /// c1=115 fires のうち relocation-only=48（休 fires の80%が個人別回数の下限=上限で固定された職員由来）
    /// は、X追加が low/high(90/45)&gt;c1(30×窓数)で必ず isBetter に棄却され、**i自身のXを余剰位置→不足窓へ
    /// 移す回数保存の移設**だけが唯一の改善手と判明（行内2日swapの貪欲シムで c1 115→62, -46%）。
    /// 現行手A/Bにこの移設プリミティブが無い欠落を埋めるため、手A(同日交換)の直後・手B(直接移動)の前に
    /// 保存性の強い順で2手を追加する:
    ///   手R1=鏡像長方形（i=[X@j1,b@j]↔i'=[b@j1,X@j]の4セル交換）: 両職員の回数と日別人数が両方保存
    ///        （groupViol/pref/low/high/apt/c2/covU/covO/c41系まで構造的不変）＝isBetterはc1/c3系/weekly
    ///        だけの勝負になり採用されやすい最も安全な移設。
    ///   手R2=自己2日swap（i の X@j1 ↔ b@j）: i の回数は保存（low/high/apt/c2/pref/groupViol不変）だが
    ///        日別人数が変わるため、離脱側2箇所を <see cref="Problem.CovUCell"/>（source of truth）で
    ///        事前除外してから適用。
    /// どちらも c3n(HARD) は <see cref="Problem.MakesForbiddenRun"/> で事前枝刈り（見逃しても isBetter が
    /// 最終拒否＝安全側）。採否は既存と同じ <see cref="UnifiedViolationChecker.BetterReport"/>
    /// (hard→weighted→total) の keep-best のみ＝退化不能・HF77非該当（重み不変）。
    /// add-fixable（追加が唯一の解の局面）は既存手A/Bの担当のまま＝手クラスが互いに素で冗長を作らない。
    ///
    /// [手R3・局所探索の強化] 手A/R1/R2/手Bを尽くしてもなお不足しているルールに対し、アンカーセルに限定
    /// しない全ペア網羅（Xの保有movable日×非保有movable日の全ペア）を1回だけ試す。職員全体のfires
    /// （全cons1横断合計）が最も改善するペアを採用する(best-improvement)。安全性は手R2と同一の被覆ガード
    /// (covUCell)＋makesForbiddenRun事前枝刈り＋isBetter最終ゲート。
    /// </summary>
    public static CyclicSwapResult ApplyC1WindowPolish(
        MagiState state, int[][] schedule, int maxPasses = 3, Func<bool>? shouldStop = null, long seed = 0x1C1L)
    {
        var stop = shouldStop ?? (() => false);
        // [3.326.0] 回数固定(lo==hi)だけが却下した候補試行を対象別に数える（緩和対象の提示用）。
        var pinBlocks = new PinBlockAttribution();
        var p = new Problem(state);
        var work = ScheduleUtil.NormalizeSchedule(schedule, p);
        var before = UnifiedViolationChecker.Check(state, work);
        var bestRep = before;
        var applied = 0;
        var aRect = 0; var aSelf = 0;
        if (p.Cons1.Count == 0)
        {
            return new CyclicSwapResult(work, before.Total, bestRep.Total, 0,
                new[] { new MirrorLog(tag: "C1Polish", message: "cons1なし=スキップ") });
        }
        var rng = new JavaRandom(seed);

        string StaffLabel(int idx) => idx >= 0 && idx < state.StaffList.Count ? state.StaffList[idx].Name : $"#{idx}";
        string ShiftLabel(int idx) => idx >= 0 && idx < state.Shifts.Count ? state.Shifts[idx].Kigou : idx.ToString();

        // [監査で発見・3.270.0] p.wish[i][j]<0 は実現不能な希望まで動かせないと誤判定していた
        //   （3.183.0 LightMirrorOptimizer と同型のバグ）。wishLocked は canDo ガード込みで正しい。
        bool Movable(int i, int j) => !p.WishLocked(i, j);

        // [C1研磨・手B強化] staff i2 が shift x2 について day を含むいずれかの窓で不足しているか（全cons1横断）。
        //   手B(findCovUChain の玉突き連鎖)の候補選定に c1Pref として渡し、「連鎖に組み込む相手が、たまたま
        //   その相手自身のc1不足も一緒に解消する」候補を優先させる（並べ替えのみ・安全条件は不変・探索の
        //   正しさは常に isBetter が最終担保）。
        bool C1Deficient(int i2, int x2, int day)
        {
            if (day < 0 || day >= p.T) return false;
            foreach (var c2 in p.Cons1)
            {
                if (c2.ShiftIdx != x2 || c2.Day1 <= 0) continue;
                if (InDeficientC1Window(p, work, i2, x2, c2.Day1, c2.Day2, day)) return true;
            }
            return false;
        }

        // [頭打ちの理由を可視化/RangePolish=3.222.0と同型] 手A/R1/R2いずれも成立しなかった最終フォール
        //   バック(手B=直接移動+玉突き)の結果を(staff,shift,規則index)ごとに集計。「候補なし」=findCovUChainが
        //   埋め戻し相手を1人も見つけられなかった／「不採用」=候補は見つかったが実目的関数(isBetter)が
        //   総合的に拒否した、の2分類（RangePolishと同じ粒度）。休の窓ルールが解消しない理由を
        //   ユーザーがログから直接読めるようにする。
        // [3.326.0] キーに**規則index**を含める。旧: (職員,シフト) だけだったため、同じシフトに複数の
        //   期間の決まり（例「休 5日で1回以上」と「休 15日で4回以上」）があると別の規則で却下された理由が
        //   混ざって並んだ。規則ごとに分ければ「どの決まりで詰まったか」が読める。
        //   同一規則の複数の窓は依然まとめて数える（1日が複数の不足窓に属しうるため代表窓を選べない）。
        var blockStats = new Dictionary<(int, int, int), Dictionary<string, int>>();
        // [不採用の主因, 3.302.0] 「不採用」だけでは何に負けたか読めないため、拒否した候補が重み付きで
        //   最も増やした族を併記する（実機ログの c1 残存が「不採用×65 / 候補なし×4」＝ほぼ全部が拒否で、
        //   次に何を緩めるべきかが読めなかった）。AdaptiveBlockSwap と同じ WorstWorsenedFamily を共用。
        var culpritStats = new Dictionary<(int, int, int), Dictionary<string, int>>();
        void RecordBlock(int i, int x, int ri, string reason, ViolationReport? after = null, ViolationReport? before2 = null)
        {
            var key = (i, x, ri);
            if (!blockStats.TryGetValue(key, out var inner)) { inner = new Dictionary<string, int>(); blockStats[key] = inner; }
            inner[reason] = inner.GetValueOrDefault(reason) + 1;
            if (after != null && before2 != null)
            {
                var culprit = V6SearchOperators.WorstWorsenedFamily(after, before2);
                if (culprit != null)
                {
                    if (!culpritStats.TryGetValue(key, out var cinner)) { cinner = new Dictionary<string, int>(); culpritStats[key] = cinner; }
                    cinner[culprit] = cinner.GetValueOrDefault(culprit) + 1;
                }
            }
        }

        // [汎用玉突き結合フレームワーク, 3.249.0] 手B/手R3が単独では isBetter に不採用だった候補
        //   （chain/repackとも構築自体は成功したもの）を蓄積し、末尾で複数を束ねて再挑戦する。
        var combinable = new List<CombinatorialRepair.Candidate>();
        var pass = 0;
        while (pass < maxPasses)
        {
            if (stop()) break;
            var improved = false;
            // [違反セル指向] c1で違反している職員のみを起点に絞る。c1は職員ごと→改善手は必ず違反職員を
            //   含む＝ロスレス。空なら即終了でコスト0。
            // [実機ログ起因/実バグ修正] 旧実装は rep0.violations（1セル=最重1クラスのみ）を見ていたため、
            //   c1違反セルが同じセルでc3n(HARD,重み7000)等の更に重い違反も起こしている場合、そのセルの
            //   c1マークが violations 上では上書きされて消え、該当職員のc1違反自体が研磨の起点候補から
            //   漏れうる潜在バグだった。cellFamilies（1セル=重み降順の全クラスリスト、weight-priorityで
            //   discard しない）に切替えれば漏れなく検出できる。
            var rep0 = pass == 0 ? before : UnifiedViolationChecker.Check(state, work);
            var anchorStaff = new HashSet<int>();
            foreach (var (key, fams) in rep0.CellFamilies)
            {
                if (!fams.Contains("vio-c1")) continue;
                var idx = KotlinInterop.ToIntOrNull(key.Split(',')[0]);
                if (idx == null) continue;
                anchorStaff.Add(idx.Value);
            }
            if (anchorStaff.Count == 0) break;
            for (var ri = 0; ri < p.Cons1.Count; ri++)
            {
                var c = p.Cons1[ri];
                var x = c.ShiftIdx; var d = c.Day1; var n = c.Day2;
                if (x < 0 || x >= p.K || d <= 0) continue;
                for (var i = 0; i < p.S; i++)
                {
                    if (stop()) break;
                    if (!anchorStaff.Contains(i)) continue;
                    if (!p.CanDo(i, x)) continue;

                    // [移設ドナー] i 自身の X 保有日のうち「抜いても this ルールの窓が新規に不足化しない」余剰位置。
                    //   盤面が変わるたび(i,x)単位で無効化し次の j で再構築する（遅延キャッシュ）。
                    List<int>? donorsCache = null;
                    List<int> Donors()
                    {
                        if (donorsCache != null) return donorsCache;
                        var result = new List<int>();
                        for (var j1 = 0; j1 < p.T; j1++)
                        {
                            if (work[i][j1] != x || !Movable(i, j1)) continue;
                            var wStart = Math.Max(0, j1 - d + 1);
                            var wEnd = Math.Min(j1, p.T - d);
                            var surplus = true;
                            while (wStart <= wEnd)
                            {
                                var z = 0;
                                for (var l = 0; l < d; l++) if (work[i][wStart + l] == x) z++;
                                if (z <= n) { surplus = false; break; }   // 閾値ちょうど以下=抜くと新規fire→保守的に除外
                                wStart++;
                            }
                            if (surplus) result.Add(j1);
                        }
                        donorsCache = result;
                        return result;
                    }

                    for (var j = 0; j < p.T; j++)
                    {
                        // [監査(未レビュー領域再監査)] このjループはi2走査に加えfindCovUChainのBFSも伴い重い
                        //   （HF66/BlockRotationPolishと同型の予算超過対策として日ごとにも確認）。
                        if (stop()) break;
                        if (work[i][j] == x || !Movable(i, j)) continue;
                        if (!InDeficientC1Window(p, work, i, x, d, n, j)) continue;
                        var a = work[i][j];                                  // i の旧シフト
                        // [厳密ピン保護] 手A/手B は i(・i2)の自身のシフト回数を実際に変える(x+1/a-1)唯一の
                        //   手（手R1/R2/R3は同一職員内の日入替のみで回数は代数的に保存される＝対象外）。
                        //   staffRangeが下限=上限で完全固定("厳密ピン")の職員をこの手で崩さないよう、
                        //   swap前の盤面を基準にexactPinRegressionで追加ガードする（keep-best/重みは不変）。
                        var workBeforeDay = work.Copy2D();
                        var done = false;

                        // 手A: 同日スワップ。
                        for (var i2 = 0; i2 < p.S; i2++)
                        {
                            if (i2 == i || work[i2][j] != x || !Movable(i2, j) || !p.MayPlace(i2, a)) continue;
                            work[i][j] = x; work[i2][j] = a;                 // 同日スワップ（被覆不変）
                            var rep = UnifiedViolationChecker.Check(state, work);
                            var pinBadA = V6SearchOperators.ExactPinRegression(p, workBeforeDay, work);
                            if (pinBadA && IsBetter(rep, bestRep)) pinBlocks.Record(p, workBeforeDay, work);
                            if (IsBetter(rep, bestRep) && !pinBadA)
                            {
                                bestRep = rep; applied++; improved = true; done = true; break;
                            }
                            // [3.324.0/外部レビュー] 手Aのピン却下も記録する（手Bだけの部分集計だった穴を塞ぐ）。
                            // [3.347.0/敵対検証] 手Aは**ピン却下だけ**を数えており、同じ手が採点で落ちた
                            //   ときは何も残していなかった。手Bは両方残すので、同じ (職員,シフト,決まり)
                            //   の集計でピン側だけが厚くなる。どちらも i2 ごと＝同じ粒度なので対称に数える。
                            if (IsBetter(rep, bestRep) && pinBadA) RecordBlock(i, x, ri, C1PlateauDiagnosis.REASON_PIN);
                            else RecordBlock(i, x, ri, C1PlateauDiagnosis.REASON_SCORE, after: rep, before2: bestRep);
                            work[i][j] = a; work[i2][j] = x;                 // 巻き戻し
                        }
                        if (done) { donorsCache = null; continue; }

                        // [手R1] 鏡像長方形: i=[X@j1,a@j] ↔ i2=[a@j1,X@j]。回数・日別人数とも完全保存
                        //   （i2 は既に保有しているシフトしか持たない＝canDo自動成立だが規律として明示検査する）。
                        var fires0 = C1RowFires(p, work, i);
                        foreach (var j1 in Donors())
                        {
                            if (done || stop()) break;
                            if (j1 == j) continue;
                            work[i][j1] = a; work[i][j] = x;
                            var gain = fires0 - C1RowFires(p, work, i);
                            work[i][j1] = x; work[i][j] = a;                 // 判定用の一時変更は必ず復元
                            if (gain <= 0) continue;
                            for (var i2 = 0; i2 < p.S; i2++)
                            {
                                if (done || stop()) break;
                                if (i2 == i) continue;
                                if (work[i2][j1] != a || work[i2][j] != x) continue;      // 完全鏡像の相手のみ
                                if (!Movable(i2, j1) || !Movable(i2, j)) continue;
                                if (!p.MayPlace(i, x) || !p.MayPlace(i2, a)) continue;           // 構造上恒真・規律として明示
                                work[i][j1] = a; work[i][j] = x; work[i2][j1] = x; work[i2][j] = a;
                                var bad3n = p.MakesForbiddenRun(work, i, j1, a) || p.MakesForbiddenRun(work, i, j, x) ||
                                    p.MakesForbiddenRun(work, i2, j1, x) || p.MakesForbiddenRun(work, i2, j, a);
                                if (!bad3n)
                                {
                                    var rep = UnifiedViolationChecker.Check(state, work);
                                    if (IsBetter(rep, bestRep))
                                    {
                                        bestRep = rep; applied++; aRect++; improved = true; done = true;
                                        donorsCache = null;
                                        break;
                                    }
                                }
                                if (!done) { work[i][j1] = x; work[i][j] = a; work[i2][j1] = a; work[i2][j] = x; }
                            }
                        }
                        if (done) continue;

                        // [手R2] 自己2日swap: i の X@j1 ↔ a@j（回数保存＝low/high/apt/c2不変。日別人数が
                        //   変わるため離脱側2箇所を covUCell(source of truth)で事前除外してから適用）。
                        //   a が normalizeSchedule 由来の -1(範囲外/未割当) なら「本物のシフト」ではないため
                        //   R2(自己内の付け替え)の対象外とする（work[i][j1] へ -1 を書き込む不正な手を防ぐ。
                        //   手A/手Bは a=-1 でも canDo(-1)=false / findCovUChain の範囲ガードで元々安全なので
                        //   この場合も手Bへは進める＝ここは continue でなく R2 ブロックだけを囲む）。
                        if (a >= 0 && a < p.K)
                        {
                            foreach (var j1 in Donors())
                            {
                                if (done || stop()) break;
                                if (j1 == j) continue;
                                work[i][j1] = a; work[i][j] = x;
                                var gain = fires0 - C1RowFires(p, work, i);
                                work[i][j1] = x; work[i][j] = a;
                                if (gain <= 0) continue;
                                var cx = 0; var ca = 0;
                                for (var s = 0; s < p.S; s++) { if (work[s][j1] == x) cx++; if (work[s][j] == a) ca++; }
                                if (p.CovUCell(x, j1, cx - 1) > p.CovUCell(x, j1, cx)) continue;   // X の j1 離脱で covU 悪化
                                if (p.CovUCell(a, j, ca - 1) > p.CovUCell(a, j, ca)) continue;      // a の j 離脱で covU 悪化
                                work[i][j1] = a; work[i][j] = x;
                                var bad3n = p.MakesForbiddenRun(work, i, j1, a) || p.MakesForbiddenRun(work, i, j, x);
                                if (!bad3n)
                                {
                                    var rep = UnifiedViolationChecker.Check(state, work);
                                    if (IsBetter(rep, bestRep))
                                    {
                                        bestRep = rep; applied++; aSelf++; improved = true; done = true; donorsCache = null;
                                    }
                                }
                                if (!done) { work[i][j1] = x; work[i][j] = a; }
                            }
                        }
                        if (done) continue;

                        // [E11反映] 直接の交換相手が見つからない/不採用 → i を X へ動かし、空いた a の穴を
                        //   玉突き連鎖で埋め直す（findCovUChain は盤面を変えないため元値を保存して巻き戻せるようにする）。
                        work[i][j] = x;
                        // exclude=i: i は既に x へ動かした本人なので、a を埋め戻す候補から除外
                        //   （除外しないと「i が a に戻る」= i の移動そのものを打ち消す退行手をBFSが選びうる）。
                        // c1Pref=c1Deficient: 連鎖の相手選びを「その相手自身のc1不足も一緒に解消するか」で
                        //   優先付け（並べ替えのみ・見つからなければ従来どおり）。
                        var chain = V6SearchOperators.FindCovUChain(p, work, a, j, rng, exclude: i,
                            c1Pref: (s2, sh, dy) => C1Deficient(s2, sh, dy));
                        var oldVals = chain?.Select(mv => work[mv[0]][mv[1]]).ToArray();
                        if (chain != null) foreach (var mv in chain) work[mv[0]][mv[1]] = mv[2];
                        var rep2 = UnifiedViolationChecker.Check(state, work);
                        if (IsBetter(rep2, bestRep) && !pinBlocks.BlocksImproving(p, workBeforeDay, work))
                        {
                            bestRep = rep2; applied++; improved = true;
                            donorsCache = null;
                        }
                        else
                        {
                            if (chain != null && oldVals != null)
                            {
                                for (var idx = 0; idx < chain.Count; idx++) work[chain[idx][0]][chain[idx][1]] = oldVals[idx];
                                var hint = $"{StaffLabel(i)}({ShiftLabel(x)})";
                                combinable.Add(new CombinatorialRepair.Candidate(
                                    new List<int[]> { new[] { i, j, x } }.Concat(chain).ToList(), "手B", hint));
                            }
                            work[i][j] = a;
                            // [不採用の主因, 3.302.0] ピン破り（厳密ピンを崩すため却下）は違反自体が
                            //   悪化していないので主因族を持たない＝別ラベルにして混同を避ける。
                            if (chain == null) RecordBlock(i, x, ri, C1PlateauDiagnosis.REASON_NO_CANDIDATE);
                            else if (IsBetter(rep2, bestRep)) RecordBlock(i, x, ri, C1PlateauDiagnosis.REASON_PIN);
                            else RecordBlock(i, x, ri, C1PlateauDiagnosis.REASON_SCORE, after: rep2, before2: bestRep);
                        }
                    }
                }
            }
            pass++;
            if (!improved) break;
        }

        // [手R3・局所探索の強化=ユーザー指示「賢く深く網羅的に」] 手A/R1/R2/手Bを尽くしてもなお不足
        //   しているルールに対し、アンカーセルに限定しない全ペア網羅(2-opt完全探索)を1回だけ試す。
        //   真に壁がある職員（例: 休の個人上限が窓ルール最低必要回数を下回る）でも、休の「配置の仕方」
        //   次第で窓違反件数は変動しうる。既存の手A/R1/R2/手Bはいずれも「現在違反しているセルj」を
        //   アンカーに限定した局所改善のみで、その職員の休配置パターン全体を作り直す大きな手を
        //   一度も試していなかった。DP等の厳密最適化は正しさのリスクが実装前から顕在化するため不採用済みで、
        //   既存アーキテクチャに忠実な局所探索強化（手R2の一般化＝アンカー限定とdonors(改善見込みの事前判定)
        //   の両方の制約を外した全ペア評価）を採用。xの保有movable日×非保有movable日の全ペアを評価し、
        //   職員全体のfires(全cons1横断合計)が最も改善するペアを採用する(best-improvement)。安全性は
        //   手R2と同一の被覆ガード(covUCell)＋makesForbiddenRun事前枝刈り＋isBetter最終ゲート。真に壁が
        //   ある場合はgain<=0のまま全ペアが尽き、安全に諦める（退化不能）。対象は残存c1違反のある全職員
        //   （壁の有無を問わない＝壁でない職員も既存の狭い近傍だけでは見つからない改善を拾える）。
        var aRepack = 0;
        for (var i = 0; i < p.S; i++)
        {
            if (stop()) break;
            for (var ri = 0; ri < p.Cons1.Count; ri++)
            {
                if (stop()) break;
                var c = p.Cons1[ri];
                var x = c.ShiftIdx; var d = c.Day1; var n = c.Day2;
                if (x < 0 || x >= p.K || d <= 0 || !p.CanDo(i, x)) continue;
                var stillDeficient0 = Enumerable.Range(0, Math.Max(0, p.T - d + 1))
                    .Any(j => InDeficientC1Window(p, work, i, x, d, n, j));
                if (!stillDeficient0) continue;
                var hx = Enumerable.Range(0, p.T).Where(it => work[i][it] == x && Movable(i, it)).ToList();
                var ho = Enumerable.Range(0, p.T).Where(it => work[i][it] != x && Movable(i, it)).ToList();
                if (hx.Count == 0 || ho.Count == 0) { RecordBlock(i, x, ri, C1PlateauDiagnosis.REASON_NO_REPACK); continue; }
                var fires0 = C1RowFires(p, work, i);
                var bestGain = 0; var bestJx = -1; var bestJo = -1;
                foreach (var jx in hx)
                {
                    if (stop()) break;
                    foreach (var jo in ho)
                    {
                        var a = work[i][jo];
                        var cx = 0; var ca = 0;
                        for (var s = 0; s < p.S; s++) { if (work[s][jx] == x) cx++; if (work[s][jo] == a) ca++; }
                        if (p.CovUCell(x, jx, cx - 1) > p.CovUCell(x, jx, cx)) continue;
                        if (p.CovUCell(a, jo, ca - 1) > p.CovUCell(a, jo, ca)) continue;
                        work[i][jx] = a; work[i][jo] = x;
                        var bad3n = p.MakesForbiddenRun(work, i, jx, a) || p.MakesForbiddenRun(work, i, jo, x);
                        if (!bad3n)
                        {
                            var fires1 = C1RowFires(p, work, i);
                            var gain = fires0 - fires1;
                            if (gain > bestGain) { bestGain = gain; bestJx = jx; bestJo = jo; }
                        }
                        work[i][jx] = x; work[i][jo] = a;
                    }
                }
                if (bestGain > 0)
                {
                    var a = work[i][bestJo];
                    work[i][bestJx] = a; work[i][bestJo] = x;
                    var rep = UnifiedViolationChecker.Check(state, work);
                    if (IsBetter(rep, bestRep))
                    {
                        bestRep = rep; applied++; aRepack++;
                    }
                    else
                    {
                        work[i][bestJx] = x; work[i][bestJo] = a;
                        var hint = $"{StaffLabel(i)}({ShiftLabel(x)})";
                        combinable.Add(new CombinatorialRepair.Candidate(
                            new List<int[]> { new[] { i, bestJx, a }, new[] { i, bestJo, x } }, "手R3", hint));
                        RecordBlock(i, x, ri, C1PlateauDiagnosis.REASON_SCORE, after: rep, before2: bestRep);
                    }
                }
                else
                {
                    RecordBlock(i, x, ri, C1PlateauDiagnosis.REASON_NO_REPACK);
                }
            }
        }

        // [汎用玉突き結合フレームワーク, 3.249.0] 単独では不採用だった候補群を2〜4件束ねて再挑戦
        //   （c1/range/c3mn/apt/fair横断の共通ヘルパ）。stuckNames より前に実行し、結合で解消した箇所が
        //   「残存」に残らないようにする。
        var rejectedOut = new List<CombinatorialRepair.Candidate>();
        var c1CombStats = new CombinatorialRepair.Stats();
        bestRep = CombinatorialRepair.CombineAndApply(
            state, work, bestRep, Enumerable.Reverse(combinable).ToList(), IsBetter,
            shouldStop: stop, stats: c1CombStats, p: p, leftover: rejectedOut);
        applied += c1CombStats.CombosAccepted;

        // [頭打ちの理由を可視化/RangePolish=3.222.0と同型] 手B(直接移動+玉突き)が最終的に失敗した
        //   (staff,ルールのシフト)のうち、最終盤面でなお当該窓が不足しているものだけを「残存」として表示
        //   （途中で別の手/別のjで解消済みなら除外）。「候補なし」=玉突き相手が1人も見つからない構造的
        //   ブロック／「不採用」=候補は見つかったが総合的に isBetter が拒否（他族とのトレードオフで負け）。
        string? BuildStuckLabel((int Staff, int Shift, int RuleIndex) key, IReadOnlyDictionary<string, int> reasons)
        {
            var (i, x, ri) = key;
            var rule = ri >= 0 && ri < p.Cons1.Count ? p.Cons1[ri] : null;
            var stillDeficient = rule != null && rule.ShiftIdx == x && rule.Day1 > 0 &&
                Enumerable.Range(0, Math.Max(0, p.T - rule.Day1 + 1))
                    .Any(j => InDeficientC1Window(p, work, i, x, rule.Day1, rule.Day2, j));
            if (!stillDeficient) return null;
            var lbl = $"{StaffLabel(i)} {ShiftLabel(x)}({rule!.Day1}日{rule.Day2}回)";
            if (reasons.Count == 0) return lbl;
            var top = reasons.OrderByDescending(kv => kv.Value).First();
            // [不採用の主因, 3.302.0] 「不採用」のときだけ、拒否された候補が重み付きで最も壊した族を
            //   上位2件まで併記する（何を緩めれば通るのかがログから直接読める）。
            var culprits = "";
            if (top.Key == C1PlateauDiagnosis.REASON_SCORE && culpritStats.TryGetValue(key, out var cmap))
            {
                var joined = string.Join(" ", cmap.OrderByDescending(kv => kv.Value).Take(2).Select(kv => $"{kv.Key}:{kv.Value}"));
                if (joined.Length > 0) culprits = $" 主因 {joined}";
            }
            return $"{lbl}({top.Key}×{top.Value}{culprits})";
        }

        var stuckNames = blockStats
            .Select(kv => BuildStuckLabel(kv.Key, kv.Value))
            .Where(s => s != null)
            .Select(s => s!)
            .Distinct()
            .ToList();

        // [構造化診断, 3.322.0] 上の「残存:」はログ文字列だが、同じ材料を構造化して UI まで運ぶ
        //   （文字列を後から解析させない）。判定は最終盤面で不足が残っている (職員,シフト,規則index) だけ。
        var plateau = C1PlateauDiagnosis.Build(
            remainingC1: bestRep.Breakdown.GetValueOrDefault("c1", 0),
            blockStats: blockStats.ToDictionary(kv => kv.Key, kv => (IReadOnlyDictionary<string, int>)kv.Value),
            culpritStats: culpritStats.ToDictionary(kv => kv.Key, kv => (IReadOnlyDictionary<string, int>)kv.Value),
            staffName: StaffLabel,
            shiftKigou: ShiftLabel,
            ruleLabel: ri => ri >= 0 && ri < p.Cons1.Count ? $"{p.Cons1[ri].Day1}日で{p.Cons1[ri].Day2}回以上" : "?",
            // [3.326.0] 規則単位で判定する。旧: 同じシフトの**どれか**の決まりが残っていれば全部残す
            //   （別の決まりで却下された理由が、解消済みの決まりの理由として並びうる）。
            stillDeficient: (i, x, ri) =>
            {
                var rc = ri >= 0 && ri < p.Cons1.Count ? p.Cons1[ri] : null;
                return rc != null && rc.ShiftIdx == x && rc.Day1 > 0 &&
                    Enumerable.Range(0, Math.Max(0, p.T - rc.Day1 + 1))
                        .Any(j => InDeficientC1Window(p, work, i, x, rc.Day1, rc.Day2, j));
            });

        var c1CombSummary = c1CombStats.Summary();
        var c1Before = before.Breakdown.GetValueOrDefault("c1", 0);
        var c1After = bestRep.Breakdown.GetValueOrDefault("c1", 0);
        var msg = $"期間要件(c1)研磨: c1 {c1Before}->{c1After} / total {before.Total}->{bestRep.Total} " +
            $"HARD {before.Hard}->{bestRep.Hard} 採用{applied}回(鏡像:{aRect} 自己:{aSelf} 再配置:{aRepack})";
        if (applied == 0 && c1Before > 0) msg += " [頭打ち=改善手なし]";
        if (stuckNames.Count > 0) msg += $" 残存: {string.Join(", ", stuckNames)}";
        if (c1CombSummary.Length > 0) msg += $" / {c1CombSummary}";
        var logs = new[] { new MirrorLog(tag: "C1Polish", message: msg) };
        return new CyclicSwapResult(work, before.Total, bestRep.Total, applied, logs, plateau, pinBlocks.Attempts, pinBlocks, rejectedOut);
    }
}
