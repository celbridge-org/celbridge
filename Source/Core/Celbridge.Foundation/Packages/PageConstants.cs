namespace Celbridge.Packages;

/// <summary>
/// Well-known names shared across the pages subsystem. Pages are decoupled from
/// packages: a page is a folder of static web content published to a public URL,
/// not a versioned artifact.
/// </summary>
public static class PageConstants
{
    /// <summary>
    /// File name of the page manifest at the root of a page folder.
    /// </summary>
    public const string ManifestFileName = "pages.toml";

    /// <summary>
    /// Default publish-source folder for pages, relative to the project root.
    /// </summary>
    public const string DefaultPagesFolder = "pages";
}
