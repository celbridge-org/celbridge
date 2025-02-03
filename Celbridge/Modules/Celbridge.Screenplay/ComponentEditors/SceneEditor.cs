using Celbridge.Entities;

namespace Celbridge.Screenplay.Components;

public class SceneEditor : ComponentEditorBase
{
    private const string _configPath = "Celbridge.Screenplay.Assets.Components.Scene.json";

    public const string ComponentType = "Screenplay.Scene";
    public const string SceneTitle = "/sceneTitle";
    public const string SceneDescription = "/sceneDescription";

    public override string GetComponentConfig()
    {
        return LoadEmbeddedResource(_configPath);
    }

    public override ComponentSummary GetComponentSummary()
    {
        var sceneTitle = Component.GetString(SceneTitle);
        var sceneDescription = Component.GetString(SceneDescription);

        var summaryText = $"{sceneTitle}: {sceneDescription}";
        return new ComponentSummary(summaryText, summaryText);
    }
}
