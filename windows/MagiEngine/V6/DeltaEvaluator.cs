namespace MagiEngine.V6;

/// <summary>
/// Faithful port of Kotlin's <c>DeltaEvaluator.kt</c> — incremental (delta) evaluator, the native
/// equivalent of the Web's BIT-DELTA framework.
///
/// Instead of recomputing the whole objective for every candidate (<see cref="Evaluator.FullEval"/>,
/// O(S * T * constraints)), this maintains running per-piece penalty totals plus the aggregates
/// needed to update them, and computes the score change caused by a single cell move
/// (i, j): old shift -&gt; new shift, touching only the windows/columns that can actually change.
///
/// Per-instance mutable state; NOT thread-safe (each SA worker owns one instance).
///
/// The Kotlin original's delta logic was validated against fullEval with a 20,000-move
/// randomized differential test (zero mismatches on both preview and committed score); this
/// port's parity test suite re-validates the same property against the C# <see cref="Evaluator"/>.
/// </summary>
public sealed class DeltaEvaluator
{
    private readonly Problem _p;
    private readonly int S, T, K;
    private readonly int[][] _a;

    // aggregates
    private readonly int[][] _cntSS;   // per-staff per-shift count
    private readonly int[][] _cntDay;  // per-shift per-day count
    // [統一weekly/3.345.0] per-staff × per-shift の曜日別カウント（休も1シフト＝特別扱いしない）。
    private readonly int[][][] _wdCnt;

    // running penalty pieces
    private long _sc1, _sc2, _sc41, _sc42;
    private long _sc41s, _sc42s;   // [スキルグループ] c41s/c42s（ssk ベース、soft）
    private long _sc3, _hc3n, _sc3m, _sc3mn;
    private long _hpref, _hct;
    // [3.318.0] groupViol（担当できないシフトに就いているセル）。MirrorKeys.Hard は元から4族なのに
    // 評価器側だけ3族（c3n/pref/covU）で、同じ盤面に対しチェッカーと評価器の hard が食い違っていた。
    private long _hGrpV;
    private long _sApt;    // [統一apt] 適切回数(双方向目標)の running total（SOFT, 重み1）
    private long _sFair;   // [統一fair] グループ内公平化の running total（SOFT, 重み1）
    private long _sWeekly; // [統一weekly] 曜日平準化の running total（SOFT, 重み1）
    private long _scovO;   // [統一a] 過剰被覆(covO)の running total（SOFT）
    private long _covUTot; // [監査#4b] per-cell covU の総和（セル局所Δで維持）

    // stashed deltas from the last preview (applied by Commit())
    private int _lI = -1, _lJ = -1, _lOld = -1, _lNw = -1;
    private long _dC1, _dC2, _dC41, _dC42;
    private long _dC41s, _dC42s;
    private long _dC3, _dC3n, _dC3m, _dC3mn;
    private long _dPref, _dGrpV, _dCt, _dApt, _dFair, _dWeekly, _dCovO, _nCovU;

    public DeltaEvaluator(Problem p)
    {
        _p = p;
        S = p.S; T = p.T; K = p.K;

        _cntSS = new int[S][];
        for (int i = 0; i < S; i++) _cntSS[i] = new int[K];
        _cntDay = new int[K][];
        for (int k = 0; k < K; k++) _cntDay[k] = new int[T];
        _wdCnt = new int[S][][];
        for (int i = 0; i < S; i++)
        {
            _wdCnt[i] = new int[K][];
            for (int k = 0; k < K; k++) _wdCnt[i][k] = new int[7];
        }

        _a = p.InitialAssignment();
        Rebuild();
    }

    /// <summary>
    /// Reset to a fresh assignment and recompute all aggregates / totals.
    ///
    /// [3.410.0/D-01] 旧: 行長・値域を検証せず配列コピーしていたため、S×T に満たない盤面や範囲外
    /// シフト値で添字が飛んだ。正規経路（<see cref="Problem.InitialAssignment"/> /
    /// <c>allowedShiftsForStaff</c> 由来）では起きないが、このメソッドは呼出側から直接届きうる。
    /// **丸めず落とす**: <see cref="Rebuild"/> は <c>_cntSS[i][k]++</c> を無検証で行うので
    /// センチネル -1 を入れても結局そこで飛ぶし、<c>RestIdx</c> へ黙って丸めるのは「静かに意味を変える」
    /// fail-open そのもの。この不変条件（盤面は必ず S×T かつ全セルが [0,K)）は
    /// <see cref="Problem.InitialAssignment"/> が入口で保証しており、破れているなら呼出側のバグ＝
    /// その場で名指しするのが正しい。
    /// </summary>
    public void Reset(int[][] init)
    {
        if (init.Length < S) throw new ArgumentException($"reset: rows {init.Length} < S={S}");
        for (int i = 0; i < S; i++)
        {
            var row = init[i];
            if (row.Length < T) throw new ArgumentException($"reset: row {i} has {row.Length} cells < T={T}");
            for (int j = 0; j < T; j++)
            {
                int k = row[j];
                if (k < 0 || k >= K) throw new ArgumentException($"reset: cell({i},{j})={k} out of [0,{K})");
                _a[i][j] = k;
            }
        }
        Rebuild();
    }

