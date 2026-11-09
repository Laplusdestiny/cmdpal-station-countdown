using System.Globalization;
using Microsoft.CommandPalette.Extensions.Toolkit;
using StationCountdown.Core.Calendar;
using StationCountdown.Core.Model;
using StationCountdown.Extension.Services;

namespace StationCountdown.Extension.Dock;

/// <summary>
/// spec §10 の V1〜V3・V6 を確かめるための試作バンド（Task 13 で本番のバンドに置き換える）。
/// Title に JST の時刻、Subtitle に更新回数を出し、更新のたびにログにも記録する。
/// </summary>
internal sealed partial class PrototypeBand : ListItem
{
    public const string BandId = StationConfig.BandCommandIdPrefix + "prototype";

    private readonly TimeProvider _time;
    private int _count;

    public PrototypeBand(TimeProvider time)
        : base(new NoOpCommand { Id = BandId + ".item" })
    {
        _time = time;
        Icon = new IconInfo("");   // Segoe Fluent Icons: Train
        Refresh();
    }

    public void Refresh()
    {
        var now = _time.GetUtcNow().ToOffset(ServiceDayResolver.Jst);
        var count = Interlocked.Increment(ref _count);
        Title = string.Create(CultureInfo.InvariantCulture, $"試作 {now:H:mm}");
        Subtitle = string.Create(CultureInfo.InvariantCulture, $"更新 {count} 回目");
        Log.Info(string.Create(CultureInfo.InvariantCulture, $"prototype band refreshed: {Title} / {Subtitle}"));
    }
}
