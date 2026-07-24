using Celbridge.Utilities;
using Tomlyn;
using Tomlyn.Model;

namespace Celbridge.Projects.Services;

/// <summary>
/// Reads a page manifest (pages.toml): the required [publish].path that names the served path of a page's
/// static content. Discovery of pages.toml files is left to the consumer; this only parses one manifest.
/// </summary>
public static class PageManifestParser
{
    private const string PublishSectionName = "publish";
    private const string PathKey = "path";

    /// <summary>
    /// Reads the [publish].path from a page manifest file, using the filesystem gateway.
    /// </summary>
    public static Result<string> ParsePublishPathFromFile(string manifestFilePath)
    {
        // Static class cannot receive DI, so fall back to the service locator to acquire the gateway.
        var fileSystem = ServiceLocator.AcquireService<ILocalFileSystem>();

        var readResult = SyncRunner.Run(() => fileSystem.ReadAllTextAsync(manifestFilePath));
        if (readResult.IsFailure)
        {
            return Result<string>.Fail($"Failed to read page manifest: {manifestFilePath}")
                .WithErrors(readResult);
        }

        return ParsePublishPath(readResult.Value);
    }

    /// <summary>
    /// Reads the [publish].path from page manifest TOML text.
    /// </summary>
    public static Result<string> ParsePublishPath(string tomlText)
    {
        TomlTable? tomlTable;
        try
        {
            tomlTable = TomlSerializer.Deserialize<TomlTable>(tomlText);
        }
        catch (TomlException exception)
        {
            return Result<string>.Fail($"Invalid TOML in page manifest: {exception.Message}");
        }

        if (tomlTable is null)
        {
            return Result<string>.Fail("Page manifest is empty or not a valid TOML table.");
        }

        if (!tomlTable.TryGetValue(PublishSectionName, out var publishObject)
            || publishObject is not TomlTable publishTable)
        {
            return Result<string>.Fail("Page manifest is missing the required [publish] section.");
        }

        if (!publishTable.TryGetValue(PathKey, out var pathObject)
            || pathObject is not string pathValue
            || string.IsNullOrWhiteSpace(pathValue))
        {
            return Result<string>.Fail("Page manifest is missing a required 'path' field in the [publish] section.");
        }

        return pathValue.Trim();
    }
}
