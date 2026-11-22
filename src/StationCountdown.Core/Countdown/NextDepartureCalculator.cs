using StationCountdown.Core.Calendar;
using StationCountdown.Core.Model;

namespace StationCountdown.Core.Countdown;

/// <summary>次発と一覧の計算（spec §5.4、§5.5）。時刻はすべて JST の営業日を基準にする。</summary>
public static class NextDepartureCalculator
{
    public static BandState ComputeBand(DateTimeOffset now, CalcContext ctx)
    {
        var today = ServiceDayResolver.GetServiceDate(now);
        if (GetDepartures(today, ctx) is not { } all)
            return new BandState.NoTimetable();

        // 表示する種別だけにしてから探す（#13）。
        var departures = Filter(all, ctx.Station);

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
        var nextAll = GetDepartures(today.AddDays(1), ctx);
        if (nextAll is not null && Filter(nextAll, ctx.Station) is [var first, ..])
            return new BandState.EndOfService(first);

        // 電車はあるのに、絞り込んだせいで出せる電車がない。
        return nextAll is [_, ..] || (all.Count > 0 && departures.Count == 0)
            ? new BandState.NoMatchingTrains()
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
            foreach (var d in Filter(GetDepartures(date, ctx) ?? [], ctx.Station))
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

    /// <summary>一覧が、表示する種別で絞り込んだせいで空になっているか（#13）。</summary>
    public static bool IsListHiddenByTrainTypes(DateTimeOffset now, CalcContext ctx, int limit)
        => ctx.Station.TrainTypes.Count > 0
           && ComputeList(now, ctx, limit).Count == 0
           && ComputeList(now, ctx with { Station = ctx.Station with { TrainTypes = [] } }, limit).Count > 0;

    /// <summary>表示する種別の電車だけを残す。<see cref="StationConfig.TrainTypes"/> が空なら、そのまま返す。</summary>
    private static IReadOnlyList<Departure> Filter(IReadOnlyList<Departure> departures, StationConfig station)
        => station.TrainTypes.Count == 0
            ? departures
            : departures.Where(d => station.TrainTypes.Contains(d.TrainType, StringComparer.Ordinal)).ToList();

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
