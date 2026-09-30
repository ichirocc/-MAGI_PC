using System.Text.RegularExpressions;
using MagiEngine.Model;
using MagiEngine.Tests.TestSupport;
using MagiEngine.V6;
using Xunit;

namespace MagiEngine.Tests.V6;

/// <summary>Android <c>CsvPartialImportTest.kt</c> の 1 対 1 移植。引用符が閉じていない勤務表CSVは、読めたところまでを取り込むか確認してから適用する（利用者決定 B）。</summary>
public class CsvPartialImportTest
{
    private static MagiState BuildState(IReadOnlyList<Staff>? staff = null) => MinimalState.Build(
        startDate: "2026-07-01", endDate: "2026-07-03",
        shifts: new List<Shift>
        {
            new("日勤", "日", "0", ""), new("夜勤", "夜", "0", ""), new("休み", "休", "0", "", ShiftRole.Rest),
        },
        staffList: staff ?? new List<Staff> { new("山田", 0), new("鈴木", 0), new("佐藤", 0) },
        schedule: Enumerable.Range(0, 3).Select(_ => (IReadOnlyList<int>)new List<int> { 2, 2, 2 }).ToList());

    private static int[][] Base() => new[] { new[] { 2, 2, 2 }, new[] { 2, 2, 2 }, new[] { 2, 2, 2 } };

    private static int Breaks(string s) => Regex.Matches(s, "\r\n|\r|\n").Count;

    [Fact]
    public void WellFormedCsvIsNotFlaggedAndReadsEveryRecord()
    {
        var r = CsvPartialImport.ReadableOf("スタッフ \\ 日付,1,2,3\n山田,日,夜,休\n鈴木,休,休,日\n");
        Assert.False(r.UnclosedQuote);
        Assert.Equal(3, r.Records);
        Assert.Equal(3, r.EndLine);
    }

    [Fact]
    public void LastRecordWithoutTrailingNewlineCountsItsOwnLine()
    {
        var r = CsvPartialImport.ReadableOf("a,b\nc,d");
        Assert.False(r.UnclosedQuote);
        Assert.Equal(2, r.Records);
        Assert.Equal(2, r.EndLine);
    }

    [Fact]
    public void EndLineIsTheLastLineOfTheLastTerminatedRecord()
    {
        var text = "スタッフ \\ 日付,1,2,3\n山田,日,夜,休\n鈴木,\"日,休,休\n佐藤,休,休,日\n";
        var r = CsvPartialImport.ReadableOf(text);
        Assert.True(r.UnclosedQuote);
        Assert.Equal(2, r.Records);
        Assert.Equal(2, r.EndLine);
        Assert.Equal("スタッフ \\ 日付,1,2,3\n山田,日,夜,休\n", r.PrefixText);
    }

    [Fact]
    public void MultiLineQuotedFieldInTheReadablePartCountsEveryPhysicalLine()
    {
        // 2 件目の氏名セルが改行を含む（2〜3 行目）。そのあと 3 件目（4 行目）で引用符が開いたまま終わる。
        var text = "スタッフ \\ 日付,1,2,3\n\"山田\n（日勤帯）\",日,夜,休\n鈴木,\"日,休\n佐藤,休,休,日\n";
        var r = CsvPartialImport.ReadableOf(text);
        Assert.True(r.UnclosedQuote);
        Assert.Equal(2, r.Records);
        Assert.Equal(3, r.EndLine);
        Assert.Equal(text.Substring(0, text.IndexOf("鈴木", StringComparison.Ordinal)), r.PrefixText);
    }

    [Fact]
    public void NewlinesInsideTheSwallowedRecordDoNotMoveTheEnd()
    {
        var r = CsvPartialImport.ReadableOf("山田,日,夜,休\n鈴木,\"日\n\n\n休\n休\n");
        Assert.True(r.UnclosedQuote);
        Assert.Equal(1, r.Records);
        Assert.Equal(1, r.EndLine);
    }

    [Fact]
    public void CrlfAndLoneCrEachCountAsOneLineBreakOutsideAndInsideQuotes()
    {
        var r1 = CsvPartialImport.ReadableOf("a,b\r\n\"x\r\ny\",z\r\nq,\"r");
        Assert.True(r1.UnclosedQuote);
        Assert.Equal(2, r1.Records);
        Assert.Equal(3, r1.EndLine);
        var r2 = CsvPartialImport.ReadableOf("a,b\r\"x\ry\",z\rq,\"r");
        Assert.True(r2.UnclosedQuote);
        Assert.Equal(2, r2.Records);
        Assert.Equal(3, r2.EndLine);
    }

