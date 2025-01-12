using Celbridge.Messaging;
using Celbridge.Workspace;
using CommunityToolkit.Diagnostics;

namespace Celbridge.Entities.Models;

public class ComponentProxy : IComponentProxy
{
    private IEntityService _entityService;
    private IMessengerService _messengerService;

    public bool IsValid { get; set; } = true;

    public ComponentKey Key { get; }

    public ComponentSchema Schema { get; }

    public ComponentStatus Status { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public string Tooltip { get; private set; } = string.Empty;

    public ComponentProxy(IServiceProvider serviceProvider, ComponentKey componentKey, ComponentSchema schema)
    {
        var workspaceWraper = serviceProvider.GetRequiredService<IWorkspaceWrapper>();
        _entityService = workspaceWraper.WorkspaceService.EntityService;
        _messengerService = serviceProvider.GetRequiredService<IMessengerService>();

        Key = componentKey;
        Schema = schema;
    }

    public void SetAnnotation(ComponentStatus status, string description, string tooltip)
    {
        Status = status;
        Description = description;
        Tooltip = tooltip;

        var message = new ComponentAnnotationUpdatedMessage(Key);
        _messengerService.Send(message);
    }

    // Property accessors

    public Result<T> GetProperty<T>(string propertyPath) where T : notnull
    {
        return _entityService.GetProperty<T>(Key, propertyPath);
    }

    public T? GetProperty<T>(string propertyPath, T? defaultValue) where T : notnull
    {
        return _entityService.GetProperty(Key, propertyPath, defaultValue);
    }

    public string GetString(string propertyPath, string defaultValue = "")
    {
        Guard.IsNotNull(defaultValue);

        var getResult = _entityService.GetProperty<string>(Key, propertyPath);
        if (getResult.IsFailure)
        {
            return defaultValue;
        }

        return getResult.Value;
    }

    public Result SetProperty<T>(string propertyPath, T newValue, bool insert) where T : notnull
    {
        return _entityService.SetProperty(Key, propertyPath, newValue);
    }
}
