using static MagiApp.ViewModels.OpLogText;

namespace MagiApp.ViewModels.Tests;

/// <summary>Kotlin <c>Review3697Test</c> の 1 対 1 移植。3.697.0「操作ログで人間の迷いを読む」: 画面の操作の行と「操作の要約（迷いの指標）」の形を固定する（Kotlin と同じ文字列）。</summary>
public class Review3697Test
{
    [Fact]
    public void UiLinesUseTheFixedWords()
    {
        Assert.Equal("画面: ホーム", UiLine(new UiOp.Tab(0)));
        Assert.Equal("画面: 勤務表", UiLine(new UiOp.Tab(1)));
        Assert.Equal("画面: 編集", UiLine(new UiOp.Tab(2)));
        Assert.Equal("画面: 分析", UiLine(new UiOp.Tab(3)));
        Assert.Equal("画面: 設定", UiLine(new UiOp.Tab(4)));
        Assert.Equal("巡回 開始: 必須違反 5か所", UiLine(new UiOp.TourStart(5)));
        Assert.Equal("巡回 終了: 3/5", UiLine(new UiOp.TourEnd(2), tourTotal: 5));
        Assert.Equal("作成前の確認: 開く", UiLine(new UiOp.PreRunOpen()));
        Assert.Equal("作成前の確認: 条件を確認する", UiLine(new UiOp.PreRunCheckConditions()));
        Assert.Equal("作成前の確認: 閉じる", UiLine(new UiOp.PreRunClose()));
        Assert.Equal("配布前の確認: 開く", UiLine(new UiOp.PreDistOpen()));
        Assert.Equal("配布前の確認: 書き出す", UiLine(new UiOp.PreDistExport()));
        Assert.Equal("配布前の確認: 戻る", UiLine(new UiOp.PreDistBack()));
        // 不足枠の日は CoverageShortfall.DayLabel「10/12(月)」から M/D だけ。
        Assert.Equal("人員不足の手伝い: 開く 10/12 Aｱ 候補 3人", UiLine(new UiOp.GuidedOpen("10/12(月)", "Aｱ", 3)));
        Assert.Equal("人員不足の手伝い: 閉じる（適用なし）", UiLine(new UiOp.GuidedClose()));
    }

    [Fact]
    public void ManualChangeLines()
    {
        Assert.Equal("手で変更: 甲 10/12 休→夜", CellChangeLine("甲", "10/12", "休", "夜"));
        Assert.Equal("手で変更: 甲 10/12 空→夜", CellChangeLine("甲", "10/12", EmptyCell, "夜"));
        Assert.Equal("手で変更: 7セル → 休（一括）", CellsChangeLine(7, "休"));
    }

    [Fact]
    public void DraftAndSearchAndPreviewLines()
    {
        Assert.Equal("下書きで置き換え（確認済み）: 必須 0件→3件", DraftReplaceLine(0L, 3));
        Assert.Equal("案を探す: 11/3のAｱ", FixSearchStartLine("11/3のAｱ"));
        Assert.Equal("案を探す: 全体", FixSearchStartLine(FixOriginAll));
        Assert.Equal("案: 8件（3秒）", FixSearchEndLine(8, 3400));
        Assert.Equal("案: なし（8秒）", FixSearchEndLine(0, 8020));
        Assert.Equal("変更の確認: 開く 12/7 休↔Cｱ", PreviewOpenLine("12/7 休↔Cｱ"));
        Assert.Equal("変更の確認: 今は変更しない 12/7 休↔Cｱ", PreviewDismissLine("12/7 休↔Cｱ"));
        Assert.Equal("相談に残す: 12/7 休↔Cｱ", ConsultKeepLine("12/7 休↔Cｱ"));
        Assert.Equal("改善手を適用: 12/7 休↔Cｱ（必須 2→1・合計 40→38） ・ 案を見てから 12秒", WithSeenSeconds("改善手を適用: 12/7 休↔Cｱ（必須 2→1・合計 40→38）", 12_900));
    }

    private const long Base = 1_760_000_000_000L;   // 2025-10-09 の昼前（UTC）。間隔だけに意味がある

