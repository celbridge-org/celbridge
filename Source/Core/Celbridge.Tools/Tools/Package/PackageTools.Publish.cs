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
/// Result returned by package_publish with the published package details,
/// including the version number assigned by the workshop.
/// </summary>
public record class PackagePublishResult(string PackageName, int Version, int Entries, long Size);

public partial class PackageTools
{
    /// <summary>Publish a package folder to the workshop as a new version, named from its manifest.</summary>
    [McpServerTool(Name = "package_publish", Destructive = true)]
    [ToolAlias("package.publish")]
    [RelatedGuides("resource_keys", "packages_overview", "silent_vs_interactive", "document_editor_contributions")]
    public async partial Task<CallToolResult> Publish(string resource, string summary = "", bool confirmWithUser = true)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolResponse.InvalidResourceKey(resource);
        }

        if (summary.Length > PackageConstants.MaxSummaryLength)
        {
            return ToolResponse.Error(
                $"The summary is {summary.Length} characters, but the maximum is {PackageConstants.MaxSummaryLength}. " +
                "Shorten the summary and try again; it is not truncated automatically.");
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        if (!workspaceWrapper.IsWorkspacePageLoaded)
        {
            return ToolResponse.Error("No project is loaded. Open a project before publishing a package.");
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

        var locateResult = await LocatePackageFolderAsync(fileSystem, resourceKey, resolvedPath);
        if (locateResult.IsFailure)
        {
            return ToolResponse.Error(locateResult);
        }
        var packageSource = locateResult.Value;

        var nameResult = await ReadManifestPackageNameAsync(fileSystem, packageSource.ManifestPath);
        if (nameResult.IsFailure)
        {
            return ToolResponse.Error(nameResult);
        }
        var packageName = nameResult.Value;

        if (confirmWithUser)
        {
            var localizerService = GetRequiredService<Celbridge.Localization.ILocalizerService>();
            var title = localizerService.GetString("Package_PublishConfirm_Title");
            var message = localizerService.GetString("Package_PublishConfirm_Message", packageName);

            var confirmed = await ConfirmActionAsync(title, message);
            if (!confirmed)
            {
                return ToolResponse.Error("Publish cancelled by user.");
            }
        }

        var buildResult = await BuildPackageArchiveAsync(fileSystem, packageSource.FolderPath);
        if (buildResult.IsFailure)
        {
            return ToolResponse.Error(buildResult);
        }
        var archive = buildResult.Value;

        var packageApiClient = GetRequiredService<IPackageApiClient>();
        var publishSummary = string.IsNullOrEmpty(summary) ? null : summary;
        var publishResult = await packageApiClient.PublishVersionAsync(packageName, archive.ZipData, publishSummary);
        if (publishResult.IsFailure)
        {
            return ToolResponse.Error(publishResult);
        }
        var receipt = publishResult.Value;

        // Refresh the local HISTORY.md to the version just assigned, so the
        // source folder matches what a consumer who installs this version
        // receives. Best effort: the publish has already succeeded.
        await RefreshPublishedHistoryAsync(
            packageApiClient,
            resourceService.FileSystem,
            packageSource.FolderResource,
            packageName,
            receipt.Version);

        var result = new PackagePublishResult(packageName, receipt.Version, archive.EntryCount, archive.ZipData.Length);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }

    private async Task RefreshPublishedHistoryAsync(
        IPackageApiClient packageApiClient,
        IResourceFileSystem resourceFileSystem,
        ResourceKey folderResource,
        string packageName,
        int publishedVersion)
    {
        var detailsResult = await packageApiClient.GetPackageAsync(packageName);
        if (detailsResult.IsFailure)
        {
            Logger.LogWarning(detailsResult,
                $"Published '{packageName}' version {publishedVersion} but could not read back its history to refresh {PackageConstants.HistoryFileName}");
            return;
        }

        var historyFile = folderResource.Combine(PackageConstants.HistoryFileName);
        var historyMarkdown = PackageHistoryFile.Format(detailsResult.Value.Versions, publishedVersion);
        var writeResult = await resourceFileSystem.WriteAllTextAsync(historyFile, historyMarkdown);
        if (writeResult.IsFailure)
        {
            Logger.LogWarning(writeResult,
                $"Published '{packageName}' version {publishedVersion} but could not write {PackageConstants.HistoryFileName}");
        }
    }

    // The publish source is the package's package.toml; its folder is what gets
    // zipped. Accepts either the manifest's own resource key or the folder that
    // contains it, so an agent can name whichever it has to hand.
    private static async Task<Result<PackageSource>> LocatePackageFolderAsync(
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

        ResourceKey folderResource;
        string folderPath;
        string manifestPath;
        if (infoResult.Value.Kind == StorageItemKind.Folder)
        {
            folderResource = resourceKey;
            folderPath = resolvedPath;
            manifestPath = Path.Combine(resolvedPath, PackageConstants.ManifestFileName);
        }
        else
        {
            var fileName = Path.GetFileName(resolvedPath);
            if (!string.Equals(fileName, PackageConstants.ManifestFileName, StringComparison.OrdinalIgnoreCase))
            {
                return Result.Fail(
                    $"Expected the package's '{PackageConstants.ManifestFileName}' manifest or its folder, " +
                    $"but '{resourceKey}' is a different file.");
            }
            folderResource = resourceKey.GetParent();
            manifestPath = resolvedPath;
            folderPath = Path.GetDirectoryName(resolvedPath)!;
        }

        var manifestInfoResult = await fileSystem.GetInfoAsync(manifestPath);
        if (manifestInfoResult.IsFailure
            || manifestInfoResult.Value.Kind != StorageItemKind.File)
        {
            return Result.Fail(
                $"Package manifest not found. Expected '{PackageConstants.ManifestFileName}' in the package folder.");
        }

        return new PackageSource(folderResource, folderPath, manifestPath);
    }

    private static async Task<Result<string>> ReadManifestPackageNameAsync(ILocalFileSystem fileSystem, string manifestPath)
    {
        var readResult = await fileSystem.ReadAllTextAsync(manifestPath);
        if (readResult.IsFailure)
        {
            return Result.Fail($"Failed to read package manifest: {readResult.FirstErrorMessage}");
        }
        var tomlContent = readResult.Value;

        TomlTable tomlTable;
        try
        {
            tomlTable = Toml.ToModel(tomlContent);
        }
        catch (TomlException exception)
        {
            return Result.Fail($"Invalid TOML in package manifest: {exception.Message}");
        }

        if (!tomlTable.TryGetValue("package", out var packageSection)
            || packageSection is not TomlTable packageTable)
        {
            return Result.Fail("Package manifest is missing the required [package] section.");
        }

        if (!packageTable.TryGetValue("name", out var nameValue)
            || nameValue is not string nameString
            || string.IsNullOrWhiteSpace(nameString))
        {
            return Result.Fail("Package manifest is missing a required 'name' field in the [package] section.");
        }

        if (!PackageName.IsValid(nameString))
        {
            return Result.Fail(
                $"Package manifest declares an invalid name '{nameString}'. " +
                $"Package names must be lowercase alphanumeric with single hyphen separators, 1-{PackageConstants.MaxNameLength} characters.");
        }

        return nameString;
    }

    private static async Task<Result<PackageArchive>> BuildPackageArchiveAsync(ILocalFileSystem fileSystem, string folderPath)
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
                    return Result.Fail($"Failed to enumerate package files: {enumerateResult.FirstErrorMessage}");
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

                    // The generated HISTORY.md is a snapshot of the workshop's
                    // own history, not package content, so it is never published.
                    if (string.Equals(entryName, PackageConstants.HistoryFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

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
            return Result.Fail($"Failed to create package archive: {exception.Message}");
        }

        return new PackageArchive(zipData, entryCount);
    }

    private record class PackageSource(ResourceKey FolderResource, string FolderPath, string ManifestPath);

    private record class PackageArchive(byte[] ZipData, int EntryCount);
}
