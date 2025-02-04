using Celbridge.Entities;

namespace Celbridge.Markdown.Components;

public class MarkdownEditor : ComponentEditorBase
{
    private const string _configPath = "Celbridge.Markdown.Assets.Components.MarkdownComponent.json";
    private const string _formPath = "Celbridge.Markdown.Assets.Forms.MarkdownForm.json";

    public const string ComponentType = "Markdown.Markdown";

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
}
