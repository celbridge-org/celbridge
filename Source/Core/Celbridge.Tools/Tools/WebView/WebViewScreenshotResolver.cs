using System.Globalization;
using Path = System.IO.Path;

namespace Celbridge.Tools;

/// <summary>
/// Resolves the saveTo argument of webview_screenshot into a project resource
/// key. Auto-generates a timestamped filename when saveTo identifies a folder.
/// Validates that the file extension matches the requested format.
/// </summary>
public static class WebViewScreenshotResolver
{
    /// <summary>
    /// Folder used when saveTo is given as an empty string but a save was
    /// otherwise requested (e.g. saveTo == "/" or just "/"). Callers that
    /// support an opt-out from saving should not pass an empty saveTo here.
    /// </summary>
    public const string DefaultFolder = "screenshots";

    /// <summary>
    /// Resolves a saveTo argument plus a format into a target resource key.
    /// A trailing slash, or a path with no extension, is treated as a folder
    /// reference and an auto-named file is generated inside it. Otherwise
    /// the saveTo value is used verbatim and its extension is checked
    /// against the format. The projectFolderPath is used to probe for
    /// filename collisions when generating an auto-name, so the common case
    /// (no collision) yields a clean unsuffixed filename.
    /// </summary>
    public static Result<ResourceKey> Resolve(string saveTo, string format, string projectFolderPath)
    {
        var extension = ExtensionForFormat(format);
        if (extension is null)
        {
            return Result.Fail($"Unsupported screenshot format '{format}'. Use 'jpeg' or 'png'.");
        }

        // An empty saveTo at this layer means "save into the default folder".
        // The screenshot tool itself decides whether to call the saver at all
        // based on the agent's intent (ephemeral capture vs explicit save).
        var effectiveSaveTo = string.IsNullOrEmpty(saveTo) ? DefaultFolder + "/" : saveTo;

        // Strip a single trailing slash before validating as a resource key.
        // ResourceKey rejects trailing slashes, but we want to honour the
        // "this is a folder" intent rather than failing.
        var trimmed = effectiveSaveTo.TrimEnd('/');
        var endsWithSlash = trimmed.Length != effectiveSaveTo.Length;

        if (!ResourceKey.TryCreate(trimmed, out var key))
        {
            return Result.Fail($"Invalid saveTo resource key: '{saveTo}'");
        }

        // A trailing slash means "auto-name in this folder". A path without
        // an extension is also treated as a folder, since screenshot files
        // always carry an extension.
        if (endsWithSlash || !HasExtension(key.ToString()))
        {
            var folderResourceKey = key.ToString();
            var folderAbsolutePath = ResolveAbsoluteUnderProject(projectFolderPath, folderResourceKey);
            var fileName = GenerateAutoName(extension, folderAbsolutePath);
            var combined = string.IsNullOrEmpty(folderResourceKey) ? fileName : folderResourceKey + "/" + fileName;
            if (!ResourceKey.TryCreate(combined, out var fileKey))
            {
                return Result.Fail($"Failed to construct resource key for auto-named screenshot in folder '{saveTo}'");
            }

            return fileKey;
        }

        // Treat as exact resource key path. Validate the extension matches
        // the requested format so the saved bytes are consistent with the
        // filename.
        var actualExtension = Path.GetExtension(key.ToString()).TrimStart('.').ToLowerInvariant();
        if (!ExtensionMatchesFormat(actualExtension, format))
        {
            return Result.Fail(
                $"saveTo extension '.{actualExtension}' does not match format '{format}'. " +
                $"Use a '.{extension}' extension or change the format parameter.");
        }

        return key;
    }

    private static string GenerateAutoName(string extension, string absoluteFolderPath)
    {
        // Prefer the clean unsuffixed name. In the common case (no collision)
        // the agent gets `screenshot-20260430-090238.jpg` rather than a noisy
        // sequence-suffixed variant. Only walk a sequence counter when the
        // unsuffixed name is taken — typically only when two captures happen
        // within the same wall-clock second.
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        var primary = $"screenshot-{timestamp}.{extension}";
        var primaryPath = Path.Combine(absoluteFolderPath, primary);
        if (!File.Exists(primaryPath))
        {
            return primary;
        }

        for (int seq = 1; seq <= 999; seq++)
        {
            var candidate = $"screenshot-{timestamp}-{seq}.{extension}";
            var candidatePath = Path.Combine(absoluteFolderPath, candidate);
            if (!File.Exists(candidatePath))
            {
                return candidate;
            }
        }

        // Fallback for the absurd case of >999 captures in one second. A short
        // Guid suffix never collides and keeps the call from failing.
        var fallbackSuffix = Guid.NewGuid().ToString("N").Substring(0, 8);
        return $"screenshot-{timestamp}-{fallbackSuffix}.{extension}";
    }

    private static string? ExtensionForFormat(string format)
    {
        return format.ToLowerInvariant() switch
        {
            "jpeg" => "jpg",
            "png" => "png",
            _ => null
        };
    }

    private static bool ExtensionMatchesFormat(string extension, string format)
    {
        var normalisedFormat = format.ToLowerInvariant();
        var normalisedExtension = extension.ToLowerInvariant();
        return (normalisedFormat == "jpeg" && (normalisedExtension == "jpg" || normalisedExtension == "jpeg"))
            || (normalisedFormat == "png" && normalisedExtension == "png");
    }

    private static bool HasExtension(string resourceKeyString)
    {
        var extension = Path.GetExtension(resourceKeyString);
        return !string.IsNullOrEmpty(extension);
    }

    private static string ResolveAbsoluteUnderProject(string projectFolderPath, string resourceKeyString)
    {
        if (string.IsNullOrEmpty(resourceKeyString))
        {
            return projectFolderPath;
        }

        return Path.Combine(projectFolderPath, resourceKeyString.Replace('/', Path.DirectorySeparatorChar));
    }
}
