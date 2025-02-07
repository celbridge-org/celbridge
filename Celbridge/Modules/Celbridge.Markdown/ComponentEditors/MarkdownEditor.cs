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
