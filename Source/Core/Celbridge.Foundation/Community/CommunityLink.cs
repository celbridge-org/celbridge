using Celbridge.UserInterface;

namespace Celbridge.Community;

/// <summary>
/// A page on the Celbridge website that the user can open as a web view document from the
/// Utility Panel rail.
/// </summary>
public record CommunityLink
{
    /// <summary>
    /// Stable identifier for this link, used by IOpenCommunityLinkCommand and as the automation id
    /// stem for its rail button.
    /// </summary>
    public string LinkId { get; init; } = string.Empty;

    /// <summary>
    /// Name of the web view document that backs this link, without an extension. The host derives the
    /// full resource key from it, as "temp:{DocumentName}.webview".
    /// </summary>
    public string DocumentName { get; init; } = string.Empty;

    /// <summary>
    /// The page the document opens.
    /// </summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>
    /// Icon shown on the rail button and on the document tab.
    /// </summary>
    public IconSymbol Icon { get; init; }

    /// <summary>
    /// Resource key of the localized string naming this link, used for the rail button tooltip.
    /// </summary>
    public string TooltipKey { get; init; } = string.Empty;

    /// <summary>
    /// Spotlight landmark id of this link's rail button, following the same convention as the other rail
    /// buttons. Also the button's automation id, which the landmark must match.
    /// </summary>
    public string LandmarkId => $"{LinkId}-utility-button";
}

/// <summary>
/// The catalog of Celbridge site pages, declared once and surfaced as a group of buttons at the
/// bottom of the Utility Panel rail. The source of truth for what community links exist.
/// </summary>
public static class CommunityLinks
{
    /// <summary>
    /// The Celbridge documentation site.
    /// </summary>
    public static readonly CommunityLink Learn = new()
    {
        LinkId = "learn",
        DocumentName = "learn",
        Url = "https://celbridge-org.github.io/celbridge-docs/",
        Icon = IconSymbol.Book,
        TooltipKey = "UtilityPanel_LearnTooltip"
    };

    /// <summary>
    /// The community discussion forum.
    /// </summary>
    public static readonly CommunityLink Forum = new()
    {
        LinkId = "forum",
        DocumentName = "forum",
        Url = "https://celbridge.discourse.group/",
        Icon = IconSymbol.Chat,
        TooltipKey = "UtilityPanel_ForumTooltip"
    };

    /// <summary>
    /// Every community link, in the order its buttons appear on the rail.
    /// </summary>
    public static readonly IReadOnlyList<CommunityLink> All = new List<CommunityLink>
    {
        Learn,
        Forum
    };
}
