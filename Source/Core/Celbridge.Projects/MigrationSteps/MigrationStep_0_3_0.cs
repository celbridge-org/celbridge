using System.Text;
using System.Text.Json;
using Celbridge.Projects.Services;
using Celbridge.Resources;

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
        var enumerateResult = await context.FileSystem.EnumerateAsync(
            context.ProjectFolderPath,
            $"*{WebViewOldExtension}",
            recursive: true);

        if (enumerateResult.IsFailure)
        {
            return Result.Fail($"Failed to enumerate '*{WebViewOldExtension}' files in project folder")
                .WithErrors(enumerateResult);
        }

        int convertedCount = 0;
        foreach (var oldPath in enumerateResult.Value.Where(entry => !entry.IsFolder).Select(entry => entry.FullPath))
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
            var convertResult = await ConvertWebViewFileAsync(context, fullOldPath, newPath);
            if (convertResult.IsFailure)
            {
                return Result.Fail($"Failed to convert WebView file: '{fullOldPath}'")
                    .WithErrors(convertResult);
            }

            var deleteResult = await context.FileSystem.DeleteFileAsync(fullOldPath);
            if (deleteResult.IsFailure)
            {
                return Result.Fail($"Failed to delete legacy WebView file: '{fullOldPath}'")
                    .WithErrors(deleteResult);
            }

            convertedCount++;
        }

        if (convertedCount > 0)
        {
            context.Logger.LogInformation(
                $"Converted {convertedCount} '*{WebViewOldExtension}' file(s) to '*{WebViewNewExtension}'");
        }

        return Result.Ok();
    }

    private async Task<Result> ConvertWebViewFileAsync(MigrationContext context, string oldPath, string newPath)
    {
        var readResult = await context.FileSystem.ReadAllTextAsync(oldPath);
        if (readResult.IsFailure)
        {
            return Result.Fail($"Failed to read WebView file during conversion: '{oldPath}'")
                .WithErrors(readResult);
        }

        var sourceUrl = ExtractSourceUrlFromJson(readResult.Value);
        var tomlText = BuildWebViewTomlContent(sourceUrl);

        var writeResult = await context.FileSystem.WriteAllTextAsync(newPath, tomlText);
        if (writeResult.IsFailure)
        {
            return Result.Fail($"Failed to write WebView file during conversion: '{newPath}'")
                .WithErrors(writeResult);
        }

        return Result.Ok();
    }

    private static string BuildWebViewTomlContent(string sourceUrl)
    {
        var tomlBuilder = new StringBuilder();
        tomlBuilder.Append(WebViewTomlSourceUrlKey);
        tomlBuilder.Append(" = ");
        tomlBuilder.Append(QuoteTomlBasicString(sourceUrl));
        tomlBuilder.Append('\n');
        return tomlBuilder.ToString();
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
        var readResult = await context.FileSystem.ReadAllTextAsync(context.ProjectFilePath);
        if (readResult.IsFailure)
        {
            return Result.Fail($"Failed to read project config: '{context.ProjectFilePath}'")
                .WithErrors(readResult);
        }

        var originalText = readResult.Value;

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
