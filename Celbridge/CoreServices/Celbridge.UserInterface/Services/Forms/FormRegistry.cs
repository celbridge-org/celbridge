using System.Text.Json;
using Celbridge.Forms;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Services.Forms;

public class FormRegistry
{
    private readonly IMessengerService _messengerService;

    private readonly Dictionary<string, JsonElement> _forms = new();
    private readonly HashSet<string> _workspaceForms = new();

    public FormRegistry(IMessengerService messengerService)
    {
        _messengerService = messengerService;

        _messengerService.Register<WorkspaceUnloadedMessage>(this, OnWorkspaceUnloadedMessage);
    }

    public Result RegisterFormConfig(string formName, string formConfigJSON, FormScope scope)
    {
        if (_forms.ContainsKey(formName))
        {
            return Result.Fail($"Form registration failed: A form with the name '{formName}' is already registered.");
        }

        var document = JsonDocument.Parse(formConfigJSON);
        var rootElement = document.RootElement;

        if (rootElement.ValueKind != JsonValueKind.Array)
        {
            return Result.Fail($"Form registration failed: The JSON form definition must be an array.");
        }

        _forms.Add(formName, rootElement);

        if (scope == FormScope.Workspace)
        {
            // Workspace forms are unregistered when the workspace closes
            _workspaceForms.Add(formName);
        }

        return Result.Ok();
    }

    public Result<JsonElement> GetFormConfig(string formName)
    {
        if (_forms.TryGetValue(formName, out var form))
        {
            return Result<JsonElement>.Ok(form);
        }

        return Result<JsonElement>.Fail($"Form not found: {formName}");
    }

    private void OnWorkspaceUnloadedMessage(object recipient, WorkspaceUnloadedMessage message)
    {
        foreach (var formName in _workspaceForms)
        {
            _forms.Remove(formName);
        }
        _workspaceForms.Clear();
    }
}
