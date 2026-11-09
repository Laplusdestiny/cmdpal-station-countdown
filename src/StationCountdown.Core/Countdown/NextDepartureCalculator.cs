using StationCountdown.Core.Calendar;
using StationCountdown.Core.Model;

namespace StationCountdown.Core.Countdown;

/// <summary>次発と一覧の計算（spec §5.4、§5.5）。時刻はすべて JST の営業日を基準にする。</summary>
public static class NextDepartureCalculator
{
    public static BandState ComputeBand(DateTimeOffset now, CalcContext ctx)
    {
        var today = ServiceDayResolver.GetServiceDate(now);
        if (GetDepartures(today, ctx) is not { } departures)
            return new BandState.NoTimetable();

        // 発車時刻の昇順に並んでいるので、家を出る時刻も昇順。最初に間に合う 1 本と、その次の 1 本を取る。
        for (var i = 0; i < departures.Count; i++)
        {
            var leave = LeaveTime(today, departures[i], ctx.Station);
            if (leave >= now)
            {
                var following = i + 1 < departures.Count ? departures[i + 1] : null;
                return new BandState.Upcoming(departures[i], RemainMinutes(leave, now), following);
            }
        }

        // 本日終了。翌営業日（1 日分だけ見る）の始発を出す。
        return GetDepartures(today.AddDays(1), ctx) is [var first, ..]
            ? new BandState.EndOfService(first)
            : new BandState.NoTimetable();
    }

    public static IReadOnlyList<ListEntry> ComputeList(DateTimeOffset now, CalcContext ctx, int limit)
    {
        var result = new List<ListEntry>();
        if (limit <= 0)
            return result;

        var today = ServiceDayResolver.GetServiceDate(now);
        Append(result, today, isNextServiceDay: false);
        if (result.Count < limit)
            Append(result, today.AddDays(1), isNextServiceDay: true);

        return result.Count > limit ? result.GetRange(0, limit) : result;

        void Append(List<ListEntry> list, DateOnly date, bool isNextServiceDay)
        {
            // ダイヤが決まらない日は飛ばす（バンドとは違い、出せる分だけ出す）。
            foreach (var d in GetDepartures(date, ctx) ?? [])
            {
                if (list.Count >= limit)
                    return;
                if (DepartureTime(date, d) < now)
                    continue;   // もう発車した

                var leave = LeaveTime(date, d, ctx.Station);
                list.Add(new ListEntry(d, leave >= now ? RemainMinutes(leave, now) : null, isNextServiceDay));
            }
        }
    }

    /// <summary>営業日 <paramref name="date"/> に使うダイヤ。時刻表がない、またはダイヤが決まらなければ null。</summary>
    private static IReadOnlyList<Departure>? GetDepartures(DateOnly date, CalcContext ctx)
    {
        if (ctx.Timetable is not { } timetable)
            return null;

        var dayType = ServiceDayResolver.ResolveDayType(date, timetable.Services.Keys.ToArray(), ctx.Overrides, ctx.Holidays);
        return dayType is { } t && timetable.Services.TryGetValue(t, out var list) ? list : null;
    }

    private static DateTimeOffset DepartureTime(DateOnly date, Departure d)
        => ServiceDayResolver.GetServiceDayOrigin(date).AddMinutes(d.Minutes);

    private static DateTimeOffset LeaveTime(DateOnly date, Departure d, StationConfig station)
        => DepartureTime(date, d).AddMinutes(-station.WalkMinutes);

    /// <summary>家を出るまでの分数（切り捨て）。<paramref name="leave"/> ≥ <paramref name="now"/> のときだけ呼ぶ。</summary>
    private static int RemainMinutes(DateTimeOffset leave, DateTimeOffset now)
        => (int)Math.Floor((leave - now).TotalMinutes);
}
