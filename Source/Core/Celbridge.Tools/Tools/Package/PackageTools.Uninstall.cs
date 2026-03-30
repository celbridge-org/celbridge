using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Directory = System.IO.Directory;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by package_uninstall confirming the removed package.
/// </summary>
public record class PackageUninstallResult(string PackageName);

public partial class PackageTools
{
    /// <summary>
    /// Uninstalls a package by removing its folder from the project's packages folder.
    /// </summary>
    /// <param name="packageName">Name of the package to uninstall.</param>
    /// <returns>JSON object with fields: packageName (string).</returns>
    [McpServerTool(Name = "package_uninstall", Destructive = true)]
    [ToolAlias("package.uninstall")]
    public async partial Task<CallToolResult> Uninstall(string packageName)
    {
        if (!IsValidPackageName(packageName))
        {
            return ErrorResult(
                $"Invalid package name: '{packageName}'. " +
                "Package names must be lowercase alphanumeric with hyphens, 1-214 characters.");
        }

        var packageResource = ResourceKey.Create($"packages/{packageName}");

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var resolvePackageResult = resourceRegistry.ResolveResourcePath(packageResource);
        if (resolvePackageResult.IsFailure)
        {
            return ErrorResult($"Failed to resolve path for package: '{packageName}'");
        }
        var packagePath = resolvePackageResult.Value;

        if (!Directory.Exists(packagePath))
        {
            return ErrorResult($"Package is not installed: '{packageName}'");
        }

        var callToolResult = await ExecuteCommandAsync<IDeleteResourceCommand>(command =>
        {
            command.Resources = new List<ResourceKey> { packageResource };
        });

        if (callToolResult.IsError == true)
        {
            return callToolResult;
        }

        var result = new PackageUninstallResult(packageName);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return SuccessResult(json);
    }
}
