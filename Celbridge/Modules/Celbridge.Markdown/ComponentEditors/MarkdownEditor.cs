using Celbridge.Entities;

namespace Celbridge.Markdown.Components;

public class MarkdownEditor : ComponentEditorBase
{
    public const string ComponentType = "Markdown.Markdown";

    public override string ComponentConfigPath => "Celbridge.Markdown.Assets.Components.Markdown.json";
}
