using System.Globalization;
using System.Text;

namespace StationCountdown.Core.Calendar;

/// <summary>
/// 内閣府「国民の祝日について」の <c>syukujitsu.csv</c>（Shift_JIS、<c>yyyy/M/d,名称</c>）から作る祝日の一覧（spec §5.3）。
/// </summary>
public sealed class HolidayCalendar
{
    private static readonly string[] DateFormats = ["yyyy/M/d"];

    private readonly HashSet<DateOnly> _holidays;

    private HolidayCalendar(HashSet<DateOnly> holidays)
    {
        _holidays = holidays;
        LastDate = holidays.Count > 0 ? holidays.Max() : null;
    }

    public static HolidayCalendar Empty { get; } = new([]);

    /// <summary>データに含まれる最後の祝日。データがなければ null。</summary>
    public DateOnly? LastDate { get; }

    public bool IsHoliday(DateOnly date) => _holidays.Contains(date);

    /// <summary>
    /// <paramref name="date"/> がデータの範囲内（最後の祝日以前）かどうか。範囲外の日は、祝日かどうかを判定できない。
    /// </summary>
    public bool Covers(DateOnly date) => LastDate is { } last && date <= last;

    /// <summary>
    /// CSV を読む。ヘッダー行、空行、日付として読めない行は読み飛ばす。各列の前後の空白は無視する。
    /// </summary>
    public static HolidayCalendar ParseCsv(byte[] shiftJisBytes)
    {
        // .NET Core では Shift_JIS（コードページ 932）を使う前にプロバイダーの登録が必要。何度呼んでもよい。
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var text = Encoding.GetEncoding(932).GetString(shiftJisBytes);

        var holidays = new HashSet<DateOnly>();
        foreach (var rawLine in text.Split('\n'))
        {
            var firstColumn = rawLine.Split(',', 2)[0].Trim();
            if (DateOnly.TryParseExact(firstColumn, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                holidays.Add(date);
        }
        return new HolidayCalendar(holidays);
    }
}
