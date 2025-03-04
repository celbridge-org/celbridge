using Celbridge.Entities;

namespace Celbridge.Screenplay.Components;

public class SceneEditor : ComponentEditorBase
{
    private const string _configPath = "Celbridge.Screenplay.Assets.Components.SceneComponent.json";
    private const string _formPath = "Celbridge.Screenplay.Assets.Forms.SceneForm.json";

    public const string ComponentType = "Screenplay.Scene";
    public const string Category = "/category";
    public const string Namespace = "/namespace";

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
        var categoryText = Component.GetString(Category);
        var namespaceText = Component.GetString(Namespace);

        var summaryText = $"{categoryText}: {namespaceText}";
        return new ComponentSummary(summaryText, summaryText);
    }
}
