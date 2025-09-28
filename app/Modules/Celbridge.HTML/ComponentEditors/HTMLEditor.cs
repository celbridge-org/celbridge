using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Entities;
using Celbridge.Logging;
using System.Text.Json;

namespace Celbridge.HTML.Components;

public class HTMLEditor : ComponentEditorBase
{
    private readonly ILogger<HTMLEditor> _logger;
    private readonly ICommandService _commandService;

    private const string ConfigPath = "Celbridge.HTML.Assets.Components.HTMLComponent.json";
    private const string ComponentFormPath = "Celbridge.HTML.Assets.Forms.HTMLForm.json";
    private const string ComponentRootFormPath = "Celbridge.HTML.Assets.Forms.HTMLRootForm.json";

    private const string OpenDocumentButtonId = "OpenDocument";
    private const string EditorButtonId = "Editor";
    private const string EditorAndPreviewButtonId = "EditorAndPreview";
    private const string PreviewButtonId = "Preview";

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

    protected override void OnFormPropertyChanged(string propertyPath)
    {
        if (propertyPath == DocumentConstants.EditorModeProperty)
        {
            // Notify updates to "virtual" form properties
            NotifyFormPropertyChanged(DocumentConstants.EditorEnabledProperty);
            NotifyFormPropertyChanged(DocumentConstants.EditorAndPreviewEnabledProperty);
            NotifyFormPropertyChanged(DocumentConstants.PreviewEnabledProperty);
        }
    }

    public override void OnButtonClicked(string buttonId)
    {
        switch (buttonId)
        {
            case OpenDocumentButtonId:
                OpenDocument();
                break;

            case EditorButtonId:
                SetEditorMode(EditorMode.Editor);
                OpenDocument();
                break;

            case EditorAndPreviewButtonId:
                SetEditorMode(EditorMode.EditorAndPreview);
                OpenDocument();
                break;

            case PreviewButtonId:
                SetEditorMode(EditorMode.Preview);
                OpenDocument();
                break;
        }
    }

    protected override Result<string> TryGetProperty(string propertyPath)
    {
        if (propertyPath == DocumentConstants.EditorEnabledProperty)
        {
            var editorMode = Component.GetString(DocumentConstants.EditorModeProperty);

            bool isEnabled = editorMode == nameof(EditorMode.EditorAndPreview) || editorMode == nameof(EditorMode.Preview);
            var jsonValue = JsonSerializer.Serialize(isEnabled);

            return Result<string>.Ok(jsonValue);
        }
        else if (propertyPath == DocumentConstants.EditorAndPreviewEnabledProperty)
        {
            var editorMode = Component.GetString(DocumentConstants.EditorModeProperty);

            bool isEnabled = editorMode == nameof(EditorMode.Editor) || editorMode == nameof(EditorMode.Preview);
            var jsonValue = JsonSerializer.Serialize(isEnabled);

            return Result<string>.Ok(jsonValue);
        }
        else if (propertyPath == DocumentConstants.PreviewEnabledProperty)
        {
            var editorMode = Component.GetString(DocumentConstants.EditorModeProperty);

            bool isEnabled = editorMode == nameof(EditorMode.Editor) || editorMode == nameof(EditorMode.EditorAndPreview);
            var jsonValue = JsonSerializer.Serialize(isEnabled);

            return Result<string>.Ok(jsonValue);
        }

        return Result<string>.Fail();
    }

    private void OpenDocument()
    {
        var resource = Component.Key.Resource;

        // Execute a command to open the HTML document.
        _commandService.Execute<IOpenDocumentCommand>(command =>
        {
            command.FileResource = resource;
            command.ForceReload = false;
        });
    }

    private void SetEditorMode(EditorMode editorMode)
    {
        Component.SetString(DocumentConstants.EditorModeProperty, editorMode.ToString());
    }
}
