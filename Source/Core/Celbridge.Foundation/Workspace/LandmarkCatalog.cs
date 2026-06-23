namespace Celbridge.Workspace;

/// <summary>
/// A spotlightable UI landmark. Id matches the AutomationProperties.AutomationId
/// set on the control and is the resolution key; Region is the layout region to
/// reveal before showing it, or null when the landmark is always present.
/// </summary>
public partial record class LandmarkDescriptor(string Id, LayoutRegion? Region);

/// <summary>
/// The closed-world vocabulary of UI landmarks app_spotlight can target. A control
/// is a landmark only if its AutomationId appears here.
/// </summary>
public static class LandmarkCatalog
{
    /// <summary>
    /// Every landmark in the catalog, in vocabulary order.
    /// </summary>
    public static IReadOnlyList<LandmarkDescriptor> All { get; } =
        new List<LandmarkDescriptor>
        {
            new("landmark.explorer", LayoutRegion.Primary),
            new("landmark.documents", null),
            new("landmark.console", LayoutRegion.Console),
            new("landmark.inspector", LayoutRegion.Secondary),
            new("landmark.add-file", LayoutRegion.Primary),
            new("landmark.add-folder", LayoutRegion.Primary),
            new("landmark.project-settings", LayoutRegion.Primary),
            new("landmark.activity-explorer", LayoutRegion.Primary),
            new("landmark.activity-search", LayoutRegion.Primary),
            new("landmark.search-input", LayoutRegion.Primary),
            new("landmark.console-input", LayoutRegion.Console),
            new("landmark.console-maximize", LayoutRegion.Console),
            new("landmark.document-tabs", null),
            new("landmark.split-editor", null),
            new("landmark.settings", null),
            new("landmark.toggle-explorer", null),
            new("landmark.toggle-console", null),
            new("landmark.toggle-inspector", null),
        };
}
