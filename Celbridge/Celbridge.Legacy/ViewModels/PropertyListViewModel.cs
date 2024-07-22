using Celbridge.Messaging;
using System.Collections;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.Serialization;
using Windows.Foundation.Collections;

namespace Celbridge.Legacy.ViewModels;

public record ListViewIsActiveMessage(ListView ListView);

public partial class PropertyListViewModel : ObservableObject
{
    private IMessengerService _messengerService;
    private readonly IInspectorService _inspectorService;

    public ListView? ListView { get; set; }
    public Property? Property { get; set; }
    public int ItemIndex { get; set; }
    public string LabelText { get; set; } = string.Empty;

    public int MaxLength { get; set; } = -1;

    private int _lastRemovedIndex = -1;
    private int _lastInsertedIndex = -1;
    private bool _isDragging;

    public PropertyListViewModel(IMessengerService messengerService,
                                 IInspectorService inspectorService)
    {
        _messengerService = messengerService;
        _inspectorService = inspectorService;

        PropertyChanged += PropertyListViewModel_PropertyChanged;
    }

    public void OnViewLoaded(ListView listView)
    {
        // Only listen for these messages while the view is actually loaded
        _messengerService.Register<SelectedCollectionItemGotFocusMessage>(this, OnSelectedCollectionItemGotFocus);
        _messengerService.Register<ListViewIsActiveMessage>(this, OnSetActiveListView);
        ListView = listView;
    }

    public void OnViewUnloaded()
    {
        _messengerService.Unregister<SelectedCollectionItemGotFocusMessage>(this);
        _messengerService.Unregister<ListViewIsActiveMessage>(this);
        ListView = null;
    }

    [ObservableProperty]
    public bool _isEmpty = true;

    public ICommand AddItemCommand => new RelayCommand(AddItem_Executed);
    private void AddItem_Executed()
    {
        Guard.IsNotNull(ListView);
        var itemCollection = ListView.Items;

        var result = AddItem(itemCollection.Count);
        if (result is ErrorResult error)
        {
            Log.Error($"Failed to add item: {error.Message}");
        }
    }

    public ICommand DragItemsStartingCommand => new RelayCommand<DragItemsStartingEventArgs>(DragItemsStarting_Executed);
    private void DragItemsStarting_Executed(DragItemsStartingEventArgs? e)
    {
        _isDragging = true;
    }

    public ICommand DragItemsCompletedCommand => new RelayCommand<DragItemsCompletedEventArgs>(DragItemsCompleted_Executed);
    private void DragItemsCompleted_Executed(DragItemsCompletedEventArgs? e)
    {
        _isDragging = false;

        if (_lastRemovedIndex >= 0 && _lastInsertedIndex >= 0)
        {
            int lastRemovedIndex = _lastRemovedIndex;
            int lastInsertedIndex = _lastInsertedIndex;

            // Safer to reset the values even if the MoveItem fails
            _lastRemovedIndex = -1;
            _lastInsertedIndex = -1;

            var result = MoveItem(lastRemovedIndex, lastInsertedIndex);
            if (result is ErrorResult error)
            {
                Log.Error($"Failed to add item: {error.Message}");
            }
        }
    }

    public Result PopulateListView()
    {
        Guard.IsNotNull(ListView);
        var itemCollection = ListView.Items;

        // Remove the event handler first in case this call is a refresh
        itemCollection.VectorChanged -= ViewItems_VectorChanged;
        itemCollection.Clear();

        Guard.IsNotNull(Property);
        var propInfo = Property.PropertyInfo;
        var entity = Property.Object;

        var list = propInfo.GetValue(entity) as IList;
        Guard.IsNotNull(list);

        var count = list.Count;
        for (int i = 0; i < count; i++)
        {
            var result = CreatePropertyViewAtIndex(i);
            if (result is ErrorResult error)
            {
                var message = ($"Failed to create populate list view. {error.Message}");
                return new ErrorResult(message);
            }
        }

        var maxLengthAttr = propInfo.GetCustomAttribute<MaxListLengthAttribute>();
        if (maxLengthAttr != null)
        {
            MaxLength = maxLengthAttr.MaxLength > 0 ? maxLengthAttr.MaxLength : -1;
        }

        // Listen for changes to the collection caused by the user reordering the ListView items
        itemCollection.VectorChanged += ViewItems_VectorChanged;

        return new SuccessResult();
    }

