using Celbridge.Documents;

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
    /// Parses a section name, returning false when the name does not match a section. Used for stored
    /// addresses and for agent tool arguments, both of which carry the name rather than a number.
    /// </summary>
    public static bool TryParseSection(string? name, out DocumentSection section)
    {
        return Enum.TryParse(name, ignoreCase: true, out section)
            && Enum.IsDefined(section);
    }
}
