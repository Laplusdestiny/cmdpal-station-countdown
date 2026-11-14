using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using StationCountdown.Core.Model;
using StationCountdown.Extension.Services;

namespace StationCountdown.Extension.Pages;

/// <summary>駅の右クリックのメニューと、駅一覧の末尾に置くコマンド（spec §6.1、§6.2）。</summary>
internal sealed class StationActions(StationStore store)
{
    private const string FolderGlyph = "\uE838";   // FolderOpen
    private const string LogGlyph = "\uE9F9";      // ReportDocument

    /// <summary>駅一覧の行の右クリックのメニュー。</summary>
    public IContextItem[] ForStation(StationConfig station) => [.. OpenTimetable(station), OpenSettingsFolder()];

    /// <summary>Dock のバンドの右クリックのメニュー。</summary>
    public IContextItem[] ForBand(StationConfig station) => [.. OpenTimetable(station), OpenSettingsFolder()];

    /// <summary>駅一覧の末尾に並べるコマンド。</summary>
    public static IListItem[] Footer() =>
    [
        new ListItem(new OpenUrlCommand(AppPaths.Root) { Name = "開く" })
        {
            Title = "設定フォルダを開く",
            Subtitle = AppPaths.Root,
            Icon = new IconInfo(FolderGlyph),
        },
        new ListItem(new OpenUrlCommand(AppPaths.LogsDir) { Name = "開く" })
        {
            Title = "ログフォルダを開く",
            Subtitle = AppPaths.LogsDir,
            Icon = new IconInfo(LogGlyph),
        },
    ];

    /// <summary>時刻表ファイルがあれば「時刻表ファイルを開く」（file 型の駅で、手で直すとき用）。</summary>
    private IEnumerable<IContextItem> OpenTimetable(StationConfig station)
    {
        if (store.GetTimetable(station.Id) is not null || store.GetTimetableErrors(station.Id).Count > 0)
            yield return new CommandContextItem(new OpenUrlCommand(AppPaths.TimetablePath(station.Id)) { Name = "時刻表ファイルを開く" });
    }

    private static CommandContextItem OpenSettingsFolder()
        => new(new OpenUrlCommand(AppPaths.Root) { Name = "設定フォルダを開く", Icon = new IconInfo(FolderGlyph) });
}
