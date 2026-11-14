namespace StationCountdown.Core.Model;

/// <summary>正規化した時刻表（spec §4.3）。<see cref="Services"/> の各リストは <see cref="Departure.Minutes"/> の昇順。</summary>
public sealed record Timetable(
    string StationId,
    DateTimeOffset? FetchedAt,
    IReadOnlyList<string> SourceUrls,
    IReadOnlyDictionary<DayType, IReadOnlyList<Departure>> Services);
