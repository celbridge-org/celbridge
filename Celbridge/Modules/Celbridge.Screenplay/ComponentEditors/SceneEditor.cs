using Celbridge.Entities;

namespace Celbridge.Screenplay.Components;

public class SceneEditor : ComponentEditorBase
{
    private const string _configPath = "Celbridge.Screenplay.Assets.Components.SceneComponent.json";
    private const string _formPath = "Celbridge.Screenplay.Assets.Forms.SceneForm.json";

    public const string ComponentType = "Screenplay.Scene";
    public const string SceneTitle = "/sceneTitle";
    public const string SceneDescription = "/sceneDescription";

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
        var sceneTitle = Component.GetString(SceneTitle);
        var sceneDescription = Component.GetString(SceneDescription);

        var summaryText = $"{sceneTitle}: {sceneDescription}";
        return new ComponentSummary(summaryText, summaryText);
    }
}
