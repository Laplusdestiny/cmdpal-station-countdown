using StationCountdown.Core.Calendar;
using StationCountdown.Core.Countdown;
using StationCountdown.Core.Model;

namespace StationCountdown.Core.Tests;

public class NextDepartureCalculatorTests
{
    private static readonly TimeSpan Jst = TimeSpan.FromHours(9);

    private static readonly StationConfig Station =
        new("kakuu-kita", "架空北", "北方面", WalkMinutes: 8, new StationSource(SourceType.File));

    // weekday:          05:00 普通 A / 12:30 急行 B / 12:40 普通 A / 24:10 普通 A
    // saturdayHoliday:  06:00 普通 A
    private static readonly Timetable Table = new("kakuu-kita", null, [], new Dictionary<DayType, IReadOnlyList<Departure>>
    {
        [DayType.Weekday] = Lines("05:00 普通 A", "12:30 急行 B", "12:40 普通 A", "24:10 普通 A"),
        [DayType.SaturdayHoliday] = Lines("06:00 普通 A"),
    });

    private static Departure[] Lines(params string[] lines) => lines.Select(Departure.ParseLine).ToArray();

    private static CalcContext Ctx(Timetable? table = null, StationConfig? station = null, IReadOnlyList<DayTypeOverride>? overrides = null)
        => new(station ?? Station, table ?? Table, overrides ?? [], HolidayCalendar.Empty);

    /// <summary>2026-11-10（火）を基準にした JST の時刻。</summary>
    private static DateTimeOffset At(int day, int hour, int minute, int second = 0)
        => new(2026, 11, day, hour, minute, second, Jst);

    private static Departure D(string line) => Departure.ParseLine(line);

    // ---- ComputeBand ----

    [Fact]
    public void Band_skips_uncatchable()
    {
        var state = NextDepartureCalculator.ComputeBand(At(10, 12, 23), Ctx());

        var up = Assert.IsType<BandState.Upcoming>(state);
        Assert.Equal(D("12:40 普通 A"), up.Next);
        Assert.Equal(9, up.RemainMinutes);
        Assert.Equal(D("24:10 普通 A"), up.Following);
    }

    [Fact]
    public void Band_floors_remaining_minutes()
    {
        var up = Assert.IsType<BandState.Upcoming>(NextDepartureCalculator.ComputeBand(At(10, 12, 19, 1), Ctx()));

        Assert.Equal(D("12:30 急行 B"), up.Next);
        Assert.Equal(2, up.RemainMinutes);
        Assert.Equal(D("12:40 普通 A"), up.Following);
    }

    [Fact]
    public void Band_remain_zero_when_leave_equals_now()
    {
        var up = Assert.IsType<BandState.Upcoming>(NextDepartureCalculator.ComputeBand(At(10, 12, 22), Ctx()));

        Assert.Equal(D("12:30 急行 B"), up.Next);
        Assert.Equal(0, up.RemainMinutes);
    }

    [Fact]
    public void Band_remain_zero_within_last_minute()
    {
        var up = Assert.IsType<BandState.Upcoming>(NextDepartureCalculator.ComputeBand(At(10, 12, 21, 30), Ctx()));

        Assert.Equal(D("12:30 急行 B"), up.Next);
        Assert.Equal(0, up.RemainMinutes);
    }

    [Fact]
    public void Band_one_second_after_leave_moves_to_next_train()
    {
        var up = Assert.IsType<BandState.Upcoming>(NextDepartureCalculator.ComputeBand(At(10, 12, 22, 1), Ctx()));

        Assert.Equal(D("12:40 普通 A"), up.Next);
        Assert.Equal(9, up.RemainMinutes);
    }

    [Fact]
    public void Band_following_is_null_for_last_train()
    {
        var up = Assert.IsType<BandState.Upcoming>(NextDepartureCalculator.ComputeBand(At(10, 23, 0), Ctx()));

        Assert.Equal(D("24:10 普通 A"), up.Next);
        Assert.Null(up.Following);
    }

