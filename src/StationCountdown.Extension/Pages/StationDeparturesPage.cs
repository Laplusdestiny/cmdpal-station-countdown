using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using StationCountdown.Core.Countdown;
using StationCountdown.Core.Model;
using StationCountdown.Extension.Dock;
using StationCountdown.Extension.Services;

namespace StationCountdown.Extension.Pages;

/// <summary>
/// 1 つの駅・方面の発車一覧（spec §5.5、§6.2）。まだ発車していない電車を並べ、間に合わないものには印を付ける。
/// 件数が上限に届かなければ、「翌日」の区切りのあとに翌営業日の始発から続ける。
/// </summary>
internal sealed partial class StationDeparturesPage : ListPage
{
    private readonly string _stationId;
    private readonly StationStore _store;
    private readonly HolidayService _holidays;
    private readonly TimeProvider _time;

    public StationDeparturesPage(string stationId, StationStore store, HolidayService holidays, TimeProvider time)
    {
        _stationId = stationId;
        _store = store;
        _holidays = holidays;
        _time = time;

        Id = StationConfig.BandCommandIdPrefix + stationId + ".departures";
        Name = "発車一覧を開く";
        Icon = new IconInfo(DepartureBand.TrainGlyph);
        Title = store.FindStation(stationId) is { } s ? $"{s.Name} {s.DirectionLabel}" : stationId;
    }

    public override IListItem[] GetItems()
    {
        var station = _store.FindStation(_stationId);
        if (station is null)
            return [Message("駅が見つかりません", $"stations.yaml に {_stationId} がありません。")];

        Title = $"{station.Name} {station.DirectionLabel}";

        var ctx = StationContext.Create(station, _store, _holidays);
        var entries = NextDepartureCalculator.ComputeList(_time.GetUtcNow(), ctx, _store.Current.Settings.DeparturesListLimit);
        if (entries.Count == 0)
        {
            var errors = _store.GetTimetableErrors(station.Id);
            return errors.Count > 0
                ? [Message("時刻表を読めません", string.Join(" / ", errors))]
                : [Message("時刻表未取得", ctx.Timetable is null
                    ? $"timetables\\{station.Id}.yaml がありません。"
                    : "今日と翌日のダイヤが見つかりません。")];
        }

        var items = new List<IListItem>(entries.Count + 1);
        var separatorAdded = false;
        foreach (var entry in entries)
        {
            if (entry.IsNextServiceDay && !separatorAdded)
            {
                items.Add(new Separator(CountdownFormatter.NextDaySeparatorTitle));
                separatorAdded = true;
            }

            var text = CountdownFormatter.FormatListEntry(entry);
            var catchable = entry.RemainMinutes is not null;
            items.Add(new ListItem(new NoOpCommand())
            {
                Title = text.Title,
                Subtitle = text.Subtitle,
                // 間に合わない電車はアイコンを外して目立たなくし、タグで示す。
                Icon = catchable ? new IconInfo(DepartureBand.TrainGlyph) : new IconInfo(string.Empty),
                Tags = catchable ? [] : [new Tag("間に合わない")],
            });
        }
        return [.. items];
    }

    private static ListItem Message(string title, string subtitle)
        => new(new NoOpCommand()) { Title = title, Subtitle = subtitle, Icon = new IconInfo("\uE7BA") };
}
