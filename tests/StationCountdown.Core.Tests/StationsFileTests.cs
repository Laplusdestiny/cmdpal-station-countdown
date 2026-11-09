using StationCountdown.Core.Config;
using StationCountdown.Core.Model;

namespace StationCountdown.Core.Tests;

public class StationsFileTests
{
    // spec §4.2 の例と同じ構造で、駅名・URL を架空のものに替えたもの。行番号はテストで使う。
    internal const string Valid =
        "# yaml-language-server: $schema=./schemas/stations.schema.json\n" // 1
      + "schemaVersion: 1\n"                                              // 2
      + "settings:\n"                                                     // 3
      + "  refreshIntervalDays: 7\n"                                      // 4
      + "  departuresListLimit: 30\n"                                     // 5
      + "stations:\n"                                                     // 6
      + "  - id: kakuu-kita\n"                                            // 7
      + "    name: 架空北\n"                                               // 8
      + "    directionLabel: 北方面\n"                                     // 9
      + "    walkMinutes: 8\n"                                            // 10
      + "    source:\n"                                                   // 11
      + "      type: hankyu\n"                                            // 12
      + "      stationPageUrl: https://www.hankyu.co.jp/station/kakuu.html\n" // 13
      + "      line: 架空線\n"                                             // 14
      + "      direction: 北方面\n"                                        // 15
      + "  - id: kakuu-minami\n"                                          // 16
      + "    name: 架空南\n"                                               // 17
      + "    directionLabel: 南方面\n"                                     // 18
      + "    walkMinutes: 10\n"                                           // 19
      + "    source:\n"                                                   // 20
      + "      type: file\n"                                              // 21
      + "dayTypeOverrides:\n"                                             // 22
      + "  - { date: 2026-12-31, dayType: saturdayHoliday }\n";           // 23

    [Fact]
    public void Parse_spec_example()
    {
        var r = StationsFile.Parse(Valid);

        Assert.True(r.Ok, string.Join("\n", r.Errors));
        var doc = r.Value!;
        Assert.Equal(2, doc.Stations.Count);

        var hankyu = doc.Stations[0];
        Assert.Equal("kakuu-kita", hankyu.Id);
        Assert.Equal("架空北", hankyu.Name);
        Assert.Equal("北方面", hankyu.DirectionLabel);
        Assert.Equal(8, hankyu.WalkMinutes);
        Assert.Equal(new StationSource(SourceType.Hankyu, "https://www.hankyu.co.jp/station/kakuu.html", "架空線", "北方面"), hankyu.Source);
        Assert.Equal("com.laplusdestiny.stationcountdown.band.kakuu-kita", hankyu.BandCommandId);

        Assert.Equal(new StationSource(SourceType.File), doc.Stations[1].Source);
        Assert.Equal(7, doc.Settings.RefreshIntervalDays);
        Assert.Equal(30, doc.Settings.DeparturesListLimit);
        Assert.Equal([new DayTypeOverride(new DateOnly(2026, 12, 31), DayType.SaturdayHoliday)], doc.DayTypeOverrides);
    }

    [Fact]
    public void Parse_defaults_when_settings_missing()
    {
        var yaml = """
            schemaVersion: 1
            stations:
              - id: kakuu-minami
                name: 架空南
                directionLabel: 南方面
                walkMinutes: 0
                source:
                  type: file
            """;

        var r = StationsFile.Parse(yaml);

        Assert.True(r.Ok, string.Join("\n", r.Errors));
        Assert.Equal(new GlobalSettings(), r.Value!.Settings);
        Assert.Equal(7, r.Value.Settings.RefreshIntervalDays);
        Assert.Equal(30, r.Value.Settings.DeparturesListLimit);
        Assert.Empty(r.Value.DayTypeOverrides);
    }

    [Fact]
    public void Parse_partial_settings_uses_defaults_for_rest()
    {
        var r = StationsFile.Parse("schemaVersion: 1\nsettings:\n  departuresListLimit: 50\nstations: []\n");

        Assert.True(r.Ok, string.Join("\n", r.Errors));
        Assert.Equal(new GlobalSettings(7, 50), r.Value!.Settings);
        Assert.Empty(r.Value.Stations);
    }