    public int[][] Snapshot()
    {
        var result = new int[S][];
        for (int i = 0; i < S; i++) result[i] = (int[])_a[i].Clone();
        return result;
    }

    /// <summary>Copy the current assignment into a caller-owned buffer (no allocation; hot-loop best capture).</summary>
    public void SnapshotInto(int[][] dst)
    {
        for (int i = 0; i < S; i++) Array.Copy(_a[i], dst[i], T);
    }

    /// <summary>Current shift assigned at (i,j).</summary>
    public int At(int i, int j) => _a[i][j];

    /// <summary>Running per-staff count of shift k (O(1) lookup; backs the findXxx targeted fixers).</summary>
    public int CountForStaff(int i, int k) => _cntSS[i][k];

    /// <summary>Running per-day count of shift k on day j (O(1) lookup; backs findCovOFix).</summary>
    public int CountOnDay(int k, int j) => _cntDay[k][j];

    /// <summary>
    /// [3.371.0/soft全族の完全差分] running per-family の生カウント（<see cref="MirrorKeys.All"/> の
    /// 各キーと一対一・checker の <c>Breakdown[key]</c> と同一単位）を検証専用に公開する。
    ///
    /// <see cref="Score"/>(soft集約) は各フィールドへ重みを乗じて合算するため、総和が一致しても族ごとの
    /// 誤差が相殺されて隠れる余地がある（例: c1(重み30)とc3mn(重み30)が同じ重みを持つため、片方+1・
    /// もう片方-1 の誤りは総和では検出できない。c2/c41/c42/c41s/c42s/apt/fair/weekly も全て重み1で
    /// 同じ穴を持つ）。このマップは checker の breakdown と1キーずつ突き合わせる per-family パリティ
    /// 検証のために存在する。low/high だけは <see cref="RangeRaw"/> を参照。
    /// </summary>
    internal IReadOnlyDictionary<string, long> FamilyRaw() => new Dictionary<string, long>
    {
        ["c1"] = _sc1, ["c2"] = _sc2, ["c41"] = _sc41, ["c42"] = _sc42, ["c41s"] = _sc41s, ["c42s"] = _sc42s,
        ["c3"] = _sc3, ["c3n"] = _hc3n, ["c3m"] = _sc3m, ["c3mn"] = _sc3mn,
        ["pref"] = _hpref, ["groupViol"] = _hGrpV, ["apt"] = _sApt, ["fair"] = _sFair, ["weekly"] = _sWeekly,
        ["covO"] = _scovO, ["covU"] = _covUTot,
    };

    /// <summary>
    /// [3.371.0/soft全族の完全差分] <c>_hct</c> は low(重み90)/high(重み45) を**その場で重み適用済み**の
    /// 1つの running total へ合算している（<see cref="RangeViol"/> 参照）。checker の
    /// <c>Breakdown["low"]</c>/<c>Breakdown["high"]</c> は生カウント(UNweighted)なので、単体では直接
    /// 比較できない。検証は <c>Breakdown["low"]*90 + Breakdown["high"]*45 == RangeWeighted()</c> の
    /// 形で行う。
    /// </summary>
    internal long RangeWeighted() => _hct;

    /// <summary>
    /// [3.372.0/レビュー修正] low/high を**別々の生 amount** で返す（検証専用・O(S×K) のフル再計算）。
    /// <see cref="RangeWeighted"/> だけだと「90a+45b」が単射でない（low=1,high=0 と low=0,high=2 が
    /// どちらも90）ため、「low を1件見落として high を2件過剰に数える」型の取り違えを検出できない。
    ///
    /// ホットパスの <see cref="RangeViol"/> は重み適用済みの1本(<c>_hct</c>)で持つ設計（性能上の
    /// 意図的な選択）なので、ここは同じ述語を書き下したフル再計算にしてある。両者のドリフトは、
    /// テストが <c>RangeRaw().Low*90 + RangeRaw().High*45 == RangeWeighted()</c> を毎手つき合わせる
    /// ことで検出する（左辺=フル再計算・右辺=差分維持なので、この等式は同時に増分整合性の検査にもなる）。
    /// </summary>
    internal (long Low, long High) RangeRaw()
    {
        long lowAmt = 0, highAmt = 0;
        for (int i = 0; i < S; i++)
        {
            for (int k = 0; k < K; k++)
            {
                int n = _cntSS[i][k];
                int lo = _p.RangeLo[i][k], hi = _p.RangeHi[i][k];
                if (lo != int.MinValue && lo != 0 && n < lo && _p.CanDo(i, k)) lowAmt += lo - n;
                if (hi != int.MaxValue && n > hi) highAmt += n - hi;
            }
        }
        return (lowAmt, highAmt);
    }