    [Fact]
    public void Band_after_midnight_uses_previous_service_day()
    {
        var up = Assert.IsType<BandState.Upcoming>(NextDepartureCalculator.ComputeBand(At(11, 0, 1), Ctx()));

        Assert.Equal(D("24:10 普通 A"), up.Next);
        Assert.Equal(1, up.RemainMinutes);
    }

    [Fact]
    public void Band_end_of_service_shows_next_day_first_train()
    {
        var end = Assert.IsType<BandState.EndOfService>(NextDepartureCalculator.ComputeBand(At(11, 0, 3), Ctx()));

        Assert.Equal(D("05:00 普通 A"), end.FirstTrain);
    }

    [Fact]
    public void Band_end_of_service_before_weekend_uses_weekend_first_train()
    {
        // 11-14（土）00:30。営業日は 11-13（金）で、24:10 にはもう間に合わない。翌営業日は土曜。
        var end = Assert.IsType<BandState.EndOfService>(NextDepartureCalculator.ComputeBand(At(14, 0, 30), Ctx()));

        Assert.Equal(D("06:00 普通 A"), end.FirstTrain);
    }

    [Fact]
    public void Band_converts_non_jst_now()   // Review Focus 2
    {
        var utc = At(10, 12, 23).ToUniversalTime();

        var up = Assert.IsType<BandState.Upcoming>(NextDepartureCalculator.ComputeBand(utc, Ctx()));

        Assert.Equal(D("12:40 普通 A"), up.Next);
        Assert.Equal(9, up.RemainMinutes);
    }

    [Fact]
    public void Band_walk_zero_counts_to_departure()
    {
        var station = Station with { WalkMinutes = 0 };

        var up = Assert.IsType<BandState.Upcoming>(NextDepartureCalculator.ComputeBand(At(10, 12, 23), Ctx(station: station)));

        Assert.Equal(D("12:30 急行 B"), up.Next);
        Assert.Equal(7, up.RemainMinutes);
    }

    [Fact]
    public void Band_no_timetable_when_null()
    {
        var ctx = new CalcContext(Station, null, [], HolidayCalendar.Empty);

        Assert.IsType<BandState.NoTimetable>(NextDepartureCalculator.ComputeBand(At(10, 12, 0), ctx));
    }

    [Fact]
    public void Band_no_timetable_when_day_type_missing()
    {
        var weekendOnly = Table with
        {
            Services = new Dictionary<DayType, IReadOnlyList<Departure>> { [DayType.SaturdayHoliday] = Lines("06:00 普通 A") },
        };

        Assert.IsType<BandState.NoTimetable>(NextDepartureCalculator.ComputeBand(At(10, 12, 0), Ctx(weekendOnly)));
    }

    [Fact]
    public void Band_no_timetable_when_next_day_type_missing_at_end_of_service()
    {
        // 金曜だけ平日ダイヤがあり、土曜のダイヤがない。金曜の終電後は翌営業日の始発が決まらない。
        var weekdayOnly = Table with
        {
            Services = new Dictionary<DayType, IReadOnlyList<Departure>> { [DayType.Weekday] = Lines("05:00 普通 A") },
        };

        Assert.IsType<BandState.NoTimetable>(NextDepartureCalculator.ComputeBand(At(13, 23, 0), Ctx(weekdayOnly)));
    }

    [Fact]
    public void Band_uses_override()
    {
        DayTypeOverride[] overrides = [new(new DateOnly(2026, 11, 10), DayType.SaturdayHoliday)];

        var end = NextDepartureCalculator.ComputeBand(At(10, 12, 0), Ctx(overrides: overrides));

        // 土休日ダイヤの 06:00 は過ぎているので本日終了。翌日（11-11 水）は平日ダイヤ。
        Assert.Equal(new BandState.EndOfService(D("05:00 普通 A")), end);
    }

    // ---- ComputeList ----

