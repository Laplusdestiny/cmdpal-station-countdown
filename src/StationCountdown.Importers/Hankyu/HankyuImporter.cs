using StationCountdown.Core.Calendar;
using StationCountdown.Core.Model;

namespace StationCountdown.Importers.Hankyu;

/// <summary>
/// 阪急公式サイトから時刻表を取り込む（spec §7）。1 回の取り込みで送るのは、駅ページ 1 件と時刻表ページ 2 件だけ。
/// </summary>
public sealed class HankyuImporter(HttpClient http, TimeProvider time) : ITimetableImporter
{
    private const string AllowedPrefix = "https://www.hankyu.co.jp/";

    /// <summary>駅ページに「ダイヤ改正版」が載っていたときに、駅の ID を引数にして発火する。</summary>
    public event Action<string>? RevisedTimetableDetected;

    public bool CanImport(StationConfig station) => station.Source.Type == SourceType.Hankyu;

    public async Task<HankyuStationPage> FetchStationPageAsync(Uri pageUrl, CancellationToken ct)
    {
        EnsureAllowed(pageUrl);
        var html = await ImporterHttp.GetStringAsync(http, pageUrl, ct).ConfigureAwait(false);
        return HankyuStationPageParser.Parse(html, pageUrl);
    }

    public async Task<Timetable> ImportAsync(StationConfig station, CancellationToken ct)
    {
        var source = station.Source;
        if (!CanImport(station) || source.StationPageUrl is null || source.Line is null || source.Direction is null)
            throw new ArgumentException($"阪急の駅ではありません: {station.Id}", nameof(station));

        var page = await FetchStationPageAsync(new Uri(source.StationPageUrl), ct).ConfigureAwait(false);
        if (page.HasRevisedTimetable)
            RevisedTimetableDetected?.Invoke(station.Id);

        var link = page.Current.FirstOrDefault(d => d.Line == source.Line && d.Direction == source.Direction)
            ?? throw new ImportException(
                $"駅ページに「{source.Line} {source.Direction}」が見つかりません。"
                + $"候補: {string.Join(" / ", page.Current.Select(d => $"{d.Line} {d.Direction}"))}");

        EnsureAllowed(link.WeekdayUrl);
        EnsureAllowed(link.HolidayUrl);
        var weekday = HankyuTimetableParser.Parse(await ImporterHttp.GetStringAsync(http, link.WeekdayUrl, ct).ConfigureAwait(false));
        var holiday = HankyuTimetableParser.Parse(await ImporterHttp.GetStringAsync(http, link.HolidayUrl, ct).ConfigureAwait(false));

        // 時刻表ファイルには秒までしか書かないので、読み直したときと同じ値になるよう秒未満を切り捨てる。
        var now = time.GetUtcNow().ToOffset(ServiceDayResolver.Jst);
        var fetchedAt = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, now.Offset);

        return new Timetable(
            station.Id,
            fetchedAt,
            [link.WeekdayUrl.AbsoluteUri, link.HolidayUrl.AbsoluteUri],
            new Dictionary<DayType, IReadOnlyList<Departure>>
            {
                [DayType.Weekday] = weekday,
                [DayType.SaturdayHoliday] = holiday,
            });
    }

    /// <summary>阪急公式サイト以外の URL には取りに行かない（ページ内のリンクが外部を指していた場合も含む）。</summary>
    private static void EnsureAllowed(Uri url)
    {
        if (!url.AbsoluteUri.StartsWith(AllowedPrefix, StringComparison.Ordinal))
            throw new ImportException($"阪急公式サイト以外の URL は取得しません: {url}");
    }
}
