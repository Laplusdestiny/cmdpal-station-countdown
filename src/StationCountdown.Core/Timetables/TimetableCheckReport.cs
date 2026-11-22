using System.Globalization;
using System.Text;
using StationCountdown.Core.Yaml;

namespace StationCountdown.Core.Timetables;

/// <summary>
/// 時刻表ファイルの検証結果（<c>tools/TimetableCheck</c> の出力）。AI エージェントが「直して、また検証する」を
/// 繰り返せるように、終了コードで結果を分ける: 0 = 問題なし、1 = エラー（読み込めない）、2 = 警告だけ。
/// </summary>
public sealed record TimetableCheckReport(int ExitCode, string Text)
{
    public const int Clean = 0;
    public const int HasErrors = 1;
    public const int HasWarnings = 2;

    public static TimetableCheckReport Create(string yaml, string expectedStationId)
    {
        var result = TimetableFile.Parse(yaml, expectedStationId);
        var sb = new StringBuilder();
        Append(sb, "エラー", result.Errors);
        Append(sb, "警告", result.Warnings);

        if (!result.Ok)
            return new(HasErrors, sb.ToString());

        if (result.Warnings.Count == 0)
            sb.Append("エラー・警告はありません。\n");
        sb.Append('\n').Append(TimetableSummary.ToMarkdown(result.Value!));
        return new(result.Warnings.Count > 0 ? HasWarnings : Clean, sb.ToString());
    }

    private static void Append(StringBuilder sb, string kind, IReadOnlyList<ConfigError> items)
    {
        foreach (var e in items)
        {
            sb.Append(e.Line > 0
                ? string.Create(CultureInfo.InvariantCulture, $"{kind} {e.Line} 行目: {e.Message}\n")
                : $"{kind}: {e.Message}\n");
        }
    }
}