    [Fact]
    public void List_marks_uncatchable_and_appends_next_day()
    {
        var list = NextDepartureCalculator.ComputeList(At(10, 12, 25), Ctx(), limit: 4);

        Assert.Equal(
        [
            new ListEntry(D("12:30 急行 B"), null, false),
            new ListEntry(D("12:40 普通 A"), 7, false),
            new ListEntry(D("24:10 普通 A"), 697, false),   // 12:25 → 家を出る 24:02 まで 11 時間 37 分
            new ListEntry(D("05:00 普通 A"), 987, true),    // 12:25 → 翌日 4:52 まで 16 時間 27 分
        ], list);
    }

    [Fact]
    public void List_excludes_departed()
    {
        var list = NextDepartureCalculator.ComputeList(At(10, 12, 31), Ctx(), limit: 30);

        // 翌日分の 12:30 は並ぶので、当日分だけを見る。
        Assert.DoesNotContain(list, e => !e.IsNextServiceDay && e.Departure == D("12:30 急行 B"));
        Assert.Equal(D("12:40 普通 A"), list[0].Departure);
    }

    [Fact]
    public void List_includes_train_departing_this_minute()
    {
        var list = NextDepartureCalculator.ComputeList(At(10, 12, 30), Ctx(), limit: 30);

        Assert.Equal(new ListEntry(D("12:30 急行 B"), null, false), list[0]);
    }

    [Fact]
    public void List_respects_limit()
    {
        var list = NextDepartureCalculator.ComputeList(At(10, 4, 0), Ctx(), limit: 2);

        Assert.Equal([D("05:00 普通 A"), D("12:30 急行 B")], list.Select(e => e.Departure));
        Assert.All(list, e => Assert.False(e.IsNextServiceDay));
    }

    [Fact]
    public void List_appends_whole_next_day_until_limit()
    {
        var list = NextDepartureCalculator.ComputeList(At(10, 23, 0), Ctx(), limit: 30);

        Assert.Equal(5, list.Count);   // 当日の 24:10 と、翌日（水・平日）の 4 本
        Assert.False(list[0].IsNextServiceDay);
        Assert.All(list.Skip(1), e => Assert.True(e.IsNextServiceDay));
    }

    [Fact]
    public void List_empty_when_no_timetable()
    {
        var ctx = new CalcContext(Station, null, [], HolidayCalendar.Empty);

        Assert.Empty(NextDepartureCalculator.ComputeList(At(10, 12, 0), ctx, limit: 30));
    }

    [Fact]
    public void List_shows_next_day_when_today_has_no_service()
    {
        // 平日ダイヤしかない時刻表で、ダイヤが決まらない日は飛ばして、決まる日の分だけを並べる。
        // - 11-14（土）の昼: 当日（土）も翌営業日（日）もダイヤがないので空
        // - 11-13（金）の昼: 当日分だけ。翌営業日（土）の分はない
        var weekdayOnly = Table with
        {
            Services = new Dictionary<DayType, IReadOnlyList<Departure>> { [DayType.Weekday] = Lines("05:00 普通 A", "12:40 普通 A") },
        };

        Assert.Empty(NextDepartureCalculator.ComputeList(At(14, 12, 0), Ctx(weekdayOnly), limit: 30));
        Assert.Equal([new ListEntry(D("12:40 普通 A"), 32, false)], NextDepartureCalculator.ComputeList(At(13, 12, 0), Ctx(weekdayOnly), limit: 30));

        // 11-15（日）の昼。当日分はないが、翌営業日（月・平日）は出せる。
        var sunday = NextDepartureCalculator.ComputeList(new DateTimeOffset(2026, 11, 15, 12, 0, 0, Jst), Ctx(weekdayOnly), limit: 30);
        Assert.Equal(2, sunday.Count);
        Assert.All(sunday, e => Assert.True(e.IsNextServiceDay));
    }

    [Fact]
    public void List_limit_zero_or_less_is_empty()
        => Assert.Empty(NextDepartureCalculator.ComputeList(At(10, 12, 0), Ctx(), limit: 0));
}
