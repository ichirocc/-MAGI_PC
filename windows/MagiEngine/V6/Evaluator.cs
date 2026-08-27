namespace MagiEngine.V6;

/// <summary>
/// Faithful port of Kotlin's <c>Evaluator.kt</c> — the full (non-delta) objective function used
/// by SA/ALNS candidate scoring and the final "lexicographic score" comparison. Phase 5's
/// <c>DeltaEvaluator</c> port will maintain a running total that must always agree with a fresh
/// call to <see cref="FullEvalParts"/> on the same board (validated by the parity test suite).
/// </summary>
public sealed class Evaluator
{
    /// <summary>
    /// [レビュー#1 3.213.0] 辞書式パック score = hard × SCORE_HARD_UNIT + soft の HARD 桁単位。
    /// soft がこの値以上になると hard/soft の分解・比較（split / SA の HARD ゲート / LAHC / GLS）が
    /// 壊れる。実機実測 soft は ~2e3 だが理論上限を強制しないままだったため、余裕を 1e6→1e9 へ拡大
    /// （long 上限まで hard ~9e9 の余地＝実データ規模の hard 数千に対し十分）。C++ 側（このポートの
    /// スコープ外、magi_native.cpp の SaChunk::M とリテラル 1000000000LL 群）と将来 Kotlin/C++ 版が
    /// 引き続き同期される場合はこの定数の値も揃える必要がある。
    /// </summary>
    public const long SCORE_HARD_UNIT = 1_000_000_000L;

    private readonly Problem _p;

    public Evaluator(Problem p)
    {
        _p = p;
    }

    public long FullEval(int[][] a)
    {
        var v = FullEvalParts(a);
        // [3.336.0/敵対レビュー H10] 辞書式パックは soft < SCORE_HARD_UNIT を前提にする（超えると
        // hard へ繰り上がり、SA/LAHC の HARD ゲートが静かに壊れる）。実運用（30名×31日）の実測は
        // soft ~2e3 で 1e9 に遠く及ばないが、契約として一度も検査していなかった。重みの変更（HF77）や
        // 制約の大量複製で膨らんだときに、原因不明の挙動でなく明示的な失敗にする。
        if (v[1] < 0 || v[1] >= SCORE_HARD_UNIT)
        {
            throw new InvalidOperationException(
                $"soft={v[1]} が辞書式パックの桁({SCORE_HARD_UNIT})を超えました。重みか制約数を見直してください");
        }
        return v[0] * SCORE_HARD_UNIT + v[1];
    }

