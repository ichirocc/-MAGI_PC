namespace MagiEngine.V6;

/// <summary>
/// Faithful port of Kotlin's <c>AcceptMode</c> enum (declared in <c>V6NativeOptimizer.kt</c>, used
/// by <see cref="V6SearchOperators.GlsAccept"/>; the rest of <c>V6NativeOptimizer.kt</c> itself is
/// ported in a later sub-phase). SA / Great-Deluge / Lam-adaptive acceptance rule selection for the
/// ALNS direct-eval arm.
/// </summary>
public enum AcceptMode { Sa, GreatDeluge, LamAdaptive }

/// <summary>
/// Faithful port of Kotlin's <c>V6SearchOperators.kt</c> — pure helper functions extracted from
/// <c>V6NativeOptimizer</c> (no dependency on that object's mutable state; only
/// <see cref="Problem"/>/<see cref="DeltaEvaluator"/>/<see cref="GlsPenalty"/>/<see cref="JavaRandom"/>
/// arguments). Targeted single-cell fixers (Find*Fix), raw-score acceptance, diff utilities, and the
/// covU chain-fill BFS. All Kotlin declarations in this file are top-level <c>internal fun</c>s (no
/// containing object/class) — C# has no equivalent of Kotlin's package-level free functions, so they
/// are gathered here as <c>internal static</c> members of a single wrapper class (same convention
/// used for <see cref="PolishGate"/>/<see cref="C1DeltaPrefilter"/>/<see cref="C3nBitScan"/>).
/// </summary>
internal static class V6SearchOperators
{
    /// <summary>
    /// 複数の遅延候補列を先頭から 1 件ずつ巡回する決定的ラウンドロビン（先頭の候補族だけが評価予算を使い切るのを防ぐ）。
    /// yield で中断している間も cursor をキューへ戻しておく＝呼出側が途中で列挙を止めても finally で全 enumerator を破棄できる。
    /// </summary>
    internal static IEnumerable<T> RoundRobin<T>(params IEnumerable<T>[] sources)
    {
        var cursors = new Queue<IEnumerator<T>>(sources.Select(x => x.GetEnumerator()));
        try
        {
            while (cursors.Count > 0)
            {
                var cursor = cursors.Dequeue();
                bool hasNext;
                try { hasNext = cursor.MoveNext(); }
                catch { cursor.Dispose(); throw; }   // 取り出し中の cursor はキュー外＝finally の対象にならないので、ここで破棄する。
                if (hasNext) { cursors.Enqueue(cursor); yield return cursor.Current; }
                else cursor.Dispose();
            }
        }
        finally { foreach (var cursor in cursors) cursor.Dispose(); }
    }

    // ── FindXxx: ターゲット型 single-cell 修正を [i, j, newK] で返す（無ければ null）。
    // 集合構築は 2 パス（個数を数えてから N 番目を選ぶ）で ArrayList/filter を排し GC 圧を下げる。
    // ALNS の直接評価アームから eval+cur へ copy2D なしで適用される。

    /// <summary>
    /// [need2単独定義セル見落とし修正] 過剰スキャン/移動先の不足推定をともに
    /// <see cref="Problem.CovOCell"/>/<see cref="Problem.CovUCell"/>（need1・need2のOR、source of
    /// truth）へ統一。旧実装はneed1のみで、need1未設定・need2のみで定義されたシフトの過剰/不足を
    /// 見落としていた（3.173.0のCoverageDiagnosis修正と同根）。
    /// </summary>
    internal static int[]? FindCovOFix(Problem p, DeltaEvaluator eval, JavaRandom rng)
    {
        if (p.T == 0 || p.K == 0) return null;
        int j = rng.NextInt(p.T);
        int overK = -1, maxOver = 0;
        for (int k = 0; k < p.K; k++)
        {
            int over = p.CovOCell(k, j, eval.CountOnDay(k, j));
            if (over > maxOver) { maxOver = over; overK = k; }
        }
        if (overK < 0) return null;
        int wCnt = 0;
        for (int i = 0; i < p.S; i++) if (eval.At(i, j) == overK && !p.WishLocked(i, j)) wCnt++;
        if (wCnt == 0) return null;
        int pickW = rng.NextInt(wCnt);
        int i0 = 0;
        for (int ii = 0; ii < p.S; ii++)
        {
            if (eval.At(ii, j) == overK && !p.WishLocked(ii, j))
            {
                if (pickW-- == 0) { i0 = ii; break; }
            }
        }
        int bestNw = -1, bestDef = int.MinValue;
        for (int k = 0; k < p.K; k++)
        {
            if (k == overK || !p.MayPlace(i0, k)) continue;
            int def = p.CovUCell(k, j, eval.CountOnDay(k, j));
            if (def > bestDef) { bestDef = def; bestNw = k; }
        }
        return bestNw >= 0 ? new[] { i0, j, bestNw } : null;
    }

    internal static int[]? FindC2Fix(Problem p, DeltaEvaluator eval, JavaRandom rng)
    {
        if (p.Cons2.Count == 0) return null;
        var c = p.Cons2[rng.NextInt(p.Cons2.Count)];
        int dCnt = 0;
        for (int i = 0; i < p.S; i++)
        {
            if (!p.MayPlace(i, c.ShiftIdx)) continue;
            if (eval.CountForStaff(i, c.ShiftIdx) < c.Count) dCnt++;
        }
        if (dCnt == 0) return null;
        int pickI = rng.NextInt(dCnt);
        int stf = 0;
        for (int i = 0; i < p.S; i++)
        {
            if (!p.MayPlace(i, c.ShiftIdx)) continue;
            if (eval.CountForStaff(i, c.ShiftIdx) < c.Count) { if (pickI-- == 0) { stf = i; break; } }
        }
        int dayCnt = 0;
        for (int j = 0; j < p.T; j++) if (eval.At(stf, j) != c.ShiftIdx && !p.WishLocked(stf, j)) dayCnt++;
        if (dayCnt == 0) return null;
        int pickJ = rng.NextInt(dayCnt);
        int day = 0;
        for (int j = 0; j < p.T; j++)
        {
            if (eval.At(stf, j) != c.ShiftIdx && !p.WishLocked(stf, j)) { if (pickJ-- == 0) { day = j; break; } }
        }
        return new[] { stf, day, c.ShiftIdx };
    }

    internal static int[]? FindRangeLowFix(Problem p, DeltaEvaluator eval, JavaRandom rng)
    {
        int cCnt = 0;
        for (int i = 0; i < p.S; i++)
        {
            for (int k = 0; k < p.K; k++)
            {
                int lo = p.RangeLo[i][k];
                if (lo == int.MinValue || !p.MayPlace(i, k)) continue;
                if (eval.CountForStaff(i, k) < lo) cCnt++;
            }
        }
        if (cCnt == 0) return null;
        int pickC = rng.NextInt(cCnt);
        int rlI = 0, rlK = 0;
        for (int i = 0; i < p.S; i++)
        {
            for (int k = 0; k < p.K; k++)
            {
                int lo = p.RangeLo[i][k];
                if (lo == int.MinValue || !p.MayPlace(i, k)) continue;
                if (eval.CountForStaff(i, k) < lo)
                {
                    if (pickC-- == 0) { rlI = i; rlK = k; goto FoundLowTarget; }
                }
            }
        }
        FoundLowTarget:
        int dayCnt = 0;
        for (int j = 0; j < p.T; j++) if (eval.At(rlI, j) != rlK && !p.WishLocked(rlI, j)) dayCnt++;
        if (dayCnt == 0) return null;
        int pickJ = rng.NextInt(dayCnt);
        int day = 0;
        for (int j = 0; j < p.T; j++)
        {
            if (eval.At(rlI, j) != rlK && !p.WishLocked(rlI, j)) { if (pickJ-- == 0) { day = j; break; } }
        }
        return new[] { rlI, day, rlK };
    }

