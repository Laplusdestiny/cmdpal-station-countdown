using System.Text;
using StationCountdown.Core.Calendar;
using StationCountdown.Core.Model;
using static StationCountdown.Core.Model.DayType;

namespace StationCountdown.Core.Tests;

public class ServiceDayResolverTests
{
    private static readonly TimeSpan Jst = TimeSpan.FromHours(9);

    // 2026-11-10 火 / 11-14 土 / 11-15 日 / 11-23 月（勤労感謝の日）/ 2027-01-11 月（成人の日。データの範囲外）
    private static readonly DateOnly Tue = new(2026, 11, 10);
    private static readonly DateOnly Sat = new(2026, 11, 14);
    private static readonly DateOnly Sun = new(2026, 11, 15);
    private static readonly DateOnly HolidayMon = new(2026, 11, 23);
    private static readonly DateOnly UncoveredHolidayMon = new(2027, 1, 11);

    internal static HolidayCalendar Holidays2026 { get; } = MakeHolidays("2026/11/3", "2026/11/23");

    internal static HolidayCalendar MakeHolidays(params string[] dates)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var csv = "国民の祝日・休日月日,国民の祝日・休日名称\r\n" + string.Concat(dates.Select(d => $"{d},祝日\r\n"));
        return HolidayCalendar.ParseCsv(Encoding.GetEncoding(932).GetBytes(csv));
    }

    [Fact]
    public void GetServiceDate_before_3am_is_previous_day()
        => Assert.Equal(new DateOnly(2026, 11, 9), ServiceDayResolver.GetServiceDate(new DateTimeOffset(2026, 11, 10, 2, 59, 0, Jst)));

    [Fact]
    public void GetServiceDate_at_3am_is_same_day()
        => Assert.Equal(new DateOnly(2026, 11, 10), ServiceDayResolver.GetServiceDate(new DateTimeOffset(2026, 11, 10, 3, 0, 0, Jst)));

    [Fact]
    public void GetServiceDate_at_midnight_is_previous_day()
        => Assert.Equal(new DateOnly(2026, 11, 9), ServiceDayResolver.GetServiceDate(new DateTimeOffset(2026, 11, 10, 0, 0, 0, Jst)));

    [Fact]
    public void GetServiceDate_ignores_input_offset()   // Review Focus 2
    {
        var beforeUtc = new DateTimeOffset(2026, 11, 9, 17, 59, 0, TimeSpan.Zero);   // JST 11-10 02:59
        var atUtc = new DateTimeOffset(2026, 11, 9, 18, 0, 0, TimeSpan.Zero);        // JST 11-10 03:00

        Assert.Equal(new DateOnly(2026, 11, 9), ServiceDayResolver.GetServiceDate(beforeUtc));
        Assert.Equal(new DateOnly(2026, 11, 10), ServiceDayResolver.GetServiceDate(atUtc));

        var plusOne = TimeSpan.FromHours(1);
        Assert.Equal(new DateOnly(2026, 11, 9), ServiceDayResolver.GetServiceDate(beforeUtc.ToOffset(plusOne)));
        Assert.Equal(new DateOnly(2026, 11, 10), ServiceDayResolver.GetServiceDate(atUtc.ToOffset(plusOne)));

        var minusEight = TimeSpan.FromHours(-8);
        Assert.Equal(new DateOnly(2026, 11, 10), ServiceDayResolver.GetServiceDate(atUtc.ToOffset(minusEight)));
    }

    [Fact]
    public void GetServiceDayOrigin_is_jst_midnight()
    {
        var origin = ServiceDayResolver.GetServiceDayOrigin(new DateOnly(2026, 11, 10));

        Assert.Equal(new DateTimeOffset(2026, 11, 10, 0, 0, 0, Jst), origin);
        Assert.Equal(Jst, origin.Offset);
        Assert.Equal(new DateTimeOffset(2026, 11, 11, 0, 10, 0, Jst), origin.AddMinutes(1450));   // 24:10
    }

    public static TheoryData<string, DateOnly, DayType[], DayType?> ResolveCases => new()
    {
        { "平日", Tue, [Weekday, SaturdayHoliday], Weekday },
        { "土曜・土休日ダイヤ", Sat, [Weekday, SaturdayHoliday], SaturdayHoliday },
        { "土曜・土曜ダイヤあり", Sat, [Weekday, Saturday, Holiday], Saturday },
        { "日曜・休日ダイヤあり", Sun, [Weekday, Saturday, Holiday], Holiday },
        { "日曜・土休日ダイヤ", Sun, [Weekday, SaturdayHoliday], SaturdayHoliday },
        { "祝日の月曜", HolidayMon, [Weekday, SaturdayHoliday], SaturdayHoliday },
        { "祝日の月曜・休日ダイヤあり", HolidayMon, [Weekday, Saturday, Holiday], Holiday },
        { "平日・平日ダイヤなし", Tue, [SaturdayHoliday], null },
        { "日曜・休日系ダイヤなし", Sun, [Weekday, Saturday], null },
        { "範囲外の年の祝日（土日だけで判定）", UncoveredHolidayMon, [Weekday, SaturdayHoliday], Weekday },
    };

    [Theory]
    [MemberData(nameof(ResolveCases))]
    public void ResolveDayType_follows_spec_rules(string label, DateOnly date, DayType[] available, DayType? expected)
    {
        _ = label;
        Assert.Equal(expected, ServiceDayResolver.ResolveDayType(date, available, [], Holidays2026));
    }

    [Fact]
    public void ResolveDayType_override_wins()
    {
        DayTypeOverride[] overrides = [new(Tue, SaturdayHoliday)];

        Assert.Equal(SaturdayHoliday, ServiceDayResolver.ResolveDayType(Tue, [Weekday, SaturdayHoliday], overrides, Holidays2026));
    }

    [Fact]
    public void ResolveDayType_override_for_other_date_is_ignored()
    {
        DayTypeOverride[] overrides = [new(Tue.AddDays(1), SaturdayHoliday)];

        Assert.Equal(Weekday, ServiceDayResolver.ResolveDayType(Tue, [Weekday, SaturdayHoliday], overrides, Holidays2026));
    }

    [Fact]
    public void ResolveDayType_override_unavailable_falls_through()
    {
        DayTypeOverride[] overrides = [new(Tue, Saturday)];

        Assert.Equal(Weekday, ServiceDayResolver.ResolveDayType(Tue, [Weekday, SaturdayHoliday], overrides, Holidays2026));
    }

    [Fact]
    public void ResolveDayType_override_can_force_weekday_on_holiday()
    {
        DayTypeOverride[] overrides = [new(HolidayMon, Weekday)];

        Assert.Equal(Weekday, ServiceDayResolver.ResolveDayType(HolidayMon, [Weekday, SaturdayHoliday], overrides, Holidays2026));
    }
}
