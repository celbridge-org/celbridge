using System.Text.Json;
using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Entities;
using Celbridge.Logging;

namespace Celbridge.Markdown.Components;

public class MarkdownEditor : ComponentEditorBase
{
    private readonly ILogger<MarkdownEditor> _logger;
    private readonly ICommandService _commandService;

    private const string _configPath = "Celbridge.Markdown.Assets.Components.MarkdownComponent.json";
    private const string _formPath = "Celbridge.Markdown.Assets.Forms.MarkdownForm.json";

    private const string _openDocumentButtonId = "OpenDocument";
    private const string _editorButtonId = "Editor";
    private const string _editorAndPreviewButtonId = "EditorAndPreview";
    private const string _previewButtonId = "Preview";

    public const string ComponentType = "Markdown.Markdown";

    public MarkdownEditor(
        ILogger<MarkdownEditor> logger,
        ICommandService commandService)
    {
        _logger = logger;
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

    protected override void OnFormPropertyChanged(string propertyPath)
    {
        if (propertyPath == "/editorMode")
        {
            // Notify updates to "virtual" form properties
            NotifyFormPropertyChanged("/editorEnabled");
            NotifyFormPropertyChanged("/editorAndPreviewEnabled");
            NotifyFormPropertyChanged("/previewEnabled");
        }
    }

    public override void OnButtonClicked(string buttonId)
    {
        switch (buttonId)
        {
            case _openDocumentButtonId:
                OpenDocument();
                break;

            case _editorButtonId:
                SetEditorMode(EditorMode.Editor);
                OpenDocument();
                break;

            case _editorAndPreviewButtonId:
                SetEditorMode(EditorMode.EditorAndPreview);
                OpenDocument();
                break;

            case _previewButtonId:
                SetEditorMode(EditorMode.Preview);
                OpenDocument();
                break;
        }
    }

    protected override Result<string> TryGetProperty(string propertyPath)
    {
        if (propertyPath == "/editorEnabled")
        {
            var editorMode = Component.GetString(MarkdownComponentConstants.EditorMode);

            bool isEnabled = editorMode == "EditorAndPreview" || editorMode == "Preview";
            var jsonValue = JsonSerializer.Serialize(isEnabled);

            return Result<String>.Ok(jsonValue);
        }
        else if (propertyPath == "/editorAndPreviewEnabled")
        {
            var editorMode = Component.GetString(MarkdownComponentConstants.EditorMode);

            bool isEnabled = editorMode == "Editor" || editorMode == "Preview";
            var jsonValue = JsonSerializer.Serialize(isEnabled);

            return Result<String>.Ok(jsonValue);
        }
        else if (propertyPath == "/previewEnabled")
        {
            var editorMode = Component.GetString(MarkdownComponentConstants.EditorMode);

            bool isEnabled = editorMode == "Editor" || editorMode == "EditorAndPreview";
            var jsonValue = JsonSerializer.Serialize(isEnabled);

            return Result<String>.Ok(jsonValue);
        }

        return Result<string>.Fail();
    }


    private void OpenDocument()
    {
        var resource = Component.Key.Resource;

        // Execute a command to open the markdown document.
        _commandService.Execute<IOpenDocumentCommand>(command =>
        {
            command.FileResource = resource;
            command.ForceReload = false;
        });
    }

    private void SetEditorMode(EditorMode editorMode)
    {
        Component.SetString(MarkdownComponentConstants.EditorMode, editorMode.ToString());
    }
}
