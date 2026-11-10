using System.Text.RegularExpressions;
using Celbridge.Logging;
using Celbridge.Projects.Services;
using Tomlyn;
using Tomlyn.Model;

namespace Celbridge.Projects.MigrationSteps;

/// <summary>
/// Renames each .webview file in the project to .browser in the same folder. A file's sidecar moves with the file, and
/// each [[shortcut]] entry naming a moved file takes the new name. A file that cannot move stays in place, and its
/// shortcut goes on naming the file. The step turns source_url into a bookmark that opens on start. It also removes the
/// keys for hiding the toolbars and for bookmark icons. A file with none of those keys, or one that fails to parse,
/// keeps the same contents.
/// </summary>
public sealed class BrowserDocumentMigrationStep : IMigrationStep
{
    private const string OldExtension = ".webview";
    private const string NewExtension = ".browser";
    private const string SidecarExtension = ".cel";

    private const string SourceUrlKey = "source_url";
    private const string ShowUrlBarKey = "show_url_bar";
    private const string ShowBookmarksBarKey = "show_bookmarks_bar";
    private const string BookmarksKey = "bookmarks";
    private const string BookmarkUrlKey = "url";
    private const string BookmarkNameKey = "name";
    private const string BookmarkIconKey = "icon";
    private const string BookmarkOpenOnStartKey = "open_on_start";

    private static readonly Regex TableHeaderRegex = new(@"^\s*\[");
    private static readonly Regex ShortcutHeaderRegex = new(@"^\s*\[\[\s*shortcut\s*\]\]\s*(#.*)?$");

    private static readonly Regex ResourceLineRegex = new(
        @"^(?<before>\s*resource\s*=\s*"")(?<resource>[^""]*)\.webview(?<after>""\s*(#.*)?)$",
        RegexOptions.IgnoreCase);

    public SemanticVersion TargetVersion { get; } = new SemanticVersion(1, 7, 0);

    public async Task<Result> ApplyAsync(MigrationContext context)
    {
        var documentPaths = new List<string>();
        await CollectDocumentsAsync(context, context.ProjectFolderPath, documentPaths);

        foreach (var documentPath in documentPaths)
        {
            await MoveDocumentAsync(context, documentPath);
        }

        return await RenameShortcutsAsync(context);
    }

    // The search skips the .git folder and links.
    private static async Task CollectDocumentsAsync(MigrationContext context, string folderPath, List<string> documentPaths)
    {
        var enumerateResult = await context.FileSystem.EnumerateAsync(folderPath, "*", recursive: false);
        if (enumerateResult.IsFailure)
        {
            context.Logger.LogWarning(enumerateResult, $"Failed to list a folder while looking for {OldExtension} files: '{folderPath}'");
            return;
        }

        foreach (var entry in enumerateResult.Value)
        {
            if (entry.IsLink)
            {
                continue;
            }

            var name = Path.GetFileName(entry.FullPath);
            if (entry.IsFolder)
            {
                if (!string.Equals(name, ProjectConstants.GitFolder, StringComparison.OrdinalIgnoreCase))
                {
                    await CollectDocumentsAsync(context, entry.FullPath, documentPaths);
                }

                continue;
            }

            if (string.Equals(Path.GetExtension(name), OldExtension, StringComparison.OrdinalIgnoreCase))
            {
                documentPaths.Add(entry.FullPath);
            }
        }
    }

    private static async Task MoveDocumentAsync(MigrationContext context, string documentPath)
    {
        var newPath = Path.ChangeExtension(documentPath, NewExtension);

        var infoResult = await context.FileSystem.GetInfoAsync(newPath);
        if (infoResult.IsFailure
            || infoResult.Value.Kind != StorageItemKind.NotFound)
        {
            context.Logger.LogWarning($"Left '{documentPath}' in place because a {NewExtension} file already exists at that path");
            return;
        }

        var moveResult = await context.FileSystem.MoveFileAsync(documentPath, newPath);
        if (moveResult.IsFailure)
        {
            context.Logger.LogWarning(moveResult, $"Failed to move '{documentPath}' to its {NewExtension} name");
            return;
        }

        await MoveSidecarAsync(context, documentPath, newPath);
        await MigrateContentsAsync(context, newPath);

        context.Logger.LogInformation($"Moved '{documentPath}' to its {NewExtension} name");
    }

