using StationCountdown.Core.Model;
using StationCountdown.Core.Timetables;

namespace StationCountdown.Core.Tests;

public class TimetableSummaryTests
{
    private static Timetable Load(string yaml)
    {
        var r = TimetableFile.Parse(yaml, "kakuu-kita");
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        return r.Value!;
    }

    private const string Yaml = """
        schemaVersion: 1
        stationId: kakuu-kita
        services:
          weekday:
            - 05:00 普通 架空台
            - 05:30 急行 架空町
            - 07:10 普通 架空台
            - 24:05 普通 架空台
          saturdayHoliday:
            - 06:00 普通 架空台
        """;

    [Fact]
    public void Summarize_counts_trains_per_day_type()
    {
        var days = TimetableSummary.Summarize(Load(Yaml));

        Assert.Equal([DayType.Weekday, DayType.SaturdayHoliday], days.Select(d => d.DayType));
        var weekday = days[0];
        Assert.Equal(4, weekday.Count);
        Assert.Equal("05:00", weekday.First.ToLine()[..5]);
        Assert.Equal("24:05", weekday.Last.ToLine()[..5]);
    }

    [Fact]
    public void Summarize_lists_every_hour_between_first_and_last_including_empty_hours()
    {
        var weekday = TimetableSummary.Summarize(Load(Yaml))[0];

        Assert.Equal(Enumerable.Range(5, 20), weekday.Hours.Select(h => h.Hour));
        Assert.Equal(2, weekday.Hours.Single(h => h.Hour == 5).Count);
        Assert.Equal(0, weekday.Hours.Single(h => h.Hour == 6).Count);
        Assert.Equal(1, weekday.Hours.Single(h => h.Hour == 24).Count);
    }

    [Fact]
    public void Summarize_lists_train_types_and_destinations_with_counts_in_order_of_appearance()
    {
        var weekday = TimetableSummary.Summarize(Load(Yaml))[0];

        Assert.Equal([("普通", 3), ("急行", 1)], weekday.TrainTypes.Select(c => (c.Name, c.Count)));
        Assert.Equal([("架空台", 3), ("架空町", 1)], weekday.Destinations.Select(c => (c.Name, c.Count)));
    }

    [Fact]
    public void ToMarkdown_shows_hour_table_with_after_midnight_hours_labelled()
    {
        var md = TimetableSummary.ToMarkdown(Load(Yaml));

        Assert.Contains("| 時 | 平日 | 土曜・休日 |", md);
        Assert.Contains("| 5 | 2 | 0 |", md);
        Assert.Contains("| 6 | 0 | 1 |", md);
        // 24 時台は、元の時刻表で探しやすいように 0 時台とも書く。
        Assert.Contains("| 24（0） | 1 | 0 |", md);
        Assert.Contains("普通 3", md);
    }
}
