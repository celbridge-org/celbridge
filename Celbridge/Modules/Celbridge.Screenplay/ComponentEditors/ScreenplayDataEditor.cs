using Celbridge.Commands;
using Celbridge.Entities;
using Celbridge.Screenplay.Commands;

namespace Celbridge.Screenplay.Components;

public class ScreenplayDataEditor : ComponentEditorBase
{
    private ICommandService _commandService;

    private const string _configPath = "Celbridge.Screenplay.Assets.Components.ScreenplayDataComponent.json";
    private const string _formPath = "Celbridge.Screenplay.Assets.Forms.ScreenplayDataForm.json";

    public const string ComponentType = "Screenplay.ScreenplayData";

    public ScreenplayDataEditor(ICommandService commandService)
    {
        _commandService = commandService;
    }

    public override string GetComponentConfig()
    {
        return LoadEmbeddedResource(_configPath);
    }

    public override string GetComponentForm()
    {
        return LoadEmbeddedResource(_formPath);
    }

    public override ComponentSummary GetComponentSummary()
    {
        return new ComponentSummary(string.Empty, string.Empty);
    }

    public override void OnButtonClicked(string buttonId)
    {
        var resource = Component.Key.Resource;

        _commandService.Execute<ImportScreenplayCommand>(command =>
        {
            command.ExcelFile = resource;
        });
    }
}