    /// <summary>Fused previewMove + commit for a single cell. Returns the new total score.</summary>
    public long Apply(int i, int j, int nw)
    {
        PreviewMove(i, j, nw);
        Commit(i, j, nw);
        return Score();
    }

    public long Score() => ScoreFrom(_covUTot);

    private long ScoreFrom(long cu)
    {
        long h1 = _hc3n + cu + _hpref + _hGrpV;
        // [統一a/b] range(_hct, 重み付き) と covO(_scovO) を SOFT に含める。
        // [統一c] c3/c3m/c3mn に checker 重み(3/2/30)を適用（_sc3等は #fire/run-deficit の生カウント）。
        // [統一c1] c1 にも checker 重み(30)を適用（_sc1 は #fire 生カウント、canDoガード済）。
        // [統一apt/fair/weekly] _sApt(適切回数) _sFair(群内公平化) _sWeekly(曜日平準化) を SOFT に含める
        // （共に重み1）。[HF77明示数値指示] c1: 4→5→15→30。c3mn: 12→15→30。covO: 1→5(2026-08-27)。
        long soft = _sc1 * 30 + _sc2 + _sc41 + _sc42 + _sc41s + _sc42s + _sc3 * 3 + _sc3m * 2 + _sc3mn * 30
                    + _hct + _sApt + _sFair + _sWeekly + _scovO * 5;
        return h1 * Evaluator.SCORE_HARD_UNIT + soft;
    }

