using Celbridge.Entities;

namespace Celbridge.Screenplay.Components;

public class SpreadsheetEditor : ComponentEditorBase
{
    private const string _configPath = "Celbridge.Spreadsheet.Assets.Components.SpreadsheetComponent.json";
    private const string _formPath = "Celbridge.Spreadsheet.Assets.Forms.SpreadsheetForm.json";

    public const string ComponentType = "Data.Spreadsheet";

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
        return new ComponentSummary(string.Empty, string.Empty);
    }
}
