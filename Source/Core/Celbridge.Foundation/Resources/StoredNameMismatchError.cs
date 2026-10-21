namespace Celbridge.Resources;

/// <summary>
/// Marks a failure for a resource key that reaches an existing item by a name other than the name stored on
/// disk. The names differ in letter case or Unicode normalization, or the key uses a Windows short name. The
/// surface that shows the failure writes the reason in the user's language, and gives the stored name when
/// there is one.
/// </summary>
public sealed class StoredNameMismatchError : Exception
{
    /// <summary>
    /// Returns the key that was refused.
    /// </summary>
    public ResourceKey Resource { get; }

    /// <summary>
    /// Returns the key with each name as it is stored on disk, or null when no stored name matches, as for a
    /// Windows short name.
    /// </summary>
    public ResourceKey? StoredResource { get; }

    public StoredNameMismatchError(ResourceKey resource, ResourceKey? storedResource)
        : base(FormatMessage(resource, storedResource))
    {
        Resource = resource;
        StoredResource = storedResource;
    }

    private static string FormatMessage(ResourceKey resource, ResourceKey? storedResource)
    {
        if (storedResource is null)
        {
            return $"Resource key '{resource}' does not match the name stored on disk.";
        }

        return $"Resource key '{resource}' does not match the name stored on disk. Canonical form is '{storedResource}'.";
    }
}
