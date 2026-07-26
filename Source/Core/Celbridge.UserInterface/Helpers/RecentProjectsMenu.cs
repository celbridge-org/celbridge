using Celbridge.Projects;
using Celbridge.UserInterface.Views.Controls;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Builds the shared recent-projects menu used by both the main menu's Open Recent submenu and the Current
/// Project switcher, so the two surfaces present the project list and the clear action identically.
/// </summary>
public static class RecentProjectsMenu
{
    /// <summary>
    /// Populates the target collection with one item per recent project (the name plus a right-aligned path
    /// column) followed by a separator and a Clear Recent Projects item. Call only when there is at least one
    /// recent project. onOpenProject receives the clicked project's file path; onClearRecent clears the list.
    /// </summary>
    public static void Populate(
        IList<MenuFlyoutItemBase> items,
        IReadOnlyList<RecentProject> recentProjects,
        Action<string> onOpenProject,
        string clearRecentLabel,
        Action onClearRecent)
    {
        foreach (var recentProject in recentProjects)
        {
            var projectFilePath = recentProject.ProjectFilePath;

            var projectItem = new MenuFlyoutItem
            {
                Text = recentProject.ProjectName,
                // Display-only secondary text (it registers no keyboard accelerator), reused as a right-aligned
                // path column so the list scans by name with the on-disk location shown at a glance.
                KeyboardAcceleratorTextOverride = projectFilePath
            };
            ToolTipService.SetToolTip(projectItem, projectFilePath);
            projectItem.Click += (sender, e) => onOpenProject(projectFilePath);

            items.Add(projectItem);
        }

        items.Add(new MenuFlyoutSeparator());

        var clearRecentItem = new MenuFlyoutItem
        {
            Text = clearRecentLabel,
            Icon = new Icon { Symbol = IconSymbol.Delete }
        };
        clearRecentItem.Click += (sender, e) => onClearRecent();

        items.Add(clearRecentItem);
    }
}
