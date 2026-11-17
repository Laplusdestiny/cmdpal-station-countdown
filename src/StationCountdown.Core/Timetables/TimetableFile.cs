using System.Globalization;
using StationCountdown.Core.Calendar;
using StationCountdown.Core.Model;
using StationCountdown.Core.Yaml;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace StationCountdown.Core.Timetables;

/// <summary><c>timetables/{id}.yaml</c> の読み書き（spec §4.3）。</summary>
public static class TimetableFile
{
    public const int SchemaVersion = 1;
    public const string SchemaComment = "# yaml-language-server: $schema=../schemas/timetable.schema.json";

    private const string FetchedAtFormat = "yyyy-MM-dd'T'HH:mm:sszzz";
    private static readonly string[] FetchedAtFormats = [FetchedAtFormat, "yyyy-MM-dd'T'HH:mm:ss'Z'"];

    public static LoadResult<Timetable> Parse(string yaml, string expectedStationId)
    {
        var r = new YamlNodeReader();
        var root = r.LoadRootMapping(yaml);
        if (root is null)
            return new(null, r.Errors);

        r.RejectUnknownKeys(root, "schemaVersion", "stationId", "fetchedAt", "sourceUrls", "services");

        if (r.GetScalarNode(root, "schemaVersion", required: true) is { } version && version.Value != "1")
            r.Error(version, $"schemaVersion は {SchemaVersion} だけに対応しています: {version.Value}");

        var stationIdNode = r.GetScalarNode(root, "stationId", required: true);
        if (stationIdNode is not null && stationIdNode.Value != expectedStationId)
            r.Error(stationIdNode, $"stationId（{stationIdNode.Value}）がファイル名の ID（{expectedStationId}）と一致しません。");

        DateTimeOffset? fetchedAt = null;
        if (r.GetScalarNode(root, "fetchedAt", required: false) is { } fetchedNode)
        {
            if (DateTimeOffset.TryParseExact(fetchedNode.Value, FetchedAtFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var parsed))
                fetchedAt = parsed;
            else
                r.Error(fetchedNode, $"fetchedAt は 2026-11-09T12:00:00+09:00 の形で書いてください: {fetchedNode.Value}");
        }

        var sourceUrls = new List<string>();
        if (r.GetSequence(root, "sourceUrls", required: false) is { } urlSeq)
        {
            foreach (var item in r.ScalarItems(urlSeq, "sourceUrls"))
            {
                if (Uri.TryCreate(item.Value, UriKind.Absolute, out _))
                    sourceUrls.Add(item.Value!);
                else
                    r.Error(item, $"sourceUrls には URL を書いてください: {item.Value}");
            }
        }

        var services = new Dictionary<DayType, IReadOnlyList<Departure>>();
        if (r.GetMapping(root, "services", required: true) is { } servicesNode)
        {
            foreach (var (keyNode, valueNode) in servicesNode.Children)
            {
                var name = (keyNode as YamlScalarNode)?.Value;
                if (name is null || !DayTypeNames.TryParse(name, out var dayType))
                {
                    r.Error(keyNode, $"services のキーは {string.Join(" / ", DayTypeNames.All)} のいずれかにしてください: {name}");
                    continue;
                }
                if (valueNode is not YamlSequenceNode lines)
                {
                    r.Error(valueNode, $"services.{name} は「- HH:MM 種別 行先」のリストで書いてください。");
                    continue;
                }

                var departures = new List<Departure>(lines.Children.Count);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                Departure? previous = null;
                foreach (var line in r.ScalarItems(lines, $"services.{name}"))
                {
                    Departure departure;
                    try
                    {
                        departure = Departure.ParseLine(line.Value!);
                    }
                    catch (FormatException ex)
                    {
                        r.Error(line, ex.Message);
                        continue;
                    }

                    WarnIfSuspicious(r, line, departure, previous, seen);
                    departures.Add(departure);
                    previous = departure;
                }
                // 同じ時刻の電車は、ファイルに書いた順を保つ（安定ソート）。
                services[dayType] = departures.OrderBy(d => d.Minutes).ToArray();
            }

            if (servicesNode.Children.Count == 0)
                r.Error(servicesNode, "services にダイヤが 1 つもありません。");
        }

        if (r.HasErrors || stationIdNode is null)
            return new(null, r.Errors) { Warnings = r.Warnings };

        return new(new Timetable(stationIdNode.Value!, fetchedAt, sourceUrls, services), r.Errors) { Warnings = r.Warnings };
    }

