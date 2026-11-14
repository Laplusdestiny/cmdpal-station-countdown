using System.Globalization;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using StationCountdown.Core.Model;
using StationCountdown.Extension.Dock;
using StationCountdown.Extension.Services;

namespace StationCountdown.Extension.Pages;

/// <summary>
/// トップレベルの「駅カウントダウン」（spec §6.2）。登録した駅の一覧と、設定の誤り、駅の追加などのコマンドを出す。
/// </summary>
internal sealed partial class StationsPage : ListPage
{
    private const string WarningGlyph = "\uE7BA";

    private readonly StationStore _store;
    private readonly HolidayService _holidays;
    private readonly TimeProvider _time;
    private readonly StationActions _actions;

    public StationsPage(StationStore store, HolidayService holidays, TimeProvider time, StationActions actions)
    {
        _store = store;
        _holidays = holidays;
        _time = time;
        _actions = actions;

        Id = "com.laplusdestiny.stationcountdown.stations";
        Title = "駅カウントダウン";
        Name = "開く";
        Icon = new IconInfo(DepartureBand.TrainGlyph);
        PlaceholderText = "駅を探す";

        _store.Changed += () => RaiseItemsChanged();
    }

    public override IListItem[] GetItems()
    {
        var items = new List<IListItem>();

        // 設定に誤りがあれば先頭に出す。最後に正しく読めた設定で動き続けていることも示す。
        foreach (var error in _store.Errors)
        {
            items.Add(new ListItem(new OpenUrlCommand(AppPaths.StationsYaml) { Name = "stations.yaml を開く" })
            {
                Title = error.Line > 0
                    ? string.Create(CultureInfo.InvariantCulture, $"設定エラー（{error.Line} 行目）")
                    : "設定エラー",
                Subtitle = error.Message,
                Icon = new IconInfo(WarningGlyph),
            });
        }

        var now = _time.GetUtcNow();
        foreach (var station in _store.Current.Stations)
        {
            foreach (var error in _store.GetTimetableErrors(station.Id))
            {
                items.Add(new ListItem(new OpenUrlCommand(AppPaths.TimetablePath(station.Id)) { Name = "時刻表ファイルを開く" })
                {
                    Title = error.Line > 0
                        ? string.Create(CultureInfo.InvariantCulture, $"時刻表エラー: {station.Name} {station.DirectionLabel}（{error.Line} 行目）")
                        : $"時刻表エラー: {station.Name} {station.DirectionLabel}",
                    Subtitle = error.Message,
                    Icon = new IconInfo(WarningGlyph),
                });
            }
        }

        foreach (var station in _store.Current.Stations)
        {
            items.Add(new ListItem(new StationDeparturesPage(station.Id, _store, _holidays, _time))
            {
                Title = $"{station.Name} {station.DirectionLabel}",
                Subtitle = StationContext.FormatBand(station, _store, _holidays, now).Title,
                Icon = new IconInfo(DepartureBand.TrainGlyph),
                MoreCommands = _actions.ForStation(station),
            });
        }

        items.AddRange(_actions.Footer());
        return [.. items];
    }
}
