using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Entities;
using Celbridge.Logging;

namespace Celbridge.HTML.Components;

public class HTMLEditor : ComponentEditorBase
{
    private readonly ILogger<HTMLEditor> _logger;
    private readonly ICommandService _commandService;

    private const string ConfigPath = "Celbridge.HTML.Assets.Components.HTMLComponent.json";
    private const string ComponentFormPath = "Celbridge.HTML.Assets.Forms.HTMLForm.json";
    private const string ComponentRootFormPath = "Celbridge.HTML.Assets.Forms.HTMLRootForm.json";

    private const string OpenDocumentButtonId = "OpenDocument";

    public const string ComponentType = "HTML.HTML";

    public HTMLEditor(
        ILogger<HTMLEditor> logger,
        ICommandService commandService)
    {
        _logger = logger;
        _commandService = commandService;
    }

    public override string GetComponentConfig()
    {
        return LoadEmbeddedResource(ConfigPath);
    }

    public override string GetComponentForm()
    {
        return LoadEmbeddedResource(ComponentFormPath);
    }

    public override string GetComponentRootForm()
    {
        return LoadEmbeddedResource(ComponentRootFormPath);
    }

    public override ComponentSummary GetComponentSummary()
    {
        return new ComponentSummary(string.Empty, string.Empty);
    }

    public override void OnButtonClicked(string buttonId)
    {
        if (buttonId == OpenDocumentButtonId)
        {
            OpenDocument();
        }
    }

    private void OpenDocument()
    {
        var resource = Component.Key.Resource;

        _commandService.Execute<IOpenDocumentCommand>(command =>
        {
            command.FileResource = resource;
            command.ForceReload = false;
        });
    }
}
