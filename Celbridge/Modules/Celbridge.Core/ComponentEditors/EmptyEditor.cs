using Celbridge.Entities;

namespace Celbridge.Core.Components;

public class EmptyEditor : ComponentEditorBase
{
    private const string _configPath = "Celbridge.Core.Assets.Components.Empty.json";

    public const string Comment = "/comment";

    public override string GetComponentConfig()
    {
        return LoadEmbeddedResource(_configPath);
    }

    public override ComponentSummary GetComponentSummary()
    {
        var comment = GetString("/comment");
        return new ComponentSummary(comment, comment);
    }
}
