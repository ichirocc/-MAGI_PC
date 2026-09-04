using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// [汎用玉突き結合フレームワーク, 3.249.0] 単独では isBetter に不採用(拒否)された複数の候補を
/// 束ねてまとめて適用し、全体としてなら採用できないか再挑戦する汎用ヘルパ。
///
/// ユーザー実データの研磨ログ「不採用×78」「不採用×19」は、chain探索自体は候補を構築できた
/// (findCovUChain/手M/手F等が成立)がisBetterの最終総合判定(hard→weightedScore→total)に負けた
/// 個別候補の件数。多くは「その1手だけでは他族とのトレードオフで損」だが、複数の個別に損な手を
/// 同時に適用すると全体では改善する組合せが存在しうる（例: Aさんの休振替とBさんのDﾃ振替は単独
/// ではどちらもweekly悪化で負けるが、組合わせるとweekly族の増減が打ち消し合い総合改善する）。
///
/// grilling確定(2026-07-20): 対象=c1/range/c3mn/apt/fairの5族横断の汎用フレームワーク。起動方式=
/// 各パス内でリアルタイムに束ねる（独立メタパスでない）。束ね単位=上限K=3〜4件の可変長組合せ。
/// 候補プール上限=なし(ShouldStop()のみで打ち切り、時間予算のみで制御)。完了条件=5族各々に
/// 「単独では不採用だが結合で採用」の最小盤面テストを固定。
///
/// 安全性: 組合せの採否は必ずUnifiedViolationChecker+isBetter(hard→weightedScore→total辞書式、
/// 呼び出し側からinjectされる)でゲート。近似は一切せず本物の目的関数で評価するため退化不能
/// （悪化する組合せは採用されない。最悪ケースは「見つからず終わる」だけ＝既存の単独手の結果より
/// 悪化することはない）。
/// </summary>
internal static class CombinatorialRepair
{
    /// <summary>
    /// 単独では isBetter に拒否された1候補。Ops=[staff,day,newShift]の差分列（適用順・巻き戻し済み）。
    /// Hint は捕捉時点の表示名（例「桒澤美幸(Aｱ)」）。ops先頭からの逆算は対象(staff,違反シフト)と
    /// 移動先シフトが食い違いうるため、捕捉時に呼び出し側が意味の通る名前を確定させる。
    /// </summary>
    public sealed record Candidate(IReadOnlyList<int[]> Ops, string Mechanism, string Hint = "");

    /// <summary>ログ強化用の集計。呼び出し側が最終ログ文字列にそのまま連結できる。</summary>
    public sealed class Stats
    {
        public int CombosTried { get; internal set; }
        public int CombosAccepted { get; internal set; }
        public bool Truncated { get; internal set; }

        /// <summary>
        /// [停滞検知, ユーザー指示「早期脱出しないのか?」への対応] 連続maxStagnantTries回不採用のまま
        /// 進むと成立見込み薄と判断し早期break（truncated=時間切れ、こちらは無駄打ち回避で区別）。
        /// </summary>
        public bool StagnantExit { get; internal set; }

        /// <summary>
        /// [3.375.0/ユーザー指示「停滞脱出のログにイテ回数と時間を出す」] 結合探索に費やしたミリ秒。
        /// 旧: 「200通り試行→無駄打ち回避で早期終了」と回数だけで、その空振りが一瞬なのか秒単位なのかが
        /// 読めず、打ち切り閾値(maxStagnantTries)が妥当かを実機ログから判断できなかった。
        /// </summary>
        public long ElapsedMs { get; internal set; }

        public List<string> AcceptedLabels { get; } = new();

        public IReadOnlyDictionary<string, int> MechanismCounts => _mechanismCounts;

        private readonly List<string> _mechanismOrder = new();
        private readonly Dictionary<string, int> _mechanismCounts = new();

        internal void OnFeed(Candidate c)
        {
            if (_mechanismCounts.TryGetValue(c.Mechanism, out var n))
            {
                _mechanismCounts[c.Mechanism] = n + 1;
            }
            else
            {
                _mechanismCounts[c.Mechanism] = 1;
                _mechanismOrder.Add(c.Mechanism);
            }
        }

        /// <summary>「結合候補: 手B×2 tryRelocate×78 / 結合探索: 42通り試行→打ち切り / 結合成立×3(...)」形式。</summary>
        public string Summary()
        {
            if (_mechanismOrder.Count == 0) return "";
            var parts = new List<string>
            {
                "結合候補: " + string.Join(" ", _mechanismOrder.Select(m => $"{m}×{_mechanismCounts[m]}")),
            };
            var exitReason = Truncated ? "→時間切れ打ち切り" : StagnantExit ? "→無駄打ち回避で早期終了" : "";
            parts.Add($"結合探索: {CombosTried}通り試行{(ElapsedMs > 0 ? $"/{ElapsedMs}ms" : "")}{exitReason}");
            if (CombosAccepted > 0)
            {
                var labelPart = AcceptedLabels.Count > 0 ? $"({string.Join(", ", AcceptedLabels)})" : "";
                parts.Add($"結合成立×{CombosAccepted}{labelPart}");
            }
            return string.Join(" / ", parts);
        }
    }

