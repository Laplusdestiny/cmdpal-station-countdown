namespace StationCountdown.Core.Model;

/// <summary>全体の設定（spec §6.3）。省略した項目は既定値になる。</summary>
public sealed record GlobalSettings(int RefreshIntervalDays = 7, int DeparturesListLimit = 30);

/// <summary>特定の日のダイヤ種別を上書きする（spec §5.2 の 1）。</summary>
public sealed record DayTypeOverride(DateOnly Date, DayType DayType);

/// <summary><c>stations.yaml</c> の中身（spec §4.2）。</summary>
public sealed record StationsDocument(
    GlobalSettings Settings,
    IReadOnlyList<StationConfig> Stations,
    IReadOnlyList<DayTypeOverride> DayTypeOverrides)
{
    public static StationsDocument Empty { get; } = new(new GlobalSettings(), [], []);
}
