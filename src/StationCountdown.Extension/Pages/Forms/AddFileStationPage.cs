using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using StationCountdown.Core.Config;
using StationCountdown.Core.Model;
using StationCountdown.Extension.Services;

namespace StationCountdown.Extension.Pages.Forms;

/// <summary>「＋ ファイルで駅を追加」（spec §6.2）。保存すると、時刻表ファイルの置き場所を知らせてフォルダを開く。</summary>
internal sealed partial class AddFileStationPage : ContentPage
{
    private readonly AddFileStationForm _form;

    public AddFileStationPage(StationStore store)
    {
        Id = "com.laplusdestiny.stationcountdown.add-file";
        Title = "ファイルで駅を追加";
        Name = "追加";
        Icon = new IconInfo("");   // Add
        _form = new AddFileStationForm(store);
    }

    public override IContent[] GetContent()
    {
        _form.Reset();
        return [_form];
    }
}

internal sealed partial class AddFileStationForm(StationStore store) : FormContent
{
    public void Reset() => TemplateJson = Cards.Card(
        "追加して時刻表のフォルダを開く",
        Cards.Text(Cards.CommentNotice, subtle: true),
        Cards.Text("時刻表は、利用者が用意する YAML ファイル（timetables\\{ID}.yaml）から読みます。追加すると、書き方の例を置いたフォルダを開きます。", subtle: true),
        Cards.TextInput("id", "ID（半角英小文字・数字と -）", StationIds.Next("file", store.Current.Stations.Select(s => s.Id)), "tokyu-miyamaedaira-shibuya"),
        Cards.TextInput("name", "表示名", "", "宮前平"),
        Cards.TextInput("directionLabel", "方面", "", "渋谷方面"),
        Cards.NumberInput("walkMinutes", "家から駅までの徒歩の分数", 5, 0, 120));

    public override ICommandResult SubmitForm(string inputs)
    {
        var form = FormInputs.Parse(inputs);
        if (form.Int("walkMinutes") is not { } walk)
            return FormInputs.Reject("徒歩の分数は整数で入力してください。");

        var station = new StationConfig(form.Text("id"), form.Text("name"), form.Text("directionLabel"), walk,
            new StationSource(SourceType.File));
        var doc = store.Current.WithStationAdded(station);
        var errors = StationsFile.Validate(doc);
        if (errors.Count > 0)
            return FormInputs.Reject(string.Join(" / ", errors.Select(e => e.Message)));

        store.Save(doc);
        WriteExample(station);
        ShellHelpers.OpenInShell(AppPaths.TimetablesDir);
        Log.Info("station added from form: " + station.Id);

        return CommandResult.ShowToast(new ToastArgs
        {
            Message = $"時刻表ファイルを {AppPaths.TimetablePath(station.Id)} に置いてください（例: {Path.GetFileName(ExamplePath(station))}）",
            Result = CommandResult.GoHome(),
        });
    }

    private static string ExamplePath(StationConfig station) => AppPaths.TimetablePath(station.Id) + ".example";

    /// <summary>書き方の例。ファイル名が .example で終わるので、名前を変えるまでは読み込まれない。</summary>
    private static void WriteExample(StationConfig station)
    {
        var path = ExamplePath(station);
        if (File.Exists(path) || File.Exists(AppPaths.TimetablePath(station.Id)))
            return;

        File.WriteAllText(path, $"""
            # yaml-language-server: $schema=../schemas/timetable.schema.json
            # {station.Name} {station.DirectionLabel} の時刻表の書き方の例。
            # 中身を実際の時刻に書き換えて、ファイル名を {station.Id}.yaml に変えると読み込まれます。
            # - 1 本を 1 行で「HH:MM 種別 行先」と書く（半角スペースで区切る。全角スペースやタブは使えない）
            # - 0 時台以降の終電は 24:10 のように 24 時以降で書く（29:59 まで）
            # - ダイヤは weekday / saturday / holiday / saturdayHoliday（土曜・休日）のうち、必要なものだけ書く
            schemaVersion: 1
            stationId: {station.Id}
            services:
              weekday:
                - 05:00 各停 行先
                - 24:10 各停 行先
              saturdayHoliday:
                - 05:30 各停 行先

            """.ReplaceLineEndings("\n"));
    }
}
