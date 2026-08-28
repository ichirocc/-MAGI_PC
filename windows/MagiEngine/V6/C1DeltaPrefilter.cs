namespace MagiEngine.V6;

/// <summary>
/// [C1 Delta Prefilter / 3.275.0 移植元] C1修復候補を「全チェッカーへ渡す前」に安く選別する
/// （図の C1 Delta Prefilter）。
///
/// <b>accept非変更・スコア不変が絶対条件</b>: 本フィルタは「UnifiedViolationChecker + isBetter が
/// <b>確実に却下する</b>候補」だけを早期に落とし、採用され得る候補は一切落とさない（＝退化不能）。
/// 最終採否は常に呼出側の checker + keep-best のまま。
///
///  - <see cref="HasActionableC1"/>: 盤面に不足窓が1つも無い＝すべての c1修復手は c1中立。c1オペレータは
///    c1違反セルにアンカーするため候補ゼロ＝no-op。よってクラスタ全体を1回のチェックで安全にスキップできる。
///  - <see cref="ScreenCell"/>: <b>単一セル候補</b>の速い判定。適用すると HARD（groupViol/pref/c3n）が
///    必ず増える、または盤面が変わらない候補は checker が辞書式(hard→weighted→total)で必ず却下するため
///    HARD_REJECT を返す。それ以外は NEUTRAL（判定を checker に委ねる）。<b>単一セル専用</b>＝相手の隣接日
///    に触れる bundle には使わない（その場合 makesForbiddenRun の per-cell 判定が陳腐化するため）。
///  - <see cref="C1Delta"/>: (staff,day)→newShift の<b>その職員のc1 fire 正味増減</b>（gain−loss を厳密勘定）。
///    <b>順位付け/診断専用</b>（accept を一切ゲートしない）。
///
/// [設計判断] 各オペレータの内側ループ（first-improvement 順に依存）への per候補配線は探索順序を変え得るため
///   本版では見送り（スコア不変を最優先）。ScreenCell/C1Delta は新規オペレータ・診断のための検証済み部品として
///   提供し、既存オペレータは従来どおり自前の canDo/wishLocked/makesForbiddenRun 判定を保持する。
/// </summary>
public static class C1DeltaPrefilter
{
    public enum Verdict
    {
        /// <summary>checker が確実に却下する（HARD増 or 無変化）＝安全に早期スキップ可。</summary>
        HardReject,

        /// <summary>改善し得る＝checker+keep-best に判定を委ねる。</summary>
        Neutral,
    }

    /// <summary>不足窓が無ければ c1オペレータは一律 no-op。クラスタ全体を安全にスキップできる。</summary>
    public static bool HasActionableC1(C1RepairIndexResult index) => index.HasActionable;

    /// <summary>
    /// 単一セル候補 (staff,day)→newShift を安く選別する。HardReject は「適用しても isBetter が必ず false」を
    /// 意味する（＝スキップしても採用結果は不変）。
    ///
    /// [3.279.0/外部レビューC1-01/02/12 移植元] 旧実装は per-family の存在判定（makesForbiddenRun=true／
    /// wishLocked希望外）で無条件却下していたが、それでは<b>正味では HARD が悪化しない候補</b>まで落として
    /// いた:
    ///  - C1-01: 新しい禁止連続を1件作りつつ既存の禁止連続を1件以上壊す手（c3n 正味0以下）＝checker は採用しうる。
    ///  - C1-02: 既に希望違反中のセルを別の非希望シフトへ変える手（pref 1→1 不変）＝checker は採用しうる。
    /// 反例をホストJVMで実証済み（screenCell=HARD_REJECT だが isBetter=true）。契約を sound にするため、
    /// <b>単一セル変更の全 HARD 族（groupViol/pref/c3n/covU）の正味Δを厳密に計算し、Δ&gt;0 のときだけ却下</b>する
    /// （cand.hard = best.hard + Δ が厳密に成立＝Δ&gt;0 なら isBetter は hard 比較で必ず false）。
    /// per-family の相殺（c3n+1 を covU−2 が打ち消す等）も正しく通す。C1-12 の座標境界チェックも追加。
    /// </summary>
    public static Verdict ScreenCell(Problem p, int[][] schedule, int staff, int day, int newShift)
    {
        if (staff < 0 || staff >= p.S || day < 0 || day >= p.T) return Verdict.HardReject; // [C1-12] 不正座標は非手
        if (newShift < 0 || newShift >= p.K) return Verdict.HardReject;
        // [3.279.1/レビューnit 移植元] 旧: normalizeSchedule で全盤面 O(S×T) をコピーしていたが、本判定が
        //   読むのは staff の1行と day の1列のみ。normalizeSchedule と同一の意味論（欠損セル→0=休へ
        //   パディング・範囲外値→-1）を読み取り時に局所適用し、コピーを行1本 O(T) に削減。
        int Cell(int i, int j)
        {
            int v = (i >= 0 && i < schedule.Length && j >= 0 && j < schedule[i].Length) ? schedule[i][j] : 0;
            return v is >= 0 && v < p.K ? v : -1;
        }

        int old = Cell(staff, day);
        if (old == newShift) return Verdict.HardReject; // 無変化＝isBetter は非改善で却下
        int delta = 0;
        // groupViol: checker は範囲内かつ担当外のセルのみ計上（-1 セルは対象外）。
        if (!p.CanDo(staff, newShift)) delta++;
        if (old is >= 0 && old < p.K && !p.CanDo(staff, old)) delta--;
        // pref: 実現可能希望の未充足（checker と同一）。既に違反中なら別シフトへ変えても不変（C1-02）。
        if (p.WishLocked(staff, day))
        {
            int w = p.Wish[staff][day];
            delta += (newShift != w ? 1 : 0) - (old != w ? 1 : 0);
        }

        // c3n: 行内の禁止連続 fire 数の正味差分（生成と破壊の両方を勘定＝C1-01）。
        var row = new int[p.T];
        for (int t = 0; t < p.T; t++) row[t] = Cell(staff, t);
        int before = StaffC3nFires(p, row);
        row[day] = newShift;
        delta += StaffC3nFires(p, row) - before;
        // covU: 到着側のみ勘定（≤0＝改善方向。c3n/pref の正味+1 を covU改善が相殺する候補を正しく通す）。
        //   離脱側の covU 悪化（≥0）は意図的に含めない: applyC1IndexChainRepair の branch(b) が
        //   「離脱で空いた covU 穴を玉突き連鎖で埋め直す」前提の候補であり、ここで落とすと連鎖経路が死ぬ
        //   （実テストで回帰確認済み）。正項の省略は Δ_computed ≤ Δ_true ＝ under-reject 方向のため
        //   「HardReject ⇒ checker が必ず却下」の契約は保たれる（離脱悪化の最終判定は checker）。
        int cntNew = 0;
        for (int i = 0; i < p.S; i++) if (Cell(i, day) == newShift) cntNew++;
        delta += p.CovUCell(newShift, day, cntNew + 1) - p.CovUCell(newShift, day, cntNew);
        return delta > 0 ? Verdict.HardReject : Verdict.Neutral;
    }

