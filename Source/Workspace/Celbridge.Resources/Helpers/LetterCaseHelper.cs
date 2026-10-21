namespace Celbridge.Resources.Helpers;

/// <summary>
/// Compares the names of resource keys by letter case, with the platform's path comparison.
/// </summary>
internal static class LetterCaseHelper
{
    /// <summary>
    /// Returns true if two keys in one folder have names that differ only in letter case. Returns false on Linux,
    /// where such names are different items.
    /// </summary>
    public static bool DiffersOnlyInNameCase(ResourceKey key, ResourceKey otherKey)
    {
        var name = key.ResourceName;
        var otherName = otherKey.ResourceName;

        return key.GetParent() == otherKey.GetParent()
            && !string.Equals(name, otherName, StringComparison.Ordinal)
            && string.Equals(name, otherName, PathComparison.Comparison);
    }
}
