using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Path = System.IO.Path;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by package_install with the installed package details.
/// </summary>
public record class PackageInstallResult(string PackageName, int Version, int Entries, string Destination);

public partial class PackageTools
{
    /// <summary>Install a workshop package version or alias into a destination folder (default packages/).</summary>
    [McpServerTool(Name = "package_install", Destructive = true)]
    [ToolAlias("package.install")]
    [RelatedGuides("packages_overview", "resource_keys", "silent_vs_interactive")]
    public async partial Task<CallToolResult> Install(
        string packageName,
        string version = PackageConstants.LatestAlias,
        string destination = "",
        bool confirmWithUser = true)
    {
        if (!PackageName.IsValid(packageName))
        {
            return ToolResponse.Error(InvalidPackageNameError(packageName));
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        if (!workspaceWrapper.IsWorkspacePageLoaded)
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
            var duplicateCheck = CheckForDuplicateProjectPackage(
                workspaceService.PackageService,
                resourceRegistry,
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

        var requestedVersion = string.IsNullOrWhiteSpace(version) ? PackageConstants.LatestAlias : version.Trim();
        var resolveVersionResult = PackageVersionResolver.ResolveForInstall(packageDetails, requestedVersion);
        if (resolveVersionResult.IsFailure)
        {
            return ToolResponse.Error(resolveVersionResult);
        }
        var resolvedVersion = resolveVersionResult.Value;

        // Treat an existing folder at the destination as a replace: its contents
        // are trashed and the package re-extracted. The installed version is read
        // back from the existing HISTORY.md to inform the confirmation.
        var existingFolderResult = await resourceFileSystem.GetInfoAsync(packageFolder);
        var isReplace = existingFolderResult.IsSuccess
            && existingFolderResult.Value.Kind == StorageItemKind.Folder;

        int? installedVersion = null;
        if (isReplace)
        {
            installedVersion = await TryReadInstalledVersionAsync(resourceFileSystem, packageFolder);
        }

        if (confirmWithUser)
        {
            var confirmed = await ConfirmInstallAsync(
                packageName,
                packageFolder,
                resolvedVersion,
                isReplace,
                installedVersion);
            if (!confirmed)
            {
                return ToolResponse.Error("Install cancelled by user.");
            }
        }

        var downloadResult = await packageApiClient.DownloadVersionAsync(packageName, resolvedVersion);
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
            // Best-effort cleanup of the staged archive; a failure here does
            // not change the install outcome the caller sees.
            await resourceFileSystem.DeleteAsync(stagedArchive);
        }

        var historyFile = packageFolder.Combine(PackageConstants.HistoryFileName);
        var historyMarkdown = PackageHistoryFile.Format(packageDetails.Versions, resolvedVersion);
        var writeHistoryResult = await resourceFileSystem.WriteAllTextAsync(historyFile, historyMarkdown);
        if (writeHistoryResult.IsFailure)
        {
            Logger.LogWarning(writeHistoryResult, $"Failed to write {PackageConstants.HistoryFileName} for package '{packageName}'");
        }

        var result = new PackageInstallResult(packageName, resolvedVersion, extractedEntries, packageFolder.ToString());
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }

    private static Result CheckForDuplicateProjectPackage(
        IPackageService packageService,
        IResourceRegistry resourceRegistry,
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

    private static async Task<int?> TryReadInstalledVersionAsync(
        IResourceFileSystem resourceFileSystem,
        ResourceKey packageFolder)
    {
        var historyFile = packageFolder.Combine(PackageConstants.HistoryFileName);
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

        return PackageHistoryFile.TryReadInstalledVersion(readResult.Value);
    }

    private async Task<bool> ConfirmInstallAsync(
        string packageName,
        ResourceKey packageFolder,
        int incomingVersion,
        bool isReplace,
        int? installedVersion)
    {
        var localizerService = GetRequiredService<Celbridge.Localization.ILocalizerService>();

        string title;
        string message;
        if (isReplace)
        {
            title = localizerService.GetString("Package_ReplaceConfirm_Title");
            if (installedVersion.HasValue)
            {
                message = localizerService.GetString(
                    "Package_ReplaceConfirm_Message",
                    packageFolder.ToString(),
                    packageName,
                    installedVersion.Value,
                    incomingVersion);
            }
            else
            {
                message = localizerService.GetString(
                    "Package_ReplaceConfirm_MessageUnknownVersion",
                    packageFolder.ToString(),
                    packageName,
                    incomingVersion);
            }
        }
        else
        {
            title = localizerService.GetString("Package_InstallConfirm_Title");
            message = localizerService.GetString(
                "Package_InstallConfirm_Message",
                packageName,
                incomingVersion,
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
