using StationCountdown.Core.Model;

namespace StationCountdown.Importers;

/// <summary>外部から時刻表を取り込む。取り込みに失敗したら <see cref="ImportException"/> を投げる。</summary>
public interface ITimetableImporter
{
    bool CanImport(StationConfig station);

    Task<Timetable> ImportAsync(StationConfig station, CancellationToken ct);
}
