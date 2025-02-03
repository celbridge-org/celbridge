using System.Text.Json.Nodes;
using Celbridge.Messaging;
using Celbridge.Workspace;

namespace Celbridge.Entities.Models;

public class ComponentProxy : IComponentProxy
{
    private IEntityService _entityService;
    private IMessengerService _messengerService;

    public bool IsValid { get; private set; } = true;

    public ComponentKey Key { get; }

    public ComponentSchema Schema { get; }

    public event Action<string>? ComponentPropertyChanged;

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

        _messengerService.Register<ComponentChangedMessage>(this, OnComponentChangedMessage);
    }

    // Property accessors

    public Result<string> GetProperty(string propertyPath)
    {
        return _entityService.GetProperty(Key, propertyPath);
    }

    public Result SetProperty(string propertyPath, string jsonValue, bool insert = false)
    {
        return _entityService.SetProperty(Key, propertyPath, jsonValue, insert);
    }

    public string GetString(string propertyPath, string defaultValue = "")
    {
        Guard.IsNotNull(defaultValue);

        var getResult = _entityService.GetProperty(Key, propertyPath);
        if (getResult.IsFailure)
        {
            return defaultValue;
        }
        var jsonValue = getResult.Value;

        var jsonNode = JsonNode.Parse(jsonValue);
        if (jsonNode == null)
        {
            return defaultValue;
        }

        var value = jsonNode.ToString();

        return value;
    }

    private void OnComponentChangedMessage(object recipient, ComponentChangedMessage message)
    {
        var componentKey = message.ComponentKey;
        var propertyPath = message.PropertyPath;

        if (message.ComponentKey == Key)
        {
            if (propertyPath == "/")
            {
                // Invalidate the proxy
                Invalidate();
            }
            else
            {
                // Notify listeners that a component property has changed
                ComponentPropertyChanged?.Invoke(propertyPath);
            }
        }
    }

    public void Invalidate()
    {
        if (IsValid)
        {
            IsValid = false;
            ComponentPropertyChanged = null;
            _messengerService.UnregisterAll(this);
        }
    }
}
