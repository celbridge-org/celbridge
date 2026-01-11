using Celbridge.Commands;
using Celbridge.Settings;

namespace Celbridge.UserInterface.Commands;

public class ToggleZenModeCommand : CommandBase, IToggleZenModeCommand
{
    private readonly IUserInterfaceService _userInterfaceService;
    private readonly IEditorSettings _editorSettings;

    public ToggleZenModeCommand(
        IUserInterfaceService userInterfaceService,
        IEditorSettings editorSettings)
    {
        _userInterfaceService = userInterfaceService;
        _editorSettings = editorSettings;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var currentLayout = _editorSettings.WindowLayout;

        // Toggle between Windowed and ZenMode
        // If in any fullscreen mode, return to Windowed
        // If in Windowed mode, enter ZenMode
        if (currentLayout == WindowLayout.Windowed)
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
                    // don't enter Zen Mode. Instead, restore all panels (similar to VS Code).
                    _editorSettings.IsContextPanelVisible = true;
                    _editorSettings.IsInspectorPanelVisible = true;
                    _editorSettings.IsConsolePanelVisible = true;
                    return Result.Ok();
                }
            }

            _userInterfaceService.SetWindowLayout(WindowLayout.ZenMode);
        }
        else
        {
            // Exit any fullscreen mode back to Windowed
            _userInterfaceService.SetWindowLayout(WindowLayout.Windowed);
        }

        await Task.CompletedTask;

        return Result.Ok();
    }
}
