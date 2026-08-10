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
            new("explorer-panel", LayoutRegion.UtilityPanel),
            new("documents-panel", null),
            new("main-area", null),
            new("bottom-area", LayoutRegion.BottomArea),
            new("side-area", LayoutRegion.SideArea),
            new("new-file-button", LayoutRegion.UtilityPanel),
            new("new-folder-button", LayoutRegion.UtilityPanel),
            new("collapse-folders-button", LayoutRegion.UtilityPanel),
            new("explorer-utility-button", LayoutRegion.UtilityPanel),
            new("search-utility-button", LayoutRegion.UtilityPanel),
            new("project-settings-utility-button", LayoutRegion.UtilityPanel),
            new("search-input", LayoutRegion.UtilityPanel),
            new("search-run-button", LayoutRegion.UtilityPanel),
            new("search-history-button", LayoutRegion.UtilityPanel),
            new("search-match-case-button", LayoutRegion.UtilityPanel),
            new("search-whole-word-button", LayoutRegion.UtilityPanel),
            new("search-collapse-results-button", LayoutRegion.UtilityPanel),
            new("search-replace-toggle-button", LayoutRegion.UtilityPanel),
            new("search-replace-input", LayoutRegion.UtilityPanel),
            new("search-replace-history-button", LayoutRegion.UtilityPanel),
            new("search-replace-all-button", LayoutRegion.UtilityPanel),
            new("document-tab-strip", null),
            new("main-area-split-button", null),
            new("bottom-area-split-button", LayoutRegion.BottomArea),
            new("bottom-area-close-button", LayoutRegion.BottomArea),
            new("side-area-split-button", LayoutRegion.SideArea),
            new("side-area-close-button", LayoutRegion.SideArea),
            new("home-button", null),
            new("community-button", null),
            new("workspace-button", null),
            new("panel-layout-button", null),
            new("settings-button", null),
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
