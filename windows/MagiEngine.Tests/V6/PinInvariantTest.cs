using MagiEngine.Model;
using MagiEngine.Tests.Fixtures;
using MagiEngine.V6;
// System.Range (built-in C# 8+ slice type, brought into scope by the SDK's implicit
// `global using global::System;`) collides by simple name with MagiEngine.Model.Range.
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [3.338.0/敵対レビュー A2, フェーズ6ピース30] <c>PinInvariantTest.kt</c>の忠実な移植。
///
/// **ピンの不変条件を「読んで確認した」から「試験が主張する」へ上げる**。
///
/// この最適化器は2種類のピンを守ると宣言している。
///  1. **実現可能な希望**（<see cref="ScheduleUtil.WishLocked"/> = 希望があり、かつ担当できる）
///     … 入口の <c>InitialAssignment</c>/HF67HardRepair が盤面へ載せ、以後どの手も触らない
///  2. **厳密な回数固定**（<c>StaffRange</c> の Lo==Hi）… <c>ExactPinRegression</c> が目標から遠ざかる手を却下する
///
/// どちらも各パスがローカルに <c>movable</c> / <c>ExactPinRegression</c> を呼ぶ形で守られており、
/// **1箇所書き忘れても誰も気づけない**（3.334.0 の SA 近傍・3.336.0 の <c>strongPerturbFlat</c> が実例）。
/// ここで後処理チェーン全体を通した不変条件として固定する。
///
/// 実データでの確認（3.338.0 時点）: golden/real/user とも固定 84/81/81 件で**動かされた 0 件**。
///
/// **この試験が捕まえる範囲（実測して確かめた）**:
///  - 14箇所の <c>movable</c> を**全部**潰す → 落ちる ✓
///  - <c>ExactPinRegression</c> を無効化する → 落ちる ✓
///  - <c>movable</c> を**1箇所だけ**潰す → **落ちない**。希望を破ると pref(9000/HARD) が増え、
///    そのパスの <c>IsBetter</c> が必ず却下するため。つまり不変条件を最終的に強制しているのは**採否**で、
///    <c>movable</c> は「必ず却下される手を作らない」ための事前フィルタ。3.334.0 の SA 近傍の欠落が
///    「誤った勤務表」でなく「反復の空振り」で済んだのは同じ理由。
///  → よってこの試験は**不変条件**を守るが、**個々のガードの有無**は守らない。ガードの網羅は
///    grep で数える（教訓#31）。
/// </summary>
public class PinInvariantTest
{
    private static IReadOnlyList<(int Staff, int Day)> LockedCells(Problem p)
    {
        var result = new List<(int, int)>();
        for (var i = 0; i < p.S; i++)
            for (var j = 0; j < p.T; j++)
                if (p.WishLocked(i, j)) result.Add((i, j));
        return result;
    }

    /// <summary>厳密ピン(lo==hi)のうち、入口で目標どおりだったものを列挙する。</summary>
    private static IReadOnlyList<(int Staff, int Shift, int Target)> ExactPins(Problem p, int[][] board)
    {
        var result = new List<(int, int, int)>();
        for (var i = 0; i < p.S; i++)
        {
            var cnt = new int[p.K];
            for (var j = 0; j < p.T; j++)
            {
                var v = board[i][j];
                if (v >= 0 && v < p.K) cnt[v]++;
            }
            for (var k = 0; k < p.K; k++)
            {
                var lo = p.RangeLo[i][k];
                var hi = p.RangeHi[i][k];
                if (lo != int.MinValue && hi != int.MaxValue && lo == hi && cnt[k] == lo)
                    result.Add((i, k, lo));
            }
        }
        return result;
    }

    private static void AssertPinsHeld(string label, MagiState st)
    {
        var p = new Problem(st);
        var start = p.InitialAssignment(); // 実現可能な希望を載せた入口盤面
        var pins = ExactPins(p, start);
        var r = V6HotfixPasses.RunPostOptimization(st, start.Copy2D(), "p", seed: 12345L);

        foreach (var (i, j) in LockedCells(p))
        {
            Assert.True(p.Wish[i][j] == r.Schedule[i][j],
                $"{label}: 実現可能な希望（職員{i} 日{j}）が後処理で動いた");
        }
        // 厳密ピンは「入口で満たしていたものを外さない」＝ExactPinRegression の契約。
        foreach (var (i, k, target) in pins)
        {
            var cnt = 0;
            for (var j = 0; j < p.T; j++) if (r.Schedule[i][j] == k) cnt++;
            Assert.True(target == cnt,
                $"{label}: 厳密な回数固定（職員{i} シフト{k} = {target} 回）が後処理で崩れた");
        }
    }

    [Fact]
    public void PostOptimizationHoldsPinsOnRealProductionState()
    {
        var json = FixtureLoader.ReadRaw("golden_state.json");
        var st = StateJsonSerializer.Parse(json);
        var p = new Problem(st);
        Assert.True(LockedCells(p).Count > 0, "固定セルが無いと何も検証できない");
        AssertPinsHeld("golden_state", st);
    }