    /// <summary>Preview the score after moving (i,j) -&gt; nw, stashing deltas for <see cref="Commit"/>. No mutation of totals.</summary>
    private long PreviewMove(int i, int j, int nw)
    {
        // [3.410.0/D-02] 旧: nw を無検証で cntDay[nw][j] 等の添字に使っており、範囲外で即座に
        // 添字例外になった。正規の探索オペレータは allowedShiftsForStaff から選ぶので到達しないが、
        // Reset は過去の a[i][j] を戻すため、盤面の不変条件（Reset の検証）と対で保っておく必要がある。
        if (nw < 0 || nw >= K) throw new ArgumentException($"previewMove: nw={nw} out of [0,{K})");
        int old = _a[i][j];
        _lI = i; _lJ = j; _lOld = old; _lNw = nw;
        if (nw == old)
        {
            _dC1 = 0; _dC2 = 0; _dC41 = 0; _dC42 = 0; _dC41s = 0; _dC42s = 0; _dC3 = 0; _dC3n = 0; _dC3m = 0; _dC3mn = 0;
            _dPref = 0; _dGrpV = 0; _dCt = 0; _dApt = 0; _dFair = 0; _dWeekly = 0; _dCovO = 0; _nCovU = _covUTot;
            return Score();
        }

        // windowed families (c1, c3 family): before/after via temporary swap
        long bC1 = C1Local(i, j);
        long bC3 = C3Local(i, j, _p.Cons3, false);
        long bC3n = C3Local(i, j, _p.Cons3n, true);
        long bC3m = C3Local(i, j, _p.Cons3m, false);
        long bC3mn = C3Local(i, j, _p.Cons3mn, true);
        // [外部レビュー検証/例外安全性] a[i][j] を一時的に nw へ書き換えて after 側を測るこの窓は、
        // C1Local/C3Local が現行の不変条件下では例外を投げない（境界は min/max で常にクランプ済み）ため
        // 実害は未確認だが、将来の変更で throw する経路が増えても「復元されないまま盤面が壊れる」という
        // 最悪の失敗モード（クラッシュではなく以降の全差分計算が静かに狂う）を構造的に防ぐため
        // try/finally で復元を保証する。
        _a[i][j] = nw;
        long aC1 = 0, aC3 = 0, aC3n = 0, aC3m = 0, aC3mn = 0;
        try
        {
            aC1 = C1Local(i, j);
            aC3 = C3Local(i, j, _p.Cons3, false);
            aC3n = C3Local(i, j, _p.Cons3n, true);
            aC3m = C3Local(i, j, _p.Cons3m, false);
            aC3mn = C3Local(i, j, _p.Cons3mn, true);
        }
        finally
        {
            _a[i][j] = old;
        }
        _dC1 = aC1 - bC1; _dC3 = aC3 - bC3; _dC3n = aC3n - bC3n; _dC3m = aC3m - bC3m; _dC3mn = aC3mn - bC3mn;

        // c2 (per-staff total) for shifts old / nw
        long d2 = 0;
        // [監査#5] 担当不可は対象外（全量/集計/差分の3箇所で同一条件に統一し再乖離を防ぐ）。
        // in-bucket不変量下では old/nw は常に担当可のため実質no-op（差分恒等性は保たれる）。
        foreach (var c in _p.Cons2)
        {
            if (!_p.CanDo(i, c.ShiftIdx)) continue;
            if (c.ShiftIdx == old)
                d2 += Viol01(_cntSS[i][old] - 1 < c.Count) - Viol01(_cntSS[i][old] < c.Count);
            else if (c.ShiftIdx == nw)
                d2 += Viol01(_cntSS[i][nw] + 1 < c.Count) - Viol01(_cntSS[i][nw] < c.Count);
        }
        _dC2 = d2;

        // ct (LimMin/LimMax) for shifts old / nw
        _dCt = (RangeViol(i, old, _cntSS[i][old] - 1) - RangeViol(i, old, _cntSS[i][old]))
             + (RangeViol(i, nw, _cntSS[i][nw] + 1) - RangeViol(i, nw, _cntSS[i][nw]));

        // [統一apt] 適切回数(双方向目標) for shifts old / nw — staff i の old/nw 列のみ変化（range と同形）。
        _dApt = (AptViol(i, old, _cntSS[i][old] - 1) - AptViol(i, old, _cntSS[i][old]))
              + (AptViol(i, nw, _cntSS[i][nw] + 1) - AptViol(i, nw, _cntSS[i][nw]));

        // [統一fair] グループ内公平化 — staff i の群 gI の old/nw 列のみ偏差が動く（本人 ±1 で平均も動くので群内再計算）。
        int gI = _p.Sgrp[i];
        _dFair = 0L;
        if (_p.CanDo(i, old)) _dFair += FairDevAt(gI, old, i, -1) - FairDevAt(gI, old, -1, 0);
        if (_p.CanDo(i, nw)) _dFair += FairDevAt(gI, nw, i, +1) - FairDevAt(gI, nw, -1, 0);

        // [統一weekly/3.345.0] 曜日平準化 — シフト別なので old と nw の2バケットだけが動く（old==nw は不変）。
        // 範囲外セントネル(-1等)はそのバケットを持たないので、範囲ガードで片側だけ動かす。
        // [3.445.0続/L-04経由で発見] ここも C1Local/C3Local の a[i][j] 窓と同型の一時書換え→測定→復元
        // パターンで、復元前に例外が飛ぶと _wdCnt が恒久的にずれる。WeeklyDevOfBucket は現行不変条件下で
        // 例外を投げない純関数（配列を添字なしで走査するのみ・0除算は 7.0 固定なので発生しない）ため
        // 実害は未確認だが、PreviewMove 冒頭の a[i][j] と同じ理由で try/finally を掛け、将来の変更でも
        // 構造的に安全にする。
        _dWeekly = 0L;
        int wdIdx = (_p.Dow0 + j) % 7;
        if (old != nw)
        {
            long accW = 0L;
            if (old >= 0 && old < K)
            {
                var b = _wdCnt[i][old];
                long before = ScheduleUtil.WeeklyDevOfBucket(b);
                b[wdIdx]--;
                try { accW += ScheduleUtil.WeeklyDevOfBucket(b) - before; }
                finally { b[wdIdx]++; }
            }
            if (nw >= 0 && nw < K)
            {
                var b = _wdCnt[i][nw];
                long before = ScheduleUtil.WeeklyDevOfBucket(b);
                b[wdIdx]++;
                try { accW += ScheduleUtil.WeeklyDevOfBucket(b) - before; }
                finally { b[wdIdx]--; }
            }
            _dWeekly = accW;
        }

        // pref (this cell only)
        int w = _p.Wish[i][j];
        // [監査#11②] 実現可能な希望のみ pref 計上（不可能希望は全量/集計/差分で対称除外）。
        bool wOk = w >= 0 && _p.CanDo(i, w);
        _dPref = (wOk && nw != w ? 1L : 0L) - (wOk && old != w ? 1L : 0L);

        // [3.318.0] groupViol（担当できないシフト）はセル単位なので差分もこの1セルだけ。
        // 範囲外セントネル(-1)は canDo の対象外＝0 として扱う（Evaluator/checker と同じ範囲ガード）。
        _dGrpV = ((nw >= 0 && nw < K && !_p.CanDo(i, nw)) ? 1L : 0L)
               - ((old >= 0 && old < K && !_p.CanDo(i, old)) ? 1L : 0L);

        // c41 (group/day range) on day j — only constraints touching this staff's group & shifts
        int gi = _p.Sgrp[i];
        long d41 = 0;
        foreach (var c in _p.Cons41)
        {
            if (c.GroupIdx != gi || (c.ShiftIdx != old && c.ShiftIdx != nw)) continue;
            int z = 0;
            for (int ii = 0; ii < S; ii++) if (_p.Sgrp[ii] == c.GroupIdx && _a[ii][j] == c.ShiftIdx) z++;
            int za = z + (c.ShiftIdx == nw ? 1 : 0) - (c.ShiftIdx == old ? 1 : 0);
            d41 += Viol01(za < c.L || c.U < za) - Viol01(z < c.L || c.U < z);
        }
        _dC41 = d41;

        // c42 (group pair) on day j — skip constraints the move cannot change (mirrors c41's guard).
        long d42 = 0;
        foreach (var c in _p.Cons42)
        {
            bool touch1 = c.G1 == gi && (c.S1 == old || c.S1 == nw);
            bool touch2 = c.G2 == gi && (c.S2 == old || c.S2 == nw);
            if (!touch1 && !touch2) continue; // n1a==n1 && n2a==n2 -> delta 0
            int n1 = 0, n2 = 0;
            for (int ii = 0; ii < S; ii++)
            {
                if (_p.Sgrp[ii] == c.G1 && _a[ii][j] == c.S1) n1++;
                if (_p.Sgrp[ii] == c.G2 && _a[ii][j] == c.S2) n2++;
            }
            int n1a = n1 + (c.G1 == gi && c.S1 == nw ? 1 : 0) - (c.G1 == gi && c.S1 == old ? 1 : 0);
            int n2a = n2 + (c.G2 == gi && c.S2 == nw ? 1 : 0) - (c.G2 == gi && c.S2 == old ? 1 : 0);
            bool same = c.G1 == c.G2 && c.S1 == c.S2;
            d42 += Evaluator.C42PairCount(same, n1a, n2a) - Evaluator.C42PairCount(same, n1, n2);
        }
        _dC42 = d42;

        // c41s (skill group/day range) on day j — same shape as c41 but indexed by ssk
        int gis = _p.Ssk[i];
        long d41s = 0;
        foreach (var c in _p.Cons41s)
        {
            if (c.GroupIdx != gis || (c.ShiftIdx != old && c.ShiftIdx != nw)) continue;
            int z = 0;
            for (int ii = 0; ii < S; ii++) if (_p.Ssk[ii] == c.GroupIdx && _a[ii][j] == c.ShiftIdx) z++;
            int za = z + (c.ShiftIdx == nw ? 1 : 0) - (c.ShiftIdx == old ? 1 : 0);
            d41s += Viol01(za < c.L || c.U < za) - Viol01(z < c.L || c.U < z);
        }
        _dC41s = d41s;

        // c42s (skill group pair) on day j — same shape as c42 but indexed by ssk
        long d42s = 0;
        foreach (var c in _p.Cons42s)
        {
            bool touch1 = c.G1 == gis && (c.S1 == old || c.S1 == nw);
            bool touch2 = c.G2 == gis && (c.S2 == old || c.S2 == nw);
            if (!touch1 && !touch2) continue;
            int n1 = 0, n2 = 0;
            for (int ii = 0; ii < S; ii++)
            {
                if (_p.Ssk[ii] == c.G1 && _a[ii][j] == c.S1) n1++;
                if (_p.Ssk[ii] == c.G2 && _a[ii][j] == c.S2) n2++;
            }
            int n1a = n1 + (c.G1 == gis && c.S1 == nw ? 1 : 0) - (c.G1 == gis && c.S1 == old ? 1 : 0);
            int n2a = n2 + (c.G2 == gis && c.S2 == nw ? 1 : 0) - (c.G2 == gis && c.S2 == old ? 1 : 0);
            bool same = c.G1 == c.G2 && c.S1 == c.S2;
            d42s += Evaluator.C42PairCount(same, n1a, n2a) - Evaluator.C42PairCount(same, n1, n2);
        }
        _dC42s = d42s;

        // [監査#4b] covU/covO 差分はセル局所（影響は (old,j)/(nw,j) の2セルのみ）。共有ヘルパで全量と同式。
        int co = _cntDay[old][j];
        int cn = _cntDay[nw][j];
        long dCovU = (_p.CovUCell(old, j, co - 1) - _p.CovUCell(old, j, co))
                   + (_p.CovUCell(nw, j, cn + 1) - _p.CovUCell(nw, j, cn));
        _nCovU = _covUTot + dCovU;
        _dCovO = (_p.CovOCell(old, j, co - 1) - _p.CovOCell(old, j, co))
               + (_p.CovOCell(nw, j, cn + 1) - _p.CovOCell(nw, j, cn));

        // [統一b] dCt(range) は SOFT へ移動（hard から除外）。
        long dHard = _dC3n + (_nCovU - _covUTot) + _dPref + _dGrpV;
        // [統一c] c3/c3m/c3mn の delta にも checker 重み(3/2/30)を適用（full soft と同一係数）。
        // [統一c1] c1 の delta にも ×30。
        long dSoft = _dC1 * 30 + _dC2 + _dC41 + _dC42 + _dC41s + _dC42s + _dC3 * 3 + _dC3m * 2 + _dC3mn * 30
                   + _dCt + _dApt + _dFair + _dWeekly + _dCovO;
        return Score() + dHard * Evaluator.SCORE_HARD_UNIT + dSoft;
    }

