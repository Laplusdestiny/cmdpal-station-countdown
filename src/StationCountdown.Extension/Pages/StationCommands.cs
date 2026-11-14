using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using StationCountdown.Core.Model;
using StationCountdown.Extension.Services;

namespace StationCountdown.Extension.Pages;

/// <summary>駅の削除（spec §6.2）。確認してから stations.yaml から取り除く。時刻表ファイルは残す。</summary>
internal sealed partial class DeleteStationCommand : InvokableCommand
{
    private readonly StationConfig _station;
    private readonly StationStore _store;

    public DeleteStationCommand(StationConfig station, StationStore store)
    {
        _station = station;
        _store = store;
        Id = StationConfig.BandCommandIdPrefix + station.Id + ".delete";
        Name = "削除";
        Icon = new IconInfo("");   // Delete
    }

    public override ICommandResult Invoke() => CommandResult.Confirm(new ConfirmationArgs
    {
        Title = "駅を削除",
        Description = $"「{_station.Name} {_station.DirectionLabel}」を削除しますか？ 時刻表ファイルは残ります。",
        IsPrimaryCommandCritical = true,
        PrimaryCommand = new AnonymousCommand(() =>
        {
            _store.Save(_store.Current.WithStationRemoved(_station.Id));
            Log.Info("station deleted from form: " + _station.Id);
        })
        {
            Name = "削除",
            Result = CommandResult.KeepOpen(),
        },
    });
}

#if !NO_IMPORTERS
/// <summary>「時刻表を再取得」（hankyu 型の駅だけ。spec §6.1）。終わったら結果を知らせる。</summary>
internal sealed partial class RefreshTimetableCommand : InvokableCommand
{
    private readonly StationConfig _station;
    private readonly RefreshScheduler _scheduler;

    public RefreshTimetableCommand(StationConfig station, RefreshScheduler scheduler)
    {
        _station = station;
        _scheduler = scheduler;
        Id = StationConfig.BandCommandIdPrefix + station.Id + ".refresh";
        Name = "時刻表を再取得";
        Icon = new IconInfo("");   // Refresh
    }

    public override ICommandResult Invoke()
    {
        var label = $"{_station.Name} {_station.DirectionLabel}";
        _ = Task.Run(async () =>
        {
            var ok = await _scheduler.RefreshNowAsync(_station.Id).ConfigureAwait(false);
            new ToastStatusMessage(ok ? $"{label} の時刻表を取得しました。" : $"{label} の時刻表を取得できませんでした。").Show();
        });
        return CommandResult.ShowToast(new ToastArgs { Message = $"{label} の時刻表を取得しています…", Result = CommandResult.KeepOpen() });
    }
}
#endif
