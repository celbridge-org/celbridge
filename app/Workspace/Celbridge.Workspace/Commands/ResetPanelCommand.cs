using Celbridge.Commands;
using Celbridge.Settings;
using Celbridge.UserInterface;

namespace Celbridge.Workspace.Commands;

public class ResetPanelCommand : CommandBase, IResetPanelCommand
{
    private readonly IEditorSettings _editorSettings;

    public PanelVisibilityFlags Panel { get; set; }

    public ResetPanelCommand(IEditorSettings editorSettings)
    {
        _editorSettings = editorSettings;
    }

    public override async Task<Result> ExecuteAsync()
    {
        switch (Panel)
        {
            case PanelVisibilityFlags.Primary:
                _editorSettings.PrimaryPanelWidth = UserInterfaceConstants.PrimaryPanelWidth;
                break;

            case PanelVisibilityFlags.Secondary:
                _editorSettings.SecondaryPanelWidth = UserInterfaceConstants.SecondaryPanelWidth;
                break;

            case PanelVisibilityFlags.Console:
                _editorSettings.ConsolePanelHeight = UserInterfaceConstants.ConsolePanelHeight;
                break;

            default:
                return Result.Fail($"Unknown panel: {Panel}");
        }

        await Task.CompletedTask;

        return Result.Ok();
    }
}
