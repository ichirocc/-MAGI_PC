namespace MagiEngine.V6;

/// <summary>
/// [HF63] Infeasibility-Aware Adaptive Deprioritization — faithful port of Kotlin's
/// <c>Hf63Infeasibility.kt</c> (119 lines, entirely self-contained — a direct dependency of
/// <see cref="V6NativeOptimizer"/>'s RSI driver functions).
///
/// 目的（業務担当者の核心要望）: 「データ問題があっても最適化できるアルゴリズム」。各制約族の改善を
/// 追跡し、<see cref="INFEAS_STALL_ITERS"/> 反復のあいだ改善が無い族を「構造的に充足不能
/// （infeasible-likely）」と推定して学習する。改善を検出したら解除（self-correction）。
///
/// **この学習を何に使うか**（Kotlin原本 3.409.0/3.409.10 のコメント参照）:
///  - <see cref="InfeasibleBreakdownKeys"/> が RSI の focus 候補から充足困難と学習した族を外す。
///  - <see cref="InfeasibleFamilies"/> が残存分析（診断ログ）へ供給する。
///  - **目的関数の重みには一切触れない**（HF77 該当・本クラスの対象外）。
///
/// [Kotlin原本 3.409.10 のコメント、この移植でも維持] λ上限(penalty cap)の一式（<c>maxLam</c>/
/// <c>maxLamBatch</c>/<c>weightFactor</c>/<c>updateBatch</c>/<c>infeasibleCount</c>）は Kotlin
/// 側で既に撤去済み — 移植元の VBA が持っていた制約ごとの Lagrange 乗数 <c>gLam</c> はこの
/// エンジンに存在しない（固定重み <c>MirrorKeys.Weights</c>＋GLS penalty で動く）ため、「まだ
/// 配線していない」のではなく「書かれたままでは配線できない」設計だった。学習の半分（不可能性の
/// 推定と focus 回避への供給）だけを、Kotlin 原本のこの縮小済みクラスからそのまま移植する。
/// </summary>
public sealed class Hf63Infeasibility
{
    public const int INFEAS_STALL_ITERS = 5000;
    public const int N_CONSTRAINTS = 14;

    public static readonly IReadOnlyList<string> CNames = new[]
    {
        "C1", "C2", "C3", "C3n", "C3m", "C3mn", "C41", "C42",
        "CovU", "CovO", "Pref", "LimMin", "LimMax", "Apt",
    };

    /// <summary>UnifiedViolationChecker の breakdown キー → HF63 index（無いものは追跡しない）。</summary>
    public static readonly IReadOnlyDictionary<string, int> KeyToIndex = new Dictionary<string, int>
    {
        ["c1"] = 0, ["c2"] = 1, ["c3"] = 2, ["c3n"] = 3, ["c3m"] = 4, ["c3mn"] = 5,
        ["c41"] = 6, ["c42"] = 7, ["covU"] = 8, ["covO"] = 9, ["pref"] = 10,
        ["low"] = 11, ["high"] = 12,
    };

    private readonly int[] _gBestCurV = Enumerable.Repeat(int.MaxValue, N_CONSTRAINTS).ToArray();
    private readonly int[] _gLastImproveIter = new int[N_CONSTRAINTS];
    private readonly bool[] _gInfeasibleLikely = new bool[N_CONSTRAINTS];
    // [レビュー#5 3.213.0, Kotlin原本] focus 投入量ベースの停滞累積（UpdateFromBreakdownFocused 用）。
    //   gIter 時計と独立に「実際に focus した無改善ラウンドの概算反復数」だけを族ごとに積む。
    private readonly int[] _gFocusedStall = new int[N_CONSTRAINTS];

    public void Reset()
    {
        for (int c = 0; c < N_CONSTRAINTS; c++)
        {
            _gBestCurV[c] = int.MaxValue;
            _gLastImproveIter[c] = 0;
            _gInfeasibleLikely[c] = false;
            _gFocusedStall[c] = 0;
        }
    }

