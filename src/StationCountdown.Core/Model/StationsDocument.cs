namespace StationCountdown.Core.Model;

/// <summary>全体の設定（spec §6.3）。省略した項目は既定値になる。</summary>
public sealed record GlobalSettings(int RefreshIntervalDays = 7, int DeparturesListLimit = 30);

/// <summary>特定の日のダイヤ種別を上書きする（spec §5.2 の 1）。</summary>
public sealed record DayTypeOverride(DateOnly Date, DayType DayType);

/// <summary><c>stations.yaml</c> の中身（spec §4.2）。編集用のメソッドは、元の文書を変えずに新しい文書を返す。</summary>
public sealed record StationsDocument(
    GlobalSettings Settings,
    IReadOnlyList<StationConfig> Stations,
    IReadOnlyList<DayTypeOverride> DayTypeOverrides)
{
    public static StationsDocument Empty { get; } = new(new GlobalSettings(), [], []);

    public StationsDocument WithStationAdded(StationConfig station) => this with { Stations = [.. Stations, station] };

    /// <summary>同じ ID の駅を、同じ位置で置き換える。</summary>
    /// <exception cref="ArgumentException">その ID の駅がない。</exception>
    public StationsDocument WithStationReplaced(StationConfig station)
    {
        var index = Stations.ToList().FindIndex(s => s.Id == station.Id);
        if (index < 0)
            throw new ArgumentException($"駅が見つかりません: {station.Id}", nameof(station));

        var stations = Stations.ToArray();
        stations[index] = station;
        return this with { Stations = stations };
    }

    public StationsDocument WithStationRemoved(string id) => this with { Stations = Stations.Where(s => s.Id != id).ToArray() };
}
