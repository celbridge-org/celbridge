using Celbridge.Commands;
using Celbridge.Settings;
using Celbridge.UserInterface;

namespace Celbridge.Workspace.Commands;

public class ResetPanelCommand : CommandBase, IResetPanelCommand
{
    private readonly IEditorSettings _editorSettings;

    public PanelRegion Region { get; set; }

    public ResetPanelCommand(IEditorSettings editorSettings)
    {
        _editorSettings = editorSettings;
    }

    public override async Task<Result> ExecuteAsync()
    {
        switch (Region)
        {
            case PanelRegion.Primary:
                _editorSettings.PrimaryPanelWidth = UserInterfaceConstants.PrimaryPanelWidth;
                break;

            case PanelRegion.Secondary:
                _editorSettings.SecondaryPanelWidth = UserInterfaceConstants.SecondaryPanelWidth;
                break;

            case PanelRegion.Console:
                _editorSettings.ConsolePanelHeight = UserInterfaceConstants.ConsolePanelHeight;
                break;

            default:
                return Result.Fail($"Unknown region: {Region}");
        }

        await Task.CompletedTask;

        return Result.Ok();
    }
}
