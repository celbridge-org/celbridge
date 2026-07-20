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
            new("explorer-panel", LayoutRegion.Primary),
            new("documents-panel", null),
            new("console-panel", LayoutRegion.Console),
            new("inspector-panel", LayoutRegion.Secondary),
            new("new-file-button", LayoutRegion.Primary),
            new("new-folder-button", LayoutRegion.Primary),
            new("collapse-folders-button", LayoutRegion.Primary),
            new("explorer-utility-button", LayoutRegion.Primary),
            new("search-utility-button", LayoutRegion.Primary),
            new("project-settings-utility-button", LayoutRegion.Primary),
            new("search-input", LayoutRegion.Primary),
            new("search-run-button", LayoutRegion.Primary),
            new("search-history-button", LayoutRegion.Primary),
            new("search-match-case-button", LayoutRegion.Primary),
            new("search-whole-word-button", LayoutRegion.Primary),
            new("search-collapse-results-button", LayoutRegion.Primary),
            new("search-replace-toggle-button", LayoutRegion.Primary),
            new("search-replace-input", LayoutRegion.Primary),
            new("search-replace-history-button", LayoutRegion.Primary),
            new("search-replace-all-button", LayoutRegion.Primary),
            new("console-input", LayoutRegion.Console),
            new("console-maximize-button", LayoutRegion.Console),
            new("document-tab-strip", null),
            new("split-editor-button", null),
            new("home-button", null),
            new("community-button", null),
            new("workspace-button", null),
            new("panel-layout-button", null),
            new("settings-button", null),
            new("explorer-toggle-button", null),
            new("console-toggle-button", null),
            new("inspector-toggle-button", null),
        };

    public static void Seed(ISpotlightRegistry registry)
    {
        foreach (var landmark in BuiltInLandmarks)
        {
            registry.RegisterLandmark(landmark);
        }
    }
}
