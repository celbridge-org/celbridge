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

    public override Task<Result> ExecuteAsync()
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
                return Task.FromResult<Result>(Result.Fail($"Unknown panel: {Panel}"));
        }

        return Task.FromResult(Result.Ok());
    }
}
