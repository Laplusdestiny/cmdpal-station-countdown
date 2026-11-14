namespace StationCountdown.Core.Yaml;

/// <summary>設定・時刻表ファイルの誤り。<see cref="Line"/> は 1 始まりの行番号で、ファイル上の位置がない場合は 0。</summary>
public sealed record ConfigError(int Line, string Message)
{
    public override string ToString() => Line > 0 ? $"{Line} 行目: {Message}" : Message;
}

public sealed record LoadResult<T>(T? Value, IReadOnlyList<ConfigError> Errors)
{
    public bool Ok => Errors.Count == 0 && Value is not null;
}
