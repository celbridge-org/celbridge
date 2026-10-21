namespace Celbridge.Resources.Helpers;

/// <summary>
/// Compares the names of resource keys by letter case. A volume that compares names exactly treats names that
/// differ in case as different names.
/// </summary>
internal static class LetterCaseHelper
{
    /// <summary>
    /// Returns true if the two keys are in the same folder and their names differ only in letter case, on a volume
    /// that ignores case.
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
