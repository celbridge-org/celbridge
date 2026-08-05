using System.Globalization;
using System.Text;
using Celbridge.Utilities;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Parsing;

namespace Celbridge.WebHost;

/// <summary>
/// Storage shape for a .webview file: a TOML document carrying the Home URL the
/// embedded WebView opens and how the document presents its browser chrome.
/// </summary>
public sealed record WebViewFileContent(string SourceUrl, bool ShowUrlBar = true)
{
    private const string SourceUrlKey = "source_url";
    private const string ShowUrlBarKey = "show_url_bar";

    /// <summary>
    /// Parses the TOML body of a .webview file. An empty file or a missing key
    /// yields the default value rather than a failure, so a brand-new file (or a
    /// hand-edited blank file) still loads. Unrecognised keys are ignored.
    /// </summary>
    public static Result<WebViewFileContent> TryParse(string toml)
    {
        if (string.IsNullOrWhiteSpace(toml))
        {
            return new WebViewFileContent(string.Empty);
        }

        // Tomlyn rejects bare-\r line terminators, so normalize before parsing.
        var text = LineEndingHelper.ConvertLineEndings(toml, "\n");

        var parse = SyntaxParser.Parse(text);
        if (parse.HasErrors)
        {
            var errors = string.Join("; ", parse.Diagnostics.Select(d => d.ToString()));
            return Result.Fail($"Invalid TOML: {errors}");
        }

        var root = TomlSerializer.Deserialize<TomlTable>(text);
        if (root is null)
        {
            return Result.Fail("Invalid TOML: failed to deserialize.");
        }

        var sourceUrl = string.Empty;
        if (root.TryGetValue(SourceUrlKey, out var sourceUrlValue))
        {
            if (sourceUrlValue is not string sourceUrlText)
            {
                return Result.Fail($"Key '{SourceUrlKey}' must be a string.");
            }
            sourceUrl = sourceUrlText;
        }

        var showUrlBar = true;
        if (root.TryGetValue(ShowUrlBarKey, out var showUrlBarValue))
        {
            if (showUrlBarValue is not bool showUrlBarFlag)
            {
                return Result.Fail($"Key '{ShowUrlBarKey}' must be a boolean.");
            }
            showUrlBar = showUrlBarFlag;
        }

        return new WebViewFileContent(sourceUrl, showUrlBar);
    }

    /// <summary>
    /// Serialises this content as the canonical .webview TOML document.
    /// Trailing newline matches the convention used by the other text-storage
    /// roundtrips.
    /// </summary>
    public string ToToml()
    {
        var builder = new StringBuilder();
        builder.Append(SourceUrlKey);
        builder.Append(" = ");
        builder.Append(EncodeTomlString(SourceUrl));
        builder.Append('\n');

        builder.Append(ShowUrlBarKey);
        builder.Append(" = ");
        builder.Append(ShowUrlBar ? "true" : "false");
        builder.Append('\n');

        return builder.ToString();
    }

    // Encodes a value as a TOML basic string, escaping the few characters a URL
    // can legally carry that a basic string cannot hold verbatim.
    private static string EncodeTomlString(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var character in value)
        {
            if (character == '\\' ||
                character == '"')
            {
                builder.Append('\\');
                builder.Append(character);
            }
            else if (character < 0x20 ||
                     character == 0x7F)
            {
                builder.AppendFormat(CultureInfo.InvariantCulture, "\\u{0:X4}", (int)character);
            }
            else
            {
                builder.Append(character);
            }
        }
        builder.Append('"');

        return builder.ToString();
    }
}
