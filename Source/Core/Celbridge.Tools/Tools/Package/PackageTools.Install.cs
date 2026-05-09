using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Directory = System.IO.Directory;
using File = System.IO.File;
using Path = System.IO.Path;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by package_install with the installed package details.
/// </summary>
public record class PackageInstallResult(string PackageName, int Entries, string Destination);

public partial class PackageTools
{
    /// <summary>READ GUIDE FIRST. Install a package from the remote registry into packages/{packageName}/.</summary>
    [McpServerTool(Name = "package_install", Destructive = true)]
    [ToolAlias("package.install")]
    public async partial Task<CallToolResult> Install(string packageName, bool confirmWithUser = true)
    {
        const string ToolGuide = "package_install";

        if (!IsValidPackageName(packageName))
        {
            return ToolResponse.Error(
                $"Invalid package name: '{packageName}'. " +
                "Package names must be lowercase alphanumeric with hyphens, 1-214 characters.",
                ToolGuide);
        }

        // Find the package in the remote registry
        var packageApiClient = GetRequiredService<IPackageApiClient>();
        var listResult = await packageApiClient.ListPackagesAsync();

        if (listResult.IsFailure)
        {
            return ToolResponse.Error(listResult, ToolGuide);
        }

        var expectedFileName = $"{packageName}.zip";
        PackageApiEntry? matchingEntry = null;
        foreach (var entry in listResult.Value)
        {
            if (string.Equals(entry.FileName, expectedFileName, StringComparison.OrdinalIgnoreCase))
            {
                matchingEntry = entry;
                break;
            }
        }

        if (matchingEntry is null)
        {
            return ToolResponse.Error($"Package not found in registry: '{packageName}'", ToolGuide);
        }

        if (confirmWithUser)
        {
            var localizerService = GetRequiredService<Celbridge.Localization.ILocalizerService>();
            var title = localizerService.GetString("Package_InstallConfirm_Title");
            var message = localizerService.GetString("Package_InstallConfirm_Message", packageName);

            var confirmed = await ConfirmActionAsync(title, message);
            if (!confirmed)
            {
                return ToolResponse.Error("Install cancelled by user.", ToolGuide);
            }
        }

        // Download the package zip
        var downloadResult = await packageApiClient.DownloadPackageAsync(matchingEntry.Id);
        if (downloadResult.IsFailure)
        {
            return ToolResponse.Error(downloadResult, ToolGuide);
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        // Write the downloaded zip to a temporary cache file in the project
        var tempArchiveResource = ResourceKey.Create($".celbridge/.cache/{packageName}.zip");
        var resolveTempResult = resourceRegistry.ResolveResourcePath(tempArchiveResource);
        if (resolveTempResult.IsFailure)
        {
            var failure = Result.Fail("Failed to resolve temporary archive path")
                .WithErrors(resolveTempResult);
            return ToolResponse.Error(failure, ToolGuide);
        }
        var tempArchivePath = resolveTempResult.Value;

        var tempFolder = Path.GetDirectoryName(tempArchivePath);
        if (!string.IsNullOrEmpty(tempFolder) && !Directory.Exists(tempFolder))
        {
            Directory.CreateDirectory(tempFolder);
        }

        try
        {
            await File.WriteAllBytesAsync(tempArchivePath, downloadResult.Value);
        }
        catch (System.IO.IOException exception)
        {
            return ToolResponse.Error($"Failed to write downloaded package: {exception.Message}", ToolGuide);
        }

        var destinationResource = ResourceKey.Create($"packages/{packageName}");

        try
        {
            var unarchiveResultWrapper = await ExecuteCommandAsync<IUnarchiveResourceCommand, UnarchiveResult>(command =>
            {
                command.ArchiveResource = tempArchiveResource;
                command.DestinationResource = destinationResource;
                command.Overwrite = false;
            });

            if (unarchiveResultWrapper.IsFailure)
            {
                return ToolResponse.Error(unarchiveResultWrapper, ToolGuide);
            }

            var unarchiveResult = unarchiveResultWrapper.Value;
            var result = new PackageInstallResult(packageName, unarchiveResult.Entries, destinationResource.ToString());
            var json = JsonSerializer.Serialize(result, JsonOptions);
            return ToolResponse.Success(json);
        }
        finally
        {
            if (File.Exists(tempArchivePath))
            {
                File.Delete(tempArchivePath);
            }
        }
    }
}