    private Result AddItem(int itemIndex)
    {
        if (!CanAddItem())
        {
            var message = $"The maximum number of items ({MaxLength}) has been reached.";
            return new ErrorResult(message);
        }

        Guard.IsNotNull(ListView);
        var itemCollection = ListView.Items;

        // To match the behaviour of Duplicate and Delete, the new item is inserted at the itemIndex + 1.
        // Negative indices are inserted at the start of the list.
        // Indices greater than or equal to the list count are added at the end of the list.
        Guard.IsTrue(itemIndex >= -1 && itemIndex <= itemCollection.Count);

        Guard.IsNotNull(Property);
        var propInfo = Property.PropertyInfo;
        var entity = Property.Object;

        try
        {
            // Get the current list from the property value
            var list = (IList?)propInfo.GetValue(entity);
            Guard.IsNotNull(list);
            Guard.IsTrue(list.Count == itemCollection.Count);

            int newIndex = itemIndex + 1;
            newIndex = Math.Clamp(newIndex, 0, list.Count);

            var originalType = propInfo.PropertyType.GetGenericArguments()[0];
            Guard.IsNotNull(originalType);

            object? newItem = null;
            if (originalType == typeof(string))
            {
                // Strings require special handling because the other two methods 
                // fail with the error "Uninitialized Strings cannot be created"
                newItem = string.Empty;
            }
            else if (HasDefaultConstructor(originalType))
            {
                newItem = Activator.CreateInstance(originalType);
            }
            else
            {
                // If the type has no parameterless constructor, this method creates
                // a zeroed instance of the type.
                newItem = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(originalType);
            }

            if (newIndex == list.Count)
            {
                list.Add(newItem);
            }
            else
            {
                list.Insert(newIndex, newItem);
            }

            // If both the parent and the new child are INodes, then set the child node's parent.
            if (Property.Object is ITreeNode parentNode &&
                newItem is ITreeNode childNode)
            {
                ParentNodeRef.SetParent(childNode, parentNode);
            }

            // Update the ItemCollection to match the list
            return CreatePropertyViewAtIndex(newIndex);
        }
        catch (Exception ex)
        {
            return new ErrorResult($"Failed to add item. {ex.Message}");
        }
    }

    private Result DuplicateItem(int itemIndex)
    {
        if (!CanAddItem())
        {
            var message = $"The maximum number of items ({MaxLength}) has been reached.";
            return new ErrorResult(message);
        }

        Guard.IsNotNull(ListView);
        var itemCollection = ListView.Items;

        Guard.IsTrue(itemIndex >= 0 && itemIndex < itemCollection.Count);

        Guard.IsNotNull(Property);
        var propInfo = Property.PropertyInfo;
        var entity = Property.Object;

        try
        {
            // Get the current list from the property value
            var list = (IList?)propInfo.GetValue(entity);
            Guard.IsNotNull(list);
            Guard.IsTrue(list.Count == itemCollection.Count);

            // Get the item at the specified index
            var originalItem = list[itemIndex];
            Guard.IsNotNull(originalItem);

            var jsonSettings = JsonSettings.Create();

            // Lists may only contain types that are serializable to Json, so the most robust way to 
            // deep copy an item is to serialize it to Json and then deserialize it back into a new object.
            var json = JsonConvert.SerializeObject(originalItem, jsonSettings);
            var copy = JsonConvert.DeserializeObject(json, originalItem.GetType(), jsonSettings);

            if (originalItem is ITreeNode originalNode &&
                originalNode.ParentNode.TreeNode is not null)
            {
                // The copy should have the same parent as the original
                var copyNode = copy as ITreeNode;
                Guard.IsNotNull(copyNode);

                ParentNodeRef.SetParent(copyNode, originalNode.ParentNode.TreeNode);
            }

            // Insert the new item into the list after the original item
            list.Insert(itemIndex + 1, copy);

            // If both the parent and the new child are INodes, then set the child node's parent.
            if (Property.Object is ITreeNode parentNode &&
                copy is ITreeNode childNode)
            {
                ParentNodeRef.SetParent(childNode, parentNode);
            }

            // Update the ItemCollection to match the list
            return CreatePropertyViewAtIndex(itemIndex + 1);
        }
        catch (Exception ex)
        {
            return new ErrorResult($"Failed to duplicate item. {ex.Message}");
        }
    }

