namespace Celbridge.WorkspaceUI.Views.Controls;

/// <summary>
/// The icon strip down the left of the workspace, holding every rail button in one top-aligned stack.
/// </summary>
public sealed partial class UtilityRail : UserControl
{
    // The gap that sets one group of rail buttons apart from the group above it.
    private const double SpacerHeight = 16;

    public UtilityRail()
    {
        this.InitializeComponent();

        // The rail sizes itself, so the column hosting it does not set a width.
        Width = WorkspaceConstants.UtilityRailWidth;
    }

    /// <summary>
    /// Removes every button from the rail, ahead of a rebuild.
    /// </summary>
    internal void ClearButtons()
    {
        RailItems.Children.Clear();
    }

    /// <summary>
    /// Appends one visual group of buttons, drawing a gap between it and the buttons already on the rail.
    /// An empty group adds nothing, so a gap can never lead the rail or double up.
    /// </summary>
    internal void AddButtonGroup(IReadOnlyList<UtilityButton> buttons)
    {
        bool drawSpacer = RailItems.Children.Count > 0;

        foreach (var button in buttons)
        {
            // Buttons survive rebuilds, so the margin is reset on every add rather than only set when a
            // gap is drawn.
            button.Margin = new Thickness(0, drawSpacer ? SpacerHeight : 0, 0, 0);
            drawSpacer = false;

            RailItems.Children.Add(button);
        }
    }
}
