using System.Globalization;

namespace StationCountdown.Extension.Services;

/// <summary>
/// <c>logs/yyyyMMdd.log</c> に追記するだけの簡単なログ（spec §4.1）。ログが書けなくても、拡張機能は止めない。
/// </summary>
internal static class Log
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    private static readonly Lock Gate = new();

    /// <summary>起動時に 1 回呼ぶ。7 日より古いログを削除する。</summary>
    public static void Initialize()
    {
        try
        {
            var limit = DateTime.Now - Retention;
            foreach (var file in Directory.EnumerateFiles(AppPaths.LogsDir, "*.log"))
            {
                if (File.GetLastWriteTime(file) < limit)
                    File.Delete(file);
            }
        }
#pragma warning disable CA1031 // ログの後始末の失敗で、拡張機能を止めない
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    public static void Info(string message) => Write("INFO ", message, null);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            var now = DateTimeOffset.Now;
            var line = string.Create(CultureInfo.InvariantCulture,
                $"{now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}{(exception is null ? "" : Environment.NewLine + exception)}{Environment.NewLine}");
            var path = Path.Combine(AppPaths.LogsDir, now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");

            lock (Gate)
            {
                File.AppendAllText(path, line);
            }
        }
#pragma warning disable CA1031 // ログが書けなくても、拡張機能は止めない
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }
}
