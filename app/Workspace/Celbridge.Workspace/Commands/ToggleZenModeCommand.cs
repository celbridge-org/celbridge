using Celbridge.Commands;
using Celbridge.Settings;

namespace Celbridge.Workspace.Commands;

public class ToggleZenModeCommand : CommandBase, IToggleZenModeCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IEditorSettings _editorSettings;

    public ToggleZenModeCommand(
        IWorkspaceWrapper workspaceWrapper,
        IEditorSettings editorSettings)
    {
        _workspaceWrapper = workspaceWrapper;
        _editorSettings = editorSettings;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail("Workspace is not loaded.");
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var currentMode = _editorSettings.LayoutMode;

        // Toggle between Windowed and ZenMode
        // If in any fullscreen mode, return to Windowed
        // If in Windowed mode, enter ZenMode
        if (currentMode == LayoutMode.Windowed)
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
            }
            else
            {
                workspaceService.SetLayoutMode(LayoutMode.ZenMode);
            }
        }
        else
        {
            // Exit any fullscreen mode back to Windowed
            workspaceService.SetLayoutMode(LayoutMode.Windowed);
        }

        await Task.CompletedTask;

        return Result.Ok();
    }
}
