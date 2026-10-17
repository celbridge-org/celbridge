namespace Celbridge.Resources;

/// <summary>
/// Marks a failure for an item that was left out because it is a link. A link is a symbolic link or a
/// junction. The surface that shows the failure writes the reason in the user's language.
/// </summary>
public sealed class LinkNotFollowedError : Exception
{
    /// <summary>
    /// Returns the path of the link that was left out.
    /// </summary>
    public string LinkPath { get; }

    public LinkNotFollowedError(string linkPath)
        : base($"'{linkPath}' is a link, and Celbridge never follows links.")
    {
        LinkPath = linkPath;
    }
}