    private Result DeleteItem(int itemIndex)
    {
        Guard.IsNotNull(ListView);
        var itemCollection = ListView.Items;

        Guard.IsTrue(itemIndex >= 0 && itemIndex < itemCollection.Count);

        Guard.IsNotNull(Property);
        var propInfo = Property.PropertyInfo;
        var entity = Property.Object;

        try
        {
            // Get the list object from the entity property
            var list = (IList?)propInfo.GetValue(entity);
            Guard.IsNotNull(list);

            Guard.IsTrue(list.Count == itemCollection.Count);
            Guard.IsTrue(itemIndex >= 0 && itemIndex < list.Count);

            // Remove the item from the list, and the corresponding view
            list.RemoveAt(itemIndex);

            // Update the ItemCollection to match the list
            return DeletePropertyViewAtIndex(itemIndex);
        }
        catch (Exception ex)
        {
            return new ErrorResult($"Failed to delete item. {ex.Message}");
        }
    }

    private Result MoveItem(int fromItemIndex, int toItemIndex)
    {
        Guard.IsNotNull(ListView);
        var itemCollection = ListView.Items;

        Guard.IsTrue(fromItemIndex >= 0 && fromItemIndex < itemCollection.Count);
        Guard.IsTrue(toItemIndex >= 0 && toItemIndex < itemCollection.Count);

        if (fromItemIndex == toItemIndex)
        {
            return new SuccessResult();
        }

        Guard.IsNotNull(Property);
        var propInfo = Property.PropertyInfo;
        var entity = Property.Object;

        try
        {
            // Get the current list from the property value
            var list = (IList?)propInfo.GetValue(entity);
            Guard.IsNotNull(list);
            Guard.IsTrue(list.Count == itemCollection.Count);

            var originalItem = list[fromItemIndex];
            Guard.IsNotNull(originalItem);

            var jsonSettings = JsonSettings.Create();

            var json = JsonConvert.SerializeObject(originalItem, jsonSettings);
            var copy = JsonConvert.DeserializeObject(json, originalItem.GetType(), jsonSettings);

            if (originalItem is ITreeNode originalNode &&
                originalNode.ParentNode.TreeNode is not null)
            {
                // The copy should have the same parent as the original
                var copyNode = copy as ITreeNode;
                Guard.IsNotNull(copyNode);
                ParentNodeRef.SetParent(copyNode, originalNode.ParentNode.TreeNode);
            }

            list.RemoveAt(fromItemIndex);
            list.Insert(toItemIndex, copy);

            // This method is called as the result of a drag and drop operation, so the ItemViews
            // are aleady in the correct order. We just need to update their indices.
            OnPropertyChanged(nameof(ListView));

            NotifyPropertyViewMovedToIndex(toItemIndex);

            return new SuccessResult();
        }
        catch (Exception ex)
        {
            return new ErrorResult($"Failed to swap item. {ex.Message}");
        }
    }

    private Result CreatePropertyViewAtIndex(int itemIndex)
    {
        Guard.IsNotNull(ListView);
        var itemCollection = ListView.Items;

        Guard.IsTrue(itemIndex <= itemCollection.Count);

        try
        {
            Guard.IsNotNull(Property);
            var result = PropertyViewUtils.CreatePropertyView(Property, itemIndex, string.Empty);
            if (result is ErrorResult<UIElement> error)
            {
                var message = ($"Failed to create property view. {error.Message}");
                return new ErrorResult(message);
            }
            var propertyView = result.Data!;

            // Wrap the listItem inside a container view that appears when the item is hovered over.
            // This container displays duplicate, delete, etc. buttons for interacting with the item.

            var container = new PropertyListItem();

            container.SetContainedItem(propertyView, AddItem, DuplicateItem, DeleteItem, CanAddItem);
            Guard.IsNotNull(container.PropertyView);

            container.PropertyView.ItemIndex = itemIndex;

            if (itemIndex == itemCollection.Count)
            {
                itemCollection.Add(container);
            }
            else
            {
                itemCollection.Insert(itemIndex, container);
            }

            OnPropertyChanged(nameof(ListView));
        }
        catch (Exception ex)
        {
            var message = ($"Failed to create property view at index {itemIndex}. {ex.Message}");
            return new ErrorResult(message);
        }

        return new SuccessResult();
    }

    private bool CanAddItem()
    {
        Guard.IsNotNull(ListView);
        return MaxLength == -1 || ListView.Items.Count < MaxLength;
    }

    private Result DeletePropertyViewAtIndex(int itemIndex)
    {
        Guard.IsNotNull(ListView);
        var itemCollection = ListView.Items;

        try
        {
            // Notify the property view that it is about to be deleted so that it
            // can notify its ViewModel if necessary.
            var propertyListItem = itemCollection[itemIndex] as PropertyListItem;
            if (propertyListItem != null)
            {
                var propertyView = propertyListItem.PropertyView;
                if (propertyView != null)
                {
                    propertyView.NotifyWillDelete();
                }
            }

            itemCollection.RemoveAt(itemIndex);
            OnPropertyChanged(nameof(ListView));
        }
        catch (Exception ex)
        {
            var message = ($"Failed to delete property view. {ex.Message}");
            return new ErrorResult(message);
        }

        return new SuccessResult();
    }

