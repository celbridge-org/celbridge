namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Decides which close items a tab's context menu shows. Close to the Right and Close to the Left need a tab on that
/// side. Close Others hides when there is only one other tab, because a side's item already closes it. Close All hides
/// when the tab is alone, because Close already closes it.
/// </summary>
public readonly record struct TabCloseItems(
    bool ShowCloseOthers,
    bool ShowCloseToTheRight,
    bool ShowCloseToTheLeft,
    bool ShowCloseAll)
{
    /// <summary>
    /// Returns which close items show for the tab at tabIndex, among tabCount tabs.
    /// </summary>
    public static TabCloseItems ForTab(int tabIndex, int tabCount)
    {
        return new TabCloseItems(
            ShowCloseOthers: tabCount > 2,
            ShowCloseToTheRight: tabIndex >= 0 && tabIndex < tabCount - 1,
            ShowCloseToTheLeft: tabIndex > 0,
            ShowCloseAll: tabCount > 1);
    }
}
