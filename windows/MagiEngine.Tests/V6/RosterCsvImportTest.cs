using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [フェーズ7ピース10] Kotlin原本 <c>RosterCsvImportTest.kt</c>（109行・3テスト）の1:1移植。
///
/// 病院勤務表テンプレCSVの取込検証。テキストは復号済み(UTF-8)前提＝エンコーディング非依存の構造解析を
/// 確認する。列位置（氏名=idx2 / シフト記号 idx4〜 / 凡例 記号=idx1, 必要数 idx4〜）と空セル→休 を
/// 中心に検証。
/// </summary>
public class RosterCsvImportTest
{
    private static readonly string Sample = string.Join("\n", new[]
    {
        "令和8年,,,7,月",
        "ユニット名：,,柳,,1,2,3",
        "№,,氏 名,,水,木,金",
        "1,リーダー,古泉 健一,予定,Aｱ,,休",
        "2,,山本 昌幸,予定,A4,休,",
        "3,,,予定,,,",
        ",,,,,,",
        "ユニット名：,,桐,,1,2,3",
        "№,,氏 名,,水,木,金",
        "1,主任,上條 洋平,予定,B4,休,Aｱ",
        ",,,,,,",
        ",記号,時刻,休憩時間,水,木,金",
        ",A4,6:00～15:00,1h,1,0,1",
        ",Aｱ,7:30～16:30,1h,2,0,1",
        ",B4,8:30～17:30,1h,1,0,0",
        ",休,定休,,1,2,1",
    });

    [Fact]
    public void DetectsTemplate()
    {
        Assert.True(RosterCsvImport.Detect(Sample));
        Assert.False(RosterCsvImport.Detect("name,1,2,3\nA,休,休,休"));
    }

    [Fact]
    public void ParsesUnitsStaffShiftsAndGrid()
    {
        var st = RosterCsvImport.Parse(Sample);
        Assert.NotNull(st);

        // 期間: 令和8年7月 → 2026-07-01、3日
        Assert.Equal("2026-07-01", st!.StartDate);
        Assert.Equal(3, st.DayCount);

        // ユニット=グループ（柳・桐）
        Assert.Equal(2, st.GroupCount);
        Assert.Equal("柳", st.Groups[0].Kigou);
        Assert.Equal("桐", st.Groups[1].Kigou);

        // スタッフ（空欄№3は除外）。柳=2名 + 桐=1名 = 3名。
        Assert.Equal(3, st.StaffCount);
        Assert.Equal("古泉 健一", st.StaffList[0].Name);
        Assert.Equal(0, st.StaffList[0].GroupIdx);
        Assert.Equal("上條 洋平", st.StaffList[2].Name);
        Assert.Equal(1, st.StaffList[2].GroupIdx);

        // シフトは凡例から（A4, Aｱ, B4, 休）
        var k = st.Shifts.Select((shift, idx) => (shift.Kigou, idx)).ToDictionary(x => x.Kigou, x => x.idx);
        Assert.True(k.ContainsKey("A4"));
        Assert.True(k.ContainsKey("Aｱ"));
        Assert.True(k.ContainsKey("B4"));
        Assert.True(k.ContainsKey("休"));

        // 勤務表グリッド（空セル→休）
        var rest = k["休"];
        // 古泉: Aｱ, (空→休), 休
        Assert.Equal(k["Aｱ"], st.Schedule[0][0]);
        Assert.Equal(rest, st.Schedule[0][1]);
        Assert.Equal(rest, st.Schedule[0][2]);
        // 山本: A4, 休, (空→休)
        Assert.Equal(k["A4"], st.Schedule[1][0]);
        Assert.Equal(rest, st.Schedule[1][2]);
        // 上條(桐): B4, 休, Aｱ
        Assert.Equal(k["B4"], st.Schedule[2][0]);
        Assert.Equal(k["Aｱ"], st.Schedule[2][2]);

        // 必要人数はCSVに無い（凡例の日別数値は現在表の人数集計＝需要ではない）→ needDay は取り込まない。
        Assert.Empty(st.NeedDay1);
        Assert.Empty(st.NeedDay2);
        Assert.True(st.Shifts.All(shift => shift.Need1.Trim().Length == 0 && shift.Need2.Trim().Length == 0));

        // 担当可否は不明→全シフト可で取込
        Assert.Equal(st.ShiftCount, st.GroupShift[0].Count);
        Assert.True(st.GroupShift[0].All(v => v == 1));
    }

    [Fact]
    public void ParsesAsWishesLeavesScheduleEmptyAndFillsWishes()
    {
        var st = RosterCsvImport.Parse(Sample, asWishes: true);
        Assert.NotNull(st);
        var k = st!.Shifts.Select((shift, idx) => (shift.Kigou, idx)).ToDictionary(x => x.Kigou, x => x.idx);
        var rest = k["休"];

        // 希望モード: 勤務表は全て公休で開始（最適化が希望を尊重して埋める）。
        for (var i = 0; i < st.StaffCount; i++)
        for (var j = 0; j < st.DayCount; j++)
            Assert.Equal(rest, st.Schedule[i][j]);

        // 埋まっていたセルは希望として登録（空セルは希望なし）。元の明示「休」は希望休として残る。
        Assert.Equal(k["Aｱ"], st.Wishes["0,0"]);   // 古泉 d0
        Assert.False(st.Wishes.ContainsKey("0,1"));  // 空セル→希望なし
        Assert.Equal(rest, st.Wishes["0,2"]);         // 古泉 d2 = 希望休
        Assert.Equal(k["A4"], st.Wishes["1,0"]);      // 山本 d0
        Assert.Equal(k["B4"], st.Wishes["2,0"]);      // 上條(桐) d0
        Assert.Equal(k["Aｱ"], st.Wishes["2,2"]);      // 上條 d2

        // 必要人数は取込方法に依らずCSVに無い。
        Assert.Empty(st.NeedDay1);
    }
}
