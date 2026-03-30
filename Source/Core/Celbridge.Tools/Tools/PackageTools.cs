using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
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
/// MCP tools for package operations: archiving, unarchiving, and local package management.
/// </summary>
[McpServerToolType]
public partial class PackageTools : AgentToolBase
{
    public PackageTools(IApplicationServiceProvider services) : base(services) { }

    /// <summary>
    /// Creates a zip archive from a file or folder. When archiving a folder, the archive
    /// contains the folder's contents at the root, not the folder itself.
    /// </summary>
    /// <param name="resource">Resource key of the file or folder to archive.</param>
    /// <param name="archive">Resource key for the output zip file.</param>
    /// <param name="include">Optional semicolon-separated glob patterns to include (e.g. "*.py;*.md"). When empty, all files are included.</param>
    /// <param name="exclude">Optional semicolon-separated glob patterns to exclude (e.g. "__pycache__;.git").</param>
    /// <param name="overwrite">Whether to overwrite an existing archive. Default is false.</param>
    /// <returns>JSON object with fields: entries (int), size (long), archive (string).</returns>
    [McpServerTool(Name = "package_archive")]
    [ToolAlias("package.archive")]
    public async partial Task<CallToolResult> Archive(
        string resource,
        string archive,
        string include = "",
        string exclude = "",
        bool overwrite = false)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ErrorResult($"Invalid resource key: '{resource}'");
        }

        if (!ResourceKey.TryCreate(archive, out var archiveKey))
        {
            return ErrorResult($"Invalid resource key: '{archive}'");
        }

        var (callToolResult, archiveResult) = await ExecuteCommandAsync<IArchiveResourceCommand, ArchiveResult>(command =>
        {
            command.SourceResource = resourceKey;
            command.ArchiveResource = archiveKey;
            command.Include = include;
            command.Exclude = exclude;
            command.Overwrite = overwrite;
        });

        if (callToolResult.IsError == true || archiveResult is null)
        {
            return callToolResult;
        }

        return SuccessResult(JsonSerializer.Serialize(new
        {
            entries = archiveResult.Entries,
            size = archiveResult.Size,
            archive = archiveResult.Archive
        }));
    }

    /// <summary>
    /// Extracts a zip archive to a destination folder.
    /// </summary>
    /// <param name="archive">Resource key of the zip file to extract.</param>
    /// <param name="destination">Resource key of the target folder.</param>
    /// <param name="overwrite">Whether to overwrite existing files. Default is false.</param>
    /// <returns>JSON object with fields: entries (int), destination (string).</returns>
    [McpServerTool(Name = "package_unarchive")]
    [ToolAlias("package.unarchive")]
    public async partial Task<CallToolResult> Unarchive(
        string archive,
        string destination,
        bool overwrite = false)
    {
        if (!ResourceKey.TryCreate(archive, out var archiveKey))
        {
            return ErrorResult($"Invalid resource key: '{archive}'");
        }

        if (!ResourceKey.TryCreate(destination, out var destinationKey))
        {
            return ErrorResult($"Invalid resource key: '{destination}'");
        }

        var (callToolResult, unarchiveResult) = await ExecuteCommandAsync<IUnarchiveResourceCommand, UnarchiveResult>(command =>
        {
            command.ArchiveResource = archiveKey;
            command.DestinationResource = destinationKey;
            command.Overwrite = overwrite;
        });

        if (callToolResult.IsError == true || unarchiveResult is null)
        {
            return callToolResult;
        }

        return SuccessResult(JsonSerializer.Serialize(new
        {
            entries = unarchiveResult.Entries,
            destination = unarchiveResult.Destination
        }));
    }

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

        return SuccessResult(JsonSerializer.Serialize(new
        {
            packageName,
            entries = entryCount,
            size = packageSize
        }));
    }

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

            return SuccessResult(JsonSerializer.Serialize(new
            {
                packageName,
                entries = unarchiveResult.Entries,
                destination = destinationResource.ToString()
            }));
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

        return SuccessResult(JsonSerializer.Serialize(new
        {
            packageName
        }));
    }

    /// <summary>
    /// Lists all packages available in the local package registry.
    /// </summary>
    /// <returns>JSON array of objects with fields: packageName (string), size (long).</returns>
    [McpServerTool(Name = "package_list", ReadOnly = true)]
    [ToolAlias("package.list")]
    public partial CallToolResult List()
    {
        var registryPath = GetPackageRegistryPath();

        var packages = new List<object>();

        if (Directory.Exists(registryPath))
        {
            var zipFiles = Directory.GetFiles(registryPath, "*.zip");

            foreach (var zipFile in zipFiles)
            {
                var fileName = Path.GetFileNameWithoutExtension(zipFile);

                if (!IsValidPackageName(fileName))
                {
                    continue;
                }

                var fileInfo = new FileInfo(zipFile);
                packages.Add(new
                {
                    packageName = fileName,
                    size = fileInfo.Length
                });
            }
        }

        return SuccessResult(JsonSerializer.Serialize(packages));
    }

    private static bool IsValidPackageName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 214)
        {
            return false;
        }

        return Regex.IsMatch(name, @"^[a-z0-9]([a-z0-9\-]*[a-z0-9])?$") &&
               !name.Contains("--");
    }

    private static string GetPackageRegistryPath()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appDataPath, "Celbridge", "Packages");
    }

    private static string GetPackageFilePath(string packageName)
    {
        return Path.Combine(GetPackageRegistryPath(), $"{packageName}.zip");
    }
}
