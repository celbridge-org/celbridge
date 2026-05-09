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

    /// <summary>READ GUIDE FIRST. Publish packages/{packageName}/ to the remote registry (visible to other users).</summary>
    [McpServerTool(Name = "package_publish", Destructive = true)]
    [ToolAlias("package.publish")]
    public async partial Task<CallToolResult> Publish(string resource, string packageName, bool confirmWithUser = true)
    {
        const string ToolGuide = "package_publish";

        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolResponse.InvalidResourceKey(resource);
        }

        if (!IsValidPackageName(packageName))
        {
            return ToolResponse.Error(
                $"Invalid package name: '{packageName}'. " +
                "Package names must be lowercase alphanumeric with hyphens, 1-214 characters.",
                ToolGuide);
        }

        // Validate the resource is inside the packages folder
        var resourceString = resourceKey.ToString();
        if (!resourceString.StartsWith(PackagesFolderPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return ToolResponse.Error(
                $"Package must be inside the '{PackagesFolderPrefix}' folder. " +
                $"Expected: '{PackagesFolderPrefix}{packageName}'",
                ToolGuide);
        }

        // Validate the folder name matches the package name
        var folderName = resourceString.Substring(PackagesFolderPrefix.Length).TrimEnd('/');
        if (!string.Equals(folderName, packageName, StringComparison.Ordinal))
        {
            return ToolResponse.Error(
                $"Folder name '{folderName}' does not match package name '{packageName}'. " +
                $"The package folder must be '{PackagesFolderPrefix}{packageName}'.",
                ToolGuide);
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var resolveSourceResult = resourceRegistry.ResolveResourcePath(resourceKey);
        if (resolveSourceResult.IsFailure)
        {
            return ToolResponse.Error($"Failed to resolve path for resource: '{resource}'", ToolGuide);
        }
        var sourcePath = resolveSourceResult.Value;

        if (!Directory.Exists(sourcePath))
        {
            return ToolResponse.Error($"Folder not found: '{resource}'", ToolGuide);
        }

        // Validate that the package manifest exists and is valid
        var manifestPath = Path.Combine(sourcePath, ManifestFileName);
        var validateResult = ValidatePackageManifest(manifestPath);
        if (validateResult.IsFailure)
        {
            return ToolResponse.Error(validateResult, ToolGuide);
        }

        if (confirmWithUser)
        {
            var localizerService = GetRequiredService<Celbridge.Localization.ILocalizerService>();
            var title = localizerService.GetString("Package_PublishConfirm_Title");
            var message = localizerService.GetString("Package_PublishConfirm_Message", packageName);

            var confirmed = await ConfirmActionAsync(title, message);
            if (!confirmed)
            {
                return ToolResponse.Error("Publish cancelled by user.", ToolGuide);
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
            return ToolResponse.Error($"Failed to create package archive: {exception.Message}", ToolGuide);
        }

        var packageApiClient = GetRequiredService<IPackageApiClient>();
        var fileName = $"{packageName}.zip";
        var uploadResult = await packageApiClient.UploadPackageAsync(fileName, zipData);

        if (uploadResult.IsFailure)
        {
            return ToolResponse.Error(uploadResult, ToolGuide);
        }

        var result = new PackagePublishResult(packageName, entryCount, zipData.Length);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
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
