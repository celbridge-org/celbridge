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
/// including the version number assigned by the workshop. Warning carries an
/// advisory note (e.g. a stale-base concurrent-publish warning) or is null.
/// </summary>
public record class PackagePublishResult(string PackageName, int Version, int Entries, long Size, string? Warning = null);

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

        var packageApiClient = GetRequiredService<IPackageApiClient>();

        // Guardrail against the concurrent-publish footgun: if this folder was
        // installed from a version older than the workshop's current latest,
        // another publish landed in between and this one may overwrite or diverge
        // from it. The confirmation spells out the risk so the user gives informed
        // consent. Publishing is append-only (the sibling version still exists),
        // so an agent run (confirmWithUser false) proceeds with the warning in the
        // result rather than being blocked. A present-but-unreadable install
        // record is surfaced the same way, since the check could not run.
        var baseCheck = await CheckBaseAsync(
            packageApiClient,
            resourceService.FileSystem,
            packageSource.FolderResource,
            packageName);

        if (confirmWithUser)
        {
            var confirmed = await ConfirmPublishAsync(packageName, baseCheck);
            if (!confirmed)
            {
                return ToolResponse.Error("Publish cancelled by user.");
            }
        }

        var publishWarning = BuildPublishWarning(packageName, baseCheck);
        if (publishWarning is not null)
        {
            Logger.LogWarning(publishWarning);
        }

        var buildResult = await BuildPackageArchiveAsync(fileSystem, packageSource.FolderPath);
        if (buildResult.IsFailure)
        {
            return ToolResponse.Error(buildResult);
        }
        var archive = buildResult.Value;

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

        var result = new PackagePublishResult(packageName, receipt.Version, archive.EntryCount, archive.ZipData.Length, publishWarning);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }

    private async Task<bool> ConfirmPublishAsync(string packageName, PublishBaseCheck baseCheck)
    {
        var localizerService = GetRequiredService<Celbridge.Localization.ILocalizerService>();
        var title = localizerService.GetString("Package_PublishConfirm_Title");

        string message = baseCheck.Concern switch
        {
            PublishBaseConcern.Stale => localizerService.GetString(
                "Package_PublishStaleConfirm_Message", packageName, baseCheck.InstalledVersion, baseCheck.LatestVersion),
            PublishBaseConcern.RecordUnreadable => localizerService.GetString(
                "Package_PublishUnreadableRecordConfirm_Message", packageName),
            _ => localizerService.GetString("Package_PublishConfirm_Message", packageName)
        };

        return await ConfirmActionAsync(title, message);
    }

    private static string? BuildPublishWarning(string packageName, PublishBaseCheck baseCheck)
    {
        switch (baseCheck.Concern)
        {
            case PublishBaseConcern.Stale:
                return $"This folder was installed from {packageName}@{baseCheck.InstalledVersion}, " +
                    $"but the workshop's latest version is now {baseCheck.LatestVersion}. Another version was " +
                    "published after this folder was installed, so publishing may overwrite or diverge " +
                    "from that work. To build on the latest, reinstall it and re-apply your changes.";

            case PublishBaseConcern.RecordUnreadable:
                return $"The install record ({PackageConstants.HistoryFileName}) for this folder could not be read, " +
                    "so the stale-base check was skipped. If this folder was installed from the workshop, verify it " +
                    "is not based on a superseded version before relying on this publish.";

            default:
                return null;
        }
    }

    // Inspects the source folder's install record to decide whether publishing is
    // building on an out-of-date base. The record is read here (rather than via
    // the shared helper) so a present-but-unreadable record is told apart from an
    // absent one: an absent record is the legitimate authored-in-place case, while
    // an unreadable one means the check could not run and is surfaced as such.
    private async Task<PublishBaseCheck> CheckBaseAsync(
        IPackageApiClient packageApiClient,
        IResourceFileSystem resourceFileSystem,
        ResourceKey folderResource,
        string packageName)
    {
        var historyFile = folderResource.Combine(PackageConstants.HistoryFileName);
        var infoResult = await resourceFileSystem.GetInfoAsync(historyFile);
        if (infoResult.IsFailure
            || infoResult.Value.Kind != StorageItemKind.File)
        {
            // No record: authored in place, or never installed. Nothing to check.
            return new PublishBaseCheck(PublishBaseConcern.None);
        }

        var readResult = await resourceFileSystem.ReadAllTextAsync(historyFile);
        if (readResult.IsFailure)
        {
            return new PublishBaseCheck(PublishBaseConcern.RecordUnreadable);
        }

        var installedReference = PackageHistoryFile.TryReadInstalledReference(readResult.Value);
        if (installedReference is null)
        {
            // Present but no parseable heading: the base cannot be determined.
            return new PublishBaseCheck(PublishBaseConcern.RecordUnreadable);
        }

        var detailsResult = await packageApiClient.GetPackageAsync(packageName);
        if (detailsResult.IsFailure)
        {
            // A brand-new package or an unreachable workshop has nothing to compare.
            return new PublishBaseCheck(PublishBaseConcern.None);
        }

        var liveVersions = detailsResult.Value.Versions
            .Where(packageVersion => !packageVersion.Deleted)
            .ToList();
        if (liveVersions.Count == 0)
        {
            return new PublishBaseCheck(PublishBaseConcern.None);
        }
        var latestLiveVersion = liveVersions.Max(packageVersion => packageVersion.Version);

        if (!PackageHistoryFile.IsStaleBase(installedReference, packageName, latestLiveVersion))
        {
            return new PublishBaseCheck(PublishBaseConcern.None);
        }

        return new PublishBaseCheck(PublishBaseConcern.Stale, installedReference.Version, latestLiveVersion);
    }

    private enum PublishBaseConcern
    {
        None,
        Stale,
        RecordUnreadable
    }

    private sealed record PublishBaseCheck(PublishBaseConcern Concern, int InstalledVersion = 0, int LatestVersion = 0);

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
        var historyMarkdown = PackageHistoryFile.Format(packageName, detailsResult.Value.Versions, publishedVersion);
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