    internal static int[]? FindC41Fix(Problem p, DeltaEvaluator eval, JavaRandom rng)
    {
        if (p.Cons41.Count == 0 || p.T == 0) return null;
        var c = p.Cons41[rng.NextInt(p.Cons41.Count)];
        int j = rng.NextInt(p.T);
        int cnt = 0;
        for (int i = 0; i < p.S; i++) if (p.Sgrp[i] == c.GroupIdx && eval.At(i, j) == c.ShiftIdx) cnt++;

        if (cnt > c.U)
        {
            int wCnt = 0;
            for (int i = 0; i < p.S; i++)
                if (p.Sgrp[i] == c.GroupIdx && eval.At(i, j) == c.ShiftIdx && !p.WishLocked(i, j)) wCnt++;
            if (wCnt == 0) return null;
            int pickW = rng.NextInt(wCnt);
            int ci = 0;
            for (int i = 0; i < p.S; i++)
            {
                if (p.Sgrp[i] == c.GroupIdx && eval.At(i, j) == c.ShiftIdx && !p.WishLocked(i, j))
                {
                    if (pickW-- == 0) { ci = i; break; }
                }
            }
            var allowed41 = p.AllowedShiftsForStaff(ci);
            int oCnt = 0;
            foreach (var ak in allowed41) if (ak != c.ShiftIdx) oCnt++;
            if (oCnt == 0) return null;
            int pickK = rng.NextInt(oCnt);
            int nwK = 0;
            foreach (var ak in allowed41)
            {
                if (ak != c.ShiftIdx) { if (pickK-- == 0) { nwK = ak; break; } }
            }
            return new[] { ci, j, nwK };
        }

        if (cnt < c.L)
        {
            int aCnt = 0;
            for (int i = 0; i < p.S; i++)
                if (p.Sgrp[i] == c.GroupIdx && eval.At(i, j) != c.ShiftIdx && !p.WishLocked(i, j) && p.MayPlace(i, c.ShiftIdx)) aCnt++;
            if (aCnt == 0) return null;
            int pickA = rng.NextInt(aCnt);
            int ai = 0;
            for (int i = 0; i < p.S; i++)
            {
                if (p.Sgrp[i] == c.GroupIdx && eval.At(i, j) != c.ShiftIdx && !p.WishLocked(i, j) && p.MayPlace(i, c.ShiftIdx))
                {
                    if (pickA-- == 0) { ai = i; break; }
                }
            }
            return new[] { ai, j, c.ShiftIdx };
        }

        return null;
    }

    /// <summary>c41 のスキルグループ版（Ssk + Cons41s）。形は FindC41Fix と同一。</summary>
    internal static int[]? FindC41sFix(Problem p, DeltaEvaluator eval, JavaRandom rng)
    {
        if (p.Cons41s.Count == 0 || p.T == 0) return null;
        var c = p.Cons41s[rng.NextInt(p.Cons41s.Count)];
        int j = rng.NextInt(p.T);
        int cnt = 0;
        for (int i = 0; i < p.S; i++) if (p.Ssk[i] == c.GroupIdx && eval.At(i, j) == c.ShiftIdx) cnt++;

        if (cnt > c.U)
        {
            int wCnt = 0;
            for (int i = 0; i < p.S; i++)
                if (p.Ssk[i] == c.GroupIdx && eval.At(i, j) == c.ShiftIdx && !p.WishLocked(i, j)) wCnt++;
            if (wCnt == 0) return null;
            int pickW = rng.NextInt(wCnt);
            int ci = 0;
            for (int i = 0; i < p.S; i++)
            {
                if (p.Ssk[i] == c.GroupIdx && eval.At(i, j) == c.ShiftIdx && !p.WishLocked(i, j))
                {
                    if (pickW-- == 0) { ci = i; break; }
                }
            }
            var allowed41 = p.AllowedShiftsForStaff(ci);
            int oCnt = 0;
            foreach (var ak in allowed41) if (ak != c.ShiftIdx) oCnt++;
            if (oCnt == 0) return null;
            int pickK = rng.NextInt(oCnt);
            int nwK = 0;
            foreach (var ak in allowed41)
            {
                if (ak != c.ShiftIdx) { if (pickK-- == 0) { nwK = ak; break; } }
            }
            return new[] { ci, j, nwK };
        }

        if (cnt < c.L)
        {
            int aCnt = 0;
            for (int i = 0; i < p.S; i++)
                if (p.Ssk[i] == c.GroupIdx && eval.At(i, j) != c.ShiftIdx && !p.WishLocked(i, j) && p.MayPlace(i, c.ShiftIdx)) aCnt++;
            if (aCnt == 0) return null;
            int pickA = rng.NextInt(aCnt);
            int ai = 0;
            for (int i = 0; i < p.S; i++)
            {
                if (p.Ssk[i] == c.GroupIdx && eval.At(i, j) != c.ShiftIdx && !p.WishLocked(i, j) && p.MayPlace(i, c.ShiftIdx))
                {
                    if (pickA-- == 0) { ai = i; break; }
                }
            }
            return new[] { ai, j, c.ShiftIdx };
        }

        return null;
    }

    internal static int[]? FindRangeHighFix(Problem p, DeltaEvaluator eval, JavaRandom rng)
    {
        int cCnt = 0;
        for (int i = 0; i < p.S; i++)
        {
            for (int k = 0; k < p.K; k++)
            {
                int hi = p.RangeHi[i][k];
                if (hi == int.MaxValue) continue;
                if (eval.CountForStaff(i, k) > hi) cCnt++;
            }
        }
        if (cCnt == 0) return null;
        int pickC = rng.NextInt(cCnt);
        int rhI = 0, rhK = 0;
        for (int i = 0; i < p.S; i++)
        {
            for (int k = 0; k < p.K; k++)
            {
                int hi = p.RangeHi[i][k];
                if (hi == int.MaxValue) continue;
                if (eval.CountForStaff(i, k) > hi)
                {
                    if (pickC-- == 0) { rhI = i; rhK = k; goto FoundHighTarget; }
                }
            }
        }
        FoundHighTarget:
        int dayCnt = 0;
        for (int j = 0; j < p.T; j++) if (eval.At(rhI, j) == rhK && !p.WishLocked(rhI, j)) dayCnt++;
        if (dayCnt == 0) return null;
        int pickJ = rng.NextInt(dayCnt);
        int day = 0;
        for (int j = 0; j < p.T; j++)
        {
            if (eval.At(rhI, j) == rhK && !p.WishLocked(rhI, j)) { if (pickJ-- == 0) { day = j; break; } }
        }
        var allowed = p.AllowedShiftsForStaff(rhI);
        int oCnt = 0;
        foreach (var ak in allowed) if (ak != rhK) oCnt++;
        if (oCnt == 0) return null;
        int pickK = rng.NextInt(oCnt);
        int nwK = 0;
        foreach (var ak in allowed)
        {
            if (ak != rhK) { if (pickK-- == 0) { nwK = ak; break; } }
        }
        return new[] { rhI, day, nwK };
    }

