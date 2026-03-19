using Celbridge.Commands;
using Celbridge.Console;
using Celbridge.Entities;
using Celbridge.Logging;

namespace Celbridge.Screenplay.Components;

public class SpreadsheetEditor : ComponentEditorBase
{
    private const string _configPath = "Celbridge.Spreadsheet.Assets.Components.SpreadsheetComponent.json";
    private const string _formPath = "Celbridge.Spreadsheet.Assets.Forms.SpreadsheetForm.json";

    public const string ComponentType = "Data.Spreadsheet";

    private readonly ILogger<SpreadsheetEditor> _logger;
    private readonly ICommandService _commandService;

    public SpreadsheetEditor(
        ILogger<SpreadsheetEditor> logger,
        ICommandService commandService)
    {
        _logger = logger;
        _commandService = commandService;
    }

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

    public override void OnButtonClicked(string buttonId)
    {
        if (buttonId == "RunScript")
        {
            RunScript();
        }
    }

    private void RunScript()
    {
        var scriptResource = Component.GetString("/pythonScript");
        var arguments = Component.GetString("/arguments");

        _logger.LogInformation($"Running script: {scriptResource} with arguments: {arguments}");

        _commandService.Execute<IRunCommand>(command =>
        {
            command.ScriptResource = scriptResource;
            command.Arguments = arguments;
        });
    }
}
