using Celbridge.Commands;
using Celbridge.Settings;

namespace Celbridge.Workspace.Commands;

public class ToggleFocusModeCommand : CommandBase, IToggleFocusModeCommand
{
    private readonly IEditorSettings _editorSettings;

    public ToggleFocusModeCommand(IEditorSettings editorSettings)
    {
        _editorSettings = editorSettings;
    }

    public override async Task<Result> ExecuteAsync()
    {
        // Are we in focus mode?
        bool isFocusModeActive = !_editorSettings.IsExplorerPanelVisible && 
            !_editorSettings.IsInspectorPanelVisible;

        if (isFocusModeActive) 
        {
            // Exit focus mode
            _editorSettings.IsExplorerPanelVisible = true;
            _editorSettings.IsInspectorPanelVisible = true;
        }
        else
        {
            // Enter focus mode
            _editorSettings.IsExplorerPanelVisible = false;
            _editorSettings.IsInspectorPanelVisible = false;
        }

        await Task.CompletedTask;

        return Result.Ok();
    }
}
