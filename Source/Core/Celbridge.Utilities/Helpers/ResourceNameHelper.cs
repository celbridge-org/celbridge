using Celbridge.Resources;

namespace Celbridge.Utilities;

/// <summary>
/// Helpers for working with resource names. Currently provides duplicate-name
/// generation shared by the silent and dialog duplicate paths so both follow
/// the same auto-naming convention.
/// </summary>
public static class ResourceNameHelper
{
    // Bounded so a pathological folder full of "X - Copy (N)" entries can't
    // spin the search loop indefinitely. 1000 attempts covers any realistic
    // ceiling and still surfaces a clean failure if exceeded.
    private const int MaxNameCollisionAttempts = 1000;

    /// <summary>
    /// Generates a unique destination key in the same folder as the source by
    /// trying "<base> - Copy<ext>" first, then "<base> - Copy (2)<ext>",
    /// "<base> - Copy (3)<ext>", etc. until an unused name is found. Returns
    /// a failure Result when MaxNameCollisionAttempts is exhausted (rare in
    /// practice; would require a folder containing 1000 existing copies of
    /// the same source name).
    ///
    /// A dot at the very start of the name is treated as part of the basename
    /// (so ".gitignore" → ".gitignore - Copy" rather than " - Copy.gitignore").
    /// </summary>
    public static Result<ResourceKey> GenerateUniqueDuplicateKey(ResourceKey source, IResourceRegistry registry)
    {
        var parent = source.GetParent();
        var resourceName = source.ResourceName;

        int extensionIndex = resourceName.LastIndexOf('.');
        string baseName;
        string extension;
        if (extensionIndex > 0)
        {
            baseName = resourceName.Substring(0, extensionIndex);
            extension = resourceName.Substring(extensionIndex);
        }
        else
        {
            baseName = resourceName;
            extension = string.Empty;
        }

        var firstCandidate = parent.Combine($"{baseName} - Copy{extension}");
        if (registry.GetResource(firstCandidate).IsFailure)
        {
            return firstCandidate;
        }

        for (int attempt = 2; attempt <= MaxNameCollisionAttempts; attempt++)
        {
            var candidate = parent.Combine($"{baseName} - Copy ({attempt}){extension}");
            if (registry.GetResource(candidate).IsFailure)
            {
                return candidate;
            }
        }

        return Result<ResourceKey>.Fail(
            $"Could not generate a unique duplicate name for '{source}' after {MaxNameCollisionAttempts} attempts.");
    }
}
