using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using StationCountdown.Core.Config;
using StationCountdown.Core.Model;
using StationCountdown.Core.Timetables;
using StationCountdown.Extension.Services;

namespace StationCountdown.Extension.Pages.Forms;

/// <summary>「駅を編集」（spec §6.2）。表示名・方面・徒歩の分数と、表示する種別（#13）を変える。</summary>
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
    // 表示中のフォームに種別の欄があるか。何も選ばないと値が送られてこない場合もあるので、送られてきた入力では判断しない。
    private bool _trainTypesShown;

    public void Reset()
    {
        _trainTypesShown = false;
        var s = store.FindStation(stationId);
        TemplateJson = s is null
            ? Cards.Card("閉じる", Cards.Text($"駅が見つかりません: {stationId}"))
            : Cards.Card(
                "保存",
                [
                    Cards.Text(Cards.CommentNotice, subtle: true),
                    Cards.Text($"ID: {s.Id}", subtle: true),
                    Cards.TextInput("name", "表示名", s.Name),
                    Cards.TextInput("directionLabel", "方面", s.DirectionLabel),
                    Cards.NumberInput("walkMinutes", "家から駅までの徒歩の分数", s.WalkMinutes, 0, 120),
                    .. TrainTypeInputs(s),
                ]);
    }

    /// <summary>表示する種別の欄。時刻表がまだなければ案内だけ出し、欄は置かない（保存しても今の設定は変わらない）。</summary>
    private IEnumerable<string> TrainTypeInputs(StationConfig s)
    {
        if (store.GetTimetable(s.Id) is not { } timetable)
            return [Cards.Text("時刻表を取得すると、表示する種別を選べます。", subtle: true)];

        _trainTypesShown = true;
        var choices = TrainTypes.Choices(timetable, s.TrainTypes)
            .Select(c => (Title: c.InTimetable ? c.Value : $"{c.Value}（時刻表になし）", c.Value));
        return
        [
            Cards.Text("何も選ばないと、すべての種別を表示します。", subtle: true),
            Cards.MultiChoiceSet("trainTypes", "表示する種別", choices, s.TrainTypes),
        ];
    }

    public override ICommandResult SubmitForm(string inputs)
    {
        var current = store.FindStation(stationId);
        if (current is null)
            return CommandResult.GoBack();

        var form = FormInputs.Parse(inputs);
        if (form.Int("walkMinutes") is not { } walk)
            return FormInputs.Reject("徒歩の分数は整数で入力してください。");

        var edited = current with
        {
            Name = form.Text("name"),
            DirectionLabel = form.Text("directionLabel"),
            WalkMinutes = walk,
            TrainTypes = TrainTypes.ApplySelection(current.TrainTypes, _trainTypesShown ? form.Text("trainTypes") : null),
        };
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