    /// <summary>
    /// [監査#7] hard/soft を分離して返す（soft の SCORE_HARD_UNIT 桁溢れ＝辞書式崩壊の診断用）。
    /// <see cref="FullEval"/> はこの合成で挙動不変。Returns <c>[hard1, soft]</c>.
    /// </summary>
    public long[] FullEvalParts(int[][] a)
    {
        int S = _p.S, T = _p.T, K = _p.K;
        long hard1 = 0L;
        long soft = 0L;

        // ---- c1: every window of length day1 must contain >= day2 of shiftIdx --------------
        // [統一] (1)担当不可スタッフは対象外(canDoガード=チェッカーと一致、解消不能な幻の違反を除去)、
        // (2)#fire 計上(soft += 30×重み)。[HF77] 窓の要件(c1)の重みは 4→5→15→30 と変遷、現在値は30。
        foreach (var c in _p.Cons1)
        {
            int d1 = c.Day1, si = c.ShiftIdx, d2 = c.Day2;
            for (int i = 0; i < S; i++)
            {
                if (!_p.CanDo(i, si)) continue;
                int j = 0;
                while (j <= T - d1)
                {
                    int z = 0;
                    for (int l = 0; l < d1; l++) if (a[i][j + l] == si) z++;
                    if (z < d2) soft += 30L;
                    j++;
                }
            }
        }

        // ---- c2: per-staff total of a shift must reach count -------------------------------
        foreach (var c in _p.Cons2)
        {
            for (int i = 0; i < S; i++)
            {
                if (!_p.CanDo(i, c.ShiftIdx)) continue; // [監査#5] 担当不可の職員は対象外（チェッカーと同一条件）
                int z = 0;
                for (int j = 0; j < T; j++) if (a[i][j] == c.ShiftIdx) z++;
                if (z < c.Count) soft += 1;
            }
        }

        // ---- c41: per-day, count of (group, shift) must lie in [l, u] ----------------------
        foreach (var c in _p.Cons41)
        {
            for (int j = 0; j < T; j++)
            {
                int z = 0;
                for (int i = 0; i < S; i++) if (_p.Sgrp[i] == c.GroupIdx && a[i][j] == c.ShiftIdx) z++;
                if (z < c.L || c.U < z) soft += 1;
            }
        }

        // ---- c42: per-day, (g1,s1) co-occurring with (g2,s2) is penalized per pair ---------
        foreach (var c in _p.Cons42)
        {
            for (int j = 0; j < T; j++)
            {
                int n1 = 0, n2 = 0;
                for (int i = 0; i < S; i++)
                {
                    if (_p.Sgrp[i] == c.G1 && a[i][j] == c.S1) n1++;
                    if (_p.Sgrp[i] == c.G2 && a[i][j] == c.S2) n2++;
                }
                soft += C42PairCount(c.G1 == c.G2 && c.S1 == c.S2, n1, n2);
            }
        }

        // ---- c41s / c42s: スキルグループ版（ssk = スキル群index。既存 sgrp とは独立） -----------
        // 罰則は c41/c42 と同等(soft)。
        foreach (var c in _p.Cons41s)
        {
            for (int j = 0; j < T; j++)
            {
                int z = 0;
                for (int i = 0; i < S; i++) if (_p.Ssk[i] == c.GroupIdx && a[i][j] == c.ShiftIdx) z++;
                if (z < c.L || c.U < z) soft += 1;
            }
        }
        foreach (var c in _p.Cons42s)
        {
            for (int j = 0; j < T; j++)
            {
                int n1 = 0, n2 = 0;
                for (int i = 0; i < S; i++)
                {
                    if (_p.Ssk[i] == c.G1 && a[i][j] == c.S1) n1++;
                    if (_p.Ssk[i] == c.G2 && a[i][j] == c.S2) n2++;
                }
                soft += C42PairCount(c.G1 == c.G2 && c.S1 == c.S2, n1, n2);
            }
        }

        // ---- c3 family — [統一] UnifiedViolationChecker と同じ重み(c3=3/c3m=2/c3mn=30)を soft に適用。
        // c3n は forbidden=HARD として hard1 のまま。窓マッチは #fire 計上(後述の sub += 1)。
        // [HF77] 回避の並び(c3mn)の重みは 12→15→30 と変遷、現在値は30。
        soft += C3Check(a, _p.Cons3, forbidden: false) * 3L;
        hard1 += C3Check(a, _p.Cons3n, forbidden: true);
        soft += C3Check(a, _p.Cons3m, forbidden: false) * 2L;
        soft += C3Check(a, _p.Cons3mn, forbidden: true) * 30L;

        // ---- pref / groupViol ----------------------------------------------------------------
        // pref: wished cell not honored -> display HARD（[監査#11②] 実現可能な希望のみ計上。
        //   不可能希望は計数から対称除外）。
        // groupViol: 担当できないシフトに就いているセル。3.318.0 でチェッカーの MirrorKeys.hard
        //   （groupViol/c3n/covU/pref の4族）と揃えた。
        for (int i = 0; i < S; i++)
        {
            for (int j = 0; j < T; j++)
            {
                int w = _p.Wish[i][j];
                if (w >= 0 && _p.CanDo(i, w) && a[i][j] != w) hard1 += 1;
                int k = a[i][j];
                if (k >= 0 && k < K && !_p.CanDo(i, k)) hard1 += 1;
            }
        }

        // ---- range (low/high) + apt -----------------------------------------------------------
        // [統一a/b] range (LimMin/LimMax) は SOFT。UnifiedViolationChecker と同じ amount×重み
        // (low=90/high=45)・同じガード(lo!=0, low は canDo 必須)。
        var ssn = new int[S][];
        for (int i = 0; i < S; i++) ssn[i] = new int[K];
        // [レビュー#7 3.213.0] a[i][j] は範囲外(正規化前の -1 センチネル等)を取りうるため、範囲内の
        // 値だけを数える（C++ fullEvalParts 3.199.0 と同じ意味論への対称化）。
        for (int i = 0; i < S; i++)
        {
            for (int j = 0; j < T; j++)
            {
                int k = a[i][j];
                if (k >= 0 && k < K) ssn[i][k]++;
            }
        }
        for (int i = 0; i < S; i++)
        {
            for (int k = 0; k < K; k++)
            {
                int lo = _p.RangeLo[i][k];
                int hi = _p.RangeHi[i][k];
                int n = ssn[i][k];
                if (lo != int.MinValue && lo != 0 && n < lo && _p.CanDo(i, k)) soft += (long)(lo - n) * 90L;
                if (hi != int.MaxValue && n > hi) soft += (long)(n - hi) * 45L;
                // [統一apt] 適切回数(双方向目標) SOFT・重み1・L1偏差|n-t|。UnifiedViolationChecker の "apt" と一致。
                int t = _p.Apt[i][k];
                if (t >= 0) soft += Math.Abs(n - t);
            }
        }

        // ---- fair: within-group equalization ---------------------------------------------------
        // [統一fair] グループ内公平化 SOFT・重み1。群×担当ONシフトごと、メンバー回数の round(平均)
        // からの L1偏差和（UnifiedViolationChecker の "fair" と一致）。
        for (int g = 0; g < _p.G; g++)
        {
            var mem = _p.GroupMembers[g];
            int m = mem.Length;
            if (m < 2) continue;
            foreach (var k in _p.Bucket[g])
            {
                int sum = 0;
                foreach (var x in mem) sum += ssn[x][k];
                int tgt = (int)KotlinInterop.MathRound(sum / (double)m);
                foreach (var x in mem) soft += Math.Abs(ssn[x][k] - tgt);
            }
        }

        // ---- weekly: 7-day-cycle shift equalization ----------------------------------------------
        // [統一weekly] 7日周期のシフト平準化 SOFT・重み1。職員ごとシフトごとに、そのシフトが入る日の
        // 曜日別カウントの round(回数/7) からの L1偏差和（UnifiedViolationChecker の "weekly" と一致）。
        // [3.345.0] 休も1シフトとして数える（旧: 勤務日=非休の二値）。
        for (int i = 0; i < S; i++)
        {
            var wd = new int[K][];
            for (int k = 0; k < K; k++) wd[k] = new int[7];
            for (int j = 0; j < T; j++)
            {
                int k = a[i][j];
                if (k >= 0 && k < K) wd[k][(_p.Dow0 + j) % 7]++;
            }
            for (int k = 0; k < K; k++) soft += ScheduleUtil.WeeklyDevOfBucket(wd[k]);
        }

        // ---- covU / covO ------------------------------------------------------------------------
        // [監査#4b] 被覆は per-cell OR/AND（VBA本家=Web HF574 と三面統一）。共有ヘルパで Δ/Checker と同式。
        long covU = 0L;
        for (int j = 0; j < T; j++)
        {
            for (int k = 0; k < K; k++)
            {
                int dsn = 0;
                for (int i = 0; i < S; i++) if (a[i][j] == k) dsn++;
                covU += _p.CovUCell(k, j, dsn);
                // [HF77明示指示 2026-08-27] covO 重み 1→5。MirrorKeys.Weights["covO"] と同時に変更。
                soft += (long)_p.CovOCell(k, j, dsn) * 5L;
            }
        }
        hard1 += covU;

        return new[] { hard1, soft };
    }

