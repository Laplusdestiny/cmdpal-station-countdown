using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using StationCountdown.Core.Model;
using StationCountdown.Extension.Pages.Forms;
using StationCountdown.Extension.Services;
#if !NO_IMPORTERS
using StationCountdown.Importers.Hankyu;
#endif

namespace StationCountdown.Extension.Pages;

/// <summary>駅の右クリックのメニューと、駅一覧の末尾に置くコマンド（spec §6.1、§6.2）。</summary>
internal sealed class StationActions
{
    private const string FolderGlyph = "";   // FolderOpen
    private const string LogGlyph = "";      // ReportDocument

    private readonly StationStore _store;
    private readonly AddFileStationPage _addFile;
#if !NO_IMPORTERS
    private readonly RefreshScheduler _scheduler;
    private readonly AddHankyuStationPage _addHankyu;

    public StationActions(StationStore store, HankyuImporter importer, RefreshScheduler scheduler)
    {
        _store = store;
        _scheduler = scheduler;
        _addFile = new AddFileStationPage(store);
        _addHankyu = new AddHankyuStationPage(store, importer, scheduler);
    }
#else
    public StationActions(StationStore store)
    {
        _store = store;
        _addFile = new AddFileStationPage(store);
    }
#endif

    /// <summary>駅一覧の行の右クリックのメニュー（編集・削除・再取得）。</summary>
    public IContextItem[] ForStation(StationConfig station) =>
    [
        new CommandContextItem(new EditStationPage(station.Id, _store)),
        new CommandContextItem(new DeleteStationCommand(station, _store)) { IsCritical = true },
        .. Refresh(station),
        .. OpenTimetable(station),
        OpenSettingsFolder(),
    ];

    /// <summary>Dock のバンドの右クリックのメニュー（再取得・編集・設定フォルダを開く）。</summary>
    public IContextItem[] ForBand(StationConfig station) =>
    [
        .. Refresh(station),
        new CommandContextItem(new EditStationPage(station.Id, _store)),
        OpenSettingsFolder(),
    ];

    /// <summary>駅一覧の末尾に並べるコマンド。</summary>
    public IListItem[] Footer() =>
    [
#if !NO_IMPORTERS
        new ListItem(_addHankyu) { Title = "＋ 阪急の駅を追加", Subtitle = "公式サイトの駅ページから時刻表を取り込む", Icon = new IconInfo("") },
#endif
        new ListItem(_addFile) { Title = "＋ ファイルで駅を追加", Subtitle = "自分で用意した時刻表の YAML ファイルを使う", Icon = new IconInfo("") },
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

    private IEnumerable<IContextItem> Refresh(StationConfig station)
    {
#if !NO_IMPORTERS
        if (_scheduler.CanRefresh(station))
            yield return new CommandContextItem(new RefreshTimetableCommand(station, _scheduler));
#else
        _ = station;
        yield break;
#endif
    }

    /// <summary>時刻表ファイルがあれば「時刻表ファイルを開く」（file 型の駅で、手で直すとき用）。</summary>
    private IEnumerable<IContextItem> OpenTimetable(StationConfig station)
    {
        if (_store.GetTimetable(station.Id) is not null || _store.GetTimetableErrors(station.Id).Count > 0)
            yield return new CommandContextItem(new OpenUrlCommand(AppPaths.TimetablePath(station.Id)) { Name = "時刻表ファイルを開く" });
    }

    private static CommandContextItem OpenSettingsFolder()
        => new(new OpenUrlCommand(AppPaths.Root) { Name = "設定フォルダを開く", Icon = new IconInfo(FolderGlyph) });
}
