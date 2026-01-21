# Settings Page Requirements

## Overview

This document outlines proposed user settings for the Celbridge Settings Page. The settings are organized by category and prioritized based on user impact, implementation complexity, and alignment with existing functionality.

## Current State

The Settings Page currently only supports:
- **Application Theme** - System, Light, or Dark mode selection

The `IEditorSettings` interface already stores additional settings that are not exposed in the UI:
- Window geometry and panel sizes
- Recent projects list
- Search panel options (Match Case, Whole Word)
- Previous file paths and extensions

## Priority Levels

- **P0** - Critical settings that significantly improve user experience and leverage existing infrastructure
- **P1** - Important settings that address common user needs
- **P2** - Nice-to-have settings that enhance productivity
- **P3** - Advanced settings for power users
- **P4** - Future enhancements that require significant architectural changes

---

## 1. Editor & Documents Settings

### P0: Monaco Editor Font Size
**Description:** Allow users to configure the font size for Monaco code editors  
**Current State:** Hard-coded in the editor  
**Implementation:** 
- Add property to `IEditorSettings`: `int EditorFontSize { get; set; }` (default: 14)
- Apply setting when initializing Monaco editor instances
- Add number spinner to Settings Page (range: 8-32)

**User Impact:** High - Affects readability and accessibility for all code editing

---

### P0: Default Editor Mode for Documents
**Description:** Set default view mode for markdown and document files  
**Current State:** Currently defaults to Editor mode  
**Implementation:**
- Add property to `IEditorSettings`: `EditorMode DefaultEditorMode { get; set; }` (default: EditorMode.Editor)
- Add dropdown to Settings Page with options: Editor, EditorAndPreview, Preview
- Apply when opening documents without saved preferences

**User Impact:** High - Users frequently switch between modes; setting a default saves clicks

---

### P1: Auto-Save Interval for Spreadsheets
**Description:** Configure delay before auto-saving spreadsheet documents  
**Current State:** Hard-coded to 1.0 seconds in `SpreadsheetDocumentViewModel`  
**Implementation:**
- Add property to `IEditorSettings`: `double SpreadsheetAutoSaveDelay { get; set; }` (default: 1.0)
- Add number input to Settings Page (range: 0.5-10.0 seconds)
- Update `SpreadsheetDocumentViewModel.SaveDelay` to use setting

**User Impact:** Medium - Prevents data loss while allowing user control over performance

---

### P1: Monaco Editor Theme
**Description:** Separate editor theme from application theme  
**Current State:** Editor theme follows application theme  
**Implementation:**
- Add property to `IEditorSettings`: `string EditorTheme { get; set; }` (default: "vs-dark")
- Add dropdown with options: "vs" (light), "vs-dark" (dark), "hc-black" (high contrast)
- Apply when initializing Monaco editor instances

**User Impact:** Medium - Some users prefer dark code editors with light UI or vice versa

---

### P1: Default File Extension for New Files
**Description:** Set preferred file extension when creating new files  
**Current State:** Defaults to ".py" in `IEditorSettings.PreviousNewFileExtension`  
**Implementation:**
- Add dropdown to Settings Page with common extensions: .py, .md, .txt, .json, .html, etc.
- Bind to existing `PreviousNewFileExtension` property

**User Impact:** Medium - Quality of life improvement for users who primarily work with one file type

---

### P2: Tab Size and Indentation
**Description:** Configure indentation preferences for code editors  
**Current State:** Likely uses Monaco defaults  
**Implementation:**
- Add properties to `IEditorSettings`:
  - `int EditorTabSize { get; set; }` (default: 4)
  - `bool EditorInsertSpaces { get; set; }` (default: true)
- Add number input for tab size (2, 4, 8)
- Add toggle for "Insert Spaces" vs "Use Tabs"
- Apply to Monaco editor configuration

**User Impact:** Medium - Important for code consistency and team collaboration

---

### P2: Word Wrap in Editors
**Description:** Enable/disable word wrap in text editors  
**Current State:** Unknown  
**Implementation:**
- Add property to `IEditorSettings`: `bool EditorWordWrap { get; set; }` (default: false)
- Add toggle to Settings Page
- Apply to Monaco editor configuration