    [Fact]
    public void DoubledQuoteInsideQuotedFieldDoesNotCloseIt()
    {
        var ok = CsvPartialImport.ReadableOf("a,\"x\"\"y\"\nb,c\n");
        Assert.False(ok.UnclosedQuote);
        var ng = CsvPartialImport.ReadableOf("a,b\nc,\"x\"\"y\nd,e\n");
        Assert.True(ng.UnclosedQuote);
        Assert.Equal(1, ng.Records);
        Assert.Equal(1, ng.EndLine);
    }

    [Fact]
    public void QuoteOpeningInTheFirstRecordReadsNothing()
    {
        var r = CsvPartialImport.ReadableOf("\"山田,日,夜,休\n鈴木,日,日,日\n");
        Assert.True(r.UnclosedQuote);
        Assert.Equal(0, r.Records);
        Assert.Equal(0, r.EndLine);
        Assert.Equal("", r.PrefixText);
    }

    [Fact]
    public void BomIsSkippedAndDoesNotShiftTheLineNumbers()
    {
        var r = CsvPartialImport.ReadableOf("﻿a,b\nc,\"d\ne,f\n");
        Assert.True(r.UnclosedQuote);
        Assert.Equal(1, r.Records);
        Assert.Equal(1, r.EndLine);
        Assert.Equal("﻿a,b\n", r.PrefixText);
    }

    [Fact]
    public void PrefixTextParsesToExactlyTheReadableRecordsAndLineCountsAgree()
    {
        var rnd = new Random(20260930);
        var alphabet = new[] { 'a', 'b', ',', '"', '"', '\n', '\r', ' ' };
        var unclosedSeen = 0;
        for (var n = 0; n < 4000; n++)
        {
            var text = new string(Enumerable.Range(0, rnd.Next(40)).Select(_ => alphabet[rnd.Next(alphabet.Length)]).ToArray());
            var r = CsvPartialImport.ReadableOf(text);
            var full = CsvUtil.ParseCsvFull(text);
            Assert.True(full.UnclosedQuote == r.UnclosedQuote, $"「{text}」の旗");
            if (r.UnclosedQuote)
            {
                unclosedSeen++;
                var again = CsvUtil.ParseCsvFull(r.PrefixText);
                Assert.False(again.UnclosedQuote, $"「{text}」の読めた部分は閉じている");
                Assert.True(r.Records == again.Rows.Count, $"「{text}」の読めた件数");
                Assert.True(
                    full.Rows.Take(r.Records).Select(row => string.Join("\u0001", row)).SequenceEqual(again.Rows.Select(row => string.Join("\u0001", row))),
                    $"「{text}」の読めた行");
                Assert.True(Breaks(r.PrefixText) == r.EndLine, $"「{text}」の行番号");
                Assert.True(text.StartsWith(r.PrefixText, StringComparison.Ordinal), $"「{text}」は前置きで始まる");
            }
            else
            {
                Assert.True(full.Rows.Count == r.Records, $"「{text}」の件数");
                Assert.True(text == r.PrefixText, "閉じていれば全文");
                if (r.Records == 0) Assert.Equal(0, r.EndLine);
                else if (text.EndsWith('\n') || text.EndsWith('\r')) Assert.True(Breaks(text) == r.EndLine, $"「{text}」の最終行");
                else
                {
                    // 改行より後ろが引用符だけなら、行になるかは従来の解析次第（範囲だけ見る）。
                    var tail = text.Substring(Math.Max(text.LastIndexOf('\n'), text.LastIndexOf('\r')) + 1);
                    if (tail.Any(c => c != '"')) Assert.True(Breaks(text) + 1 == r.EndLine, $"「{text}」の最終行");
                    else Assert.True(r.EndLine >= Breaks(text) && r.EndLine <= Breaks(text) + 1, $"「{text}」の最終行");
                }
            }
        }
        Assert.True(unclosedSeen > 300, "未閉の例が十分に含まれる");
    }

    [Fact]
    public void JudgeLeavesWellFormedCsvToTheUsualPath()
    {
        var v = CsvPartialImport.Judge("山田,日,夜,休\n鈴木,休,休,日\n", BuildState(), Base());
        Assert.IsType<CsvPartialImport.Verdict.WellFormed>(v);
    }

