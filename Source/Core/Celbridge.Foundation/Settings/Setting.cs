using System.Reflection;
using Celbridge.Workspace;

namespace Celbridge.Settings;

/// <summary>
/// The declarations of every setting in the application, grouped by domain. This
/// is the source of truth for what settings exist; typed view facades such as
/// IEditorSettings are presentation layers over these descriptors. A descriptor's
/// scope is the only thing that decides where its value is stored, so moving a
/// setting between scopes is a one-line change here.
/// </summary>
public static class Setting
{
    /// <summary>
    /// Every declared setting descriptor, discovered from the grouped declarations
    /// below. Lets callers enumerate all settings (e.g. to reset them) without a
    /// hand-maintained list.
    /// </summary>
    public static IReadOnlyList<ISettingDescriptor> All { get; } = DiscoverAll();

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
    /// Panel and region layout state. Application-scoped today; Phase 2 of the
    /// settings refactor flips these to Workspace scope.
    /// </summary>
    public static class Layout
    {
        public static readonly SettingDescriptor<LayoutRegion> PreferredRegionVisibility =
            new("Layout.PreferredRegionVisibility", SettingScope.Application, LayoutRegion.All);

        public static readonly SettingDescriptor<float> PrimaryPanelWidth =
            new("Layout.PrimaryPanelWidth", SettingScope.Application, WorkspaceConstants.PrimaryPanelWidth);

        public static readonly SettingDescriptor<float> SecondaryPanelWidth =
            new("Layout.SecondaryPanelWidth", SettingScope.Application, WorkspaceConstants.SecondaryPanelWidth);

        public static readonly SettingDescriptor<float> ConsolePanelHeight =
            new("Layout.ConsolePanelHeight", SettingScope.Application, WorkspaceConstants.ConsolePanelHeight);

        public static readonly SettingDescriptor<float> DetailPanelHeight =
            new("Layout.DetailPanelHeight", SettingScope.Application, WorkspaceConstants.DetailPanelHeight);

        public static readonly SettingDescriptor<bool> IsConsoleMaximized =
            new("Layout.IsConsoleMaximized", SettingScope.Application, false);
    }

    /// <summary>
    /// Search panel options. Application-scoped today; Phase 2 flips these to
    /// Workspace scope.
    /// </summary>
    public static class Search
    {
        public static readonly SettingDescriptor<bool> MatchCase =
            new("Search.MatchCase", SettingScope.Application, false);

        public static readonly SettingDescriptor<bool> WholeWord =
            new("Search.WholeWord", SettingScope.Application, false);

        public static readonly SettingDescriptor<bool> ReplaceMode =
            new("Search.ReplaceMode", SettingScope.Application, false);
    }

    /// <summary>
    /// Document editor preferences and history.
    /// </summary>
    public static class Editor
    {
        public static readonly SettingDescriptor<string> PreviousNewFileExtension =
            new("Editor.PreviousNewFileExtension", SettingScope.Application, ".py");
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
    /// stored in the clear; the key itself is Protected. Get and Set on the key
    /// transparently encrypt and decrypt at the service, sourcing DPAPI entropy
    /// from the descriptor key. Rotating the entropy is a key rename
    /// (Workshop.Key to Workshop.Key.v2): old ciphertext becomes unreadable and
    /// the user re-enters the key.
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

    private static IReadOnlyList<ISettingDescriptor> DiscoverAll()
    {
        var descriptors = new List<ISettingDescriptor>();

        foreach (var groupType in typeof(Setting).GetNestedTypes())
        {
            var fields = groupType.GetFields(BindingFlags.Public | BindingFlags.Static);
            foreach (var field in fields)
            {
                if (field.GetValue(null) is ISettingDescriptor descriptor)
                {
                    descriptors.Add(descriptor);
                }
            }
        }

        return descriptors;
    }
}
