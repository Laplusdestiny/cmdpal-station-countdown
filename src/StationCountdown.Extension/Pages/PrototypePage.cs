using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using StationCountdown.Extension.Services;

namespace StationCountdown.Extension.Pages;

/// <summary>試作の段階のトップレベルページ（Task 14 で StationsPage に置き換える）。</summary>
internal sealed partial class PrototypePage : ListPage
{
    public PrototypePage()
    {
        Icon = new IconInfo("");
        Title = "駅カウントダウン";
        Name = "開く";
    }

    public override IListItem[] GetItems() =>
    [
        new ListItem(new OpenUrlCommand(AppPaths.LogsDir) { Name = "開く" })
        {
            Title = "ログフォルダを開く",
            Subtitle = AppPaths.LogsDir,
        },
        new ListItem(new OpenUrlCommand(AppPaths.Root) { Name = "開く" })
        {
            Title = "設定フォルダを開く",
            Subtitle = AppPaths.Root,
        },
    ];
}
