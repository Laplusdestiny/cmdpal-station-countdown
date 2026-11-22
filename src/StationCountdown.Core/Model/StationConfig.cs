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

    /// <summary>表示する種別（許可リスト、#13）。空なら絞り込まない。</summary>
    public IReadOnlyList<string> TrainTypes { get; init; } = [];

    // TrainTypes をリストの中身で比べる（record の既定ではリストが参照で比べられる）。
    public bool Equals(StationConfig? other)
        => other is not null
           && Id == other.Id && Name == other.Name && DirectionLabel == other.DirectionLabel
           && WalkMinutes == other.WalkMinutes && Source == other.Source
           && TrainTypes.SequenceEqual(other.TrainTypes, StringComparer.Ordinal);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(Name);
        hash.Add(DirectionLabel);
        hash.Add(WalkMinutes);
        hash.Add(Source);
        foreach (var t in TrainTypes)
            hash.Add(t, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}
