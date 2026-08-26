using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Explorer;
using Celbridge.Settings;
using Celbridge.UserInterface.ViewModels.Controls;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// Defines and installs Celbridge's native macOS menubar, dispatching to the same view models as the in-window
/// hamburger menu. macOS-only. Call once at startup on the UI thread.
/// </summary>
internal static class MacOSMainMenu
{
    private const long TagSettings = 1;
    private const long TagQuit = 2;
    private const long TagNewProject = 3;
    private const long TagOpenProject = 4;
    private const long TagReloadProject = 5;
    private const long TagCloseProject = 6;
    private const long TagHelpWebsite = 7;
    private const long TagAbout = 8;
    private const long TagClearRecentProjects = 9;
    private const long TagNoRecentProjects = 10;
    private const long TagNewFile = 11;
    private const long TagNewFolder = 12;
    private const long TagShowLogs = 13;
    private const long TagFind = 14;
    private const long TagCheckReferences = 15;
    private const long TagLayoutDefault = 16;
    private const long TagLayoutFocus = 17;
    private const long TagLayoutPresentation = 18;
    private const long TagUtilityPanel = 19;
    private const long TagBottomArea = 20;
    private const long TagSideArea = 21;
    private const long TagResetLayout = 22;
    private const long TagThemeSystem = 23;
    private const long TagThemeLight = 24;
    private const long TagThemeDark = 25;

    // Recent project items are generated on demand, so their tags start above the fixed tags and index into
    // _recentProjectPaths, which the Open Recent submenu provider rebuilds each time the menu opens.
    private const long TagRecentProjectBase = 1000;

    private static readonly Dictionary<long, string> _recentProjectPaths = new();

    private const string WebsiteUrl = "https://celbridge.org";
    private const string GitHubUrl = "https://github.com/celbridge-org/celbridge";

    public static bool Install()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        var stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        string Text(string key) => stringLocalizer.GetString(key);

        var appMenu = new MacMenu
        {
            Title = "Celbridge",
            Items = new List<MacMenuItem>
            {
                MacMenuItem.Command(Text("Menu_About"), TagAbout),
                MacMenuItem.Separator(),
                MacMenuItem.Command(Text("MainMenu_Settings"), TagSettings, ","),
                MacMenuItem.Separator(),
                MacMenuItem.Selector(Text("Menu_Hide"), "hide:", "h"),
                MacMenuItem.Selector(Text("Menu_HideOthers"), "hideOtherApplications:", "h", MacKeyModifier.Command | MacKeyModifier.Option),
                MacMenuItem.Selector(Text("Menu_ShowAll"), "unhideAllApplications:"),
                MacMenuItem.Separator(),
                MacMenuItem.Command(Text("Menu_Quit"), TagQuit, "q")
            }
        };

        var fileMenu = new MacMenu
        {
            Title = Text("Menu_File"),
            Items = new List<MacMenuItem>
            {
                MacMenuItem.Command(Text("MainMenu_NewProject"), TagNewProject, "n", MacKeyModifier.Command | MacKeyModifier.Shift),
                MacMenuItem.Command(Text("MainMenu_NewFile"), TagNewFile, "n"),
                MacMenuItem.Command(Text("MainMenu_NewFolder"), TagNewFolder),
                MacMenuItem.Separator(),
                MacMenuItem.Command(Text("MainMenu_OpenProject"), TagOpenProject, "o"),
                MacMenuItem.Submenu(Text("MainMenu_OpenRecent"), BuildRecentProjectItems),
                MacMenuItem.Separator(),
                MacMenuItem.Command(Text("MainMenu_ReloadProject"), TagReloadProject),
                MacMenuItem.Command(Text("MainMenu_CloseProject"), TagCloseProject),
                MacMenuItem.Separator(),
                // Scans the project for project: references that no longer resolve and opens the
                // findings as a report.
                MacMenuItem.Command(Text("MainMenu_CheckReferences"), TagCheckReferences),
                // Reveals the current run's log file in the file manager.
                MacMenuItem.Command(Text("MainMenu_ShowLog"), TagShowLogs)
            }
        };