    [Fact]
    public void JudgeAsksWithTheLineAndTheStaffCountOfTheReadablePart()
    {
        var text = "スタッフ \\ 日付,1,2,3\n山田,日,夜,休\n鈴木,休,休,日\n佐藤,\"日,日,日\n";
        var v = Assert.IsType<CsvPartialImport.Verdict.Ask>(CsvPartialImport.Judge(text, BuildState(), Base()));
        Assert.Equal(3, v.EndLine);
        Assert.Equal(2, v.Matched);
        Assert.False(v.Result.UnclosedQuote, "読めた部分は閉じている");
        Assert.Equal(
            "CSV の 3行目までは読めました（2 名分）。その先は引用符が閉じていないため読めません。この部分だけ取り込みますか？",
            v.Prompt);
    }

    [Fact]
    public void JudgeNeverAppliesTheRecordThatSwallowsTheRest()
    {
        // 鈴木の行は 2 日目の「夜」のあとで引用符が開く＝その行は「読めた」に入らない（手前のセルも反映しない）。
        var text = "山田,日,夜,休\n鈴木,日,\"夜,休\n佐藤,休,休,日\n";
        var v = Assert.IsType<CsvPartialImport.Verdict.Ask>(CsvPartialImport.Judge(text, BuildState(), Base()));
        Assert.Equal(1, v.EndLine);
        Assert.Equal(1, v.Matched);
        Assert.Equal(new[] { 0, 1, 2 }, v.Result.Schedule[0]);
        Assert.Equal(new[] { 2, 2, 2 }, v.Result.Schedule[1]);
        Assert.Equal(new[] { 2, 2, 2 }, v.Result.Schedule[2]);
    }

    [Fact]
    public void JudgeKeepsSkippingAmbiguousNamesInTheReadablePart()
    {
        var dup = BuildState(new List<Staff> { new("山田", 0), new("山田", 0), new("佐藤", 0) });
        var v = Assert.IsType<CsvPartialImport.Verdict.Ask>(
            CsvPartialImport.Judge("山田,日,日,日\n佐藤,夜,夜,夜\n鈴木,\"日\n", dup, Base()));
        Assert.Equal(1, v.Matched);
        Assert.Equal(new[] { "山田" }, v.Result.AmbiguousNames);
        Assert.Equal(new[] { 1, 1, 1 }, v.Result.Schedule[2]);
    }

    [Fact]
    public void JudgeReportsNothingReadableWhenTheQuoteOpensBeforeAnyRow()
    {
        var v = CsvPartialImport.Judge("\"山田,日,夜,休\n鈴木,日,日,日\n", BuildState(), Base());
        Assert.IsType<CsvPartialImport.Verdict.NothingReadable>(v);
    }

    [Fact]
    public void JudgeReportsNothingReadableWhenNoReadableRowMatchesAStaffName()
    {
        var v = CsvPartialImport.Judge("田中,日,夜,休\n高橋,休,休,日\n山田,\"日,日,日\n", BuildState(), Base());
        Assert.IsType<CsvPartialImport.Verdict.NothingReadable>(v);
    }

    [Fact]
    public void WordingMatchesTheDecidedTexts()
    {
        Assert.Equal("取り込める行がありませんでした（引用符が閉じていません）", CsvPartialImport.NothingReadable);
        Assert.Equal("盤面が変わったため取込をやめました。もう一度取り込んでください", CsvPartialImport.Stale);
        Assert.Equal("取込をやめました", CsvPartialImport.Cancelled);
        Assert.Equal("この部分だけ取り込む", CsvPartialImport.ConfirmLabel);
        Assert.Equal("やめる", CsvPartialImport.CancelLabel);
        Assert.Equal("｜⚠ 引用符（\"）が閉じていません。ここから後ろの行は読めていません", CsvPartialImport.AppliedWarning);
    }

    private static CsvPartialImport.Verdict.Ask Ask() =>
        Assert.IsType<CsvPartialImport.Verdict.Ask>(CsvPartialImport.Judge("山田,日,夜,休\n鈴木,\"日\n", BuildState(), Base()));

    [Fact]
    public void ResolveAppliesOnlyWhileStateAndBoardAreUnchanged()
    {
        var pending = new CsvPartialImport.Pending(Ask(), StateKey: 11L, BoardKey: 22L);
        var same = Assert.IsType<CsvPartialImport.Resolution.Apply>(CsvPartialImport.Resolve(pending, 11L, 22L));
        Assert.Same(pending, same.Pending);
        Assert.IsType<CsvPartialImport.Resolution.Stale>(CsvPartialImport.Resolve(pending, 12L, 22L));
        Assert.IsType<CsvPartialImport.Resolution.Stale>(CsvPartialImport.Resolve(pending, 11L, 23L));
    }

    [Fact]
    public void ResolveWithoutPendingDoesNothing()
    {
        Assert.IsType<CsvPartialImport.Resolution.NoPending>(CsvPartialImport.Resolve(null, 1L, 2L));
    }
}
