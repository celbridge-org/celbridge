using System.IO.Compression;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Tomlyn;
using Tomlyn.Model;
using Directory = System.IO.Directory;
using File = System.IO.File;
using FileAccess = System.IO.FileAccess;
using FileAttributes = System.IO.FileAttributes;
using FileMode = System.IO.FileMode;
using FileShare = System.IO.FileShare;
using FileStream = System.IO.FileStream;
using MemoryStream = System.IO.MemoryStream;
using Path = System.IO.Path;
using Result = Celbridge.Core.Result;
using SearchOption = System.IO.SearchOption;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by package_publish with the published package details.
/// </summary>
public record class PackagePublishResult(string PackageName, int Entries, long Size);

public partial class PackageTools
{
    private const string PackagesFolderPrefix = "packages/";
    private const string ManifestFileName = "package.toml";

    /// <summary>
    /// Publishes a folder as a named package to the remote package registry.
    /// The folder must be inside the project's packages/ folder, the folder name must
    /// match the package name, and the folder must contain a valid package.toml manifest.
    /// Always set confirmWithUser to true unless the user has explicitly asked for unattended operation.
    /// </summary>
    /// <param name="resource">Resource key of the folder to publish (must be packages/{packageName}).</param>
    /// <param name="packageName">Package name (lowercase alphanumeric and hyphens, e.g. "my-widget").</param>
    /// <param name="confirmWithUser">When true, shows a confirmation dialog before publishing. Default is true.</param>
    /// <returns>JSON object with fields: packageName (string), entries (int), size (long).</returns>
    [McpServerTool(Name = "package_publish", Destructive = true)]
    [ToolAlias("package.publish")]
    public async partial Task<CallToolResult> Publish(string resource, string packageName, bool confirmWithUser = true)
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

        // Validate the resource is inside the packages folder
        var resourceString = resourceKey.ToString();
        if (!resourceString.StartsWith(PackagesFolderPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return ErrorResult(
                $"Package must be inside the '{PackagesFolderPrefix}' folder. " +
                $"Expected: '{PackagesFolderPrefix}{packageName}'");
        }

        // Validate the folder name matches the package name
        var folderName = resourceString.Substring(PackagesFolderPrefix.Length).TrimEnd('/');
        if (!string.Equals(folderName, packageName, StringComparison.Ordinal))
        {
            return ErrorResult(
                $"Folder name '{folderName}' does not match package name '{packageName}'. " +
                $"The package folder must be '{PackagesFolderPrefix}{packageName}'.");
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

        // Validate that the package manifest exists and is valid
        var manifestPath = Path.Combine(sourcePath, ManifestFileName);
        var validateResult = ValidatePackageManifest(manifestPath);
        if (validateResult.IsFailure)
        {
            return ErrorResult(validateResult.Error);
        }

        if (confirmWithUser)
        {
            var localizerService = GetRequiredService<Celbridge.Localization.ILocalizerService>();
            var title = localizerService.GetString("Package_PublishConfirm_Title");
            var message = localizerService.GetString("Package_PublishConfirm_Message", packageName);

            var confirmed = await ConfirmActionAsync(title, message);
            if (!confirmed)
            {
                return ErrorResult("Publish cancelled by user.");
            }
        }

        int entryCount = 0;
        byte[] zipData;

        try
        {
            using var memoryStream = new MemoryStream();
            using (var zipArchive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
            {
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

            zipData = memoryStream.ToArray();
        }
        catch (System.IO.IOException exception)
        {
            return ErrorResult($"Failed to create package archive: {exception.Message}");
        }

        var packageApiClient = GetRequiredService<IPackageApiClient>();
        var fileName = $"{packageName}.zip";
        var uploadResult = await packageApiClient.UploadPackageAsync(fileName, zipData);

        if (uploadResult.IsFailure)
        {
            return ErrorResult(uploadResult.Error);
        }

        var result = new PackagePublishResult(packageName, entryCount, zipData.Length);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return SuccessResult(json);
    }

    private static Result ValidatePackageManifest(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            return Result.Fail(
                $"Package manifest not found. Expected '{ManifestFileName}' in the package folder.");
        }

        string tomlContent;
        try
        {
            tomlContent = File.ReadAllText(manifestPath);
        }
        catch (System.IO.IOException exception)
        {
            return Result.Fail($"Failed to read package manifest: {exception.Message}");
        }

        TomlTable tomlTable;
        try
        {
            tomlTable = Toml.ToModel(tomlContent);
        }
        catch (TomlException exception)
        {
            return Result.Fail($"Invalid TOML in package manifest: {exception.Message}");
        }

        if (!tomlTable.TryGetValue("package", out var packageSection) ||
            packageSection is not TomlTable packageTable)
        {
            return Result.Fail("Package manifest is missing the required [package] section.");
        }

        if (!packageTable.TryGetValue("id", out var idValue) ||
            idValue is not string idString ||
            string.IsNullOrWhiteSpace(idString))
        {
            return Result.Fail("Package manifest is missing a required 'id' field in the [package] section.");
        }

        if (!packageTable.TryGetValue("name", out var nameValue) ||
            nameValue is not string nameString ||
            string.IsNullOrWhiteSpace(nameString))
        {
            return Result.Fail("Package manifest is missing a required 'name' field in the [package] section.");
        }

        return Result.Ok();
    }
}
