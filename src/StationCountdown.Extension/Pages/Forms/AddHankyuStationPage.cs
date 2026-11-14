#if !NO_IMPORTERS
using System.Globalization;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using StationCountdown.Core.Config;
using StationCountdown.Core.Model;
using StationCountdown.Extension.Services;
using StationCountdown.Importers;
using StationCountdown.Importers.Hankyu;

namespace StationCountdown.Extension.Pages.Forms;

/// <summary>
/// 「＋ 阪急の駅を追加」（spec §6.2）。2 段階のフォーム。
/// (1) 駅ページの URL を入れると、駅ページを取得して路線・方面の候補を作る。
/// (2) 路線・方面を選び、表示名・徒歩の分数・ID を入れて保存する。保存すると時刻表を取得する。
/// </summary>
internal sealed partial class AddHankyuStationPage : ContentPage
{
    private readonly AddHankyuStationForm _form;

    public AddHankyuStationPage(StationStore store, HankyuImporter importer, RefreshScheduler scheduler)
    {
        Id = "com.laplusdestiny.stationcountdown.add-hankyu";
        Title = "阪急の駅を追加";
        Name = "追加";
        Icon = new IconInfo("");   // Add
        _form = new AddHankyuStationForm(this, store, importer, scheduler);
    }

    public override IContent[] GetContent() => [_form];

    /// <summary>フォームの段階が変わったので、Command Palette に描き直してもらう。</summary>
    internal void Redraw() => RaiseItemsChanged();
}

internal sealed partial class AddHankyuStationForm : FormContent
{
    private const string DefaultUrl = "https://www.hankyu.co.jp/station/";
    private const string BackAction = "back";

    private readonly AddHankyuStationPage _owner;
    private readonly StationStore _store;
    private readonly HankyuImporter _importer;
    private readonly RefreshScheduler _scheduler;

    private Uri? _pageUrl;
    private HankyuStationPage? _page;

    public AddHankyuStationForm(AddHankyuStationPage owner, StationStore store, HankyuImporter importer, RefreshScheduler scheduler)
    {
        _owner = owner;
        _store = store;
        _importer = importer;
        _scheduler = scheduler;
        ShowStep1(DefaultUrl);
    }

    public override ICommandResult SubmitForm(string inputs, string data)
    {
        if (Cards.ActionOf(data) == BackAction)
        {
            ShowStep1(_pageUrl?.AbsoluteUri ?? DefaultUrl);
            _owner.Redraw();
            return CommandResult.KeepOpen();
        }

        var form = FormInputs.Parse(inputs);
        return _page is null ? SubmitStep1(form) : SubmitStep2(form, _page);
    }

    public override ICommandResult SubmitForm(string inputs) => SubmitForm(inputs, "");

    private void ShowStep1(string url)
    {
        _page = null;
        TemplateJson = Cards.Card(
            "駅ページを読み込む",
            Cards.Text("阪急公式サイトで駅を開き、その URL を貼り付けてください（例: https://www.hankyu.co.jp/station/okamachi.html）。", subtle: true),
            Cards.TextInput("url", "駅ページの URL", url, "https://www.hankyu.co.jp/station/okamachi.html"));
    }

    private ICommandResult SubmitStep1(FormInputs form)
    {
        if (!Uri.TryCreate(form.Text("url"), UriKind.Absolute, out var url)
            || !url.AbsoluteUri.StartsWith(DefaultUrl, StringComparison.Ordinal))
            return FormInputs.Reject($"{DefaultUrl} で始まる駅ページの URL を入力してください。");

        _owner.IsLoading = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var page = await _importer.FetchStationPageAsync(url, CancellationToken.None).ConfigureAwait(false);
                _pageUrl = url;
                _page = page;
                TemplateJson = Step2Card(page);
                _owner.Redraw();
            }
            catch (ImportException ex)
            {
                Log.Error("failed to read station page: " + url, ex);
                new ToastStatusMessage("駅ページを読み込めませんでした: " + ex.Message).Show();
            }
#pragma warning disable CA1031 // フォームの処理で拡張機能のプロセスを落とさない
            catch (Exception ex)
#pragma warning restore CA1031
            {
                Log.Error("failed to read station page: " + url, ex);
                new ToastStatusMessage("駅ページを読み込めませんでした。").Show();
            }
            finally
            {
                _owner.IsLoading = false;
            }
        });

        return CommandResult.KeepOpen();
    }

    private string Step2Card(HankyuStationPage page)
    {
        var choices = page.Current.Select((d, i) => (Title: $"{d.Line} {d.Direction}", Value: i.ToString(CultureInfo.InvariantCulture)));
        return Cards.CardWithActions(
            [Cards.Submit("追加して時刻表を取得"), Cards.Submit("駅ページを選び直す", BackAction, validate: false)],
            [
                Cards.Text(Cards.CommentNotice, subtle: true),
                Cards.ChoiceSet("direction", "路線・方面", choices, "0"),
                Cards.TextInput("name", "表示名", page.StationName ?? "", "岡町"),
                Cards.NumberInput("walkMinutes", "家から駅までの徒歩の分数", 5, 0, 120),
                Cards.TextInput("id", "ID（半角英小文字・数字と -）", StationIds.Next("hankyu", _store.Current.Stations.Select(s => s.Id))),
            ]);
    }

    private ICommandResult SubmitStep2(FormInputs form, HankyuStationPage page)
    {
        if (form.Int("direction") is not { } index || index < 0 || index >= page.Current.Count)
            return FormInputs.Reject("路線・方面を選んでください。");
        if (form.Int("walkMinutes") is not { } walk)
            return FormInputs.Reject("徒歩の分数は整数で入力してください。");

        var link = page.Current[index];
        var station = new StationConfig(form.Text("id"), form.Text("name"), link.Direction, walk,
            new StationSource(SourceType.Hankyu, _pageUrl!.AbsoluteUri, link.Line, link.Direction));
        var doc = _store.Current.WithStationAdded(station);
        var errors = StationsFile.Validate(doc);
        if (errors.Count > 0)
            return FormInputs.Reject(string.Join(" / ", errors.Select(e => e.Message)));

        if (_store.TrySave(doc) is { } saveError)
            return FormInputs.Reject(saveError);
        Log.Info("station added from form: " + station.Id);
        ShowStep1(DefaultUrl);

        _ = Task.Run(async () =>
        {
            var ok = await _scheduler.RefreshNowAsync(station.Id).ConfigureAwait(false);
            new ToastStatusMessage(ok
                ? $"{station.Name} {station.DirectionLabel} の時刻表を取得しました。"
                : $"{station.Name} {station.DirectionLabel} の時刻表を取得できませんでした。あとで自動で再試行します。").Show();
        });

        return CommandResult.ShowToast(new ToastArgs
        {
            Message = $"{station.Name} {station.DirectionLabel} を追加しました。時刻表を取得しています…",
            Result = CommandResult.GoHome(),
        });
    }
}
#endif
