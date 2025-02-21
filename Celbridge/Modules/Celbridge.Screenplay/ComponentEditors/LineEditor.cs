using Celbridge.Entities;

namespace Celbridge.Screenplay.Components;

public class LineEditor : ComponentEditorBase
{
    private const string _configPath = "Celbridge.Screenplay.Assets.Components.LineComponent.json";
    private const string _formPath = "Celbridge.Screenplay.Assets.Forms.LineForm.json";

    public const string ComponentType = "Screenplay.Line";
    public const string Character = "/character";
    public const string SourceText = "/sourceText";

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
        var character = Component.GetString(Character);
        var sourceText = Component.GetString(SourceText);

        var summaryText = $"{character}: {sourceText}";
        return new ComponentSummary(summaryText, summaryText);
    }

    protected override Result<string> TryGetProperty(string propertyPath)
    {
        if (propertyPath == "/characterIds")
        {
            // Todo: Return the list of character Ids for the current screenplay
            var characterIds = "[\"Character 1\", \"Character 2\", \"Character 3\", \"Character 4\"]";
            return Result<string>.Ok(characterIds);
        }

        return Result<string>.Fail();
    }

}

