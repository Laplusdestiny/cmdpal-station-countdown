using System.Text.RegularExpressions;
using StationCountdown.Core.Model;
using StationCountdown.Core.Timetables;

namespace StationCountdown.Core.Tests;

public partial class AiTimetablePromptTests
{
    private static readonly StationConfig Station =
        new("kakuu-kita", "架空北", "南方面", 8, new StationSource(SourceType.File));

    [GeneratedRegex(@"```yaml\n(?<body>.*?)```", RegexOptions.Singleline)]
    private static partial Regex YamlBlock();

    [Fact]
    public void Build_for_station_embeds_its_id_name_and_direction()
    {
        var prompt = AiTimetablePrompt.Build(Station);

        Assert.Contains("`kakuu-kita`", prompt);
        Assert.Contains("架空北", prompt);
        Assert.Contains("南方面", prompt);
        Assert.Contains("stationId: kakuu-kita", prompt);
    }

    [Fact]
    public void Build_example_yaml_loads_without_errors_or_warnings()
    {
        var yaml = YamlBlock().Match(AiTimetablePrompt.Build(Station)).Groups["body"].Value;

        var r = TimetableFile.Parse(yaml, Station.Id);

        Assert.True(r.Ok, string.Join("\n", r.Errors));
        Assert.Empty(r.Warnings);
    }

    [Fact]
    public void Build_without_station_asks_the_user_for_the_id()
    {
        var prompt = AiTimetablePrompt.Build(null);

        Assert.Contains("利用者に確認", prompt);
        var yaml = YamlBlock().Match(prompt).Groups["body"].Value;
        Assert.True(TimetableFile.Parse(yaml, AiTimetablePrompt.ExampleStationId).Ok);
    }

    [Fact]
    public void Build_tells_the_user_to_check_the_source_terms()
    {
        // 特定の事業者は名指ししない。一般的な注意書きだけを入れる。
        Assert.Contains("取得元の規約を確認", AiTimetablePrompt.Build(null));
    }

    [Fact]
    public void Build_covers_rules_that_ai_conversions_get_wrong()
    {
        var prompt = AiTimetablePrompt.Build(null);

        Assert.Contains("24 時以降", prompt);          // 0 時以降の書き方
        Assert.Contains("日付を選んで", prompt);        // 日付で選ぶ形の時刻表
        Assert.Contains("# 要確認", prompt);            // 推測で埋めない
        Assert.Contains("時間帯ごとの本数", prompt);    // 人が見比べるための表
        Assert.Contains("TimetableCheck", prompt);      // エージェントは検証コマンドを使う
    }

    [Fact]
    public void Docs_copy_matches_generated_prompt()
    {
        var path = Path.Combine(RepoRoot(), "docs", "ai-timetable-prompt.md");

        Assert.True(File.Exists(path), $"{path} がありません。");
        Assert.True(File.ReadAllText(path).ReplaceLineEndings("\n") == AiTimetablePrompt.Build(null),
            "docs/ai-timetable-prompt.md が AiTimetablePrompt.Build(null) と違います。"
            + "`dotnet run --project tools/TimetableCheck -- prompt --out docs/ai-timetable-prompt.md` で作り直してください。");
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "StationCountdown.sln")))
                return dir.FullName;
        }
        throw new InvalidOperationException("リポジトリのルートが見つかりません。");
    }
}
