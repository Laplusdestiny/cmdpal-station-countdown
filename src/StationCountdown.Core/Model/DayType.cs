namespace StationCountdown.Core.Model;

/// <summary>ダイヤの種別（spec §4.3、§5.2）。</summary>
public enum DayType
{
    Weekday,
    Saturday,
    Holiday,
    SaturdayHoliday,
}
