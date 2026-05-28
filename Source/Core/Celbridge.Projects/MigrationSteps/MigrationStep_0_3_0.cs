using System.Text;
using System.Text.Json;
using Celbridge.Projects.Services;

namespace Celbridge.Projects.MigrationSteps;

/// <summary>
/// Migrates projects to v0.3.0. The user-visible change in this version is that
/// the WebView resource adopts the .cel sidecar/standalone form: each pre-v0.3.0
/// "blah.webview" JSON file is converted to "blah.webview.cel" TOML. The .celbridge
/// project file extension and the .toml package and document manifest filenames are
/// deliberately retained. Quoted references to the old WebView extension inside the
/// project config are rewritten so the post-migration project loads cleanly.
/// </summary>
public class MigrationStep_0_3_0 : IMigrationStep
{
    private const string WebViewOldExtension = ".webview";
    private const string WebViewNewExtension = ".webview.cel";

    private const string WebViewJsonSourceUrlProperty = "sourceUrl";
    private const string WebViewTomlSourceUrlKey = "source_url";

    public Version TargetVersion => new Version("0.3.0");

    public async Task<Result> ApplyAsync(MigrationContext context)
    {
        var projectDataFolderPath = Path.GetFullPath(context.ProjectDataFolderPath);

        var webViewConvertResult = await ConvertWebViewFilesAsync(context, projectDataFolderPath);
        if (webViewConvertResult.IsFailure)
        {
            return webViewConvertResult;
        }

        var configRewriteResult = await RewriteProjectConfigAsync(context);
        if (configRewriteResult.IsFailure)
        {
            return configRewriteResult;
        }

        return Result.Ok();
    }

    private async Task<Result> ConvertWebViewFilesAsync(MigrationContext context, string projectDataFolderPath)
    {
        try
        {
            var matches = Directory.EnumerateFiles(
                context.ProjectFolderPath,
                $"*{WebViewOldExtension}",
                SearchOption.AllDirectories);

            int convertedCount = 0;
            foreach (var oldPath in matches)
            {
                var fullOldPath = Path.GetFullPath(oldPath);
                if (IsInsideMetaDataFolder(fullOldPath, projectDataFolderPath))
                {
                    continue;
                }

                // EnumerateFiles uses a Windows-style trailing-wildcard match which also
                // accepts longer extensions. Skip anything that already carries the new
                // suffix so reruns do not double-convert.
                if (fullOldPath.EndsWith(WebViewNewExtension, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var newPath = fullOldPath + ".cel";
                if (File.Exists(newPath))
                {
                    return Result.Fail(
                        $"Cannot convert '{fullOldPath}' to '{newPath}'. Target file already exists.");
                }

                var convertResult = await ConvertWebViewFileAsync(fullOldPath, newPath);
                if (convertResult.IsFailure)
                {
                    return Result.Fail($"Failed to convert WebView file: '{fullOldPath}'")
                        .WithErrors(convertResult);
                }

                File.Delete(fullOldPath);
                convertedCount++;
            }

            if (convertedCount > 0)
            {
                context.Logger.LogInformation(
                    $"Converted {convertedCount} '*{WebViewOldExtension}' file(s) to '*{WebViewNewExtension}'");
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to convert '*{WebViewOldExtension}' files in project folder")
                .WithException(ex);
        }
    }

    private async Task<Result> ConvertWebViewFileAsync(string oldPath, string newPath)
    {
        try
        {
            var originalText = await File.ReadAllTextAsync(oldPath);
            var sourceUrl = ExtractSourceUrlFromJson(originalText);

            var tomlBuilder = new StringBuilder();
            tomlBuilder.Append(WebViewTomlSourceUrlKey);
            tomlBuilder.Append(" = ");
            tomlBuilder.Append(QuoteTomlBasicString(sourceUrl));
            tomlBuilder.Append('\n');

            await File.WriteAllTextAsync(newPath, tomlBuilder.ToString());
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to read or write WebView file during conversion: '{oldPath}'")
                .WithException(ex);
        }
    }

    private static string ExtractSourceUrlFromJson(string jsonText)
    {
        // A pre-0.3.0 .webview file always parsed as a JSON object with a
        // single "sourceUrl" string. Missing or malformed content is treated as
        // an empty URL: the migrated file still loads, just navigates nowhere
        // until the user supplies a URL.
        if (string.IsNullOrWhiteSpace(jsonText))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(jsonText);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            if (!document.RootElement.TryGetProperty(WebViewJsonSourceUrlProperty, out var urlElement))
            {
                return string.Empty;
            }

            if (urlElement.ValueKind != JsonValueKind.String)
            {
                return string.Empty;
            }

            var url = urlElement.GetString();
            return url ?? string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static string QuoteTomlBasicString(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var character in value)
        {
            switch (character)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                default:
                    if (character < 0x20)
                    {
                        builder.Append($"\\u{(int)character:X4}");
                    }
                    else
                    {
                        builder.Append(character);
                    }
                    break;
            }
        }
        builder.Append('"');
        return builder.ToString();
    }

    private async Task<Result> RewriteProjectConfigAsync(MigrationContext context)
    {
        try
        {
            var originalText = await File.ReadAllTextAsync(context.ProjectFilePath);

            // Rewrites are scoped to quoted occurrences so bare prose mentions of
            // the old extension in comments stay untouched.
            var updatedText = RewriteQuotedExtensions(originalText, WebViewOldExtension, WebViewNewExtension);

            if (updatedText == originalText)
            {
                return Result.Ok();
            }

            var writeResult = await context.WriteProjectFileAsync(updatedText);
            if (writeResult.IsFailure)
            {
                return Result.Fail($"Failed to write rewritten project config: '{context.ProjectFilePath}'")
                    .WithErrors(writeResult);
            }

            context.Logger.LogInformation(
                $"Rewrote renamed-resource references in project config: '{context.ProjectFilePath}'");

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail("Failed to rewrite project config")
                .WithException(ex);
        }
    }

    private static string RewriteQuotedExtensions(string text, string oldExtension, string newExtension)
    {
        return text
            .Replace($"{oldExtension}\"", $"{newExtension}\"")
            .Replace($"{oldExtension}'", $"{newExtension}'");
    }

    private static bool IsInsideMetaDataFolder(string fullPath, string projectDataFolderPath)
    {
        if (string.IsNullOrEmpty(projectDataFolderPath))
        {
            return false;
        }

        return fullPath.StartsWith(projectDataFolderPath, StringComparison.OrdinalIgnoreCase);
    }
}