    /// <summary>研磨パスが実際に走るよう、違反を多めに含む小さな状態を作る。</summary>
    private static MagiState BusyState(JavaRandom rng, int s, int t, int k)
    {
        var groups = new List<Group> { new("G", "G") };
        var staff = Enumerable.Range(0, s).Select(i => new Staff($"S{i}", 0)).ToList();
        var shifts = new List<Shift> { new("休", "休", "0", "") };
        for (var x = 1; x < k; x++)
            shifts.Add(new Shift($"S{x}", $"S{x}", "1", (1 + rng.NextInt(2)).ToString()));

        var staffRange = new Dictionary<string, Range>();
        for (var i = 0; i < s; i++)
        {
            // 一部の職員に厳密ピン（lo==hi）を置く＝ExactPinRegression の対象を必ず作る。
            if (rng.NextInt(2) == 0)
            {
                var n = 2 + rng.NextInt(3);
                staffRange[$"{i},0"] = new Range(n.ToString(), n.ToString());
            }
            for (var x = 1; x < k; x++)
                if (rng.NextInt(3) == 0)
                    staffRange[$"{i},{x}"] = new Range("1", (2 + rng.NextInt(3)).ToString());
        }

        var wishes = new Dictionary<string, int>();
        var wishCount = s * t / 5;
        for (var w = 0; w < wishCount; w++)
            wishes[$"{rng.NextInt(s)},{rng.NextInt(t)}"] = rng.NextInt(k);

        string Sym(int x) => x == 0 ? "休" : $"S{x}";

        // [評価順に注意] Kotlin原本は MagiState(...) 呼出のソース上の記述順で引数を評価する
        // （groupShiftApt = ... が schedule = ... より前に書かれている）ため、rng の消費順は
        // groupShiftApt → schedule。C#でも同じ順でRNGを消費しないと、以降のRandom列が丸ごと
        // 分岐してKotlin原本とは別の（それでも内部的には妥当な）状態を生む。
        var groupShiftApt = new List<string>();
        for (var x = 0; x < k; x++)
            groupShiftApt.Add(rng.NextInt(2) == 0 ? (1 + rng.NextInt(4)).ToString() : "");

        var schedule = new List<IReadOnlyList<int>>();
        for (var i = 0; i < s; i++)
        {
            var row = new List<int>();
            for (var j = 0; j < t; j++) row.Add(rng.NextInt(k));
            schedule.Add(row);
        }

        return new MagiState(
            StartDate: "2026-05-01", EndDate: "2026-12-28",
            Shifts: shifts, Groups: groups, StaffList: staff, Use2Patterns: true,
            GroupShift: new List<IReadOnlyList<int>> { Enumerable.Repeat(1, k).ToList() },
            GroupShiftApt: new List<IReadOnlyList<string>> { groupShiftApt },
            Schedule: schedule,
            Wishes: wishes, StaffRange: staffRange,
            NeedDay1: new Dictionary<string, string>(), NeedDay2: new Dictionary<string, string>(),
            Cons1: new List<C1Row> { new("4", "休", "1") },
            Cons2: new List<C2Row> { new(Sym(1), "2") },
            Cons3: new List<C3Row> { new(new List<string> { Sym(1), Sym(1) }) },
            Cons3n: new List<C3Row> { new(new List<string> { Sym(1), Sym(k > 2 ? 2 : 0) }) },
            Cons3m: new List<C3Row> { new(new List<string> { "休", Sym(1) }) },
            Cons3mn: new List<C3Row> { new(new List<string> { "休", "休" }) },
            Cons41: new List<C41Row> { new("G", Sym(1), "1", "2") },
            Cons42: new List<C42Row>(),
            SkillGroups: new List<Group>(),
            Cons41s: new List<C41Row>(),
            Cons42s: new List<C42Row>(),
            ShiftColors: new Dictionary<string, string>(),
            Extras: new Dictionary<string, System.Text.Json.JsonElement>());
    }

    [Fact]
    public void PostOptimizationHoldsPinsAcrossRandomStates()
    {
        var rng = new JavaRandom(0x91A7L);
        var totalLocked = 0;
        var totalExact = 0;
        for (var iter = 0; iter < 12; iter++)
        {
            var st = BusyState(rng, s: 4 + rng.NextInt(3), t: 10 + rng.NextInt(8), k: 3 + rng.NextInt(2));
            var p = new Problem(st);
            totalLocked += LockedCells(p).Count;
            totalExact += ExactPins(p, p.InitialAssignment()).Count;
            AssertPinsHeld($"random#{iter}", st);
        }
        // 対象が無いまま緑になっていないことを数字で見せる。
        Assert.True(totalLocked > 20, "希望固定セルが一度も現れていない（試験が何も守っていない）");
        Assert.True(totalExact > 3, "厳密な回数固定が一度も現れていない（試験が何も守っていない）");
    }
}
