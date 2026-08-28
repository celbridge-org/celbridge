namespace Celbridge.Workshop;

/// <summary>
/// A section of the Celbridge site, shown as a bookmark in the Workshop document's bookmarks bar.
/// </summary>
public sealed record WorkshopSection
{
    /// <summary>
    /// The address the bookmark navigates to.
    /// </summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>
    /// Resource key of the localized string naming this section, which becomes the bookmark's label.
    /// </summary>
    public string NameKey { get; init; } = string.Empty;

    /// <summary>
    /// Prefixed icon name for the glyph shown beside the bookmark's label.
    /// </summary>
    public string IconName { get; init; } = string.Empty;
}

/// <summary>
/// The sections of the Celbridge site, declared once and seeded as the bookmarks of the Workshop
/// document. The source of truth for what the Workshop offers.
/// </summary>
public static class WorkshopSections
{
    /// <summary>
    /// The Celbridge site's landing page, which is also the Workshop document's Home target.
    /// </summary>
    public static readonly WorkshopSection Celbridge = new()
    {
        Url = "https://celbridge.org/",
        NameKey = "Workshop_Section_Celbridge",
        IconName = "bs-house"
    };

    /// <summary>
    /// The Celbridge documentation site.
    /// </summary>
    public static readonly WorkshopSection Learn = new()
    {
        Url = "https://celbridge-org.github.io/celbridge-docs/",
        NameKey = "Workshop_Section_Learn",
        IconName = "bs-book"
    };

    /// <summary>
    /// The community discussion forum.
    /// </summary>
    public static readonly WorkshopSection Forum = new()
    {
        Url = "https://celbridge.discourse.group/",
        NameKey = "Workshop_Section_Forum",
        IconName = "bs-chat-dots"
    };

    /// <summary>
    /// Every section, in the order its bookmark appears in the bookmarks bar.
    /// </summary>
    public static readonly IReadOnlyList<WorkshopSection> All =
    [
        Celbridge,
        Learn,
        Forum
    ];
}
