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
    /// Installs a named package from the local registry into the project's packages folder.
    /// The package is extracted to packages/{packageName}/ in the project root.
    /// </summary>
    /// <param name="packageName">Name of the package to install.</param>
    /// <returns>JSON object with fields: packageName (string), entries (int), destination (string).</returns>
    [McpServerTool(Name = "package_install", Destructive = true)]
    [ToolAlias("package.install")]
    public async partial Task<CallToolResult> Install(string packageName)
    {
        if (!IsValidPackageName(packageName))
        {
            return ErrorResult(
                $"Invalid package name: '{packageName}'. " +
                "Package names must be lowercase alphanumeric with hyphens, 1-214 characters.");
        }

        var packageFilePath = GetPackageFilePath(packageName);
        if (!File.Exists(packageFilePath))
        {
            return ErrorResult($"Package not found: '{packageName}'");
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        // Copy the package zip into the project as a temporary file
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
            File.Copy(packageFilePath, tempArchivePath, overwrite: true);
        }
        catch (System.IO.IOException exception)
        {
            return ErrorResult($"Failed to copy package for installation: {exception.Message}");
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
            // Clean up the temporary zip
            if (File.Exists(tempArchivePath))
            {
                File.Delete(tempArchivePath);
            }
        }
    }
}
