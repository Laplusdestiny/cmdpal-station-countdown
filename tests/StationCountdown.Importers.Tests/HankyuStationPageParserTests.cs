using StationCountdown.Importers.Hankyu;

namespace StationCountdown.Importers.Tests;

public class HankyuStationPageParserTests
{
    private static readonly Uri PageUrl = new("https://www.hankyu.co.jp/station/kakuu.html");

    internal static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void Parse_extracts_current_directions()
    {
        var page = HankyuStationPageParser.Parse(Fixture("hankyu_station_page.html"), PageUrl);

        Assert.Equal(
        [
            new HankyuDirectionLink("架空線", "北方面",
                new Uri("https://www.hankyu.co.jp/station/html/HK-99_kk_2_w.html"),
                new Uri("https://www.hankyu.co.jp/station/html/HK-99_kk_2_h.html")),
            new HankyuDirectionLink("架空支線", "南方面",
                new Uri("https://www.hankyu.co.jp/station/html/HK-99_ks_1_w.html"),
                new Uri("https://www.hankyu.co.jp/station/html/HK-99_ks_1_h.html")),
        ], page.Current);
    }

    [Fact]
    public void Parse_excludes_dated_revision_and_flags_it()
    {
        var page = HankyuStationPageParser.Parse(Fixture("hankyu_station_page.html"), PageUrl);

        Assert.DoesNotContain(page.Current, d => d.WeekdayUrl.AbsolutePath.Contains("/20990101/") || d.HolidayUrl.AbsolutePath.Contains("/20990101/"));
        Assert.True(page.HasRevisedTimetable);
    }

    [Fact]
    public void Parse_extracts_station_name_from_h1()
        => Assert.Equal("架空駅", HankyuStationPageParser.Parse(Fixture("hankyu_station_page.html"), PageUrl).StationName);

    [Fact]
    public void Parse_falls_back_to_title_for_station_name()
    {
        var html = Block("架空線", Direction("北方面", "/station/html/a_w.html", "/station/html/a_h.html"))
            .Replace("<html>", "<html><head><title> 架空台 | 時刻表 構内図 おでかけ情報 | 阪急電鉄</title></head>");

        Assert.Equal("架空台", HankyuStationPageParser.Parse(html, PageUrl).StationName);
    }

    [Fact]
    public void Parse_station_name_is_null_when_missing()
    {
        var html = Block("架空線", Direction("北方面", "/station/html/a_w.html", "/station/html/a_h.html"));

        Assert.Null(HankyuStationPageParser.Parse(html, PageUrl).StationName);
    }

    [Fact]
    public void Parse_without_revision_is_not_flagged()
    {
        var html = Block("架空線", Direction("北方面", "/station/html/HK-99_kk_2_w.html", "/station/html/HK-99_kk_2_h.html"));

        var page = HankyuStationPageParser.Parse(html, PageUrl);

        Assert.Single(page.Current);
        Assert.False(page.HasRevisedTimetable);
    }

    [Fact]
    public void Parse_reads_multiple_directions_in_one_line()
    {
        var html = Block("架空線",
            Direction("北方面", "/station/html/HK-99_kk_2_w.html", "/station/html/HK-99_kk_2_h.html"),
            Direction("南方面", "/station/html/HK-99_kk_1_w.html", "/station/html/HK-99_kk_1_h.html"));

        var page = HankyuStationPageParser.Parse(html, PageUrl);

        Assert.Equal(["架空線 北方面", "架空線 南方面"], page.Current.Select(d => $"{d.Line} {d.Direction}"));
        Assert.EndsWith("_kk_1_h.html", page.Current[1].HolidayUrl.AbsoluteUri);
    }

    [Fact]
    public void Parse_resolves_relative_and_absolute_links()
    {
        var html = Block("架空線", Direction("北方面", "html/HK-99_kk_2_w.html", "https://www.hankyu.co.jp/station/html/HK-99_kk_2_h.html"));

        var d = Assert.Single(HankyuStationPageParser.Parse(html, PageUrl).Current);

        Assert.Equal("https://www.hankyu.co.jp/station/html/HK-99_kk_2_w.html", d.WeekdayUrl.AbsoluteUri);
        Assert.Equal("https://www.hankyu.co.jp/station/html/HK-99_kk_2_h.html", d.HolidayUrl.AbsoluteUri);
    }

    [Fact]
    public void Parse_trims_whitespace_in_names()
    {
        var html = Block("\n  架空線  \n", Direction(" 北方面\n", "/station/html/a_w.html", "/station/html/a_h.html"));

        var d = Assert.Single(HankyuStationPageParser.Parse(html, PageUrl).Current);

        Assert.Equal(("架空線", "北方面"), (d.Line, d.Direction));
    }

    [Fact]
    public void Parse_skips_direction_missing_a_link()
    {
        var html = Block("架空線",
            """<div class="timetable_cnt_in"><div class="timetable_cnt_dir"><h5>北方面</h5></div><ul class="timetable_cnt_link"><li><a href="/station/html/a_w.html">平日</a></li></ul></div>""",
            Direction("南方面", "/station/html/b_w.html", "/station/html/b_h.html"));

        var d = Assert.Single(HankyuStationPageParser.Parse(html, PageUrl).Current);

        Assert.Equal("南方面", d.Direction);
    }

    [Theory]
    [InlineData("<html></html>")]
    [InlineData("")]
    [InlineData("<div class=\"timetable_cnt\"><h4>架空線</h4></div>")]
    public void Parse_throws_when_no_timetable_blocks(string html)
        => Assert.Throws<ImportException>(() => HankyuStationPageParser.Parse(html, PageUrl));

    private static string Block(string line, params string[] directions)
        => $"""<html><body><div class="tab-pane" id="jikoku"><div class="timetable_cnt"><h4>{line}</h4>{string.Concat(directions)}</div></div></body></html>""";

    private static string Direction(string name, string weekdayHref, string holidayHref)
        => $"""
            <div class="timetable_cnt_in">
              <div class="timetable_cnt_dir"><h5>{name}</h5></div>
              <ul class="timetable_cnt_link">
                <li><a href="{weekdayHref}">平日</a></li>
                <li><a href="{holidayHref}">土曜・休日</a></li>
              </ul>
            </div>
            """;
}
