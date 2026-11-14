using StationCountdown.Importers.Hankyu;

namespace StationCountdown.Importers.Tests;

public class HankyuTimetableParserTests
{
    private static readonly string Weekday = HankyuStationPageParserTests.Fixture("hankyu_timetable_weekday.html");

    // fixture の時の欄（5 時・23 時・0 時）を書き換えるための、元の文字列。
    private static string HourCell(int hour)
        => $"""<li class="timetable_hour hour_w"><span data-wovn-ignore>{hour}</span><span class="about_alt">時</span></li>""";

    [Fact]
    public void Parse_reads_departures()
    {
        var list = HankyuTimetableParser.Parse(Weekday);

        Assert.Equal(["05:00 普通 架空台", "05:30 急行 架空台", "23:50 普通 架空台", "24:10 普通 架空町"], list.Select(d => d.ToLine()));
    }

    [Fact]
    public void Parse_converts_midnight_hour_to_24()
        => Assert.Equal("24:10 普通 架空町", HankyuTimetableParser.Parse(Weekday)[^1].ToLine());

    [Fact]
    public void Parse_strips_brackets_from_train_type()
    {
        var list = HankyuTimetableParser.Parse(Weekday);

        Assert.All(list, d => Assert.DoesNotContain("［", d.TrainType));
        Assert.Equal("急行", list[1].TrainType);
    }

    [Fact]
    public void Parse_strips_half_width_brackets_and_inner_spaces()
    {
        var html = Weekday.Replace("［急行］", "[ 通勤 急行 ]").Replace("<p class=\"timetable_station_name\">架空町</p>", "<p class=\"timetable_station_name\"> 架空　町\n</p>");

        var list = HankyuTimetableParser.Parse(html);

        Assert.Equal("05:30 通勤急行 架空台", list[1].ToLine());
        Assert.Equal("24:10 普通 架空町", list[^1].ToLine());
    }

    [Fact]
    public void Parse_ignores_hour_class_suffix()
        => Assert.Equal(
            HankyuTimetableParser.Parse(Weekday),
            HankyuTimetableParser.Parse(Weekday.Replace("hour_w", "hour_h")));

    [Fact]
    public void Parse_hours_after_midnight_keep_increasing()
    {
        // 0 時台のあとに 1 時台がある場合は 25 時になる。
        var html = Weekday.Replace(
            "</ul>\n</div>\n</div>",
            """
                <li><ul>
                  <li class="timetable_hour hour_w"><span data-wovn-ignore>1</span><span class="about_alt">時</span></li>
                  <li class="timetable_mins"><ul><li><a><p class="timetable_min"><span data-wovn-ignore>05</span><span class="about_alt">分</span></p>
                    <div class="timetable_att"><p class="timetable_train_type">［普通］</p><p class="timetable_station_name">架空町</p></div></a></li></ul></li>
                </ul></li>
              </ul>
            </div>
            </div>
            """);

        Assert.Equal("25:05 普通 架空町", HankyuTimetableParser.Parse(html)[^1].ToLine());
    }

    [Theory]
    [InlineData("<html><body></body></html>")]
    [InlineData("")]
    public void Parse_throws_on_zero_departures(string html)
        => Assert.Throws<ImportException>(() => HankyuTimetableParser.Parse(html));

    [Fact]
    public void Parse_throws_on_minute_out_of_range()
    {
        var html = Weekday.Replace("<span data-wovn-ignore>30</span>", "<span data-wovn-ignore>75</span>");

        Assert.Throws<ImportException>(() => HankyuTimetableParser.Parse(html));
    }

    [Fact]
    public void Parse_throws_on_non_increasing_hours()
    {
        // 5 時、7 時、6 時の順。6 時は 7 時より小さいので 24 を足して 30 時になり、範囲外になる。
        var html = Weekday.Replace(HourCell(23), HourCell(7)).Replace(HourCell(0), HourCell(6));

        Assert.Throws<ImportException>(() => HankyuTimetableParser.Parse(html));
    }

    [Fact]
    public void Parse_throws_on_decreasing_minutes_within_hour()
    {
        var html = Weekday.Replace("<span data-wovn-ignore>30</span>", "<span data-wovn-ignore>xx</span>")
                          .Replace("<span data-wovn-ignore>00</span>", "<span data-wovn-ignore>30</span>")
                          .Replace("<span data-wovn-ignore>xx</span>", "<span data-wovn-ignore>00</span>");

        Assert.Throws<ImportException>(() => HankyuTimetableParser.Parse(html));
    }

    [Fact]
    public void Parse_throws_on_unreadable_hour()
    {
        var html = Weekday.Replace(HourCell(5), """<li class="timetable_hour hour_w"><span>五</span></li>""");

        Assert.Throws<ImportException>(() => HankyuTimetableParser.Parse(html));
    }

    [Fact]
    public void Parse_throws_on_missing_destination()
    {
        var html = Weekday.Replace("<p class=\"timetable_station_name\">架空町</p>", "");

        Assert.Throws<ImportException>(() => HankyuTimetableParser.Parse(html));
    }

    [Fact]
    public void ImportException_wraps_format_error()
    {
        var html = Weekday.Replace("<span data-wovn-ignore>30</span>", "<span data-wovn-ignore>75</span>");

        var ex = Assert.Throws<ImportException>(() => HankyuTimetableParser.Parse(html));

        Assert.IsType<FormatException>(ex.InnerException);
    }
}
