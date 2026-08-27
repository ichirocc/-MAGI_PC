using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// Faithful port of Kotlin's <c>C3nBitScanTest</c>.
///
/// [c3n ビット走査のパリティ] <see cref="C3nBitScan"/> は既存のスカラー実装
/// (<see cref="Problem.MakesForbiddenRun"/> / <see cref="C1DeltaPrefilter.StaffC3nFires"/>) を
/// 置き換えず、候補が爆発する新経路だけで使う高速版。両者が食い違うと「禁止連続を作る手を通して
/// しまう」ため、ランダム盤面で全単一セル変更を突き合わせる（3.172.0/3.174.0 で C++ 側のビット化に
/// 課したのと同じ規律を Kotlin 側にも課す。この C# 移植でも同じ規律を継続する）。
///
/// [PRNG の選択] Kotlin原本は <c>kotlin.random.Random</c>（<c>java.util.Random</c>ではない）を使う。
/// アサーションは特定の乱数値そのものには依存せず、「どんなランダム盤面でもビット経路とスカラー
/// オラクルが一致する」という不変条件だけを検証するため、C#側は素の <see cref="System.Random"/> で
/// 十分（<see cref="ParityTest"/>のランダム移動フィジングと同じ判断・同じ選択）。
/// </summary>
public class C3nBitScanTest
{
    // shift index: 0=休 1=D 2=A 3=B 4=C
    private static readonly IReadOnlyList<string> Kigou = new List<string> { "休", "D", "A", "B", "C" };
    private const int Rest = 0;
    private const int DShift = 1;
    private const int AShift = 2;

    /// <summary>実データの形（Dﾃ→X の2連が主・Dﾃ→休→X の3連が混じる）を模した合成問題。</summary>
    private static MagiState State(int days, IReadOnlyList<IReadOnlyList<int>> schedule)
    {
        var result = MinimalState.Build(
            startDate: "2026-12-01", endDate: "",
            shifts: Kigou.Select(k => new Shift(k, k, "", "")).ToList(),
            groups: new List<Group> { new("G", "G") },
            staffList: Enumerable.Range(0, schedule.Count).Select(i => new Staff($"s{i}", 0)).ToList(),
            groupShift: new List<IReadOnlyList<int>> { Enumerable.Repeat(1, Kigou.Count).ToList() },
            schedule: schedule,
            // 2連・3連を混在させる（3連が入るとパターン先頭が j-2 に来る局面が生まれる）。
            cons3n: new List<C3Row>
            {
                new(new List<string> { "D", "A" }),
                new(new List<string> { "D", "B" }),
                new(new List<string> { "C", "A" }),
                new(new List<string> { "D", "休", "A" }),
                new(new List<string> { "D", "休", "B" }),
            });
        if (result.DayCount != days)
            throw new ArgumentException($"dayCount mismatch: expected {days}, got {result.DayCount}");
        return result;
    }

    private static MagiState RandomState(int days, int seed)
    {
        var rng = new Random(seed);
        var schedule = Enumerable.Range(0, 2)
            .Select(_ => (IReadOnlyList<int>)Enumerable.Range(0, days).Select(_ => rng.Next(Kigou.Count)).ToList())
            .ToList();
        return State(days, schedule);
    }

    [Fact]
    public void BitScanMatchesScalarOracleForEverySingleCellChange()
    {
        var checkedFires = 0;
        var checkedCells = 0;
        for (var seed = 1; seed <= 12; seed++)
        {
            var st = RandomState(21, seed);
            var p = new Problem(st);
            var sched = st.Schedule.ToIntArray2D();
            for (var i = 0; i < p.S; i++)
            {
                var row = sched[i];
                var mask = C3nBitScan.BuildRowMask(p, row);
                // fires が一致 (seed=seed staff=i)
                Assert.Equal(C1DeltaPrefilter.StaffC3nFires(p, row), C3nBitScan.Fires(p, mask));
                checkedFires++;
                for (var j = 0; j < p.T; j++)
                {
                    var old = row[j];
                    for (var k = 0; k < p.K; k++)
                    {
                        // MakesForbiddenRun は「日 j をまたぐ窓での成立」を判定する。
                        // hitsAfterSet が一致 (seed=seed staff=i day=j k=k)
                        Assert.Equal(p.MakesForbiddenRun(sched, i, j, k), C3nBitScan.HitsAfterSet(p, mask, j, old, k));
                        // firesAfterSet は行全体の fire 数。スカラーは行を実際に書き換えて数える。
                        row[j] = k;
                        var expectFires = C1DeltaPrefilter.StaffC3nFires(p, row);
                        row[j] = old;
                        // firesAfterSet が一致 (seed=seed staff=i day=j k=k)
                        Assert.Equal(expectFires, C3nBitScan.FiresAfterSet(p, mask, j, old, k));
                        checkedCells++;
                    }
                    Assert.Equal(mask, C3nBitScan.BuildRowMask(p, row)); // mask は判定で不変
                }
            }
        }
        // 実際に十分な件数を突き合わせたことを固定（フィクスチャ退化でテストが空回りするのを防ぐ）。
        Assert.Equal(24, checkedFires);
        Assert.Equal(24 * 21 * 5, checkedCells);
    }

