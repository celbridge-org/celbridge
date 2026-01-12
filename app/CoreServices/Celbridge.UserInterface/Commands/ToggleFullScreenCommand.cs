using Celbridge.Commands;
using Celbridge.Settings;

namespace Celbridge.UserInterface.Commands;

public class ToggleFullScreenCommand : CommandBase, IToggleFullScreenCommand
{
    private readonly IUserInterfaceService _userInterfaceService;
    private readonly IEditorSettings _editorSettings;

    public ToggleFullScreenCommand(
        IUserInterfaceService userInterfaceService,
        IEditorSettings editorSettings)
    {
        _userInterfaceService = userInterfaceService;
        _editorSettings = editorSettings;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var currentMode = _editorSettings.WindowMode;

        // Toggle between Windowed and ZenMode
        // If in any fullscreen mode, return to Windowed
        // If in Windowed mode, enter ZenMode
        if (currentMode == WindowMode.Windowed)
        {
            // Only check panel state when on the Workspace page
            if (_userInterfaceService.ActivePage == ApplicationPage.Workspace)
            {
                // Check if all panels are already collapsed
                bool allPanelsCollapsed = !_editorSettings.IsContextPanelVisible &&
                    !_editorSettings.IsInspectorPanelVisible &&
                    !_editorSettings.IsConsolePanelVisible;

                if (allPanelsCollapsed)
                {
                    // Special case: If all panels are already collapsed in Windowed mode,
                    // don't enter full screen. Instead, restore all panels (similar to VS Code).
                    _editorSettings.IsContextPanelVisible = true;
                    _editorSettings.IsInspectorPanelVisible = true;
                    _editorSettings.IsConsolePanelVisible = true;
                    return Result.Ok();
                }
            }

            _userInterfaceService.SetWindowMode(WindowMode.ZenMode);
        }
        else
        {
            // Exit any fullscreen mode back to Windowed
            _userInterfaceService.SetWindowMode(WindowMode.Windowed);
        }

        await Task.CompletedTask;

        return Result.Ok();
    }
}
