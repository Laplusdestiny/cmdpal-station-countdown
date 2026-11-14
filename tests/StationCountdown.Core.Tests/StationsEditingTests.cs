using StationCountdown.Core.Config;
using StationCountdown.Core.Model;

namespace StationCountdown.Core.Tests;

public class StationsEditingTests
{
    private static readonly StationsDocument Good = StationsFile.Parse(StationsFileTests.Valid).Value!;

    private static StationConfig NewStation(string id) => new(id, "新駅", "東方面", 3, new StationSource(SourceType.File));

    [Fact]
    public void WithStationAdded_appends_and_keeps_others()
    {
        var doc = Good.WithStationAdded(NewStation("kakuu-higashi"));

        Assert.Equal(["kakuu-kita", "kakuu-minami", "kakuu-higashi"], doc.Stations.Select(s => s.Id));
        Assert.Equal(Good.Settings, doc.Settings);
        Assert.Equal(Good.DayTypeOverrides, doc.DayTypeOverrides);
        Assert.Equal(2, Good.Stations.Count);   // 元の文書は変わらない
    }

    [Fact]
    public void WithStationReplaced_keeps_position()
    {
        var edited = Good.Stations[0] with { Name = "架空北（改）", WalkMinutes = 12 };

        var doc = Good.WithStationReplaced(edited);

        Assert.Equal(edited, doc.Stations[0]);
        Assert.Equal(Good.Stations[1], doc.Stations[1]);
    }

    [Fact]
    public void WithStationReplaced_unknown_id_throws()
        => Assert.Throws<ArgumentException>(() => Good.WithStationReplaced(NewStation("nai")));

    [Fact]
    public void WithStationRemoved_removes_only_that_station()
    {
        var doc = Good.WithStationRemoved("kakuu-kita");

        Assert.Equal(["kakuu-minami"], doc.Stations.Select(s => s.Id));
    }

    [Fact]
    public void WithStationRemoved_unknown_id_is_noop()
        => Assert.Equal(Good.Stations, Good.WithStationRemoved("nai").Stations);

    [Theory]
    [InlineData(new string[0], "hankyu-1")]
    [InlineData(new[] { "hankyu-1", "hankyu-2" }, "hankyu-3")]
    [InlineData(new[] { "hankyu-2", "kakuu-kita" }, "hankyu-1")]
    [InlineData(new[] { "hankyu-1", "hankyu-3" }, "hankyu-2")]
    public void NextId_returns_first_free_number(string[] existing, string expected)
        => Assert.Equal(expected, StationIds.Next("hankyu", existing));

    [Fact]
    public void NextId_result_is_valid_id()
        => Assert.Null(StationRules.CheckId(StationIds.Next("hankyu", [])));
}