    private static string Hm(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void HesitationSummaryCountsFromPrefixes()
    {
        var log = new (long T, string Line)[]
        {
            (0L, "読込 11名/31日/10シフト"),
            (10_000L, "画面: 勤務表"),
            (20_000L, "手で変更: 甲 10/12 休→夜"),
            (25_000L, "違反チェック 必須=3 合計=40"),
            (30_000L, "元に戻す: セル編集"),
            (31_000L, "やり直し: セル編集"),
            (40_000L, "最適化 開始 (予算60s, 並列8, 方式AUTO) ・ 期間=…"),
            (100_000L, "最適化 完了 必須=1 合計=30 ・ 盤面指紋=…"),
            (150_000L, "下書きで置き換え（確認済み）: 必須 1件→4件"),   // 完了から 50 秒＝結果の直後
            (200_000L, "案を探す: 全体"),
            (203_000L, "案: 8件（3秒）"),
            (210_000L, "変更の確認: 開く 12/7 休↔Cｱ"),
            (222_000L, "改善手を適用: 12/7 休↔Cｱ（必須 4→3・合計 30→28） ・ 案を見てから 12秒"),
            (230_000L, "案を探す: 11/3のAｱ"),
            (238_000L, "案: なし（8秒）"),
            (240_000L, "変更の確認: 開く 11/4 休↔Bｲ"),
            (245_000L, "変更の確認: 今は変更しない 11/4 休↔Bｲ"),
            (250_000L, "相談に残す: 11/4 休↔Bｲ"),
            (260_000L, "人員不足の手伝い: 開く 10/12 Aｱ 候補 3人"),
            (270_000L, "人員不足の手伝い: 閉じる（適用なし）"),
            (280_000L, "作成前の確認: 開く"),
            (290_000L, "作成前の確認: 閉じる"),
            (300_000L, "配布前の確認: 開く"),
            (310_000L, "配布前の確認: 戻る"),
            (2_110_000L, "最適化 開始 (予算60s, 並列8, 方式AUTO) ・ 期間=…"),   // 30 分の空白のあと
            (2_170_000L, "最適化 完了 必須=0 合計=20 ・ 盤面指紋=…"),
            (2_300_000L, "下書きで置き換え（確認済み）: 必須 0件→2件"),   // 完了から 130 秒＝直後ではない
            (2_310_000L, "変更の確認: 開く 12/8 休↔Cｱ"),
            (2_330_000L, "改善手を適用: 12/8 休↔Cｱ（必須 2→1・合計 20→18） ・ 案を見てから 20秒"),
            (2_340_000L, "改善手を適用: 12/9 休↔Cｱ（必須 1→0・合計 18→16）"),   // 確認を開いていない＝中央値に入れない
        };
        // 操作ログのリングは新しい順＝逆順で渡しても同じ答え。
        var rev = log.Reverse().ToList();
        var lines = HesitationSummary(rev.Select(x => x.Line).ToList(), rev.Select(x => Base + x.T).ToList());
        Assert.Equal(6, lines.Count);
        Assert.Equal("手で変更 1回 ・ 元に戻す 1回 ・ やり直し 1回", lines[0]);
        Assert.Equal("案を探した 2回（案あり 1・なし 1） ・ 適用 3回 ・ 今は変更しない 1回 ・ 相談に残す 1回", lines[1]);
        Assert.Equal("自動作成 2回 ・ 下書き 2回（結果の直後 1回）", lines[2]);
        Assert.Equal("確認の画面を閉じた（適用なし）3回", lines[3]);
        Assert.Equal($"最長の空白 30分（{Hm(Base + 310_000L)}〜{Hm(Base + 2_110_000L)}）", lines[4]);
        Assert.Equal("案を見てから適用までの中央値 16秒", lines[5]);
        Assert.Equal(lines, HesitationSummary(log.Select(x => x.Line).ToList(), log.Select(x => Base + x.T).ToList()));
    }

    [Fact]
    public void HesitationSummaryWithoutDataSaysDash()
    {
        var lines = HesitationSummary(Array.Empty<string>(), Array.Empty<long>());
        Assert.Equal("手で変更 0回 ・ 元に戻す 0回 ・ やり直し 0回", lines[0]);
        Assert.Equal("案を探した 0回（案あり 0・なし 0） ・ 適用 0回 ・ 今は変更しない 0回 ・ 相談に残す 0回", lines[1]);
        Assert.Equal("自動作成 0回 ・ 下書き 0回（結果の直後 0回）", lines[2]);
        Assert.Equal("確認の画面を閉じた（適用なし）0回", lines[3]);
        Assert.Equal("最長の空白 —", lines[4]);
        Assert.Equal("案を見てから適用までの中央値 —", lines[5]);
        // 1 行だけでは空白が無い。
        Assert.Equal("最長の空白 —", HesitationSummary(new[] { "画面: 勤務表" }, new[] { Base })[4]);
    }

    [Fact]
    public void RightAfterResultIsSixtySecondsInclusive()
    {
        static int N(long gapMs) =>
            HesitationOf(new[] { "最適化 完了 必須=1 合計=30", "下書きで置き換え（確認済み）: 必須 1件→4件" }, new[] { Base, Base + gapMs }).DraftsRightAfterResult;
        Assert.Equal(1, N(0L));
        Assert.Equal(1, N(60_000L));
        Assert.Equal(0, N(60_001L));
        // 完了が無い下書きは「結果の直後」に数えない（下書きの回数には入る）。
        var h = HesitationOf(new[] { "下書きで置き換え（確認済み）: 必須 0件→4件" }, new[] { Base });
        Assert.Equal(1, h.Drafts); Assert.Equal(0, h.DraftsRightAfterResult);
        Assert.Equal(60_000L, RightAfterMs);
    }

    [Fact]
    public void SummaryHeadAndNoNamesInIt()
    {
        Assert.Equal("==== 操作の要約（迷いの指標）====", HesitationHead);
        var lines = HesitationSummary(new[] { "手で変更: 吉江雄貴 10/12 休→夜" }, new[] { Base });
        Assert.DoesNotContain(lines, l => l.Contains("吉江"));
        Assert.False(lines[0].StartsWith("手で変更:", StringComparison.Ordinal));
    }
}
