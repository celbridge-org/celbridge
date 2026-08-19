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
            new("explorer-panel", WorkspaceSurface.UtilityPanel),
            new("documents-panel", null),
            new("main-area", null),
            new("bottom-area", WorkspaceSurface.BottomArea),
            new("side-area", WorkspaceSurface.SideArea),
            new("new-file-button", WorkspaceSurface.UtilityPanel),
            new("new-folder-button", WorkspaceSurface.UtilityPanel),
            new("collapse-folders-button", WorkspaceSurface.UtilityPanel),
            new("explorer-utility-button", WorkspaceSurface.UtilityPanel),
            new("search-utility-button", WorkspaceSurface.UtilityPanel),
            new("project-settings-utility-button", WorkspaceSurface.UtilityPanel),
            new("search-input", WorkspaceSurface.UtilityPanel),
            new("search-run-button", WorkspaceSurface.UtilityPanel),
            new("search-history-button", WorkspaceSurface.UtilityPanel),
            new("search-match-case-button", WorkspaceSurface.UtilityPanel),
            new("search-whole-word-button", WorkspaceSurface.UtilityPanel),
            new("search-collapse-results-button", WorkspaceSurface.UtilityPanel),
            new("search-replace-toggle-button", WorkspaceSurface.UtilityPanel),
            new("search-replace-input", WorkspaceSurface.UtilityPanel),
            new("search-replace-history-button", WorkspaceSurface.UtilityPanel),
            new("search-replace-all-button", WorkspaceSurface.UtilityPanel),
            new("document-tab-strip", null),
            new("bottom-area-close-button", WorkspaceSurface.BottomArea),
            new("side-area-close-button", WorkspaceSurface.SideArea),
            new("home-button", null),
            new("community-button", null),
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
