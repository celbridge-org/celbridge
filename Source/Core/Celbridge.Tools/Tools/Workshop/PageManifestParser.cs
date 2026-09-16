using Tomlyn;
using Tomlyn.Model;

namespace Celbridge.Tools;

/// <summary>
/// Reads a page manifest (pages.toml): the required [publish].path that names the served path of a page's
/// static content. Discovery of pages.toml files is left to the consumer, and this only parses one manifest.
/// </summary>
internal static class PageManifestParser
{
    private const string PublishSectionName = "publish";
    private const string PathKey = "path";

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
