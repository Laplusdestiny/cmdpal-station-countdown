namespace StationCountdown.Core.Refresh;

/// <summary>時刻表を自動で再取得するタイミング（spec §7.3、§8）。</summary>
public static class RefreshPolicy
{
    /// <summary>失敗したあとに再試行する最大回数。</summary>
    public const int MaxRetries = 3;

    public static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(30);

    /// <summary>
    /// 次に取得を試みる時刻。<paramref name="now"/> 以前の値なら、すぐに取得してよい。
    /// </summary>
    /// <param name="fetchedAt">最後に成功した時刻。一度も取得していなければ null。</param>
    /// <param name="consecutiveFailures">続けて失敗した回数（成功すると 0 に戻る）。</param>
    /// <param name="lastFailureAt">最後に失敗した時刻。</param>
    public static DateTimeOffset NextAttempt(
        DateTimeOffset now,
        DateTimeOffset? fetchedAt,
        int intervalDays,
        int consecutiveFailures,
        DateTimeOffset? lastFailureAt)
    {
        if (consecutiveFailures > 0)
        {
            if (lastFailureAt is not { } failedAt)
                return now;

            // 最初の失敗と 3 回の再試行（計 4 回）までは 30 分おき。それでも駄目なら、次回の定期取得まで待つ。
            // 定期取得は最後の失敗から数える（fetchedAt が古いと、すぐに再試行を繰り返してしまうため）。
            return consecutiveFailures <= MaxRetries
                ? failedAt + RetryInterval
                : failedAt.AddDays(intervalDays);
        }

        return fetchedAt is { } fetched ? fetched.AddDays(intervalDays) : now;
    }
}
