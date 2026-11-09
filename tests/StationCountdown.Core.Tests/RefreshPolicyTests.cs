using StationCountdown.Core.Refresh;

namespace StationCountdown.Core.Tests;

public class RefreshPolicyTests
{
    private static readonly TimeSpan Jst = TimeSpan.FromHours(9);
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, Jst);
    private static readonly DateTimeOffset FailedAt = new(2026, 11, 9, 11, 50, 0, Jst);

    private static DateTimeOffset At(int day, int hour, int minute) => new(2026, 11, day, hour, minute, 0, Jst);

    [Fact]
    public void Never_fetched_is_due_now()
        => Assert.Equal(Now, RefreshPolicy.NextAttempt(Now, fetchedAt: null, intervalDays: 7, consecutiveFailures: 0, lastFailureAt: null));

    [Fact]
    public void Fetched_waits_interval_days()
        => Assert.Equal(At(12, 12, 0), RefreshPolicy.NextAttempt(Now, At(5, 12, 0), 7, 0, null));

    [Fact]
    public void Overdue_fetch_is_in_the_past()
        => Assert.True(RefreshPolicy.NextAttempt(Now, At(1, 12, 0), 7, 0, null) <= Now);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Retries_every_30_minutes_up_to_max(int failures)
        => Assert.Equal(At(9, 12, 20), RefreshPolicy.NextAttempt(Now, At(5, 12, 0), 7, failures, FailedAt));

    [Fact]
    public void After_max_retries_waits_interval_from_last_failure()
        => Assert.Equal(At(16, 11, 50), RefreshPolicy.NextAttempt(Now, At(5, 12, 0), 7, 4, FailedAt));

    [Fact]
    public void After_max_retries_does_not_loop_when_fetchedAt_is_old()
    {
        // fetchedAt が古くても、最後の失敗から間隔を空ける（すぐに再試行を繰り返さない）。
        var next = RefreshPolicy.NextAttempt(Now, At(1, 0, 0), 7, 4, FailedAt);

        Assert.Equal(At(16, 11, 50), next);
    }

    [Fact]
    public void Retry_for_never_fetched_station()
        => Assert.Equal(At(9, 12, 20), RefreshPolicy.NextAttempt(Now, null, 7, 1, FailedAt));

    [Fact]
    public void Failures_without_timestamp_are_due_now()
        => Assert.Equal(Now, RefreshPolicy.NextAttempt(Now, At(5, 12, 0), 7, 2, null));

    [Fact]
    public void Constants_match_spec()
    {
        Assert.Equal(3, RefreshPolicy.MaxRetries);
        Assert.Equal(TimeSpan.FromMinutes(30), RefreshPolicy.RetryInterval);
    }
}
