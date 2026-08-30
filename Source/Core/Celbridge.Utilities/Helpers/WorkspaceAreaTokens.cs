using Celbridge.Workspace;

namespace Celbridge.Utilities;

// The wire tokens for a WorkspaceArea, as they appear in manifests, in stored layout data, and in the
// utility MCP tools. The token text is part of those formats and does not follow enum member renames.
public static class WorkspaceAreaTokens
{
    public const string Utility = "utility";
    public const string Main = "main";
    public const string Bottom = "bottom";
    public const string Side = "side";

    /// <summary>
    /// The token naming the given area.
    /// </summary>
    public static string ToToken(this WorkspaceArea area)
    {
        switch (area)
        {
            case WorkspaceArea.Utility:
                return Utility;

            case WorkspaceArea.Main:
                return Main;

            case WorkspaceArea.Bottom:
                return Bottom;

            case WorkspaceArea.Side:
                return Side;

            default:
                // These tokens are a wire format, so an unmapped area would be persisted as a real area
                // that means something else.
                throw new NotSupportedException($"No token is defined for the '{area}' workspace area.");
        }
    }

    /// <summary>
    /// Parses an area token, returning false when the token names no area.
    /// </summary>
    public static bool TryParse(string? token, out WorkspaceArea area)
    {
        switch (token)
        {
            case Utility:
                area = WorkspaceArea.Utility;
                return true;

            case Main:
                area = WorkspaceArea.Main;
                return true;

            case Bottom:
                area = WorkspaceArea.Bottom;
                return true;

            case Side:
                area = WorkspaceArea.Side;
                return true;

            default:
                area = WorkspaceArea.Utility;
                return false;
        }
    }
}
