namespace StationCountdown.Importers;

/// <summary>時刻表の取得や解析に失敗した。呼び出し側は前回の時刻表で表示を続ける（spec §8）。</summary>
public sealed class ImportException(string message, Exception? inner = null) : Exception(message, inner);
