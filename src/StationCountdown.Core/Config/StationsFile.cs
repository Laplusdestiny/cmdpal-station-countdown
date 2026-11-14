using System.Globalization;
using StationCountdown.Core.Model;
using StationCountdown.Core.Timetables;
using StationCountdown.Core.Yaml;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace StationCountdown.Core.Config;

/// <summary><c>stations.yaml</c> の読み書きと検証（spec §4.2、§6.3）。</summary>
public static class StationsFile
{
    public const int SchemaVersion = 1;
    public const string SchemaComment = "# yaml-language-server: $schema=./schemas/stations.schema.json";

    private const string DateFormat = "yyyy-MM-dd";

    public static LoadResult<StationsDocument> Parse(string yaml)
    {
        var r = new YamlNodeReader();
        var root = r.LoadRootMapping(yaml);
        if (root is null)
            return new(null, r.Errors);

        r.RejectUnknownKeys(root, "schemaVersion", "settings", "stations", "dayTypeOverrides");

        if (r.GetScalarNode(root, "schemaVersion", required: true) is { } version && version.Value != "1")
            r.Error(version, $"schemaVersion は {SchemaVersion} だけに対応しています: {version.Value}");

        var settings = ParseSettings(r, root);
        var stations = ParseStations(r, root);
        var overrides = ParseOverrides(r, root);

        return r.HasErrors
            ? new(null, r.Errors)
            : new(new StationsDocument(settings, stations, overrides), r.Errors);
    }

    /// <summary>
    /// 組み立てた文書（フォームの入力など）を、<see cref="Parse"/> と同じルールで検証する。
    /// YAML のノードがないので、エラーの行番号は 0 になる。
    /// </summary>
    public static IReadOnlyList<ConfigError> Validate(StationsDocument doc)
    {
        var errors = new List<ConfigError>();
        void Check(string? error)
        {
            if (error is not null)
                errors.Add(new ConfigError(0, error));
        }

        Check(StationRules.CheckRange("refreshIntervalDays", doc.Settings.RefreshIntervalDays,
            StationRules.RefreshIntervalDaysMin, StationRules.RefreshIntervalDaysMax));
        Check(StationRules.CheckRange("departuresListLimit", doc.Settings.DeparturesListLimit,
            StationRules.DeparturesListLimitMin, StationRules.DeparturesListLimitMax));

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in doc.Stations)
        {
            var idError = StationRules.CheckId(s.Id);
            Check(idError);
            if (idError is null && !seenIds.Add(s.Id))
                Check($"id が重複しています: {s.Id}");

            Check(StationRules.CheckNotBlank("name", s.Name));
            Check(StationRules.CheckNotBlank("directionLabel", s.DirectionLabel));
            Check(StationRules.CheckRange("walkMinutes", s.WalkMinutes, StationRules.WalkMinutesMin, StationRules.WalkMinutesMax));

            switch (s.Source.Type)
            {
                case SourceType.Hankyu:
                    Check(s.Source.StationPageUrl is { } url ? StationRules.CheckHankyuUrl(url) : "stationPageUrl がありません。");
                    Check(StationRules.CheckNotBlank("line", s.Source.Line ?? ""));
                    Check(StationRules.CheckNotBlank("direction", s.Source.Direction ?? ""));
                    break;
                case SourceType.File:
                    if (s.Source.StationPageUrl is not null || s.Source.Line is not null || s.Source.Direction is not null)
                        Check($"file 型の駅（{s.Id}）には stationPageUrl・line・direction を書けません。");
                    break;
                default:
                    Check($"source.type は hankyu か file にしてください: {s.Source.Type}");
                    break;
            }
        }

        var seenDates = new HashSet<DateOnly>();
        foreach (var o in doc.DayTypeOverrides)
        {
            if (!seenDates.Add(o.Date))
                Check($"同じ日付の上書きが複数あります: {o.Date.ToString(DateFormat, CultureInfo.InvariantCulture)}");
        }

