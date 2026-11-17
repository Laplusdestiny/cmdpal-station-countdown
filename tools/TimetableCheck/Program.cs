// 時刻表ファイルの検証と、AI 用のプロンプトの出力（docs/ai-timetable-prompt.md）。
// 終了コード: 0 = 問題なし、1 = エラー（読み込めない）、2 = 警告だけ、64 = 引数の誤り。
using System.Text;
using StationCountdown.Core.Timetables;

var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
Console.OutputEncoding = utf8;

return args switch
{
    ["check", var path] => Check(path, StationIdFromPath(path)),
    ["check", var path, "--id", var id] => Check(path, id),
    ["prompt"] => Prompt(null),
    ["prompt", "--out", var outPath] => Prompt(outPath),
    _ => Usage(),
};

int Check(string path, string stationId)
{
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"ファイルがありません: {path}");
        return TimetableCheckReport.HasErrors;
    }

    var report = TimetableCheckReport.Create(File.ReadAllText(path), stationId);
    Console.Write(report.Text);
    return report.ExitCode;
}

int Prompt(string? outPath)
{
    var prompt = AiTimetablePrompt.Build(null);
    if (outPath is null)
        Console.Write(prompt);
    else
        File.WriteAllText(outPath, prompt, utf8);   // リダイレクトだとシェルによって文字コードが変わるので、直接書く
    return 0;
}

// 時刻表ファイルの名前は {ID}.yaml なので、ファイル名から ID を決める。
static string StationIdFromPath(string path) => Path.GetFileNameWithoutExtension(path);

static int Usage()
{
    Console.Error.WriteLine("""
        使い方:
          TimetableCheck check <時刻表ファイル> [--id <駅の ID>]   ID を省くと、ファイル名（.yaml を除く）を ID とする
          TimetableCheck prompt [--out <ファイル>]                 AI 用のプロンプト（駅を指定しない版）を出力する
        終了コード: 0 = 問題なし、1 = エラー、2 = 警告だけ、64 = 引数の誤り
        """);
    return 64;
}
