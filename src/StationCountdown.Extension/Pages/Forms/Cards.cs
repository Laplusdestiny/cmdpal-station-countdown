using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace StationCountdown.Extension.Pages.Forms;

/// <summary>
/// フォームの Adaptive Card（1.5）の JSON を組み立てる。値は必ず <see cref="Str"/> でエスケープしてから埋め込む。
/// リフレクションを使うシリアライザーは使わない（AOT・トリミングのため）。
/// </summary>
internal static class Cards
{
    public const string CommentNotice = "保存すると stations.yaml 内のコメントは消えます（直前の内容は stations.yaml.bak に保存されます）。";

    private static readonly JavaScriptEncoder Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    /// <summary>JSON の文字列リテラル（前後の引用符を含む）。</summary>
    public static string Str(string value) => "\"" + JsonEncodedText.Encode(value, Encoder).ToString() + "\"";

    public static string Card(string submitTitle, params IEnumerable<string> body)
        => CardWithActions([Submit(submitTitle)], body);

    public static string CardWithActions(IEnumerable<string> actions, IEnumerable<string> body) => $$"""
        {
          "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
          "type": "AdaptiveCard",
          "version": "1.5",
          "body": [{{string.Join(",", body)}}],
          "actions": [{{string.Join(",", actions)}}]
        }
        """;

    /// <summary>送信ボタン。<paramref name="action"/> を付けると、SubmitForm の data に <c>{"action": …}</c> が届く。</summary>
    public static string Submit(string title, string? action = null, bool validate = true)
        => action is null
            ? $$"""{ "type": "Action.Submit", "title": {{Str(title)}} }"""
            : $$"""{ "type": "Action.Submit", "title": {{Str(title)}}, "data": { "action": {{Str(action)}} }, "associatedInputs": {{Str(validate ? "auto" : "none")}} }""";

    /// <summary>SubmitForm の data から、<see cref="Submit"/> で付けた action を読む。</summary>
    public static string? ActionOf(string? data)
    {
        if (string.IsNullOrWhiteSpace(data))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(data);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("action", out var a) && a.ValueKind == JsonValueKind.String
                ? a.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string Text(string text, bool subtle = false)
        => $$"""{ "type": "TextBlock", "text": {{Str(text)}}, "wrap": true, "isSubtle": {{(subtle ? "true" : "false")}} }""";

    public static string TextInput(string id, string label, string value, string? placeholder = null)
        => $$"""
            { "type": "Input.Text", "id": {{Str(id)}}, "label": {{Str(label)}}, "value": {{Str(value)}},
              "placeholder": {{Str(placeholder ?? "")}}, "isRequired": true, "errorMessage": {{Str(label + "を入力してください")}} }
            """;

    public static string NumberInput(string id, string label, int value, int min, int max)
        => string.Create(CultureInfo.InvariantCulture, $$"""
            { "type": "Input.Number", "id": {{Str(id)}}, "label": {{Str(label)}}, "value": {{value}}, "min": {{min}}, "max": {{max}},
              "isRequired": true, "errorMessage": {{Str($"{min}〜{max} の整数を入力してください")}} }
            """);

    public static string ChoiceSet(string id, string label, IEnumerable<(string Title, string Value)> choices, string value)
        => $$"""
            { "type": "Input.ChoiceSet", "id": {{Str(id)}}, "label": {{Str(label)}}, "style": "expanded", "isRequired": true,
              "value": {{Str(value)}},
              "choices": [{{string.Join(",", choices.Select(c => $$"""{ "title": {{Str(c.Title)}}, "value": {{Str(c.Value)}} }"""))}}] }
            """;
}