    internal static int[]? FindC3WantFix(Problem p, DeltaEvaluator eval, JavaRandom rng)
    {
        IReadOnlyList<C3> list;
        if (p.Cons3.Count > 0 && p.Cons3m.Count > 0)
            list = rng.NextBoolean() ? p.Cons3 : p.Cons3m;
        else if (p.Cons3.Count > 0)
            list = p.Cons3;
        else if (p.Cons3m.Count > 0)
            list = p.Cons3m;
        else
            return null;

        var c = list[rng.NextInt(list.Count)];
        var seq = c.Seq;
        int d = seq.Length;
        if (d < 2 || d > p.T) return null;
        int iStart = rng.NextInt(p.S);
        for (int di = 0; di < p.S; di++)
        {
            int i = (iStart + di) % p.S;
            int j = 0;
            while (j <= p.T - d)
            {
                if (eval.At(i, j) == seq[0])
                {
                    int miss = 0, missL = -1;
                    for (int l = 1; l < d; l++)
                    {
                        if (eval.At(i, j + l) != seq[l])
                        {
                            miss++;
                            if (miss > 1) break; else missL = l;
                        }
                    }
                    if (miss == 1 && missL >= 0)
                    {
                        int ml = j + missL;
                        if (!p.WishLocked(i, ml) && p.MayPlace(i, seq[missL])) return new[] { i, ml, seq[missL] };
                    }
                }
                j++;
            }
        }
        return null;
    }

    /// <summary>
    /// 8 種のターゲット修正(covO/c2/low/c41/high/c41s/c3want/apt)を一様シャッフル順に試し、最初に
    /// 見つかった修正を返す（無ければ null）。1 種が null でも次へフォールスルーするため、違反が
    /// 少ない近最適解でも毎反復に有効手を当てやすい。
    /// </summary>
    internal static int[]? FindTargetedFix(Problem p, DeltaEvaluator eval, JavaRandom rng)
    {
        var order = new int[8];
        for (int k = 0; k < 8; k++) order[k] = k;
        for (int i = 7; i >= 1; i--)
        {
            int j = rng.NextInt(i + 1);
            int t = order[i]; order[i] = order[j]; order[j] = t;
        }
        foreach (var idx in order)
        {
            int[]? fix = idx switch
            {
                0 => FindCovOFix(p, eval, rng),
                1 => FindC2Fix(p, eval, rng),
                2 => FindRangeLowFix(p, eval, rng),
                3 => FindC41Fix(p, eval, rng),
                4 => FindRangeHighFix(p, eval, rng),
                5 => FindC41sFix(p, eval, rng),
                6 => FindC3WantFix(p, eval, rng),
                _ => FindAptFix(p, eval, rng),
            };
            if (fix != null) return fix;
        }
        return null;
    }

    /// <summary>
    /// [apt研磨] 適切回数(apt)の偏差を1セルで縮める手を探す。あるスタッフで apt 超過のシフト kOver を
    /// 1日、apt 不足の担当可シフト kUnder へ振り替える（超過−1・不足−1 の双方向改善）。既存の
    /// Find*Fix には apt 専用がなく、apt 超過(例: 単一専門職の休過多)が研磨で直らなかったため追加。
    /// 担当不可・希望ロックの日は除外。covO 等の副作用は呼び出し側スコアの受理で評価。
    /// </summary>
    internal static int[]? FindAptFix(Problem p, DeltaEvaluator eval, JavaRandom rng)
    {
        if (p.S == 0 || p.T == 0) return null;
        var order = new int[p.S];
        for (int k = 0; k < p.S; k++) order[k] = k;
        for (int i = p.S - 1; i >= 1; i--)
        {
            int j = rng.NextInt(i + 1);
            int t = order[i]; order[i] = order[j]; order[j] = t;
        }
        foreach (var i in order)
        {
            var allowed = p.AllowedShiftsForStaff(i);
            if (allowed.Length == 0) continue;
            var cnt = new int[p.K];
            for (int j = 0; j < p.T; j++) cnt[eval.At(i, j)]++;
            int kOver = -1, kUnder = -1;
            for (int k = 0; k < p.K; k++)
            {
                int tg = p.Apt[i][k];
                if (tg < 0) continue;
                if (kOver < 0 && cnt[k] > tg) kOver = k;
                if (kUnder < 0 && cnt[k] < tg && Array.IndexOf(allowed, k) >= 0) kUnder = k;
            }
            if (kOver < 0 || kUnder < 0 || kOver == kUnder) continue;
            int dayStart = rng.NextInt(p.T);
            for (int d = 0; d < p.T; d++)
            {
                int j = (dayStart + d) % p.T;
                if (!p.WishLocked(i, j) && eval.At(i, j) == kOver) return new[] { i, j, kUnder };
            }
        }
        return null;
    }

    /// <summary>[GLS] 1 セルの割当変更による penalty 拡張分の差分（変更セルだけで O(1)）。</summary>
    internal static double GlsMoveAug(GlsPenalty gls, int i, int j, int oldK, int nwK) =>
        oldK == nwK ? 0.0 : gls.Lambda * (gls.PenaltyOf(i, j, nwK) - gls.PenaltyOf(i, j, oldK));

    /// <summary>DeltaEvaluator 生スコア(hard*SCORE_HARD_UNIT+soft)の比較。小さいほど良い。</summary>
    internal static bool BetterScore(long a, long b) => a < b;

    /// <summary>DeltaEvaluator 生スコアの SA 受理。hard が +2 超増える手は却下。</summary>
    internal static bool AcceptWorseScore(long a, long b, double temp, JavaRandom rng)
    {
        // [3.213.0見落とし修正] SCORE_HARD_UNIT を1e6→1e9へ拡大した際にこの閾値だけ旧スケール
        // (2*1e6)のまま残っていた。新スケールでは2*1e6は1 HARD単位(1e9)の0.2%に過ぎず、hardが1
        // 増えるだけで即座に(却下する意図の"+2超"よりずっと手前で)却下される退行だった。
        // 2*SCORE_HARD_UNITへ同期。
        if (a > b + 2 * Evaluator.SCORE_HARD_UNIT) return false;
        double delta = a - b;
        return delta <= 0.0 || rng.NextDouble() < Math.Exp(-Math.Max(0.0, delta) / (200.0 * temp + 1e-9));
    }

