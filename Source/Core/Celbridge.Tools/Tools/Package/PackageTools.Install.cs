using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by package_install with the installed package details.
/// </summary>
public record class PackageInstallResult(string PackageName, int Entries, string Destination);

public partial class PackageTools
{
    /// <summary>Install a package from the remote registry into packages/{packageName}/.</summary>
    [McpServerTool(Name = "package_install", Destructive = true)]
    [ToolAlias("package.install")]
    [RelatedGuides("packages_overview", "silent_vs_interactive")]
    public async partial Task<CallToolResult> Install(string packageName, bool confirmWithUser = true)
    {
        if (!IsValidPackageName(packageName))
        {
            return ToolResponse.Error(
                $"Invalid package name: '{packageName}'. " +
                "Package names must be lowercase alphanumeric with hyphens, 1-214 characters.");
        }

        // Find the package in the remote registry
        var packageApiClient = GetRequiredService<IPackageApiClient>();
        var listResult = await packageApiClient.ListPackagesAsync();

        if (listResult.IsFailure)
        {
            return ToolResponse.Error(listResult);
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
            return ToolResponse.Error($"Package not found in registry: '{packageName}'");
        }

        if (confirmWithUser)
        {
            var localizerService = GetRequiredService<Celbridge.Localization.ILocalizerService>();
            var title = localizerService.GetString("Package_InstallConfirm_Title");
            var message = localizerService.GetString("Package_InstallConfirm_Message", packageName);

            var confirmed = await ConfirmActionAsync(title, message);
            if (!confirmed)
            {
                return ToolResponse.Error("Install cancelled by user.");
            }
        }

        // Download the package zip
        var downloadResult = await packageApiClient.DownloadPackageAsync(matchingEntry.Id);
        if (downloadResult.IsFailure)
        {
            return ToolResponse.Error(downloadResult);
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var workspaceService = workspaceWrapper.WorkspaceService;
        var fileSystem = workspaceService.ResourceFileSystem;

        // Stage the downloaded zip under temp: so it lives in .celbridge/temp/
        // (created at workspace load) and is reachable through the chokepoint.
        var tempArchiveResource = new ResourceKey($"temp:{packageName}.zip");
        var writeArchiveResult = await fileSystem.WriteAllBytesAsync(tempArchiveResource, downloadResult.Value);
        if (writeArchiveResult.IsFailure)
        {
            return ToolResponse.Error($"Failed to write downloaded package: {writeArchiveResult.FirstErrorMessage}");
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
                return ToolResponse.Error(unarchiveResultWrapper);
            }

            var unarchiveResult = unarchiveResultWrapper.Value;
            var result = new PackageInstallResult(packageName, unarchiveResult.Entries, destinationResource.ToString());
            var json = JsonSerializer.Serialize(result, JsonOptions);
            return ToolResponse.Success(json);
        }
        finally
        {
            // Best-effort cleanup of the staged archive; a failure here does
            // not change the install outcome the caller sees.
            await fileSystem.DeleteAsync(tempArchiveResource);
        }
    }
}
