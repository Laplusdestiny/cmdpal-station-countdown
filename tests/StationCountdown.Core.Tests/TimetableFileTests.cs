using StationCountdown.Core.Model;
using StationCountdown.Core.Timetables;

namespace StationCountdown.Core.Tests;

public class TimetableFileTests
{
    private const string Id = "kakuu-kita";

    // spec §4.3 の例を、駅名・URL を架空のものに替えたもの。
    private const string Valid = """
        # yaml-language-server: $schema=../schemas/timetable.schema.json
        schemaVersion: 1
        stationId: kakuu-kita
        fetchedAt: 2026-11-09T12:00:00+09:00
        sourceUrls:
          - https://example.invalid/kk_2_w.html
          - https://example.invalid/kk_2_h.html
        services:
          weekday:
            - 05:00 普通 架空台
            - 05:20 普通 架空台
            - 24:10 普通 架空町
          saturdayHoliday:
            - 05:00 普通 架空台
        """;

    [Fact]
    public void Parse_valid_file()
    {
        var r = TimetableFile.Parse(Valid, Id);

        Assert.True(r.Ok, string.Join("\n", r.Errors));
        var t = r.Value!;
        Assert.Equal(Id, t.StationId);
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.FromHours(9)), t.FetchedAt);
        Assert.Equal(["https://example.invalid/kk_2_w.html", "https://example.invalid/kk_2_h.html"], t.SourceUrls);
        Assert.Equal(3, t.Services[DayType.Weekday].Count);
        Assert.Equal(1450, t.Services[DayType.Weekday][^1].Minutes);
        Assert.Single(t.Services[DayType.SaturdayHoliday]);
        Assert.False(t.Services.ContainsKey(DayType.Saturday));
    }

    [Fact]
    public void Parse_sorts_unsorted_lines()
    {
        var yaml = """
            schemaVersion: 1
            stationId: kakuu-kita
            services:
              weekday:
                - 24:10 普通 架空町
                - 05:20 普通 架空台
                - 05:00 普通 架空台
            """;

        var r = TimetableFile.Parse(yaml, Id);

        Assert.True(r.Ok, string.Join("\n", r.Errors));
        Assert.Equal([300, 320, 1450], r.Value!.Services[DayType.Weekday].Select(d => d.Minutes));
    }

    [Fact]
    public void Parse_reports_line_number_for_bad_departure()
    {
        var yaml = "schemaVersion: 1\n"
                 + "stationId: kakuu-kita\n"
                 + "services:\n"
                 + "  weekday:\n"
                 + "    - 05:00　普通　架空台\n"
                 + "    - 05:20 普通 架空台\n";

        var r = TimetableFile.Parse(yaml, Id);

        Assert.False(r.Ok);
        Assert.Null(r.Value);
        Assert.Contains(r.Errors, e => e.Line == 5);
    }

    [Fact]
    public void Parse_rejects_stationId_mismatch()
    {
        var r = TimetableFile.Parse(Valid, "other-station");

        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.Line == 3);
    }

    [Fact]
    public void Parse_rejects_unknown_service_key()
    {
        var yaml = """
            schemaVersion: 1
            stationId: kakuu-kita
            services:
              sunday:
                - 05:00 普通 架空台
            """;

        var r = TimetableFile.Parse(yaml, Id);

        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.Line == 4);
    }

    [Theory]
    [InlineData("services: {}\n")]
    [InlineData("")]
    public void Parse_rejects_no_services(string servicesPart)
    {
        var yaml = "schemaVersion: 1\nstationId: kakuu-kita\n" + servicesPart;

        var r = TimetableFile.Parse(yaml, Id);

        Assert.False(r.Ok);
        Assert.NotEmpty(r.Errors);
    }

    [Fact]
    public void Parse_rejects_schemaVersion_2()
    {
        var r = TimetableFile.Parse(Valid.Replace("schemaVersion: 1", "schemaVersion: 2"), Id);

        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.Line == 2);
    }

    [Fact]
    public void Parse_allows_missing_fetchedAt_and_sourceUrls()
    {
        var yaml = """
            schemaVersion: 1
            stationId: kakuu-kita
            services:
              weekday:
                - 05:00 普通 架空台
            """;

        var r = TimetableFile.Parse(yaml, Id);

        Assert.True(r.Ok, string.Join("\n", r.Errors));
        Assert.Null(r.Value!.FetchedAt);
        Assert.Empty(r.Value.SourceUrls);
    }

    [Fact]
    public void Parse_rejects_invalid_fetchedAt()
    {
        var r = TimetableFile.Parse(Valid.Replace("2026-11-09T12:00:00+09:00", "yesterday"), Id);

        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.Line == 4);
    }

    [Fact]
    public void Parse_handles_bom_and_crlf()   // Review Focus 4
    {
        var plain = TimetableFile.Parse(Valid, Id);
        var bomCrlf = TimetableFile.Parse("﻿" + Valid.ReplaceLineEndings("\r\n"), Id);

        Assert.True(bomCrlf.Ok, string.Join("\n", bomCrlf.Errors));
        AssertSameTimetable(plain.Value!, bomCrlf.Value!);
    }

    [Fact]
    public void Parse_handles_bom_and_crlf_line_numbers()
    {
        var yaml = "﻿schemaVersion: 1\r\nstationId: kakuu-kita\r\nservices:\r\n  weekday:\r\n    - 05:00\t普通 架空台\r\n";

        var r = TimetableFile.Parse(yaml, Id);

        Assert.Contains(r.Errors, e => e.Line == 5);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n")]
    [InlineData("# only a comment\n")]
    [InlineData("- just\n- a list\n")]
    [InlineData("services: [unterminated\n")]
    [InlineData("schemaVersion: 1\nschemaVersion: 1\n")]
    public void Parse_malformed_is_error_not_exception(string yaml)
    {
        var r = TimetableFile.Parse(yaml, Id);

        Assert.False(r.Ok);
        Assert.Null(r.Value);
        Assert.NotEmpty(r.Errors);
    }

    [Fact]
    public void Serialize_then_Parse_roundtrips()
    {
        var original = TimetableFile.Parse(Valid, Id).Value!;

        var yaml = TimetableFile.Serialize(original);
        var again = TimetableFile.Parse(yaml, Id);

        Assert.True(again.Ok, yaml + "\n" + string.Join("\n", again.Errors));
        AssertSameTimetable(original, again.Value!);
    }

    [Fact]
    public void Serialize_writes_lines_in_spec_format()
    {
        var yaml = TimetableFile.Serialize(TimetableFile.Parse(Valid, Id).Value!);

        Assert.StartsWith("# yaml-language-server: $schema=../schemas/timetable.schema.json\n", yaml);
        Assert.Contains("  weekday:\n    - 05:00 普通 架空台\n", yaml);
        Assert.Contains("- 24:10 普通 架空町", yaml);
        Assert.DoesNotContain("\r", yaml);
        Assert.DoesNotContain("...", yaml);
        Assert.Contains("fetchedAt: 2026-11-09T12:00:00+09:00", yaml);
        Assert.Contains("saturdayHoliday:", yaml);
    }

    [Fact]
    public void Serialize_omits_missing_optional_fields()
    {
        var t = new Timetable(Id, null, [],
            new Dictionary<DayType, IReadOnlyList<Departure>> { [DayType.Weekday] = [new(300, "普通", "架空台")] });

        var yaml = TimetableFile.Serialize(t);

        Assert.DoesNotContain("fetchedAt", yaml);
        Assert.DoesNotContain("sourceUrls", yaml);
        Assert.True(TimetableFile.Parse(yaml, Id).Ok);
    }

    private static void AssertSameTimetable(Timetable expected, Timetable actual)
    {
        Assert.Equal(expected.StationId, actual.StationId);
        Assert.Equal(expected.FetchedAt, actual.FetchedAt);
        Assert.Equal(expected.SourceUrls, actual.SourceUrls);
        Assert.Equal(expected.Services.Keys.Order(), actual.Services.Keys.Order());
        foreach (var (key, list) in expected.Services)
            Assert.Equal(list, actual.Services[key]);
    }
}
