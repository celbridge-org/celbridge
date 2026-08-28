using Celbridge.Documents;

namespace Celbridge.Utilities;

// The wire tokens for a DocumentSection, used in stored layout data and in the document MCP tools. Each
// token joins its area's token to the side within that area, so a section token always begins with the
// token of the area holding it. Kept explicit for the same reason as WorkspaceAreaTokens.
public static class DocumentSectionTokens
{
    public const string MainLeft = "main_left";
    public const string MainRight = "main_right";
    public const string BottomLeft = "bottom_left";
    public const string BottomRight = "bottom_right";
    public const string SideTop = "side_top";
    public const string SideBottom = "side_bottom";

    /// <summary>
    /// Every section token, in the same order as DocumentLayoutHelper.AllSections.
    /// </summary>
    public static readonly IReadOnlyList<string> AllTokens =
    [
        MainLeft,
        MainRight,
        BottomLeft,
        BottomRight,
        SideTop,
        SideBottom
    ];

    /// <summary>
    /// The token naming the given section.
    /// </summary>
    public static string ToToken(this DocumentSection section)
    {
        switch (section)
        {
            case DocumentSection.MainRight:
                return MainRight;

            case DocumentSection.BottomLeft:
                return BottomLeft;

            case DocumentSection.BottomRight:
                return BottomRight;

            case DocumentSection.SideTop:
                return SideTop;

            case DocumentSection.SideBottom:
                return SideBottom;

            default:
                return MainLeft;
        }
    }

    /// <summary>
    /// Parses a section token, returning false when the token names no section.
    /// </summary>
    public static bool TryParse(string? token, out DocumentSection section)
    {
        switch (token)
        {
            case MainLeft:
                section = DocumentSection.MainLeft;
                return true;

            case MainRight:
                section = DocumentSection.MainRight;
                return true;

            case BottomLeft:
                section = DocumentSection.BottomLeft;
                return true;

            case BottomRight:
                section = DocumentSection.BottomRight;
                return true;

            case SideTop:
                section = DocumentSection.SideTop;
                return true;

            case SideBottom:
                section = DocumentSection.SideBottom;
                return true;

            default:
                section = DocumentSection.MainLeft;
                return false;
        }
    }
}
