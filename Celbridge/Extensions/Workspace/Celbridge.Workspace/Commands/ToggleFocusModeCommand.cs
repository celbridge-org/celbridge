using Celbridge.Commands;
using Celbridge.Settings;

namespace Celbridge.Workspace.Commands;

public class ToggleFocusModeCommand : CommandBase, IToggleFocusModeCommand
{
    private readonly IEditorSettings _editorSettings;

    public ToggleFocusModeCommand(IEditorSettings editorSettings)
    {
    }

    public override async Task<Result> ExecuteAsync()
    {
        // Are we in focus mode?
        bool isFocusModeActive = !_editorSettings.IsLeftPanelVisible && 
            !_editorSettings.IsRightPanelVisible;

        if (isFocusModeActive) 
        {
            // Exit focus mode
            _editorSettings.IsLeftPanelVisible = true;
            _editorSettings.IsRightPanelVisible = true;
        }
        else
        {
            // Enter focus mode
            _editorSettings.IsLeftPanelVisible = false;
            _editorSettings.IsRightPanelVisible = false;
        }

        await Task.CompletedTask;

        return Result.Ok();
    }
}
