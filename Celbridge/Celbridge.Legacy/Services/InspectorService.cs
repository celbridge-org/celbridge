using Celbridge.Messaging;
using CommunityToolkit.Mvvm.Messaging;

namespace Celbridge.Legacy.Services;

public interface IInspectorService
{
    IEntity? SelectedEntity { get; set; }
    object? SelectedItemUserData { get; set; }
    IProperty? SelectedCollection { get; }
    int SelectedCollectionIndex { get; }
    void SetSelectedCollection(IProperty? selectedCollection, int selectedIndex);
}

public record SelectedEntityChangedMessage(IEntity? Entity);

public record SelectedItemUserDataChangedMessage(object? UserData);

public record SelectedCollectionChangedMessage(IProperty? Property, int Index);

public record SelectedCollectionItemGotFocusMessage(IProperty Property, int Index);

public class DetailPropertyChangedMessage
{
    // The collection property that contains the detail collectionProperty
    public IProperty CollectionProperty { get; private set; }

    // The index into the collection where the detail property is stored.
    public int CollectionIndex { get; private set; }

    // The name of the property that changed on the detail property.
    public string ChangedPropertyName { get; private set; }

    public DetailPropertyChangedMessage(IProperty collectionProperty, int collectionIndex, string changedPropertyName)
    {
        CollectionProperty = collectionProperty;
        CollectionIndex = collectionIndex;
        ChangedPropertyName = changedPropertyName;
    }
}

public class EntityPropertyChangedMessage
{
    public IEntity Entity { get; private set; }
    public string PropertyName { get; private set; }

    public EntityPropertyChangedMessage(IEntity entity, string propertyName)
    {
        Entity = entity;
        PropertyName = propertyName;
    }
}

public class InspectorService : IInspectorService
{
    private readonly IMessengerService _messengerService;

    public InspectorService(IMessengerService messengerService)
    {
        _messengerService = messengerService;
        _messengerService.Register<DocumentClosedMessage>(this, OnDocumentClosed);
        _messengerService.Register<ActiveProjectChangedMessage>(this, OnActiveProjectChanged);
    }

    private void OnDocumentClosed(object recipient, DocumentClosedMessage message)
    {
        // This is hacky a catch all to avoid having every type of selectable entity having to
        // check if it's owner document has been closed. Instead, we just set the active entity
        // to null whenever any document is closed.
        SetSelectedCollection(null, -1);
    }

    private void OnActiveProjectChanged(object recipient, ActiveProjectChangedMessage message)
    {
        var activeProject = message.Project;
        if (activeProject == null)
        {
            // Ensure that no entity is selected if there's no project currently loaded
            SelectedEntity = null;
        }
    }

    private IEntity? _selectedEntity;
    public IEntity? SelectedEntity
    {
        get => _selectedEntity;
        set
        {
            if (_selectedEntity == value)
            {
                return;
            }

            _selectedEntity = value;

            // Clear any User Data that has been set previously
            SelectedItemUserData = null;

            SetSelectedCollection(null, -1);

            var message = new SelectedEntityChangedMessage(_selectedEntity);
            _messengerService.Send(message);
        }
    }

    // User Data is used to temporarily store information about the currently selected entity
    // or collection item. Clients can listen for a message to detect when the User Data is set,
    // or cleared when another entity or collection item is selected in the inspector.

    private object? _selectedItemUserData;
    public object? SelectedItemUserData
    {
        get => _selectedItemUserData;
        set
        {
            if (_selectedItemUserData == value)
            {
                return;
            }
            _selectedItemUserData = value;

            var message = new SelectedItemUserDataChangedMessage(_selectedItemUserData);
            _messengerService.Send(message);
        }
    }

    private IProperty? _selectedCollection;
    public IProperty? SelectedCollection => _selectedCollection;

    private int _selectedCollectionIndex = -1;
    public int SelectedCollectionIndex => _selectedCollectionIndex;

    public void SetSelectedCollection(IProperty? selectedCollection, int selectedIndex)
    {
        if (_selectedCollection == selectedCollection && _selectedCollectionIndex == selectedIndex)
        {
            return;
        }

        // Clear any User Data that has been set previously
        SelectedItemUserData = null;

        // Set both the collection property and the collection index prior to sending the message
        _selectedCollection = selectedCollection;
        _selectedCollectionIndex = selectedIndex;

        var collectionChangedMessage = new SelectedCollectionChangedMessage(_selectedCollection, _selectedCollectionIndex);
        _messengerService.Send(collectionChangedMessage);
    }
}