    /// <summary>
    /// Apply the stashed move from the last <see cref="PreviewMove"/>(i,j,nw). Internal —
    /// external callers use <see cref="Apply"/>.
    ///
    /// [C#移植上の判断] Kotlin の <c>require(...)</c> は常に <c>IllegalArgumentException</c> を
    /// 投げるが、この2つの検査はどちらも「(i,j,nw) という引数自体」の妥当性ではなく「直前の
    /// PreviewMove 呼出との整合」というプロトコル/内部状態の不変条件を確認している。C# の慣例に
    /// 合わせ <see cref="InvalidOperationException"/> とする（条件・メッセージは Kotlin と同一。
    /// この例外は型でキャッチされることを想定しないプログラミングエラー検出用のため、型の違いに
    /// よる挙動差は無い）。
    /// </summary>
    private void Commit(int i, int j, int nw)
    {
        if (!(_lI >= 0 && i == _lI && j == _lJ && nw == _lNw))
            throw new InvalidOperationException("commit must match last preview");
        if (_a[i][j] != _lOld)
            throw new InvalidOperationException("state changed after preview");
        int old = _lOld;
        try
        {
            if (nw == old) return;
            _a[i][j] = nw;
            _cntSS[i][old]--; _cntSS[i][nw]++;
            _cntDay[old][j]--; _cntDay[nw][j]++;
            // [統一weekly/3.345.0] シフト別バケットを更新（PreviewMove の _dWeekly と同ステップ）。
            // 範囲ガードは preview と対称（範囲外セントネルでも _wdCnt が乖離しない）。
            if (old != nw)
            {
                int wIdx = (_p.Dow0 + j) % 7;
                if (old >= 0 && old < K) _wdCnt[i][old][wIdx]--;
                if (nw >= 0 && nw < K) _wdCnt[i][nw][wIdx]++;
            }
            _sc1 += _dC1; _sc2 += _dC2; _sc41 += _dC41; _sc42 += _dC42; _sc41s += _dC41s; _sc42s += _dC42s;
            _sc3 += _dC3; _hc3n += _dC3n; _sc3m += _dC3m; _sc3mn += _dC3mn;
            _hpref += _dPref; _hGrpV += _dGrpV; _hct += _dCt; _sApt += _dApt; _sFair += _dFair; _sWeekly += _dWeekly; _scovO += _dCovO;
            _covUTot = _nCovU;
        }
        finally
        {
            // invalidate the stash so a stray double-commit cannot corrupt aggregates
            _lI = -1; _lJ = -1; _lOld = -1; _lNw = -1;
        }
    }