**User Impact:** Low - User preference varies by use case

---

### P2: Show Line Numbers
**Description:** Toggle line numbers in code editors  
**Current State:** Likely enabled by default  
**Implementation:**
- Add property to `IEditorSettings`: `bool EditorShowLineNumbers { get; set; }` (default: true)
- Add toggle to Settings Page
- Apply to Monaco editor configuration

**User Impact:** Low - Most users prefer line numbers, but some find them distracting

---

### P3: Monaco Editor Minimap
**Description:** Show/hide the Monaco editor minimap  
**Current State:** Unknown  
**Implementation:**
- Add property to `IEditorSettings`: `bool EditorShowMinimap { get; set; }` (default: true)
- Add toggle to Settings Page
- Apply to Monaco editor configuration

**User Impact:** Low - Power user feature for navigation in large files

---

## 2. Python & Console Settings

### P0: Python Log Level
**Description:** Configure logging verbosity for Python execution  
**Current State:** Hard-coded to "DEBUG" in `PythonService`  
**Implementation:**
- Add property to `IEditorSettings`: `string PythonLogLevel { get; set; }` (default: "INFO")
- Add dropdown with options: DEBUG, INFO, WARNING, ERROR, CRITICAL
- Update `PythonService.InitializePython()` to use setting instead of hard-coded value

**User Impact:** High - Reduces console noise for non-debugging scenarios

---

### P1: Console Auto-Open on Script Run
**Description:** Automatically show console panel when running Python scripts  
**Current State:** Console panel visibility is manual  
**Implementation:**
- Add property to `IEditorSettings`: `bool ConsoleAutoOpenOnRun { get; set; }` (default: true)
- Add toggle to Settings Page
- Check setting in run command implementation before showing console

**User Impact:** Medium - Convenience feature that improves workflow

---

### P2: Clear Console on Run
**Description:** Option to clear console output before each script execution  
**Current State:** Console accumulates output  
**Implementation:**
- Add property to `IEditorSettings`: `bool ConsoleClearOnRun { get; set; }` (default: false)
- Add toggle to Settings Page
- Clear console in run command if setting is enabled

**User Impact:** Low - User preference for output management

---

### P3: Python Package Auto-Install Prompt
**Description:** Confirm before installing new Python packages  
**Current State:** Packages are installed automatically via uv  
**Implementation:**
- Add property to `IEditorSettings`: `bool PythonPromptBeforeInstall { get; set; }` (default: false)
- Add toggle to Settings Page
- Show confirmation dialog before package installation if enabled

**User Impact:** Low - Security/control feature for cautious users

---

## 3. Explorer & Files Settings

### P1: Show Hidden Files
**Description:** Toggle visibility of hidden/system files in Explorer  
**Current State:** Unknown  
**Implementation:**
- Add property to `IEditorSettings`: `bool ExplorerShowHiddenFiles { get; set; }` (default: false)
- Add toggle to Settings Page
- Filter file list in Explorer based on setting

**User Impact:** Medium - Common need when working with config files

---

### P1: Recent Projects List Size
**Description:** Configure maximum number of recent projects to remember  
**Current State:** Unlimited list in `IEditorSettings.RecentProjects`  
**Implementation:**
- Add property to `IEditorSettings`: `int MaxRecentProjects { get; set; }` (default: 10)
- Add number input to Settings Page (range: 5-20)
- Trim recent projects list when adding new entries

**User Impact:** Medium - Prevents UI clutter in recent projects menu

---

### P2: File Sorting in Explorer
**Description:** Default sort order for files in Explorer  
**Current State:** Unknown  
**Implementation:**
- Add property to `IEditorSettings`: `FileSortMode ExplorerSortMode { get; set; }` (default: FileSortMode.Name)
- Create enum: Name, Date, Type, Size
- Add dropdown to Settings Page
- Apply sort in Explorer file list

**User Impact:** Low - User preference for file organization

---

