using Celbridge.Commands;
using Celbridge.Settings;

namespace Celbridge.Workspace.Commands;

public class ResetPanelCommand : CommandBase, IResetPanelCommand
{
    private readonly IEditorSettings _editorSettings;

    public LayoutRegion Region { get; set; }

    public ResetPanelCommand(IEditorSettings editorSettings)
    {
        _editorSettings = editorSettings;
    }

    public override async Task<Result> ExecuteAsync()
    {
        switch (Region)
        {
            case LayoutRegion.Primary:
                _editorSettings.PrimaryPanelWidth = WorkspaceConstants.PrimaryPanelWidth;
                break;

            case LayoutRegion.Secondary:
                _editorSettings.SecondaryPanelWidth = WorkspaceConstants.SecondaryPanelWidth;
                break;

            case LayoutRegion.Console:
                _editorSettings.ConsolePanelHeight = WorkspaceConstants.ConsolePanelHeight;
                break;

            default:
                return Result.Fail($"Unknown region: {Region}");
        }

        await Task.CompletedTask;

        return Result.Ok();
    }
}