    // ---- aggregate / total rebuild --------------------------------------------

    private void Rebuild()
    {
        for (int i = 0; i < S; i++) Array.Clear(_cntSS[i]);
        for (int k = 0; k < K; k++) Array.Clear(_cntDay[k]);
        for (int i = 0; i < S; i++) for (int k = 0; k < K; k++) Array.Clear(_wdCnt[i][k]);
        for (int i = 0; i < S; i++)
        {
            for (int j = 0; j < T; j++)
            {
                int k = _a[i][j];
                _cntSS[i][k]++; _cntDay[k][j]++;
                if (k >= 0 && k < K) _wdCnt[i][k][(_p.Dow0 + j) % 7]++;
            }
        }

        _sc1 = C1All(); _sc2 = C2All(); _sc41 = C41All(); _sc42 = C42All(); _sc41s = C41sAll(); _sc42s = C42sAll();
        _sc3 = C3All(_p.Cons3, false); _hc3n = C3All(_p.Cons3n, true);
        _sc3m = C3All(_p.Cons3m, false); _sc3mn = C3All(_p.Cons3mn, true);
        _hpref = PrefAll(); _hGrpV = GroupViolAll(); _hct = CtAll(); _sApt = AptAll(); _sFair = FairAll(); _sWeekly = WeeklyAll(); _scovO = CovOAll();
        _covUTot = CovUAll();
    }

    // ---- helpers ---------------------------------------------------------------

    private static long Viol01(bool b) => b ? 1L : 0L;

    private long RangeViol(int i, int k, int n)
    {
        // [統一b] UnifiedViolationChecker と同分類(SOFT)・同重み: low(lo!=0, canDo必須)=amount×90 / high=amount×45。
        int lo = _p.RangeLo[i][k], hi = _p.RangeHi[i][k];
        long v = 0L;
        if (lo != int.MinValue && lo != 0 && n < lo && _p.CanDo(i, k)) v += (long)(lo - n) * 90L;
        if (hi != int.MaxValue && n > hi) v += (long)(n - hi) * 45L;
        return v;
    }

    /// <summary>[統一a] 1セル(shift k, 日 j)の過剰被覆量。上限 hi=(use2&amp;&amp;need2>=0?need2:need1)。checker covO と同一。</summary>
    private long CovOAll()
    {
        long s = 0L;
        for (int k = 0; k < K; k++) for (int j = 0; j < T; j++) s += _p.CovOCell(k, j, _cntDay[k][j]);
        return s;
    }

