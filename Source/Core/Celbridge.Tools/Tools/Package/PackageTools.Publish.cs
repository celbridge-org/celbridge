using System.IO.Compression;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Tomlyn;
using Tomlyn.Model;
using File = System.IO.File;
using FileAttributes = System.IO.FileAttributes;
using MemoryStream = System.IO.MemoryStream;
using Path = System.IO.Path;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by package_publish with the published package details.
/// </summary>
public record class PackagePublishResult(string PackageName, int Entries, long Size);

public partial class PackageTools
{
    private const string PackagesFolderPrefix = "packages/";
    private const string ManifestFileName = "package.toml";

    /// <summary>Publish packages/{packageName}/ to the remote registry (visible to other users).</summary>
    [McpServerTool(Name = "package_publish", Destructive = true)]
    [ToolAlias("package.publish")]
    [RelatedGuides("resource_keys", "packages_overview", "silent_vs_interactive")]
    [AllowDirectFileSystemAccess]
    public async partial Task<CallToolResult> Publish(string resource, string packageName, bool confirmWithUser = true)
    {
        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolResponse.InvalidResourceKey(resource);
        }

        if (!IsValidPackageName(packageName))
        {
            return ToolResponse.Error(
                $"Invalid package name: '{packageName}'. " +
                "Package names must be lowercase alphanumeric with hyphens, 1-214 characters.");
        }

        // Validate the resource is inside the packages folder
        var resourceString = resourceKey.ToString();
        if (!resourceString.StartsWith(PackagesFolderPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return ToolResponse.Error(
                $"Package must be inside the '{PackagesFolderPrefix}' folder. " +
                $"Expected: '{PackagesFolderPrefix}{packageName}'");
        }

        // Validate the folder name matches the package name
        var folderName = resourceString.Substring(PackagesFolderPrefix.Length).TrimEnd('/');
        if (!string.Equals(folderName, packageName, StringComparison.Ordinal))
        {
            return ToolResponse.Error(
                $"Folder name '{folderName}' does not match package name '{packageName}'. " +
                $"The package folder must be '{PackagesFolderPrefix}{packageName}'.");
        }

        var workspaceWrapper = GetRequiredService<IWorkspaceWrapper>();
        var resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var fileSystem = GetRequiredService<ILocalFileSystem>();

        var resolveSourceResult = resourceRegistry.ResolveResourcePath(resourceKey);
        if (resolveSourceResult.IsFailure)
        {
            return ToolResponse.Error(resolveSourceResult.FirstErrorMessage);
        }
        var sourcePath = resolveSourceResult.Value;

        var sourceInfoResult = await fileSystem.GetInfoAsync(sourcePath);
        if (sourceInfoResult.IsFailure
            || sourceInfoResult.Value.Kind != StorageItemKind.Folder)
        {
            return ToolResponse.Error($"Folder not found: '{resourceKey}'");
        }

        // Validate that the package manifest exists and is valid
        var manifestPath = Path.Combine(sourcePath, ManifestFileName);
        var validateResult = await ValidatePackageManifestAsync(fileSystem, manifestPath);
        if (validateResult.IsFailure)
        {
            return ToolResponse.Error(validateResult);
        }

        if (confirmWithUser)
        {
            var localizerService = GetRequiredService<Celbridge.Localization.ILocalizerService>();
            var title = localizerService.GetString("Package_PublishConfirm_Title");
            var message = localizerService.GetString("Package_PublishConfirm_Message", packageName);

            var confirmed = await ConfirmActionAsync(title, message);
            if (!confirmed)
            {
                return ToolResponse.Error("Publish cancelled by user.");
            }
        }

        int entryCount = 0;
        byte[] zipData;

        try
        {
            using var memoryStream = new MemoryStream();
            using (var zipArchive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var enumerateResult = await fileSystem.EnumerateAsync(sourcePath, "*", recursive: true);
                if (enumerateResult.IsFailure)
                {
                    return ToolResponse.Error($"Failed to enumerate package files: {enumerateResult.FirstErrorMessage}");
                }
                var filePaths = enumerateResult.Value
                    .Where(entry => !entry.IsFolder)
                    .Select(entry => entry.FullPath)
                    .ToList();

                foreach (var filePath in filePaths)
                {
                    // Reparse-point check still uses System.IO directly: file
                    // attribute introspection is outside the ILocalFileSystem gateway.
                    var fileAttributes = File.GetAttributes(filePath);
                    if (fileAttributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        continue;
                    }

                    var relativePath = Path.GetRelativePath(sourcePath, filePath);
                    var entryName = relativePath.Replace('\\', '/');

                    var entry = zipArchive.CreateEntry(entryName, CompressionLevel.Optimal);
                    using var entryStream = entry.Open();
                    var openResult = await fileSystem.OpenReadAsync(filePath);
                    if (openResult.IsFailure)
                    {
                        return ToolResponse.Error($"Failed to open file for packaging '{filePath}': {openResult.FirstErrorMessage}");
                    }
                    using var sourceStream = openResult.Value;
                    await sourceStream.CopyToAsync(entryStream);
                    entryCount++;
                }
            }

            zipData = memoryStream.ToArray();
        }
        catch (System.IO.IOException exception)
        {
            return ToolResponse.Error($"Failed to create package archive: {exception.Message}");
        }

        var packageApiClient = GetRequiredService<IPackageApiClient>();
        var fileName = $"{packageName}.zip";
        var uploadResult = await packageApiClient.UploadPackageAsync(fileName, zipData);

        if (uploadResult.IsFailure)
        {
            return ToolResponse.Error(uploadResult);
        }

        var result = new PackagePublishResult(packageName, entryCount, zipData.Length);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }

    private static async Task<Result> ValidatePackageManifestAsync(ILocalFileSystem fileSystem, string manifestPath)
    {
        var manifestInfoResult = await fileSystem.GetInfoAsync(manifestPath);
        if (manifestInfoResult.IsFailure
            || manifestInfoResult.Value.Kind != StorageItemKind.File)
        {
            return Result.Fail(
                $"Package manifest not found. Expected '{ManifestFileName}' in the package folder.");
        }

        var readResult = await fileSystem.ReadAllTextAsync(manifestPath);
        if (readResult.IsFailure)
        {
            return Result.Fail($"Failed to read package manifest: {readResult.FirstErrorMessage}");
        }
        var tomlContent = readResult.Value;

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
