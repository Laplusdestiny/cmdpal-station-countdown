using System.Text;
using StationCountdown.Core.Model;
using StationCountdown.Core.Timetables;

namespace StationCountdown.Core.Tests;

public sealed class TimetableWriterTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("station-countdown-tests-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static Timetable Make(string firstLine) => new("kakuu-kita", new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.FromHours(9)),
        ["https://example.invalid/a.html"],
        new Dictionary<DayType, IReadOnlyList<Departure>> { [DayType.Weekday] = [Departure.ParseLine(firstLine)] });

    [Fact]
    public void TimetableWriter_WriteAtomic_replaces_file_and_leaves_no_tmp()
    {
        var path = Path.Combine(_dir, "kakuu-kita.yaml");
        File.WriteAllText(path, "old content");

        TimetableWriter.WriteAtomic(path, Make("05:00 普通 架空台"));

        var read = TimetableFile.Parse(File.ReadAllText(path), "kakuu-kita");
        Assert.True(read.Ok, string.Join("\n", read.Errors));
        Assert.Equal("05:00 普通 架空台", read.Value!.Services[DayType.Weekday][0].ToLine());
        Assert.Equal(["kakuu-kita.yaml"], Directory.GetFiles(_dir).Select(Path.GetFileName));
    }

    [Fact]
    public void WriteAtomic_creates_new_file()
    {
        var path = Path.Combine(_dir, "new.yaml");

        TimetableWriter.WriteAtomic(path, Make("06:00 普通 架空台"));

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void WriteAtomic_writes_utf8_without_bom()
    {
        var path = Path.Combine(_dir, "kakuu-kita.yaml");

        TimetableWriter.WriteAtomic(path, Make("05:00 普通 架空台"));

        var bytes = File.ReadAllBytes(path);
        Assert.False(bytes is [0xEF, 0xBB, 0xBF, ..]);
        Assert.Contains("普通 架空台", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void WriteAtomic_overwrites_leftover_tmp()
    {
        var path = Path.Combine(_dir, "kakuu-kita.yaml");
        File.WriteAllText(path + ".tmp", "left over from a crash");

        TimetableWriter.WriteAtomic(path, Make("05:00 普通 架空台"));

        Assert.True(TimetableFile.Parse(File.ReadAllText(path), "kakuu-kita").Ok);
        Assert.False(File.Exists(path + ".tmp"));
    }
}
