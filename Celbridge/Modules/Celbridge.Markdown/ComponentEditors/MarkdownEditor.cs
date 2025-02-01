using Celbridge.Entities;

namespace Celbridge.Markdown.Components;

public class MarkdownEditor : ComponentEditorBase
{
    private const string _configPath = "Celbridge.Markdown.Assets.Components.Markdown.json";

    public const string ComponentType = "Markdown.Markdown";

    public override string GetComponentConfig()
    {
        return LoadEmbeddedResource(_configPath);
    }

    public override ComponentSummary GetComponentSummary()
    {
        return new ComponentSummary(string.Empty, string.Empty);
    }
}