    /// <summary>制約 c の改善状況を追跡し、不可能性を判定する（VBA UpdateInfeasibilityState 等価）。</summary>
    public void Update(int c, int curV, int gIter)
    {
        if (c < 0 || c >= N_CONSTRAINTS) return;
        if (curV < _gBestCurV[c])
        {
            _gBestCurV[c] = curV;
            _gLastImproveIter[c] = gIter;
            if (_gInfeasibleLikely[c]) _gInfeasibleLikely[c] = false; // self-correction
        }
        else if (curV == 0)
        {
            // 一度でも充足(0)に到達した族は「不能」ではない。再違反に備え停滞カウンタを進めておく
            // （これを怠ると、0到達後に摂動で再違反した瞬間 gIter-旧改善 が即 STALL を超え、
            //  解けた族を誤って infeasible 判定し RSI focus から外してしまう）。
            _gLastImproveIter[c] = gIter;
        }
        else if (gIter - _gLastImproveIter[c] >= INFEAS_STALL_ITERS)
        {
            // curV>0 かつ STALL 反復改善なし。focus 回避/診断のため deprioritize する。
            _gInfeasibleLikely[c] = true; // 構造的下限推定 → deprioritize
        }
    }

    /// <summary>
    /// UnifiedViolationChecker の breakdown から更新（族→indexへマップ）。注: 全族の停滞を無差別に
    /// 加算する旧セマンティクス。focus の概念が無い呼出元用に温存。RSI の focus 選択には
    /// <see cref="UpdateFromBreakdownFocused"/> を使う（レビュー#5）。
    /// </summary>
    public void UpdateFromBreakdown(IReadOnlyDictionary<string, int> breakdown, int gIter)
    {
        foreach (var (key, idx) in KeyToIndex)
            Update(idx, breakdown.TryGetValue(key, out var v) ? v : 0, gIter);
    }

    /// <summary>
    /// [レビュー#5 3.213.0, Kotlin原本] focus 投入量ベースの更新。実際に探索資源を投入した族
    /// （focusedKey=直前ラウンドの focus）だけ停滞を加算し、他族は改善/0到達の追跡
    /// （self-correction）のみ行う。旧 UpdateFromBreakdown は「covU に focus が張り付いている間、
    /// 一度も focus されなかった族」まで約3ラウンドで infeasible 判定していた（HARD/SOFT を問わず
    /// 「試していない族を不能と推定しない」のが本更新の狙い）。effortIters=そのラウンドに focus へ
    /// 投入した概算反復数（RSI 側の粒度補正と同じ）。
    /// </summary>
    public void UpdateFromBreakdownFocused(IReadOnlyDictionary<string, int> breakdown, string? focusedKey, int effortIters)
    {
        foreach (var (key, idx) in KeyToIndex)
        {
            int curV = breakdown.TryGetValue(key, out var v) ? v : 0;
            if (curV < _gBestCurV[idx])
            {
                _gBestCurV[idx] = curV;
                _gFocusedStall[idx] = 0;
                if (_gInfeasibleLikely[idx]) _gInfeasibleLikely[idx] = false; // self-correction
            }
            else if (curV == 0)
            {
                _gFocusedStall[idx] = 0; // 充足済みの族は「不能」ではない（Update() の 0 到達分岐と同義）
            }
            else if (key == focusedKey)
            {
                _gFocusedStall[idx] += effortIters;
                if (_gFocusedStall[idx] >= INFEAS_STALL_ITERS) _gInfeasibleLikely[idx] = true;
            }
            // focus外かつ非改善: 何もしない＝停滞時計を進めない（探索資源を投入していないため）。
        }
    }

    public bool IsInfeasibleLikely(int c) => c >= 0 && c < N_CONSTRAINTS && _gInfeasibleLikely[c];

    /// <summary>構造的に充足不能と推定された制約族名（診断/ログ用）。</summary>
    public IReadOnlyList<string> InfeasibleFamilies() =>
        Enumerable.Range(0, N_CONSTRAINTS).Where(i => _gInfeasibleLikely[i]).Select(i => CNames[i]).ToList();

    /// <summary>infeasible-likely な族の breakdown キー集合（探索の focus 回避に使用）。</summary>
    public IReadOnlySet<string> InfeasibleBreakdownKeys() =>
        KeyToIndex.Where(kv => _gInfeasibleLikely[kv.Value]).Select(kv => kv.Key).ToHashSet();
}
