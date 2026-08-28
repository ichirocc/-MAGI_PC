using System.Globalization;
using MagiEngine.Model;
using MagiEngine.V6;
// System.Range (built-in C# 8+ slice type, brought into scope by the SDK's implicit
// `global using global::System;`) collides by simple name with MagiEngine.Model.Range.
using Range = MagiEngine.Model.Range;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [フェーズ7ピース13] <see cref="ConstraintMus"/> の移植元テストの抽出（Kotlin原本
/// <c>ConstraintMusTest.kt</c>、~7件のうちエンジン単体（<c>AnalyzeStaffConflicts</c>/
/// <c>AnalyzeDayConflicts</c>のみ）を検証する6件）。各テストは「極小コアの正確な構成」まで固定する
/// （極小性: どの1件を外しても証明が崩れる構成を手計算で設計済み＝コアは一意）。
///
/// <c>guidanceEmitsDayConflictWithWishLabels</c>（全体が <c>V6SanityPort.BuildGuidance</c> 経由）は
/// piece 14/15（<c>V6SanityPort.Guidance*.cs</c>）が対象＝<c>BuildGuidance</c> 未移植のためここでは
/// 対象外。<c>engineFindsWishFreeConflictButGuidanceSuppressesIt</c> はエンジン部分（前半、
/// <see cref="EngineFindsWishFreeConflict"/>）のみ移植し、後半の
/// <c>V6SanityPort.BuildGuidance</c> 呼び出し以降（「希望なしコアは検査9から出さない」の確認）は
/// 同じ理由で piece 14/15 のテストファイルへ委ねる。
/// </summary>
public class ConstraintMusTest
{
    private static MagiState State(
        int days,
        List<Shift> shifts,
        Dictionary<string, int>? wishes = null,
        Dictionary<string, Range>? staffRange = null,
        List<C1Row>? cons1 = null,
        int staffCount = 1)
    {
        wishes ??= new Dictionary<string, int>();
        staffRange ??= new Dictionary<string, Range>();
        cons1 ??= new List<C1Row>();
        var end = "2026-01-" + days.ToString(CultureInfo.InvariantCulture).PadLeft(2, '0');
        return new MagiState(
            StartDate: "2026-01-01", EndDate: end,
            Shifts: shifts,
            Groups: new List<Group> { new("G", "G") },
            StaffList: Enumerable.Range(0, staffCount).Select(it => new Staff($"s{it}", 0)).ToList(),
            Use2Patterns: false,
            GroupShift: new List<IReadOnlyList<int>> { Enumerable.Repeat(1, shifts.Count).ToList() },
            GroupShiftApt: new List<IReadOnlyList<string>> { Enumerable.Repeat("", shifts.Count).ToList() },
            Schedule: Enumerable.Range(0, staffCount)
                .Select(_ => (IReadOnlyList<int>)Enumerable.Repeat(0, days).ToList()).ToList(),
            Wishes: wishes, StaffRange: staffRange,
            NeedDay1: new Dictionary<string, string>(), NeedDay2: new Dictionary<string, string>(),
            Cons1: cons1, Cons2: new List<C2Row>(), Cons3: new List<C3Row>(),
            Cons3n: new List<C3Row>(), Cons3m: new List<C3Row>(), Cons3mn: new List<C3Row>(),
            Cons41: new List<C41Row>(), Cons42: new List<C42Row>(),
            SkillGroups: new List<Group>(), Cons41s: new List<C41Row>(), Cons42s: new List<C42Row>(),
            ShiftColors: new Dictionary<string, string>(), Extras: new Dictionary<string, System.Text.Json.JsonElement>());
    }

    [Fact]
    public void StaffMusCapVersusPinnedWishes()
    {
        // X上限1に対しXへの固定希望が2件 → {上限, 希望, 希望} の3件が極小コア。
        var st = State(
            days: 5,
            shifts: new List<Shift> { new("休", "休", "", ""), new("X", "X", "", "") },
            wishes: new Dictionary<string, int> { ["0,0"] = 1, ["0,2"] = 1 },
            staffRange: new Dictionary<string, Range> { ["0,1"] = new Range("", "1") });
        var res = ConstraintMus.AnalyzeStaffConflicts(new Problem(st));
        Assert.Single(res);
        var core = res[0].Core;
        Assert.Equal(3, core.Count);
        Assert.Single(core.OfType<ConstraintMus.RangeCap>());
        Assert.Equal(2, core.OfType<ConstraintMus.WishPin>().Count());
    }

