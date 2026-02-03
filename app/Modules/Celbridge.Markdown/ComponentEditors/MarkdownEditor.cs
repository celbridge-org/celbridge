using System.Text.Json;
using Celbridge.Documents;
using Celbridge.Entities;

namespace Celbridge.Markdown.ComponentEditors;

public class MarkdownEditor : ComponentEditorBase
{
    private const string ConfigPath = "Celbridge.Markdown.Assets.Components.MarkdownComponent.json";
    private const string ComponentFormPath = "Celbridge.Markdown.Assets.Forms.MarkdownForm.json";
    private const string ComponentRootFormPath = "Celbridge.Markdown.Assets.Forms.MarkdownRootForm.json";

    private const string EditorButtonId = "Editor";
    private const string EditorAndPreviewButtonId = "EditorAndPreview";
    private const string PreviewButtonId = "Preview";

    public const string ComponentType = "Markdown.Markdown";

    public MarkdownEditor()
    { }

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
            case EditorButtonId:
                SetEditorMode(EditorMode.Editor);
                break;

            case EditorAndPreviewButtonId:
                SetEditorMode(EditorMode.EditorAndPreview);
                break;

            case PreviewButtonId:
                SetEditorMode(EditorMode.Preview);
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

    private void SetEditorMode(EditorMode editorMode)
    {
        Component.SetString(DocumentConstants.EditorModeProperty, editorMode.ToString());
    }
}
