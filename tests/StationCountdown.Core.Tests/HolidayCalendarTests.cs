using System.Text;
using StationCountdown.Core.Calendar;

namespace StationCountdown.Core.Tests;

public class HolidayCalendarTests
{
    private const string Header = "国民の祝日・休日月日,国民の祝日・休日名称\r\n";

    static HolidayCalendarTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private static byte[] ShiftJis(string text) => Encoding.GetEncoding(932).GetBytes(text);

    [Fact]
    public void ParseCsv_reads_dates()
    {
        var cal = HolidayCalendar.ParseCsv(ShiftJis(Header + "2026/1/1,元日\r\n2026/11/3,文化の日\r\n"));

        Assert.True(cal.IsHoliday(new DateOnly(2026, 11, 3)));
        Assert.True(cal.IsHoliday(new DateOnly(2026, 1, 1)));
        Assert.False(cal.IsHoliday(new DateOnly(2026, 11, 4)));
        Assert.Equal(new DateOnly(2026, 11, 3), cal.LastDate);
    }

    [Fact]
    public void ParseCsv_ignores_header_blank_lines_and_whitespace()   // Review Focus 5
    {
        var csv = Header + "2026/1/1,元日\r\n 2026/5/6 ,休日 \r\n\r\n\r\n";

        var cal = HolidayCalendar.ParseCsv(ShiftJis(csv));

        Assert.True(cal.IsHoliday(new DateOnly(2026, 5, 6)));
        Assert.Equal(new DateOnly(2026, 5, 6), cal.LastDate);
    }

    [Fact]
    public void ParseCsv_accepts_lf_and_two_digit_month_day()
    {
        var cal = HolidayCalendar.ParseCsv(ShiftJis(Header.Replace("\r\n", "\n") + "2026/02/11,建国記念の日\n"));

        Assert.True(cal.IsHoliday(new DateOnly(2026, 2, 11)));
    }

    [Fact]
    public void ParseCsv_skips_unreadable_lines()
    {
        var cal = HolidayCalendar.ParseCsv(ShiftJis(Header + "2026/13/1,ありえない日\r\nnot a date\r\n2026/1/12,成人の日\r\n"));

        Assert.True(cal.IsHoliday(new DateOnly(2026, 1, 12)));
        Assert.Equal(new DateOnly(2026, 1, 12), cal.LastDate);
    }

    [Fact]
    public void LastDate_is_max_even_when_unsorted()
    {
        var cal = HolidayCalendar.ParseCsv(ShiftJis(Header + "2027/1/1,元日\r\n2026/11/3,文化の日\r\n"));

        Assert.Equal(new DateOnly(2027, 1, 1), cal.LastDate);
    }

    [Fact]
    public void Covers_false_after_last_date()
    {
        var cal = HolidayCalendar.ParseCsv(ShiftJis(Header + "2026/11/23,勤労感謝の日\r\n"));

        Assert.True(cal.Covers(new DateOnly(2026, 11, 23)));
        Assert.True(cal.Covers(new DateOnly(2026, 1, 1)));
        Assert.False(cal.Covers(new DateOnly(2026, 11, 24)));
        Assert.False(cal.Covers(new DateOnly(2026, 12, 31)));
    }

    [Fact]
    public void Empty_has_no_holidays_and_no_last_date()
    {
        Assert.False(HolidayCalendar.Empty.IsHoliday(new DateOnly(2026, 1, 1)));
        Assert.Null(HolidayCalendar.Empty.LastDate);
        Assert.False(HolidayCalendar.Empty.Covers(new DateOnly(2026, 1, 1)));
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x00 })]
    public void ParseCsv_garbage_gives_empty_calendar(byte[] bytes)
    {
        var cal = HolidayCalendar.ParseCsv(bytes);

        Assert.Null(cal.LastDate);
    }
}
