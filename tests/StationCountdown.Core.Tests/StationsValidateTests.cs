using StationCountdown.Core.Config;
using StationCountdown.Core.Model;

namespace StationCountdown.Core.Tests;

public class StationsValidateTests
{
    private static readonly StationsDocument Good = StationsFile.Parse(StationsFileTests.Valid).Value!;

    private static StationConfig Hankyu => Good.Stations[0];

    private static StationConfig File => Good.Stations[1];

    private static StationsDocument WithFirst(StationConfig station) => Good with { Stations = [station, File] };

    public static TheoryData<string, StationsDocument, bool> InvalidDocuments => new()
    {
        { "id が大文字と _", WithFirst(Hankyu with { Id = "Bad_ID" }), true },
        { "id の - が連続", WithFirst(Hankyu with { Id = "kakuu--kita" }), true },
        { "id が 65 文字", WithFirst(Hankyu with { Id = new string('a', 65) }), true },
        { "id の重複", Good with { Stations = [Hankyu, File with { Id = Hankyu.Id }] }, true },
        { "name が空", WithFirst(Hankyu with { Name = "" }), true },
        { "name が空白だけ", WithFirst(Hankyu with { Name = "  " }), true },
        { "directionLabel が空", WithFirst(Hankyu with { DirectionLabel = "" }), true },
        { "walkMinutes が -1", WithFirst(Hankyu with { WalkMinutes = -1 }), true },
        { "walkMinutes が 121", WithFirst(Hankyu with { WalkMinutes = 121 }), true },
        { "hankyu 型で line がない", WithFirst(Hankyu with { Source = Hankyu.Source with { Line = null } }), true },
        { "hankyu 型で direction が空", WithFirst(Hankyu with { Source = Hankyu.Source with { Direction = " " } }), true },
        { "hankyu 型で URL がない", WithFirst(Hankyu with { Source = Hankyu.Source with { StationPageUrl = null } }), true },
        { "stationPageUrl が阪急以外", WithFirst(Hankyu with { Source = Hankyu.Source with { StationPageUrl = "https://example.com/" } }), true },
        { "stationPageUrl が http", WithFirst(Hankyu with { Source = Hankyu.Source with { StationPageUrl = "http://www.hankyu.co.jp/station/kakuu.html" } }), true },
        { "file 型に hankyu 用の項目", Good with { Stations = [Hankyu, File with { Source = File.Source with { Line = "架空線" } }] }, false },
        { "refreshIntervalDays が 0", Good with { Settings = Good.Settings with { RefreshIntervalDays = 0 } }, true },
        { "refreshIntervalDays が 31", Good with { Settings = Good.Settings with { RefreshIntervalDays = 31 } }, true },
        { "departuresListLimit が 4", Good with { Settings = Good.Settings with { DeparturesListLimit = 4 } }, true },
        { "departuresListLimit が 101", Good with { Settings = Good.Settings with { DeparturesListLimit = 101 } }, true },
        { "上書きの日付の重複", Good with { DayTypeOverrides = [.. Good.DayTypeOverrides, Good.DayTypeOverrides[0] with { DayType = DayType.Weekday }] }, true },
    };

    [Fact]
    public void Validate_accepts_valid_document()
    {
        Assert.Empty(StationsFile.Validate(Good));
        Assert.Empty(StationsFile.Validate(StationsDocument.Empty));
    }

    [Theory]
    [MemberData(nameof(InvalidDocuments))]
    public void Validate_matches_parse_rules(string label, StationsDocument doc, bool serializable)
    {
        _ = label;

        var errors = StationsFile.Validate(doc);

        // フォームの検証（Validate）は YAML のノードがないので、行番号は 0。
        Assert.NotEmpty(errors);
        Assert.All(errors, e => Assert.Equal(0, e.Line));

        // 同じ内容を YAML にして読むと、Parse でもエラーになる（ルールが一致している）。
        // file 型の駅の hankyu 用の項目は Serialize が書き出さないので、このケースは YAML にできない。
        if (serializable)
            Assert.False(StationsFile.Parse(StationsFile.Serialize(doc)).Ok);
    }
}
