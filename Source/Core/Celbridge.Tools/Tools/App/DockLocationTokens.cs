namespace Celbridge.Tools;

// The wire tokens the utility MCP tools use for a DockLocation. Kept explicit rather than derived from the enum
// member names (UtilityPanel and Document differ from "panel" and "document") so serialize and parse share one
// source of truth and a code-side enum rename cannot silently change the tool API.
internal static class DockLocationTokens
{
    public const string Panel = "panel";
    public const string Document = "document";

    public static string ToToken(DockLocation location)
    {
        return location == DockLocation.Document ? Document : Panel;
    }

    public static bool TryParse(string? token, out DockLocation location)
    {
        if (string.Equals(token, Document, StringComparison.OrdinalIgnoreCase))
        {
            location = DockLocation.Document;
            return true;
        }

        location = DockLocation.UtilityPanel;
        return string.Equals(token, Panel, StringComparison.OrdinalIgnoreCase);
    }
}
