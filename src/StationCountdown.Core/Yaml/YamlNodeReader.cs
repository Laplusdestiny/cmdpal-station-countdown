using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace StationCountdown.Core.Yaml;

/// <summary>
/// YamlDotNet の表現モデル（<see cref="YamlStream"/>）をたどりながら値を取り出し、
/// 誤りを行番号付きの <see cref="ConfigError"/> として集める。リフレクションは使わない。
/// </summary>
internal sealed class YamlNodeReader
{
    private readonly List<ConfigError> _errors = [];

    public IReadOnlyList<ConfigError> Errors => _errors;

    public bool HasErrors => _errors.Count > 0;

    public void Error(YamlNode? node, string message) => _errors.Add(new ConfigError(LineOf(node), message));

    public static int LineOf(YamlNode? node) => node is null ? 0 : ToLine(node.Start);

    /// <summary>
    /// 文字列を読み、ルートのマッピングを返す。空のファイル、構文エラー、ルートがマッピングでない場合は
    /// エラーを記録して null を返す（例外は投げない）。
    /// </summary>
    public YamlMappingNode? LoadRootMapping(string text)
    {
        // メモ帳などが付ける BOM を取り除く。改行コード（CRLF）は YamlDotNet がそのまま扱える。
        if (text.Length > 0 && text[0] == '﻿')
            text = text[1..];

        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(text));
        }
        catch (YamlException ex)
        {
            var message = ex.InnerException is { } inner ? $"{ex.Message} {inner.Message}" : ex.Message;
            _errors.Add(new ConfigError(ToLine(ex.Start), $"YAML として読めません: {message}"));
            return null;
        }

        if (stream.Documents.Count == 0)
        {
            _errors.Add(new ConfigError(1, "ファイルが空です。"));
            return null;
        }

        if (stream.Documents.Count > 1)
            Error(stream.Documents[1].RootNode, "ドキュメントは 1 つだけにしてください（--- で区切らない）。");

        if (stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            Error(stream.Documents[0].RootNode, "ファイルの最上位は「キー: 値」の形にしてください。");
            return null;
        }

        return root;
    }

    /// <summary><paramref name="known"/> にないキーをエラーにする（書き間違いの検出）。</summary>
    public void RejectUnknownKeys(YamlMappingNode map, params ReadOnlySpan<string> known)
    {
        foreach (var key in map.Children.Keys)
        {
            var name = (key as YamlScalarNode)?.Value;
            if (name is null || !Contains(known, name))
                Error(key, $"不明なキーです: {name ?? key.ToString()}");
        }
    }

    public YamlNode? GetNode(YamlMappingNode map, string key, bool required)
    {
        if (map.Children.TryGetValue(new YamlScalarNode(key), out var node))
            return node;

        if (required)
            Error(map, $"{key} がありません。");
        return null;
    }

    public YamlScalarNode? GetScalarNode(YamlMappingNode map, string key, bool required)
    {
        var node = GetNode(map, key, required);
        if (node is null)
            return null;
        if (node is YamlScalarNode scalar && scalar.Value is not null)
            return scalar;

        Error(node, $"{key} には値を 1 つ書いてください。");
        return null;
    }

    public string? GetScalar(YamlMappingNode map, string key, bool required)
        => GetScalarNode(map, key, required)?.Value;

    /// <summary>空文字を許さない文字列。</summary>
    public string? GetNonEmptyString(YamlMappingNode map, string key, bool required)
    {
        var node = GetScalarNode(map, key, required);
        if (node is null)
            return null;
        if (string.IsNullOrWhiteSpace(node.Value))
        {
            Error(node, $"{key} が空です。");
            return null;
        }
        return node.Value;
    }

    public int? GetInt(YamlMappingNode map, string key, bool required, int min, int max)
    {
        var node = GetScalarNode(map, key, required);
        if (node is null)
            return null;
        if (!int.TryParse(node.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            || value < min || value > max)
        {
            Error(node, $"{key} は {min}〜{max} の整数で書いてください: {node.Value}");
            return null;
        }
        return value;
    }

    public YamlSequenceNode? GetSequence(YamlMappingNode map, string key, bool required)
    {
        var node = GetNode(map, key, required);
        if (node is null)
            return null;
        if (node is YamlSequenceNode seq)
            return seq;

        Error(node, $"{key} は「- 」で始まるリストで書いてください。");
        return null;
    }

    public YamlMappingNode? GetMapping(YamlMappingNode map, string key, bool required)
    {
        var node = GetNode(map, key, required);
        if (node is null)
            return null;
        if (node is YamlMappingNode child)
            return child;

        Error(node, $"{key} は「キー: 値」の形で書いてください。");
        return null;
    }

    /// <summary>リストの要素を文字列として読む。文字列でない要素はエラーにして飛ばす。</summary>
    public IEnumerable<YamlScalarNode> ScalarItems(YamlSequenceNode seq, string key)
    {
        foreach (var item in seq.Children)
        {
            if (item is YamlScalarNode { Value: not null } scalar)
                yield return scalar;
            else
                Error(item, $"{key} の要素は文字列で書いてください。");
        }
    }

    private static int ToLine(Mark mark) => (int)Math.Clamp(mark.Line, 0, int.MaxValue);

    private static bool Contains(ReadOnlySpan<string> values, string value)
    {
        foreach (var v in values)
        {
            if (v == value)
                return true;
        }
        return false;
    }
}
