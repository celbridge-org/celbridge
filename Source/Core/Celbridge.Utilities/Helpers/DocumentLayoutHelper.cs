using Celbridge.Documents;
using Celbridge.Workspace;

namespace Celbridge.Utilities;

/// <summary>
/// Maps between document areas and the sections they contain.
/// </summary>
public static class DocumentLayoutHelper
{
    /// <summary>
    /// Every area, in the order they read on screen.
    /// </summary>
    public static readonly IReadOnlyList<DocumentArea> AllAreas =
    [
        DocumentArea.Main,
        DocumentArea.Bottom,
        DocumentArea.Side
    ];

    /// <summary>
    /// Every section, grouped by area and ordered primary then secondary within each.
    /// </summary>
    public static readonly IReadOnlyList<DocumentSection> AllSections =
    [
        DocumentSection.MainLeft,
        DocumentSection.MainRight,
        DocumentSection.BottomLeft,
        DocumentSection.BottomRight,
        DocumentSection.SideTop,
        DocumentSection.SideBottom
    ];

    /// <summary>
    /// The section that a document opens in when the caller gives no address. Main's primary section is
    /// the one section that is always present, so an unaddressed open always has somewhere to land.
    /// </summary>
    public static readonly DocumentSection DefaultOpenSection = DocumentSection.MainLeft;

    /// <summary>
    /// The area that contains the given section.
    /// </summary>
    public static DocumentArea GetArea(this DocumentSection section)
    {
        switch (section)
        {
            case DocumentSection.MainLeft:
            case DocumentSection.MainRight:
                return DocumentArea.Main;

            case DocumentSection.BottomLeft:
            case DocumentSection.BottomRight:
                return DocumentArea.Bottom;

            default:
                return DocumentArea.Side;
        }
    }

    /// <summary>
    /// Whether the section exists only while its area is split.
    /// </summary>
    public static bool IsSecondarySection(this DocumentSection section)
    {
        return section == DocumentSection.MainRight
            || section == DocumentSection.BottomRight
            || section == DocumentSection.SideBottom;
    }

    /// <summary>
    /// The section of the area that is always present, whether or not the area is split.
    /// </summary>
    public static DocumentSection GetPrimarySection(this DocumentArea area)
    {
        switch (area)
        {
            case DocumentArea.Main:
                return DocumentSection.MainLeft;

            case DocumentArea.Bottom:
                return DocumentSection.BottomLeft;

            default:
                return DocumentSection.SideTop;
        }
    }

    /// <summary>
    /// The section of the area that is present only while it is split.
    /// </summary>
    public static DocumentSection GetSecondarySection(this DocumentArea area)
    {
        switch (area)
        {
            case DocumentArea.Main:
                return DocumentSection.MainRight;

            case DocumentArea.Bottom:
                return DocumentSection.BottomRight;

            default:
                return DocumentSection.SideBottom;
        }
    }

    /// <summary>
    /// Both of the area's sections, primary first.
    /// </summary>
    public static IReadOnlyList<DocumentSection> GetSections(this DocumentArea area)
    {
        var sections = new List<DocumentSection>
        {
            area.GetPrimarySection(),
            area.GetSecondarySection()
        };

        return sections;
    }

    /// <summary>
    /// Whether the area places its two sections side by side rather than stacking them. True for Main and
    /// Bottom, false for Side.
    /// </summary>
    public static bool SplitsHorizontally(this DocumentArea area)
    {
        return area != DocumentArea.Side;
    }

    /// <summary>
    /// Whether the area can be collapsed by the user. False for Main, which always shows.
    /// </summary>
    public static bool IsCollapsible(this DocumentArea area)
    {
        return area != DocumentArea.Main;
    }

    /// <summary>
    /// Whether the area's toolbar belongs at the leading end of its tab strip rather than the trailing end.
    /// True for Side, whose inner edge is the one facing the documents, so its collapse chevron sits there and
    /// points out towards the application edge. Bottom collapses downwards, which leaves its strip free to
    /// keep the toolbar in the conventional trailing corner.
    /// </summary>
    public static bool PlacesToolbarAtStripStart(this DocumentArea area)
    {
        return area == DocumentArea.Side;
    }

    /// <summary>
    /// The workspace surface the area occupies, or None for Main, which is always visible and has no
    /// surface of its own.
    /// </summary>
    public static WorkspaceSurface GetSurface(this DocumentArea area)
    {
        switch (area)
        {
            case DocumentArea.Bottom:
                return WorkspaceSurface.BottomArea;

            case DocumentArea.Side:
                return WorkspaceSurface.SideArea;

            default:
                return WorkspaceSurface.None;
        }
    }

    /// <summary>
    /// The document area occupying the surface, or null for a surface that holds no area.
    /// </summary>
    public static DocumentArea? GetArea(this WorkspaceSurface surface)
    {
        switch (surface)
        {
            case WorkspaceSurface.BottomArea:
                return DocumentArea.Bottom;

            case WorkspaceSurface.SideArea:
                return DocumentArea.Side;

            default:
                return null;
        }
    }

    /// <summary>
    /// Parses a section name, returning false when the name does not match a section. Used for stored
    /// addresses and for agent tool arguments, both of which carry the name rather than a number.
    /// </summary>
    public static bool TryParseSection(string? name, out DocumentSection section)
    {
        return Enum.TryParse(name, ignoreCase: true, out section)
            && Enum.IsDefined(section);
    }
}
