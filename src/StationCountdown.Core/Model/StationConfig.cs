namespace StationCountdown.Core.Model;

public enum SourceType
{
    Hankyu,
    File,
}

/// <summary>時刻表の取得元（spec §4.2）。<see cref="SourceType.Hankyu"/> のときだけ URL・路線・方面を持つ。</summary>
public sealed record StationSource(SourceType Type, string? StationPageUrl = null, string? Line = null, string? Direction = null);

/// <summary>登録した駅と方面の組み合わせ 1 つ（Dock のバンド 1 つに対応する）。</summary>
public sealed record StationConfig(string Id, string Name, string DirectionLabel, int WalkMinutes, StationSource Source)
{
    public const string BandCommandIdPrefix = "com.laplusdestiny.stationcountdown.band.";

    public string BandCommandId => BandCommandIdPrefix + Id;
}
