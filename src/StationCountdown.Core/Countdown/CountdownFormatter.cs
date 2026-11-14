using System.Globalization;
using StationCountdown.Core.Model;

namespace StationCountdown.Core.Countdown;

/// <summary>バンドや一覧の 1 行に出す文言。</summary>
public sealed record BandText(string Title, string Subtitle);

/// <summary>表示文言（spec §6.1、§6.2）。時刻は H:MM（時をゼロ埋めしない。24 時以降は 0 時台）で出す。</summary>
public static class CountdownFormatter
{
    public const string NextDaySeparatorTitle = "翌日";

    private const string Separator = " ／ ";
    private const string FetchFailedMarker = "取得失敗";

    public static BandText FormatBand(BandState state, StationConfig station, bool fetchFailed)
    {
        var text = state switch
        {
            BandState.Upcoming up => new BandText(
                $"{up.Next.TrainType} {up.Next.Destination} {Remain(up.RemainMinutes)}",
                up.Following is { } f
                    ? $"{up.Next.DisplayTime}発{Separator}次 {f.DisplayTime} {f.TrainType}"
                    : $"{up.Next.DisplayTime}発"),
            BandState.EndOfService end => new BandText(
                "本日終了",
                $"始発 {end.FirstTrain.DisplayTime} {end.FirstTrain.TrainType} {end.FirstTrain.Destination}"),
            BandState.NoTimetable => new BandText(
                "時刻表未取得",
                $"{station.Name} {station.DirectionLabel}"),
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };

        // 前回のデータで表示を続けていることを示す。どの状態でも末尾に付ける。
        return fetchFailed ? text with { Subtitle = text.Subtitle + Separator + FetchFailedMarker } : text;
    }

    public static BandText FormatListEntry(ListEntry entry)
    {
        var d = entry.Departure;
        return new BandText(
            $"{d.DisplayTime} {d.TrainType} {d.Destination}",
            entry.RemainMinutes is { } remain ? Remain(remain) : "間に合わない");
    }

    private static string Remain(int minutes)
        => minutes <= 0 ? "今すぐ" : string.Create(CultureInfo.InvariantCulture, $"あと{minutes}分");
}
