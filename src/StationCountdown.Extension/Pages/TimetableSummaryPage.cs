using System.Text;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using StationCountdown.Core.Model;
using StationCountdown.Core.Timetables;
using StationCountdown.Extension.Services;

namespace StationCountdown.Extension.Pages;

/// <summary>
/// 「時刻表の要約」。ダイヤ種別ごとの本数・始発・終電・時間帯ごとの本数・種別と行先を出す。
/// 手で書いたり AI で作ったりした時刻表を、元の時刻表と見比べて確かめるためのもの。
/// </summary>
internal sealed partial class TimetableSummaryPage : ContentPage
{
    private readonly string _stationId;
    private readonly StationStore _store;

    public TimetableSummaryPage(string stationId, StationStore store)
    {
        _stationId = stationId;
        _store = store;
        Id = StationConfig.BandCommandIdPrefix + stationId + ".summary";
        Name = "時刻表の要約";
        Title = "時刻表の要約";
        Icon = new IconInfo("");   // BulletedList
    }

    public override IContent[] GetContent()
    {
        var sb = new StringBuilder();
        var station = _store.FindStation(_stationId);
        sb.Append("# ").Append(station is null ? _stationId : $"{station.Name} {station.DirectionLabel}").Append("\n\n");

        foreach (var warning in _store.GetTimetableWarnings(_stationId))
            sb.Append("- ⚠ ").Append(warning).Append('\n');

        if (_store.GetTimetable(_stationId) is { } timetable)
        {
            sb.Append('\n').Append(TimetableSummary.ToMarkdown(timetable));
        }
        else
        {
            foreach (var error in _store.GetTimetableErrors(_stationId))
                sb.Append("- ").Append(error).Append('\n');
            sb.Append("\n時刻表を読み込めていません。\n");
        }

        return [new MarkdownContent { Body = sb.ToString() }];
    }
}
