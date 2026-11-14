using StationCountdown.Core.Model;

namespace StationCountdown.Core.Tests;

public class DepartureTests
{
    [Fact]
    public void ParseLine_reads_three_fields()
    {
        var d = Departure.ParseLine("05:20 普通 架空台");
        Assert.Equal(5 * 60 + 20, d.Minutes);
        Assert.Equal("普通", d.TrainType);
        Assert.Equal("架空台", d.Destination);
    }

    [Theory]
    [InlineData("24:10 普通 架空台", 1450)]
    [InlineData("29:59 普通 架空台", 1799)]
    [InlineData("00:00 普通 架空台", 0)]
    public void ParseLine_accepts_hours_up_to_29(string line, int minutes)
        => Assert.Equal(minutes, Departure.ParseLine(line).Minutes);

    [Theory]
    [InlineData("30:00 普通 架空台")]
    [InlineData("05:60 普通 架空台")]
    [InlineData("5:00 普通 架空台")]
    [InlineData("05:00 普通")]
    [InlineData("05:00 普通 架空 台")]
    [InlineData("05:00  普通 架空台")]
    [InlineData("05:00 普通 架空台 ")]
    [InlineData("05:00　普通　架空台")]   // Review Focus 1: 全角スペース
    [InlineData("05:00\t普通\t架空台")]   // Review Focus 1: タブ
    [InlineData("05:00 普通 架空台\n")]
    [InlineData("")]
    public void ParseLine_rejects_invalid(string line)
        => Assert.Throws<FormatException>(() => Departure.ParseLine(line));

    [Theory]
    [InlineData(1450, "0:10")]
    [InlineData(300, "5:00")]
    [InlineData(754, "12:34")]
    public void DisplayTime_wraps_after_24(int m, string s)
        => Assert.Equal(s, new Departure(m, "普通", "x").DisplayTime);

    [Theory]
    [InlineData(300, "05:00 普通 架空台")]
    [InlineData(1450, "24:10 普通 架空台")]
    public void ToLine_zero_pads_hours(int m, string expected)
        => Assert.Equal(expected, new Departure(m, "普通", "架空台").ToLine());
}
