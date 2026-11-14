using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using StationCountdown.Extension.Dock;
using StationCountdown.Extension.Pages;
using StationCountdown.Extension.Services;

namespace StationCountdown.Extension;

public sealed partial class StationCountdownCommandsProvider : CommandProvider
{
    public const string ProviderId = "com.laplusdestiny.stationcountdown";

    private static readonly TimeSpan HolidayCheckInterval = TimeSpan.FromHours(6);

    private readonly TimeProvider _time = TimeProvider.System;
    private readonly TickService _tick;
    private readonly StationStore _store;
    private readonly HolidayService _holidays = new();
    private readonly HttpClient _http = AppHttp.Create();
    private readonly ITimer _holidayTimer;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly StationActions _actions;
    private readonly ICommandItem[] _commands;

    private readonly Lock _bandsGate = new();
    private ICommandItem[] _bands = [];
    private DepartureBand[] _bandItems = [];
    private string _bandsKey = "";

    public StationCountdownCommandsProvider()
    {
        Id = ProviderId;
        DisplayName = "Station Countdown";
        Icon = new IconInfo(DepartureBand.TrainGlyph);

        _tick = new TickService(_time);
        _holidays.LoadLocal();
        _store = new StationStore(_time);
        _actions = new StationActions(_store);

        _commands =
        [
            new CommandItem(new StationsPage(_store, _holidays, _time, _actions))
            {
                Title = "駅カウントダウン",
                Subtitle = "登録した駅と方面の、家を出るまでの時間",
            },
        ];

        RebuildBands(force: true);
        _store.Changed += OnDataChanged;
        _holidays.Changed += OnDataChanged;

        // 祝日データは起動時と、その後 6 時間ごとに確かめる（取りに行くのは足りないときだけ、1 日 1 回まで）。
        _holidayTimer = _time.CreateTimer(_ => CheckHolidays(), null, TimeSpan.FromSeconds(10), HolidayCheckInterval);
    }

    public override ICommandItem[] TopLevelCommands() => _commands;

    public override ICommandItem[]? GetDockBands()
    {
        lock (_bandsGate)
            return _bands;
    }

    internal void Shutdown()
    {
        _shutdown.Cancel();
        _holidayTimer.Dispose();
        _store.Dispose();
        _tick.Dispose();
        _http.Dispose();
    }

    private void OnDataChanged() => RebuildBands(force: false);

    /// <summary>
    /// 駅の顔ぶれ（ID・表示名・方面）が変わったときだけバンドを作り直し、Command Palette に知らせる。
    /// 変わっていなければ、今のバンドの表示を更新するだけにする（ピン留めや表示中の状態を保つため）。
    /// </summary>
    private void RebuildBands(bool force)
    {
        var stations = _store.Current.Stations;
        var key = string.Join('\n', stations.Select(s => $"{s.Id}\t{s.Name}\t{s.DirectionLabel}"));

        DepartureBand[] toRefresh;
        DepartureBand[] removed = [];
        var rebuilt = false;
        lock (_bandsGate)
        {
            if (force || key != _bandsKey)
            {
                removed = _bandItems;
                var items = stations.Select(s => new DepartureBand(s.Id, _store, _holidays, _time, _actions)).ToArray();
                _bands = stations.Zip(items, (station, band) => (ICommandItem)new OnLoadBandItem(
                    [band],
                    station.BandCommandId,
                    $"{station.Name} {station.DirectionLabel}",
                    onLoaded: () =>
                    {
                        _tick.Subscribe(band, band.Refresh);
                        band.Refresh();
                    },
                    onUnloaded: () => _tick.Unsubscribe(band))).ToArray();
                _bandItems = items;
                _bandsKey = key;
                rebuilt = true;
            }
            toRefresh = _bandItems;
        }

        foreach (var band in removed)
            _tick.Unsubscribe(band);

        if (rebuilt)
        {
            Log.Info($"dock bands rebuilt ({toRefresh.Length})");
            if (!force)
                RaiseItemsChanged();
        }
        else
        {
            foreach (var band in toRefresh)
                band.Refresh();
        }
    }

    private void CheckHolidays()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _holidays.EnsureCurrentAsync(_http, _time, _shutdown.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
#pragma warning disable CA1031 // タイマーの処理で拡張機能のプロセスを落とさない（spec §8）
            catch (Exception ex)
#pragma warning restore CA1031
            {
                Log.Error("holiday check failed", ex);
            }
        });
    }
}