    /// <summary>
    /// [3連への到達] 旧実装が届かなかった「パターン先頭が j-2」の局面で、崩す候補日として j-2 まで
    /// 返ること。これが範囲拡張(3.303.0)の前提。
    /// </summary>
    [Fact]
    public void CoveringRunDaysReturnsWholePatternIncludingItsHead()
    {
        const int days = 10;
        // D(0日) 休(1日) A(2日) → cons3n "D→休→A" が窓[0..2]で成立。以降は休で埋め他ルールを不発に。
        var row = Enumerable.Repeat(Rest, days).ToList();
        row[0] = DShift; row[1] = Rest; row[2] = AShift;
        var st = State(days, new List<IReadOnlyList<int>> { row });
        var p = new Problem(st);
        var mask = C3nBitScan.BuildRowMask(p, st.Schedule.ToIntArray2D()[0]);

        var fired = C3nBitScan.Fires(p, mask);
        Assert.Equal(1, fired); // 3連がちょうど1件成立している前提

        var covering = C3nBitScan.CoveringRunDays(p, mask, 2);
        Assert.Equal(C3nBitScan.RangeMask(0, 2), covering); // パターン全域(0,1,2)が候補日として返る
        // 旧実装の視野(j±1 = 1,3)ではパターン先頭 day0 に届かないことを対比で固定。
        Assert.Equal(1L, covering & 1L); // 先頭 day0 が含まれる
    }

    // ---- [3.348.0] T>64 のスカラー退避 --------------------------------------------------------
    // C3nRowScan は 64日以内をビット、65日以上を既存スカラーで読む。既存テストは days=21/10 しか
    // 使っておらず**スカラー分岐を一度も通していなかった**（業務前提は最大31日なので実運用では
    // 到達しないが、この分岐は「日数上限を上げたとき」のための保険であり、保険が動く証拠が無かった）。
    //
    // 65日目に「どのパターンの末尾にもならないシフト」(休)を置くと、日64を含む窓は
    // start = 65-d の1本だけで、その窓は必ず不成立になる。よって 65日行のスカラー結果は
    // 先頭64日を切り出した行のビット結果と厳密に一致しなければならない。

    private static List<int> Prefix64(IReadOnlyList<int> row) => row.Take(64).ToList();

    [Fact]
    public void ScalarFallbackForLongHorizonMatchesTheBitPathOnTheSharedPrefix()
    {
        var checkedCells = 0;
        for (var seed = 1; seed <= 6; seed++)
        {
            var longState = RandomState(65, seed);
            // 65日目を「どのパターンの末尾にもならない」休へ固定する（境界の窓を必ず不成立にする）。
            var rows65 = longState.Schedule
                .Select(r => { var m = r.ToList(); m[64] = Rest; return (IReadOnlyList<int>)m; })
                .ToList();
            var st65 = State(65, rows65);
            var st64 = State(64, rows65.Select(r => (IReadOnlyList<int>)Prefix64(r)).ToList());
            var p65 = new Problem(st65);
            var p64 = new Problem(st64);
            Assert.False(C3nBitScan.Usable(p65), "65日はビット経路を使えない");
            Assert.True(C3nBitScan.Usable(p64), "64日はビット経路");

            var row65 = rows65[0].ToArray();
            var row64 = Prefix64(rows65[0]).ToArray();
            var scan65 = new C3nRowScan(p65, row65);
            var scan64 = new C3nRowScan(p64, row64);
            Assert.Equal(scan64.Fires(), scan65.Fires()); // スカラーとビットで fire 数が一致 (seed=seed)
            Assert.Equal(C1DeltaPrefilter.StaffC3nFires(p65, row65), scan65.Fires()); // スカラーはオラクルと一致

            // 全セル×全シフトで、変更後の fire 数と「崩せる日」の集合まで一致させる。
            // 日63以降は 65日側にだけ存在する窓が絡むので対象外（比較対象が同じでなくなる）。
            for (var day = 0; day < 62; day++)
            {
                for (var k = 0; k < Kigou.Count; k++)
                {
                    // 変更後の fire 数が一致 (seed=seed day=day k=k)
                    Assert.Equal(scan64.FiresAfterSet(day, k), scan65.FiresAfterSet(day, k));
                    // 崩せる日の集合が一致 (seed=seed day=day k=k)
                    Assert.Equal(scan64.CoveringDaysAfterSet(day, k), scan65.CoveringDaysAfterSet(day, k));
                    checkedCells++;
                }
            }
            Assert.Equal(rows65[0], row65); // スカラー経路は行を復元する
            Assert.Equal(Prefix64(rows65[0]), row64); // ビット経路は行を復元する
        }
        Assert.True(checkedCells >= 1_800, $"十分な数のセルを検査した: {checkedCells}");
    }
}
