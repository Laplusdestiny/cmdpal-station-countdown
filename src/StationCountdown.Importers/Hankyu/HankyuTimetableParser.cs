using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using StationCountdown.Core.Model;

namespace StationCountdown.Importers.Hankyu;

/// <summary>
/// 阪急公式サイトの時刻表ページ（例: <c>HK-01_ta_2_w.html</c>）を解析する（spec §7.2）。
/// <c>li.timetable_hour</c>（時）と、その直後の <c>li.timetable_mins</c>（その時台の電車）が組になって並ぶ。
/// </summary>
public static partial class HankyuTimetableParser
{
    // 時は「5時」、分は「00分」のように読み上げ用の文字が後ろに付くので、先頭の数字だけを読む。
    [GeneratedRegex(@"^\s*(\d{1,2})(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingNumber();

    /// <returns>時刻の昇順に並んだ電車。0 時台以降は 24 時台以降として返す。</returns>
    /// <exception cref="ImportException">電車が 0 本、時や分が読めない・範囲外、時刻が昇順でない場合。</exception>
    public static IReadOnlyList<Departure> Parse(string html)
    {
        var document = new HtmlParser().ParseDocument(html);

        var departures = new List<Departure>();
        var previousHour = -1;

        foreach (var hourCell in document.QuerySelectorAll("li.timetable_hour"))
        {
            var rawHour = ReadNumber(hourCell, "時");

            // 時の欄は 5 → 23 → 0 の順に並ぶ。直前の時より小さい時が出てきたら、翌日の未明として 24 を足す。
            var hour = rawHour < previousHour ? rawHour + 24 : rawHour;
            previousHour = hour;

            var minutesCell = NextSibling(hourCell, "timetable_mins");
            if (minutesCell is null)
                continue;

            foreach (var minuteElement in minutesCell.QuerySelectorAll("p.timetable_min"))
            {
                var minute = ReadNumber(minuteElement, "分");
                var train = minuteElement.Closest("li") ?? minutesCell;
                var trainType = Clean(train.QuerySelector("p.timetable_train_type")?.TextContent);
                var destination = Clean(train.QuerySelector("p.timetable_station_name")?.TextContent);

                var line = string.Create(CultureInfo.InvariantCulture, $"{hour:00}:{minute:00} {trainType} {destination}");
                try
                {
                    departures.Add(Departure.ParseLine(line));
                }
                catch (FormatException ex)
                {
                    throw new ImportException($"時刻表の電車を読めません（{line}）。ページの構造が変わった可能性があります。", ex);
                }
            }
        }

        if (departures.Count == 0)
            throw new ImportException("時刻表に電車が 1 本も見つかりません。ページの構造が変わった可能性があります。");

        for (var i = 1; i < departures.Count; i++)
        {
            if (departures[i].Minutes < departures[i - 1].Minutes)
                throw new ImportException($"時刻表の時刻が昇順になっていません（{departures[i - 1].ToLine()} の次が {departures[i].ToLine()}）。");
        }

        return departures;
    }

    private static int ReadNumber(IElement element, string unit)
    {
        var m = LeadingNumber().Match(element.TextContent);
        if (!m.Success)
            throw new ImportException($"時刻表の「{unit}」を読めません: \"{element.TextContent.Trim()}\"");
        return int.Parse(m.Groups[1].ValueSpan, CultureInfo.InvariantCulture);
    }

    private static IElement? NextSibling(IElement element, string className)
    {
        for (var e = element.NextElementSibling; e is not null; e = e.NextElementSibling)
        {
            if (e.ClassList.Contains(className))
                return e;
            if (e.ClassList.Contains("timetable_hour"))
                return null;   // 次の時の欄まで来た
        }
        return null;
    }

    /// <summary>種別の「［］」「[]」と、空白（全角を含む）を取り除く。</summary>
    private static string Clean(string? text)
    {
        if (text is null)
            return "";

        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (!char.IsWhiteSpace(c) && c is not ('［' or '］' or '[' or ']'))
                sb.Append(c);
        }
        return sb.ToString();
    }
}