    private static async Task MoveSidecarAsync(MigrationContext context, string documentPath, string newPath)
    {
        var sidecarPath = documentPath + SidecarExtension;
        var sidecarInfo = await context.FileSystem.GetInfoAsync(sidecarPath);
        if (sidecarInfo.IsFailure
            || sidecarInfo.Value.Kind != StorageItemKind.File)
        {
            return;
        }

        var moveResult = await context.FileSystem.MoveFileAsync(sidecarPath, newPath + SidecarExtension);
        if (moveResult.IsFailure)
        {
            context.Logger.LogWarning(moveResult, $"Failed to move the sidecar '{sidecarPath}' with its document");
        }
    }

    private static async Task MigrateContentsAsync(MigrationContext context, string documentPath)
    {
        var readResult = await context.FileSystem.ReadAllTextAsync(documentPath);
        if (readResult.IsFailure)
        {
            context.Logger.LogWarning(readResult, $"Failed to read '{documentPath}' to update its contents");
            return;
        }

        var text = LineEndingHelper.ConvertLineEndings(readResult.Value, "\n");

        TomlTable document;
        try
        {
            document = TomlSerializer.Deserialize<TomlTable>(text) ?? new TomlTable();
        }
        catch (TomlException exception)
        {
            context.Logger.LogWarning(exception, $"Left the contents of '{documentPath}' unchanged because it failed to parse");
            return;
        }

        var bookmarks = new TomlTableArray();
        if (document.TryGetValue(BookmarksKey, out var bookmarksObject))
        {
            if (bookmarksObject is not TomlTableArray existingBookmarks)
            {
                context.Logger.LogWarning($"Left the contents of '{documentPath}' unchanged because its {BookmarksKey} key is not a list of tables");
                return;
            }

            bookmarks = existingBookmarks;
        }

        if (!HasOldKeys(document, bookmarks))
        {
            return;
        }

        ApplySourceUrl(context, document, bookmarks, documentPath);

        document.Remove(SourceUrlKey);
        document.Remove(ShowUrlBarKey);
        document.Remove(ShowBookmarksBarKey);

        foreach (var bookmark in bookmarks)
        {
            bookmark.Remove(BookmarkIconKey);
        }

        if (bookmarks.Count > 0)
        {
            document[BookmarksKey] = bookmarks;
        }

        var writeResult = await context.FileSystem.WriteAllTextAsync(documentPath, TomlSerializer.Serialize(document));
        if (writeResult.IsFailure)
        {
            context.Logger.LogWarning(writeResult, $"Failed to update the contents of '{documentPath}'");
        }
    }

    private static bool HasOldKeys(TomlTable document, TomlTableArray bookmarks)
    {
        return document.ContainsKey(SourceUrlKey)
            || document.ContainsKey(ShowUrlBarKey)
            || document.ContainsKey(ShowBookmarksBarKey)
            || bookmarks.Any(bookmark => bookmark.ContainsKey(BookmarkIconKey));
    }

