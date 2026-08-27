using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// [HF528/537/540/541 PORTED FROM WEB 2026-06-11] Faithful port of Kotlin's <c>V6LateOperators</c>
/// object — 後期演算子(EarlyChainフック用)。
///
/// Web <c>runRectSwap2</c>(magi_v6_web.html L10637-) / <c>runC1BlockN</c>(L10753-) の忠実移植。
///  - RectSwap2 [HF528]: 2人×多日(2..5日)の矩形交換。同日内の入替なので被覆(covU)保存。
///      [HF540] ドナー狙い撃ち(D): i1 が個人別回数(下限)違反者のとき 70% で不足シフト kd の
///      最多保持者 i2 を貪欲選択し、窓を「i2 が kd を持ち i1 が持たない転送日」を含む位置へ寄せる。
///  - C1BlockN [HF541 = VBA HF219 逆輸入]: c1 違反窓内の連続 blen=min(不足,5) 日を、各日別ドナー
///      (既選択優先→kd 最多保持)との同日交換で一括充足し窓を割る(最大6者=i1+5ドナー)。
///
/// 採否ゲート [HF537 同等]:
///  base  = (hard減) or (hard同 かつ soft減)
///  boost = base不成立でも Δc1&lt;0 ∧ Δhard&lt;=0 ∧ Δ(200*high+120*low)&lt;=0 ∧ ΔSOFT&lt;=0 なら採用
///  ※ native 採点では LimMin/LimMax(low/high)=hard2 のため lim 節は hard 条件に実質内包されるが、
///    Web ゲート(HF151系 200/120)との同値性を明示するため breakdown の low/high で同式を保持する。
///  ※ native の SOFT は無重み合計(soft)。Web の weighted ΔSOFT に対する保守的同等条件として ΔSOFT&lt;=0 を用いる。
///  不採用は全 revert。Web の _lockActive/_separable(ロック機能)は native 未実装のため対象外(全員 active)。
///
/// 統合点: V6NativeOptimizer の RSI 系(runRsi 各ラウンド後 / RSI++ Refine 後)。Web の
/// 「内部V5の reheat 停滞時 EarlyChain(L11705-)」に対応する native の停滞境界。
/// フラグ: optFlags.rectSwap(既定ON [HF532])を Rect/BlkN で共用(Web L11710-11711 と同じ)。
///
/// [C#移植メモ] Kotlin の <c>object V6LateOperators</c>（メンバは修飾子なし＝public）に対応させ、
/// <c>public static class</c> とする（<see cref="SaOptimizer"/>/<see cref="GlsPenalty"/>と同型）。
/// この関数は非同期(coroutines/TPL)に一切関与しない同期的ヘルパーのため（サブフェーズ5c/5dの対象外）、
/// <c>deadlineMs</c> をそのまま <c>long</c> で受ける（<see cref="CancellationToken"/> は導入しない）。
/// </summary>
public static class V6LateOperators
{
    /// <summary>Faithful port of Kotlin's <c>LateImproveResult</c> data class.</summary>
    public sealed record LateImproveResult(
        int[][] Schedule,
        ViolationReport Report,
        int Chain3,
        int Chain4,
        int Rect,
        int BlkN,
        IReadOnlyList<MirrorLog> Logs);

    /// <summary>
    /// state.Extras 経由の optFlags.&lt;name&gt; 読取(JSONObject/Map の両形に対応)。未設定は def。
    /// [C#移植メモ] Kotlin の <c>extras: Map&lt;String, Any?&gt;</c> は org.json.JSONObject/Map の
    /// いずれも格納しうるが、C# の <see cref="MagiState.Extras"/> は常に
    /// <see cref="System.Text.Json.JsonElement"/> なので分岐は不要。<see cref="JsonHelpers.OptBoolean"/>
    /// （"optFlags" オブジェクトが無い/nameが無い/真偽値でなければ def を返す寛容な読取）へ委譲する。
    /// </summary>
    public static bool OptFlagBool(MagiState state, string name, bool def)
    {
        if (!state.Extras.TryGetValue("optFlags", out var of)) return def;
        return JsonHelpers.OptBoolean(of, name, def);
    }

