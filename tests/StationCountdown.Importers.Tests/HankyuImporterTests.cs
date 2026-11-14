using System.Net;
using StationCountdown.Core.Model;
using StationCountdown.Importers.Hankyu;

namespace StationCountdown.Importers.Tests;

public class HankyuImporterTests
{
    private const string PageUrl = "https://www.hankyu.co.jp/station/kakuu.html";
    private const string WeekdayUrl = "https://www.hankyu.co.jp/station/html/HK-99_kk_2_w.html";
    private const string HolidayUrl = "https://www.hankyu.co.jp/station/html/HK-99_kk_2_h.html";

    private static readonly DateTimeOffset Now = new(2026, 11, 9, 3, 0, 0, 500, TimeSpan.Zero);   // JST 12:00:00.5

    private static readonly StationConfig Station = new("kakuu-kita", "架空駅", "北方面", 5,
        new StationSource(SourceType.Hankyu, PageUrl, "架空線", "北方面"));

    private static readonly string StationPage = HankyuStationPageParserTests.Fixture("hankyu_station_page.html");
    private static readonly string Weekday = HankyuStationPageParserTests.Fixture("hankyu_timetable_weekday.html");
    private static readonly string Holiday = Weekday.Replace("hour_w", "hour_h").Replace("［急行］", "［準急］");

    private static (HankyuImporter Importer, FakeHandler Handler) Create(Dictionary<string, (HttpStatusCode, string)>? pages = null)
    {
        var handler = new FakeHandler(pages ?? new()
        {
            [PageUrl] = (HttpStatusCode.OK, StationPage),
            [WeekdayUrl] = (HttpStatusCode.OK, Weekday),
            [HolidayUrl] = (HttpStatusCode.OK, Holiday),
        });
        return (new HankyuImporter(ImporterHttp.Create("1.2.3", handler), new FixedTimeProvider(Now)), handler);
    }