    // Turns source_url into a bookmark at the top of the list, with open_on_start on. A bookmark with the same address
    // gets open_on_start instead. The step does not add a bookmark for a blank or non-web value.
    private static void ApplySourceUrl(MigrationContext context, TomlTable document, TomlTableArray bookmarks, string documentPath)
    {
        if (!document.TryGetValue(SourceUrlKey, out var sourceUrlObject)
            || sourceUrlObject is not string sourceUrl
            || string.IsNullOrWhiteSpace(sourceUrl))
        {
            return;
        }

        if (!TryParseAddress(sourceUrl, out var address, out var uri))
        {
            context.Logger.LogWarning($"Dropped the address '{sourceUrl}' from '{documentPath}' because it is not a web address");
            return;
        }

        foreach (var bookmark in bookmarks)
        {
            if (bookmark.TryGetValue(BookmarkUrlKey, out var urlObject)
                && urlObject is string url
                && TryParseAddress(url, out _, out var bookmarkUri)
                && bookmarkUri.AbsoluteUri == uri.AbsoluteUri)
            {
                bookmark[BookmarkOpenOnStartKey] = true;
                return;
            }
        }

        var newBookmark = new TomlTable
        {
            [BookmarkUrlKey] = address,
            [BookmarkNameKey] = uri.Host,
            [BookmarkOpenOnStartKey] = true
        };

        bookmarks.Insert(0, newBookmark);
    }

    // Adds https:// to a value with no scheme.
    private static bool TryParseAddress(string value, out string address, out Uri uri)
    {
        address = value.Trim();
        if (!address.Contains("://", StringComparison.Ordinal))
        {
            address = $"https://{address}";
        }

        if (!Uri.TryCreate(address, UriKind.Absolute, out var parsedUri)
            || (parsedUri.Scheme != Uri.UriSchemeHttp && parsedUri.Scheme != Uri.UriSchemeHttps))
        {
            uri = null!;
            return false;
        }

        uri = parsedUri;
        return true;
    }

    private static async Task<Result> RenameShortcutsAsync(MigrationContext context)
    {
        var readResult = await context.FileSystem.ReadAllTextAsync(context.ProjectFilePath);
        if (readResult.IsFailure)
        {
            return Result.Fail("Failed to read the project file to rename its shortcuts")
                .WithErrors(readResult);
        }

        var text = LineEndingHelper.ConvertLineEndings(readResult.Value, "\n");
        var lines = text.Split('\n');

        var isChanged = false;
        var inShortcut = false;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (TableHeaderRegex.IsMatch(line))
            {
                inShortcut = ShortcutHeaderRegex.IsMatch(line);
                continue;
            }

            if (!inShortcut)
            {
                continue;
            }

            var match = ResourceLineRegex.Match(line);
            if (!match.Success)
            {
                continue;
            }

            var resource = match.Groups["resource"].Value;
            if (!await WasMovedAsync(context, resource))
            {
                continue;
            }

            lines[index] = $"{match.Groups["before"].Value}{resource}{NewExtension}{match.Groups["after"].Value}";
            isChanged = true;
        }

        if (!isChanged)
        {
            return Result.Ok();
        }

        var writeResult = await context.WriteProjectFileAsync(string.Join('\n', lines));
        if (writeResult.IsFailure)
        {
            return Result.Fail("Failed to rename the shortcuts in the project file")
                .WithErrors(writeResult);
        }

        return Result.Ok();
    }

    // Returns true when the step moved the file named by a shortcut's resource, which comes without its extension. The
    // .browser file is then there, and the .webview file is gone. The step never looks at files on another root.
    private static async Task<bool> WasMovedAsync(MigrationContext context, string resource)
    {
        if (!ResourceKey.TryCreate(resource, out var key)
            || key.Root != ResourceKey.DefaultRoot
            || key.IsEmpty)
        {
            return false;
        }

        var basePath = Path.Combine(context.ProjectFolderPath, key.Path.Replace('/', Path.DirectorySeparatorChar));

        var oldInfo = await context.FileSystem.GetInfoAsync(basePath + OldExtension);
        if (oldInfo.IsFailure
            || oldInfo.Value.Kind != StorageItemKind.NotFound)
        {
            return false;
        }

        var newInfo = await context.FileSystem.GetInfoAsync(basePath + NewExtension);

        return newInfo.IsSuccess
            && newInfo.Value.Kind == StorageItemKind.File;
    }
}
