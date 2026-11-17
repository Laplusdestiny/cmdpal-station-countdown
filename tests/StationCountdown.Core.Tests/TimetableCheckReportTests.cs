using StationCountdown.Core.Timetables;

namespace StationCountdown.Core.Tests;

public class TimetableCheckReportTests
{
    private const string Id = "kakuu-kita";

    private static TimetableCheckReport Check(string services)
        => TimetableCheckReport.Create("schemaVersion: 1\nstationId: kakuu-kita\nservices:\n" + services, Id);

    [Fact]
    public void Clean_file_exits_0_and_shows_summary()
    {
        var report = Check("  weekday:\n    - 05:00 普通 架空台\n");

        Assert.Equal(0, report.ExitCode);
        Assert.Contains("エラー・警告はありません", report.Text);
        Assert.Contains("| 5 | 1 |", report.Text);
    }

    [Fact]
    public void Warnings_only_exit_2_with_line_numbers_and_summary()
    {
        var report = Check("  weekday:\n    - 05:00 普通 架空台\n    - 00:10 普通 架空台\n");

        Assert.Equal(2, report.ExitCode);
        Assert.Contains("警告 6 行目:", report.Text);
        Assert.Contains("| 5 | 1 |", report.Text);
    }

    [Fact]
    public void Errors_exit_1_without_summary()
    {
        var report = Check("  weekday:\n    - 5:00 普通 架空台\n");

        Assert.Equal(1, report.ExitCode);
        Assert.Contains("エラー 5 行目:", report.Text);
        Assert.DoesNotContain("| 時 |", report.Text);
    }
}
