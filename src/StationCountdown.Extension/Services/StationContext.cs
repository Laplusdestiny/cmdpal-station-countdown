using StationCountdown.Core.Countdown;
using StationCountdown.Core.Model;

namespace StationCountdown.Extension.Services;

/// <summary>バンドや一覧ページが、駅ごとの計算の入力（<see cref="CalcContext"/>）を作るための共通処理。</summary>
internal static class StationContext
{
#if NO_IMPORTERS
    public const bool ImportersEnabled = false;
#else
    public const bool ImportersEnabled = true;
#endif

    public static CalcContext Create(StationConfig station, StationStore store, HolidayService holidays)
    {
        // インポーターを外したビルドでは、hankyu 型の駅は「時刻表未取得」と表示する（spec §3、Task 16）。
        var timetable = ImportersEnabled || station.Source.Type != SourceType.Hankyu ? store.GetTimetable(station.Id) : null;
        return new CalcContext(station, timetable, store.Current.DayTypeOverrides, holidays.Calendar);
    }

    public static BandText FormatBand(StationConfig station, StationStore store, HolidayService holidays, DateTimeOffset now)
    {
        var state = NextDepartureCalculator.ComputeBand(now, Create(station, store, holidays));
        return CountdownFormatter.FormatBand(state, station, store.IsFetchFailed(station.Id));
    }
}
