using Celbridge.Workspace;

namespace Celbridge.Settings;

/// <summary>
/// The catalog of every setting in the application, declared once and grouped by
/// domain. The source of truth for what settings exist. Each descriptor carries
/// the setting's key, scope, and default.
/// </summary>
public static class SettingCatalog
{
    /// <summary>
    /// Application-wide options surfaced on the Settings page.
    /// </summary>
    public static class Application
    {
        public static readonly SettingDescriptor<ApplicationColorTheme> Theme =
            new("Application.Theme", SettingScope.Application, ApplicationColorTheme.System);
    }

    /// <summary>
    /// Saved window geometry for non-maximized windowed mode.
    /// </summary>
    public static class Window
    {
        public static readonly SettingDescriptor<bool> UsePreferredGeometry =
            new("Window.UsePreferredGeometry", SettingScope.Application, false);

        public static readonly SettingDescriptor<bool> IsMaximized =
            new("Window.IsMaximized", SettingScope.Application, false);

        public static readonly SettingDescriptor<int> PreferredX =
            new("Window.PreferredX", SettingScope.Application, 0);

        public static readonly SettingDescriptor<int> PreferredY =
            new("Window.PreferredY", SettingScope.Application, 0);

        public static readonly SettingDescriptor<int> PreferredWidth =
            new("Window.PreferredWidth", SettingScope.Application, 0);

        public static readonly SettingDescriptor<int> PreferredHeight =
            new("Window.PreferredHeight", SettingScope.Application, 0);
    }

    /// <summary>
    /// Panel and surface layout state. Workspace-scoped, so each project
    /// remembers its own panel layout.
    /// </summary>
    public static class Layout
    {
        // The settings store serializes an enum by its integer value, so a change to the WorkspaceSurface
        // flags needs a new key name. Reusing one reads stale data into the wrong surfaces.
        public static readonly SettingDescriptor<WorkspaceSurface> PreferredSurfaceVisibility =
            new("Layout.VisibleSurfaces", SettingScope.Workspace, WorkspaceSurface.All);

        public static readonly SettingDescriptor<float> UtilityPanelWidth =
            new("Layout.UtilityPanelWidth", SettingScope.Workspace, WorkspaceConstants.UtilityPanelWidth);

        public static readonly SettingDescriptor<float> SideAreaWidth =
            new("Layout.SideAreaWidth", SettingScope.Workspace, WorkspaceConstants.SideAreaWidth);

        public static readonly SettingDescriptor<float> BottomAreaHeight =
            new("Layout.BottomAreaHeight", SettingScope.Workspace, WorkspaceConstants.BottomAreaHeight);

        // The utility id of the active rail surface (e.g. "celbridge.explorer" or a custom id). Restored on
        // load, falling back to Explorer when the persisted id no longer resolves to a rail item.
        public static readonly SettingDescriptor<string> UtilityPanelSelectedUtility =
            new("Layout.UtilityPanelSelectedUtility", SettingScope.Workspace, "");

        // The Project Settings section the user last viewed, as a stable section key. Restored when the
        // Project Settings panel is rebuilt, so a reload returns to the same section.
        public static readonly SettingDescriptor<string> ProjectSettingsSelectedSection =
            new("Layout.ProjectSettingsSelectedSection", SettingScope.Workspace, "");
    }

    /// <summary>
    /// Search panel options. Workspace-scoped, so each project remembers its own
    /// search panel state.
    /// </summary>
    public static class Search
    {
        public static readonly SettingDescriptor<bool> MatchCase =
            new("Search.MatchCase", SettingScope.Workspace, false);

        public static readonly SettingDescriptor<bool> WholeWord =
            new("Search.WholeWord", SettingScope.Workspace, false);

        public static readonly SettingDescriptor<bool> ReplaceMode =
            new("Search.ReplaceMode", SettingScope.Workspace, false);
    }

    /// <summary>
    /// Document editor preferences and history. The previous new-file extension
    /// is Workspace-scoped, so each project remembers the last file type the
    /// user created in it.
    /// </summary>
    public static class Editor
    {
        public static readonly SettingDescriptor<string> PreviousNewFileExtension =
            new("Editor.PreviousNewFileExtension", SettingScope.Workspace, ".py");
    }

    /// <summary>
    /// Project history. These belong to the user and installation, not to any
    /// single project, so they stay Application-scoped.
    /// </summary>
    public static class Project
    {
        public static readonly SettingDescriptor<string> PreviousProject =
            new("Project.PreviousProject", SettingScope.Application, "");

        public static readonly SettingDescriptor<List<string>> RecentProjects =
            new("Project.RecentProjects", SettingScope.Application, new List<string>());

        public static readonly SettingDescriptor<string> PreviousNewProjectFolderPath =
            new("Project.PreviousNewProjectFolderPath", SettingScope.Application, "");

        public static readonly SettingDescriptor<string> PreviousNewProjectTemplateName =
            new("Project.PreviousNewProjectTemplateName", SettingScope.Application, "");
    }

    /// <summary>
    /// Workshop connection. The URL, Author, and key hint are non-secret and
    /// stored in the clear. The key itself is Protected, transparently encrypted
    /// and decrypted at the service.
    /// </summary>
    public static class Workshop
    {
        public static readonly SettingDescriptor<string> Url =
            new("Workshop.Url", SettingScope.Application, "");

        public static readonly SettingDescriptor<string> Author =
            new("Workshop.Author", SettingScope.Application, "");

        public static readonly SettingDescriptor<string> Key =
            new("Workshop.Key", SettingScope.Protected, "");

        public static readonly SettingDescriptor<string> KeyHint =
            new("Workshop.KeyHint", SettingScope.Application, "");
    }
}