    [Fact]
    public void StaffMusFloorPlusWishesPigeonhole()
    {
        // T=5でX下限3＋休への固定希望3件 → 需要合計6>5（鳩の巣）。どの1件を外しても5以下=コアは4件で一意。
        var st = State(
            days: 5,
            shifts: new List<Shift> { new("休", "休", "", ""), new("X", "X", "", "") },
            wishes: new Dictionary<string, int> { ["0,0"] = 0, ["0,1"] = 0, ["0,2"] = 0 },
            staffRange: new Dictionary<string, Range> { ["0,1"] = new Range("3", "") });
        var res = ConstraintMus.AnalyzeStaffConflicts(new Problem(st));
        Assert.Single(res);
        var core = res[0].Core;
        Assert.Equal(4, core.Count);
        Assert.Single(core.OfType<ConstraintMus.RangeFloor>());
        Assert.Equal(3, core.OfType<ConstraintMus.WishPin>().Count());
    }

    [Fact]
    public void StaffMusWindowRulePlusWishes()
    {
        // 窓ルール「X 5日で1回以上」(最小1日) ＋ 全5日が休への固定希望 → 1+5=6>5（鳩の巣）。
        var st = State(
            days: 5,
            shifts: new List<Shift> { new("休", "休", "", ""), new("X", "X", "", "") },
            wishes: new Dictionary<string, int> { ["0,0"] = 0, ["0,1"] = 0, ["0,2"] = 0, ["0,3"] = 0, ["0,4"] = 0 },
            cons1: new List<C1Row> { new("5", "X", "1") });
        var res = ConstraintMus.AnalyzeStaffConflicts(new Problem(st));
        Assert.Single(res);
        var core = res[0].Core;
        Assert.Equal(6, core.Count);
        Assert.Single(core.OfType<ConstraintMus.WindowRule>());
        Assert.Equal(5, core.OfType<ConstraintMus.WishPin>().Count());
    }

    [Fact]
    public void DayMusCoverageBlockedByWishes()
    {
        // 日0: X必要1人・担当可能な2人とも休への固定希望 → {必要人数, 希望, 希望} の3件が極小コア。
        // 日1は希望なしで充足可能=矛盾なし。
        var st = State(
            days: 2,
            shifts: new List<Shift> { new("休", "休", "", ""), new("X", "X", "1", "") },
            wishes: new Dictionary<string, int> { ["0,0"] = 0, ["1,0"] = 0 },
            staffCount: 2);
        var res = ConstraintMus.AnalyzeDayConflicts(new Problem(st));
        Assert.Single(res);
        Assert.Equal(0, res[0].Day);
        var core = res[0].Core;
        Assert.Equal(3, core.Count);
        Assert.Single(core.OfType<ConstraintMus.DayNeed>());
        Assert.Equal(2, core.OfType<ConstraintMus.WishPin>().Count());
    }

    /// <summary>
    /// [Kotlin原本 <c>engineFindsWishFreeConflictButGuidanceSuppressesIt</c> の前半のみ] 2b-3系
    /// （上限×窓・希望なし）: エンジンは {上限, 窓ルール} の2件コアを見つける。原本後半（検査9が
    /// 希望なしコアを重複回避のため出さないことの確認）は <c>V6SanityPort.BuildGuidance</c> 未移植
    /// のため piece 14/15 のテストファイルへ委ねる。
    /// </summary>
    [Fact]
    public void EngineFindsWishFreeConflict()
    {
        var st = State(
            days: 10,
            shifts: new List<Shift> { new("休", "休", "", ""), new("X", "X", "", "") },
            staffRange: new Dictionary<string, Range> { ["0,1"] = new Range("", "1") },
            cons1: new List<C1Row> { new("5", "X", "2") });
        var res = ConstraintMus.AnalyzeStaffConflicts(new Problem(st));
        Assert.Single(res);
        Assert.Equal(2, res[0].Core.Count);
        Assert.Single(res[0].Core.OfType<ConstraintMus.RangeCap>());
        Assert.Single(res[0].Core.OfType<ConstraintMus.WindowRule>());
    }

    [Fact]
    public void NoConflictYieldsNothing()
    {
        var st = State(
            days: 5,
            shifts: new List<Shift> { new("休", "休", "", ""), new("X", "X", "1", "") },
            wishes: new Dictionary<string, int> { ["0,0"] = 1 },
            staffRange: new Dictionary<string, Range> { ["0,1"] = new Range("1", "5") },
            staffCount: 2);
        Assert.Empty(ConstraintMus.AnalyzeStaffConflicts(new Problem(st)));
        Assert.Empty(ConstraintMus.AnalyzeDayConflicts(new Problem(st)));
    }
}