### P2: Auto-Expand Folders
**Description:** Automatically expand folder tree when opening projects  
**Current State:** Unknown  
**Implementation:**
- Add property to `IEditorSettings`: `bool ExplorerAutoExpandFolders { get; set; }` (default: false)
- Add toggle to Settings Page
- Expand tree nodes on project load if enabled

**User Impact:** Low - Convenience vs. performance trade-off

---

### P3: Show File Extensions
**Description:** Always show or hide file extensions in Explorer  
**Current State:** Likely always shown  
**Implementation:**
- Add property to `IEditorSettings`: `bool ExplorerShowExtensions { get; set; }` (default: true)
- Add toggle to Settings Page
- Format file names in Explorer based on setting

**User Impact:** Low - Most development workflows require visible extensions

---

## 4. Generative AI Settings

### P1: Default AI Provider
**Description:** Select which AI provider to use for text generation  
**Current State:** Defaults to OpenAI in `GenerativeAIService`  
**Implementation:**
- Add property to `IEditorSettings`: `string AIProvider { get; set; }` (default: "OpenAI")
- Add dropdown to Settings Page (currently only OpenAI is implemented)
- Update service to use configured provider

**User Impact:** Medium - Future-proofing for when multiple providers are supported

---

### P2: AI Model Selection
**Description:** Choose specific AI model (e.g., GPT-4o, GPT-3.5)  
**Current State:** Hard-coded to "gpt-4o" in `OpenAIProvider`  
**Implementation:**
- Add property to `IEditorSettings`: `string AIModel { get; set; }` (default: "gpt-4o")
- Add dropdown to Settings Page
- Update `OpenAIProvider` to use configured model

**User Impact:** Medium - Cost/quality trade-off for different use cases

---

### P3: AI Temperature
**Description:** Control randomness/creativity in AI responses  
**Current State:** Uses OpenAI default  
**Implementation:**
- Add property to `IEditorSettings`: `double AITemperature { get; set; }` (default: 0.7)
- Add slider to Settings Page (range: 0.0-2.0)
- Pass to OpenAI API in `OpenAIProvider`

**User Impact:** Low - Advanced feature for users familiar with AI parameters

---

### P3: AI Max Tokens
**Description:** Configure maximum response length  
**Current State:** Uses OpenAI default  
**Implementation:**
- Add property to `IEditorSettings`: `int AIMaxTokens { get; set; }` (default: 2048)
- Add number input to Settings Page
- Pass to OpenAI API in `OpenAIProvider`

**User Impact:** Low - Cost optimization for power users

---

## 5. Screenplay Module Settings

### P2: Auto-Run Scripts on Data Change (Global Default)
**Description:** Default setting for new spreadsheet components  
**Current State:** Configured per-component in `SpreadsheetEditor`  
**Implementation:**
- Add property to `IEditorSettings`: `bool ScreenplayAutoRunScripts { get; set; }` (default: false)
- Add toggle to Settings Page
- Use as default when creating new spreadsheet components

**User Impact:** Low - Module-specific setting

---

### P2: Confirmation Dialogs
**Description:** Enable/disable prompts for destructive operations  
**Current State:** Always shows confirmation dialogs  
**Implementation:**
- Add property to `IEditorSettings`: `bool ShowConfirmationDialogs { get; set; }` (default: true)
- Add toggle to Settings Page
- Check setting before showing dialogs in `ScreenplayActivity`

**User Impact:** Low - Power user convenience vs. safety

---

## 6. Window & Layout Settings

### P0: Expose Panel Visibility Settings
**Description:** Make existing panel visibility settings accessible in UI  
**Current State:** `PreferredPanelVisibility` exists but not exposed  
**Implementation:**
- Add toggles to Settings Page for each panel:
  - Show Explorer Panel
  - Show Inspector Panel  
  - Show Console Panel
  - Show Detail Panel
- Bind to existing `PreferredPanelVisibility` flags

**User Impact:** High - Users want control over workspace layout

---

