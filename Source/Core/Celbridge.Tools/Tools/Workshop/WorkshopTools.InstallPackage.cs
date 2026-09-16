using System.Text.Json;
using Celbridge.Workshop;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Path = System.IO.Path;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by workshop_install_package with the installed package details.
/// </summary>
public record class WorkshopInstallPackageResult(string PackageName, int WorkshopVersion, int Entries, string Destination);

public partial class WorkshopTools
{
    /// <summary>Install a workshop version or alias of a package into a destination folder (default packages/).</summary>
    [McpServerTool(Name = "workshop_install_package", Destructive = true)]
    [ToolAlias("workshop.install_package")]
    [RelatedGuides("workshop_versions", "resource_keys", "silent_vs_interactive")]
    public async partial Task<CallToolResult> InstallPackage(
        string packageName,
        // The description generator copies this default into a file that lacks this file's usings, so the
        // constant is named in full.
        string workshopVersion = Celbridge.Workshop.WorkshopConstants.LatestAlias,
        string destination = "",
        bool confirmWithUser = true)
    {
        if (!PackageName.IsValid(packageName))
        {
            return ToolResponse.Error(InvalidPackageNameError(packageName));
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        if (!workspaceWrapper.IsWorkspaceLoaded)
        {
            return ToolResponse.Error("No project is loaded. Open a project before installing a package.");
        }

        ResourceKey destinationFolder;
        if (string.IsNullOrWhiteSpace(destination))
        {
            destinationFolder = new ResourceKey(PackageConstants.DefaultPackagesFolder);
        }
        else if (!ResourceKey.TryCreate(destination, out destinationFolder))
        {
            return ToolResponse.InvalidResourceKey(destination);
        }

        // The package extracts into a subfolder named for the package under the
        // chosen destination, so packages installed side by side never overlap.
        var packageFolder = destinationFolder.Combine(packageName);

        var workspaceService = workspaceWrapper.WorkspaceService;
        var resourceService = workspaceService.ResourceService;
        var resourceRegistry = resourceService.Registry;
        var resourceFileSystem = resourceService.FileSystem;

        var canCreateResult = resourceService.Operations.CanCreateResource(packageFolder, isFolder: true);
        if (canCreateResult.IsFailure)
        {
            return ToolResponse.Error(
                $"Cannot install into '{destinationFolder}': {canCreateResult.FirstErrorMessage}");
        }

        // A package installed under project: participates in discovery, so a
        // second copy of the same name at a different path would fault both
        // copies. Refuse before downloading and name the existing location.
        if (packageFolder.Root == ResourceKey.DefaultRoot)
        {
            var duplicateCheck = await CheckForDuplicateProjectPackageAsync(
                workspaceService.PackageService,
                resourceRegistry,
                resourceFileSystem,
                packageName,
                packageFolder);
            if (duplicateCheck.IsFailure)
            {
                return ToolResponse.Error(duplicateCheck);
            }
        }

        var packageApiClient = GetRequiredService<IPackageApiClient>();

        var detailsResult = await packageApiClient.GetPackageAsync(packageName);
        if (detailsResult.IsFailure)
        {
            return ToolResponse.Error(detailsResult);
        }
        var packageDetails = detailsResult.Value;

        var requestedWorkshopVersion = WorkshopConstants.LatestAlias;
        if (!string.IsNullOrWhiteSpace(workshopVersion))
        {
            requestedWorkshopVersion = workshopVersion.Trim();
        }

        var resolveVersionResult = WorkshopVersionResolver.Resolve(packageDetails, requestedWorkshopVersion);
        if (resolveVersionResult.IsFailure)
        {
            return ToolResponse.Error(resolveVersionResult);
        }
        var resolvedWorkshopVersion = resolveVersionResult.Value;

        // Treat an existing folder at the destination as a replace: its contents
        // are trashed and the package re-extracted. The installed workshop version
        // is read back from the existing HISTORY.md to inform the confirmation.
        var existingFolderResult = await resourceFileSystem.GetInfoAsync(packageFolder);
        var isReplace = existingFolderResult.IsSuccess
            && existingFolderResult.Value.Kind == StorageItemKind.Folder;

        int? installedWorkshopVersion = null;
        if (isReplace)
        {
            installedWorkshopVersion = await TryReadInstalledWorkshopVersionAsync(resourceFileSystem, packageFolder);
        }

        if (confirmWithUser)
        {
            var confirmed = await ConfirmInstallPackageAsync(
                packageName,
                packageFolder,
                resolvedWorkshopVersion,
                isReplace,
                installedWorkshopVersion);
            if (!confirmed)
            {
                return ToolResponse.Error("Install cancelled by user.");
            }
        }

        var downloadResult = await packageApiClient.DownloadVersionAsync(packageName, resolvedWorkshopVersion);
        if (downloadResult.IsFailure)
        {
            return ToolResponse.Error(downloadResult);
        }
        var packageBytes = downloadResult.Value;

        if (isReplace)
        {
            var replaceResult = await ReplaceExistingFolderAsync(packageFolder);
            if (replaceResult.IsFailure)
            {
                return ToolResponse.Error(replaceResult);
            }
        }

        // Stage the downloaded zip under temp: so it lives in .celbridge/temp/
        // (created at workspace load) and is reachable through the gateway.
        var stagedArchive = new ResourceKey($"temp:{packageName}.zip");
        var writeArchiveResult = await resourceFileSystem.WriteAllBytesAsync(stagedArchive, packageBytes);
        if (writeArchiveResult.IsFailure)
        {
            return ToolResponse.Error($"Failed to write downloaded package: {writeArchiveResult.FirstErrorMessage}");
        }

        int extractedEntries;
        try
        {
            var unarchiveResultWrapper = await ExecuteCommandAsync<IUnarchiveResourceCommand, UnarchiveResult>(command =>
            {
                command.ArchiveResource = stagedArchive;
                command.DestinationResource = packageFolder;
                command.Overwrite = false;
            });

            if (unarchiveResultWrapper.IsFailure)
            {
                return ToolResponse.Error(unarchiveResultWrapper);
            }

            extractedEntries = unarchiveResultWrapper.Value.Entries;
        }
        finally
        {
            // Best-effort cleanup of the staged archive. A failure here does
            // not change the install outcome the caller sees.
            await resourceFileSystem.DeleteAsync(stagedArchive);
        }

        var historyFile = packageFolder.Combine(WorkshopConstants.HistoryFileName);
        var formatHistoryResult = PackageHistoryHelper.Format(packageName, packageDetails.WorkshopVersions, resolvedWorkshopVersion);
        if (formatHistoryResult.IsFailure)
        {
            Logger.LogWarning(formatHistoryResult, $"Failed to build {WorkshopConstants.HistoryFileName} for package '{packageName}'");
        }
        else
        {
            var historyMarkdown = formatHistoryResult.Value;
            var writeHistoryResult = await resourceFileSystem.WriteAllTextAsync(historyFile, historyMarkdown);
            if (writeHistoryResult.IsFailure)
            {
                Logger.LogWarning(writeHistoryResult, $"Failed to write {WorkshopConstants.HistoryFileName} for package '{packageName}'");
            }
        }

        var result = new WorkshopInstallPackageResult(packageName, resolvedWorkshopVersion, extractedEntries, packageFolder.ToString());
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }

    // Checks against the packages as the project loaded. A same-name copy whose manifest has since been
    // removed cannot fault the next load, so it does not block the install. A same-name copy added during
    // the session is not seen, and shows up as a DuplicateName load failure on the next load.
    private static async Task<Result> CheckForDuplicateProjectPackageAsync(
        IPackageService packageService,
        IResourceRegistry resourceRegistry,
        IResourceFileSystem resourceFileSystem,
        string packageName,
        ResourceKey packageFolder)
    {
        var resolveTargetResult = resourceRegistry.ResolveResourcePath(packageFolder, validateCase: false);
        if (resolveTargetResult.IsFailure)
        {
            return Result.Fail($"Cannot resolve install destination '{packageFolder}': {resolveTargetResult.FirstErrorMessage}");
        }
        var targetPath = NormalizeFolderPath(resolveTargetResult.Value);

        foreach (var package in packageService.GetAllPackages())
        {
            if (package.Info.Origin != PackageOrigin.Project)
            {
                continue;
            }
            if (!string.Equals(package.Info.Name, packageName, StringComparison.Ordinal))
            {
                continue;
            }

            var existingPath = NormalizeFolderPath(package.Info.PackageFolder);
            if (string.Equals(existingPath, targetPath, StringComparison.OrdinalIgnoreCase))
            {
                // Same path: this is the replace case, not a duplicate fault.
                continue;
            }

            var isManifestPresent = await IsManifestPresentAsync(resourceRegistry, resourceFileSystem, package.Info.PackageFolder);
            if (!isManifestPresent)
            {
                continue;
            }

            var existingLocation = DescribeFolder(resourceRegistry, package.Info.PackageFolder);
            return Result.Fail(
                $"Package '{packageName}' is already installed in the project at '{existingLocation}'. " +
                "Move, rename, or remove it before installing to a different location, or reinstall over the existing folder to replace it.");
        }

        return Result.Ok();
    }

    private static string NormalizeFolderPath(string path)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static string DescribeFolder(IResourceRegistry resourceRegistry, string folderPath)
    {
        var keyResult = resourceRegistry.GetResourceKey(folderPath);
        if (keyResult.IsSuccess)
        {
            return keyResult.Value.ToString();
        }

        return folderPath;
    }

    // A manifest counts as present unless the file system reports it missing, so a package folder that cannot
    // be checked keeps blocking the install.
    private static async Task<bool> IsManifestPresentAsync(
        IResourceRegistry resourceRegistry,
        IResourceFileSystem resourceFileSystem,
        string packageFolderPath)
    {
        var folderKeyResult = resourceRegistry.GetResourceKey(packageFolderPath);
        if (folderKeyResult.IsFailure)
        {
            return true;
        }

        var manifestResource = folderKeyResult.Value.Combine(PackageConstants.ManifestFileName);
        var infoResult = await resourceFileSystem.GetInfoAsync(manifestResource);
        if (infoResult.IsFailure)
        {
            return true;
        }

        return infoResult.Value.Kind != StorageItemKind.NotFound;
    }

    private static async Task<int?> TryReadInstalledWorkshopVersionAsync(
        IResourceFileSystem resourceFileSystem,
        ResourceKey packageFolder)
    {
        var reference = await TryReadInstalledReferenceAsync(resourceFileSystem, packageFolder);
        return reference?.WorkshopVersion;
    }

    private static async Task<InstalledPackageReference?> TryReadInstalledReferenceAsync(
        IResourceFileSystem resourceFileSystem,
        ResourceKey packageFolder)
    {
        var historyFile = packageFolder.Combine(WorkshopConstants.HistoryFileName);
        var infoResult = await resourceFileSystem.GetInfoAsync(historyFile);
        if (infoResult.IsFailure
            || infoResult.Value.Kind != StorageItemKind.File)
        {
            return null;
        }

        var readResult = await resourceFileSystem.ReadAllTextAsync(historyFile);
        if (readResult.IsFailure)
        {
            return null;
        }

        return PackageHistoryHelper.TryReadInstalledReference(readResult.Value);
    }

    private async Task<bool> ConfirmInstallPackageAsync(
        string packageName,
        ResourceKey packageFolder,
        int incomingWorkshopVersion,
        bool isReplace,
        int? installedWorkshopVersion)
    {
        var localizerService = GetRequiredService<Celbridge.Localization.ILocalizerService>();

        string title;
        string message;
        if (isReplace)
        {
            title = localizerService.GetString("Workshop_ReplacePackageConfirm_Title");
            if (installedWorkshopVersion.HasValue)
            {
                message = localizerService.GetString(
                    "Workshop_ReplacePackageConfirm_Message",
                    packageFolder.ToString(),
                    packageName,
                    installedWorkshopVersion.Value,
                    incomingWorkshopVersion);
            }
            else
            {
                message = localizerService.GetString(
                    "Workshop_ReplacePackageConfirm_MessageUnknownVersion",
                    packageFolder.ToString(),
                    packageName,
                    incomingWorkshopVersion);
            }
        }
        else
        {
            title = localizerService.GetString("Workshop_InstallPackageConfirm_Title");
            message = localizerService.GetString(
                "Workshop_InstallPackageConfirm_Message",
                packageName,
                incomingWorkshopVersion,
                packageFolder.ToString());
        }

        return await ConfirmActionAsync(title, message);
    }

    private async Task<Result> ReplaceExistingFolderAsync(ResourceKey packageFolder)
    {
        // BreakReferences avoids a second confirmation prompt: the install
        // already confirmed the replace, and the re-extracted package recreates
        // the same resource keys, so references resolve again afterwards.
        var deleteResultWrapper = await ExecuteCommandAsync<IDeleteResourceCommand, DeleteCommandResult>(command =>
        {
            command.Resources = new List<ResourceKey> { packageFolder };
            command.ReferencePolicy = DeleteReferencePolicy.BreakReferences;
        });

        if (deleteResultWrapper.IsFailure)
        {
            return Result.Fail($"Failed to replace existing package folder '{packageFolder}'.")
                .WithErrors(deleteResultWrapper);
        }

        var deleteResult = deleteResultWrapper.Value;
        if (deleteResult.BatchOutcome != DeleteBatchOutcome.DeletedAll)
        {
            return Result.Fail(
                $"Could not fully remove the existing package folder '{packageFolder}' before reinstalling. " +
                "Close any open files under it and try again.");
        }

        return Result.Ok();
    }
}
