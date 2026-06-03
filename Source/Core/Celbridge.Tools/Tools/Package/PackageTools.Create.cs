using System.Text;
using System.Text.Json;
using Celbridge.FileSystem;
using Celbridge.Resources;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by package_create with the created package details.
/// </summary>
public record class PackageCreateResult(string PackageName, string Resource, string ManifestPath);

public partial class PackageTools
{
    /// <summary>Create a new package skeleton at packages/{packageName}/ with stub manifest.</summary>
    [McpServerTool(Name = "package_create", Destructive = true)]
    [ToolAlias("package.create")]
    [RelatedGuides("packages_overview")]
    public async partial Task<CallToolResult> Create(string packageName)
    {
        if (!IsValidPackageName(packageName))
        {
            return ToolResponse.Error(
                $"Invalid package name: '{packageName}'. " +
                "Package names must be lowercase alphanumeric with hyphens, 1-214 characters.");
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var workspaceService = workspaceWrapper.WorkspaceService;
        var resourceRegistry = workspaceService.ResourceService.Registry;
        var resourceFileSystem = workspaceService.ResourceService.FileSystem;
        var fileSystem = GetRequiredService<ILocalFileSystem>();

        var packageResource = ResourceKey.Create($"packages/{packageName}");
        var resolveResult = resourceRegistry.ResolveResourcePath(packageResource);
        if (resolveResult.IsFailure)
        {
            var failure = Result.Fail("Failed to resolve path for package")
                .WithErrors(resolveResult);
            return ToolResponse.Error(failure);
        }
        var packageFolderPath = resolveResult.Value;

        var packageInfoResult = await fileSystem.GetInfoAsync(packageFolderPath);
        if (packageInfoResult.IsSuccess
            && packageInfoResult.Value.Kind == StorageItemKind.Folder)
        {
            return ToolResponse.Error($"Package already exists: 'packages/{packageName}'");
        }

        var manifestContent = new StringBuilder();
        manifestContent.AppendLine("[package]");
        manifestContent.AppendLine($"id = \"{packageName}\"");
        manifestContent.AppendLine($"name = \"{packageName}\"");
        manifestContent.AppendLine("version = \"1.0.0\"");
        manifestContent.AppendLine();
        manifestContent.AppendLine("[contributes]");

        var manifestResource = ResourceKey.Create($"packages/{packageName}/{ManifestFileName}");
        var writeManifestResult = await resourceFileSystem.WriteAllTextAsync(manifestResource, manifestContent.ToString());
        if (writeManifestResult.IsFailure)
        {
            return ToolResponse.Error($"Failed to create package: {writeManifestResult.FirstErrorMessage}");
        }

        var result = new PackageCreateResult(
            packageName,
            packageResource.ToString(),
            $"packages/{packageName}/{ManifestFileName}");

        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }
}