### P1: Expose Panel Size Settings
**Description:** Allow manual adjustment of default panel sizes  
**Current State:** Settings exist but not exposed in UI  
**Implementation:**
- Add number inputs to Settings Page:
  - Primary Panel Width (default: 300)
  - Secondary Panel Width (default: 300)
  - Console Panel Height (default: 200)
  - Detail Panel Height (default: 200)
- Bind to existing properties in `IEditorSettings`

**User Impact:** Medium - Fine-tuning workspace layout

---

### P2: Startup Behavior
**Description:** Configure what happens when app starts  
**Current State:** Opens last project if `PreviousProject` is set  
**Implementation:**
- Add property to `IEditorSettings`: `StartupBehavior StartupMode { get; set; }` (default: StartupBehavior.OpenLastProject)
- Create enum: OpenLastProject, ShowWelcomeScreen, NewProjectDialog, DoNothing
- Add dropdown to Settings Page
- Update startup logic in main application

**User Impact:** Medium - User preference for workflow

---

### P3: Remember Window Geometry
**Description:** Expose existing window geometry setting  
**Current State:** `UsePreferredWindowGeometry` exists but not in UI  
**Implementation:**
- Add toggle to Settings Page: "Remember window position and size"
- Bind to existing `UsePreferredWindowGeometry` property

**User Impact:** Low - Most users want this enabled

---

## 7. Search Settings

### P0: Expose Search Options
**Description:** Make existing search settings accessible in UI  
**Current State:** `SearchMatchCase` and `SearchWholeWord` exist but not exposed  
**Implementation:**
- Add section to Settings Page with toggles:
  - Match Case by Default
  - Match Whole Word by Default
- Bind to existing properties in `IEditorSettings`

**User Impact:** High - Frequently used settings that already exist

---

### P2: Search History Size
**Description:** Configure number of recent searches to remember  
**Current State:** No search history  
**Implementation:**
- Add property to `IEditorSettings`: `int SearchHistorySize { get; set; }` (default: 10)
- Add number input to Settings Page
- Implement search history feature in search panel

**User Impact:** Low - Requires new feature implementation

---

### P3: Search Include/Exclude Patterns
**Description:** Default file patterns for search operations  
**Current State:** No pattern filtering  
**Implementation:**
- Add properties to `IEditorSettings`:
  - `List<string> SearchIncludePatterns { get; set; }`
  - `List<string> SearchExcludePatterns { get; set; }`
- Add text inputs to Settings Page (comma-separated patterns)
- Apply patterns in search operations

**User Impact:** Low - Advanced feature for large projects

---

## 8. Localization Settings

### P3: Language Selection
**Description:** Choose application UI language  
**Current State:** Uses `ILocalizerService` with fixed language  
**Implementation:**
- Add property to `IEditorSettings`: `string UILanguage { get; set; }` (default: "en")
- Add dropdown to Settings Page with supported languages
- Restart required to apply change

**User Impact:** Medium - Important for non-English users when localization is available

---

### P4: Date and Number Formats
**Description:** Configure regional format preferences  
**Current State:** Uses system defaults  
**Implementation:**
- Add properties to `IEditorSettings`:
  - `string DateFormat { get; set; }`
  - `string NumberFormat { get; set; }`
- Add dropdowns to Settings Page
- Apply in formatting operations

**User Impact:** Low - Regional preference

---

## 9. Performance Settings

### P2: File Monitoring
**Description:** Enable/disable auto-reload on external file changes  
**Current State:** Always monitors files (e.g., in `SpreadsheetDocumentViewModel`)  
**Implementation:**
- Add property to `IEditorSettings`: `bool EnableFileMonitoring { get; set; }` (default: true)
- Add toggle to Settings Page
- Disable file watchers if setting is false

**User Impact:** Low - Performance optimization for large projects

---

### P3: Max File Size for Syntax Highlighting
**Description:** Disable expensive features for large files  
**Current State:** No limit  
**Implementation:**
- Add property to `IEditorSettings`: `int MaxFileSizeForHighlighting { get; set; }` (default: 1024 KB)
- Add number input to Settings Page
- Check file size before enabling Monaco features

