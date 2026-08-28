using MagiEngine.Model;
using MagiEngine.V6;

namespace MagiEngine.Tests.TestSupport;

/// <summary>
/// Small verification-oracle helpers shared across post-optimization polish-pass tests.
///
/// [Kotlin原本] <c>V6FinalBridgePortTest.kt</c>末尾の <c>internal fun invalidAssignmentCount</c>。
/// KDocいわく「旧 <c>V6WebCompat.invalidAssignmentCount</c>。本番の呼出は無く、この検証オラクルが
/// 唯一の用途だったので Web 互換オブジェクトの撤去(3.393.0)に合わせてテスト側へ移した」——本ヘルパは
/// その C# 版で、複数の研磨パステスト（同日/複数日交換系・日割当系）から共通利用される。
///
/// [注意] Kotlin原本には<b>もう1つ</b>、まったく同じ算術の <c>private fun invalidAssignmentCount</c>
/// が <c>V6HotfixPasses.kt</c> の本体側にも独立して存在する（本番診断の内部専用ヘルパ、非公開）。
/// 両者は偶然同じ実装を持つだけで別物＝production 側は該当パスを移植する際に別途 C# 化する。
/// </summary>
internal static class ScheduleAssertions
{
    /// <summary>
    /// 盤面のうち「担当できないシフト／範囲外のシフト番号」が入っているセル数。被覆保存系の研磨パス
    /// （同日/複数日の値入替えのみ）が本当に妥当な割当だけを返しているかを確認するオラクル。
    /// </summary>
    public static int InvalidAssignmentCount(MagiState state, int[][]? schedule = null)
    {
        var p = new Problem(state);
        var s = ScheduleUtil.NormalizeSchedule(schedule ?? state.Schedule.ToIntArray2D(), p);
        var n = 0;
        for (var i = 0; i < p.S; i++)
        {
            for (var j = 0; j < p.T; j++)
            {
                var k = s[i][j];
                if (k < 0 || k >= p.K || !p.CanDo(i, k)) n++;
            }
        }
        return n;
    }
}