    /// <summary>
    /// [3.303.0] 職員 i を day j で <paramref name="fillShift"/> にしたとき成立する禁止パターンに
    /// ついて、「崩しに行ける日」を j に近い順で返す（j 自身は目的のシフトなので除く）。
    ///
    /// 旧実装は j-1 / j+1 の2日<b>固定</b>だった。実データの cons3n には <c>Dﾃ→休→A4</c> のような
    /// 3連があり、当日が末尾(A4)のときパターンの先頭は j-2 にあるため、<b>旧実装ではそのパターンを
    /// 一度も崩せなかった</b>（c1 研磨の不採用主因が c3n だと 3.302.0 のログ強化で実測できたことが、
    /// この穴を追う入口になった）。ここでは実際に成立している窓がまたぐ日をビット走査で求め、その
    /// 全部を候補にする。T&gt;64 は <see cref="C3nBitScan"/> が使えないので従来どおり j±1 のみ
    /// （安全側＝候補が減るだけ）。
    ///
    /// [Kotlin 3.356.0/ピース5で配線] <see cref="TuningTelemetry.IncrementWideC3nCalls"/>/
    /// <see cref="TuningTelemetry.IncrementWideC3nDiffered"/> という診断専用カウンタ（探索・採否・
    /// スコアには一切影響しない読み取り専用の計数）をここで加算する。関数の分岐・戻り値ロジックは
    /// 完全にそのまま（<c>TuningTelemetry</c>自体はピース5で移植・配線済み）。
    /// </summary>
    internal static int[] BreakableDaysFor(Problem p, int[][] sched, int i, int j, int fillShift)
    {
        // [Kotlin原本, 既定OFF・3.303.0] 一般化として正しいが実データ3件で利得が一貫しなかった
        //   （PolishGate の docstring に計測値）。既定は従来どおり j±1 のみで、ゲートを ON にしたときだけ広げる。
        TuningTelemetry.IncrementWideC3nCalls();
        if (!PolishGate.WideC3nBreakDays) return new[] { j - 1, j + 1 };
        if (!C3nBitScan.Usable(p) || i < 0 || i >= sched.Length) return new[] { j - 1, j + 1 };
        var row = sched[i];
        var mask = C3nBitScan.BuildRowMask(p, row);
        int old = (j >= 0 && j < p.T && j < row.Length) ? row[j] : -1;
        long days = C3nBitScan.CoveringRunDaysAfterSet(p, mask, j, old, fillShift);
        // [Kotlin 3.356.0] 既定(j±1)と違う結果になった回数を数える。**広がる場合だけでなく狭まる場合もある**
        //   （covering run が無ければ空を返す＝既定より狭い）ので、「違うかどうか」で数える。
        if (days == 0L) { TuningTelemetry.IncrementWideC3nDiffered(); return Array.Empty<int>(); }
        // j に近い日から試す（当日から遠い日ほど他の制約への波及が読みにくいため、影響の小さい順）。
        var outList = new List<int>(System.Numerics.BitOperations.PopCount((ulong)days));
        long rest = days & ~(1L << j);
        while (rest != 0L)
        {
            outList.Add(System.Numerics.BitOperations.TrailingZeroCount((ulong)rest));
            rest &= rest - 1;
        }
        var result = outList.OrderBy(d => Math.Abs(d - j)).ToArray();
        if (result.Length != 2 || !(result.Contains(j - 1) && result.Contains(j + 1))) TuningTelemetry.IncrementWideC3nDiffered();
        return result;
    }

    /// <summary>
    /// [3.163.0でFindCovUChain内に導入・3.226.0で共通ヘルパーへ汎用化] 職員 i を day j で
    /// fillShift へ動かすと禁止連続(c3n)に触れる場合、パターンがまたぐ日の i 自身の割当を別シフトへ
    /// 変えて崩せないか試す（3.303.0 で j±1 固定 → 実際に成立している窓の全日へ拡張）。変更で空く
    /// シフト(oldJ2)が covU 悪化を招くなら、FindCovUChain を <c>allowCrossDayFix=false</c> で1段だけ
    /// 再帰し玉突き連鎖として埋め直す（無限展開防止）。見つかれば [(i, j2, alt), ...サブ連鎖]
    /// （すべて day j2 のみの手）を返す（盤面は判定中に一時変更するが必ず復元＝呼び出し側が実際に
    /// 適用するかを決める）。見つからなければ null。FindCovUChain（covU側の禁止連続回避）・
    /// applyCovOFree（covO側、3.226.0で追加採用）から共通利用。
    /// [3.232.0/ドッグフーディングで発見] maxDepth既定は FindCovUChain と同じ理由で
    /// (p.K-1).coerceAtLeast(1) 相当（下記 FindCovUChain のドキュメント参照）。
    /// </summary>
    internal static List<int[]>? TryFixForbiddenRunViaAdjacentDay(
        Problem p, int[][] sched, int i, int j, int fillShift, JavaRandom rng,
        bool allowCrossDayFix = true, int? maxDepth = null)
    {
        int effMaxDepth = maxDepth ?? Math.Max(p.K - 1, 1);
        if (!allowCrossDayFix) return null;
        foreach (var j2 in BreakableDaysFor(p, sched, i, j, fillShift))
        {
            if (j2 < 0 || j2 >= p.T || p.WishLocked(i, j2)) continue;
            int oldJ2 = sched[i][j2];
            if (oldJ2 < 0 || oldJ2 >= p.K) continue;
            // 候補シフト: 担当可能シフトを順に試す。
            // [3.345.0] 休は通常のシフト種の一つ＝先頭に置く優先をやめた（旧: 休を第一候補にしていた）。
            //   実データ3件の後処理研磨で最終盤面がバイト一致＝この優先は実質不活性だった。
            var altOrder = new List<int>();
            foreach (var s in p.AllowedShiftsForStaff(i)) if (s != oldJ2) altOrder.Add(s);
            foreach (var alt in altOrder)
            {
                int cntBefore = 0;
                for (int it = 0; it < p.S; it++) if (sched[it][j2] == oldJ2) cntBefore++;
                sched[i][j2] = alt;   // [一時変更] 下の判定後に必ず復元する
                bool jOk = !p.MakesForbiddenRun(sched, i, j, fillShift);
                bool j2Ok = !p.MakesForbiddenRun(sched, i, j2, alt);
                if (!jOk || !j2Ok) { sched[i][j2] = oldJ2; continue; }
                if (p.CovUCell(oldJ2, j2, cntBefore - 1) > p.CovUCell(oldJ2, j2, cntBefore))
                {
                    // i の離脱で oldJ2 が covU 悪化 → 同アルゴリズムを1段だけ再帰して埋め直す。
                    var subChain = FindCovUChain(p, sched, oldJ2, j2, rng, maxDepth: effMaxDepth, exclude: i, allowCrossDayFix: false);
                    sched[i][j2] = oldJ2;
                    if (subChain != null)
                    {
                        var result = new List<int[]> { new[] { i, j2, alt } };
                        result.AddRange(subChain);
                        return result;
                    }
                }
                else
                {
                    sched[i][j2] = oldJ2;
                    return new List<int[]> { new[] { i, j2, alt } };
                }
            }
        }
        return null;
    }

