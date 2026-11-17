using System.Globalization;
using System.Text;
using StationCountdown.Core.Model;

namespace StationCountdown.Core.Timetables;

/// <summary>
/// 時刻表の要約（ダイヤ種別ごとの本数・始発・終電・時間帯ごとの本数・種別と行先）。
/// 手で書いたり AI で作ったりした時刻表を、元の時刻表と見比べて確かめるために使う。
/// </summary>
public static class TimetableSummary
{
    public static IReadOnlyList<DaySummary> Summarize(Timetable timetable)
        => [.. timetable.Services
            .Where(kv => kv.Value.Count > 0)
            .OrderBy(kv => kv.Key)
            .Select(kv => SummarizeDay(kv.Key, kv.Value))];

    /// <summary>ダイヤ種別の日本語の名前。</summary>
    public static string Label(DayType dayType) => dayType switch
    {
        DayType.Weekday => "平日",
        DayType.Saturday => "土曜",
        DayType.Holiday => "日曜・祝日",
        DayType.SaturdayHoliday => "土曜・休日",
        _ => throw new ArgumentOutOfRangeException(nameof(dayType), dayType, null),
    };

    /// <summary>要約を Markdown で書く。時間帯ごとの本数は、ダイヤ種別を列にした 1 つの表にする。</summary>
    public static string ToMarkdown(Timetable timetable)
    {
        var days = Summarize(timetable);
        var sb = new StringBuilder();
        if (days.Count == 0)
            return "電車が 1 本もありません。\n";

        foreach (var day in days)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"**{Label(day.DayType)}**: {day.Count} 本（始発 {day.First.ToLine()} ／ 終電 {day.Last.ToLine()}）\n\n");
            sb.Append("- 種別: ").Append(Join(day.TrainTypes)).Append('\n');
            sb.Append("- 行先: ").Append(Join(day.Destinations)).Append("\n\n");
        }

        var firstHour = days.Min(d => d.Hours[0].Hour);
        var lastHour = days.Max(d => d.Hours[^1].Hour);
        sb.Append("| 時 | ").AppendJoin(" | ", days.Select(d => Label(d.DayType))).Append(" |\n");
        sb.Append("| ---: |").Append(string.Concat(days.Select(_ => " ---: |"))).Append('\n');
        for (var hour = firstHour; hour <= lastHour; hour++)
        {
            var label = hour >= 24
                ? string.Create(CultureInfo.InvariantCulture, $"{hour}（{hour - 24}）")
                : hour.ToString(CultureInfo.InvariantCulture);
            sb.Append("| ").Append(label).Append(" | ");
            sb.AppendJoin(" | ", days.Select(d => CountAt(d, hour).ToString(CultureInfo.InvariantCulture)));
            sb.Append(" |\n");
        }
        return sb.ToString();
    }

    private static DaySummary SummarizeDay(DayType dayType, IReadOnlyList<Departure> departures)
    {
        var first = departures[0];
        var last = departures[^1];
        var hours = Enumerable.Range(first.Minutes / 60, last.Minutes / 60 - first.Minutes / 60 + 1)
            .Select(h => new HourCount(h, departures.Count(d => d.Minutes / 60 == h)))
            .ToArray();
        return new DaySummary(dayType, departures.Count, first, last, hours,
            CountByName(departures.Select(d => d.TrainType)),
            CountByName(departures.Select(d => d.Destination)));
    }

    /// <summary>名前ごとの本数。最初に出てきた順に並べる。</summary>
    private static NameCount[] CountByName(IEnumerable<string> names)
        => [.. names.GroupBy(n => n, StringComparer.Ordinal).Select(g => new NameCount(g.Key, g.Count()))];

    private static int CountAt(DaySummary day, int hour)
        => day.Hours.FirstOrDefault(h => h.Hour == hour)?.Count ?? 0;

    private static string Join(IReadOnlyList<NameCount> counts)
        => string.Join("、", counts.Select(c => string.Create(CultureInfo.InvariantCulture, $"{c.Name} {c.Count}")));
}

public sealed record DaySummary(
    DayType DayType,
    int Count,
    Departure First,
    Departure Last,
    IReadOnlyList<HourCount> Hours,
    IReadOnlyList<NameCount> TrainTypes,
    IReadOnlyList<NameCount> Destinations);

public sealed record HourCount(int Hour, int Count);

public sealed record NameCount(string Name, int Count);