    /// <summary>入力 schedule は変更しない(コピーに適用)。採用が無ければ入力 report をそのまま返す。</summary>
    public static LateImproveResult Improve(
        MagiState state,
        int[][] schedule,
        ViolationReport report,
        JavaRandom rng,
        long deadlineMs,
        bool rectEnabled = true,
        int chainTry3 = 20,
        int chainTry4 = 12,
        int rectTry = 12,
        int blkTry = 8)
    {
        var p = ScheduleUtil.CachedProblem(state);
        var sched = schedule.Copy2D();
        var logs = new List<MirrorLog>();
        var cur = report;

        bool TimeUp() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() >= deadlineMs;
        int Lim(ViolationReport r) =>
            200 * r.Breakdown.GetValueOrDefault("high", 0) + 120 * r.Breakdown.GetValueOrDefault("low", 0);
        int C1Count(ViolationReport r) => r.Breakdown.GetValueOrDefault("c1", 0);

        // 採否ゲート [HF537]: 採用なら cur 更新 + ログ。不採用なら false(呼び元で revert)。
        bool Gate(string tag, string detail)
        {
            var nv = UnifiedViolationChecker.Check(state, sched);
            // [3.287.0 keep-best統一→3.335.0 委譲] 判定は `BetterReport`（hard→weightedScore→total）へ。
            //   3.287.0 は第2キーだけ weightedScore へ寄せて**第3キー total へ落ちる分岐を書き忘れて**おり、
            //   weighted 同値・total 改善の候補（例: c1×1 と c42×30 は weighted 30 で同値・total は29違う。
            //   [外部レビューM2] c1 重みは 3.409.24 で 15→30 に変更済みのため数値を実装へ合わせて訂正）を
            //   捨てていた。boost 側の soft<= ガードは「生カウントも悪化させない」追加条件として従来どおり残す。
            var baseOk = UnifiedViolationChecker.BetterReport(nv, cur);
            // [3.336.0/敵対レビュー H3] boost に weightedScore 非増を要求する。`Lim` は 200×high+120×low で
            //   **目的関数の重み(high45 < low90)と大小が逆**なので、high−1/low+1 の入れ替えは lim を下げつつ
            //   weighted を +45 悪化させられた。反例: c1 5→4・high 2→1・low 1→2 は
            //   lim 520→440 ✓ / soft件数 8→7 ✓ / c1 減 ✓ を全部満たしつつ weighted 255→285。
            //   これで boost は「hard・weighted・件数のどれも悪化させずに c1 だけ減らす」横移動に限定される。
            var boost = !baseOk &&
                C1Count(nv) < C1Count(cur) &&
                nv.Hard <= cur.Hard &&
                nv.WeightedScore <= cur.WeightedScore &&
                Lim(nv) <= Lim(cur) &&
                nv.Soft <= cur.Soft;
            if (baseOk || boost)
            {
                cur = nv;
                logs.Add(new MirrorLog(
                    tag: tag,
                    message: $"{detail}{(boost ? "(c1ブースト)" : "")} hard={nv.Hard} soft={nv.Soft}"));
                return true;
            }
            return false;
        }

        var rect = 0;
        var blkN = 0;

        var chain3 = 0;
        var chain4 = 0;

        // ── 共有ヘルパ(ChainSwap3/4) ──
        var sN = p.S;
        var tN = p.T;
        var kN = p.K;
        var alw = new int[sN][];
        for (var i = 0; i < sN; i++) alw[i] = p.AllowedShiftsForStaff(i);

        int[][] SsnOf(int[][] sc)
        {
            var a = new int[sN][];
            for (var i = 0; i < sN; i++) a[i] = new int[kN];
            for (var i = 0; i < sN; i++)
            {
                for (var j = 0; j < tN; j++)
                {
                    var kk = sc[i][j];
                    if (kk is >= 0 && kk < kN) a[i][kk]++;
                }
            }
            return a;
        }

        List<int> BaseViolators(int[][] ssn)
        {
            var v = new List<int>();
            for (var i = 0; i < sN; i++)
            {
                var bad = false;
                for (var kk = 0; kk < kN; kk++)
                {
                    if (ssn[i][kk] < p.RangeLo[i][kk] || ssn[i][kk] > p.RangeHi[i][kk]) { bad = true; break; }
                }
                if (bad) v.Add(i);
            }
            return v;
        }

        // [HF354→三連/五連など任意長対応] c3n前後チェック: 循環後 newK が前後日と c3n を作るなら true。
        //   Problem.MakesForbiddenRun が任意長ルール(旧: 長さ2の禁止連続のみ)を一般判定する。
        bool C3nHit(int i, int j, int newK) => p.MakesForbiddenRun(sched, i, j, newK);

        // [HF411 Level Zero準拠] 平準化対象シフト: need定義済み かつ 担当可能2名以上(番号非依存=全シフト同等)
        bool IsBalanceable(int bk)
        {
            if (bk < 0 || bk >= kN) return false;
            // [3.309.0] 旧実装は生 state の need1 / needDay1 しか見ず、**P2 だけで需要が定義された
            //   シフトを「需要なし」と判定**して Chain 系の候補から丸ごと外していた（3.173.0 で
            //   CoverageDiagnosis に対して直したのと同型の取り残し）。判定は source of truth の
            //   Problem.CovUCell に委ねる＝誰も配置しない状態(got=0)で不足が出るならその日は需要がある。
            var hasNeed = false;
            for (var j = 0; j < tN; j++)
            {
                if (p.CovUCell(bk, j, 0) > 0) { hasNeed = true; break; }
            }
            if (!hasNeed) return false;
            var elig = 0;
            for (var i = 0; i < sN; i++)
            {
                if (Array.IndexOf(alw[i], bk) >= 0) { if (++elig >= 2) return true; }
            }
            return false;
        }

        // 採否(Chain系)。[3.309.0] 旧実装は weightedScore 純改善のみで **HARD を一切見ていなかった**
        //   （すぐ上の Gate() は 3.287.0 で hard 優先へ統一済みなのに、ここだけ 3.287.0/3.289.0 の
        //   全サイト掃討から漏れていた）。実害は到達不能に近い＝同日3〜4者交換は最大4セルしか変えず、
        //   soft から得られる weighted 改善は現実的に数百（low=90/high=45/c1=15）に対し、HARD の最小重みは
        //   c3n=7000 なので HARD を増やす受理は成立しない。それでも契約は揃える（将来 HF77 で HARD 重みが
        //   下がったときに静かに壊れる罠を残さない）。[3.335.0] 判定は `BetterReport` へ委譲＝第3キー total
        //   まで見る（3.309.0 は hard→weightedScore を手書きで複製しており total へ落ちなかった）。
        bool GateW()
        {
            var nv = UnifiedViolationChecker.Check(state, sched);
            var ok = UnifiedViolationChecker.BetterReport(nv, cur); // [3.335.0] Gate と同じく BetterReport へ委譲（第3キー total まで見る）
            if (ok) { cur = nv; return true; }
            return false;
        }

        // ───────── ChainSwap3 [HF354-358] 3者同日循環 ─────────
        {
            var ssn = SsnOf(sched);
            var violators = BaseViolators(ssn);

            // [HF356] c3単発(必須2連続の孤立が3件以上)のスタッフを追加
            var c3Mand = p.Cons3
                .Where(c => c.Seq.Length == 2 && c.Seq[0] == c.Seq[1])
                .Select(c => c.Seq[0])
                .ToList();
            if (c3Mand.Count > 0)
            {
                var vset = violators.ToHashSet();
                for (var i = 0; i < sN; i++)
                {
                    if (vset.Contains(i)) continue;
                    var iso = 0;
                    foreach (var mk in c3Mand)
                    {
                        for (var j = 0; j < tN; j++)
                        {
                            if (sched[i][j] != mk) continue;
                            var prevSame = j > 0 && sched[i][j - 1] == mk;
                            var nextSame = j < tN - 1 && sched[i][j + 1] == mk;
                            if (!prevSame && !nextSame) iso++;
                        }
                    }
                    if (iso >= 3) violators.Add(i);
                }
            }

            // [HF357] 曜日偏在(労働シフトの曜日σ>1.0)のスタッフを追加
            {
                var dow0 = p.Dow0; // 既存の Problem.Dow0 は Kotlin の
                                   // `runCatching { LocalDate.parse(state.startDate).dayOfWeek.value % 7 }.getOrDefault(0)`
                                   // と数値的に完全一致する（Problem.cs のコメント参照）ので再計算せず流用する。
                var vset = violators.ToHashSet();
                for (var i = 0; i < sN; i++)
                {
                    if (vset.Contains(i)) continue;
                    var wd = new int[7];
                    for (var j = 0; j < tN; j++)
                    {
                        var kk = sched[i][j];
                        if (kk is >= 0 && kk < kN && IsBalanceable(kk)) wd[(dow0 + j) % 7]++;
                    }
                    var avg = wd.Sum() / 7.0;
                    var vs = 0.0;
                    foreach (var x in wd) vs += (x - avg) * (x - avg);
                    if (Math.Sqrt(vs / 7.0) > 1.0) violators.Add(i);
                }
            }

            // [HF358] シフト集中(担当可能者間σ>0.8 で平均から1σ超)のスタッフを追加
            {
                var vset = violators.ToHashSet();
                for (var kk = 0; kk < kN; kk++)
                {
                    if (!IsBalanceable(kk)) continue;
                    var elig = new List<(int Staff, int Count)>();
                    for (var i = 0; i < sN; i++)
                    {
                        if (Array.IndexOf(alw[i], kk) >= 0) elig.Add((i, ssn[i][kk]));
                    }
                    if (elig.Count < 2) continue;
                    var avg = elig.Sum(e => e.Count) / (double)elig.Count;
                    var vs = 0.0;
                    foreach (var (_, c) in elig) vs += (c - avg) * (c - avg);
                    var std = Math.Sqrt(vs / elig.Count);
                    if (std <= 0.8) continue;
                    foreach (var (i, c) in elig)
                    {
                        if (vset.Contains(i)) continue;
                        if (Math.Abs(c - avg) > std) { violators.Add(i); vset.Add(i); }
                    }
                }
            }

            var tr = 0;
            while (tr < chainTry3)
            {
                tr++;
                if (TimeUp()) break;
                // [HF355] 3段階選択: 違反2+経由1(前半50%) / 違反1(〜80%) / 全ランダム(残り)
                int i1, i2, i3;
                if (violators.Count >= 2 && tr <= (int)(chainTry3 * 0.5))
                {
                    var a = rng.NextInt(violators.Count);
                    var b = rng.NextInt(violators.Count);
                    if (b == a) b = (b + 1) % violators.Count;
                    i1 = violators[a]; i3 = violators[b]; i2 = rng.NextInt(sN);
                }
                else if (violators.Count > 0 && tr <= (int)(chainTry3 * 0.8))
                {
                    i1 = violators[rng.NextInt(violators.Count)]; i2 = rng.NextInt(sN); i3 = rng.NextInt(sN);
                }
                else
                {
                    i1 = rng.NextInt(sN); i2 = rng.NextInt(sN); i3 = rng.NextInt(sN);
                }
                var j = rng.NextInt(tN);
                if (i1 == i2 || i2 == i3 || i1 == i3) continue;
                var k1 = sched[i1][j]; var k2 = sched[i2][j]; var k3 = sched[i3][j];
                if (k1 < 0 || k2 < 0 || k3 < 0) continue;
                if (k1 == k2 || k2 == k3 || k1 == k3) continue;
                if (Array.IndexOf(alw[i1], k2) < 0 || Array.IndexOf(alw[i2], k3) < 0 || Array.IndexOf(alw[i3], k1) < 0) continue;
                if (p.WishLocked(i1, j) || p.WishLocked(i2, j) || p.WishLocked(i3, j)) continue;
                if (C3nHit(i1, j, k2) || C3nHit(i2, j, k3) || C3nHit(i3, j, k1)) continue;
                sched[i1][j] = k2; sched[i2][j] = k3; sched[i3][j] = k1;
                if (GateW()) chain3++;
                else { sched[i1][j] = k1; sched[i2][j] = k2; sched[i3][j] = k3; }
            }
        }

        // ───────── ChainSwap4 [HF360] 4者同日循環(3-wayの補完) ─────────
        {
            var violators = BaseViolators(SsnOf(sched));
            var tr = 0;
            while (tr < chainTry4)
            {
                tr++;
                if (TimeUp()) break;
                int i1, i2, i3, i4;
                if (violators.Count >= 2 && tr <= (int)(chainTry4 * 0.7))
                {
                    var a = rng.NextInt(violators.Count);
                    var b = rng.NextInt(violators.Count);
                    if (b == a) b = (b + 1) % violators.Count;
                    i1 = violators[a]; i3 = violators[b]; i2 = rng.NextInt(sN); i4 = rng.NextInt(sN);
                }
                else
                {
                    i1 = rng.NextInt(sN); i2 = rng.NextInt(sN); i3 = rng.NextInt(sN); i4 = rng.NextInt(sN);
                }
                var j = rng.NextInt(tN);
                if (new HashSet<int> { i1, i2, i3, i4 }.Count < 4) continue;
                var k1 = sched[i1][j]; var k2 = sched[i2][j]; var k3 = sched[i3][j]; var k4 = sched[i4][j];
                if (k1 < 0 || k2 < 0 || k3 < 0 || k4 < 0) continue;
                if (new HashSet<int> { k1, k2, k3, k4 }.Count < 4) continue;
                if (Array.IndexOf(alw[i1], k2) < 0 || Array.IndexOf(alw[i2], k3) < 0 ||
                    Array.IndexOf(alw[i3], k4) < 0 || Array.IndexOf(alw[i4], k1) < 0) continue;
                if (p.WishLocked(i1, j) || p.WishLocked(i2, j) ||
                    p.WishLocked(i3, j) || p.WishLocked(i4, j)) continue;
                if (C3nHit(i1, j, k2) || C3nHit(i2, j, k3) || C3nHit(i3, j, k4) || C3nHit(i4, j, k1)) continue;
                sched[i1][j] = k2; sched[i2][j] = k3; sched[i3][j] = k4; sched[i4][j] = k1;
                if (GateW()) chain4++;
                else { sched[i1][j] = k1; sched[i2][j] = k2; sched[i3][j] = k3; sched[i4][j] = k4; }
            }
        }

        // ───────── RectSwap2 [HF528+540] ─────────
        if (rectEnabled)
        {
            var s = p.S;
            var t = p.T;
            var k = p.K;
            // 個人別回数の現況(Web ssnR 相当)と違反者(Web 同様、ループ前に一度だけ算出)
            var ssn = new int[s][];
            for (var i = 0; i < s; i++) ssn[i] = new int[k];
            for (var i = 0; i < s; i++)
            {
                for (var j = 0; j < t; j++)
                {
                    var kk = sched[i][j];
                    if (kk is >= 0 && kk < k) ssn[i][kk]++;
                }
            }
            var violators = new List<int>();
            for (var i = 0; i < s; i++)
            {
                var bad = false;
                for (var kk = 0; kk < k; kk++)
                {
                    if (ssn[i][kk] < p.RangeLo[i][kk] || ssn[i][kk] > p.RangeHi[i][kk]) { bad = true; break; }
                }
                if (bad) violators.Add(i);
            }
            var tr = 0;
            while (tr < rectTry)
            {
                tr++;
                if (TimeUp()) break;
                var i1 = violators.Count > 0 && tr <= (int)(rectTry * 0.7)
                    ? violators[rng.NextInt(violators.Count)] : rng.NextInt(s);
                // [HF540] ドナー狙い撃ち
                var i2 = -1;
                var dJd = -1;
                if (violators.Contains(i1) && rng.NextDouble() < 0.7)
                {
                    var dKd = -1;
                    for (var kk = 0; kk < k; kk++)
                    {
                        if (ssn[i1][kk] < p.RangeLo[i1][kk]) { dKd = kk; break; }
                    }
                    if (dKd >= 0)
                    {
                        var bestC = -1;
                        var st0 = rng.NextInt(s);
                        for (var o = 0; o < s; o++)
                        {
                            var c2 = (st0 + o) % s;
                            if (c2 == i1) continue;
                            if (ssn[c2][dKd] > bestC) { bestC = ssn[c2][dKd]; i2 = c2; }
                        }
                        if (i2 >= 0 && bestC > 0)
                        {
                            var dds = new List<int>();
                            for (var dj = 0; dj < t; dj++)
                            {
                                if (sched[i2][dj] == dKd && sched[i1][dj] != dKd) dds.Add(dj);
                            }
                            dJd = dds.Count > 0 ? dds[rng.NextInt(dds.Count)] : -1;
                        }
                        if (dJd < 0) i2 = -1;
                    }
                }
                var dMode = i2 >= 0;
                if (!dMode) i2 = rng.NextInt(s);
                if (i1 == i2) continue;
                var len = 2 + rng.NextInt(4); // 2..5日
                int j1;
                if (dMode)
                {
                    var off = rng.NextInt(len);
                    j1 = Math.Clamp(dJd - off, 0, Math.Max(0, t - len));
                }
                else
                {
                    j1 = rng.NextInt(Math.Max(1, t - len + 1));
                }
                var j2 = Math.Min(t - 1, j1 + len - 1);
                var b1 = p.AllowedShiftsForStaff(i1);
                var b2 = p.AllowedShiftsForStaff(i2);
                var ok = true;
                var anyDiff = false;
                var ks1 = new int[j2 - j1 + 1];
                var ks2 = new int[j2 - j1 + 1];
                var x = 0;
                var j = j1;
                while (j <= j2)
                {
                    if (p.WishLocked(i1, j) || p.WishLocked(i2, j)) { ok = false; break; } // 希望(pref=HARD)破壊回避
                    var kk1 = sched[i1][j];
                    var kk2 = sched[i2][j];
                    if (kk1 < 0 || kk2 < 0) { ok = false; break; }
                    if (kk1 != kk2) anyDiff = true;
                    if (Array.IndexOf(b1, kk2) < 0 || Array.IndexOf(b2, kk1) < 0) { ok = false; break; } // 群互換(双方向)
                    ks1[x] = kk1; ks2[x] = kk2;
                    x++; j++;
                }
                if (!ok || !anyDiff) continue;
                // 適用(同日内交換=被覆保存)
                x = 0; j = j1;
                while (j <= j2) { sched[i1][j] = ks2[x]; sched[i2][j] = ks1[x]; x++; j++; }
                if (Gate("RectSwap2", $"矩形交換採用{(dMode ? "(D)" : "")} i={i1}<->{i2} j=[{j1}..{j2}]"))
                {
                    rect++;
                }
                else
                {
                    // revert
                    x = 0; j = j1;
                    while (j <= j2) { sched[i1][j] = ks1[x]; sched[i2][j] = ks2[x]; x++; j++; }
                }
            }
        }

        // ───────── C1BlockN [HF541 = VBA HF219] ─────────
        if (rectEnabled)
        {
            var rules = p.Cons1;
            if (rules.Count > 0)
            {
                var s = p.S;
                var t = p.T;
                var tr = 0;
                while (tr < blkTry)
                {
                    tr++;
                    if (TimeUp()) break;
                    var c = rules[rng.NextInt(rules.Count)];
                    var kd = c.ShiftIdx;
                    var days = c.Day1;
                    var need = c.Day2;
                    if (days < 2 || need <= 0 || days > t) continue;
                    var i1 = rng.NextInt(s);
                    if (Array.IndexOf(p.AllowedShiftsForStaff(i1), kd) < 0) continue;
                    // 違反窓を1つ探す(ランダム起点巡回)
                    var wN = t - days + 1;
                    var w0 = rng.NextInt(Math.Max(1, wN));
                    var w = -1;
                    var have = 0;
                    for (var o = 0; o < wN; o++)
                    {
                        var ws = (w0 + o) % wN;
                        var cnt = 0;
                        for (var j = ws; j < ws + days; j++)
                        {
                            if (sched[i1][j] == kd) cnt++;
                        }
                        if (cnt < need) { w = ws; have = cnt; break; }
                    }
                    if (w < 0) continue;
                    var blen = Math.Min(need - have, 5);
                    if (blen < 1) continue;
                    // 窓内の連続 blen 日(i1 が非kd かつ 希望なし)
                    var j1 = -1;
                    for (var s0 = w; s0 <= w + days - blen; s0++)
                    {
                        var okc = true;
                        for (var d = 0; d < blen; d++)
                        {
                            var j = s0 + d;
                            if (p.WishLocked(i1, j) || sched[i1][j] == kd) { okc = false; break; }
                        }
                        if (okc) { j1 = s0; break; }
                    }
                    if (j1 < 0) continue;
                    // 各日のドナー貪欲選択(既選択優先 → 新規は kd 総保持数最多)
                    var oldKs = new int[blen];
                    var donors = new int[blen];
                    var used = new List<int>();
                    var fail = false;
                    for (var d = 0; d < blen; d++)
                    {
                        var j = j1 + d;
                        var k1 = sched[i1][j];
                        if (k1 < 0) { fail = true; break; }
                        var pick = -1;
                        foreach (var u in used)
                        {
                            if (sched[u][j] == kd && !p.WishLocked(u, j) &&
                                Array.IndexOf(p.AllowedShiftsForStaff(u), k1) >= 0)
                            {
                                pick = u;
                                break;
                            }
                        }
                        if (pick < 0)
                        {
                            if (used.Count >= 5) { fail = true; break; }
                            var bestC = -1;
                            var st0 = rng.NextInt(s);
                            for (var o = 0; o < s; o++)
                            {
                                var c2 = (st0 + o) % s;
                                if (c2 == i1 || used.Contains(c2)) continue;
                                if (sched[c2][j] != kd) continue;
                                if (p.WishLocked(c2, j)) continue;
                                if (Array.IndexOf(p.AllowedShiftsForStaff(c2), k1) < 0) continue;
                                var cnt2 = 0;
                                for (var jj = 0; jj < t; jj++)
                                {
                                    if (sched[c2][jj] == kd) cnt2++;
                                }
                                if (cnt2 > bestC) { bestC = cnt2; pick = c2; }
                            }
                            if (pick >= 0) used.Add(pick);
                        }
                        if (pick < 0) { fail = true; break; }
                        oldKs[d] = k1; donors[d] = pick;
                    }
                    if (fail) continue;
                    // 一括適用(i1: oldK->kd / donor: kd->oldK = 同日交換で被覆保存)
                    for (var d = 0; d < blen; d++)
                    {
                        var j = j1 + d;
                        sched[i1][j] = kd;
                        sched[donors[d]][j] = oldKs[d];
                    }
                    if (Gate("C1BlockN", $"N者間採用 i={i1} kd={kd} j=[{j1}..{j1 + blen - 1}] 者={used.Count + 1}"))
                    {
                        blkN++;
                    }
                    else
                    {
                        // revert
                        for (var d = 0; d < blen; d++)
                        {
                            var j = j1 + d;
                            sched[i1][j] = oldKs[d];
                            sched[donors[d]][j] = kd;
                        }
                    }
                }
            }
        }

        return new LateImproveResult(sched, cur, chain3, chain4, rect, blkN, logs);
    }
}