        // The Edit items are responder-chain Selector items (cut:/copy:/paste:/selectAll:/undo:/redo:).
        // AppKit auto-enables each only when a responder in the chain handles it and routes the action
        // there, so they target whatever native view holds focus: a hosted WKWebView editor or the project
        // HTML viewer's form fields. Managed Uno panels (Explorer, Search) are painted on the Skia
        // canvas and are not AppKit responders, so the items disable there and the key equivalents fall
        // through to Uno's managed keyboard handling (the same path app-global undo/redo already uses).
        var editMenu = new MacMenu
        {
            Title = Text("Menu_Edit"),
            Items = new List<MacMenuItem>
            {
                MacMenuItem.Selector(Text("Menu_Undo"), "undo:", "z"),
                MacMenuItem.Selector(Text("Menu_Redo"), "redo:", "z", MacKeyModifier.Command | MacKeyModifier.Shift),
                MacMenuItem.Separator(),
                MacMenuItem.Selector(Text("Menu_Cut"), "cut:", "x"),
                MacMenuItem.Selector(Text("Menu_Copy"), "copy:", "c"),
                MacMenuItem.Selector(Text("Menu_Paste"), "paste:", "v"),
                MacMenuItem.Selector(Text("Menu_SelectAll"), "selectAll:", "a"),
                MacMenuItem.Separator(),
                // Find is a Command item (not a responder-chain Selector): it targets the active document's
                // host find bar. It disables when the active document has none (e.g. the Monaco code editor),
                // so Cmd+F falls through the responder chain and Monaco keeps its own find widget.
                MacMenuItem.Command(Text("Menu_Find"), TagFind, "f")
            }
        };

        // The layout modes carry check marks rather than being separate enter and exit commands, so
        // Presentation mode always has a visible way out here. The in-window reveal strip cannot serve as
        // one on macOS, because the menu bar and title bar auto-reveal over it in native fullscreen.
        var viewMenu = new MacMenu
        {
            Title = Text("Menu_View"),
            Items = new List<MacMenuItem>
            {
                MacMenuItem.Command(Text("LayoutToolbar_DefaultLabel"), TagLayoutDefault),
                MacMenuItem.Command(Text("LayoutToolbar_FocusLabel"), TagLayoutFocus),
                MacMenuItem.Command(Text("LayoutToolbar_PresentationLabel"), TagLayoutPresentation),
                MacMenuItem.Separator(),
                MacMenuItem.Command(Text("Menu_UtilityPanel"), TagUtilityPanel),
                MacMenuItem.Command(Text("Menu_BottomArea"), TagBottomArea),
                MacMenuItem.Command(Text("Menu_SideArea"), TagSideArea),
                MacMenuItem.Separator(),
                // Full Screen and Reset Layout act on the window as a whole rather than on one
                // surface, so they group together as they do in the layout flyout. macOS owns fullscreen,
                // so it is a responder-chain Selector reaching the window rather than a command of ours.
                MacMenuItem.Selector(Text("Menu_EnterFullScreen"), "toggleFullScreen:", "f", MacKeyModifier.Command | MacKeyModifier.Control),
                MacMenuItem.Command(Text("LayoutToolbar_ResetLayoutButton"), TagResetLayout),
                MacMenuItem.Separator(),
                MacMenuItem.Submenu(Text("Menu_Theme"), BuildThemeItems)
            }
        };

        var windowMenu = new MacMenu
        {
            Title = Text("Menu_Window"),
            IsWindowMenu = true,
            Items = new List<MacMenuItem>
            {
                MacMenuItem.Selector(Text("Menu_Minimize"), "performMiniaturize:", "m"),
                MacMenuItem.Selector(Text("Menu_Zoom"), "performZoom:"),
                MacMenuItem.Separator(),
                MacMenuItem.Selector(Text("Menu_BringAllToFront"), "arrangeInFront:")
            }
        };

        var helpMenu = new MacMenu
        {
            Title = Text("Menu_Help"),
            Items = new List<MacMenuItem>
            {
                MacMenuItem.Command(Text("Menu_HelpWebsite"), TagHelpWebsite)
            }
        };

        var menus = new List<MacMenu>
        {
            appMenu,
            fileMenu,
            editMenu,
            viewMenu,
            windowMenu,
            helpMenu
        };