    /// <summary>
    /// 読み込めるが誤りが疑われる行を警告する。手で書いたり AI で作ったりしたファイルに多い誤りを拾う。
    /// 1 行につき警告は 1 つまで（0〜2 時台 → 並び順 → 重複の順に見る）。
    /// </summary>
    private static void WarnIfSuspicious(YamlNodeReader r, YamlScalarNode line, Departure departure, Departure? previous, HashSet<string> seen)
    {
        var text = departure.ToLine();
        if (departure.Minutes < ServiceDayResolver.ServiceDayStartHour * 60)
        {
            var later = departure with { Minutes = departure.Minutes + 24 * 60 };
            r.Warning(line, $"{text[..5]} は 0〜2 時台です。営業日は 3:00 で切り替わるので、このままではカウントダウンに出ません。"
                + $"0 時以降の電車は 24 時以降で書いてください（例: {later.ToLine()[..5]}）。");
        }
        else if (previous is not null && departure.Minutes < previous.Minutes)
        {
            r.Warning(line, $"{text[..5]} が前の行の {previous.ToLine()[..5]} より早くなっています。"
                + "時刻の書き誤りか、行の抜け・入れ替わりがないか確かめてください。");
        }
        else if (seen.Contains(text))
        {
            r.Warning(line, $"同じ行（{text}）が 2 回あります。");
        }
        seen.Add(text);
    }

    public static string Serialize(Timetable timetable)
    {
        var root = new YamlMappingNode
        {
            { "schemaVersion", Plain(SchemaVersion.ToString(CultureInfo.InvariantCulture)) },
            { "stationId", Plain(timetable.StationId) },
        };

        if (timetable.FetchedAt is { } fetchedAt)
            root.Add("fetchedAt", Plain(fetchedAt.ToString(FetchedAtFormat, CultureInfo.InvariantCulture)));

        if (timetable.SourceUrls.Count > 0)
            root.Add("sourceUrls", new YamlSequenceNode(timetable.SourceUrls.Select(u => (YamlNode)Plain(u))));

        var services = new YamlMappingNode();
        foreach (var (dayType, departures) in timetable.Services.OrderBy(kv => kv.Key))
        {
            services.Add(DayTypeNames.ToName(dayType),
                new YamlSequenceNode(departures.OrderBy(d => d.Minutes).Select(d => (YamlNode)Plain(d.ToLine()))));
        }
        root.Add("services", services);

        return YamlText.Write(SchemaComment, root);
    }

    private static YamlScalarNode Plain(string value) => new(value) { Style = ScalarStyle.Plain };
}

/// <summary>YAML のキー名と <see cref="DayType"/> の対応（spec §4.3）。<c>stations.yaml</c> の読み書きでも使う。</summary>
internal static class DayTypeNames
{
    public static readonly string[] All = ["weekday", "saturday", "holiday", "saturdayHoliday"];

    public static string ToName(DayType dayType) => dayType switch
    {
        DayType.Weekday => "weekday",
        DayType.Saturday => "saturday",
        DayType.Holiday => "holiday",
        DayType.SaturdayHoliday => "saturdayHoliday",
        _ => throw new ArgumentOutOfRangeException(nameof(dayType), dayType, null),
    };

    public static bool TryParse(string name, out DayType dayType)
    {
        switch (name)
        {
            case "weekday": dayType = DayType.Weekday; return true;
            case "saturday": dayType = DayType.Saturday; return true;
            case "holiday": dayType = DayType.Holiday; return true;
            case "saturdayHoliday": dayType = DayType.SaturdayHoliday; return true;
            default: dayType = default; return false;
        }
    }
}
