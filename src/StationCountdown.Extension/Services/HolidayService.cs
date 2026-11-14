using StationCountdown.Core.Calendar;

namespace StationCountdown.Extension.Services;

/// <summary>
/// 祝日データ（spec §5.3）。同梱の CSV と取得したキャッシュのうち、新しいほうを使う。
/// どちらも今年の 12/31 までを含まないときだけ、内閣府の CSV を取りに行く（1 日 1 回まで）。
/// </summary>
internal sealed class HolidayService
{
    private const string SourceUrl = "https://www8.cao.go.jp/chosei/shukujitsu/syukujitsu.csv";

    private readonly Lock _gate = new();
    private volatile HolidayCalendar _calendar = HolidayCalendar.Empty;
    private DateOnly? _lastAttempt;

    public event Action? Changed;

    public HolidayCalendar Calendar => _calendar;

    /// <summary>同梱の CSV とキャッシュを読む。どちらも読めなければ、土日だけで判定する（祝日なし）。</summary>
    public void LoadLocal()
    {
        var bundled = Load(Path.Combine(AppContext.BaseDirectory, "Assets", "syukujitsu.csv"));
        var cached = Load(AppPaths.HolidaysCsv);
        _calendar = Newer(bundled, cached);
        Log.Info($"holidays loaded (last date: {_calendar.LastDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "none"})");
    }

    public async Task EnsureCurrentAsync(HttpClient http, TimeProvider time, CancellationToken ct)
    {
        var today = ServiceDayResolver.GetServiceDate(time.GetUtcNow());
        if (_calendar.Covers(new DateOnly(today.Year, 12, 31)))
            return;

        lock (_gate)
        {
            if (_lastAttempt == today)
                return;
            _lastAttempt = today;
        }

        // 前のプロセスで今日すでに取得していれば、取りに行かない。
        if (File.Exists(AppPaths.HolidaysCsv)
            && DateOnly.FromDateTime(File.GetLastWriteTime(AppPaths.HolidaysCsv)) == DateOnly.FromDateTime(DateTime.Now))
            return;

        try
        {
            var bytes = await http.GetByteArrayAsync(new Uri(SourceUrl), ct).ConfigureAwait(false);
            var fetched = HolidayCalendar.ParseCsv(bytes);
            if (fetched.LastDate is null)
            {
                Log.Error("downloaded holiday CSV has no dates");
                return;
            }

            var tmp = AppPaths.HolidaysCsv + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes, ct).ConfigureAwait(false);
            File.Move(tmp, AppPaths.HolidaysCsv, overwrite: true);

            var previous = _calendar;
            _calendar = Newer(previous, fetched);
            Log.Info($"holidays downloaded (last date: {fetched.LastDate:yyyy-MM-dd})");
            if (!ReferenceEquals(previous, _calendar))
                Changed?.Invoke();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // 祝日が取れなくても、前のデータ（または土日だけ）で判定を続ける
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Log.Error("failed to download holidays", ex);
        }
    }

    private static HolidayCalendar Newer(HolidayCalendar a, HolidayCalendar b)
        => (b.LastDate ?? DateOnly.MinValue) > (a.LastDate ?? DateOnly.MinValue) ? b : a;

    private static HolidayCalendar Load(string path)
    {
        try
        {
            return File.Exists(path) ? HolidayCalendar.ParseCsv(File.ReadAllBytes(path)) : HolidayCalendar.Empty;
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Log.Error("failed to read " + path, ex);
            return HolidayCalendar.Empty;
        }
    }
}
