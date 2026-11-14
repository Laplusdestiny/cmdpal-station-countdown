#if !NO_IMPORTERS
using System.Globalization;
using StationCountdown.Core.Model;
using StationCountdown.Core.Refresh;
using StationCountdown.Core.Timetables;
using StationCountdown.Importers;
using StationCountdown.Importers.Hankyu;

namespace StationCountdown.Extension.Services;

/// <summary>
/// 時刻表の自動の再取得（spec §7.3、§8）。起動の少し後と、その後 5 分ごとに、取り込める駅について
/// <see cref="RefreshPolicy.NextAttempt"/> を確かめ、時刻が来ていれば取り込む。取り込みは 1 駅ずつ順に行う。
/// </summary>
internal sealed partial class RefreshScheduler : IDisposable
{
    private static readonly TimeSpan FirstCheck = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);

    private sealed class Attempts
    {
        public int Failures { get; set; }

        public DateTimeOffset? LastFailureAt { get; set; }
    }

    private readonly StationStore _store;
    private readonly ITimetableImporter[] _importers;
    private readonly TimeProvider _time;
    private readonly ITimer _timer;
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Attempts> _attempts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _revisedLogged = new(StringComparer.Ordinal);

    public RefreshScheduler(StationStore store, IEnumerable<ITimetableImporter> importers, TimeProvider time)
    {
        _store = store;
        _importers = [.. importers];
        _time = time;

        foreach (var hankyu in _importers.OfType<HankyuImporter>())
            hankyu.RevisedTimetableDetected += OnRevisedTimetable;

        _timer = time.CreateTimer(_ => _ = CheckDueAsync(), null, FirstCheck, CheckInterval);
    }

    public bool CanRefresh(StationConfig station) => _importers.Any(i => i.CanImport(station));

    /// <summary>今すぐ取り込む（フォームで追加したとき、手動の「時刻表を再取得」）。成功したら true。</summary>
    public async Task<bool> RefreshNowAsync(string stationId)
    {
        var station = _store.FindStation(stationId);
        if (station is null)
            return false;

        await _oneAtATime.WaitAsync(_shutdown.Token).ConfigureAwait(false);
        try
        {
            return await ImportAsync(station).ConfigureAwait(false);
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _timer.Dispose();
        foreach (var hankyu in _importers.OfType<HankyuImporter>())
            hankyu.RevisedTimetableDetected -= OnRevisedTimetable;
    }

    private async Task CheckDueAsync()
    {
        // 前回の確認がまだ終わっていなければ、今回は見送る。
        if (!await _oneAtATime.WaitAsync(0).ConfigureAwait(false))
            return;
        try
        {
            var interval = _store.Current.Settings.RefreshIntervalDays;
            foreach (var station in _store.Current.Stations)
            {
                if (_shutdown.IsCancellationRequested || !CanRefresh(station))
                    continue;

                Attempts attempts;
                lock (_gate)
                    attempts = _attempts.TryGetValue(station.Id, out var a) ? a : new Attempts();

                var now = _time.GetUtcNow();
                var next = RefreshPolicy.NextAttempt(now, _store.GetTimetable(station.Id)?.FetchedAt, interval,
                    attempts.Failures, attempts.LastFailureAt);
                if (next <= now)
                    await ImportAsync(station).ConfigureAwait(false);
            }
        }
#pragma warning disable CA1031 // タイマーの処理で拡張機能のプロセスを落とさない（spec §8）
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Log.Error("scheduled refresh failed", ex);
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    private async Task<bool> ImportAsync(StationConfig station)
    {
        var importer = _importers.FirstOrDefault(i => i.CanImport(station));
        if (importer is null)
            return false;

        Log.Info($"importing timetable: {station.Id}");
        try
        {
            var timetable = await importer.ImportAsync(station, _shutdown.Token).ConfigureAwait(false);
            TimetableWriter.WriteAtomic(AppPaths.TimetablePath(station.Id), timetable);

            lock (_gate)
                _attempts.Remove(station.Id);
            Log.Info(string.Create(CultureInfo.InvariantCulture,
                $"timetable imported: {station.Id} ({string.Join(", ", timetable.Services.Select(kv => $"{kv.Key} {kv.Value.Count}"))})"));

            _store.SetFetchFailed(station.Id, false);
            _store.Reload();
            return true;
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            return false;
        }
#pragma warning disable CA1031 // 取得の失敗（通信・解析・書き込み）は、前回の時刻表で表示を続ける（spec §8）
        catch (Exception ex)
#pragma warning restore CA1031
        {
            int failures;
            lock (_gate)
            {
                var attempts = _attempts.TryGetValue(station.Id, out var a) ? a : _attempts[station.Id] = new Attempts();
                attempts.Failures++;
                attempts.LastFailureAt = _time.GetUtcNow();
                failures = attempts.Failures;
            }
            Log.Error(string.Create(CultureInfo.InvariantCulture, $"timetable import failed: {station.Id} (failures: {failures})"), ex);
            _store.SetFetchFailed(station.Id, true);
            return false;
        }
    }

    private void OnRevisedTimetable(string stationId)
    {
        // 駅ページに古い改正版の表示が残ることがあるので、プロセスごとに 1 回だけ記録する。
        lock (_gate)
        {
            if (!_revisedLogged.Add(stationId))
                return;
        }
        Log.Info($"revised timetable available: {stationId}");
    }
}
#endif
