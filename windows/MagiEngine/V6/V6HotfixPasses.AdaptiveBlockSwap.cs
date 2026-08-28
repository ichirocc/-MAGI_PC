using MagiEngine.Model;

namespace MagiEngine.V6;

public static partial class V6HotfixPasses
{
    /// <summary>
    /// [長期ブロック交換の候補長, Kotlin原本] 月次勤務表で「局所交換では越えにくい」谷を越えるための
    /// 非等間隔ポートフォリオで、短い方から順に 11/13/17/19/23/28 日を試す。
    /// 28 は 2月（28日）で「1か月まるごと」の交換を確保するための長さ（素数列ではない点は意図的）。
    /// [移植メモ] Kotlin原本コメントは「15 日固定の旧 BlockSwapPolish は後方互換のため残す」と書くが、
    /// 実際には Kotlin 3.300.0 でその旧パスは定義ごと削除済み（コメントの stale・本移植の対象はこの
    /// 適応版のみ）。
    /// </summary>
    private static readonly int[] AdaptiveBlockLengths = { 11, 13, 17, 19, 23, 28 };

    /// <summary>
    /// 巡回交換の1候補。<see cref="Cycle"/> は巡回順で、<c>Cycle[t]</c> は <c>Cycle[(t+1) % n]</c> の
    /// シフトを受け取る（n=2 なら通常の2者交換と同一）。<see cref="Days"/> は据え置き分を除いた実際の
    /// 交換日。
    /// </summary>
    private sealed record BlockSwapCandidate(int[] Cycle, int Start, int Length, long Priority, int Differences, int[] Days);

