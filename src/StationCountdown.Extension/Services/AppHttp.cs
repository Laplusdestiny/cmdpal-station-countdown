using System.Reflection;

namespace StationCountdown.Extension.Services;

/// <summary>
/// 拡張機能が外部に取りに行くときの <see cref="HttpClient"/>（祝日 CSV など）。
/// インポーターを外したビルド（IncludeImporters=false）でも使えるように、Importers には依存しない。
/// </summary>
internal static class AppHttp
{
    public static string Version { get; } =
        typeof(AppHttp).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public static HttpClient Create()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"StationCountdown/{Version} (+https://github.com/Laplusdestiny/cmdpal-station-countdown)");
        return http;
    }
}