    /// <summary>
    /// [E11/多人数ブロック移動] covU セル (k0, j) を同日・多人数の「玉突き連鎖」で充填する交代連鎖を
    /// BFS（最短優先）で探す。対象の failure mode（実機 2026-08 データ・ユーザー指摘で確認）: 直接
    /// 充填の候補が (a)希望ロック (b)単一被覆シフト在勤＝引き抜くと玉突き covU (c)禁止連続 に当たり、
    /// 既存の修復オペレータ（destroyRepairDay=「休→勤務」しか試さない）では踏めない「勤務→勤務」
    /// 連鎖でのみ埋まる局面。ユーザー実例: 8/11 モニカ B4→Cｵ（深さ1・過剰B4から補充）／8/17 上條
    /// Cｵ→Cｱ, 山本 →Cｵ（深さ2）。
    ///
    /// 探索: 「k0 を職員 i が埋める → i が空けたシフト m を次の職員が埋める → … → 空けても covU が
    /// 増えないシフト（需要0 or 余裕あり）で終端」。リンク条件: canDo・非wishLocked・禁止連続(c3n,
    /// 任意長=三連/五連等)のプルーニング・同一職員の再訪なし・同一シフト展開の再訪なし・深さ≤maxDepth。
    /// 同日内交換なので被覆総量は保存。
    ///
    /// [3.232.0/ドッグフーディングで発見・maxDepth既定を(p.K-1)へ引き上げ] visited はシフト単位
    /// (bool[K])で管理されるため、本BFSは元々シフト数K種を超えて展開できない＝maxDepthをK以上に
    /// しても計算量は増えない（自然にO(K×S)で頭打ち）。旧既定5(「最大5人の玉突き」という検証しやすさ
    /// 重視の設計意図)は、実データでこの上限より深い箇所にのみ解が存在する場合に「候補なし」として
    /// 誤って諦めてしまう（桒澤美幸のAｱ超過・8/6のCｱ不足など、RangePolish/RSI covU focusで繰り返し
    /// 「候補なし/玉突き必要」のまま残る事例で確認）。計算コストがほぼ無視できることを踏まえ、既定を
    /// Max(p.K-1, 1)（=シフト全種を使い切るまで探索）へ引き上げた（ユーザー承認: 「人間が検証しやすい
    /// 5人まで」の設計意図より網羅性を優先）。
    ///
    /// [禁止連続の回避=隣接日調整] 候補 i が k0 を埋めると禁止連続(c3n)に触れる場合、即除外せず、
    /// 隣接日(j-1/j+1)の i 自身の割当も変えてパターンを崩せないか試す
    /// （<see cref="TryFixForbiddenRunViaAdjacentDay"/>）。その調整で i の隣接日の元シフトが空き covU
    /// が悪化するなら、そこも同じ FindCovUChain を allowCrossDayFix=false で再帰し玉突き連鎖として
    /// 埋め直す（cross-day 再帰は1段のみ＝無限展開防止）。見つかった追加手は Node.Extra に積み、最終
    /// 手順に合流する。
    ///
    /// 返り値 = 適用手 [(i, j, newK), ...]（本関数は盤面を変更しない。適用と採否=keep-best は
    /// 呼び出し側＝スコアリング不変・退化不能）。見つからなければ null。
    /// </summary>
    /// <param name="c1Pref">
    /// [C1研磨・手B強化] 候補(staff,shift,day)がその職員自身のc1不足解消にも資するかの述語。
    /// 非nullのとき Candidates() の返り値を「述語=true」優先に並べ替えるだけ（探索ロジック自体は
    /// 不変）。既定null=既存呼出元は完全に挙動不変。
    /// </param>
    /// <param name="rangeAvoid">
    /// [頭打ち調査で判明・RangePolish/C3mnPolish/C3RunPolish向け] Candidates() は構造的妥当性
    /// (canDo/希望ロック/禁止連続)のみで選び、rng順の最初の1件が完成すればそれで確定＝コスト無視。
    /// 桒澤美幸のAｱ超過(3.215.0)研磨が頭打ちする実例を追跡した結果、「候補自身のstaffRange上限を
    /// 新たに超えさせる」手を先頭で引くと、excess(3)+excess(1)で合計は不変＝isBetterが改善なしとして
    /// 却下し、その日は二度と試行されない（1日1回きりの呼出のため）ことを確認。rangeAvoid が真を返す
    /// 候補は「除外」でなく「後回し」にするだけ（他に候補が無ければ従来どおり使う＝解が消えない）。
    /// 既定null=既存呼出元は完全に挙動不変。
    /// </param>
    internal static List<int[]>? FindCovUChain(
        Problem p, int[][] sched, int k0, int j, JavaRandom rng,
        int? maxDepth = null, int exclude = -1, bool allowCrossDayFix = true,
        Func<int, int, int, bool>? c1Pref = null,
        Func<int, int, bool>? rangeAvoid = null)
    {
        int effMaxDepth = maxDepth ?? Math.Max(p.K - 1, 1);
        if (j < 0 || j >= p.T || k0 < 0 || k0 >= p.K || p.S == 0) return null;
        var cnt = new int[p.K];
        for (int i = 0; i < p.S; i++)
        {
            int kk = sched[i][j];
            if (kk >= 0 && kk < p.K) cnt[kk]++;
        }
        // 充填で covU が実際に減るセルのみ対象（need 未設定などは対象外）。
        if (p.CovUCell(k0, j, cnt[k0] + 1) >= p.CovUCell(k0, j, cnt[k0])) return null;

        // [三連/五連など任意長対応] 禁止連続(c3n)を作る移動を除外（最終ゲートは呼び出し側 checker が
        //   担保＝ここは成功率向上の枝刈り。Problem.MakesForbiddenRun が任意長ルールを一般判定）。
        bool C3nHits(int i, int newK) => p.MakesForbiddenRun(sched, i, j, newK);

        // [禁止連続の回避=隣接日調整] i を k0(day j) へ動かすと禁止連続に触れるとき、隣接日(j-1/j+1)の
        //   i の割当を別シフトへ変えてパターンを崩せないか試す（共通ヘルパー
        //   TryFixForbiddenRunViaAdjacentDay に汎用化・3.226.0でapplyCovOFreeからも再利用）。
        List<int[]>? TryFixC3nViaAdjacentDay(int i, int fillShift) =>
            TryFixForbiddenRunViaAdjacentDay(p, sched, i, j, fillShift, rng, allowCrossDayFix, effMaxDepth);

        // 職員の走査順を乱択（同型解の多様化。決定性が欲しい呼び出しは seed 固定の rng を渡す）。
        var order = new int[p.S];
        for (int k = 0; k < p.S; k++) order[k] = k;
        for (int x = p.S - 1; x >= 1; x--)
        {
            int y = rng.NextInt(x + 1);
            int t = order[x]; order[x] = order[y]; order[y] = t;
        }

        // BFS ノード = 「fillShift へ staff が入る」手。子 = staff が空けた現シフトを埋める手。
        // Extra = 禁止連続を回避するための追加手（隣接日調整＋サブ連鎖。無ければ null）。
        List<Node> Candidates(int fillShift, Node? prev)
        {
            var outNodes = new List<Node>();
            foreach (var i in order)
            {
                if (i == exclude) continue;   // [C1×E11] 呼出元が別途動かした職員を連鎖の候補から除外（無効な回帰手を防ぐ）
                int m = sched[i][j];
                if (m < 0 || m >= p.K || m == fillShift) continue;
                if (!p.MayPlace(i, fillShift) || p.WishLocked(i, j)) continue;
                var q = prev;
                bool used = false;
                while (q != null) { if (q.Staff == i) { used = true; break; } q = q.Prev; }
                if (used) continue;
                if (C3nHits(i, fillShift))
                {
                    var fix = TryFixC3nViaAdjacentDay(i, fillShift);
                    if (fix == null) continue;
                    outNodes.Add(new Node(fillShift, i, prev, fix));
                    continue;
                }
                outNodes.Add(new Node(fillShift, i, prev));
            }
            // [頭打ち調査] rangeAvoid が真の候補（自身の新規range違反を招く）を後回しへ。他に候補が無
            //   ければ最終的にはこの中から使われる＝解が消えることはない（「除外」でなく「並べ替え」のみ）。
            List<Node> result = outNodes;
            if (rangeAvoid != null && result.Count > 1)
            {
                var keep = new List<Node>();
                var avoid = new List<Node>();
                foreach (var n in result) { if (!rangeAvoid(n.Staff, fillShift)) keep.Add(n); else avoid.Add(n); }
                result = keep.Concat(avoid).ToList();
            }
            // [C1研磨・手B強化] c1Pref を満たす候補を先頭へ（TryComplete は frontier を先頭から見て
            //   最初に成立した連鎖を採用するため、並べ替えだけで「その職員のc1不足も一緒に解消する
            //   連鎖」が優先的に見つかる。安全条件(canDo/wishLock/c3n)は上のフィルタ済のままなので
            //   探索の正しさは不変）。
            if (c1Pref != null && result.Count > 1)
            {
                var pref = new List<Node>();
                var rest = new List<Node>();
                foreach (var n in result) { if (c1Pref(n.Staff, fillShift, j)) pref.Add(n); else rest.Add(n); }
                result = pref.Concat(rest).ToList();
            }
            return result;
        }

        // 終端: このノードの職員が空けるシフト m は、1人減っても covU が増えない（需要0 or 余裕あり）。
        List<int[]>? TryComplete(Node node)
        {
            int m = sched[node.Staff][j];
            // [敵対的レビュー修正] cnt[] は探索開始時点の静的値。祖先ノードのチェーン適用でシフト m の
            //   実際のheadcountは変わりうるため、祖先を辿って m への「到着」(+1: 祖先の
            //   FillShift==m)と「離脱」(-1: 祖先の元シフト==m、つまりその祖先はチェーンの一員として
            //   既に m を離れることが確定している)を両方加味した真のheadcountで安全性を判定する。
            //   [第2版・重要] 到着分だけを補正する初版修正は不完全だった: 祖先 a が m から離脱しつつ
            //   別の祖先 g が m へ到着するケース（3段連鎖等）では、離脱を差し引かないと m の
            //   headcountを過大評価し、実際には covU を悪化させる連鎖を安全と誤判定しかねない
            //   （false accept）。呼出元の checker+isBetter が最終防波堤とはいえ、判定ロジック自体は
            //   到着・離脱の両方を対称に扱うのが正しい。
            int adj = 0;
            var anc = node.Prev;
            while (anc != null)
            {
                var a = anc;
                if (a.FillShift == m) adj++;                     // 祖先 a が m へ到着済み
                if (sched[a.Staff][j] == m) adj--;                // 祖先 a の元シフトが m＝m から離脱済み
                anc = a.Prev;
            }
            int trueCnt = cnt[m] + adj;
            if (p.CovUCell(m, j, trueCnt - 1) > p.CovUCell(m, j, trueCnt)) return null;
            var moves = new List<int[]>();
            Node? n = node;
            while (n != null)
            {
                moves.Add(new[] { n.Staff, j, n.FillShift });
                if (n.Extra != null) moves.AddRange(n.Extra);   // [禁止連続の回避] 隣接日調整＋サブ連鎖の追加手を合流
                n = n.Prev;
            }
            return moves;
        }

        var visited = new bool[p.K];
        visited[k0] = true;
        var frontier = Candidates(k0, null);
        int depth = 0;
        while (depth < effMaxDepth && frontier.Count > 0)
        {
            foreach (var node in frontier)
            {
                var complete = TryComplete(node);
                if (complete != null) return complete;
            }
            var next = new List<Node>();
            foreach (var node in frontier)
            {
                int m = sched[node.Staff][j];
                if (m >= 0 && m < p.K && !visited[m]) { visited[m] = true; next.AddRange(Candidates(m, node)); }
            }
            frontier = next;
            depth++;
        }
        return null;
    }

