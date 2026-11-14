using System.Globalization;
using System.Text.Json;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace StationCountdown.Extension.Pages.Forms;

/// <summary>フォームから送られてきた入力（JSON）を読む。値は文字列でも数値でも受け付ける。</summary>
internal sealed class FormInputs
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    private FormInputs()
    {
    }

    public static FormInputs Parse(string json)
    {
        var inputs = new FormInputs();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            return inputs;

        foreach (var p in doc.RootElement.EnumerateObject())
        {
            inputs._values[p.Name] = p.Value.ValueKind switch
            {
                JsonValueKind.String => p.Value.GetString() ?? "",
                JsonValueKind.Null or JsonValueKind.Undefined => "",
                _ => p.Value.GetRawText(),
            };
        }
        return inputs;
    }

    public string Text(string id) => _values.GetValueOrDefault(id, "").Trim();

    /// <summary>整数として読めなければ null（範囲の検証は StationsFile.Validate に任せる）。</summary>
    public int? Int(string id)
        => double.TryParse(Text(id), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
           && d == Math.Floor(d) && d is >= int.MinValue and <= int.MaxValue
            ? (int)d
            : null;

    /// <summary>入力の誤りを知らせ、フォームを開いたままにする。</summary>
    public static ICommandResult Reject(string message)
        => CommandResult.ShowToast(new ToastArgs { Message = message, Result = CommandResult.KeepOpen() });
}
