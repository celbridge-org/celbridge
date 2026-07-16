using System.IO.Compression;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Tomlyn;
using Tomlyn.Model;
using MemoryStream = System.IO.MemoryStream;
using Path = System.IO.Path;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by page_publish with the served path and URL the workshop
/// assigned, and the size of the uploaded bundle.
/// </summary>
public record class PagePublishResult(string Path, string Url, int Entries, long Size);

public partial class PageTools
{
    /// <summary>Publish a folder of static web content to the workshop as a page (default pages/).</summary>
    [McpServerTool(Name = "page_publish", Destructive = true)]
    [ToolAlias("page.publish")]
    [RelatedGuides("pages_overview", "resource_keys", "silent_vs_interactive")]
    public async partial Task<CallToolResult> Publish(string resource = "", bool confirmWithUser = true)
    {
        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        if (!workspaceWrapper.IsWorkspacePageLoaded)
        {
            return ToolResponse.Error("No project is loaded. Open a project before publishing a page.");
        }

        // Every published page records its publisher, so a non-empty Author must
        // be configured before any upload work begins.
        var authorResult = await ResolvePublishAuthorAsync(confirmWithUser);
        if (authorResult.IsFailure)
        {
            return ToolResponse.Error(authorResult);
        }
        var author = authorResult.Value;

        ResourceKey resourceKey;
        if (string.IsNullOrWhiteSpace(resource))
        {
            resourceKey = new ResourceKey(PageConstants.DefaultPagesFolder);
        }
        else if (!ResourceKey.TryCreate(resource, out resourceKey))
        {
            return ToolResponse.InvalidResourceKey(resource);
        }

        var resourceService = workspaceWrapper.WorkspaceService.ResourceService;
        var resourceRegistry = resourceService.Registry;
        var fileSystem = GetRequiredService<ILocalFileSystem>();

        var resolveResult = resourceRegistry.ResolveResourcePath(resourceKey);
        if (resolveResult.IsFailure)
        {
            return ToolResponse.Error(resolveResult.FirstErrorMessage);
        }
        var resolvedPath = resolveResult.Value;

        var locateResult = await LocatePageFolderAsync(fileSystem, resourceKey, resolvedPath);
        if (locateResult.IsFailure)
        {
            return ToolResponse.Error(locateResult);
        }
        var pageSource = locateResult.Value;

        var pathResult = await ReadManifestPublishPathAsync(fileSystem, pageSource.ManifestPath);
        if (pathResult.IsFailure)
        {
            return ToolResponse.Error(pathResult);
        }
        var publishPath = pathResult.Value;

        if (confirmWithUser)
        {
            var confirmed = await ConfirmPublishAsync(publishPath);
            if (!confirmed)
            {
                return ToolResponse.Error("Publish cancelled by user.");
            }
        }

        var buildResult = await BuildPageArchiveAsync(fileSystem, pageSource.FolderPath);
        if (buildResult.IsFailure)
        {
            return ToolResponse.Error(buildResult);
        }
        var archive = buildResult.Value;

        var pageApiClient = GetRequiredService<IPageApiClient>();
        var publishResult = await pageApiClient.PublishPageAsync(archive.ZipData, publishPath, author);
        if (publishResult.IsFailure)
        {
            return ToolResponse.Error(publishResult);
        }
        var page = publishResult.Value;

        // The server is authoritative for the served path, so report what it
        // returned rather than the path read from the manifest.
        var result = new PagePublishResult(page.Path, page.Url, archive.EntryCount, archive.ZipData.Length);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }

    private async Task<bool> ConfirmPublishAsync(string publishPath)
    {
        var localizerService = GetRequiredService<Celbridge.Localization.ILocalizerService>();
        var title = localizerService.GetString("Page_PublishConfirm_Title");
        var message = localizerService.GetString("Page_PublishConfirm_Message", publishPath);

        return await ConfirmActionAsync(title, message);
    }

    // The publish source is a folder containing a pages.toml. Accepts either the
    // manifest's own resource key or the folder that holds it, so an agent can
    // name whichever it has to hand (mirrors package_publish).
    private static async Task<Result<PageSource>> LocatePageFolderAsync(
        ILocalFileSystem fileSystem,
        ResourceKey resourceKey,
        string resolvedPath)
    {
        var infoResult = await fileSystem.GetInfoAsync(resolvedPath);
        if (infoResult.IsFailure
            || infoResult.Value.Kind == StorageItemKind.NotFound)
        {
            return Result.Fail($"Resource not found: '{resourceKey}'.");
        }

        string folderPath;
        string manifestPath;
        if (infoResult.Value.Kind == StorageItemKind.Folder)
        {
            folderPath = resolvedPath;
            manifestPath = Path.Combine(resolvedPath, PageConstants.ManifestFileName);
        }
        else
        {
            var fileName = Path.GetFileName(resolvedPath);
            if (!string.Equals(fileName, PageConstants.ManifestFileName, StringComparison.OrdinalIgnoreCase))
            {
                return Result.Fail(
                    $"Expected the page's '{PageConstants.ManifestFileName}' manifest or its folder, " +
                    $"but '{resourceKey}' is a different file.");
            }
            manifestPath = resolvedPath;
            folderPath = Path.GetDirectoryName(resolvedPath)!;
        }

        var manifestInfoResult = await fileSystem.GetInfoAsync(manifestPath);
        if (manifestInfoResult.IsFailure
            || manifestInfoResult.Value.Kind != StorageItemKind.File)
        {
            // The plural 'pages.toml' is an easy thing to get wrong because every
            // other manifest in the project is singular. If the singular spelling
            // is present, point at it directly rather than reporting a bare miss.
            var nearMissResult = await DetectManifestNearMissAsync(fileSystem, folderPath);
            if (nearMissResult is not null)
            {
                return Result.Fail(nearMissResult);
            }

            return Result.Fail(
                $"Page manifest not found. Expected '{PageConstants.ManifestFileName}' in the page folder.");
        }

        return new PageSource(folderPath, manifestPath);
    }