    /// <summary>
    /// Faithful port of the local <c>class Node(...)</c> defined inside Kotlin's
    /// <c>findCovUChain</c> function body. C# has no equivalent of a class declared at local/method
    /// scope (only local *functions* are supported since C# 7), so this is instead a private nested
    /// class of the containing static class — narrower than <see cref="V6SearchOperators"/> itself
    /// (still invisible outside it) but slightly broader than Kotlin's true function-local scope
    /// (visible to every method of this class, not just <see cref="FindCovUChain"/>). This is a
    /// necessary, deliberate translation accommodation, not a behavioral change.
    /// </summary>
    private sealed class Node
    {
        public readonly int FillShift;
        public readonly int Staff;
        public readonly Node? Prev;
        public readonly List<int[]>? Extra;

        public Node(int fillShift, int staff, Node? prev, List<int[]>? extra = null)
        {
            FillShift = fillShift;
            Staff = staff;
            Prev = prev;
            Extra = extra;
        }
    }

    /// <summary>
    /// 非改善手の受理判定（生スコア＝hard*SCORE_HARD_UNIT+soft）。GLS 拡張分(moveAug=候補−現行)を
    /// 加味する。hard が +2 超増える手は常に却下。Great Deluge は水位以下かつ hard 非増加で受理。
    /// </summary>
    internal static bool GlsAccept(
        long ns, long curScore, double moveAug, double curAug,
        AcceptMode mode, double temp, double gdLevel, JavaRandom rng)
    {
        // [3.213.0見落とし修正] AcceptWorseScore と同じ理由で 2*SCORE_HARD_UNIT へ同期（詳細は
        // AcceptWorseScore のコメント参照）。
        if (ns > curScore + 2 * Evaluator.SCORE_HARD_UNIT) return false;
        switch (mode)
        {
            case AcceptMode.GreatDeluge:
                return (ns + curAug + moveAug) <= gdLevel
                    && (ns / Evaluator.SCORE_HARD_UNIT) <= (curScore / Evaluator.SCORE_HARD_UNIT);
            case AcceptMode.Sa:
            case AcceptMode.LamAdaptive:
            default:
                // LAM_ADAPTIVE は受理式は SA と同じ Boltzmann。違いは呼び出し側が temp を受理率追従で
                // 適応させる点。
                double delta = (ns - curScore) + moveAug;
                return delta <= 0.0 || rng.NextDouble() < Math.Exp(-Math.Max(0.0, delta) / (200.0 * temp + 1e-9));
        }
    }

