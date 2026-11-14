using Microsoft.CommandPalette.Extensions.Toolkit;
using StationCountdown.Extension.Pages;
using StationCountdown.Extension.Services;

namespace StationCountdown.Extension.Dock;

/// <summary>
/// Dock のバンドに出す 1 行（spec §6.1）。計算と文言は Core に任せ、ここでは結果を Title と Subtitle に入れるだけ。
/// クリックすると、Command Palette がその駅の発車一覧（<see cref="StationDeparturesPage"/>）で開く。
/// </summary>
internal sealed partial class DepartureBand : ListItem
{
    public const string TrainGlyph = "\uE7C0";   // Segoe Fluent Icons: Train

    private readonly StationStore _store;
    private readonly HolidayService _holidays;
    private readonly TimeProvider _time;

    public DepartureBand(string stationId, StationStore store, HolidayService holidays, TimeProvider time, StationActions actions)
        : base(new StationDeparturesPage(stationId, store, holidays, time))
    {
        StationId = stationId;
        _store = store;
        _holidays = holidays;
        _time = time;
        Icon = new IconInfo(TrainGlyph);
        if (store.FindStation(stationId) is { } station)
            MoreCommands = actions.ForBand(station);
        Refresh();
    }

    public string StationId { get; }

    public void Refresh()
    {
        var station = _store.FindStation(StationId);
        if (station is null)
            return;

        var text = StationContext.FormatBand(station, _store, _holidays, _time.GetUtcNow());

        // 値が変わったときだけ設定する（毎分の更新で、変化のない通知を Command Palette に送らない）。
        if (Title != text.Title)
            Title = text.Title;
        if (Subtitle != text.Subtitle)
            Subtitle = text.Subtitle;
    }
}
