The Workspace should consist of 4 main panels: Primary (left), Secondary (right), Documents and Console.

The main navigation bar should move to the Titlebar and always be visible. Items are the Main Menu, a Workspace button that displays the workspace (if one is loaded), the Community button and the Settings button. Each of these buttons navigates to the corresponding Page. The currently loaded project is now displayed centered in the Title Bar, with the horizontal navigation bar aligned to the left hand side.

The "Context" buttons for Explorer, Search, Debug, etc. and the custom shortcut buttons move to a new "Project Panel". The Project Panel has a vertical Navigation bar along the left hand side and the right side of the Project Panel displays the option selected in the Navigation Bar (e.g. Explorer, Search, Debug, Source Control, custom shortcut buttons).

The Project Panel (with new navigation bar) is displayed in the Primary panel, the Inspector is displayed in the Secondary panel. There is a new option in the Window Layout flyout to swap the left and right hand panel contents (so that the Project Panel is displayed in the Secondary Panel and vice versa). This option is persisted in the Editor Settings.

The goal here is to free up the maximum horizontal space when the Primary and Secondary Panels are collapsed, while still enabling easy navigation.

## Stirngs

TitleBar_MenuTooltip = "Main Menu"
TitleBar_NewProjectTooltip = "Create a new project"
TitleBar_OpenProjectTooltip = "Open an existing project"
TitleBar_ReloadProjectTooltip = "Reload the current project"
TitleBar_WorkspaceTooltip = "Go to Workspace"
TitleBar_CommunityTooltip = "Community"
TitleBar_SettingsTooltip = "Settings"
TitleBar_Menu = "Menu"
TitleBar_NewProject = "New Project"
TitleBar_OpenProject = "Open Project"
TitleBar_ReloadProject = "Reload Project"

TitleBar_HomeTooltip	Home
TitleBar_CommunityTooltip = "Community"
TitleBar_SettingsTooltip = "Settings"
TitleBar_WorkspaceTooltip = "Workspace"

ProjectPanel_ExplorerTooltip = "Explorer"
ProjectPanel_SearchTooltip = "Search"
ProjectPanel_DebugTooltip = "Debug"
ProjectPanel_SourceControlTooltip = "Source Control"

LayoutToolbar_SwapPanelsLabel = "Swap Left/Right Panels"

---

## Implementation Plan

### Design Clarifications

1. **Panel Availability**: The Primary, Secondary, Documents, and Console panels are only present when the WorkspacePage is displayed. The Workspace button in the TitleBar navigation is disabled when no project is loaded.

2. **Panel Collapse Behavior**: When the Primary panel is collapsed, the entire content collapses including the vertical navigation bar within the Project Panel.

3. **TitleBar Navigation Style**: Use a compact horizontal `NavigationView` for the TitleBar navigation items (Main Menu, Workspace, Community, Settings).

4. **Settings Location**: Settings moves from the footer of the current left NavigationView to the TitleBar navigation alongside Community.

---

### Component Changes

#### 1. TitleBar (`TitleBar.xaml` / `TitleBar.xaml.cs`)

**Current State**: Contains app icon, project name, save indicator, and LayoutToolbar.

**Changes**:
- Add a horizontal `NavigationView` (LeftCompact mode, oriented horizontally) to the left side
- Navigation items:
  - **Main Menu** (hamburger icon) - Flyout with New Project, Open Project, Reload Project
  - **Workspace** (folder/project icon) - Navigates to WorkspacePage, disabled when no project loaded
  - **Community** (globe icon) - Navigates to CommunityPage
  - **Settings** (gear icon) - Navigates to SettingsPage
- Project name remains centered
- LayoutToolbar remains on the right

**New Layout** (left to right):
```
[App Icon] [Menu ▼] [Workspace] [Community] [Settings] ... [Project Name (centered)] ... [Save Icon] [LayoutToolbar] [Caption Buttons]
```

#### 2. MainPage (`MainPage.cs`)

**Current State**: Contains a vertical `NavigationView` with Home, Explorer, Search, Debug, Source Control, Community, and Settings items, plus a content Frame.

**Changes**:
- Remove the entire `NavigationView` from MainPage
- MainPage becomes a simple container with just the TitleBar and ContentFrame
- Navigation logic moves to be handled by TitleBar's NavigationView
- Remove shortcut menu item building from MainPage (moves to ProjectPanel)

#### 3. New ProjectPanel (`ProjectPanel.xaml` / `ProjectPanel.xaml.cs`)

**New Component** in `Celbridge.Workspace` project.

**Structure**:
- Vertical `NavigationView` in LeftCompact mode on the left side
- Content area on the right showing the selected panel (Explorer, Search, Debug, Source Control)
- Navigation items:
  - Explorer (file explorer icon)
  - Search (magnifying glass icon)
  - Debug (bug icon) - placeholder
  - Source Control (git icon) - placeholder
  - Custom shortcut buttons (loaded from project configuration)

**Behavior**:
- Selecting a nav item switches the content area to show the corresponding panel
- Shortcut commands execute scripts via ConsoleService
- The entire ProjectPanel is placed inside the Primary/Secondary panel based on swap setting

#### 4. WorkspacePage (`WorkspacePage.xaml` / `WorkspacePage.xaml.cs`)