**User Impact:** Low - Performance edge case

---

### P3: Editor Debounce Interval
**Description:** Delay before processing changes in editors  
**Current State:** Various hard-coded values  
**Implementation:**
- Add property to `IEditorSettings`: `double EditorDebounceMs { get; set; }` (default: 300)
- Add number input to Settings Page
- Apply to change handlers in editors

**User Impact:** Low - Performance tuning for slow machines

---

## 10. Advanced Settings

### P2: Reset All Settings
**Description:** Button to restore all settings to defaults  
**Current State:** `IEditorSettings.Reset()` exists but not exposed  
**Implementation:**
- Add "Reset to Defaults" button to Settings Page
- Show confirmation dialog
- Call `IEditorSettings.Reset()` on confirmation

**User Impact:** Medium - Recovery option for misconfigured settings

---

### P3: Developer Mode
**Description:** Show additional debugging information  
**Current State:** No developer mode  
**Implementation:**
- Add property to `IEditorSettings`: `bool DeveloperMode { get; set; }` (default: false)
- Add toggle to Settings Page
- Enable debug logging, show internal IDs, etc. when enabled

**User Impact:** Low - For development and troubleshooting

---

### P4: Telemetry
**Description:** Opt-in/out of usage analytics  
**Current State:** No telemetry  
**Implementation:**
- Add property to `IEditorSettings`: `bool EnableTelemetry { get; set; }` (default: false)
- Add toggle to Settings Page
- Implement telemetry collection when feature is added

**User Impact:** Low - Privacy feature for future functionality

---

### P4: Automatic Updates
**Description:** Check for updates on startup  
**Current State:** No update mechanism  
**Implementation:**
- Add property to `IEditorSettings`: `bool CheckForUpdates { get; set; }` (default: true)
- Add toggle to Settings Page
- Implement update checking when feature is added

**User Impact:** Low - Convenience feature for future functionality

---

## Implementation Roadmap

### Phase 1: Quick Wins (P0 Settings)
These settings leverage existing infrastructure and provide immediate value:
1. Monaco Editor Font Size
2. Default Editor Mode for Documents
3. Python Log Level
4. Expose Panel Visibility Settings
5. Expose Search Options (Match Case, Whole Word)

**Estimated Effort:** 1-2 days  
**User Impact:** High - Addresses most common user requests

---

### Phase 2: Quality of Life (P1 Settings)
Important settings that enhance the user experience:
1. Auto-Save Interval for Spreadsheets
2. Monaco Editor Theme
3. Default File Extension
4. Console Auto-Open on Script Run
5. Show Hidden Files
6. Recent Projects List Size
7. Default AI Provider
8. Expose Panel Size Settings

**Estimated Effort:** 3-5 days  
**User Impact:** Medium - Improves productivity and personalization

---

### Phase 3: Polish and Advanced Features (P2-P3 Settings)
Nice-to-have settings for power users:
1. Tab Size and Indentation
2. Word Wrap, Line Numbers, Minimap
3. Clear Console on Run
4. File Sorting, Auto-Expand Folders
5. AI Model Selection and Parameters
6. Startup Behavior
7. Performance Settings

**Estimated Effort:** 5-7 days  
**User Impact:** Low-Medium - Caters to specific workflows

---

### Phase 4: Future Enhancements (P4 Settings)
Settings that require new features to be implemented:
1. Localization (Language, Date/Number Formats)
2. Telemetry
3. Automatic Updates

**Estimated Effort:** Depends on feature implementation  
**User Impact:** Low - Long-term roadmap items

---

## UI Design Considerations

### Settings Page Layout
The current Settings Page has a simple layout with one setting. As we add more settings, consider:

1. **Categorized Sections** - Group settings by category (Editor, Python, Explorer, etc.)
2. **Tabs or Accordion** - Use tabs for top-level categories or an accordion for expandable sections
3. **Search/Filter** - Add a search box to quickly find settings
4. **Reset Buttons** - Per-section reset buttons in addition to global reset
5. **Visual Hierarchy** - Use headings, separators, and spacing to organize settings
6. **Responsive Layout** - Ensure settings are accessible on different screen sizes

