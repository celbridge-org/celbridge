using System.Text;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Directory = System.IO.Directory;
using File = System.IO.File;
using Path = System.IO.Path;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by package_create with the created package details.
/// </summary>
public record class PackageCreateResult(string PackageName, string Resource, string ManifestPath);

public partial class PackageTools
{
    /// <summary>READ GUIDE FIRST. Create a new package skeleton at packages/{packageName}/ with stub manifest.</summary>
    [McpServerTool(Name = "package_create", Destructive = true)]
    [ToolAlias("package.create")]
    public partial CallToolResult Create(string packageName)
    {
        if (!IsValidPackageName(packageName))
        {
            return ToolResponse.Error(
                $"Invalid package name: '{packageName}'. " +
                "Package names must be lowercase alphanumeric with hyphens, 1-214 characters.");
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var packageResource = ResourceKey.Create($"packages/{packageName}");
        var resolveResult = resourceRegistry.ResolveResourcePath(packageResource);
        if (resolveResult.IsFailure)
        {
            var failure = Result.Fail("Failed to resolve path for package")
                .WithErrors(resolveResult);
            return ToolResponse.Error(failure);
        }
        var packageFolderPath = resolveResult.Value;

        if (Directory.Exists(packageFolderPath))
        {
            return ToolResponse.Error($"Package already exists: 'packages/{packageName}'");
        }

        try
        {
            Directory.CreateDirectory(packageFolderPath);

            var manifestContent = new StringBuilder();
            manifestContent.AppendLine("[package]");
            manifestContent.AppendLine($"id = \"{packageName}\"");
            manifestContent.AppendLine($"name = \"{packageName}\"");
            manifestContent.AppendLine("version = \"1.0.0\"");
            manifestContent.AppendLine();
            manifestContent.AppendLine("[contributes]");

            var manifestPath = Path.Combine(packageFolderPath, ManifestFileName);
            File.WriteAllText(manifestPath, manifestContent.ToString());
        }
        catch (System.IO.IOException exception)
        {
            return ToolResponse.Error($"Failed to create package: {exception.Message}");
        }

        var result = new PackageCreateResult(
            packageName,
            packageResource.ToString(),
            $"packages/{packageName}/{ManifestFileName}");

        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }
}