    /// <summary>from と to で異なるセルの flat index(i*T+j) を buf に詰め、件数を返す（ゼロアロケーション）。</summary>
    internal static int DiffInto(int t, int[][] from, int[][] to, int[] buf)
    {
        int n = 0;
        for (int i = 0; i < from.Length; i++)
        {
            var fr = from[i];
            var tr = to[i];
            for (int j = 0; j < t; j++) if (fr[j] != tr[j]) buf[n++] = i * t + j;
        }
        return n;
    }

    /// <summary>
    /// [不採用の主因] 候補盤面 <paramref name="after"/> が基準 <paramref name="before"/> より
    /// 「重み付きで最も増えた」族を返す（同点は MirrorKeys.All の順で先勝ち・悪化が無ければ null）。
    /// 研磨パスが候補を捨てたとき、ログに「何を壊したから捨てたのか」を族名で残すための読み取り専用
    /// ヘルパー。
    ///
    /// 動機: 頭打ち理由の分類（RangePolish 3.222.0 / C1Polish 3.236.0）は「候補なし（構造的に手が
    /// 無い）」と「不採用（手はあるが目的関数が拒否）」を区別するが、後者の<b>理由</b>までは出して
    /// いなかった。実機ログの c1 残存が「不採用×65 / 候補なし×4」＝ほぼ全部が拒否だったため、次に
    /// 何を緩めるべきかがログから読めない。AdaptiveBlockSwap（3.293.0）は同じ趣旨の「悪化の主因」を
    /// 既に出しており、その計算をここへ共通化する。
    /// </summary>
    internal static string? WorstWorsenedFamily(ViolationReport after, ViolationReport before)
    {
        string? worstFam = null;
        double worstDelta = 0.0;
        foreach (var fam in MirrorKeys.All)
        {
            int afterVal = after.Breakdown.TryGetValue(fam, out var av) ? av : 0;
            int beforeVal = before.Breakdown.TryGetValue(fam, out var bv) ? bv : 0;
            double d = (afterVal - beforeVal) * MirrorKeys.WeightOf(fam);
            if (d > worstDelta) { worstDelta = d; worstFam = fam; }
        }
        return worstFam;
    }

    /// <summary>
    /// [3.326.0] 厳密ピンを目標から遠ざけた (職員, シフト) を全部返す。
    ///
    /// <see cref="ExactPinRegression"/> は「1件でもあるか」の高速判定（見つけ次第 return）なので、
    /// <b>どのピンが止めたか</b>は分からない。緩和の対象を利用者へ提示するにはそれが必要なので、判定が
    /// 真だったときだけこちらを呼ぶ（ホットパスは ExactPinRegression のまま＝早期 return が残る）。
    /// </summary>
    /// <returns>[職員, シフト] の並び。判定と同じ意味論（目標からの距離が増えたものだけ）。</returns>
    internal static List<int[]> ExactPinOffenders(Problem p, int[][] before, int[][] after)
    {
        var outList = new List<int[]>(2);
        for (int i = 0; i < p.S; i++)
        {
            for (int k = 0; k < p.K; k++)
            {
                int lo = p.RangeLo[i][k];
                int hi = p.RangeHi[i][k];
                if (lo == int.MinValue || hi == int.MaxValue || lo != hi) continue;
                int beforeCnt = 0, afterCnt = 0;
                for (int j = 0; j < p.T; j++)
                {
                    if (before[i][j] == k) beforeCnt++;
                    if (after[i][j] == k) afterCnt++;
                }
                if (Math.Abs(afterCnt - lo) > Math.Abs(beforeCnt - lo)) outList.Add(new[] { i, k });
            }
        }
        return outList;
    }

    /// <summary>
    /// [厳密ピン(lo==hi)保護] 職員×シフトの staffRange が下限=上限で完全固定("厳密ピン"＝月に必ず
    /// ちょうど N回)されている箇所について、候補盤面が基準盤面より目標回数からより遠ざかる職員が
    /// 1人でもいるか判定する。
    ///
    /// 動機（桒澤美幸の実例、実機ログ+実データ検証）: 休(rest)が10-10固定の彼女が、
    /// C1JointLnsPolish/C1TemporalFlowPolish/PersonalBalanceJointLnsPolish 等の複数職員横断ジョイント
    /// 研磨により、他職員の c1/covU改善の副作用として10→13へ動かされる（total/weightedScoreは全体と
    /// して改善するため既存の isBetter/better(hard→weighted→total辞書式)keep-bestだけでは防げない）。
    /// 通常のlo&lt;hi範囲は既存の重み(90/45)付きソフト評価のままで良いが、"厳密ピン"は「担当外(canDo)
    /// ガード」や「希望固定」と同種の個人単位の確定事項として扱い、これらのジョイント研磨パスの最終
    /// 採否にAND条件として追加する。
    ///
    /// 目的関数・重みは不変（Checker/Evaluator/DeltaEvaluatorは無変更）。あくまで該当パスの候補受理を
    /// 追加で絞るだけ＝退化不能（現状維持したままでも常に選べる）。既に基準盤面がピンから外れている
    /// (データ側の既存不整合)場合は、そこから遠ざける変更のみを禁じ、現状維持やピンへ近づける変更は
    /// 妨げない。
    /// </summary>
    internal static bool ExactPinRegression(Problem p, int[][] before, int[][] after)
    {
        for (int i = 0; i < p.S; i++)
        {
            for (int k = 0; k < p.K; k++)
            {
                int lo = p.RangeLo[i][k];
                int hi = p.RangeHi[i][k];
                if (lo == int.MinValue || hi == int.MaxValue || lo != hi) continue;
                int beforeCnt = 0, afterCnt = 0;
                for (int j = 0; j < p.T; j++)
                {
                    if (before[i][j] == k) beforeCnt++;
                    if (after[i][j] == k) afterCnt++;
                }
                if (Math.Abs(afterCnt - lo) > Math.Abs(beforeCnt - lo)) return true;
            }
        }
        return false;
    }
}

/// <summary>
/// [不採用の理由・パス共通の集計, 3.303.0 → 3.321.0 で分類化] 研磨パスが候補を捨てたときの理由を
/// 数える。
///
/// 3.302.0 で C1Polish / RangePolish に入れた職員別の主因表示を、構造の異なる他パスへも同じ形で
/// 広げるための最小の受け皿（パス単位の集計＝AdaptiveBlockSwap と同じ粒度）。各パスの不採用地点で
/// <see cref="Record"/> を呼び、ログ末尾に <see cref="Summary"/> を足すだけで済む。読み取り専用・
/// 採否には一切影響しない。
///
/// [3.321.0] <b>理由を分類する</b>。旧実装は <see cref="V6SearchOperators.WorstWorsenedFamily"/> の
/// 結果だけを数えていたため、呼出側の受理条件
/// <c>isBetter(rep, best) &amp;&amp; !ExactPinRegression(...)</c> の<b>後半で落ちた候補</b>（＝厳密
/// ピン破り。isBetter 自体は true なので悪化族が存在しない）が <c>Rejected</c> だけ増やして主因に何も
/// 残さず、「不採用N件(主因 …)」の N と内訳が合わなくなっていた。分類は
/// <see cref="UnifiedViolationChecker.BetterReport"/> の判定順（hard → weightedScore → total）と厳密に
/// 一致させ、AdaptiveBlockSwap(3.293.0) が既に持っていた5分類をここへ集約する。
/// </summary>
internal sealed class RejectCulpritStats
{
    private readonly Dictionary<string, int> _counts = new();

