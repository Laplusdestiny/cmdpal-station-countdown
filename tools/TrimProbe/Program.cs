// spec §10 の V4・V5 の検証。トリミングして publish したものを実行し、すべて期待どおりなら "PROBE OK" で終了コード 0。
using System.Text;
using StationCountdown.Core.Calendar;
using StationCountdown.Core.Config;
using StationCountdown.Core.Model;
using StationCountdown.Core.Timetables;
using StationCountdown.Importers.Hankyu;

var failures = new List<string>();

void Check(string name, bool ok, string detail = "")
{
    Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {name}{(ok || detail.Length == 0 ? "" : $": {detail}")}");
    if (!ok)
        failures.Add(name);
}

void Run(string name, Action action)
{
    try
    {
        action();
    }
    catch (Exception ex)
    {
        Check(name, false, ex.ToString());
    }
}

// ---- V4: stations.yaml の往復（spec §4.2 の例を架空化） ----
Run("V4 stations.yaml", () =>
{
    const string stationsYaml = """
        # yaml-language-server: $schema=./schemas/stations.schema.json
        schemaVersion: 1
        settings:
          refreshIntervalDays: 7
          departuresListLimit: 30
        stations:
          - id: kakuu-kita
            name: 架空北
            directionLabel: 北方面
            walkMinutes: 8
            source:
              type: hankyu
              stationPageUrl: https://www.hankyu.co.jp/station/kakuu.html
              line: 架空線
              direction: 北方面
          - id: kakuu-minami
            name: '架空: 南'
            directionLabel: 南方面
            walkMinutes: 10
            source:
              type: file
        dayTypeOverrides:
          - { date: 2026-12-31, dayType: saturdayHoliday }
        """;

    var first = StationsFile.Parse(stationsYaml);
    Check("V4 stations parse", first.Ok, string.Join("; ", first.Errors));
    if (!first.Ok)
        return;

    var second = StationsFile.Parse(StationsFile.Serialize(first.Value!));
    Check("V4 stations roundtrip", second.Ok
        && second.Value!.Stations.SequenceEqual(first.Value!.Stations)
        && second.Value.DayTypeOverrides.SequenceEqual(first.Value.DayTypeOverrides)
        && second.Value.Settings == first.Value.Settings
        && second.Value.Stations[1].Name == "架空: 南");

    var broken = StationsFile.Parse("schemaVersion: 1\nstations:\n  - id: Bad_ID\n    name: [unterminated\n");
    Check("V4 stations error has line number", !broken.Ok && broken.Errors.All(e => e.Line > 0), string.Join("; ", broken.Errors));
});

// ---- V4: 時刻表 YAML の往復 ----
Run("V4 timetable yaml", () =>
{
    const string timetableYaml = """
        schemaVersion: 1
        stationId: kakuu-kita
        fetchedAt: 2026-11-09T12:00:00+09:00
        sourceUrls:
          - https://example.invalid/kk_2_w.html
        services:
          weekday:
            - 24:10 普通 架空町
            - 05:00 普通 架空台
          saturdayHoliday:
            - 06:00 普通 架空台
        """;

    var first = TimetableFile.Parse(timetableYaml, "kakuu-kita");
    Check("V4 timetable parse", first.Ok, string.Join("; ", first.Errors));
    if (!first.Ok)
        return;

    var second = TimetableFile.Parse(TimetableFile.Serialize(first.Value!), "kakuu-kita");
    Check("V4 timetable roundtrip", second.Ok
        && second.Value!.FetchedAt == first.Value!.FetchedAt
        && second.Value.Services[DayType.Weekday].SequenceEqual(first.Value.Services[DayType.Weekday])
        && second.Value.Services[DayType.Weekday][0].ToLine() == "05:00 普通 架空台");

    var broken = TimetableFile.Parse("schemaVersion: 1\nstationId: kakuu-kita\nservices:\n  weekday:\n    - 05:00　普通　架空台\n", "kakuu-kita");
    Check("V4 timetable error has line number", !broken.Ok && broken.Errors.Any(e => e.Line == 5), string.Join("; ", broken.Errors));
});

// ---- V5: AngleSharp による阪急 HTML の解析 ----
Run("V5 hankyu html", () =>
{
    var dir = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    var timetable = HankyuTimetableParser.Parse(File.ReadAllText(Path.Combine(dir, "hankyu_timetable_weekday.html")));
    Check("V5 timetable page", timetable.Select(d => d.ToLine()).SequenceEqual(
        ["05:00 普通 架空台", "05:30 急行 架空台", "23:50 普通 架空台", "24:10 普通 架空町"]),
        string.Join(", ", timetable.Select(d => d.ToLine())));

    var page = HankyuStationPageParser.Parse(File.ReadAllText(Path.Combine(dir, "hankyu_station_page.html")),
        new Uri("https://www.hankyu.co.jp/station/kakuu.html"));
    Check("V5 station page", page.Current.Count == 2 && page.HasRevisedTimetable
        && page.Current[0].WeekdayUrl.AbsoluteUri == "https://www.hankyu.co.jp/station/html/HK-99_kk_2_w.html");
});

// ---- 拡張機能で使うもの: Shift_JIS の祝日 CSV ----
Run("holiday csv", () =>
{
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    var bytes = Encoding.GetEncoding(932).GetBytes("国民の祝日・休日月日,国民の祝日・休日名称\r\n2026/11/3,文化の日\r\n");
    var calendar = HolidayCalendar.ParseCsv(bytes);
    Check("holiday csv (Shift_JIS)", calendar.IsHoliday(new DateOnly(2026, 11, 3)) && calendar.LastDate == new DateOnly(2026, 11, 3));
});

if (failures.Count > 0)
{
    Console.WriteLine($"PROBE FAILED ({failures.Count})");
    return 1;
}

Console.WriteLine("PROBE OK");
return 0;
