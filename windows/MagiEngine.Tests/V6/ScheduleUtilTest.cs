using MagiEngine.V6;

namespace MagiEngine.Tests.V6;

/// <summary>
/// [フェーズ7ピース1] <c>ScheduleUtil.FormatDay</c>'s port of <c>MirrorCore.kt</c>'s
/// <c>formatDay</c>. Every case below (including the two documented gaps) was cross-checked
/// against a real Kotlin runtime (kotlin-compiler-embeddable) before being trusted — see the
/// leniency dimensions 1-3 documented on <see cref="ScheduleUtil.FormatDay"/>'s own KDoc.
///
/// The other members of <c>ScheduleUtil</c> (<c>RestShiftIndex</c>/<c>FillShiftIndex</c>/
/// <c>CanDo</c>/<c>WishLocked</c>/<c>NormalizeSchedule</c>/<c>WeeklyFloorOfCount</c>/
/// <c>WeeklyDevOfBucket</c>/<c>CachedProblem</c>/etc.) were ported in phases 2-3 and are already
/// exercised incidentally by <c>ProblemTest.cs</c>/<c>ParityTest.cs</c>/the polish-pass test files
/// — not duplicated here.
/// </summary>
public class ScheduleUtilTest
{
    [Theory]
    [InlineData("2026-06-01", 0, "6/1(月)")]
    [InlineData("2026-06-01", 1, "6/2(火)")]
    [InlineData("2026-06-01", 30, "7/1(水)")]  // June has 30 days: rolls into July
    [InlineData("2026-01-01", -1, "12/31(水)")] // negative offset rolls back across a year boundary
    public void FormatDay_ComputesDateAndWeekdayFromValidInput(string startDate, int offset, string expected)
    {
        Assert.Equal(expected, ScheduleUtil.FormatDay(startDate, offset));
    }

    // Leniency dimension 1 (see FormatDay's KDoc): numeric field widths are not fixed at 4/2/2.
    [Theory]
    [InlineData("2026-06-1")]
    [InlineData("2026-6-01")]
    public void FormatDay_AcceptsUnpaddedMonthOrDay(string startDate)
    {
        Assert.Equal("6/1(月)", ScheduleUtil.FormatDay(startDate, 0));
    }

    // Leniency dimension 2 (see FormatDay's KDoc): leading whitespace is skipped, and only a
    // leading y-m-d prefix needs to match — trailing content (whitespace, a time-of-day suffix,
    // or arbitrary garbage) is silently ignored, matching Java's DateFormat.parse(String)
    // "some progress from position 0" contract rather than requiring a full-string match.
    [Theory]
    [InlineData("2026-06-01 ")]
    [InlineData(" 2026-06-01")]
    [InlineData("2026-06-01T00:00:00")]
    [InlineData("2026-06-01T12:34:56.789Z")]
    [InlineData("2026-06-01x")]
    public void FormatDay_IgnoresLeadingWhitespaceAndTrailingContent(string startDate)
    {
        Assert.Equal("6/1(月)", ScheduleUtil.FormatDay(startDate, 0));
    }

    // Leniency dimension 3 (see FormatDay's KDoc): out-of-range month/day values roll over via
    // calendar field-carry arithmetic rather than failing to parse, in all four directions.
    [Theory]
    [InlineData("2026-13-05", "1/5(火)")]   // month overflow -> carries into January of next year
    [InlineData("2026-02-30", "3/2(月)")]   // day overflow -> Feb 2026 has 28 days, carries into March
    [InlineData("2026-00-01", "12/1(月)")]  // month underflow -> carries into December of prior year
    [InlineData("2026-06-00", "5/31(日)")]  // day underflow -> carries into the last day of May
    public void FormatDay_RollsOverOutOfRangeFieldsLikeJavaCalendar(string startDate, string expected)
    {
        Assert.Equal(expected, ScheduleUtil.FormatDay(startDate, 0));
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("2026/06/01")] // '-' separators are literal, not lenient — '/' fails to parse at all
    public void FormatDay_FallsBackToOffsetPlusOneWhenUnparseable(string startDate)
    {
        Assert.Equal("1日", ScheduleUtil.FormatDay(startDate, 0));
    }

    [Fact]
    public void FormatDay_FallbackReflectsTheRequestedOffset()
    {
        Assert.Equal("6日", ScheduleUtil.FormatDay("garbage", 5));
    }

    // [Accepted, documented gaps — see FormatDay's KDoc] Confirmed against real Kotlin: these two
    // inputs produce a DIFFERENT result in this port than in the Android app, both firmly outside
    // anything the app's own date picker could ever produce as state.startDate.
    [Fact]
    public void FormatDay_DocumentedGap_AncientDatesUseGregorianNotJulianCalendar()
    {
        // Real Kotlin: "6/1(土)" (Saturday) — GregorianCalendar switches to Julian-calendar
        // weekday arithmetic before its 1582-10-15 cutover. This port is always proleptic
        // Gregorian (DateOnly has no calendar-system cutover), producing a different weekday for
        // the nominally "same" year-26 date.
        Assert.Equal("6/1(月)", ScheduleUtil.FormatDay("26-06-01", 0));
    }

    [Fact]
    public void FormatDay_DocumentedGap_YearZeroFallsBackInsteadOfProducingABcEraDate()
    {
        // Real Kotlin: "6/1(火)" — Calendar.YEAR is always non-negative with a separate BC/AD ERA
        // field, so year-field 0 becomes "1 BC" and a real date is produced. DateOnly's valid
        // range is 1-9999 (no BC/proleptic-negative-year support), so this throws internally and
        // falls to the same fallback branch as genuinely unparseable input.
        Assert.Equal("1日", ScheduleUtil.FormatDay("0000-06-01", 0));
    }
}
