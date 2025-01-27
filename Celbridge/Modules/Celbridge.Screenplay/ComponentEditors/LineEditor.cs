using Celbridge.Entities;

namespace Celbridge.Screenplay.Components;

public class LineEditor : ComponentEditorBase
{
    public const string ComponentType = "Screenplay.Line";
    public const string Character = "/character";
    public const string SourceText = "/sourceText";

    public override string ComponentConfigPath => "Celbridge.Screenplay.Assets.Components.Line.json";

    public override Result<ComponentSummary> GetComponentSummary()
    {
        var getCharacter = GetProperty("/character");
        if (getCharacter.IsFailure)
        {
            return Result<ComponentSummary>.Fail(getCharacter.Error);
        }
        var character = getCharacter.Value;

        var getSourceText = GetProperty("/sourceText");
        if (getSourceText.IsFailure)
        {
            return Result<ComponentSummary>.Fail(getSourceText.Error);
        }
        var sourceText = getSourceText.Value;

        var summaryText = $"{character}: {sourceText}";
        var summary = new ComponentSummary(summaryText, summaryText);

        return Result<ComponentSummary>.Ok(summary);
    }
}