    private void NotifyPropertyViewMovedToIndex(int itemIndex)
    {
        Guard.IsNotNull(ListView);
        var itemCollection = ListView.Items;

        // Notify the property view that it's index in the list has changed.
        var propertyListItem = itemCollection[itemIndex] as PropertyListItem;
        if (propertyListItem != null)
        {
            var propertyView = propertyListItem.PropertyView;
            if (propertyView != null)
            {
                propertyView.NotifyIndexChanged(itemIndex);
            }
        }
    }

    private static bool HasDefaultConstructor(Type t)
    {
        return t.IsValueType || t.GetConstructor(Type.EmptyTypes) != null;
    }

    private void OnSelectedCollectionItemGotFocus(object recipient, SelectedCollectionItemGotFocusMessage message)
    {
        if (message.Property == Property)
        {
            // User has started editing the text box for a collection item.
            Guard.IsNotNull(ListView);
            ListView.SelectedIndex = message.Index;
        }
    }

    private void OnSetActiveListView(object recipient, ListViewIsActiveMessage message)
    {
        var activeListView = message.ListView;
        if (ListView != activeListView)
        {
            // This PropertyListView is not the currently active one, so deselect any items in it.
            Guard.IsNotNull(ListView);
            ListView.SelectedIndex = -1;
        }
    }

    private void PropertyListViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ListView))
        {
            Guard.IsNotNull(ListView);
            var itemCollection = ListView.Items;

            IsEmpty = itemCollection.Count == 0;

            // These should have been reset already, but let's make sure
            _lastRemovedIndex = -1;
            _lastInsertedIndex = -1;

            // Ensure that all container items have the correct index
            for (int i = 0; i < itemCollection.Count; i++)
            {
                var item = itemCollection[i] as PropertyListItem;
                Guard.IsNotNull(item);

                // The item index is actually stored in the property's ViewModel.
                // The view layers above that simply wrap this value, so we don't have to keep
                // the value in sync in multiple places.
                Guard.IsNotNull(item.PropertyView);
                item.PropertyView.ItemIndex = i;
            }

            // Notify observers of the property list object that the list has changed
            Guard.IsNotNull(Property);
            Property.NotifyPropertyChanged();

            if (Property == _inspectorService.SelectedCollection)
            {
                // An item in this collection is currently selected, i.e. displayed in the detail panel.
                // The detail panel now needs to be updated to ensure the indexing is up to date.
                int selectedIndex = _inspectorService.SelectedCollectionIndex;
                _inspectorService.SetSelectedCollection(null, -1);
                _inspectorService.SetSelectedCollection(Property, selectedIndex);
            }
        }
    }

    private void ViewItems_VectorChanged(IObservableVector<object> sender, IVectorChangedEventArgs e)
    {
        // Record indices in case this is a drag and drop operation
        switch (e.CollectionChange)
        {
            case CollectionChange.ItemInserted:
                _lastInsertedIndex = (int)e.Index;
                break;
            case CollectionChange.ItemRemoved:
                _lastRemovedIndex = (int)e.Index;
                break;
        }
    }

    public void SetSelectedIndex(int selectedIndex)
    {
        Guard.IsNotNull(Property);
        if (Property.Attributes.OfType<ShowDetailOnSelectAttribute>().Any())
        {
            // The [ShowDetailOnSelect] attribute is only valid on collections of IRecord objects.
            Guard.IsTrue(typeof(IRecord).IsAssignableFrom(Property.CollectionType));
        }
        else
        {
            // This type does not support selecting an item to display in the detail panel.
            // This check is particularly important when selecting a collection item in the
            // detail panel. Without this check, the selected item would try to display itself
            // in the detail panel.
            return;
        }


        if (selectedIndex < 0)
        {
            _inspectorService.SetSelectedCollection(null, -1);
            return;
        }
        else
        {
            // Notify the other list views that this is now the active one, so they should
            // deselect any item they currently have selected.
            Guard.IsNotNull(ListView);
            var message = new ListViewIsActiveMessage(ListView);
            _messengerService.Send(message);
        }

        if (_isDragging)
        {
            // We're in the middle of a drag and drop operation to reorder the list.
            // Any selection changed events generated during this process should be ignored.
            // The final selection will be set when the drag and drop operation completes.
            return;
        }

        _inspectorService.SetSelectedCollection(Property, selectedIndex);
    }
}
