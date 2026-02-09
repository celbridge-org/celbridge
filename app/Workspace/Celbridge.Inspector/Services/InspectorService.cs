using Celbridge.Documents;
using Celbridge.Entities;
using Celbridge.Forms;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Workspace;

namespace Celbridge.Inspector.Services;

/// <summary>
/// Message sent when the workspace requests the inspector to update.
/// </summary>
public record UpdateInspectorMessage();

public class InspectorService : IInspectorService, IDisposable
{
    private readonly ILogger<InspectorService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IMessengerService _messengerService;
    private readonly IFormService _formService;
    private readonly ComponentEditorCache _componentEditorCache;
    private readonly EntityAnnotationCache _entityAnnotationCache;

    public IInspectorFactory InspectorFactory { get; }

    public ResourceKey InspectedResource { get; private set; }

    public int InspectedComponentIndex { get; private set; }

    private ComponentPanelMode _componentPanelMode;
    public ComponentPanelMode ComponentPanelMode
    {
        get => _componentPanelMode;
        set
        {
            if (ComponentPanelMode == value)
            {
                return;
            }

            _componentPanelMode = value;

            var message = new ComponentPanelModeChangedMessage(_componentPanelMode);
            _messengerService.Send(message);
        }
    }

    public InspectorService(
        IServiceProvider serviceProvider,
        ILogger<InspectorService> logger,
        IMessengerService messengerService,
        IFormService formService,
        IWorkspaceWrapper workspaceWrapper)
    {
        // Only the workspace service is allowed to instantiate this service
        Guard.IsFalse(workspaceWrapper.IsWorkspacePageLoaded);

        _serviceProvider = serviceProvider;
        _logger = logger;
        _messengerService = messengerService;
        _formService = formService;

        _componentEditorCache = _serviceProvider.GetRequiredService<ComponentEditorCache>();
        _entityAnnotationCache = _serviceProvider.GetRequiredService<EntityAnnotationCache>();

        _messengerService.Register<SelectedDocumentChangedMessage>(this, OnSelectedDocumentChangedMessage);
        _messengerService.Register<SelectedComponentChangedMessage>(this, OnSelectedComponentChangedMessage);

        InspectorFactory = _serviceProvider.GetRequiredService<IInspectorFactory>();
        _formService = formService;
    }

    public async Task<Result> UpdateAsync()
    {
        await Task.CompletedTask;

        // Notify the inspector panel elements to update
        var message = new UpdateInspectorMessage();
        _messengerService.Send(message);

        return Result.Ok();
    }

    public Result<object> CreateComponentEditorForm(IComponentEditor componentEditor)
    {
        Guard.IsNotNull(componentEditor.Component);

        // Get the form config from the component editor
        var formName = componentEditor.Component.SchemaReader.Schema.ComponentType;
        var formConfig = componentEditor.GetComponentForm();

        // Create the form, using the component type as the form name for error reporting.
        var buildResult = _formService.CreateForm(formName, formConfig, componentEditor);
        if (buildResult.IsFailure)
        {
            return Result<object>.Fail($"Failed to build form for component type: '{formName}'")
                .WithErrors(buildResult);
        }
        var uiElement = buildResult.Value;

        return Result<object>.Ok(uiElement);
    }

    public Result<IComponentEditor> AcquireComponentEditor(ComponentKey componentKey)
    {
        return _componentEditorCache.AcquireComponentEditor(componentKey);
    }

    public Result<IEntityAnnotation> GetCachedEntityAnnotation(ResourceKey resource)
    {
        return _entityAnnotationCache.GetEntityAnnotation(resource);
    }

    private void OnSelectedDocumentChangedMessage(object recipient, SelectedDocumentChangedMessage message)
    {
        if (InspectedResource == message.DocumentResource)
        {
            // Avoid unnecessary cache invalidation and UI updates when the same document is re-selected
            return;
        }

        InspectedResource = message.DocumentResource;
        InspectedComponentIndex = -1;

        var changedMessage = new InspectedComponentChangedMessage(new ComponentKey(InspectedResource, InspectedComponentIndex));
        _messengerService.Send(changedMessage);
    }

    private void OnSelectedComponentChangedMessage(object recipient, SelectedComponentChangedMessage message)
    {
        InspectedComponentIndex = message.componentIndex;

        var changedMessage = new InspectedComponentChangedMessage(new ComponentKey(InspectedResource, InspectedComponentIndex));
        _messengerService.Send(changedMessage);
    }

    private bool _disposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed objects here
                _messengerService.UnregisterAll(this);
            }

            _disposed = true;
        }
    }

    ~InspectorService()
    {
        Dispose(false);
    }
}