**Current State**: Contains ContextPanel (left), DocumentsPanel (center), InspectorPanel (right), ConsolePanel (bottom).

**Changes**:
- Rename `ContextPanel` to `PrimaryPanel`
- Rename `InspectorPanel` column/area to `SecondaryPanel`
- PrimaryPanel contains the new ProjectPanel
- SecondaryPanel contains the Inspector
- Add logic to swap PrimaryPanel and SecondaryPanel contents based on `EditorSettings.SwapPrimarySecondaryPanels`
- Remove direct panel population (Explorer, Search) - now handled by ProjectPanel

#### 5. EditorSettings (`EditorSettings.cs` / `IEditorSettings.cs`)

**Changes**:
- Add new property: `bool SwapPrimarySecondaryPanels` (default: false)
- This persists the user's preference for panel layout swap

#### 6. LayoutToolbar (`LayoutToolbar.xaml` / `LayoutToolbar.xaml.cs`)

**Changes**:
- Add a new toggle/checkbox in the `PanelLayoutFlyout`: "Swap Left/Right Panels"
- When toggled, executes command to update `EditorSettings.SwapPrimarySecondaryPanels`
- Send a message (`PanelSwapChangedMessage`) to notify WorkspacePage to swap panel contents

#### 7. Navigation Constants & Tags

**Changes to `NavigationConstants.cs`**:
- Add `WorkspaceTag` for the new Workspace navigation item
- Existing tags (ExplorerTag, SearchTag, DebugTag, RevisionControlTag) move to ProjectPanel usage

#### 8. Messages

**New Messages**:
- `PanelSwapChangedMessage` - Sent when swap setting changes, contains `bool IsSwapped`

---

### File Summary

| Action | File Path | Description |
|--------|-----------|-------------|
| Modify | `CoreServices\Celbridge.UserInterface\Views\Controls\TitleBar.xaml` | Add horizontal NavigationView |
| Modify | `CoreServices\Celbridge.UserInterface\Views\Controls\TitleBar.xaml.cs` | Add navigation handling |
| Modify | `CoreServices\Celbridge.UserInterface\ViewModels\Controls\TitleBarViewModel.cs` | Add navigation state, IsWorkspaceLoaded |
| Modify | `CoreServices\Celbridge.UserInterface\Views\Pages\MainPage.cs` | Remove NavigationView, simplify to shell |
| Modify | `CoreServices\Celbridge.UserInterface\ViewModels\Pages\MainPageViewModel.cs` | Simplify navigation handling |
| Create | `Workspace\Celbridge.Workspace\Views\ProjectPanel.xaml` | New ProjectPanel UI |
| Create | `Workspace\Celbridge.Workspace\Views\ProjectPanel.xaml.cs` | New ProjectPanel code-behind |
| Create | `Workspace\Celbridge.Workspace\ViewModels\ProjectPanelViewModel.cs` | New ProjectPanel ViewModel |
| Modify | `Workspace\Celbridge.Workspace\Views\WorkspacePage.xaml` | Rename panels, integrate ProjectPanel |
| Modify | `Workspace\Celbridge.Workspace\Views\WorkspacePage.xaml.cs` | Add swap logic |
| Modify | `Workspace\Celbridge.Workspace\ViewModels\WorkspacePageViewModel.cs` | Add swap property binding |
| Modify | `CoreServices\Celbridge.Settings\Services\EditorSettings.cs` | Add SwapPrimarySecondaryPanels |
| Modify | `BaseLibrary\Settings\IEditorSettings.cs` | Add SwapPrimarySecondaryPanels interface |
| Modify | `CoreServices\Celbridge.UserInterface\Views\Controls\LayoutToolbar.xaml` | Add swap toggle |
| Modify | `CoreServices\Celbridge.UserInterface\Views\Controls\LayoutToolbar.xaml.cs` | Handle swap toggle |
| Create | `BaseLibrary\Messaging\PanelSwapChangedMessage.cs` | New message for swap changes |
| Modify | `BaseLibrary\Navigation\NavigationConstants.cs` | Add WorkspaceTag |

---

### Migration Notes

1. **Shortcut Menu Items**: The `BuildShortcutMenuItems` logic currently in MainPage needs to be moved to ProjectPanel. The `IProjectService.RegisterRebuildShortcutsUI` callback should target ProjectPanel instead.

2. **Navigation Provider**: MainPageViewModel currently implements `INavigationProvider`. This should remain, but navigation commands from TitleBar will route through the existing NavigationService.

3. **Workspace Loading State**: `TitleBarViewModel` needs access to `IWorkspaceWrapper.IsWorkspacePageLoaded` to enable/disable the Workspace button.

4. **Panel Visibility**: The existing `ILayoutManager` panel visibility flags (Context, Inspector, Console) should be renamed:
   - `IsContextPanelVisible` → `IsPrimaryPanelVisible`
   - `IsInspectorPanelVisible` → `IsSecondaryPanelVisible`
   - `IsConsolePanelVisible` remains unchanged

5. **Backward Compatibility**: Messages like `WorkspacePageActivatedMessage` and `WorkspacePageDeactivatedMessage` should continue to work as-is.