    /// <summary>Returns the hard / soft split for display (運用違反 vs SOFT).</summary>
    public (long Hard, long Soft) Split(long score) => (score / SCORE_HARD_UNIT, score % SCORE_HARD_UNIT);

    private long C3Check(int[][] a, IReadOnlyList<C3> list, bool forbidden)
    {
        int S = _p.S, T = _p.T;
        long sub = 0L;
        foreach (var c in list)
        {
            var seq = c.Seq;
            int d = seq.Length;
            if (d == 0) continue;
            int first = seq[0];
            // [HF507] non-forbidden single-shift run -> run deficit (per staff whole-row)
            if (!forbidden && C3Run.IsSingleShiftSeq(seq))
            {
                for (int i = 0; i < S; i++) sub += C3Run.RowDeficit(a, i, first, d);
                continue;
            }
            for (int i = 0; i < S; i++)
            {
                int j = 0;
                while (j <= T - d)
                {
                    if (a[i][j] == first)
                    {
                        int z = 0;
                        for (int l = 1; l < d; l++) if (a[i][j + l] == seq[l]) z++;
                        bool fire = forbidden ? (z == d - 1) : (z < d - 1);
                        if (fire) sub += 1; // [統一] #fire 計上(チェッカー inc(key,1) と一致)。重みは呼び出し側で適用
                    }
                    j++;
                }
            }
        }
        return sub;
    }

    /// <summary>
    /// [3.318.0] c42/c42s の「同じ日に同時発生している禁止ペア」の数。チェッカー・評価器・Δ評価器の
    /// 共通ソース（この式を各面が独立に複製すると重みや自己ペア扱いがドリフトするため、ここへ集約する）。
    ///
    /// left = 群 g1 で s1 に就いている職員、right = 群 g2 で s2 に就いている職員。両者が互いに素なら
    /// ペア数は素直に |left|×|right| でよい。両者が同じ集合になるのは g1==g2 かつ s1==s2 のときだけで
    /// （s1!=s2 なら同じ職員が同日に両方へ就くことはなく、g1!=g2 なら sgrp が違うので両方には入らない）、
    /// そのとき素朴な積 n² は ①自分自身とのペア n 件 ②同じペアを (a,b) と (b,a) で2回、を余分に数える。
    /// 異なる2人のペア数 = C(n,2) が正しい。
    ///
    /// <see cref="ViolationChecker.CheckC3Family"/> 相当（<c>ViolationChecker.cs</c> の c42/c42s ブロック）
    /// はこの式を使わず、セルの着色（<c>mark</c> 呼び出し）が要るため実際の職員ペアを列挙する別実装を持つ
    /// （Kotlin 原本と同じ、意図した非対称）。この関数は集計値だけで足りる Evaluator/DeltaEvaluator 用。
    /// </summary>
    internal static long C42PairCount(bool sameSet, int n1, int n2) =>
        sameSet ? (long)n1 * (n1 - 1) / 2 : (long)n1 * n2;
}