    // The page manifest is 'pages.toml' (plural), unlike every other singular
    // manifest in the project. Returns a targeted message when the singular
    // 'page.toml' is present instead, so a near-miss is named rather than left
    // as a silent "no manifest found".
    private const string SingularManifestNearMiss = "page.toml";

    private static async Task<string?> DetectManifestNearMissAsync(ILocalFileSystem fileSystem, string folderPath)
    {
        // Windows file lookup is case-insensitive, so a wrong-case name already
        // resolves; the realistic near-miss is the singular spelling.
        if (string.Equals(SingularManifestNearMiss, PageConstants.ManifestFileName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var nearMissPath = Path.Combine(folderPath, SingularManifestNearMiss);
        var infoResult = await fileSystem.GetInfoAsync(nearMissPath);
        if (infoResult.IsSuccess
            && infoResult.Value.Kind == StorageItemKind.File)
        {
            return $"Found '{SingularManifestNearMiss}', but the page manifest must be named " +
                $"'{PageConstants.ManifestFileName}' (plural). Rename it to '{PageConstants.ManifestFileName}'.";
        }

        return null;
    }

    private static async Task<Result<string>> ReadManifestPublishPathAsync(ILocalFileSystem fileSystem, string manifestPath)
    {
        var readResult = await fileSystem.ReadAllTextAsync(manifestPath);
        if (readResult.IsFailure)
        {
            return Result.Fail($"Failed to read page manifest: {readResult.FirstErrorMessage}");
        }
        var tomlContent = readResult.Value;

        TomlTable? tomlTable;
        try
        {
            tomlTable = TomlSerializer.Deserialize<TomlTable>(tomlContent);
        }
        catch (TomlException exception)
        {
            return Result.Fail($"Invalid TOML in page manifest: {exception.Message}");
        }

        if (tomlTable is null)
        {
            return Result.Fail($"Page manifest '{PageConstants.ManifestFileName}' is empty or not a valid TOML table.");
        }

        if (!tomlTable.TryGetValue("publish", out var publishSection)
            || publishSection is not TomlTable publishTable)
        {
            return Result.Fail($"Page manifest is missing the required [publish] section in '{PageConstants.ManifestFileName}'.");
        }

        if (!publishTable.TryGetValue("path", out var pathValue)
            || pathValue is not string pathString
            || string.IsNullOrWhiteSpace(pathString))
        {
            return Result.Fail($"Page manifest is missing a required 'path' field in the [publish] section.");
        }

        return pathString.Trim();
    }

    // Zips the whole folder, including pages.toml. The current server reads the
    // publish path from the manifest in the bundle, so it is still uploaded; the
    // server serves everything else verbatim but does not serve pages.toml, so it
    // is not exposed at the public URL. The path is now also sent as a separate
    // form field (see PublishPageAsync), so once the server reads that field the
    // manifest can be dropped from the upload entirely.
    private static async Task<Result<PageArchive>> BuildPageArchiveAsync(ILocalFileSystem fileSystem, string folderPath)
    {
        int entryCount = 0;
        byte[] zipData;

        try
        {
            using var memoryStream = new MemoryStream();
            using (var zipArchive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var enumerateResult = await fileSystem.EnumerateAsync(folderPath, "*", recursive: true);
                if (enumerateResult.IsFailure)
                {
                    return Result.Fail($"Failed to enumerate page files: {enumerateResult.FirstErrorMessage}");
                }
                var fileEntries = enumerateResult.Value
                    .Where(entry => !entry.IsFolder)
                    .ToList();

                foreach (var fileEntry in fileEntries)
                {
                    // Skip symlinks and other reparse points rather than following them.
                    if (fileEntry.Attributes.HasFlag(FileSystemAttributes.ReparsePoint))
                    {
                        continue;
                    }

                    var filePath = fileEntry.FullPath;
                    var relativePath = Path.GetRelativePath(folderPath, filePath);
                    var entryName = relativePath.Replace('\\', '/');

                    var entry = zipArchive.CreateEntry(entryName, CompressionLevel.Optimal);
                    using var entryStream = entry.Open();
                    var openResult = await fileSystem.OpenReadAsync(filePath);
                    if (openResult.IsFailure)
                    {
                        return Result.Fail($"Failed to open file for packaging '{filePath}': {openResult.FirstErrorMessage}");
                    }
                    using var sourceStream = openResult.Value;
                    await sourceStream.CopyToAsync(entryStream);
                    entryCount++;
                }
            }

            zipData = memoryStream.ToArray();
        }
        catch (System.IO.IOException exception)
        {
            return Result.Fail($"Failed to create page archive: {exception.Message}");
        }

        if (entryCount == 0)
        {
            return Result.Fail("The page folder contains no files to publish.");
        }

        return new PageArchive(zipData, entryCount);
    }

    private record class PageSource(string FolderPath, string ManifestPath);

    private record class PageArchive(byte[] ZipData, int EntryCount);
}
