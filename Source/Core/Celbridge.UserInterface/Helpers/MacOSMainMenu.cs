#if !WINDOWS
using Celbridge.Commands;
using Celbridge.Explorer;
using Celbridge.Resources;
using Celbridge.UserInterface.ViewModels.Controls;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Defines and installs Celbridge's native macOS menubar. Mirrors the in-window hamburger menu's project
/// commands (dispatched to the same MainMenuViewModel) and adds the standard App, Edit, and Window menus
/// macOS users expect. macOS-only; call once at startup on the UI thread.
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
                MacMenuItem.Command(Text("MainMenu_CloseProject"), TagCloseProject)
            }
        };

        // The Edit items are responder-chain Selector items (cut:/copy:/paste:/selectAll:/undo:/redo:).
        // AppKit auto-enables each only when a responder in the chain handles it and routes the action
        // there, so they target whatever native view holds focus: a hosted WKWebView editor or the project
        // HTML viewer's form fields. Managed Uno panels (Explorer, Inspector) are painted on the Skia
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
                MacMenuItem.Selector(Text("Menu_SelectAll"), "selectAll:", "a")
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
            windowMenu,
            helpMenu
        };

        return MacOSMenuInterop.Install(menus, OnCommand, Validate);
    }

    private static IReadOnlyList<MacMenuItem> BuildRecentProjectItems()
    {
        var stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        var viewModel = ServiceLocator.AcquireService<MainMenuViewModel>();
        var recentProjects = viewModel.GetRecentProjects();

        _recentProjectPaths.Clear();

        var items = new List<MacMenuItem>();

        if (recentProjects.Count == 0)
        {
            // A single disabled placeholder (greyed by Validate) when there is no history, matching the
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

    private static bool Validate(long tag)
    {
        // The standard Edit verbs are responder-chain Selector items (see the Edit menu in Install), so
        // AppKit handles their enable state; this validation only covers the Command items below.

        // Reload and Close act on the open project, so they are enabled only while a workspace is loaded;
        // every other command is always available. Mirrors the hamburger menu's IsWorkspaceLoaded gating.
        switch (tag)
        {
            case TagReloadProject:
            case TagCloseProject:
            case TagNewFile:
            case TagNewFolder:
                return ServiceLocator.AcquireService<IWorkspaceWrapper>().IsWorkspacePageLoaded;

            case TagNoRecentProjects:
                return false;

            default:
                return true;
        }
    }

    private static void OnCommand(long tag)
    {
        // The standard Edit verbs are responder-chain Selector items handled by AppKit, so they never
        // reach this callback; only the Command items (project, help, about) below are dispatched here.

        // The project commands run through the same view-model the hamburger menu uses, so the two menus
        // stay in lockstep. Resolved per invocation; the methods only dispatch commands or open dialogs.
        var viewModel = ServiceLocator.AcquireService<MainMenuViewModel>();

        // Recent project items carry generated tags above the fixed range; open the project they map to.
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
                viewModel.NavigateToSettings();
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
                _ = viewModel.ReloadProjectAsync();
                break;

            case TagCloseProject:
                _ = viewModel.CloseProjectAsync();
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
#endif
