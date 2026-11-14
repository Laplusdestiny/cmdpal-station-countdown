using StationCountdown.Core.Model;

namespace StationCountdown.Core.Calendar;

/// <summary>営業日とダイヤ種別の決め方（spec §5.1、§5.2）。</summary>
public static class ServiceDayResolver
{
    /// <summary>日本標準時。夏時間がないので固定オフセットで Tokyo Standard Time と同じ結果になる。</summary>
    public static readonly TimeSpan Jst = TimeSpan.FromHours(9);

    /// <summary>営業日の区切り。0:00〜2:59 は前日の営業日（時刻表上は 24:00〜26:59）として扱う。</summary>
    public const int ServiceDayStartHour = 3;

    /// <summary>
    /// <paramref name="now"/> が属する営業日の日付。入力のオフセット（PC のタイムゾーン）には依存せず、常に JST で判定する。
    /// </summary>
    public static DateOnly GetServiceDate(DateTimeOffset now)
    {
        var jst = now.ToOffset(Jst);
        var date = DateOnly.FromDateTime(jst.DateTime);
        return jst.Hour < ServiceDayStartHour ? date.AddDays(-1) : date;
    }

    /// <summary>営業日 <paramref name="serviceDate"/> の JST 0:00。時刻表の分数はここから数える。</summary>
    public static DateTimeOffset GetServiceDayOrigin(DateOnly serviceDate)
        => new(serviceDate.ToDateTime(TimeOnly.MinValue), Jst);

    /// <summary>
    /// spec §5.2 の 1〜5 の順でダイヤ種別を決める。該当するダイヤが <paramref name="available"/> になければ null。
    /// </summary>
    public static DayType? ResolveDayType(
        DateOnly serviceDate,
        IReadOnlyCollection<DayType> available,
        IReadOnlyList<DayTypeOverride> overrides,
        HolidayCalendar holidays)
    {
        // 1. 上書き。その種別のダイヤがなければ、2 以降で判定し直す。
        foreach (var o in overrides)
        {
            if (o.Date == serviceDate && available.Contains(o.DayType))
                return o.DayType;
        }

        // 2. 日曜・祝日（祝日データの範囲外の年は IsHoliday が false になり、土日だけで判定する）。
        var dayOfWeek = serviceDate.DayOfWeek;
        if (dayOfWeek == DayOfWeek.Sunday || holidays.IsHoliday(serviceDate))
            return FirstAvailable(available, DayType.Holiday, DayType.SaturdayHoliday);

        // 3. 土曜。
        if (dayOfWeek == DayOfWeek.Saturday)
            return FirstAvailable(available, DayType.Saturday, DayType.SaturdayHoliday);

        // 4. 平日。5. なければ null。
        return FirstAvailable(available, DayType.Weekday);
    }

    private static DayType? FirstAvailable(IReadOnlyCollection<DayType> available, params ReadOnlySpan<DayType> candidates)
    {
        foreach (var c in candidates)
        {
            if (available.Contains(c))
                return c;
        }
        return null;
    }
}
