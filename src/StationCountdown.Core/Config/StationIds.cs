using System.Globalization;

namespace StationCountdown.Core.Config;

/// <summary>フォームで駅を追加するときの、既定の ID の採番。</summary>
public static class StationIds
{
    /// <summary><c>{prefix}-1</c>、<c>{prefix}-2</c>…のうち、まだ使われていない最小のもの。</summary>
    public static string Next(string prefix, IEnumerable<string> existing)
    {
        var used = existing.ToHashSet(StringComparer.Ordinal);
        for (var n = 1; ; n++)
        {
            var id = string.Create(CultureInfo.InvariantCulture, $"{prefix}-{n}");
            if (!used.Contains(id))
                return id;
        }
    }
}
