using Celbridge.Entities;

namespace Celbridge.Core.Components;

public class EmptyEditor : ComponentEditorBase
{
    private const string _configPath = "Celbridge.Core.Assets.Components.EmptyComponent.json";
    private const string _formPath = "Celbridge.Core.Assets.Forms.EmptyForm.json";

    public const string Comment = "/comment";

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
        var comment = Component.GetString(Comment);
        return new ComponentSummary(comment, comment);
    }
}
