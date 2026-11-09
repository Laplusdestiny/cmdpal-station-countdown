using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using StationCountdown.Extension.Dock;
using StationCountdown.Extension.Pages;
using StationCountdown.Extension.Services;

namespace StationCountdown.Extension;

public sealed partial class StationCountdownCommandsProvider : CommandProvider
{
    public const string ProviderId = "com.laplusdestiny.stationcountdown";

    private readonly TickService _tick = new(TimeProvider.System);
    private readonly ICommandItem[] _commands;
    private readonly ICommandItem[] _bands;

    public StationCountdownCommandsProvider()
    {
        Id = ProviderId;
        DisplayName = "Station Countdown";
        Icon = new IconInfo("");   // Segoe Fluent Icons: Train

        _commands =
        [
            new CommandItem(new PrototypePage()) { Title = "駅カウントダウン", Subtitle = "試作" },
        ];

        // 試作の段階では、固定の 1 バンドだけを返す（Task 13 で stations.yaml の駅ごとのバンドに置き換える）。
        var band = new PrototypeBand(TimeProvider.System);
        _bands =
        [
            new OnLoadBandItem(
                [band],
                PrototypeBand.BandId,
                "駅カウントダウン（試作）",
                onLoaded: () =>
                {
                    Log.Info("band loaded: " + PrototypeBand.BandId);
                    _tick.Subscribe(band, band.Refresh);
                    band.Refresh();
                },
                onUnloaded: () =>
                {
                    Log.Info("band unloaded: " + PrototypeBand.BandId);
                    _tick.Unsubscribe(band);
                }),
        ];
    }

    public override ICommandItem[] TopLevelCommands() => _commands;

    public override ICommandItem[]? GetDockBands() => _bands;

    internal void Shutdown() => _tick.Dispose();
}