    /// <summary>却下の総数。以下の内訳の合計と必ず一致する。</summary>
    public int Rejected { get; private set; }

    /// <summary>厳密ピン(lo==hi)を目標から遠ざけるため却下。違反自体は改善しているので主因族を持たない。</summary>
    public int PinBroken { get; private set; }

    /// <summary>必須違反(HARD)が増えるため却下。主因族つき。</summary>
    public int HardUp { get; private set; }

    /// <summary>HARD 同値で重み付きスコアが悪化するため却下。主因族つき。</summary>
    public int WeightUp { get; private set; }

    /// <summary>HARD・重みとも同値で件数が増えるため却下。</summary>
    public int TotalUp { get; private set; }

    /// <summary>どのキーも同値＝改善しないため却下。</summary>
    public int Same { get; private set; }

    /// <param name="pinBroken">
    /// 呼出側の <see cref="V6SearchOperators.ExactPinRegression"/> の結果。
    ///
    /// [3.347.0/敵対検証] <b>「ピン破り」を名乗れるのは、目的関数が採用を認めた手をピンだけが止めた
    /// とき</b>（そのときだけ「違反自体は改善しているので主因族を持たない」が成り立つ）。呼出側は
    /// <c>isBetter(...) &amp;&amp; !ExactPinRegression(...)</c> の形で、隣の
    /// <see cref="PinBlockAttribution.BlocksImproving"/> は <c>pinBad &amp;&amp; isBetter(...)</c> と
    /// 正しく絞っているのに、こちらは生の <c>pinBad</c> をそのまま受けていた。結果、<b>採点でも落ちる
    /// 手までピン破りに数え、本当の主因族を隠していた</b>。実データ計測: golden の c3mn 96件・fair
    /// 28件、user の c3mn 98件・fair 266件が<b>全件</b>「非改善なのにピン破り」で、98〜100% が誤ラベル。
    /// 3.326.0 が <see cref="PinBlockAttribution"/> 側で厳密化した意味論の取り残し（教訓#31）。ここで
    /// <see cref="UnifiedViolationChecker.BetterReport"/> を通し、採点で落ちる手は従来どおり必須増／
    /// 重み悪化／件数悪化へ分類する。
    /// </param>
    public void Record(ViolationReport after, ViolationReport before, bool pinBroken = false)
    {
        Rejected++;
        if (pinBroken && UnifiedViolationChecker.BetterReport(after, before)) { PinBroken++; return; }
        if (after.Hard > before.Hard)
        {
            HardUp++;
            var fam = V6SearchOperators.WorstWorsenedFamily(after, before);
            if (fam != null) _counts[fam] = (_counts.TryGetValue(fam, out var v) ? v : 0) + 1;
        }
        else if (after.WeightedScore > before.WeightedScore)
        {
            WeightUp++;
            var fam = V6SearchOperators.WorstWorsenedFamily(after, before);
            if (fam != null) _counts[fam] = (_counts.TryGetValue(fam, out var v) ? v : 0) + 1;
        }
        else if (after.Total > before.Total)
        {
            TotalUp++;
        }
        else
        {
            Same++;
        }
    }

    public string Summary()
    {
        if (Rejected == 0) return "";
        var parts = new List<string>();
        if (PinBroken > 0) parts.Add($"ピン破り:{PinBroken}");
        if (HardUp > 0) parts.Add($"必須増:{HardUp}");
        if (WeightUp > 0) parts.Add($"重み悪化:{WeightUp}");
        if (TotalUp > 0) parts.Add($"件数悪化:{TotalUp}");
        if (Same > 0) parts.Add($"同値:{Same}");
        string culprits = string.Join(" ",
            _counts.OrderByDescending(kv => kv.Value).Take(3).Select(kv => $"{kv.Key}:{kv.Value}"));
        return $" 不採用{Rejected}件({string.Join(" ", parts)})" +
            (culprits.Length == 0 ? "" : $"(主因 {culprits})");
    }
}

/// <summary>
/// [3.326.0] 「回数固定(lo==hi)だけが却下の理由だった候補試行」を<b>対象(職員,シフト)別に</b>数える。
///
/// ここに入るのは <see cref="UnifiedViolationChecker.BetterReport"/> が採用を認めた手だけ＝ピンの
/// ガードを外せば通ったはずの手。よって「どのピンを緩めれば何回ぶん通り得たか」の実測値になる
/// （推測ではない）。
///
/// 判定本体の <see cref="V6SearchOperators.ExactPinRegression"/>/
/// <see cref="V6SearchOperators.ExactPinOffenders"/> は internal のまま、この集計クラス自体は public
/// （UIの緩和対象一覧が読むため。Kotlin原本の <c>class PinBlockAttribution</c> は無修飾＝publicで
/// あることに一致）。
///
/// <b>数の性質</b>（画面へ出すときは必ず添える）: 手の数ではなく<b>試行の回数</b>。研磨は最大4巡する
/// ので同じ手が複数の巡で数えられうる（重複排除していない）。
/// </summary>
public sealed class PinBlockAttribution
{
    private readonly Dictionary<long, int> _counts = new();

    /// <summary>計測できた試行の総数。</summary>
    public int Attempts { get; private set; }

    /// <summary>
    /// 判定と記録を同時に行う。<b>isBetter が真であることを確認した直後にだけ呼ぶ</b>
    /// （<c>isBetter(...) &amp;&amp; !pinBlocks.BlocksImproving(...)</c> の形なら短絡により保証される）。
    /// 記録対象を「目的関数が採用を認めた手」に限定するのが目的で、そうでない候補を混ぜると
    /// 「緩めれば通ったはず」の主張が崩れる。
    /// </summary>
    public bool BlocksImproving(Problem p, int[][] before, int[][] after)
    {
        bool bad = V6SearchOperators.ExactPinRegression(p, before, after);
        if (bad) Record(p, before, after);
        return bad;
    }

    /// <summary>ピン単独却下を1件記録する。ExactPinRegression が真だったときだけ呼ぶ。</summary>
    public void Record(Problem p, int[][] before, int[][] after)
    {
        Attempts++;
        foreach (var o in V6SearchOperators.ExactPinOffenders(p, before, after))
        {
            long key = ((long)o[0] << 32) | (long)o[1];
            _counts[key] = _counts.TryGetValue(key, out var v) ? v + 1 : 1;
        }
    }

    public void Merge(PinBlockAttribution other)
    {
        Attempts += other.Attempts;
        foreach (var (k, v) in other._counts)
            _counts[k] = _counts.TryGetValue(k, out var cur) ? cur + v : v;
    }

    /// <summary>(職員, シフト) → 却下試行数。件数の多い順。</summary>
    public List<(int Staff, int Shift, int Count)> ByTarget() =>
        _counts.OrderByDescending(kv => kv.Value)
            .Select(kv => ((int)(kv.Key >> 32), (int)(kv.Key & 0xFFFFFFFFL), kv.Value))
            .ToList();

    public bool IsEmpty => Attempts == 0;
}
