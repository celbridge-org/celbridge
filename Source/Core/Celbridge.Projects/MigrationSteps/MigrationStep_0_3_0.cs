using System.Text;
using System.Text.Json;
using Celbridge.Projects.Services;

namespace Celbridge.Projects.MigrationSteps;

/// <summary>
/// Migrates projects to v0.3.0. Celbridge's package and document file formats
/// consolidate onto the .cel extension as the final phase of the resources
/// redesign. This step renames each package.toml to package.cel, each *.document.toml
/// to *.document.cel, and each *.webview file to *.webview.cel (converting the
/// JSON body to TOML at the same time). The .celbridge project file extension is
/// deliberately retained and is not renamed by this step. Internal references in
/// the project config and the renamed package.cel manifests are rewritten so the
/// post-migration project loads cleanly.
/// </summary>
public class MigrationStep_0_3_0 : IMigrationStep
{
    private const string PackageManifestOldName = "package.toml";
    private const string PackageManifestNewName = "package.cel";

    private const string DocumentManifestOldExtension = ".document.toml";
    private const string DocumentManifestNewExtension = ".document.cel";

    private const string WebViewOldExtension = ".webview";
    private const string WebViewNewExtension = ".webview.cel";

    private const string WebViewJsonSourceUrlProperty = "sourceUrl";
    private const string WebViewTomlSourceUrlKey = "source_url";

    public Version TargetVersion => new Version("0.3.0");

    public async Task<Result> ApplyAsync(MigrationContext context)
    {
        var projectDataFolderPath = Path.GetFullPath(context.ProjectDataFolderPath);

        var packageRenameResult = RenamePackageManifests(context, projectDataFolderPath);
        if (packageRenameResult.IsFailure)
        {
            return packageRenameResult;
        }

        var documentRenameResult = RenameDocumentManifests(context, projectDataFolderPath);
        if (documentRenameResult.IsFailure)
        {
            return documentRenameResult;
        }

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

    private Result RenamePackageManifests(MigrationContext context, string projectDataFolderPath)
    {
        try
        {
            var matches = Directory.EnumerateFiles(
                context.ProjectFolderPath,
                PackageManifestOldName,
                SearchOption.AllDirectories);

            int renamedCount = 0;
            foreach (var oldPath in matches)
            {
                var fullOldPath = Path.GetFullPath(oldPath);
                if (IsInsideMetaDataFolder(fullOldPath, projectDataFolderPath))
                {
                    continue;
                }

                var newPath = Path.Combine(
                    Path.GetDirectoryName(fullOldPath)!,
                    PackageManifestNewName);

                if (File.Exists(newPath))
                {
                    return Result.Fail(
                        $"Cannot rename '{fullOldPath}' to '{newPath}'. Target file already exists.");
                }

                File.Move(fullOldPath, newPath);
                renamedCount++;
            }

            if (renamedCount > 0)
            {
                context.Logger.LogInformation(
                    $"Renamed {renamedCount} '{PackageManifestOldName}' file(s) to '{PackageManifestNewName}'");
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to rename '{PackageManifestOldName}' files in project folder")
                .WithException(ex);
        }
    }

    private Result RenameDocumentManifests(MigrationContext context, string projectDataFolderPath)
    {
        try
        {
            // Two passes: rename the files first, then rewrite each surviving
            // package.cel so the document_editors array points at the renamed
            // *.document.cel files. The package.cel rewrite is best-effort: we only
            // touch quoted entries that end with the old extension, leaving any
            // bespoke prose paths alone.

            var renamedFiles = new List<(string OldPath, string NewPath)>();
            var matches = Directory.EnumerateFiles(
                context.ProjectFolderPath,
                $"*{DocumentManifestOldExtension}",
                SearchOption.AllDirectories);

            foreach (var oldPath in matches)
            {
                var fullOldPath = Path.GetFullPath(oldPath);
                if (IsInsideMetaDataFolder(fullOldPath, projectDataFolderPath))
                {
                    continue;
                }

                var fileName = Path.GetFileName(fullOldPath);
                var stem = fileName.Substring(0, fileName.Length - DocumentManifestOldExtension.Length);
                var newPath = Path.Combine(
                    Path.GetDirectoryName(fullOldPath)!,
                    stem + DocumentManifestNewExtension);

                if (File.Exists(newPath))
                {
                    return Result.Fail(
                        $"Cannot rename '{fullOldPath}' to '{newPath}'. Target file already exists.");
                }

                File.Move(fullOldPath, newPath);
                renamedFiles.Add((fullOldPath, newPath));
            }

            if (renamedFiles.Count > 0)
            {
                context.Logger.LogInformation(
                    $"Renamed {renamedFiles.Count} '*{DocumentManifestOldExtension}' file(s) to '*{DocumentManifestNewExtension}'");
            }

            var packageManifestRewriteResult = RewritePackageManifestReferences(context, projectDataFolderPath);
            if (packageManifestRewriteResult.IsFailure)
            {
                return packageManifestRewriteResult;
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to rename '*{DocumentManifestOldExtension}' files in project folder")
                .WithException(ex);
        }
    }

    private Result RewritePackageManifestReferences(MigrationContext context, string projectDataFolderPath)
    {
        var packageManifests = Directory.EnumerateFiles(
            context.ProjectFolderPath,
            PackageManifestNewName,
            SearchOption.AllDirectories);

        foreach (var packageManifestPath in packageManifests)
        {
            var fullPath = Path.GetFullPath(packageManifestPath);
            if (IsInsideMetaDataFolder(fullPath, projectDataFolderPath))
            {
                continue;
            }

            try
            {
                var originalText = File.ReadAllText(fullPath);
                var rewrittenText = RewriteQuotedExtensions(
                    originalText,
                    DocumentManifestOldExtension,
                    DocumentManifestNewExtension);

                if (rewrittenText != originalText)
                {
                    File.WriteAllText(fullPath, rewrittenText);
                    context.Logger.LogInformation(
                        $"Rewrote '*{DocumentManifestOldExtension}' references in package manifest: '{fullPath}'");
                }
            }
            catch (Exception ex)
            {
                return Result.Fail($"Failed to rewrite references in package manifest: '{fullPath}'")
                    .WithException(ex);
            }
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
            // these extensions in comments stay untouched. Order matters: rewrite
            // the longer/more-specific extension first so .webview does not eat
            // .document.toml or vice versa.
            var updatedText = originalText;
            updatedText = RewriteQuotedExtensions(updatedText, DocumentManifestOldExtension, DocumentManifestNewExtension);
            updatedText = RewriteQuotedExtensions(updatedText, WebViewOldExtension, WebViewNewExtension);
            updatedText = RewriteQuotedFilenames(updatedText, PackageManifestOldName, PackageManifestNewName);

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

    private static string RewriteQuotedFilenames(string text, string oldFilename, string newFilename)
    {
        return text
            .Replace($"/{oldFilename}\"", $"/{newFilename}\"")
            .Replace($"/{oldFilename}'", $"/{newFilename}'")
            .Replace($"\\{oldFilename}\"", $"\\{newFilename}\"")
            .Replace($"\\{oldFilename}'", $"\\{newFilename}'");
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