### Example Layout Structure
```
Settings Page
├── Editor
│   ├── Font Size [number]
│   ├── Theme [dropdown]
│   ├── Default Mode [dropdown]
│   ├── Tab Size [number]
│   ├── Insert Spaces [toggle]
│   ├── Word Wrap [toggle]
│   └── Show Line Numbers [toggle]
├── Python & Console
│   ├── Log Level [dropdown]
│   ├── Auto-Open Console [toggle]
│   └── Clear on Run [toggle]
├── Explorer
│   ├── Show Hidden Files [toggle]
│   ├── Sort By [dropdown]
│   └── Max Recent Projects [number]
├── Workspace
│   ├── Panel Visibility [toggles]
│   ├── Panel Sizes [numbers]
│   └── Startup Behavior [dropdown]
├── Search
│   ├── Match Case [toggle]
│   └── Match Whole Word [toggle]
└── Advanced
    ├── Developer Mode [toggle]
    └── Reset All Settings [button]
```

---

## Technical Implementation Notes

### Adding a New Setting

1. **Update `IEditorSettings` interface:**
   ```csharp
   public interface IEditorSettings : INotifyPropertyChanged
   {
       // ... existing properties ...
       
       /// <summary>
       /// Your setting description.
       /// </summary>
       YourType YourSettingName { get; set; }
   }
   ```

2. **Implement in `EditorSettings` class:**
   ```csharp
   public class EditorSettings : ObservableSettings, IEditorSettings
   {
       // ... existing properties ...
       
       public YourType YourSettingName
       {
           get => GetValue<YourType>(nameof(YourSettingName), defaultValue);
           set => SetValue(nameof(YourSettingName), value);
       }
   }
   ```

3. **Add localized strings:**
   Add entries to localization resource files for the setting label and description.

4. **Update Settings Page XAML:**
   Add UI controls (TextBox, ComboBox, ToggleSwitch, etc.) with bindings.

5. **Update Settings Page code-behind:**
   Add event handlers if needed for immediate application of settings.

6. **Use the setting:**
   Access via `IEditorSettings` service in the relevant component.

### Testing Checklist
- [ ] Setting persists across app restarts
- [ ] Setting applies immediately (or after restart if required)
- [ ] Default value is correct
- [ ] Setting validation prevents invalid values
- [ ] UI updates when setting changes programmatically
- [ ] Localized strings are correct
- [ ] Reset functionality works

---

## Appendix: Related Code Files

### Core Settings Files
- `BaseLibrary/Settings/IEditorSettings.cs` - Settings interface
- `CoreServices/Celbridge.Settings/Services/EditorSettings.cs` - Settings implementation
- `CoreServices/Celbridge.UserInterface/Views/Pages/SettingsPage.xaml` - Settings UI
- `CoreServices/Celbridge.UserInterface/Views/Pages/SettingsPage.xaml.cs` - Settings code-behind

### Components Using Hard-Coded Values
- `Workspace/Celbridge.Python/Services/PythonService.cs` - Python log level
- `Workspace/Celbridge.Documents/ViewModels/SpreadsheetDocumentViewModel.cs` - Auto-save delay
- `Workspace/Celbridge.GenerativeAI/Services/OpenAIProvider.cs` - AI model

### Related Services
- `CoreServices/Celbridge.Settings/Services/SettingsService.cs` - Settings persistence
- `CoreServices/Celbridge.Localization/Services/LocalizerService.cs` - String localization
- `CoreServices/Celbridge.UserInterface/Services/UserInterfaceService.cs` - Theme management

---

## Revision History

| Date | Version | Changes |
|------|---------|---------|
| 2025-01-XX | 1.0 | Initial requirements document |

---

## Feedback and Iteration

This document should be reviewed with:
- Product owner for priority validation
- UX designer for Settings Page mockups
- Development team for effort estimation
- QA team for test planning

Settings should be implemented iteratively, starting with Phase 1 (P0) items and gathering user feedback before proceeding to lower-priority items.
