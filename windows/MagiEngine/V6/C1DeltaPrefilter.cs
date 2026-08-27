namespace MagiEngine.V6;

/// <summary>
/// Faithful (partial) port of Kotlin's <c>C1DeltaPrefilter</c> object.
///
/// [フェーズ5b, 移植範囲の限定] Kotlin原本は <c>Verdict</c>/<c>HasActionableC1</c>/<c>ScreenCell</c>/
/// <c>C1Delta</c> も持つが、それらは <c>C1RepairIndex</c>（フェーズ6の C1 修復サテライト群、
/// <c>V6HotfixPasses.kt</c> 一式と一緒に移植する）に依存するか、それ自体がフェーズ6専用の消費者しか
/// 持たない。本フェーズが必要とするのは <see cref="StaffC3nFires"/>（<c>C3nRowScan</c> のスカラー
/// フォールバック経路が、64日超の T でオラクルとして使う）だけなので、それだけを先に移植する。
/// フェーズ6で残りをこのファイルへ追記する。
/// </summary>
public static class C1DeltaPrefilter
{
    /// <summary>
    /// 職員行 row の cons3n（禁止連続, HARD）fire 数。checker の forbidden 窓完全一致と同一意味論。
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
}