    [Fact]
    public async Task ImportAsync_builds_timetable_from_both_pages()
    {
        var (importer, handler) = Create();

        var t = await importer.ImportAsync(Station, TestContext.Current.CancellationToken);

        Assert.Equal("kakuu-kita", t.StationId);
        Assert.Equal([DayType.Weekday, DayType.SaturdayHoliday], t.Services.Keys.Order());
        Assert.Equal("05:30 急行 架空台", t.Services[DayType.Weekday][1].ToLine());
        Assert.Equal("05:30 準急 架空台", t.Services[DayType.SaturdayHoliday][1].ToLine());
        Assert.Equal([WeekdayUrl, HolidayUrl], t.SourceUrls);
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.FromHours(9)), t.FetchedAt);
        Assert.Equal(TimeSpan.FromHours(9), t.FetchedAt!.Value.Offset);
        Assert.Equal([PageUrl, WeekdayUrl, HolidayUrl], handler.Requests.Select(r => r.RequestUri!.AbsoluteUri));
    }

    [Fact]
    public async Task ImportAsync_sends_user_agent()
    {
        var (importer, handler) = Create();

        await importer.ImportAsync(Station, TestContext.Current.CancellationToken);

        Assert.All(handler.Requests, r => Assert.Equal(
            "StationCountdown/1.2.3 (+https://github.com/Laplusdestiny/cmdpal-station-countdown)",
            r.Headers.UserAgent.ToString()));
    }

    [Fact]
    public void ImporterHttp_sets_timeout()
        => Assert.Equal(TimeSpan.FromSeconds(20), ImporterHttp.Create("1.2.3").Timeout);

    [Fact]
    public async Task ImportAsync_throws_when_direction_not_found()
    {
        var (importer, _) = Create();
        var station = Station with { Source = Station.Source with { Direction = "西方面" } };

        var ex = await Assert.ThrowsAsync<ImportException>(() => importer.ImportAsync(station, TestContext.Current.CancellationToken));

        Assert.Contains("架空線 北方面", ex.Message);   // 候補を示す
    }

    [Fact]
    public async Task ImportAsync_wraps_http_error()
    {
        var (importer, _) = Create(new() { [PageUrl] = (HttpStatusCode.InternalServerError, "") });

        var ex = await Assert.ThrowsAsync<ImportException>(() => importer.ImportAsync(Station, TestContext.Current.CancellationToken));

        Assert.Contains("500", ex.Message);
    }

    [Fact]
    public async Task ImportAsync_wraps_network_failure()
    {
        var (importer, handler) = Create();
        handler.Throw = new HttpRequestException("network down");

        var ex = await Assert.ThrowsAsync<ImportException>(() => importer.ImportAsync(Station, TestContext.Current.CancellationToken));

        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task ImportAsync_wraps_timeout()
    {
        var (importer, handler) = Create();
        handler.Throw = new TaskCanceledException("timeout", new TimeoutException());

        await Assert.ThrowsAsync<ImportException>(() => importer.ImportAsync(Station, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ImportAsync_propagates_caller_cancellation()
    {
        var (importer, _) = Create();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => importer.ImportAsync(Station, cts.Token));
    }

    [Fact]
    public async Task ImportAsync_wraps_parse_failure_of_timetable_page()
    {
        var (importer, _) = Create(new()
        {
            [PageUrl] = (HttpStatusCode.OK, StationPage),
            [WeekdayUrl] = (HttpStatusCode.OK, "<html></html>"),
            [HolidayUrl] = (HttpStatusCode.OK, Holiday),
        });

        await Assert.ThrowsAsync<ImportException>(() => importer.ImportAsync(Station, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ImportAsync_refuses_links_outside_hankyu()
    {
        var page = StationPage.Replace("/station/html/HK-99_kk_2_w.html", "https://example.com/HK-99_kk_2_w.html");
        var (importer, handler) = Create(new() { [PageUrl] = (HttpStatusCode.OK, page) });

        await Assert.ThrowsAsync<ImportException>(() => importer.ImportAsync(Station, TestContext.Current.CancellationToken));
        Assert.DoesNotContain(handler.Requests, r => r.RequestUri!.Host == "example.com");
    }

    [Fact]
    public async Task ImportAsync_rejects_non_hankyu_station()
    {
        var (importer, _) = Create();
        var station = Station with { Source = new StationSource(SourceType.File) };

        Assert.False(importer.CanImport(station));
        await Assert.ThrowsAsync<ArgumentException>(() => importer.ImportAsync(station, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void CanImport_hankyu_station()
        => Assert.True(Create().Importer.CanImport(Station));

    [Fact]
    public async Task ImportAsync_raises_revised_timetable_event()
    {
        var (importer, _) = Create();
        var raised = new List<string>();
        importer.RevisedTimetableDetected += raised.Add;

        await importer.ImportAsync(Station, TestContext.Current.CancellationToken);

        Assert.Equal(["kakuu-kita"], raised);
    }

    [Fact]
    public async Task ImportAsync_does_not_raise_event_without_revision()
    {
        var page = StationPage[..StationPage.IndexOf("id=\"diagram\"", StringComparison.Ordinal)] + "></div></body></html>";
        var (importer, _) = Create(new()
        {
            [PageUrl] = (HttpStatusCode.OK, page),
            [WeekdayUrl] = (HttpStatusCode.OK, Weekday),
            [HolidayUrl] = (HttpStatusCode.OK, Holiday),
        });
        var raised = new List<string>();
        importer.RevisedTimetableDetected += raised.Add;

        await importer.ImportAsync(Station, TestContext.Current.CancellationToken);

        Assert.Empty(raised);
    }

    [Fact]
    public async Task FetchStationPageAsync_returns_directions()
    {
        var (importer, _) = Create();

        var page = await importer.FetchStationPageAsync(new Uri(PageUrl), TestContext.Current.CancellationToken);

        Assert.Equal(2, page.Current.Count);
    }

    private sealed class FakeHandler(Dictionary<string, (HttpStatusCode Status, string Body)> pages) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public Exception? Throw { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            if (Throw is not null)
                throw Throw;

            var (status, body) = pages.TryGetValue(request.RequestUri!.AbsoluteUri, out var page) ? page : (HttpStatusCode.NotFound, "");
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "text/html"),
                RequestMessage = request,
            });
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
