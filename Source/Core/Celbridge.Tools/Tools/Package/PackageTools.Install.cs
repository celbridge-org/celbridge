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
    /// <summary>Install the latest version of a workshop package into packages/{packageName}/.</summary>
    [McpServerTool(Name = "package_install", Destructive = true)]
    [ToolAlias("package.install")]
    [RelatedGuides("packages_overview", "silent_vs_interactive")]
    public async partial Task<CallToolResult> Install(string packageName, bool confirmWithUser = true)
    {
        if (!PackageName.IsValid(packageName))
        {
            return ToolResponse.Error(InvalidPackageNameError(packageName));
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

        var packageApiClient = GetRequiredService<IPackageApiClient>();
        var downloadResult = await packageApiClient.DownloadLatestAsync(packageName);
        if (downloadResult.IsFailure)
        {
            return ToolResponse.Error(downloadResult);
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var workspaceService = workspaceWrapper.WorkspaceService;
        var resourceFileSystem = workspaceService.ResourceService.FileSystem;

        // Stage the downloaded zip under temp: so it lives in .celbridge/temp/
        // (created at workspace load) and is reachable through the gateway.
        var tempArchiveResource = new ResourceKey($"temp:{packageName}.zip");
        var writeArchiveResult = await resourceFileSystem.WriteAllBytesAsync(tempArchiveResource, downloadResult.Value);
        if (writeArchiveResult.IsFailure)
        {
            return ToolResponse.Error($"Failed to write downloaded package: {writeArchiveResult.FirstErrorMessage}");
        }

        var destinationResource = ResourceKey.Create($"{PackageConstants.DefaultPackagesFolder}/{packageName}");

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
            await resourceFileSystem.DeleteAsync(tempArchiveResource);
        }
    }
}
