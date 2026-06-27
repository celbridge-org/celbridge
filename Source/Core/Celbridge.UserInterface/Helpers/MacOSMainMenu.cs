#if !WINDOWS
using Celbridge.Commands;
using Celbridge.Explorer;
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

    // Edit verbs route to the focused surface through the edit-intent command.
    private const long TagEditUndo = 9;
    private const long TagEditRedo = 10;
    private const long TagEditCut = 11;
    private const long TagEditCopy = 12;
    private const long TagEditPaste = 13;
    private const long TagEditSelectAll = 14;

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
                MacMenuItem.Command(Text("MainMenu_NewProject"), TagNewProject, "n"),
                MacMenuItem.Command(Text("MainMenu_OpenProject"), TagOpenProject, "o"),
                MacMenuItem.Separator(),
                MacMenuItem.Command(Text("MainMenu_ReloadProject"), TagReloadProject),
                MacMenuItem.Command(Text("MainMenu_CloseProject"), TagCloseProject)
            }
        };

        // The Edit items are Command items carrying the standard shortcuts. Each routes to the focused
        // surface through IPerformEditCommand, and validateMenuItem reports whether that surface can
        // perform the verb. When the focused surface cannot (a native text box, or an editor with no
        // selection), the item is disabled and AppKit lets the key equivalent fall through to the control's
        // own handling, so native and editor copy/paste/undo keep working.
        var editMenu = new MacMenu
        {
            Title = Text("Menu_Edit"),
            Items = new List<MacMenuItem>
            {
                MacMenuItem.Command(Text("Menu_Undo"), TagEditUndo, "z"),
                MacMenuItem.Command(Text("Menu_Redo"), TagEditRedo, "z", MacKeyModifier.Command | MacKeyModifier.Shift),
                MacMenuItem.Separator(),
                MacMenuItem.Command(Text("Menu_Cut"), TagEditCut, "x"),
                MacMenuItem.Command(Text("Menu_Copy"), TagEditCopy, "c"),
                MacMenuItem.Command(Text("Menu_Paste"), TagEditPaste, "v"),
                MacMenuItem.Command(Text("Menu_SelectAll"), TagEditSelectAll, "a")
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

    private static EditIntent? EditIntentForTag(long tag)
    {
        return tag switch
        {
            TagEditUndo => EditIntent.Undo,
            TagEditRedo => EditIntent.Redo,
            TagEditCut => EditIntent.Cut,
            TagEditCopy => EditIntent.Copy,
            TagEditPaste => EditIntent.Paste,
            TagEditSelectAll => EditIntent.SelectAll,
            _ => null
        };
    }

    private static bool Validate(long tag)
    {
        // An Edit verb is enabled exactly when the focused surface can perform it. When it cannot, the
        // item is disabled and AppKit lets the shortcut fall through to the control's own handling.
        var editIntent = EditIntentForTag(tag);
        if (editIntent is not null)
        {
            var focusService = ServiceLocator.AcquireService<IFocusService>();
            var target = focusService.EditTarget;
            return target is not null
                && target.CanPerformEdit(editIntent.Value);
        }

        // Reload and Close act on the open project, so they are enabled only while a workspace is loaded;
        // every other command is always available. Mirrors the hamburger menu's IsWorkspaceLoaded gating.
        switch (tag)
        {
            case TagReloadProject:
            case TagCloseProject:
                return ServiceLocator.AcquireService<IWorkspaceWrapper>().IsWorkspacePageLoaded;

            default:
                return true;
        }
    }

    private static void OnCommand(long tag)
    {
        // Edit verbs route to the focused surface through the edit-intent command, the same path the
        // keyboard and in-window menu use.
        var editIntent = EditIntentForTag(tag);
        if (editIntent is not null)
        {
            var commandService = ServiceLocator.AcquireService<ICommandService>();
            commandService.Execute<IPerformEditCommand>(command => command.Intent = editIntent.Value);
            return;
        }

        // The project commands run through the same view-model the hamburger menu uses, so the two menus
        // stay in lockstep. Resolved per invocation; the methods only dispatch commands or open dialogs.
        var viewModel = ServiceLocator.AcquireService<MainMenuViewModel>();

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

            case TagOpenProject:
                viewModel.OpenProject();
                break;

            case TagReloadProject:
                _ = viewModel.ReloadProjectAsync();
                break;

            case TagCloseProject:
                _ = viewModel.CloseProjectAsync();
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
