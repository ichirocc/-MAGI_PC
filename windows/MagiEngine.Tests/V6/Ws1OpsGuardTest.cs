using MagiEngine.Model;
using MagiEngine.V6;
using Xunit;

namespace MagiEngine.Tests.V6;

/// <summary>グループ追加とシフト削除で「担当できるシフトが無いグループ」を作らない（Kotlin <c>Ws1OpsGuardTest</c> と 1 対 1）。</summary>
public class Ws1OpsGuardTest
{
    private static MagiState St(bool rest) => StateJsonSerializer.Parse(
        "{\"startDate\":\"2026-01-01\",\"endDate\":\"2026-01-02\"," +
        "\"shifts\":[{\"name\":\"日勤\",\"kigou\":\"日\",\"need1\":\"1\",\"need2\":\"\",\"role\":\"none\"}," +
        (rest ? "{\"name\":\"休み\",\"kigou\":\"休\",\"need1\":\"\",\"need2\":\"\",\"role\":\"rest\"}]," : "{\"name\":\"公\",\"kigou\":\"公\",\"need1\":\"\",\"need2\":\"\",\"role\":\"none\"}],") +
        "\"groups\":[{\"name\":\"G\",\"kigou\":\"G\"},{\"name\":\"H\",\"kigou\":\"H\"}]," +
        "\"staff\":[{\"name\":\"a\",\"groupIdx\":0,\"skillIdx\":-1},{\"name\":\"b\",\"groupIdx\":1,\"skillIdx\":-1}]," +
        "\"groupShift\":[[1,1],[1,0]],\"schedule\":[[0,1],[0,0]]}");

    [Fact]
    public void AddedGroupCanDoAShiftEvenWithoutRestRole()
    {
        var added = Ws1Ops.AddGroup(St(rest: false), "N", "N");
        Assert.Contains(1, added.GroupShift[^1]);   // 休みの役割が無くても先頭シフトを担当にする
        Assert.Equal(new[] { 0, 1 }, Ws1Ops.AddGroup(St(rest: true), "N", "N").GroupShift[^1]);   // 休みがあれば休み
    }

    [Fact]
    public void RemovingTheOnlyShiftOfAGroupIsRefused()
    {
        var s = St(rest: true);
        Assert.Equal(1, Ws1Ops.GroupLeftWithoutShift(s, 0));   // H は日勤だけ
        var sched = new[] { new[] { 0, 1 }, new[] { 0, 0 } };
        Assert.Equal(2, Ws1Ops.RemoveShift(s, sched, 0).State.Shifts.Count);   // 消さない
        Assert.Null(Ws1Ops.GroupLeftWithoutShift(s, 1));
        Assert.Single(Ws1Ops.RemoveShift(s, sched, 1).State.Shifts);   // 休みは消せる（H は日勤が残る）
    }
}
