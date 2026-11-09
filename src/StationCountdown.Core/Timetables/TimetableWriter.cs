using System.Text;
using StationCountdown.Core.Model;

namespace StationCountdown.Core.Timetables;

/// <summary>時刻表ファイルを、途中までの内容が残らないように書き込む（spec §7.3）。</summary>
public static class TimetableWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// <c>{path}.tmp</c> に書いてから <paramref name="path"/> に置き換える。書き込みに失敗しても、元のファイルはそのまま残る。
    /// 同じボリューム内の置き換えなので、ファイルを監視している側が途中までの内容を読むこともない。
    /// </summary>
    public static void WriteAtomic(string path, Timetable timetable)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, TimetableFile.Serialize(timetable), Utf8NoBom);
        File.Move(tmp, path, overwrite: true);
    }
}
