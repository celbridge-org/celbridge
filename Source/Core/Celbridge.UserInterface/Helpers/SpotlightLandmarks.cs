using Celbridge.Workspace;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// The built-in spotlightable landmarks, seeded into the registry at startup. Each id equals the
/// AutomationId on the control. Packages register their own landmarks in addition to these.
/// </summary>
internal static class SpotlightLandmarks
{
    private static readonly IReadOnlyList<LandmarkDescriptor> BuiltInLandmarks =
        new List<LandmarkDescriptor>
        {
            new("explorer-panel", WorkspaceArea.Utility),
            new("documents-panel", null),
            new("main-area", null),
            new("bottom-area", WorkspaceArea.Bottom),
            new("side-area", WorkspaceArea.Side),
            new("new-file-button", WorkspaceArea.Utility),
            new("new-folder-button", WorkspaceArea.Utility),
            new("collapse-folders-button", WorkspaceArea.Utility),
            new("explorer-utility-button", null),
            new("search-utility-button", null),
            new("project-settings-utility-button", null),
            new("workshop-utility-button", null),
            new("search-input", WorkspaceArea.Utility),
            new("search-run-button", WorkspaceArea.Utility),
            new("search-history-button", WorkspaceArea.Utility),
            new("search-match-case-button", WorkspaceArea.Utility),
            new("search-whole-word-button", WorkspaceArea.Utility),
            new("search-collapse-results-button", WorkspaceArea.Utility),
            new("search-replace-toggle-button", WorkspaceArea.Utility),
            new("search-replace-input", WorkspaceArea.Utility),
            new("search-replace-history-button", WorkspaceArea.Utility),
            new("search-replace-all-button", WorkspaceArea.Utility),
            new("document-tab-strip", null),
            new("bottom-area-close-button", WorkspaceArea.Bottom),
            new("side-area-close-button", WorkspaceArea.Side),
            new("workspace-button", null),
            new("panel-layout-button", null),
            new("explorer-toggle-button", null),
            new("bottom-area-toggle-button", null),
            new("side-area-toggle-button", null),
        };

    public static void Seed(ISpotlightRegistry registry)
    {
        foreach (var landmark in BuiltInLandmarks)
        {
            registry.RegisterLandmark(landmark);
        }
    }
}
