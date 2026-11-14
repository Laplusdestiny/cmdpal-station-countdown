using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace StationCountdown.Importers.Hankyu;

/// <summary>駅ページにある、路線・方面ごとの時刻表ページへのリンク。</summary>
public sealed record HankyuDirectionLink(string Line, string Direction, Uri WeekdayUrl, Uri HolidayUrl);

/// <param name="Current">現行ダイヤのリンク。</param>
/// <param name="HasRevisedTimetable">日付付きディレクトリの「ダイヤ改正版」が載っているか。</param>
/// <param name="StationName">駅名（例: 岡町）。見つからなければ null。</param>
public sealed record HankyuStationPage(IReadOnlyList<HankyuDirectionLink> Current, bool HasRevisedTimetable, string? StationName = null);

/// <summary>
/// 阪急公式サイトの駅ページ（例: <c>umeda.html</c>）を解析する（spec §7.1）。
/// 構造は <c>div.timetable_cnt</c>（路線。<c>h4</c>）の中に、方面ごとの <c>div.timetable_cnt_in</c>
/// （<c>h5</c> と <c>ul.timetable_cnt_link</c> の「平日」「土曜・休日」のリンク）が並ぶ形。
/// </summary>
public static partial class HankyuStationPageParser
{
    // ダイヤ改正版は /station/html/20250222/HK-01_ta_2_w.html のように日付付きのディレクトリに置かれる。
    [GeneratedRegex(@"^/station/html/\d{8}/", RegexOptions.CultureInvariant)]
    private static partial Regex RevisedPathPattern();

    public static HankyuStationPage Parse(string html, Uri pageUrl)
    {
        var document = new HtmlParser().ParseDocument(html);

        var current = new List<HankyuDirectionLink>();
        var hasRevised = false;

        foreach (var block in document.QuerySelectorAll("div.timetable_cnt"))
        {
            var line = TextOf(block.QuerySelector("h4"));

            foreach (var dir in block.QuerySelectorAll("div.timetable_cnt_in"))
            {
                var direction = TextOf(dir.QuerySelector("h5"));
                Uri? weekday = null, holiday = null;
                var isRevised = false;

                foreach (var a in dir.QuerySelectorAll("ul.timetable_cnt_link a[href]"))
                {
                    if (!Uri.TryCreate(pageUrl, a.GetAttribute("href"), out var url))
                        continue;
                    if (RevisedPathPattern().IsMatch(url.AbsolutePath))
                    {
                        isRevised = true;
                        continue;
                    }

                    var label = a.TextContent;
                    if (label.Contains("平日", StringComparison.Ordinal))
                        weekday ??= url;
                    else if (label.Contains("土曜", StringComparison.Ordinal) || label.Contains("休日", StringComparison.Ordinal))
                        holiday ??= url;
                }

                if (isRevised)
                    hasRevised = true;
                else if (line.Length > 0 && direction.Length > 0 && weekday is not null && holiday is not null)
                    current.Add(new HankyuDirectionLink(line, direction, weekday, holiday));
            }
        }

        if (current.Count == 0)
            throw new ImportException($"駅ページに時刻表へのリンクが見つかりません。ページの構造が変わった可能性があります: {pageUrl}");

        return new HankyuStationPage(current, hasRevised, StationNameOf(document));
    }

    /// <summary>駅名は <c>h1</c>（例: <c>&lt;h1 data-basename="okamachi"&gt;岡町&lt;/h1&gt;</c>）。なければ title の「 | 」より前。</summary>
    private static string? StationNameOf(IDocument document)
    {
        var name = TextOf(document.QuerySelector("h1"));
        if (name.Length == 0)
            name = document.Title?.Split('|', 2)[0].Trim() ?? "";
        return name.Length > 0 ? name : null;
    }

    private static string TextOf(IElement? element) => element?.TextContent.Trim() ?? "";
}
