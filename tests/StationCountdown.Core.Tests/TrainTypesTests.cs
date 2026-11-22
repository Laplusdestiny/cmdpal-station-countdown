using StationCountdown.Core.Model;
using StationCountdown.Core.Timetables;

namespace StationCountdown.Core.Tests;

public class TrainTypesTests
{
    // weekday:          05:00 普通 A / 05:10 急行 B
    // saturdayHoliday:  05:00 準急 A / 06:00 普通 A
    private static readonly Timetable Table = new("kakuu-kita", null, [], new Dictionary<DayType, IReadOnlyList<Departure>>
    {
        [DayType.SaturdayHoliday] = Lines("05:00 準急 A", "06:00 普通 A"),
        [DayType.Weekday] = Lines("05:00 普通 A", "05:10 急行 B"),
    });

    private static Departure[] Lines(params string[] lines) => lines.Select(Departure.ParseLine).ToArray();

    [Fact]
    public void Choices_in_order_of_first_appearance()
        => Assert.Equal(
            [new("普通", true), new("急行", true), new TrainTypes.TrainTypeChoice("準急", true)],
            TrainTypes.Choices(Table, []));

    [Fact]
    public void Choices_keep_selected_missing_types()
        => Assert.Equal(
            [new("普通", true), new("急行", true), new("準急", true), new TrainTypes.TrainTypeChoice("特急", false)],
            TrainTypes.Choices(Table, ["特急", "普通"]));

    [Fact]
    public void Choices_skip_types_with_comma()
    {
        var table = Table with { Services = new Dictionary<DayType, IReadOnlyList<Departure>> { [DayType.Weekday] = Lines("05:00 普通,臨時 A", "05:10 普通 A") } };

        Assert.Equal([new TrainTypes.TrainTypeChoice("普通", true)], TrainTypes.Choices(table, []));
    }

    [Theory]
    [InlineData("", new string[0])]
    [InlineData("普通", new[] { "普通" })]
    [InlineData("普通,準急", new[] { "普通", "準急" })]
    [InlineData("普通,,準急", new[] { "普通", "準急" })]
    public void ParseSelection_splits_by_comma(string value, string[] expected)
        => Assert.Equal(expected, TrainTypes.ParseSelection(value));

    [Fact]
    public void ApplySelection_null_keeps_current()
        => Assert.Equal(["普通"], TrainTypes.ApplySelection(["普通"], null));

    [Fact]
    public void ApplySelection_empty_clears()
        => Assert.Empty(TrainTypes.ApplySelection(["普通"], ""));

    [Fact]
    public void SavedMessage_warns_when_filter_is_cleared()
        => Assert.Equal("保存しました。表示する種別の絞り込みを外しました（すべての種別を表示します）。", TrainTypes.SavedMessage(["普通"], []));

    [Theory]
    [InlineData(new string[0], new string[0])]
    [InlineData(new[] { "普通" }, new[] { "普通" })]
    [InlineData(new string[0], new[] { "普通" })]
    [InlineData(new[] { "普通" }, new[] { "準急" })]
    public void SavedMessage_plain_otherwise(string[] before, string[] after)
        => Assert.Equal("保存しました。", TrainTypes.SavedMessage(before, after));
}
