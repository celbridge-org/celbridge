namespace Celbridge.WorkspaceUI.Views.Controls;

/// <summary>
/// The icon strip down the left of the workspace, holding the utility surface buttons, the launcher buttons
/// below them, and the community links pinned to the bottom.
/// </summary>
public sealed partial class UtilityRail : UserControl
{
    public UtilityRail()
    {
        this.InitializeComponent();

        // The rail sizes itself, so the column hosting it does not set a width.
        Width = WorkspaceConstants.UtilityRailWidth;
    }

    /// <summary>
    /// Appends a button that selects a utility surface in the Utility Panel.
    /// </summary>
    public void AddUtilityButton(UtilityButton button)
    {
        UtilityItems.Children.Add(button);
    }

    /// <summary>
    /// Removes a utility surface button. A no-op when the button is not on the rail.
    /// </summary>
    public void RemoveUtilityButton(UtilityButton button)
    {
        UtilityItems.Children.Remove(button);
    }

    /// <summary>
    /// Appends a button that opens a document rather than selecting a utility surface.
    /// </summary>
    public void AddLauncherButton(UtilityButton button)
    {
        LauncherItems.Children.Add(button);
    }

    /// <summary>
    /// Appends a community link button to the group pinned at the bottom of the rail.
    /// </summary>
    public void AddCommunityButton(UtilityButton button)
    {
        CommunityItems.Children.Add(button);
    }
}
