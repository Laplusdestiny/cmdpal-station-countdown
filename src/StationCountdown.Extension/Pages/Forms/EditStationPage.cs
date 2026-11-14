using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using StationCountdown.Core.Config;
using StationCountdown.Core.Model;
using StationCountdown.Extension.Services;

namespace StationCountdown.Extension.Pages.Forms;

/// <summary>「駅を編集」（spec §6.2）。表示名・方面・徒歩の分数を変える。</summary>
internal sealed partial class EditStationPage : ContentPage
{
    private readonly EditStationForm _form;

    public EditStationPage(string stationId, StationStore store)
    {
        Id = StationConfig.BandCommandIdPrefix + stationId + ".edit";
        Title = "駅を編集";
        Name = "駅を編集";
        Icon = new IconInfo("");   // Edit
        _form = new EditStationForm(stationId, store);
    }

    public override IContent[] GetContent()
    {
        _form.Reset();
        return [_form];
    }
}

internal sealed partial class EditStationForm(string stationId, StationStore store) : FormContent
{
    public void Reset()
    {
        var s = store.FindStation(stationId);
        TemplateJson = s is null
            ? Cards.Card("閉じる", Cards.Text($"駅が見つかりません: {stationId}"))
            : Cards.Card(
                "保存",
                Cards.Text(Cards.CommentNotice, subtle: true),
                Cards.Text($"ID: {s.Id}", subtle: true),
                Cards.TextInput("name", "表示名", s.Name),
                Cards.TextInput("directionLabel", "方面", s.DirectionLabel),
                Cards.NumberInput("walkMinutes", "家から駅までの徒歩の分数", s.WalkMinutes, 0, 120));
    }

    public override ICommandResult SubmitForm(string inputs)
    {
        var current = store.FindStation(stationId);
        if (current is null)
            return CommandResult.GoBack();

        var form = FormInputs.Parse(inputs);
        if (form.Int("walkMinutes") is not { } walk)
            return FormInputs.Reject("徒歩の分数は整数で入力してください。");

        var edited = current with { Name = form.Text("name"), DirectionLabel = form.Text("directionLabel"), WalkMinutes = walk };
        var doc = store.Current.WithStationReplaced(edited);
        var errors = StationsFile.Validate(doc);
        if (errors.Count > 0)
            return FormInputs.Reject(string.Join(" / ", errors.Select(e => e.Message)));

        if (store.TrySave(doc) is { } saveError)
            return FormInputs.Reject(saveError);
        Log.Info("station edited from form: " + edited.Id);
        return CommandResult.ShowToast(new ToastArgs { Message = "保存しました。", Result = CommandResult.GoBack() });
    }
}
