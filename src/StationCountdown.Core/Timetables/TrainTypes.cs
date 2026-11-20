using StationCountdown.Core.Model;

namespace StationCountdown.Core.Timetables;

/// <summary>「駅を編集」フォームで選ぶ、表示する種別の候補と選択値（#13）。</summary>
public static class TrainTypes
{
    /// <summary>フォームの候補 1 つ。</summary>
    /// <param name="InTimetable">時刻表にある種別なら true。選んであるのに時刻表にない種別は false。</param>
    public sealed record TrainTypeChoice(string Value, bool InTimetable);

    /// <summary>
    /// 時刻表の種別を、ダイヤの種別の順（平日 → 土曜 → 休日 → 土休日）・各ダイヤの時刻順で最初に現れた順に並べる。
    /// カンマを含む種別は選択値の区切りと区別できないので除く。その後ろに、選んであるのに時刻表にない種別を足す。
    /// </summary>
    public static IReadOnlyList<TrainTypeChoice> Choices(Timetable timetable, IReadOnlyList<string> selected)
    {
        var inTimetable = Enum.GetValues<DayType>()
            .SelectMany(t => timetable.Services.TryGetValue(t, out var list) ? list : [])
            .Select(d => d.TrainType)
            .Where(t => !t.Contains(',', StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return
        [
            .. inTimetable.Select(t => new TrainTypeChoice(t, true)),
            .. selected.Where(t => !inTimetable.Contains(t, StringComparer.Ordinal)).Select(t => new TrainTypeChoice(t, false)),
        ];
    }

    /// <summary>Adaptive Card の複数選択の値（<c>"普通,準急"</c>）を種別のリストにする。</summary>
    public static IReadOnlyList<string> ParseSelection(string value)
        => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// フォームから送られた選択値を、保存する種別にする。<paramref name="submitted"/> が null（フォームに種別の欄がなかった）なら、
    /// 今の設定を変えない。
    /// </summary>
    public static IReadOnlyList<string> ApplySelection(IReadOnlyList<string> current, string? submitted)
        => submitted is null ? current : ParseSelection(submitted);
}
