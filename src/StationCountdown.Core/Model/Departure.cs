using System.Globalization;
using System.Text.RegularExpressions;

namespace StationCountdown.Core.Model;

/// <summary>時刻表の 1 本（spec §4.3）。</summary>
/// <param name="Minutes">営業日の 0:00 からの分数（0〜1799）。24:00 以降は翌日の未明。</param>
public sealed partial record Departure(int Minutes, string TrainType, string Destination)
{
    public const int MaxMinutes = 29 * 60 + 59;

    // 区切りは半角スペース 1 つだけ。全角スペースやタブは \s に含まれるので、種別・行先にも入らない。
    [GeneratedRegex(@"^(?<h>[0-2][0-9]):(?<m>[0-5][0-9]) (?<t>[^\s]+) (?<d>[^\s]+)\z", RegexOptions.CultureInvariant)]
    private static partial Regex LinePattern();

    /// <summary><c>"HH:MM 種別 行先"</c> を読む。不正な行では <see cref="FormatException"/> を投げる。</summary>
    public static Departure ParseLine(string line)
    {
        var m = LinePattern().Match(line);
        if (!m.Success)
            throw new FormatException($"「HH:MM 種別 行先」の形（半角スペース区切り）ではありません: \"{line}\"");

        var hour = int.Parse(m.Groups["h"].ValueSpan, CultureInfo.InvariantCulture);
        if (hour > 29)
            throw new FormatException($"時は 00〜29 の範囲で書いてください: \"{line}\"");

        var minute = int.Parse(m.Groups["m"].ValueSpan, CultureInfo.InvariantCulture);
        return new Departure(hour * 60 + minute, m.Groups["t"].Value, m.Groups["d"].Value);
    }

    /// <summary>時刻表ファイルに書く形（<c>"HH:MM 種別 行先"</c>、時は 2 桁）。</summary>
    public string ToLine()
        => string.Create(CultureInfo.InvariantCulture, $"{Minutes / 60:00}:{Minutes % 60:00} {TrainType} {Destination}");

    /// <summary>表示用の <c>H:MM</c>。24 時以降は 0 時台に戻す。</summary>
    public string DisplayTime
        => string.Create(CultureInfo.InvariantCulture, $"{Minutes / 60 % 24}:{Minutes % 60:00}");
}
