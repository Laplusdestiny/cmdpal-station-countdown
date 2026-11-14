using StationCountdown.Core.Model;
using StationCountdown.Core.Yaml;

namespace StationCountdown.Core.Config;

/// <summary>
/// 最後に正しく読めた <c>stations.yaml</c> を保持する（spec §8）。
/// 読み込みに失敗しても <see cref="Current"/> は前の内容のまま残し、<see cref="Errors"/> だけを更新する。
/// </summary>
/// <remarks>
/// ファイル監視のスレッドから <see cref="Apply"/> し、UI のスレッドから読むことを想定している。
/// 文書とエラーを 1 つのスナップショットにまとめて差し替えるので、読む側が両者の食い違いを見ることはない。
/// </remarks>
public sealed class StationsState
{
    private sealed record Snapshot(StationsDocument Document, IReadOnlyList<ConfigError> Errors);

    private volatile Snapshot _snapshot = new(StationsDocument.Empty, []);

    public StationsDocument Current => _snapshot.Document;

    public IReadOnlyList<ConfigError> Errors => _snapshot.Errors;

    public void Apply(LoadResult<StationsDocument> result)
    {
        var previous = _snapshot;
        _snapshot = result.Ok
            ? new Snapshot(result.Value!, [])
            : new Snapshot(previous.Document, result.Errors.Count > 0 ? result.Errors : [new ConfigError(0, "設定を読み込めませんでした。")]);
    }
}
