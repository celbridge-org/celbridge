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
    /// <summary>
    /// Installs a named package from the remote registry into the project's packages folder.
    /// The package is downloaded and extracted to packages/{packageName}/ in the project root.
    /// Always set confirmWithUser to true unless the user has explicitly asked for unattended operation.
    /// </summary>
    /// <param name="packageName">Name of the package to install.</param>
    /// <param name="confirmWithUser">When true, shows a confirmation dialog before installing. Default is true.</param>
    /// <returns>JSON object with fields: packageName (string), entries (int), destination (string).</returns>
    [McpServerTool(Name = "package_install", Destructive = true)]
    [ToolAlias("package.install")]
    public async partial Task<CallToolResult> Install(string packageName, bool confirmWithUser = true)
    {
        if (!IsValidPackageName(packageName))
        {
            return ErrorResult(
                $"Invalid package name: '{packageName}'. " +
                "Package names must be lowercase alphanumeric with hyphens, 1-214 characters.");
        }

        // Find the package in the remote registry
        var packageApiClient = GetRequiredService<IPackageApiClient>();
        var listResult = await packageApiClient.ListPackagesAsync();

        if (listResult.IsFailure)
        {
            return ErrorResult(listResult.Error);
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
            return ErrorResult($"Package not found in registry: '{packageName}'");
        }

        if (confirmWithUser)
        {
            var localizerService = GetRequiredService<Celbridge.Localization.ILocalizerService>();
            var title = localizerService.GetString("Package_InstallConfirm_Title");
            var message = localizerService.GetString("Package_InstallConfirm_Message", packageName);

            var confirmed = await ConfirmActionAsync(title, message);
            if (!confirmed)
            {
                return ErrorResult("Install cancelled by user.");
            }
        }

        // Download the package zip
        var downloadResult = await packageApiClient.DownloadPackageAsync(matchingEntry.Id);
        if (downloadResult.IsFailure)
        {
            return ErrorResult(downloadResult.Error);
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        // Write the downloaded zip to a temporary cache file in the project
        var tempArchiveResource = ResourceKey.Create($".celbridge/.cache/{packageName}.zip");
        var resolveTempResult = resourceRegistry.ResolveResourcePath(tempArchiveResource);
        if (resolveTempResult.IsFailure)
        {
            return ErrorResult($"Failed to resolve temporary archive path: {resolveTempResult.Error}");
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
            return ErrorResult($"Failed to write downloaded package: {exception.Message}");
        }

        var destinationResource = ResourceKey.Create($"packages/{packageName}");

        try
        {
            var (callToolResult, unarchiveResult) = await ExecuteCommandAsync<IUnarchiveResourceCommand, UnarchiveResult>(command =>
            {
                command.ArchiveResource = tempArchiveResource;
                command.DestinationResource = destinationResource;
                command.Overwrite = false;
            });

            if (callToolResult.IsError == true || unarchiveResult is null)
            {
                return callToolResult;
            }

            var result = new PackageInstallResult(packageName, unarchiveResult.Entries, destinationResource.ToString());
            var json = JsonSerializer.Serialize(result, JsonOptions);
            return SuccessResult(json);
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
