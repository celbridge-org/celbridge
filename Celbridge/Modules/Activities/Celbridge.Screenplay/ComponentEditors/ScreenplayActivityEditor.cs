using Celbridge.Entities;

namespace Celbridge.Screenplay.Components;

public class ScreenplayActivityEditor : ComponentEditorBase
{
    public const string ActivityName = "Screenplay";

    public override string ComponentConfigPath => "Celbridge.Screenplay.Assets.Components.ScreenplayActivity.json";
}
