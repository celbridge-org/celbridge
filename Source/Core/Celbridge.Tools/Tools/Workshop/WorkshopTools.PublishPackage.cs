using System.IO.Compression;
using System.Text.Json;
using Celbridge.Workshop;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Tomlyn;
using Tomlyn.Model;
using MemoryStream = System.IO.MemoryStream;
using Path = System.IO.Path;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by workshop_publish_package with the published package details,
/// including the workshop version the workshop assigned. Warning carries an advisory
/// note, such as a stale-base concurrent-publish warning, or is the empty string when
/// there is none. Callers branch on the value, not on whether the key is present.
/// </summary>
public record class WorkshopPublishPackageResult(
    string PackageName,
    int WorkshopVersion,
    int Entries,
    long Size,
    string Warning = "");

public partial class WorkshopTools
{
    /// <summary>Publish a package folder to the workshop as a new workshop version, named from its manifest.</summary>
    [McpServerTool(Name = "workshop_publish_package", Destructive = true)]
    [ToolAlias("workshop.publish_package")]
    [RelatedGuides("resource_keys", "workshop_versions", "packages_overview", "silent_vs_interactive", "document_editor_contributions", "utility_documents")]
    public async partial Task<CallToolResult> PublishPackage(string resource, string summary = "", bool confirmWithUser = true)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolResponse.InvalidResourceKey(resource);
        }

        if (summary.Length > WorkshopConstants.MaxSummaryLength)
        {
            return ToolResponse.Error(
                $"The summary is {summary.Length} characters, but the maximum is {WorkshopConstants.MaxSummaryLength}. " +
                "Shorten the summary and try again. It is not truncated automatically.");
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        if (!workspaceWrapper.IsWorkspaceLoaded)
        {
            return ToolResponse.Error("No project is loaded. Open a project before publishing a package.");
        }

        // Every published workshop version records its publisher, so a non-empty
        // Author must be configured before any upload work begins.
        var authorResult = await ResolvePublishAuthorAsync(confirmWithUser);
        if (authorResult.IsFailure)
        {
            return ToolResponse.Error(authorResult);
        }
        var author = authorResult.Value;

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

        var manifestResult = await ValidatePackageManifestAsync(fileSystem, packageSource.ManifestPath);
        if (manifestResult.IsFailure)
        {
            return ToolResponse.Error(manifestResult);
        }
        var packageName = manifestResult.Value;

        var packageApiClient = GetRequiredService<IPackageApiClient>();

        // Guardrail against the concurrent-publish footgun. If this folder was
        // installed from a workshop version older than the latest, another publish
        // landed in between and this one may overwrite or diverge from it. The
        // confirmation spells out the risk so the user gives informed consent.
        // Publishing is append-only (the sibling workshop version still exists),
        // so an agent run (confirmWithUser false) proceeds with the warning in the
        // result rather than being blocked. A present but unreadable install
        // record is surfaced the same way, since the check could not run.
        var baseCheck = await CheckBaseAsync(
            packageApiClient,
            resourceService.FileSystem,
            packageSource.FolderResource,
            packageName);

        if (confirmWithUser)
        {
            var confirmed = await ConfirmPublishPackageAsync(packageName, baseCheck);
            if (!confirmed)
            {
                return ToolResponse.Error("Publish cancelled by user.");
            }
        }

        var publishWarning = BuildPublishWarning(packageName, baseCheck);
        if (publishWarning.Length > 0)
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
        var publishResult = await packageApiClient.PublishVersionAsync(packageName, archive.ZipData, publishSummary, author);
        if (publishResult.IsFailure)
        {
            return ToolResponse.Error(publishResult);
        }
        var receipt = publishResult.Value;

        // Refresh the local HISTORY.md to the workshop version just assigned, so
        // the source folder matches what a consumer who installs that version
        // receives. Best effort, because the publish has already succeeded.
        await RefreshPublishedHistoryAsync(
            packageApiClient,
            resourceService.FileSystem,
            packageSource.FolderResource,
            packageName,
            receipt.WorkshopVersion);

        var result = new WorkshopPublishPackageResult(packageName, receipt.WorkshopVersion, archive.EntryCount, archive.ZipData.Length, publishWarning);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }

    private async Task<bool> ConfirmPublishPackageAsync(string packageName, PublishBaseCheck baseCheck)
    {
        var localizerService = GetRequiredService<Celbridge.Localization.ILocalizerService>();
        var title = localizerService.GetString("Workshop_PublishPackageConfirm_Title");

        string message = baseCheck.Concern switch
        {
            PublishBaseConcern.Stale => localizerService.GetString(
                "Workshop_PublishPackageStaleConfirm_Message", packageName, baseCheck.InstalledWorkshopVersion, baseCheck.LatestWorkshopVersion),
            PublishBaseConcern.RecordUnreadable => localizerService.GetString(
                "Workshop_PublishPackageUnreadableRecordConfirm_Message", packageName),
            _ => localizerService.GetString("Workshop_PublishPackageConfirm_Message", packageName)
        };

        return await ConfirmActionAsync(title, message);
    }

    private static string BuildPublishWarning(string packageName, PublishBaseCheck baseCheck)
    {
        switch (baseCheck.Concern)
        {
            case PublishBaseConcern.Stale:
                return $"This folder was installed from {packageName}@{baseCheck.InstalledWorkshopVersion}, " +
                    $"but the latest workshop version is now {baseCheck.LatestWorkshopVersion}. Another workshop version was " +
                    "published after this folder was installed, so publishing may overwrite or diverge " +
                    "from that work. To build on the latest, reinstall it and re-apply your changes.";

            case PublishBaseConcern.RecordUnreadable:
                return $"The install record ({WorkshopConstants.HistoryFileName}) for this folder could not be read, " +
                    "so the stale-base check was skipped. If this folder was installed from the workshop, verify it " +
                    "is not based on a superseded workshop version before relying on this publish.";

            default:
                return string.Empty;
        }
    }

    // Inspects the source folder's install record to decide whether publishing is
    // building on an out-of-date base. The record is read here (rather than via
    // the shared helper) so a present but unreadable record is told apart from an
    // absent one. An absent record is the legitimate authored-in-place case, while
    // an unreadable one means the check could not run and is surfaced as such.
    private async Task<PublishBaseCheck> CheckBaseAsync(
        IPackageApiClient packageApiClient,
        IResourceFileSystem resourceFileSystem,
        ResourceKey folderResource,
        string packageName)
    {
        var historyFile = folderResource.Combine(WorkshopConstants.HistoryFileName);
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

        var installedReference = PackageHistoryHelper.TryReadInstalledReference(readResult.Value);
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

        var liveWorkshopVersions = detailsResult.Value.WorkshopVersions
            .Where(workshopVersion => !workshopVersion.Deleted)
            .ToList();
        if (liveWorkshopVersions.Count == 0)
        {
            return new PublishBaseCheck(PublishBaseConcern.None);
        }
        var latestLiveWorkshopVersion = liveWorkshopVersions.Max(workshopVersion => workshopVersion.WorkshopVersion);

        if (!PackageHistoryHelper.IsStaleBase(installedReference, packageName, latestLiveWorkshopVersion))
        {
            return new PublishBaseCheck(PublishBaseConcern.None);
        }

        return new PublishBaseCheck(PublishBaseConcern.Stale, installedReference.WorkshopVersion, latestLiveWorkshopVersion);
    }

    private enum PublishBaseConcern
    {
        None,
        Stale,
        RecordUnreadable
    }

    private sealed record PublishBaseCheck(PublishBaseConcern Concern, int InstalledWorkshopVersion = 0, int LatestWorkshopVersion = 0);

    private async Task RefreshPublishedHistoryAsync(
        IPackageApiClient packageApiClient,
        IResourceFileSystem resourceFileSystem,
        ResourceKey folderResource,
        string packageName,
        int publishedWorkshopVersion)
    {
        var detailsResult = await packageApiClient.GetPackageAsync(packageName);
        if (detailsResult.IsFailure)
        {
            Logger.LogWarning(detailsResult,
                $"Published '{packageName}' workshop version {publishedWorkshopVersion} but could not read back its history to refresh {WorkshopConstants.HistoryFileName}");
            return;
        }

        var historyFile = folderResource.Combine(WorkshopConstants.HistoryFileName);
        var formatResult = PackageHistoryHelper.Format(packageName, detailsResult.Value.WorkshopVersions, publishedWorkshopVersion);
        if (formatResult.IsFailure)
        {
            Logger.LogWarning(formatResult,
                $"Published '{packageName}' workshop version {publishedWorkshopVersion} but could not build {WorkshopConstants.HistoryFileName}");
            return;
        }

        var historyMarkdown = formatResult.Value;
        var writeResult = await resourceFileSystem.WriteAllTextAsync(historyFile, historyMarkdown);
        if (writeResult.IsFailure)
        {
            Logger.LogWarning(writeResult,
                $"Published '{packageName}' workshop version {publishedWorkshopVersion} but could not write {WorkshopConstants.HistoryFileName}");
        }
    }

    // The publish source is the package's package.toml, and its folder is what
    // gets zipped. Accepts either the manifest's own resource key or the folder
    // that contains it, so an agent can name whichever it has to hand.
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

    // Reads the package name from the manifest. A manifest the loader would reject for its name or its
    // package-version is refused, so a publish never uploads a package that fails to load once installed.
    private static async Task<Result<string>> ValidatePackageManifestAsync(ILocalFileSystem fileSystem, string manifestPath)
    {
        var readResult = await fileSystem.ReadAllTextAsync(manifestPath);
        if (readResult.IsFailure)
        {
            return Result.Fail($"Failed to read package manifest: {readResult.FirstErrorMessage}");
        }
        var tomlContent = readResult.Value;

        TomlTable? tomlTable;
        try
        {
            tomlTable = TomlSerializer.Deserialize<TomlTable>(tomlContent);
        }
        catch (TomlException exception)
        {
            return Result.Fail($"Invalid TOML in package manifest: {exception.Message}");
        }

        if (tomlTable is null)
        {
            return Result.Fail("Package manifest is empty or not a valid TOML table.");
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

        if (packageTable.TryGetValue("package-version", out var versionValue))
        {
            if (versionValue is not string versionString)
            {
                return Result.Fail($"'package-version' must be a string such as \"{SemanticVersion.Default}\".");
            }

            var versionResult = SemanticVersion.ParseOptional(versionString);
            if (versionResult.IsFailure)
            {
                return Result.Fail($"'package-version': {versionResult.FirstErrorMessage}");
            }
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
                    if (string.Equals(entryName, WorkshopConstants.HistoryFileName, StringComparison.OrdinalIgnoreCase))
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