    /// <summary>
    /// 職員行 row の cons3n（禁止連続, HARD）fire 数。checker の forbidden 窓完全一致と同一意味論。
    /// [3.280.0 移植元] c3n「なぜ崩せないか」診断（V6PortAnalyzer.DiagnoseForbiddenRuns、フェーズ7で
    /// 移植予定）が正味増減の判定に共用する（Kotlin原本の <c>internal fun</c> と同じ可視性＝この
    /// アセンブリ内のどこからでも呼べる。ScreenCell と同じ row-local 差分計算を DRY に保つ）。
    /// </summary>
    internal static int StaffC3nFires(Problem p, int[] row)
    {
        int fires = 0;
        foreach (var c in p.Cons3n)
        {
            var seq = c.Seq;
            int d = seq.Length;
            if (d == 0 || d > p.T) continue;
            int j = 0;
            while (j <= p.T - d)
            {
                if (row[j] == seq[0])
                {
                    int z = 0;
                    for (int l = 1; l < d; l++) if (row[j + l] == seq[l]) z++;
                    if (z == d - 1) fires++;
                }
                j++;
            }
        }
        return fires;
    }

    /// <summary>
    /// (staff,day)→newShift としたときの、その職員の<b>c1 fire 数の正味増減</b>（負=改善）。
    /// newShift追加で解消する窓の gain と、旧シフト除去で新たに割れる窓の loss の<b>両方</b>を厳密に
    /// 勘定する（<see cref="C1RepairIndexResult.ExpectedGain"/> は gain のみの近似＝旧シフトが c1制約を
    /// 持つと自己破壊を見落とす）。順位付け/診断専用＝accept を一切ゲートしない（採否は常に呼出側の
    /// checker+keep-best）。
    /// </summary>
    public static int C1Delta(Problem p, int[][] schedule, int staff, int day, int newShift)
    {
        if (staff < 0 || staff >= p.S || day < 0 || day >= p.T || newShift < 0 || newShift >= p.K) return 0;
        var row = (int[])schedule[staff].Clone(); // 単一行のみ複製（c1 は per-staff）
        int old = row[day];
        if (old == newShift) return 0;
        int oldFires = StaffC1Fires(p, row, staff);
        row[day] = newShift;
        int newFires = StaffC1Fires(p, row, staff);
        return newFires - oldFires;
    }

    /// <summary>職員 staff の row における全 cons1 窓の不足 fire 数（checker の c1 走査と同一意味論）。</summary>
    private static int StaffC1Fires(Problem p, int[] row, int staff)
    {
        int fires = 0;
        foreach (var c in p.Cons1)
        {
            int x = c.ShiftIdx;
            if (x < 0 || x >= p.K || c.Day1 < 1 || c.Day1 > p.T || c.Day2 < 1) continue;
            if (!p.CanDo(staff, x)) continue;
            int j = 0;
            while (j <= p.T - c.Day1)
            {
                int z = 0;
                for (int l = 0; l < c.Day1; l++) if (row[j + l] == x) z++;
                if (z < c.Day2) fires++;
                j++;
            }
        }
        return fires;
    }
}
