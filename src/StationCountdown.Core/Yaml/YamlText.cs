using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace StationCountdown.Core.Yaml;

/// <summary>表現モデルを YAML の文字列に書き出す。</summary>
internal static class YamlText
{
    /// <summary>先頭に 1 行のコメントを付け、改行は LF、末尾の文書終端記号（<c>...</c>）は付けない。</summary>
    public static string Write(string headerComment, YamlNode root)
    {
        using var writer = new StringWriter { NewLine = "\n" };
        // spec の例と同じく、リストの「- 」を親のキーより 2 字下げる。
        var emitter = new Emitter(writer, EmitterSettings.Default.WithIndentedSequences());
        new YamlStream(new YamlDocument(root)).Save(emitter, assignAnchors: false);

        var body = writer.ToString().ReplaceLineEndings("\n");
        if (body.EndsWith("...\n", StringComparison.Ordinal))
            body = body[..^"...\n".Length];

        return headerComment + "\n" + body;
    }
}
