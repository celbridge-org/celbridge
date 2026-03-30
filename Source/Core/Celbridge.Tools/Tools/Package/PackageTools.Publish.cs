using System.IO.Compression;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Directory = System.IO.Directory;
using File = System.IO.File;
using FileAccess = System.IO.FileAccess;
using FileAttributes = System.IO.FileAttributes;
using FileInfo = System.IO.FileInfo;
using FileMode = System.IO.FileMode;
using FileShare = System.IO.FileShare;
using FileStream = System.IO.FileStream;
using Path = System.IO.Path;
using SearchOption = System.IO.SearchOption;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by package_publish with the published package details.
/// </summary>
public record class PackagePublishResult(string PackageName, int Entries, long Size);

public partial class PackageTools
{
    /// <summary>
    /// Publishes a folder as a named package to the local package registry.
    /// The folder's contents are archived and stored in the application's local data folder.
    /// </summary>
    /// <param name="resource">Resource key of the folder to publish.</param>
    /// <param name="packageName">Package name (lowercase alphanumeric and hyphens, e.g. "my-widget").</param>
    /// <returns>JSON object with fields: packageName (string), entries (int), size (long).</returns>
    [McpServerTool(Name = "package_publish", Destructive = true)]
    [ToolAlias("package.publish")]
    public async partial Task<CallToolResult> Publish(string resource, string packageName)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ErrorResult($"Invalid resource key: '{resource}'");
        }

        if (!IsValidPackageName(packageName))
        {
            return ErrorResult(
                $"Invalid package name: '{packageName}'. " +
                "Package names must be lowercase alphanumeric with hyphens, 1-214 characters.");
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var resolveSourceResult = resourceRegistry.ResolveResourcePath(resourceKey);
        if (resolveSourceResult.IsFailure)
        {
            return ErrorResult($"Failed to resolve path for resource: '{resource}'");
        }
        var sourcePath = resolveSourceResult.Value;

        if (!Directory.Exists(sourcePath))
        {
            return ErrorResult($"Folder not found: '{resource}'");
        }

        var registryPath = GetPackageRegistryPath();
        if (!Directory.Exists(registryPath))
        {
            Directory.CreateDirectory(registryPath);
        }

        var packageFilePath = GetPackageFilePath(packageName);
        int entryCount = 0;

        try
        {
            using var fileStream = new FileStream(packageFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var zipArchive = new ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: false);

            var filePaths = Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories);

            foreach (var filePath in filePaths)
            {
                var fileAttributes = File.GetAttributes(filePath);
                if (fileAttributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    continue;
                }

                var relativePath = Path.GetRelativePath(sourcePath, filePath);
                var entryName = relativePath.Replace('\\', '/');

                var entry = zipArchive.CreateEntry(entryName, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                using var sourceStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                await sourceStream.CopyToAsync(entryStream);
                entryCount++;
            }
        }
        catch (System.IO.IOException exception)
        {
            return ErrorResult($"Failed to publish package: {exception.Message}");
        }

        var packageSize = new FileInfo(packageFilePath).Length;

        var result = new PackagePublishResult(packageName, entryCount, packageSize);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return SuccessResult(json);
    }
}