    /// <summary>
    /// rejected プールから2〜maxK件の組合せを列挙し、まとめて適用してisBetterなら採用する。
    /// first-improvementで見つかり次第そのcomboを盤面へコミットし、使った候補をプールから除去
    /// して残りでさらに探す（1回の呼出で複数回の結合採用がありうる）。shouldStop()・全組合せ枯渇・
    /// 停滞検知（下記）のいずれかで終了。候補プールに上限は設けず、時間予算(shouldStop)のみで
    /// 打ち切る（grilling確定）。
    ///
    /// [停滞検知] 連続maxStagnantTries回（既定200）不採用のまま進むと、それ以上試しても成立見込みが
    /// 薄いと判断し早期break（E9/E10/N4等、既存の探索停滞検知と同種の無駄打ち回避）。採否は依然
    /// isBetterが決めるため退化不能＝安全に早期終了できる。カウンタは結合成立のたびにリセット
    /// （進展がある間は打ち切らない）。
    ///
    /// ops が重複するセル(staff,day)を含む組合せは互いに排他な代替案で意味を持たないため、列挙は
    /// するがフルchecker呼出はスキップする(combosTriedには計上する＝実際に検討した件数として正直)。
    /// </summary>
    public static ViolationReport CombineAndApply(
        MagiState state,
        int[][] work,
        ViolationReport bestRepIn,
        IReadOnlyList<Candidate> rejected,
        Func<ViolationReport, ViolationReport, bool> isBetter,
        int maxK = 4,
        Func<bool>? shouldStop = null,
        int maxStagnantTries = 200,
        Stats? stats = null,
        Func<Candidate, string>? label = null,
        Problem? p = null)
    {
        stats ??= new Stats();
        var shouldStopFn = shouldStop ?? (() => false);
        var labelFn = label ?? (c => c.Hint);

        foreach (var c in rejected) stats.OnFeed(c);
        var t0 = EngineClock.NowMs();   // [3.375.0] 結合探索に費やした時間（summary で出す）
        var bestRep = bestRepIn;
        var pool = rejected.ToList();
        var misses = 0;
        while (pool.Count >= 2)
        {
            if (shouldStopFn()) { stats.Truncated = true; break; }
            List<int>? acceptedIdx = null;
            ViolationReport? acceptedRep = null;
            // [3.331.0] 組合せの試行は必ず `work` を元へ戻すので、この外側ループの間ずっと同じ盤面。
            //   旧は組合せごとに Copy2D() していた（同じ内容を最大200回作り直していた）。
            var workBeforeCombo = p != null ? work.Copy2D() : Array.Empty<int[]>();
            var upperK = Math.Min(maxK, pool.Count);
            for (var k = 2; k <= upperK; k++)
            {
                var combo = new int[k];
                for (var ci = 0; ci < k; ci++) combo[ci] = ci;
                while (true)
                {
                    if (shouldStopFn()) { stats.Truncated = true; goto SearchKDone; }
                    stats.CombosTried++;
                    var ops = combo.SelectMany(idx => pool[idx].Ops).ToList();
                    if (!HasCellOverlap(ops))
                    {
                        var saved = new int[ops.Count];
                        for (var oi = 0; oi < ops.Count; oi++) saved[oi] = work[ops[oi][0]][ops[oi][1]];
                        foreach (var op in ops) work[op[0]][op[1]] = op[2];
                        // [厳密ピン保護] 束ねた候補群も複数職員の回数を同時に変えうるため、staffRange
                        //   厳密ピン(lo==hi)を新たに崩す組合せは不採用にする（keep-best/重みは不変）。
                        //
                        // [3.331.0] **安いピン検査を先に**置く。旧はフル checker を必ず呼んでから
                        //   `isBetter(...) && !exactPinRegression(...)` を評価しており、ピンを崩す組合せにも
                        //   毎回フル評価を払っていた。`&&` は両方を要求するので**採否は完全に同一**で、
                        //   ピン破りの組合せぶんだけ checker 呼び出しが減る（実データではプールの大半が
                        //   ピン破り＝AptPolish 69/71・FairPolish 20/20）。
                        var pinBad = p != null && V6SearchOperators.ExactPinRegression(p, workBeforeCombo, work);
                        var rep = pinBad ? null : UnifiedViolationChecker.Check(state, work);
                        var ok = rep != null && isBetter(rep, bestRep);
                        for (var oi = 0; oi < ops.Count; oi++) work[ops[oi][0]][ops[oi][1]] = saved[oi];
                        if (ok)
                        {
                            acceptedIdx = combo.ToList();
                            acceptedRep = rep;
                            goto SearchKDone;
                        }
                    }
                    misses++;
                    if (misses >= maxStagnantTries) { stats.StagnantExit = true; goto SearchKDone; }
                    if (!NextCombination(combo, pool.Count)) break;
                }
            }
            SearchKDone:
            if (acceptedIdx == null) break;
            misses = 0;
            var acceptedOps = acceptedIdx.SelectMany(idx => pool[idx].Ops).ToList();
            foreach (var op in acceptedOps) work[op[0]][op[1]] = op[2];
            bestRep = acceptedRep!;
            stats.CombosAccepted++;
            var lbl = string.Join("+", acceptedIdx.Select(idx => labelFn(pool[idx]))).Trim('+', ' ');
            if (!string.IsNullOrWhiteSpace(lbl)) stats.AcceptedLabels.Add(lbl);
            foreach (var idx in acceptedIdx.OrderByDescending(x => x)) pool.RemoveAt(idx);
        }
        stats.ElapsedMs += EngineClock.NowMs() - t0;
        return bestRep;
    }

    private static bool HasCellOverlap(IReadOnlyList<int[]> ops)
    {
        var seen = new HashSet<long>();
        foreach (var op in ops)
        {
            var key = op[0] * 100_000L + op[1];
            if (!seen.Add(key)) return true;
        }
        return false;
    }

    private static bool NextCombination(int[] combo, int n)
    {
        var k = combo.Length;
        var i = k - 1;
        while (i >= 0 && combo[i] == n - k + i) i--;
        if (i < 0) return false;
        combo[i]++;
        for (var j = i + 1; j < k; j++) combo[j] = combo[j - 1] + 1;
        return true;
    }
}
