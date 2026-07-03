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
            new("project-settings-button", LayoutRegion.Primary),
            new("explorer-activity-button", LayoutRegion.Primary),
            new("search-activity-button", LayoutRegion.Primary),
            new("search-input", LayoutRegion.Primary),
            new("console-input", LayoutRegion.Console),
            new("console-maximize-button", LayoutRegion.Console),
            new("document-tab-strip", null),
            new("split-editor-button", null),
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
