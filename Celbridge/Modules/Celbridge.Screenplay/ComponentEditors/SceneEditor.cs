using Celbridge.Entities;

namespace Celbridge.Screenplay.Components;

public class SceneEditor : ComponentEditorBase
{
    public const string ComponentType = "Screenplay.Scene";
    public const string SceneTitle = "/sceneTitle";
    public const string SceneDescription = "/sceneDescription";

    public override string ComponentConfigPath => "Celbridge.Screenplay.Assets.Components.Scene.json";

    public override Result<ComponentSummary> GetComponentSummary()
    {
        var getTitle = GetProperty("/sceneTitle");
        if (getTitle.IsFailure)
        {
            return Result<ComponentSummary>.Fail(getTitle.Error);
        }
        var sceneTitle = getTitle.Value;

        var getDescriptionText = GetProperty("/sceneDescription");
        if (getDescriptionText.IsFailure)
        {
            return Result<ComponentSummary>.Fail(getDescriptionText.Error);
        }
        var sceneDescription = getDescriptionText.Value;

        var summaryText = $"{sceneTitle}: {sceneDescription}";
        var summary = new ComponentSummary(summaryText, summaryText);

        return Result<ComponentSummary>.Ok(summary);
    }
}
