using Celbridge.Activities;
using Celbridge.Commands;
using Celbridge.Console;
using Celbridge.Documents;
using Celbridge.Entities;
using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Screenplay.Components;
using Celbridge.Workspace;
using System.Text.Json;

namespace Celbridge.Spreadsheet.Services;

public class SpreadsheetActivity : IActivity
{
    private readonly ILogger<SpreadsheetActivity> _logger;
    private readonly IMessengerService _messengerService;
    private readonly ICommandService _commandService;
    private readonly IEntityService _entityService;

    public SpreadsheetActivity(
        ILogger<SpreadsheetActivity> logger,
        IMessengerService messengerService,
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _messengerService = messengerService;
        _commandService = commandService;
        _entityService = workspaceWrapper.WorkspaceService.EntityService;
    }

    public async Task<Result> ActivateAsync()
    {
        _messengerService.Register<DocumentSaveCompletedMessage>(this, OnDocumentSaveCompletedMessage);

        await Task.CompletedTask;
        return Result.Ok();
    }

    public async Task<Result> DeactivateAsync()
    {
        _messengerService.Unregister<DocumentSaveCompletedMessage>(this);

        await Task.CompletedTask;
        return Result.Ok();
    }

    public bool SupportsResource(ResourceKey resource)
    {
        var extension = Path.GetExtension(resource);
        return extension == ExplorerConstants.ExcelExtension;
    }

    public async Task<Result> InitializeResourceAsync(ResourceKey resource)
    {
        if (!SupportsResource(resource))
        {
            return Result.Fail($"This activity does not support this resource: {resource}");
        }

        var count = _entityService.GetComponentCount(resource);
        if (count > 0)
        {
            // Entity has already been initialized
            return Result.Ok();
        }

        // Add the Spreadsheet component
        var addResult = await _commandService.ExecuteImmediate<IAddComponentCommand>(command =>
        {
            command.ComponentKey = new ComponentKey(resource, 0);
            command.ComponentType = SpreadsheetEditor.ComponentType;
        });

        if (addResult.IsFailure)
        {
            return Result.Fail($"Failed to add component: {resource}")
                .WithErrors(addResult);
        }

        await Task.CompletedTask;

        return Result.Ok();
    }

    public Result AnnotateEntity(ResourceKey entity, IEntityAnnotation entityAnnotation)
    {
        var getComponents = _entityService.GetComponents(entity);
        if (getComponents.IsFailure)
        {
            return Result.Fail(entity, $"Failed to get entity components: '{entity}'")
                .WithErrors(getComponents);
        }
        var components = getComponents.Value;

        if (components.Count != entityAnnotation.ComponentAnnotationCount)
        {
            return Result.Fail(entity, $"Component count does not match annotation count: '{entity}'");
        }

        //
        // Root component must be "Data.Spreadsheet"
        //

        var rootComponent = components[0];
        if (rootComponent.IsComponentType(SpreadsheetEditor.ComponentType))
        {
            entityAnnotation.SetIsRecognized(0);
        }
        else
        {
            entityAnnotation.AddComponentError(0, new AnnotationError(
                AnnotationErrorSeverity.Critical,
                "Invalid root component",
                $"The root component must be a '{SpreadsheetEditor.ComponentType}'."));
        }

        //
        // Remaining components must have attribute "isSpreadsheetComponent"
        //

        for (int i = 1; i < components.Count; i++)
        {
            var component = components[i];

            if (component.IsComponentType(EntityConstants.EmptyComponentType))
            {
                // Skip empty components
                continue;
            }

            var isSpreadsheetComponent = component.SchemaReader.GetBooleanAttribute("isSpreadsheetComponent");

            if (isSpreadsheetComponent)
            {
                entityAnnotation.SetIsRecognized(i);
            }
            else
            {
                entityAnnotation.AddComponentError(i, new AnnotationError(
                    AnnotationErrorSeverity.Critical,
                    "Invalid component type",
                    "This component is not compatible with the 'Data.Spreadsheet' component"));
            }
        }

        return Result.Ok();
    }

    public async Task<Result> UpdateResourceContentAsync(ResourceKey resource, IEntityAnnotation entityAnnotation)
    {
        await Task.CompletedTask;

        return Result.Ok();
    }

    private void OnDocumentSaveCompletedMessage(object recipient, DocumentSaveCompletedMessage message)
    {
        var savedResource = message.DocumentResource;
        if (!SupportsResource(savedResource))
        {
            return;
        }

        var getComponentResult = _entityService.GetComponentOfType(savedResource, SpreadsheetEditor.ComponentType);
        if (getComponentResult.IsFailure)
        {
            return;
        }
        var component = getComponentResult.Value;

        var getResult = component.GetProperty("/runOnDataChange");
        if (getResult.IsFailure)
        {
            return;
        }

        var runOnDataChange = JsonSerializer.Deserialize<bool>(getResult.Value);
        if (!runOnDataChange)
        {
            return;
        }

        var scriptResource = component.GetString("/pythonScript");
        var arguments = component.GetString("/arguments");

        _logger.LogInformation($"Running script: {scriptResource} with arguments: {arguments}");

        _commandService.Execute<IRunCommand>(command =>
        {
            command.ScriptResource = scriptResource;
            command.Arguments = arguments;
        });
    }
}
