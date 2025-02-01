using Celbridge.Entities;

namespace Celbridge.Screenplay.Components;

public class LineEditor : ComponentEditorBase
{
    private const string _configPath = "Celbridge.Screenplay.Assets.Components.Line.json";

    public const string ComponentType = "Screenplay.Line";
    public const string Character = "/character";
    public const string SourceText = "/sourceText";

    public override string GetComponentConfig()
    {
        return LoadEmbeddedResource(_configPath);
    }

    public override ComponentSummary GetComponentSummary()
    {
        var character = GetString(Character);
        var sourceText = GetString(SourceText);

        var summaryText = $"{character}: {sourceText}";
        return new ComponentSummary(summaryText, summaryText);
    }
}