    private long C1Local(int i, int j)
    {
        long tot = 0L;
        foreach (var c in _p.Cons1)
        {
            if (!_p.CanDo(i, c.ShiftIdx)) continue; // [統一] 担当不可は対象外(チェッカーと一致)
            int js0 = Math.Max(0, j - c.Day1 + 1), js1 = Math.Min(T - c.Day1, j);
            int js = js0;
            while (js <= js1)
            {
                int z = 0;
                for (int l = 0; l < c.Day1; l++) if (_a[i][js + l] == c.ShiftIdx) z++;
                if (z < c.Day2) tot += 1; // [統一] #fire 計上。重みは soft 集約で×30
                js++;
            }
        }
        return tot;
    }

    private long C3Local(int i, int j, IReadOnlyList<C3> list, bool fbd)
    {
        long sub = 0L;
        foreach (var c in list)
        {
            var seq = c.Seq;
            int d = seq.Length;
            if (d == 0) continue;
            // [HF507] single-shift run: deficit is per-staff whole-row, not windowed.
            // A move at (i,j) only affects staff i's row, so recompute row i's run deficit
            // (before/after via the caller's swap captures the delta correctly).
            if (!fbd && C3Run.IsSingleShiftSeq(seq))
            {
                sub += C3Run.RowDeficit(_a, i, seq[0], d);
                continue;
            }
            int js0 = Math.Max(0, j - d + 1), js1 = Math.Min(T - d, j);
            int js = js0;
            while (js <= js1)
            {
                if (_a[i][js] == seq[0])
                {
                    int z = 0;
                    for (int l = 1; l < d; l++) if (_a[i][js + l] == seq[l]) z++;
                    bool fire = fbd ? (z == d - 1) : (z < d - 1);
                    if (fire) sub += 1; // [統一] #fire 計上。重みは soft 集約で適用
                }
                js++;
            }
        }
        return sub;
    }

    private long C1All()
    {
        long tot = 0L;
        foreach (var c in _p.Cons1)
        {
            for (int i = 0; i < S; i++)
            {
                if (!_p.CanDo(i, c.ShiftIdx)) continue; // [統一] 担当不可は対象外(チェッカーと一致)
                int js = 0;
                while (js <= T - c.Day1)
                {
                    int z = 0;
                    for (int l = 0; l < c.Day1; l++) if (_a[i][js + l] == c.ShiftIdx) z++;
                    if (z < c.Day2) tot += 1; // [統一] #fire 計上。重みは soft 集約で×30
                    js++;
                }
            }
        }
        return tot;
    }

    private long C2All()
    {
        long tot = 0L;
        // [監査#5] 担当不可の職員は対象外（チェッカーと同一条件）
        foreach (var c in _p.Cons2)
            for (int i = 0; i < S; i++)
                if (_p.CanDo(i, c.ShiftIdx) && _cntSS[i][c.ShiftIdx] < c.Count) tot += 1;
        return tot;
    }

    private long C41All()
    {
        long tot = 0L;
        foreach (var c in _p.Cons41)
        {
            for (int j = 0; j < T; j++)
            {
                int z = 0;
                for (int i = 0; i < S; i++) if (_p.Sgrp[i] == c.GroupIdx && _a[i][j] == c.ShiftIdx) z++;
                if (z < c.L || c.U < z) tot += 1;
            }
        }
        return tot;
    }

    private long C42All()
    {
        long tot = 0L;
        foreach (var c in _p.Cons42)
        {
            for (int j = 0; j < T; j++)
            {
                int n1 = 0, n2 = 0;
                for (int i = 0; i < S; i++)
                {
                    if (_p.Sgrp[i] == c.G1 && _a[i][j] == c.S1) n1++;
                    if (_p.Sgrp[i] == c.G2 && _a[i][j] == c.S2) n2++;
                }
                tot += Evaluator.C42PairCount(c.G1 == c.G2 && c.S1 == c.S2, n1, n2);
            }
        }
        return tot;
    }

    private long C41sAll()
    {
        long tot = 0L;
        foreach (var c in _p.Cons41s)
        {
            for (int j = 0; j < T; j++)
            {
                int z = 0;
                for (int i = 0; i < S; i++) if (_p.Ssk[i] == c.GroupIdx && _a[i][j] == c.ShiftIdx) z++;
                if (z < c.L || c.U < z) tot += 1;
            }
        }
        return tot;
    }

    private long C42sAll()
    {
        long tot = 0L;
        foreach (var c in _p.Cons42s)
        {
            for (int j = 0; j < T; j++)
            {
                int n1 = 0, n2 = 0;
                for (int i = 0; i < S; i++)
                {
                    if (_p.Ssk[i] == c.G1 && _a[i][j] == c.S1) n1++;
                    if (_p.Ssk[i] == c.G2 && _a[i][j] == c.S2) n2++;
                }
                tot += Evaluator.C42PairCount(c.G1 == c.G2 && c.S1 == c.S2, n1, n2);
            }
        }
        return tot;
    }

