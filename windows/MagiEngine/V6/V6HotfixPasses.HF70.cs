using MagiEngine.Model;

namespace MagiEngine.V6;

public static partial class V6HotfixPasses
{
    /// <summary>
    /// [フェーズ6, ピース25] Kotlin原本 <c>HF70Result</c>（top-level data class, 51-56行）の忠実な移植。
    /// 最適化後の盤面を異常検知の観点（担当不可/範囲外配置・実現不能希望・希望以外のHARD）で走査する
    /// <see cref="DetectHF70Anomalies"/> の戻り値。
    /// </summary>
    public sealed record HF70Result(int Anomalies, string Message, string Advice, IReadOnlyList<MirrorLog> Logs);

    /// <summary>
    /// 盤面 <paramref name="schedule"/>（正規化後）のうち、シフト値が有効範囲外か、その職員のグループが
    /// 担当できないセルの件数を数える。<see cref="DetectHF70Anomalies"/>専用の内部ヘルパ。
    /// </summary>
    private static int InvalidAssignmentCount(MagiState state, int[][] schedule)
    {
        var p = new Problem(state);
        var s = ScheduleUtil.NormalizeSchedule(schedule, p);
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

    /// <summary>
    /// [フェーズ6, ピース25] Kotlin原本 <c>detectHF70Anomalies</c>（<c>V6HotfixPasses.kt</c>由来）の
    /// 忠実な移植。他の全 HF6x パスとは異なり盤面を一切変更しない**純粋な診断**——最適化(HF80→HF67→HF66)を
    /// 一通り終えた盤面が「本当に実行可能か」を最後に確かめる異常検知。
    ///
    /// 3つの独立した観点を数え、1件以上あれば理由付きの警告メッセージを組み立てる:
    ///  - 担当不可/範囲外配置（<see cref="InvalidAssignmentCount"/>＝正規化後もなお不正なセル）。
    ///  - 実現不能希望（<see cref="V6SanityPort.DetectImpossibleWishes"/>＝グループが担当できない
    ///    シフトへの希望等）。
    ///  - 希望以外のHARD（<c>report.Hard</c>から<c>pref</c>族の寄与を除いた残り＝groupViol/c3n/covU）。
    ///
    /// [C#化の注記] Kotlinの既定引数 <c>report: ViolationReport =
    /// UnifiedViolationChecker.check(state, schedule)</c> は非定数式（関数呼出）のため、C#の既定引数
    /// （コンパイル時定数のみ許容）へそのまま移せない。<c>ViolationReport? report = null</c> とし、
    /// メソッド本体内で <c>report ?? UnifiedViolationChecker.Check(...)</c> により同じ既定値を計算する
    /// （<c>shouldStop</c>系のnull合体パターンと同じ扱い）。
    /// </summary>
    public static HF70Result DetectHF70Anomalies(
        MagiState state, int[][] schedule, string algoName, ViolationReport? report = null)
    {
        var rep = report ?? UnifiedViolationChecker.Check(state, schedule);
        var invalid = InvalidAssignmentCount(state, schedule);
        var impossible = V6SanityPort.DetectImpossibleWishes(state).Count;
        var hardCore = rep.Hard - rep.Breakdown.GetValueOrDefault("pref", 0);
        var issues = new List<string>();
        if (invalid > 0) issues.Add($"担当不可/範囲外配置 {invalid} 件");
        if (impossible > 0) issues.Add($"不可能希望 {impossible} 件");
        if (hardCore > 0) issues.Add($"希望以外HARD {hardCore} 件");
        var msg = issues.Count == 0 ? $"HF70: {algoName} 異常なし" : $"HF70: {string.Join(" / ", issues)}";
        var advice = issues.Count == 0 ? "" : "設定(担当範囲), 希望, 必要人数, 連勤禁止条件を確認してください";
        var level = issues.Count == 0 ? "I" : "W";
        var logs = new List<MirrorLog>
        {
            new MirrorLog(level: level, tag: "HF70", message: msg + (advice.Length > 0 ? $" — {advice}" : "")),
        };
        return new HF70Result(issues.Count, msg, advice, logs);
    }
}
