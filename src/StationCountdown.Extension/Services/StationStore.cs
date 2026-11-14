using System.Globalization;
using System.Text;
using StationCountdown.Core.Config;
using StationCountdown.Core.Model;
using StationCountdown.Core.Timetables;
using StationCountdown.Core.Yaml;

namespace StationCountdown.Extension.Services;

/// <summary>
/// <c>stations.yaml</c> と時刻表ファイルを読み、変更を監視して保持する（spec §4、§5.6、§8）。
/// 設定が読めなくなっても、最後に正しく読めた設定で動き続ける（<see cref="StationsState"/>）。
/// </summary>
internal sealed partial class StationStore : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(500);
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private const string InitialStationsYaml = """
        # yaml-language-server: $schema=./schemas/stations.schema.json
        # 駅カウントダウンの設定。書き方は schemas/stations.schema.json と README を参照。
        # Command Palette の「駅カウントダウン」→「＋ 阪急の駅を追加」「＋ ファイルで駅を追加」からも追加できる。
        schemaVersion: 1
        stations: []

        """;

    private readonly Lock _gate = new();
    private readonly StationsState _state = new();
    private readonly Dictionary<string, Timetable> _timetables = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<ConfigError>> _timetableErrors = new(StringComparer.Ordinal);
    private readonly HashSet<string> _fetchFailed = new(StringComparer.Ordinal);
    private readonly FileSystemWatcher? _stationsWatcher;
    private readonly FileSystemWatcher? _timetablesWatcher;
    private readonly ITimer _debounceTimer;

    public StationStore(TimeProvider time)
    {
        _debounceTimer = time.CreateTimer(_ => SafeReload(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        EnsureInitialFiles();
        Reload();

        _stationsWatcher = CreateWatcher(AppPaths.Root, "stations.yaml");
        _timetablesWatcher = CreateWatcher(AppPaths.TimetablesDir, "*.yaml");
    }

    /// <summary>設定か時刻表が変わった（読み直した、取得の失敗状態が変わった）。</summary>
    public event Action? Changed;

    public StationsDocument Current => _state.Current;

    public IReadOnlyList<ConfigError> Errors => _state.Errors;

    public StationConfig? FindStation(string id) => Current.Stations.FirstOrDefault(s => s.Id == id);

    public Timetable? GetTimetable(string id)
    {
        lock (_gate)
            return _timetables.GetValueOrDefault(id);
    }

    public IReadOnlyList<ConfigError> GetTimetableErrors(string id)
    {
        lock (_gate)
            return _timetableErrors.GetValueOrDefault(id) ?? [];
    }

    public bool IsFetchFailed(string id)
    {
        lock (_gate)
            return _fetchFailed.Contains(id);
    }

    public void SetFetchFailed(string id, bool failed)
    {
        bool changed;
        lock (_gate)
            changed = failed ? _fetchFailed.Add(id) : _fetchFailed.Remove(id);
        if (changed)
            RaiseChanged();
    }

    /// <summary>
    /// 設定を保存する。保存の直前の内容を <c>stations.yaml.bak</c> に残す。手で書いたコメントは消える（spec §6.2）。
    /// </summary>
    public void Save(StationsDocument doc)
    {
        var errors = StationsFile.Validate(doc);
        if (errors.Count > 0)
            throw new InvalidOperationException("保存しようとした設定に誤りがあります: " + string.Join(" / ", errors));

        var path = AppPaths.StationsYaml;
        if (File.Exists(path))
            File.Copy(path, AppPaths.StationsBak, overwrite: true);

        var tmp = path + ".tmp";
        File.WriteAllText(tmp, StationsFile.Serialize(doc), Utf8NoBom);
        File.Move(tmp, path, overwrite: true);
        Log.Info("stations.yaml saved (" + doc.Stations.Count.ToString(CultureInfo.InvariantCulture) + " stations)");

        Reload();
    }

    /// <summary>設定とすべての駅の時刻表を読み直す。</summary>
    public void Reload()
    {
        var result = ReadStations();
        _state.Apply(result);
        foreach (var e in result.Errors)
            Log.Error("stations.yaml: " + e);

        var timetables = new Dictionary<string, Timetable>(StringComparer.Ordinal);
        var timetableErrors = new Dictionary<string, IReadOnlyList<ConfigError>>(StringComparer.Ordinal);
        foreach (var station in _state.Current.Stations)
        {
            var path = AppPaths.TimetablePath(station.Id);
            if (!File.Exists(path))
                continue;

            var text = ReadWithRetry(path);
            var parsed = text is null
                ? new LoadResult<Timetable>(null, [new ConfigError(0, "時刻表ファイルを読めませんでした。")])
                : TimetableFile.Parse(text, station.Id);
            if (parsed.Ok)
            {
                timetables[station.Id] = parsed.Value!;
            }
            else
            {
                timetableErrors[station.Id] = parsed.Errors;
                foreach (var e in parsed.Errors)
                    Log.Error($"timetables/{station.Id}.yaml: {e}");
            }
        }

        lock (_gate)
        {
            _timetables.Clear();
            foreach (var (k, v) in timetables)
                _timetables[k] = v;
            _timetableErrors.Clear();
            foreach (var (k, v) in timetableErrors)
                _timetableErrors[k] = v;
        }

        RaiseChanged();
    }

    public void Dispose()
    {
        _stationsWatcher?.Dispose();
        _timetablesWatcher?.Dispose();
        _debounceTimer.Dispose();
    }

    private static LoadResult<StationsDocument> ReadStations()
    {
        var text = ReadWithRetry(AppPaths.StationsYaml);
        return text is null
            ? new LoadResult<StationsDocument>(null, [new ConfigError(0, "stations.yaml を読めませんでした。")])
            : StationsFile.Parse(text);
    }

    /// <summary>エディタが保存している途中などでファイルが開けないときは、200ms あけて 3 回まで試す（spec §8）。</summary>
    private static string? ReadWithRetry(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (IOException ex) when (attempt < 3)
            {
                Log.Info($"retrying read of {Path.GetFileName(path)}: {ex.Message}");
                Thread.Sleep(200);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error("failed to read " + path, ex);
                return null;
            }
        }
    }

    private static void EnsureInitialFiles()
    {
        try
        {
            if (!File.Exists(AppPaths.StationsYaml))
                File.WriteAllText(AppPaths.StationsYaml, InitialStationsYaml.ReplaceLineEndings("\n"), Utf8NoBom);

            // エディタの補完用の JSON Schema は、拡張機能のバージョンに合わせて毎回上書きする。
            var bundled = Path.Combine(AppContext.BaseDirectory, "schemas");
            if (Directory.Exists(bundled))
            {
                foreach (var file in Directory.EnumerateFiles(bundled, "*.schema.json"))
                    File.Copy(file, Path.Combine(AppPaths.SchemasDir, Path.GetFileName(file)), overwrite: true);
            }
        }
#pragma warning disable CA1031 // 初期ファイルが作れなくても、駅 0 件で動き続ける
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Log.Error("failed to prepare initial files", ex);
        }
    }

    private FileSystemWatcher? CreateWatcher(string directory, string filter)
    {
        try
        {
            var watcher = new FileSystemWatcher(directory, filter)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            watcher.Changed += OnFileEvent;
            watcher.Created += OnFileEvent;
            watcher.Deleted += OnFileEvent;
            watcher.Renamed += OnFileEvent;
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
#pragma warning disable CA1031 // 監視できなくても、手動の再読み込みとフォームの保存は使える
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Log.Error("failed to watch " + directory, ex);
            return null;
        }
    }

    // エディタは 1 回の保存で何度もイベントを出すので、最後のイベントから 500ms 待ってから読み直す。
    private void OnFileEvent(object sender, FileSystemEventArgs e) => _debounceTimer.Change(Debounce, Timeout.InfiniteTimeSpan);

    private void SafeReload()
    {
        try
        {
            Reload();
        }
#pragma warning disable CA1031 // タイマーの処理で拡張機能のプロセスを落とさない（spec §8）
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Log.Error("reload failed", ex);
        }
    }

    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke();
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Log.Error("Changed handler failed", ex);
        }
    }
}