    private long C3All(IReadOnlyList<C3> list, bool fbd)
    {
        long sub = 0L;
        foreach (var c in list)
        {
            var seq = c.Seq;
            int d = seq.Length;
            if (d == 0) continue;
            // [HF507] non-forbidden single-shift run -> run deficit (per staff whole-row)
            if (!fbd && C3Run.IsSingleShiftSeq(seq))
            {
                for (int i = 0; i < S; i++) sub += C3Run.RowDeficit(_a, i, seq[0], d);
                continue;
            }
            for (int i = 0; i < S; i++)
            {
                int j = 0;
                while (j <= T - d)
                {
                    if (_a[i][j] == seq[0])
                    {
                        int z = 0;
                        for (int l = 1; l < d; l++) if (_a[i][j + l] == seq[l]) z++;
                        bool fire = fbd ? (z == d - 1) : (z < d - 1);
                        if (fire) sub += 1; // [統一] #fire 計上。重みは soft 集約で適用
                    }
                    j++;
                }
            }
        }
        return sub;
    }

    private long PrefAll()
    {
        long h = 0L;
        for (int i = 0; i < S; i++)
        {
            for (int j = 0; j < T; j++)
            {
                int w = _p.Wish[i][j];
                if (w >= 0 && _p.CanDo(i, w) && _a[i][j] != w) h++;
            }
        }
        return h;
    }

    /// <summary>[3.318.0] 担当できないシフトに就いているセル数（checker の "groupViol" と同一条件）。</summary>
    private long GroupViolAll()
    {
        long h = 0L;
        for (int i = 0; i < S; i++)
        {
            for (int j = 0; j < T; j++)
            {
                int k = _a[i][j];
                if (k >= 0 && k < K && !_p.CanDo(i, k)) h++;
            }
        }
        return h;
    }

    private long CtAll()
    {
        long h = 0L;
        for (int i = 0; i < S; i++) for (int k = 0; k < K; k++) h += RangeViol(i, k, _cntSS[i][k]);
        return h;
    }

    /// <summary>[統一apt] 1セル(staff i, shift k)の適切回数偏差。重み1の L1 |n-t|。UnifiedViolationChecker の "apt" と一致。</summary>
    private long AptViol(int i, int k, int n)
    {
        int t = _p.Apt[i][k];
        return t >= 0 ? Math.Abs(n - t) : 0L;
    }

    private long AptAll()
    {
        long h = 0L;
        for (int i = 0; i < S; i++) for (int k = 0; k < K; k++) h += AptViol(i, k, _cntSS[i][k]);
        return h;
    }

    /// <summary>
    /// [統一fair] 群g・シフトk の公平化偏差。staff <paramref name="special"/> のカウントに
    /// <paramref name="delta"/> を加味（preview用）。round(平均) からのメンバー L1 偏差和。
    /// UnifiedViolationChecker の "fair" と一致.
    /// </summary>
    private long FairDevAt(int g, int k, int special, int delta)
    {
        var mem = _p.GroupMembers[g];
        int m = mem.Length;
        if (m < 2) return 0L;
        int sum = 0;
        foreach (var x in mem) sum += _cntSS[x][k] + (x == special ? delta : 0);
        // [Kotlin Math.round(Double):Long の忠実移植] fair の目標は「群人数 m」で割った平均の丸めであり、
        // c1/weekly の 7 割り（.5 が構造的に出ない）とは違い m によっては厳密に .5 の中間値へ到達しうる
        // ため、丸めモードが結果に影響しうる唯一の箇所。KotlinInterop.MathRound で Java の
        // "四捨五入（.5は+∞側）" 契約を再現する（C# の既定 Math.Round は银行丸めで異なる）。
        int tgt = (int)KotlinInterop.MathRound(sum / (double)m);
        long d = 0L;
        foreach (var x in mem)
        {
            int c = _cntSS[x][k] + (x == special ? delta : 0);
            d += Math.Abs(c - tgt);
        }
        return d;
    }

    private long FairAll()
    {
        long h = 0L;
        for (int g = 0; g < _p.G; g++)
            foreach (var k in _p.Bucket[g]) h += FairDevAt(g, k, -1, 0);
        return h;
    }

    /// <summary>[統一weekly/3.345.0] 全職員×全シフトの曜日平準化偏差の総和。_wdCnt を単一ソースとし checker/Evaluator と同一。</summary>
    private long WeeklyAll()
    {
        long h = 0L;
        for (int i = 0; i < S; i++) for (int k = 0; k < K; k++) h += ScheduleUtil.WeeklyDevOfBucket(_wdCnt[i][k]);
        return h;
    }

    private long CovUAll()
    {
        long t = 0L;
        for (int j = 0; j < T; j++) for (int k = 0; k < K; k++) t += _p.CovUCell(k, j, _cntDay[k][j]);
        return t;
    }
}
