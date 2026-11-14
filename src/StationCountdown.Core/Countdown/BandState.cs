using StationCountdown.Core.Calendar;
using StationCountdown.Core.Model;

namespace StationCountdown.Core.Countdown;

/// <summary>Dock のバンドに出す状態（spec §6.1）。</summary>
public abstract record BandState
{
    private BandState()
    {
    }

    /// <summary>間に合う次の電車がある。</summary>
    /// <param name="RemainMinutes">家を出るまでの分数（切り捨て）。0 は「今すぐ」。</param>
    /// <param name="Following">同じ営業日の、その次の電車。なければ null。</param>
    public sealed record Upcoming(Departure Next, int RemainMinutes, Departure? Following) : BandState;

    /// <summary>今の営業日の電車がもうない。翌営業日の始発を出す。</summary>
    public sealed record EndOfService(Departure FirstTrain) : BandState;

    /// <summary>時刻表がない、または該当するダイヤがない。</summary>
    public sealed record NoTimetable : BandState;
}

/// <summary>一覧ページの 1 行（spec §5.5）。</summary>
/// <param name="RemainMinutes">家を出るまでの分数。null は「間に合わない」。</param>
/// <param name="IsNextServiceDay">翌営業日の電車なら true（前に「翌日」の区切りを入れる）。</param>
public sealed record ListEntry(Departure Departure, int? RemainMinutes, bool IsNextServiceDay);

/// <summary>計算に必要な、駅ごとの入力。</summary>
public sealed record CalcContext(
    StationConfig Station,
    Timetable? Timetable,
    IReadOnlyList<DayTypeOverride> Overrides,
    HolidayCalendar Holidays);
