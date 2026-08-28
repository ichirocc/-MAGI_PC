using MagiEngine.Model;

namespace MagiEngine.V6;

public static partial class V6HotfixPasses
{
    /// <summary>
    /// [C1 Index-driven Chain Repair / 3.276.0] <see cref="C1RepairIndex"/>/<see cref="C1DeltaPrefilter"/>
    /// を実際に駆動する新規C1修復オペレータ（図の Index→Prefilter→Operators→checker 経路を end-to-end に通す）。
    ///
    ///  1. <see cref="C1RepairIndex.Build"/> で不足窓を索引化（不足の重い窓から処理）。
    ///  2. 窓内の候補日を <see cref="C1DeltaPrefilter.C1Delta"/>（exact net c1 delta）昇順で並べ、
    ///     <see cref="C1DeltaPrefilter.ScreenCell"/> が NEUTRAL の候補だけ試す（無変化/groupViol/pref破り/c3n
    ///     は checker が確実に却下＝事前に落とす）。
    ///  3. 候補日を不足シフトへ直接移動。旧シフトを抜いて covU 穴が空くなら
    ///     <see cref="V6SearchOperators.FindCovUChain"/>（exclude=本人）の玉突き連鎖で埋め直す（手B と同型）。
    ///  4. 採否は必ず本物の <see cref="UnifiedViolationChecker"/> + <see cref="IsBetter"/>（hard→weighted→total）
    ///     + <see cref="PinBlockAttribution.BlocksImproving"/>（3.256.0の厳密ピン保護）＝keep-best・退化不能。
    ///
    /// [位置づけ・正直な限界] 生成する手は既存の手B/beam/exact と重複する（keep-best で無害）。本オペレータの
    ///   主眼は「index駆動の候補生成＋prefilter選別」という図の経路を load-bearing にすること。実C1削減の
    ///   純増は限定的（残差は3.263.0で確認した構造的壁が支配的）。既存オペレータには一切触れない＝退化不能。
    ///
    /// [C#化の注記] Kotlinの <c>windowLoop@ for (...) { ... break@windowLoop ... }</c>（外側の窓ループから、
    /// 内側の候補日ループの中で一気に抜ける）は C# に直接の等価物が無いため、このコードベース全体で確立済みの
    /// <c>goto WindowLoopDone; ... WindowLoopDone:</c> 慣用へ翻訳した（挙動の変化なし）。既定引数
    /// <c>seed: Long = 0x1C1D2L</c> はコンパイル時定数（16進数リテラル）のため、piece26/27 の
    /// <c>System.nanoTime()</c> 系既定値と異なり null許容化は不要＝C#側も通常の既定値として直接移せる。
    /// </summary>
    public static CyclicSwapResult ApplyC1IndexChainRepair(
        MagiState state, int[][] schedule, int maxPasses = 2,
        Func<bool>? shouldStop = null, long seed = 0x1C1D2L)
    {
        var stop = shouldStop ?? (() => false);
        // [3.326.0] 回数固定(lo==hi)だけが却下した候補試行を対象別に数える（緩和対象の提示用）。
        var pinBlocks = new PinBlockAttribution();
        var p = new Problem(state);
        var work = ScheduleUtil.NormalizeSchedule(schedule, p);
        var before = UnifiedViolationChecker.Check(state, work);
        if (p.Cons1.Count == 0 || before.Breakdown.GetValueOrDefault("c1", 0) == 0)
        {
            return new CyclicSwapResult(work, before.Total, before.Total, 0,
                new List<MirrorLog> { new MirrorLog(tag: "C1IndexRepair", message: "c1対象なし=スキップ") });
        }
        var rng = new JavaRandom(seed);
        var bestRep = before;
        var applied = 0;
        var chainUsed = 0;
        var screened = 0;
        // [3.279.0/外部レビューC1-08] 旧: pass 開始時の index を採用後も走査し続け、解消済み窓の再処理と
        //   「採用で新たに生じた窓は次 pass まで不可視」の両方が起きていた（maxPasses=2 の有限 pass で
        //   改善を取りこぼす）。1手採用するたびに窓ループを抜け、最新盤面から Index を再構築する。
        //   終了保証は IsBetter の厳密改善（hard→weighted→total 辞書式で単調減少）＋採用上限の安全弁。
        var maxAdoptions = maxPasses * 32;
        var capHit = false;
        while (!stop())
        {
            // [3.279.1/レビューnit] 採用上限は安全弁＝到達を黙って打ち切らずログへ明示する（silent cap 禁止）。
            if (applied >= maxAdoptions) { capHit = true; break; }
            var index = C1RepairIndex.Build(p, work);
            if (!index.HasActionable) break;
            var adopted = false;
            foreach (var w in index.Windows.OrderByDescending(x => x.Deficit))
            {
                if (stop()) break;
                var staff = w.Staff;
                var shift = w.Shift;
                var cands = Enumerable.Range(w.Start, w.WindowDays)
                    .Where(d =>
                    {
                        var neutral = C1DeltaPrefilter.ScreenCell(p, work, staff, d, shift) == C1DeltaPrefilter.Verdict.Neutral;
                        if (!neutral && work[staff][d] != shift) screened++;
                        return neutral;
                    })
                    // [3.277.0] 順位付けを exact net c1 delta へ（旧: index.ExpectedGain=gainのみの近似）。
                    //   C1Delta は旧シフト除去で別窓を割る loss も勘定＝自己破壊候補を後回しにする賢い順序。
                    //   負=改善なので昇順（最も改善する候補を先に試す）。順位のみ＝keep-best採否は不変。
                    .OrderBy(d => C1DeltaPrefilter.C1Delta(p, work, staff, d, shift))
                    .ToList();
                foreach (var d in cands)
                {
                    if (stop()) break;
                    var old = work[staff][d];
                    var trial = work.Copy2D();
                    trial[staff][d] = shift;
                    // (a) 直接移動のみで改善（旧シフトに余裕がある場合）。
                    var repDirect = UnifiedViolationChecker.Check(state, trial);
                    if (IsBetter(repDirect, bestRep) && !pinBlocks.BlocksImproving(p, work, trial))
                    {
                        work = trial; bestRep = repDirect; applied++; adopted = true;
                        goto WindowLoopDone;
                    }
                    // (b) 旧シフトを抜いて covU 穴が空くなら玉突き連鎖で埋め直す（exclude=本人で自己選択防止）。
                    var cntOldAfter = Enumerable.Range(0, p.S).Count(it => trial[it][d] == old);
                    if (old >= 0 && old < p.K && p.CovUCell(old, d, cntOldAfter) > 0)
                    {
                        var chain = V6SearchOperators.FindCovUChain(p, trial, old, d, rng, exclude: staff);
                        if (chain != null)
                        {
                            foreach (var mv in chain) trial[mv[0]][mv[1]] = mv[2];
                            var repChain = UnifiedViolationChecker.Check(state, trial);
                            if (IsBetter(repChain, bestRep) && !pinBlocks.BlocksImproving(p, work, trial))
                            {
                                work = trial; bestRep = repChain; applied++; chainUsed++; adopted = true;
                                goto WindowLoopDone;
                            }
                        }
                    }
                }
            }
            WindowLoopDone:
            if (!adopted) break;
        }
        var c1After = bestRep.Breakdown.GetValueOrDefault("c1", 0);
        return new CyclicSwapResult(
            work, before.Total, bestRep.Total, applied,
            // [3.279.1] screened は Index 再構築のたび同一候補を再判定し得る＝「延べ」件数（重複計上あり）。
            new List<MirrorLog>
            {
                new MirrorLog(tag: "C1IndexRepair",
                    message: $"index駆動C1修復: c1 {before.Breakdown.GetValueOrDefault("c1", 0)}->{c1After} " +
                        $"採用{applied}(連鎖{chainUsed}) prefilter除外(延べ){screened}" +
                        (capHit ? $" 採用上限{maxAdoptions}到達=打ち切り" : "")),
            },
            ObservedPinBlockedAttempts: pinBlocks.Attempts, PinBlocks: pinBlocks);
    }
}
