using Celbridge.Entities;

namespace Celbridge.Screenplay.Components;

public class LineEditor : ComponentEditorBase
{
    public const string ComponentType = "Line";
    public const string Character = "/character";
    public const string SourceText = "/sourceText";

    public override string ComponentConfigPath => "Celbridge.Screenplay.Assets.Components.Line.json";
}