    /// <summary>
    /// 指定した長さの勤務ブロックを、他職員と丸ごと交換／巡回交換する適応ポートフォリオ演算子。
    ///
    /// 旧 applyBlockSwapPolish（Kotlin 3.300.0 で削除）は「同一担当グループ × 15日 × 2者」に固定されて
    /// いた。本演算子は 11/13/17/19/23/28日を独立した候補プールとして持ち、各長さから有望候補を必ず
    /// 残す。これにより、短い窓では途中退化して届かない個人回数・apt・週偏り・連続規則の同時改善を、
    /// 期間ごとのまとまりとして探索できる。
    ///
    /// <b>可変長の巡回交換（2者交換〜N者巡回, <paramref name="maxCycle"/>）</b>: 2者交換だけでは
    /// 「A の X を B へ渡したいが、B の持ち札は A に不要」という局面で成立しない。3者以上の巡回
    /// （A←B←C←A）ならこの非対称な譲り合いが閉じる。既存 <see cref="ApplyBlockRotationPolish"/> も
    /// 3者回転を持つが<b>窓が2〜3日固定・全日movable必須</b>のため、長期ブロックの巡回は本演算子だけが
    /// 探索する。
    ///
    /// 候補生成は全列挙でなく<b>改善グラフ（cyclic exchange / VLSN）</b>:
    ///  1. ブロック (start, length) ごとに、有向辺 u→v の重み＝「u が v のブロックを受け取ったときの
    ///     u 個人の回数ペナルティ改善量」を <c>PersonalBalancePenalty</c> で見積もる。この重みは
    ///     <b>各参加者が「自分の札を出して直前者の札を受け取る」ぶんだけで決まる</b>ため辺ごとに分解でき、
    ///     巡回全体の見積り改善量は辺重みの単純和になる。
    ///  2. 最小番号アンカー＋深さ <paramref name="maxCycle"/> の DFS で、見積り改善量が正になる巡回を
    ///     集める。
    ///  3. 集めた巡回について<b>実際の</b>交換日集合（全参加者が movable かつ各辺の canDo を満たす日）を
    ///     取り直し、正式な候補を作る。辺ごとの見積りは3者以上では交換日をやや広く見積もる近似だが、
    ///     <b>順位付け専用</b>であり採否は必ず正式 checker が決めるため安全側。2者の場合は近似でなく厳密。
    ///
    /// 安全性:
    /// - 同日の値を参加者間で巡回させるだけなので、日ごとのシフト多重集合＝全体の被覆量は保存する。
    /// - 異なる担当グループ間でも、その日その辺の受け手が担当可能な場合だけ候補化する。
    /// - 実現可能な希望で固定されたセル・担当不可の日は<b>据え置き</b>（その日は交換しない）、残りの日
    ///   だけを入れ替える。ブロック全体を棄却しないため、希望が多いデータでも長い期間の候補が成立する。
    /// - 厳密回数ピンを破る候補は ExactPinRegression で除外する。
    /// - 最終採否は <see cref="UnifiedViolationChecker"/> と <c>BetterReport</c> の正式順
    ///   （HARD → weightedScore → total）だけで決めるため、探索順にかかわらず退化しない。
    /// - 正式評価（フル checker）の回数は <paramref name="maxEvaluations"/> で据え置き。巡回を足しても
    ///   <b>checker コストは増えない</b>（増えるのは安価な候補生成だけ）。候補プールは
    ///   (ブロック長 × 巡回人数) ごとに分けラウンドロビンで評価するため、長い28日案が11日案に、5者案が
    ///   2者案に押し出されることもない。<paramref name="maxEvaluations"/> は pass ごとの枠（呼び出し
    ///   全体では <c>maxPasses × maxEvaluations</c> まで走る）。<paramref name="maxCycleVisits"/> も
    ///   全体予算ではない＝DFS の分岐を (ブロック長, 開始日) ごとに抑える上限（締切は各ブロックの入口で
    ///   確認する）。
    /// </summary>
    public static CyclicSwapResult ApplyAdaptiveBlockSwapPolish(
        MagiState state,
        int[][] schedule,
        int[]? blockLens = null,
        int maxPasses = 2,
        int candidatesPerLength = 8,
        int maxEvaluations = 48,
        int maxFocusStaff = 16,
        int maxCycle = 5,
        int maxCycleVisits = 50_000,
        /// <summary>
        /// 禁止連続(c3n)が正味増える候補を<b>候補生成の段階で</b>捨てるか。既定は
        /// <see cref="PolishGate.FilterC3nIncrease"/>（設定タブ→詳細設定のトグル・既定 false＝捨てない）。
        /// c3n は HARD なので増える候補は最終的に <c>isBetter</c> が必ず却下する＝true/false で<b>採用
        /// 結果は変わらない</b>。true にすると詰んだ候補へフル checker を呼ばなくなり評価枠を節約できる。
        /// </summary>
        bool? filterC3nIncrease = null,
        Func<bool>? shouldStop = null)
    {
        var lens = blockLens ?? AdaptiveBlockLengths;
        var filterC3n = filterC3nIncrease ?? PolishGate.FilterC3nIncrease;
        var stop = shouldStop ?? (() => false);

        // [3.326.0] 回数固定(lo==hi)だけが却下した候補試行を対象別に数える（緩和対象の提示用）。
        var pinBlocks = new PinBlockAttribution();
        var p = new Problem(state);
        var work = ScheduleUtil.NormalizeSchedule(schedule, p);
        var before = UnifiedViolationChecker.Check(state, work);
        var lengths = lens.Where(l => l >= 1 && l <= p.T).Distinct().OrderBy(l => l).ToList();
        var cycleCap = Math.Max(maxCycle, 2);
        if (p.S < 2 || lengths.Count == 0 || maxPasses <= 0 || candidatesPerLength <= 0 || maxEvaluations <= 0 || maxFocusStaff <= 0)
        {
            return new CyclicSwapResult(work, before.Total, before.Total, 0,
                new List<MirrorLog> { new MirrorLog(tag: "AdaptiveBlockSwap", message: "対象長または職員ペアなし=スキップ") });
        }

        string Name(int i) => i >= 0 && i < state.StaffList.Count ? state.StaffList[i].Name : $"#{i}";
        bool Movable(int i, int j) => !p.WishLocked(i, j);

        // 巡回を1つ回す。forward が真なら cycle[t] <- cycle[t+1]、偽なら逆回し。
        // 逆回しは順回しの厳密な逆変換なので、評価のための「適用→検査→巻き戻し」に使える
        // （2者交換は順逆が同一＝従来の swap と一致）。
        void Rotate(BlockSwapCandidate candidate, bool forward)
        {
            var cycle = candidate.Cycle;
            var n = cycle.Length;
            var vals = new int[n];
            foreach (var j in candidate.Days)
            {
                for (var t = 0; t < n; t++) vals[t] = work[cycle[t]][j];
                for (var t = 0; t < n; t++)
                {
                    var src = forward ? (t + 1) % n : (t + n - 1) % n;
                    work[cycle[t]][j] = vals[src];
                }
            }
        }

        // range/apt は長い交換で動かしたい主対象。ここは候補の順位付け専用であり、採否は必ず正式checkerに委譲する。
        long PersonalBalancePenalty(int staff, int shift, int count)
        {
            var outVal = 0L;
            var lo = p.RangeLo[staff][shift];
            var hi = p.RangeHi[staff][shift];
            if (lo != int.MinValue && count < lo) outVal += (long)(lo - count) * 90L;
            if (hi != int.MaxValue && count > hi) outVal += (long)(count - hi) * 45L;
            var apt = p.Apt[staff][shift];
            if (apt >= 0) outVal += Math.Abs(count - apt);
            return outVal;
        }

        long[] StaffPressure(ViolationReport report)
        {
            var outArr = new long[p.S];
            void Add(string key, string cls)
            {
                var i = KotlinInterop.ToIntOrNull(key.Split(',')[0]);
                if (i is null || i.Value < 0 || i.Value >= p.S) return;
                var family = cls.StartsWith("vio-") ? cls[4..] : cls;
                outArr[i.Value] += Math.Max((long)MirrorKeys.WeightOf(family), 1L);
            }
            foreach (var (key, cls) in report.Violations) Add(key, cls);
            foreach (var (key, cls) in report.CountViolations) Add(key, cls);
            foreach (var (family, rows) in report.DistLocations)
            {
                var weight = Math.Max((long)MirrorKeys.WeightOf(family), 1L);
                foreach (var row in rows)
                {
                    if (row.Count == 0) continue;
                    var i = row[0];
                    if (i >= 0 && i < p.S) outArr[i] += weight;
                }
            }
            return outArr;
        }

        /// <summary>
        /// [ピン保存交換] 交換日 <paramref name="swapDays"/> を「厳密ピン(lo==hi)のシフト回数が1つも
        /// 動かない」部分集合へ絞る。絞れた場合だけ true（<paramref name="swapDays"/> を破壊的に更新）。
        ///
        /// 対象は<b>いま満たされている</b>厳密ピンだけ（<c>counts == lo == hi</c>）。すでに外れている
        /// ピンは動かして直せる余地があるため拘束しない（悪化は従来どおり ExactPinRegression が弾く）。
        ///
        /// 各日 j について、参加者 t のピン付きシフト k の増減
        /// <c>d = [直前者が k] - [自分が k]</c>（∈ {-1,0,+1}）を並べた<b>符号ベクトル</b>を作る。
        /// 交換日集合の総和がゼロベクトルならピンは1つも動かない。ゼロベクトルの日は常に採り、非ゼロの
        /// 日は<b>符号が正反対の日と対にして</b>採る（打ち消し合う）。3日以上での相殺は拾わないが安価で
        /// 安全側（採れなかった日を落とすだけ＝退化しない）。
        /// </summary>
        bool BalancePinnedDays(int[] cycle, List<int> swapDays, int[][] counts)
        {
            var n = cycle.Length;
            // いま満たされている厳密ピン (参加者位置, シフト) を集める。
            var slots = 0;
            var slotStaff = new int[32];
            var slotShift = new int[32];
            for (var t = 0; t < n; t++)
            {
                var i = cycle[t];
                for (var k = 0; k < p.K; k++)
                {
                    var lo = p.RangeLo[i][k];
                    if (lo == int.MinValue || lo != p.RangeHi[i][k] || counts[i][k] != lo) continue;
                    if (slots >= 31) return true;   // 対象が多すぎる＝この安価な相殺では扱えない（従来どおり）
                    slotStaff[slots] = t; slotShift[slots] = k; slots++;
                }
            }
            if (slots == 0) return true;   // ピン無し＝制約なし（コストゼロで従来と同一）

            long SignatureOf(int j)
            {
                var sig = 0L;
                for (var s = 0; s < slots; s++)
                {
                    var t = slotStaff[s];
                    var k = slotShift[s];
                    var mine = work[cycle[t]][j] == k ? 1 : 0;
                    var incoming = work[cycle[(t + 1) % n]][j] == k ? 1 : 0;
                    var d = incoming - mine;
                    if (d > 0) sig |= 1L << (2 * s);
                    else if (d < 0) sig |= 2L << (2 * s);
                }
                return sig;
            }
            // 符号の反転（+1↔-1 のビットを入れ替える）。
            long Negate(long sig) => ((sig & 0x5555_5555_5555_5555L) << 1) | ((sig >>> 1) & 0x5555_5555_5555_5555L);

            var bySig = new Dictionary<long, List<int>>();
            foreach (var j in swapDays)
            {
                var sig = SignatureOf(j);
                if (!bySig.TryGetValue(sig, out var list)) { list = new List<int>(); bySig[sig] = list; }
                list.Add(j);
            }

            var kept = new List<int>(swapDays.Count);
            if (bySig.TryGetValue(0L, out var zeroList)) kept.AddRange(zeroList);
            var done = new HashSet<long> { 0L };
            foreach (var (sig, days) in bySig)
            {
                if (done.Contains(sig)) continue;
                done.Add(sig);
                var opposite = Negate(sig);
                done.Add(opposite);
                if (!bySig.TryGetValue(opposite, out var other)) continue;
                var pairs = Math.Min(days.Count, other.Count);
                for (var t = 0; t < pairs; t++) { kept.Add(days[t]); kept.Add(other[t]); }
            }
            if (kept.Count < 2) return false;
            kept.Sort();
            swapDays.Clear();
            swapDays.AddRange(kept);
            return true;
        }

        /// <summary>
        /// 巡回 <paramref name="cycle"/> をブロック (start, length) に適用する正式な候補を作る。
        /// 交換日は「全参加者が movable」「その日の受け渡しが全辺 canDo」「実際に値が動く」を満たす日
        /// だけ。
        /// </summary>
        BlockSwapCandidate? CandidateFor(int[] cycle, int start, int length, int[][] counts, long[] pressure)
        {
            var n = cycle.Length;
            var vals = new int[n];
            var swapDays = new List<int>(length);
            for (var j = start; j < start + length; j++)
            {
                var ok = true;
                for (var t = 0; t < n; t++)
                {
                    var i = cycle[t];
                    // [3.291.0 候補生成の緩和] 希望固定・担当不可の日はブロックごと棄却せず据え置く。
                    if (!Movable(i, j)) { ok = false; break; }
                    var v = work[i][j];
                    if (v < 0 || v >= p.K) { ok = false; break; }
                    vals[t] = v;
                }
                if (!ok) continue;
                var changes = false;
                for (var t = 0; t < n; t++)
                {
                    var incoming = vals[(t + 1) % n];
                    if (incoming != vals[t]) changes = true;
                    if (!p.CanDo(cycle[t], incoming)) { ok = false; break; }
                }
                if (!ok || !changes) continue;
                swapDays.Add(j);
            }
            if (swapDays.Count < 2) return null;
            // [3.294.0 ピン保存交換] 交換日の集合を「厳密ピン(lo==hi)の回数が変わらない」ように選び直す。
            //   3.293.0 の不採用内訳で、採用0の55〜80%が ExactPinRegression のピン破りと判明した
            //   （実データは10名中9名の「休」が厳密ピン＝長いブロックを丸ごと交換すると必ず回数が動く）。
            if (!BalancePinnedDays(cycle, swapDays, counts)) return null;

            // [3.295.0 境界c3nの事前フィルタ / 3.296.0 で既定OFF] 3.294.0 でピン破りを消した結果、
            //   残る不採用は全て必須増＝c3n（禁止連続）になった。この巡回交換では covU/covO は同日置換で
            //   不変・groupViol は canDo・pref は movable で不変なので、変化しうる HARD は c3n だけ。
            //   c3n は職員行ローカルなので、参加者の行に交換を当てた fire 数を数えれば近似でなく厳密に
            //   判定できる。既定 OFF（Kotlin 3.296.0）: フィルタは firesAfter > firesBefore の候補だけを
            //   落とす＝減る・同数の候補は元から通しているため、外しても採用は増えない（c3n は HARD なので
            //   増える候補は isBetter が第1キーで必ず却下）。ON にすると構造的に詰んだ候補へ checker を
            //   呼ばなくなり、評価枠を soft 判定まで進める候補へ回せる。
            if (filterC3n && p.Cons3n.Count > 0)
            {
                var firesBefore = 0;
                var firesAfter = 0;
                for (var t = 0; t < n; t++)
                {
                    var self = cycle[t];
                    var giver = cycle[(t + 1) % n];
                    var row = (int[])work[self].Clone();
                    firesBefore += C1DeltaPrefilter.StaffC3nFires(p, row);
                    foreach (var j in swapDays) row[j] = work[giver][j];
                    firesAfter += C1DeltaPrefilter.StaffC3nFires(p, row);
                }
                if (firesAfter > firesBefore) { TuningTelemetry.IncrementC3nFilterSkipped(); return null; }
            }

            var differences = swapDays.Count;
            // 1日だけの交換は既存の同日交換/同日3者回転(CyclicSwap)と同一＝「期間をまとめて入れ替える」手にならないので除外。
            if (differences < 2) return null;

            var beforeBalance = 0L;
            var afterBalance = 0L;
            var pressureSum = 0L;
            var delta = new int[p.K];
            for (var t = 0; t < n; t++)
            {
                var self = cycle[t];
                var giver = cycle[(t + 1) % n];
                Array.Clear(delta, 0, delta.Length);
                foreach (var j in swapDays) { delta[work[self][j]]--; delta[work[giver][j]]++; }
                for (var k = 0; k < p.K; k++)
                {
                    if (delta[k] == 0) continue;
                    beforeBalance += PersonalBalancePenalty(self, k, counts[self][k]);
                    afterBalance += PersonalBalancePenalty(self, k, counts[self][k] + delta[k]);
                }
                pressureSum += pressure[self];
            }
            // 大きい推定改善を優先しつつ、違反に関与する職員と実際に変わるセル数をタイブレークに使う。
            var priority = (beforeBalance - afterBalance) * 1_000_000L + pressureSum * 16L + differences;
            return new BlockSwapCandidate((int[])cycle.Clone(), start, length, priority, differences, swapDays.ToArray());
        }

        var bestRep = before;
        var applied = 0;
        var generated = 0;            // DFS が列挙した巡回の数（見積りキーだけで選別する安価な段）
        var builtCandidates = 0;      // 実候補まで組み立てた数（プール上位のみ）
        var builtByCycleSize = new SortedDictionary<int, int>();   // 巡回人数別の実候補数（多者交換が実際に出ているかの診断）
        var rejectReasons = new Dictionary<string, int>();  // 不採用の理由別件数（採用0のとき何に負けたか）
        var rejectCulprits = new Dictionary<string, int>(); // 悪化の主因になった族（重み付きで最も増えた族）
        var evaluated = 0;
        var cycleHits = 0;            // 3者以上の巡回として採用した手数（2者交換と区別してログに出す）
        var selectedLabels = new List<string>();
        var pass = 0;
        while (pass < maxPasses && !stop())
        {
            var counts = ScheduleUtil.CountMatrix(p, work);
            var pressure = StaffPressure(bestRep);
            // 参加者の母集団は違反関与度の高い順に絞る（DFS の分岐を p.S でなく maxFocusStaff で抑える）。
            var focus = Enumerable.Range(0, p.S)
                .OrderByDescending(i => pressure[i]).ThenBy(i => i)
                .Take(Math.Min(maxFocusStaff, p.S))
                .OrderBy(i => i)
                .ToList();
            if (focus.Count < 2) break;
            var nf = focus.Count;

            // 候補は (ブロック長 × 巡回人数) ごとの固定サイズプールへ。どちらの軸でも押し出されない。
            // [2段階生成] ①DFS は巡回を「見積りキーだけ」で選別する（実候補を作らない＝O(1)/巡回で
            //   数十万件を捌ける） ②各プールに残った上位だけ CandidateFor で実候補にする。
            //   見積りキー = 個人回数の改善見積り×1e6 ＋ 参加者の違反関与度×16（実 priority と同順序）。
            //   [見積り0の巡回も捨てない]: ブロック交換の本命は c1/連続規則/曜日偏りの同時改善で、
            //   個人回数が動かない（見積り0の）手が実際に採用されることがあるため。
            //   段①のプール幅は段②より広く取る（stageOneWidth）。見積りキーの上位が実候補化で落ちる
            //   （交換成立日が1日以下）ことは巡回人数が増えるほど起きやすく、幅が狭いとその下にある
            //   成立候補まで一緒に失うため。実候補化は高々 バケット数×幅 回で安価。
            var bucketW = Math.Max(cycleCap - 1, 1);
            var bucketCount = lengths.Count * bucketW;
            var stageOneWidth = candidatesPerLength * 8;
            var poolKeys = new long[bucketCount * stageOneWidth];
            var poolNodes = new long[bucketCount * stageOneWidth];
            var poolStart = new int[bucketCount * stageOneWidth];
            var poolSize = new int[bucketCount];
            var poolMinKey = new long[bucketCount];
            void RecordCandidate(int bucket, long key, long nodes, int start)
            {
                generated++;
                var baseIdx = bucket * stageOneWidth;
                var size = poolSize[bucket];
                if (size < stageOneWidth)
                {
                    poolKeys[baseIdx + size] = key; poolNodes[baseIdx + size] = nodes; poolStart[baseIdx + size] = start;
                    poolSize[bucket] = size + 1;
                    if (size == 0 || key < poolMinKey[bucket]) poolMinKey[bucket] = key;
                    return;
                }
                // 満杯後の却下は O(1)（最小キーを保持）。採用時のみ O(幅) で最小を取り直す。
                if (key <= poolMinKey[bucket]) return;
                var worst = 0;
                for (var t = 1; t < stageOneWidth; t++) if (poolKeys[baseIdx + t] < poolKeys[baseIdx + worst]) worst = t;
                poolKeys[baseIdx + worst] = key; poolNodes[baseIdx + worst] = nodes; poolStart[baseIdx + worst] = start;
                var mn = poolKeys[baseIdx];
                for (var t = 1; t < stageOneWidth; t++) if (poolKeys[baseIdx + t] < mn) mn = poolKeys[baseIdx + t];
                poolMinKey[bucket] = mn;
            }

            var edge = new long[nf][];
            var edgeOk = new bool[nf][];
            for (var i = 0; i < nf; i++) { edge[i] = new long[nf]; edgeOk[i] = new bool[nf]; }
            var delta = new int[p.K];
            var used = new bool[nf];
            // 巡回は focus 添字を8bitずつ詰めた Long で持ち回す（最大5者=40bit）。
            var packable = nf <= 255;

            for (var li = 0; li < lengths.Count; li++)
            {
                var length = lengths[li];
                if (stop() || !packable) break;
                for (var start = 0; start <= p.T - length; start++)
                {
                    if (stop()) break;
                    // 1) 辺重み（u が v のブロックを受け取ったときの u 個人の改善見積り）を作る。
                    for (var ui = 0; ui < nf; ui++)
                    {
                        var u = focus[ui];
                        for (var vi = 0; vi < nf; vi++)
                        {
                            edge[ui][vi] = 0L; edgeOk[ui][vi] = false;
                            if (ui == vi) continue;
                            var v = focus[vi];
                            Array.Clear(delta, 0, delta.Length);
                            var any = false;
                            for (var j = start; j < start + length; j++)
                            {
                                if (!Movable(u, j) || !Movable(v, j)) continue;
                                var a = work[u][j];
                                var b = work[v][j];
                                if (a == b || a < 0 || a >= p.K || b < 0 || b >= p.K) continue;
                                if (!p.CanDo(u, b)) continue;
                                delta[a]--; delta[b]++; any = true;
                            }
                            if (!any) continue;
                            var gain = 0L;
                            for (var k = 0; k < p.K; k++)
                            {
                                if (delta[k] == 0) continue;
                                gain += PersonalBalancePenalty(u, k, counts[u][k]) -
                                    PersonalBalancePenalty(u, k, counts[u][k] + delta[k]);
                            }
                            edge[ui][vi] = gain; edgeOk[ui][vi] = true;
                        }
                    }

                    // 2) 最小番号アンカーの DFS で巡回を列挙し、見積りキーで各プールへ入れる。
                    var visits = 0;
                    var startCapture = start;
                    void Dfs(int anchor, int depth, int last, long sum, long pres, long nodes)
                    {
                        if (depth >= 2 && edgeOk[last][anchor])
                        {
                            var key = (sum + edge[last][anchor]) * 1_000_000L + pres * 16L;
                            RecordCandidate(li * bucketW + (depth - 2), key, nodes, startCapture);
                        }
                        if (depth >= cycleCap) return;
                        for (var ni = anchor + 1; ni < nf; ni++)
                        {
                            if (used[ni] || !edgeOk[last][ni]) continue;
                            if (++visits > maxCycleVisits) return;
                            used[ni] = true;
                            Dfs(anchor, depth + 1, ni, sum + edge[last][ni], pres + pressure[focus[ni]],
                                nodes | ((long)ni << (8 * depth)));
                            used[ni] = false;
                            if (visits > maxCycleVisits) return;
                        }
                    }
                    for (var ai = 0; ai < nf; ai++)
                    {
                        if (visits > maxCycleVisits) break;
                        used[ai] = true;
                        Dfs(ai, 1, ai, 0L, pressure[focus[ai]], ai);
                        used[ai] = false;
                    }
                }
            }

            // 3) 各プールの上位だけを実候補にし、(ブロック長 × 巡回人数) からラウンドロビンで取り出す。
            var ordered = new List<List<BlockSwapCandidate>>(bucketCount);
            for (var b = 0; b < bucketCount; b++)
            {
                var size = poolSize[b];
                if (size == 0) continue;
                var n = (b % bucketW) + 2;
                var length = lengths[b / bucketW];
                var built = new List<BlockSwapCandidate>(size);
                for (var t = 0; t < size; t++)
                {
                    var packed = poolNodes[b * stageOneWidth + t];
                    var cyc = new int[n];
                    for (var it = 0; it < n; it++) cyc[it] = focus[(int)((packed >>> (8 * it)) & 0xFFL)];
                    var candidate = CandidateFor(cyc, poolStart[b * stageOneWidth + t], length, counts, pressure);
                    if (candidate != null) built.Add(candidate);
                }
                if (built.Count == 0) continue;
                builtCandidates += built.Count;
                builtByCycleSize[n] = builtByCycleSize.TryGetValue(n, out var bv) ? bv + built.Count : built.Count;
                ordered.Add(built
                    .OrderByDescending(c => c.Priority)
                    .ThenByDescending(c => c.Differences)
                    .ThenBy(c => c.Start)
                    .ThenBy(c => c.Cycle[0])
                    .Take(candidatesPerLength)
                    .ToList());
            }
            if (ordered.Count == 0) break;
            var ranked = new List<BlockSwapCandidate>();
            var rank = 0;
            while (ordered.Any(pool => rank < pool.Count))
            {
                foreach (var pool in ordered) if (rank < pool.Count) ranked.Add(pool[rank]);
                rank++;
            }
            if (ranked.Count == 0) break;

            var workBeforeEval = work.Copy2D();
            BlockSwapCandidate? chosen = null;
            ViolationReport? chosenRep = null;
            var checkedThisPass = 0;
            foreach (var candidate in ranked)
            {
                if (stop() || checkedThisPass >= maxEvaluations) break;
                Rotate(candidate, forward: true);
                var report = UnifiedViolationChecker.Check(state, work);
                var pinRegression = V6SearchOperators.ExactPinRegression(p, workBeforeEval, work);
                // [3.326.0] ピンだけが止めた候補を対象別に記録する。**盤面を戻す前に**呼ぶ
                //   （Record は after 盤面を読むため、Rotate で復元したあとでは間に合わない）。
                if (pinRegression && UnifiedViolationChecker.BetterReport(report, bestRep)) pinBlocks.Record(p, workBeforeEval, work);
                Rotate(candidate, forward: false);
                checkedThisPass++;
                evaluated++;
                if (!pinRegression && UnifiedViolationChecker.BetterReport(report, bestRep) &&
                    (chosenRep == null || UnifiedViolationChecker.BetterReport(report, chosenRep)))
                {
                    chosen = candidate;
                    chosenRep = report;
                }
                else
                {
                    // [不採用理由の分類] 採用0のとき「何に負けたか」がログから読めるようにする
                    //   （RangePolish 3.222.0・C1Polish 3.236.0 の頭打ち理由と同じ趣旨）。
                    //   分類は BetterReport の判定順（HARD → weightedScore → total）と厳密に一致させる。
                    string why;
                    if (pinRegression) why = "ピン破り";
                    else if (report.Hard > bestRep.Hard) why = "必須増";
                    else if (report.Hard < bestRep.Hard) why = "採用手に劣後";   // bestRep には勝つが同パスの別候補に負けた
                    else if (report.WeightedScore > bestRep.WeightedScore) why = "重み悪化";
                    else if (report.WeightedScore < bestRep.WeightedScore) why = "採用手に劣後";
                    else if (report.Total < bestRep.Total) why = "採用手に劣後";
                    else if (report.Total > bestRep.Total) why = "件数悪化";
                    else why = "同値";
                    rejectReasons[why] = rejectReasons.TryGetValue(why, out var rv) ? rv + 1 : 1;
                    if (why is "重み悪化" or "必須増")
                    {
                        // 重み付きで最も増えた族＝この手が壊した本体（共通ヘルパー WorstWorsenedFamily）。
                        var culprit = V6SearchOperators.WorstWorsenedFamily(report, bestRep);
                        if (culprit != null) rejectCulprits[culprit] = rejectCulprits.TryGetValue(culprit, out var cv) ? cv + 1 : 1;
                    }
                }
            }
            if (chosen == null) break;
            var accepted = chosen;
            Rotate(accepted, forward: true);
            if (chosenRep == null) break;
            bestRep = chosenRep;
            applied++;
            if (accepted.Cycle.Length >= 3) cycleHits++;
            var who = string.Join("←", accepted.Cycle.Select(Name)) + "←" + Name(accepted.Cycle[0]);
            selectedLabels.Add($"{accepted.Length}日{accepted.Cycle.Length}者:{who} {accepted.Start + 1}〜{accepted.Start + accepted.Length}日({accepted.Differences}セル)");
            pass++;
        }

        var lensLabel = string.Join("/", lengths);
        var logs = new List<MirrorLog>
        {
            new MirrorLog(tag: "AdaptiveBlockSwap",
                message: $"可変長ブロック巡回交換[{lensLabel}日・最大{cycleCap}者]: total {before.Total}->{bestRep.Total} HARD {before.Hard}->{bestRep.Hard}" +
                    $" score {(long)before.WeightedScore}->{(long)bestRep.WeightedScore} 採用{applied}回(うち3者以上{cycleHits}回)" +
                    $" 巡回{generated}件(実候補{builtCandidates}件" +
                    (builtByCycleSize.Count == 0 ? "" : " 内訳 " + string.Join(" ", builtByCycleSize.Select(kv => $"{kv.Key}者:{kv.Value}"))) +
                    $")/正式評価{evaluated}件" +
                    (applied == 0 ? " [頭打ち=改善手なし]" : "") +
                    (rejectReasons.Count == 0 ? "" : " 不採用内訳: " +
                        string.Join(" ", rejectReasons.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}{kv.Value}"))) +
                    (rejectCulprits.Count == 0 ? "" : " (悪化の主因 " +
                        string.Join(" ", rejectCulprits.OrderByDescending(kv => kv.Value).Take(4).Select(kv => $"{kv.Key}:{kv.Value}")) + ")") +
                    (selectedLabels.Count > 0 ? $" 対象: {string.Join(", ", selectedLabels)}" : "")),
        };
        return new CyclicSwapResult(work, before.Total, bestRep.Total, applied, logs, PinBlocks: pinBlocks);
    }
}
