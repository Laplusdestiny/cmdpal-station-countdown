using System.Globalization;
using Microsoft.Win32;

namespace StationCountdown.Extension.Services;

/// <summary>
/// 毎分 0 秒に、購読しているバンドを更新する（spec §5.6）。タイマーは 1 本だけで、購読者がいないときは止める。
/// 時刻設定の変更、スリープからの復帰、画面ロックの解除のときは、すぐに更新して次の分境界に予約し直す。
/// </summary>
internal sealed partial class TickService : IDisposable
{
    // 分境界の直前にタイマーが早めに発火すると、前の分のまま計算してしまうので、少し後ろにずらす。
    private static readonly TimeSpan BoundaryMargin = TimeSpan.FromMilliseconds(200);

    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly Dictionary<object, Action> _subscribers = new(ReferenceEqualityComparer.Instance);
    private ITimer? _timer;
    private bool _systemEventsHooked;
    private bool _disposed;

    public TickService(TimeProvider time)
    {
        _time = time;
        HookSystemEvents();
    }

    public void Subscribe(object client, Action onTick)
    {
        int count;
        lock (_gate)
        {
            if (_disposed)
                return;
            _subscribers[client] = onTick;
            count = _subscribers.Count;
            ScheduleLocked();
        }
        Log.Info(string.Create(CultureInfo.InvariantCulture, $"tick subscribed (subscribers: {count})"));
    }

    public void Unsubscribe(object client)
    {
        bool stopped;
        lock (_gate)
        {
            if (!_subscribers.Remove(client))
                return;
            stopped = _subscribers.Count == 0;
            if (stopped)
            {
                _timer?.Dispose();
                _timer = null;
            }
        }
        if (stopped)
            Log.Info("tick stopped");
    }

    /// <summary>今すぐ全購読者を更新し、次の分境界に予約し直す。</summary>
    public void TriggerNow() => Fire();

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _subscribers.Clear();
            _timer?.Dispose();
            _timer = null;
        }
        UnhookSystemEvents();
    }

    private void ScheduleLocked()
    {
        if (_subscribers.Count == 0 || _disposed)
            return;

        // JST はオフセットが整数時間なので、分境界は UTC と同じ。
        var now = _time.GetUtcNow();
        var intoMinute = TimeSpan.FromSeconds(now.Second) + TimeSpan.FromMilliseconds(now.Millisecond);
        var due = TimeSpan.FromMinutes(1) - intoMinute + BoundaryMargin;

        if (_timer is null)
            _timer = _time.CreateTimer(_ => Fire(), null, due, Timeout.InfiniteTimeSpan);
        else
            _timer.Change(due, Timeout.InfiniteTimeSpan);
    }

    private void Fire()
    {
        KeyValuePair<object, Action>[] snapshot;
        lock (_gate)
        {
            if (_disposed)
                return;
            snapshot = [.. _subscribers];
            ScheduleLocked();
        }

        // 長時間の放置・ロック・スリープのあとも更新が続いているかを、ログで確かめられるようにする（spec §10 の V1〜V3）。
        Log.Info(string.Create(CultureInfo.InvariantCulture, $"tick (subscribers: {snapshot.Length})"));

        foreach (var (_, onTick) in snapshot)
        {
            // 1 つのバンドの例外で、ほかのバンドやタイマーを止めない（spec §8）。
            try
            {
                onTick();
            }
#pragma warning disable CA1031
            catch (Exception ex)
#pragma warning restore CA1031
            {
                Log.Error("tick handler failed", ex);
            }
        }
    }

    private void HookSystemEvents()
    {
        try
        {
            SystemEvents.TimeChanged += OnTimeChanged;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;
            _systemEventsHooked = true;
        }
#pragma warning disable CA1031 // 通知が受けられなくても、毎分の更新は続ける
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Log.Error("failed to hook SystemEvents", ex);
        }
    }

    private void UnhookSystemEvents()
    {
        if (!_systemEventsHooked)
            return;
        SystemEvents.TimeChanged -= OnTimeChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _systemEventsHooked = false;
    }

    private void OnTimeChanged(object? sender, EventArgs e)
    {
        Log.Info("system time changed");
        TriggerNow();
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume)
            return;
        Log.Info("resumed from sleep");
        TriggerNow();
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason != SessionSwitchReason.SessionUnlock)
            return;
        Log.Info("session unlocked");
        TriggerNow();
    }
}
