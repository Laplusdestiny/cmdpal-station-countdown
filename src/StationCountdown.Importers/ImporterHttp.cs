namespace StationCountdown.Importers;

/// <summary>インポーターが使う <see cref="HttpClient"/>（spec §7.3）。</summary>
public static class ImporterHttp
{
    public const string ProjectUrl = "https://github.com/Laplusdestiny/cmdpal-station-countdown";

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    /// <summary>User-Agent に名前・バージョン・連絡先を付けた <see cref="HttpClient"/> を作る。</summary>
    public static HttpClient Create(string version, HttpMessageHandler? handler = null)
    {
        var http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);
        http.Timeout = Timeout;
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"StationCountdown/{version} (+{ProjectUrl})");
        return http;
    }

    /// <summary>
    /// GET して本文を返す。HTTP のエラー、通信の失敗、タイムアウトは <see cref="ImportException"/> にする。
    /// 呼び出し側の <paramref name="ct"/> による取り消しは、そのまま <see cref="OperationCanceledException"/> として伝える。
    /// </summary>
    internal static async Task<string> GetStringAsync(HttpClient http, Uri url, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new ImportException($"取得に失敗しました（HTTP {(int)response.StatusCode}）: {url}");
            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new ImportException($"取得がタイムアウトしました: {url}", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ImportException($"取得に失敗しました（{ex.Message}）: {url}", ex);
        }
    }
}
