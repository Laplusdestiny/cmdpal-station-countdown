using StationCountdown.Core.Countdown;
using StationCountdown.Core.Model;

namespace StationCountdown.Core.Tests;

public class CountdownFormatterTests
{
    private static readonly StationConfig Station =
        new("hankyu-umeda-takarazuka", "大阪梅田", "宝塚方面", 8, new StationSource(SourceType.File));

    private static Departure D(string line) => Departure.ParseLine(line);

    private static readonly BandState.Upcoming Normal = new(D("12:34 急行 宝塚"), 3, D("12:40 普通 宝塚"));

    // ---- FormatBand（spec §6.1 の表。半角・全角も含めて完全一致） ----

    [Fact]
    public void Band_upcoming()
        => Assert.Equal(new BandText("急行 宝塚 あと3分", "12:34発 ／ 次 12:40 普通"),
            CountdownFormatter.FormatBand(Normal, Station, fetchFailed: false));

    [Fact]
    public void Band_upcoming_now_without_following()
        => Assert.Equal(new BandText("急行 宝塚 今すぐ", "12:34発"),
            CountdownFormatter.FormatBand(new BandState.Upcoming(D("12:34 急行 宝塚"), 0, null), Station, fetchFailed: false));

    [Fact]
    public void Band_upcoming_after_midnight_wraps_hour()
        => Assert.Equal(new BandText("普通 雲雀丘 あと12分", "0:10発"),
            CountdownFormatter.FormatBand(new BandState.Upcoming(D("24:10 普通 雲雀丘"), 12, null), Station, fetchFailed: false));

    [Fact]
    public void Band_upcoming_long_wait()
        => Assert.Equal("急行 宝塚 あと125分",
            CountdownFormatter.FormatBand(Normal with { RemainMinutes = 125 }, Station, fetchFailed: false).Title);

    [Fact]
    public void Band_end_of_service()
        => Assert.Equal(new BandText("本日終了", "始発 5:00 普通 宝塚"),
            CountdownFormatter.FormatBand(new BandState.EndOfService(D("05:00 普通 宝塚")), Station, fetchFailed: false));

    [Fact]
    public void Band_no_timetable()
        => Assert.Equal(new BandText("時刻表未取得", "大阪梅田 宝塚方面"),
            CountdownFormatter.FormatBand(new BandState.NoTimetable(), Station, fetchFailed: false));

    [Fact]
    public void Band_fetch_failed_appends_marker()
        => Assert.Equal(new BandText("急行 宝塚 あと3分", "12:34発 ／ 次 12:40 普通 ／ 取得失敗"),
            CountdownFormatter.FormatBand(Normal, Station, fetchFailed: true));

    [Fact]
    public void Band_fetch_failed_appends_marker_in_other_states()
    {
        Assert.Equal("始発 5:00 普通 宝塚 ／ 取得失敗",
            CountdownFormatter.FormatBand(new BandState.EndOfService(D("05:00 普通 宝塚")), Station, fetchFailed: true).Subtitle);
        Assert.Equal("大阪梅田 宝塚方面 ／ 取得失敗",
            CountdownFormatter.FormatBand(new BandState.NoTimetable(), Station, fetchFailed: true).Subtitle);
    }

    // ---- FormatListEntry ----

    [Fact]
    public void List_uncatchable()
        => Assert.Equal(new BandText("12:30 急行 B", "間に合わない"),
            CountdownFormatter.FormatListEntry(new ListEntry(D("12:30 急行 B"), null, false)));

    [Fact]
    public void List_remaining()
        => Assert.Equal(new BandText("12:40 普通 A", "あと7分"),
            CountdownFormatter.FormatListEntry(new ListEntry(D("12:40 普通 A"), 7, false)));

    [Fact]
    public void List_now()
        => Assert.Equal(new BandText("12:40 普通 A", "今すぐ"),
            CountdownFormatter.FormatListEntry(new ListEntry(D("12:40 普通 A"), 0, false)));

    [Fact]
    public void List_wraps_hour_after_midnight()
        => Assert.Equal("0:10 普通 A",
            CountdownFormatter.FormatListEntry(new ListEntry(D("24:10 普通 A"), 30, false)).Title);

    [Fact]
    public void Next_day_separator_title()
        => Assert.Equal("翌日", CountdownFormatter.NextDaySeparatorTitle);
}