        return MacOSMenuInterop.Install(menus, OnCommand, QueryState);
    }

    private static IReadOnlyList<MacMenuItem> BuildRecentProjectItems()
    {
        var stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        var viewModel = ServiceLocator.AcquireService<ApplicationMenuViewModel>();
        var recentProjects = viewModel.GetRecentProjects();

        _recentProjectPaths.Clear();

        var items = new List<MacMenuItem>();

        if (recentProjects.Count == 0)
        {
            // A single disabled placeholder (greyed by QueryState) when there is no history, matching the
            // in-window menu's disabled Open Recent entry.
            var noRecentItem = MacMenuItem.Command(stringLocalizer.GetString("Menu_NoRecentProjects"), TagNoRecentProjects);
            items.Add(noRecentItem);
            return items;
        }

        long tag = TagRecentProjectBase;
        foreach (var recentProject in recentProjects)
        {
            _recentProjectPaths[tag] = recentProject.ProjectFilePath;
            var projectItem = MacMenuItem.Command(recentProject.ProjectName, tag);
            items.Add(projectItem);
            tag++;
        }

        items.Add(MacMenuItem.Separator());

        var clearItem = MacMenuItem.Command(stringLocalizer.GetString("MainMenu_ClearRecentProjects"), TagClearRecentProjects);
        items.Add(clearItem);

        return items;
    }

    private static IReadOnlyList<MacMenuItem> BuildThemeItems()
    {
        var stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        var items = new List<MacMenuItem>
        {
            MacMenuItem.Command(stringLocalizer.GetString("Theme_System"), TagThemeSystem),
            MacMenuItem.Command(stringLocalizer.GetString("Theme_Light"), TagThemeLight),
            MacMenuItem.Command(stringLocalizer.GetString("Theme_Dark"), TagThemeDark)
        };

        return items;
    }

    private static MacMenuItemState QueryState(long tag)
    {
        // The standard Edit verbs and Full Screen are responder-chain Selector items (see Install), so
        // AppKit handles their state. This only covers the Command items below.

        // An in-window dialog covers the hamburger menu but leaves the menu bar live, so every command
        // here stays pickable while one is open. Grey the whole bar out for the dialog's lifetime,
        // leaving Quit alone. AppKit re-asks on each open, so this needs no invalidation.
        if (tag != TagQuit &&
            ServiceLocator.AcquireService<IDialogService>().IsDialogOpen)
        {
            return MacMenuItemState.Disabled;
        }

        // Reload and Close act on the open project, so they are enabled only while a workspace is loaded.
        // Every other project command is always available. Mirrors the hamburger menu's gating.
        switch (tag)
        {
            case TagReloadProject:
            case TagCloseProject:
            case TagNewFile:
            case TagNewFolder:
            case TagCheckReferences:
                return WorkspaceCommandState();

            case TagFind:
                return FindCommandState();

            case TagLayoutDefault:
                return LayoutModeState(LayoutMode.Default);

            case TagLayoutFocus:
                return LayoutModeState(LayoutMode.Focus);

            case TagLayoutPresentation:
                return LayoutModeState(LayoutMode.Presentation);

            case TagUtilityPanel:
                return SurfaceState(WorkspaceSurface.UtilityPanel);

            case TagBottomArea:
                return SurfaceState(WorkspaceSurface.BottomArea);

            case TagSideArea:
                return SurfaceState(WorkspaceSurface.SideArea);

            case TagResetLayout:
                return WorkspaceCommandState();

            case TagThemeSystem:
                return ThemeState(ApplicationColorTheme.System);

            case TagThemeLight:
                return ThemeState(ApplicationColorTheme.Light);

            case TagThemeDark:
                return ThemeState(ApplicationColorTheme.Dark);

            case TagNoRecentProjects:
                return MacMenuItemState.Disabled;

            default:
                return MacMenuItemState.Enabled;
        }
    }

    private static MacMenuItemState FindCommandState()
    {
        var canFind = ActiveDocumentFind.GetActiveFindableDocument()?.CanFind ?? false;

        return canFind ? MacMenuItemState.Enabled : MacMenuItemState.Disabled;
    }

    private static MacMenuItemState WorkspaceCommandState()
    {
        var isWorkspaceLoaded = ServiceLocator.AcquireService<IWorkspaceWrapper>().IsWorkspaceLoaded;

        return isWorkspaceLoaded ? MacMenuItemState.Enabled : MacMenuItemState.Disabled;
    }

    private static MacMenuItemState LayoutModeState(LayoutMode layoutMode)
    {
        var viewModel = GetViewMenuViewModel();
        if (!viewModel.IsWorkspaceLoaded)
        {
            return MacMenuItemState.Disabled;
        }

        return MacMenuItemState.Checkable(viewModel.LayoutMode == layoutMode);
    }

    private static MacMenuItemState SurfaceState(WorkspaceSurface surface)
    {
        var viewModel = GetViewMenuViewModel();
        if (!viewModel.IsWorkspaceLoaded)
        {
            return MacMenuItemState.Disabled;
        }

        return MacMenuItemState.Checkable(viewModel.IsSurfaceVisible(surface));
    }

    private static MacMenuItemState ThemeState(ApplicationColorTheme theme)
    {
        // The theme applies to the whole application, so it stays available with no project open.
        var viewModel = GetViewMenuViewModel();

        return MacMenuItemState.Checkable(viewModel.Theme == theme);
    }

    private static void OnCommand(long tag)
    {
        // The standard Edit verbs are responder-chain Selector items handled by AppKit, so they never
        // reach this callback. Only the Command items (project, help, about) below are dispatched here.

        // The project commands run through the same view-model the hamburger menu uses, so the two menus
        // stay in lockstep. Resolved per invocation. The methods only dispatch commands or open dialogs.
        var viewModel = ServiceLocator.AcquireService<ApplicationMenuViewModel>();

        // Recent project items carry generated tags above the fixed range. Open the project they map to.
        if (tag >= TagRecentProjectBase)
        {
            if (_recentProjectPaths.TryGetValue(tag, out var recentProjectFilePath))
            {
                _ = viewModel.OpenRecentProjectAsync(recentProjectFilePath);
            }

            return;
        }

        switch (tag)
        {
            case TagAbout:
                ShowAboutPanel();
                break;

            case TagSettings:
                viewModel.ShowSettings();
                break;

            case TagQuit:
                viewModel.ExitApplication();
                break;

            case TagNewProject:
                viewModel.NewProject();
                break;

            case TagNewFile:
                ServiceLocator.AcquireService<ICommandService>().Execute<ICreateResourceDialogCommand>(command =>
                {
                    command.ResourceType = ResourceType.File;
                });
                break;

            case TagNewFolder:
                ServiceLocator.AcquireService<ICommandService>().Execute<ICreateResourceDialogCommand>(command =>
                {
                    command.ResourceType = ResourceType.Folder;
                });
                break;

            case TagOpenProject:
                viewModel.OpenProject();
                break;

            case TagReloadProject:
                viewModel.ReloadProject();
                break;

            case TagCloseProject:
                _ = viewModel.CloseProjectAsync();
                break;

            case TagShowLogs:
                viewModel.ShowLogs();
                break;

            case TagCheckReferences:
                ServiceLocator.AcquireService<ICommandService>().Execute<ICheckReferencesCommand>(command =>
                {
                    command.OpenReport = true;
                });
                break;

            case TagFind:
                ActiveDocumentFind.GetActiveFindableDocument()?.TryBeginFind();
                break;

            case TagLayoutDefault:
                GetViewMenuViewModel().SetLayoutMode(LayoutMode.Default);
                break;

            case TagLayoutFocus:
                GetViewMenuViewModel().SetLayoutMode(LayoutMode.Focus);
                break;

            case TagLayoutPresentation:
                GetViewMenuViewModel().SetLayoutMode(LayoutMode.Presentation);
                break;

            case TagUtilityPanel:
                ToggleSurface(WorkspaceSurface.UtilityPanel);
                break;

            case TagBottomArea:
                ToggleSurface(WorkspaceSurface.BottomArea);
                break;

            case TagSideArea:
                ToggleSurface(WorkspaceSurface.SideArea);
                break;

            case TagResetLayout:
                GetViewMenuViewModel().ResetLayout();
                break;

            case TagThemeSystem:
                GetViewMenuViewModel().SetTheme(ApplicationColorTheme.System);
                break;

            case TagThemeLight:
                GetViewMenuViewModel().SetTheme(ApplicationColorTheme.Light);
                break;

            case TagThemeDark:
                GetViewMenuViewModel().SetTheme(ApplicationColorTheme.Dark);
                break;

            case TagClearRecentProjects:
                viewModel.ClearRecentProjects();
                break;

            case TagHelpWebsite:
                var commandService = ServiceLocator.AcquireService<ICommandService>();
                commandService.Execute<IOpenBrowserCommand>(command => command.URL = WebsiteUrl);
                break;
        }
    }

    private static ViewMenuViewModel GetViewMenuViewModel()
    {
        return ServiceLocator.AcquireService<ViewMenuViewModel>();
    }

    private static void ToggleSurface(WorkspaceSurface surface)
    {
        var viewModel = GetViewMenuViewModel();
        var isVisible = viewModel.IsSurfaceVisible(surface);

        viewModel.SetSurfaceVisibility(surface, !isVisible);
    }

    private static void ShowAboutPanel()
    {
        var stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        var links = new List<MacAboutLink>
        {
            new(stringLocalizer.GetString("Menu_About_Website"), WebsiteUrl),
            new(stringLocalizer.GetString("Menu_About_GitHub"), GitHubUrl)
        };

        MacOSMenuInterop.ShowAboutPanel(links);
    }
}
