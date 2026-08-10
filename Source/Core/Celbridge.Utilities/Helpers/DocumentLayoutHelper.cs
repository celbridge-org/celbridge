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
    public static readonly IReadOnlyList<DocumentSectionId> AllSections =
    [
        DocumentSectionId.MainLeft,
        DocumentSectionId.MainRight,
        DocumentSectionId.BottomLeft,
        DocumentSectionId.BottomRight,
        DocumentSectionId.SideTop,
        DocumentSectionId.SideBottom
    ];

    /// <summary>
    /// The area that contains the given section.
    /// </summary>
    public static DocumentArea GetArea(this DocumentSectionId section)
    {
        switch (section)
        {
            case DocumentSectionId.MainLeft:
            case DocumentSectionId.MainRight:
                return DocumentArea.Main;

            case DocumentSectionId.BottomLeft:
            case DocumentSectionId.BottomRight:
                return DocumentArea.Bottom;

            default:
                return DocumentArea.Side;
        }
    }

    /// <summary>
    /// Whether the section exists only while its area is split.
    /// </summary>
    public static bool IsSecondarySection(this DocumentSectionId section)
    {
        return section == DocumentSectionId.MainRight
            || section == DocumentSectionId.BottomRight
            || section == DocumentSectionId.SideBottom;
    }

    /// <summary>
    /// The section of the area that is always present, whether or not the area is split.
    /// </summary>
    public static DocumentSectionId GetPrimarySection(this DocumentArea area)
    {
        switch (area)
        {
            case DocumentArea.Main:
                return DocumentSectionId.MainLeft;

            case DocumentArea.Bottom:
                return DocumentSectionId.BottomLeft;

            default:
                return DocumentSectionId.SideTop;
        }
    }

    /// <summary>
    /// The section of the area that is present only while it is split.
    /// </summary>
    public static DocumentSectionId GetSecondarySection(this DocumentArea area)
    {
        switch (area)
        {
            case DocumentArea.Main:
                return DocumentSectionId.MainRight;

            case DocumentArea.Bottom:
                return DocumentSectionId.BottomRight;

            default:
                return DocumentSectionId.SideBottom;
        }
    }

    /// <summary>
    /// Both of the area's sections, primary first.
    /// </summary>
    public static IReadOnlyList<DocumentSectionId> GetSections(this DocumentArea area)
    {
        var sections = new List<DocumentSectionId>
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
    public static bool TryParseSection(string? name, out DocumentSectionId section)
    {
        return Enum.TryParse(name, ignoreCase: true, out section)
            && Enum.IsDefined(section);
    }
}
