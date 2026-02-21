using Celbridge.Entities;

namespace Celbridge.Markdown.ComponentEditors;

public class MarkdownEditor : ComponentEditorBase
{
    private const string ConfigPath = "Celbridge.Markdown.Assets.Components.MarkdownComponent.json";
    private const string ComponentFormPath = "Celbridge.Markdown.Assets.Forms.MarkdownForm.json";

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

    public override ComponentSummary GetComponentSummary()
    {
        return new ComponentSummary(string.Empty, string.Empty);
    }
}