    [Theory]
    [InlineData("id: kakuu-kita", "id: Bad_ID", 7)]
    [InlineData("id: kakuu-kita", "id: kakuu--kita", 7)]
    [InlineData("id: kakuu-minami", "id: kakuu-kita", 16)]                      // 重複
    [InlineData("id: kakuu-kita", "id: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 7)] // 65 文字
    [InlineData("name: 架空北", "name: \"\"", 8)]
    [InlineData("name: 架空北", "name:", 8)]
    [InlineData("directionLabel: 北方面", "directionLabel: \" \"", 9)]
    [InlineData("walkMinutes: 8", "walkMinutes: -1", 10)]
    [InlineData("walkMinutes: 8", "walkMinutes: 121", 10)]
    [InlineData("walkMinutes: 8", "walkMinutes: 8.5", 10)]
    [InlineData("type: hankyu", "type: odpt", 12)]
    [InlineData("      line: 架空線\n", "", 12)]                                 // hankyu 型で line がない
    [InlineData("https://www.hankyu.co.jp/station/kakuu.html", "https://example.com/", 13)]
    [InlineData("https://www.hankyu.co.jp/station/kakuu.html", "http://www.hankyu.co.jp/station/kakuu.html", 13)]
    [InlineData("refreshIntervalDays: 7", "refreshIntervalDays: 0", 4)]
    [InlineData("refreshIntervalDays: 7", "refreshIntervalDays: 31", 4)]
    [InlineData("departuresListLimit: 30", "departuresListLimit: 4", 5)]
    [InlineData("departuresListLimit: 30", "departuresListLimit: 101", 5)]
    [InlineData("dayType: saturdayHoliday", "dayType: sunday", 23)]
    [InlineData("date: 2026-12-31", "date: 2026-13-01", 23)]
    [InlineData("schemaVersion: 1", "schemaVersion: 2", 2)]
    [InlineData("    walkMinutes: 10\n", "    walkMinutes: 10\n    walkMinute: 10\n", 20)] // 不明なキー
    [InlineData("      type: file\n", "      type: file\n      line: 架空線\n", 22)]       // file 型に hankyu 用のキー
    public void Parse_rejects_invalid_with_line_number(string find, string replace, int line)
    {
        Assert.Contains(find, Valid);
        var yaml = Valid.Replace(find, replace);

        var r = StationsFile.Parse(yaml);

        Assert.False(r.Ok);
        Assert.Null(r.Value);
        Assert.Contains(r.Errors, e => e.Line == line);
    }

    [Fact]
    public void Parse_rejects_duplicate_override_dates()
    {
        var yaml = Valid + "  - { date: 2026-12-31, dayType: weekday }\n";

        var r = StationsFile.Parse(yaml);

        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.Line == 24);
    }

    [Fact]
    public void Parse_collects_multiple_errors()
    {
        var yaml = Valid.Replace("walkMinutes: 8", "walkMinutes: 999").Replace("type: file", "type: odpt");

        var r = StationsFile.Parse(yaml);

        Assert.Contains(r.Errors, e => e.Line == 10);
        Assert.Contains(r.Errors, e => e.Line == 21);
    }

    [Fact]
    public void Parse_errors_are_ordered_by_line()
    {
        // 必須キーがない（マッピングの開始行で報告）エラーと、値の誤りが混ざる場合。
        var yaml = "schemaVersion: 1\nstations:\n  - id: Bad_ID\n    name:\n    walkMinutes: 8.5\n    source:\n      type: file\n";

        var lines = StationsFile.Parse(yaml).Errors.Select(e => e.Line).ToArray();

        Assert.Equal([3, 3, 4, 5], lines);
    }

    [Theory]
    [InlineData("")]
    [InlineData("stations:\n  - id: kakuu-kita\n    name: [unterminated\n")]
    [InlineData("- a\n")]
    public void Parse_malformed_is_error_not_exception(string yaml)
    {
        var r = StationsFile.Parse(yaml);

        Assert.False(r.Ok);
        Assert.NotEmpty(r.Errors);
    }

    [Fact]
    public void Parse_handles_bom_and_crlf()   // Review Focus 4
    {
        var plain = StationsFile.Parse(Valid);
        var bomCrlf = StationsFile.Parse("﻿" + Valid.ReplaceLineEndings("\r\n"));

        Assert.True(bomCrlf.Ok, string.Join("\n", bomCrlf.Errors));
        AssertSameDocument(plain.Value!, bomCrlf.Value!);

        var bad = StationsFile.Parse("﻿" + Valid.Replace("walkMinutes: 8", "walkMinutes: -1").ReplaceLineEndings("\r\n"));
        Assert.Contains(bad.Errors, e => e.Line == 10);
    }

    [Fact]
    public void Serialize_then_Parse_roundtrips()
    {
        var original = StationsFile.Parse(Valid).Value!;

        var yaml = StationsFile.Serialize(original);
        var again = StationsFile.Parse(yaml);

        Assert.True(again.Ok, yaml + "\n" + string.Join("\n", again.Errors));
        AssertSameDocument(original, again.Value!);
    }

    [Theory]
    [InlineData("梅田: 宝塚 # 方面")]
    [InlineData("'quoted'")]
    [InlineData("123")]
    [InlineData("true")]
    [InlineData("- dash")]
    [InlineData("  前後に空白  ")]
    public void Serialize_roundtrips_tricky_names(string name)
    {
        var doc = new StationsDocument(new GlobalSettings(),
            [new StationConfig("kakuu-kita", name, "北方面", 5, new StationSource(SourceType.File))], []);

        var again = StationsFile.Parse(StationsFile.Serialize(doc));

        Assert.True(again.Ok, string.Join("\n", again.Errors));
        Assert.Equal(name, again.Value!.Stations[0].Name);
    }

    [Fact]
    public void Serialize_starts_with_schema_comment()
    {
        var yaml = StationsFile.Serialize(StationsFile.Parse(Valid).Value!);

        Assert.StartsWith("# yaml-language-server: $schema=./schemas/stations.schema.json\n", yaml);
    }

    [Fact]
    public void Serialize_empty_document_parses_back()
    {
        var again = StationsFile.Parse(StationsFile.Serialize(StationsDocument.Empty));

        Assert.True(again.Ok, string.Join("\n", again.Errors));
        AssertSameDocument(StationsDocument.Empty, again.Value!);
    }

    private static void AssertSameDocument(StationsDocument expected, StationsDocument actual)
    {
        Assert.Equal(expected.Settings, actual.Settings);
        Assert.Equal(expected.Stations, actual.Stations);
        Assert.Equal(expected.DayTypeOverrides, actual.DayTypeOverrides);
    }
}
