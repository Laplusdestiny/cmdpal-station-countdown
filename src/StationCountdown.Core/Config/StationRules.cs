using System.Text.RegularExpressions;

namespace StationCountdown.Core.Config;

/// <summary>
/// <c>stations.yaml</c> の検証ルール（spec §4.2、§6.3）。YAML の読み込みとフォームの検証（Task 15）で共有する。
/// 各メソッドは、問題がなければ null、あればエラーメッセージを返す。
/// </summary>
internal static partial class StationRules
{
    public const int IdMaxLength = 64;
    public const int WalkMinutesMin = 0, WalkMinutesMax = 120;
    public const int RefreshIntervalDaysMin = 1, RefreshIntervalDaysMax = 30;
    public const int DeparturesListLimitMin = 5, DeparturesListLimitMax = 100;
    public const string HankyuUrlPrefix = "https://www.hankyu.co.jp/";

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    public static string? CheckId(string id)
    {
        if (id.Length > IdMaxLength)
            return $"id は {IdMaxLength} 文字以内にしてください（{id.Length} 文字）: {id}";
        if (!IdPattern().IsMatch(id))
            return $"id には半角英小文字・数字と、間に挟む「-」だけが使えます: {id}";
        return null;
    }

    public static string? CheckNotBlank(string key, string value)
        => string.IsNullOrWhiteSpace(value) ? $"{key} が空です。" : null;

    public static string? CheckRange(string key, int value, int min, int max)
        => value < min || value > max ? $"{key} は {min}〜{max} の整数で書いてください: {value}" : null;

    /// <summary><c>trainTypes</c> の 1 要素（#13）。時刻表の種別と同じく空白を含めない。カンマはフォームの複数選択の区切りなので使えない。</summary>
    public static string? CheckTrainType(string value)
    {
        if (value.Length == 0)
            return "trainTypes に空の種別があります。";
        if (value.Any(char.IsWhiteSpace))
            return $"trainTypes の種別には空白を入れられません: \"{value}\"";
        if (value.Contains(',', StringComparison.Ordinal))
            return $"trainTypes の種別にはカンマを入れられません: {value}";
        return null;
    }

    public static string DuplicateTrainType(string value) => $"trainTypes に同じ種別が複数あります: {value}";

    public static string? CheckHankyuUrl(string url)
    {
        if (!url.StartsWith(HankyuUrlPrefix, StringComparison.Ordinal)
            || !Uri.TryCreate(url, UriKind.Absolute, out _))
            return $"stationPageUrl は {HankyuUrlPrefix} で始まる URL にしてください: {url}";
        return null;
    }
}