        return errors;
    }

    public static string Serialize(StationsDocument doc)
    {
        var root = new YamlMappingNode
        {
            { "schemaVersion", Plain(SchemaVersion) },
            {
                "settings", new YamlMappingNode
                {
                    { "refreshIntervalDays", Plain(doc.Settings.RefreshIntervalDays) },
                    { "departuresListLimit", Plain(doc.Settings.DeparturesListLimit) },
                }
            },
        };

        var stations = new YamlSequenceNode();
        foreach (var s in doc.Stations)
        {
            var source = new YamlMappingNode { { "type", Plain(SourceTypeName(s.Source.Type)) } };
            if (s.Source.Type == SourceType.Hankyu)
            {
                source.Add("stationPageUrl", Plain(s.Source.StationPageUrl ?? ""));
                source.Add("line", Plain(s.Source.Line ?? ""));
                source.Add("direction", Plain(s.Source.Direction ?? ""));
            }

            stations.Add(new YamlMappingNode
            {
                { "id", Plain(s.Id) },
                { "name", Plain(s.Name) },
                { "directionLabel", Plain(s.DirectionLabel) },
                { "walkMinutes", Plain(s.WalkMinutes) },
                { "source", source },
            });
        }
        root.Add("stations", stations);

        if (doc.DayTypeOverrides.Count > 0)
        {
            var overrides = new YamlSequenceNode();
            foreach (var o in doc.DayTypeOverrides)
            {
                overrides.Add(new YamlMappingNode
                {
                    { "date", Plain(o.Date.ToString(DateFormat, CultureInfo.InvariantCulture)) },
                    { "dayType", Plain(DayTypeNames.ToName(o.DayType)) },
                }.WithStyle(MappingStyle.Flow));
            }
            root.Add("dayTypeOverrides", overrides);
        }

        return YamlText.Write(SchemaComment, root);
    }

    private static GlobalSettings ParseSettings(YamlNodeReader r, YamlMappingNode root)
    {
        var defaults = new GlobalSettings();
        if (r.GetMapping(root, "settings", required: false) is not { } map)
            return defaults;

        r.RejectUnknownKeys(map, "refreshIntervalDays", "departuresListLimit");
        return new GlobalSettings(
            RefreshIntervalDays: GetRangedInt(r, map, "refreshIntervalDays", required: false,
                StationRules.RefreshIntervalDaysMin, StationRules.RefreshIntervalDaysMax) ?? defaults.RefreshIntervalDays,
            DeparturesListLimit: GetRangedInt(r, map, "departuresListLimit", required: false,
                StationRules.DeparturesListLimitMin, StationRules.DeparturesListLimitMax) ?? defaults.DeparturesListLimit);
    }

    private static List<StationConfig> ParseStations(YamlNodeReader r, YamlMappingNode root)
    {
        var result = new List<StationConfig>();
        if (r.GetSequence(root, "stations", required: false) is not { } seq)
            return result;

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in seq.Children)
        {
            if (item is not YamlMappingNode map)
            {
                r.Error(item, "stations の要素は「キー: 値」の形で書いてください。");
                continue;
            }

            r.RejectUnknownKeys(map, "id", "name", "directionLabel", "walkMinutes", "source");

            var idNode = r.GetScalarNode(map, "id", required: true);
            if (idNode is not null && r.Check(idNode, StationRules.CheckId(idNode.Value!))
                && !seenIds.Add(idNode.Value!))
                r.Error(idNode, $"id が重複しています: {idNode.Value}");

            var name = GetNotBlank(r, map, "name");
            var directionLabel = GetNotBlank(r, map, "directionLabel");
            var walk = GetRangedInt(r, map, "walkMinutes", required: true, StationRules.WalkMinutesMin, StationRules.WalkMinutesMax);
            var source = ParseSource(r, map);

            if (idNode is not null && name is not null && directionLabel is not null && walk is not null && source is not null)
                result.Add(new StationConfig(idNode.Value!, name, directionLabel, walk.Value, source));
        }
        return result;
    }

    private static StationSource? ParseSource(YamlNodeReader r, YamlMappingNode station)
    {
        if (r.GetMapping(station, "source", required: true) is not { } map)
            return null;

        var typeNode = r.GetScalarNode(map, "type", required: true);
        switch (typeNode?.Value)
        {
            case null:
                return null;

            case "file":
                r.RejectUnknownKeys(map, "type");
                return new StationSource(SourceType.File);

            case "hankyu":
                r.RejectUnknownKeys(map, "type", "stationPageUrl", "line", "direction");
                var urlNode = r.GetScalarNode(map, "stationPageUrl", required: true);
                var urlOk = urlNode is not null && r.Check(urlNode, StationRules.CheckHankyuUrl(urlNode.Value!));
                var line = GetNotBlank(r, map, "line");
                var direction = GetNotBlank(r, map, "direction");
                return urlOk && line is not null && direction is not null
                    ? new StationSource(SourceType.Hankyu, urlNode!.Value, line, direction)
                    : null;

            default:
                r.Error(typeNode, $"source.type は hankyu か file にしてください: {typeNode.Value}");
                return null;
        }
    }

    private static List<DayTypeOverride> ParseOverrides(YamlNodeReader r, YamlMappingNode root)
    {
        var result = new List<DayTypeOverride>();
        if (r.GetSequence(root, "dayTypeOverrides", required: false) is not { } seq)
            return result;

        var seenDates = new HashSet<DateOnly>();
        foreach (var item in seq.Children)
        {
            if (item is not YamlMappingNode map)
            {
                r.Error(item, "dayTypeOverrides の要素は { date: 2026-12-31, dayType: saturdayHoliday } の形で書いてください。");
                continue;
            }

            r.RejectUnknownKeys(map, "date", "dayType");

            DateOnly? date = null;
            if (r.GetScalarNode(map, "date", required: true) is { } dateNode)
            {
                if (!DateOnly.TryParseExact(dateNode.Value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    r.Error(dateNode, $"date は 2026-12-31 の形で書いてください: {dateNode.Value}");
                else if (!seenDates.Add(d))
                    r.Error(dateNode, $"同じ日付の上書きが複数あります: {dateNode.Value}");
                else
                    date = d;
            }

            DayType? dayType = null;
            if (r.GetScalarNode(map, "dayType", required: true) is { } typeNode)
            {
                if (DayTypeNames.TryParse(typeNode.Value!, out var t))
                    dayType = t;
                else
                    r.Error(typeNode, $"dayType は {string.Join(" / ", DayTypeNames.All)} のいずれかにしてください: {typeNode.Value}");
            }

            if (date is not null && dayType is not null)
                result.Add(new DayTypeOverride(date.Value, dayType.Value));
        }
        return result;
    }

    private static int? GetRangedInt(YamlNodeReader r, YamlMappingNode map, string key, bool required, int min, int max)
    {
        var node = r.GetScalarNode(map, key, required);
        if (node is null)
            return null;
        if (!int.TryParse(node.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            r.Error(node, $"{key} は {min}〜{max} の整数で書いてください: {node.Value}");
            return null;
        }
        return r.Check(node, StationRules.CheckRange(key, value, min, max)) ? value : null;
    }

    private static string? GetNotBlank(YamlNodeReader r, YamlMappingNode map, string key)
    {
        var node = r.GetScalarNode(map, key, required: true);
        return node is not null && r.Check(node, StationRules.CheckNotBlank(key, node.Value!)) ? node.Value : null;
    }

    private static string SourceTypeName(SourceType type) => type switch
    {
        SourceType.Hankyu => "hankyu",
        SourceType.File => "file",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    private static YamlScalarNode Plain(string value) => new(value) { Style = ScalarStyle.Plain };

    private static YamlScalarNode Plain(int value) => Plain(value.ToString(CultureInfo.InvariantCulture));

    private static YamlMappingNode WithStyle(this YamlMappingNode node, MappingStyle style)
    {
        node.Style = style;
        return node;
    }
}
