using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;

namespace CelLegacy.Views;

public partial class PropertyListItem : UserControl
{
    private Func<int, Result>? _onAdd;
    private Func<int, Result>? _onDuplicate;
    private Func<int, Result>? _onDelete;
    private Func<bool>? _canAddItem;

    public PropertyListItem()
    {
        this.InitializeComponent();
    }

    public IPropertyView? PropertyView { get; private set; }

    public void SetContainedItem(UIElement containedItem, Func<int, Result> onAdd, Func<int, Result> onDuplicate, Func<int, Result> onDelete, Func<bool> canAddItem)
    {
        Guard.IsNotNull(containedItem);
        Guard.IsNotNull(onAdd);
        Guard.IsNotNull(onDuplicate);
        Guard.IsNotNull(onDelete);
        Guard.IsNotNull(canAddItem);

        PropertyView = containedItem as IPropertyView;
        Guard.IsNotNull(PropertyView);

        _onAdd = onAdd;
        _onDuplicate = onDuplicate;
        _onDelete = onDelete;
        _canAddItem = canAddItem;

        ItemContainer.Children.Add(containedItem);
    }

    private void PropertyListItem_PointerEntered(object? sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerDeviceType is PointerDeviceType.Mouse or PointerDeviceType.Pen)
        {
            VisualStateManager.GoToState(sender as Control, "HoverButtonsShown", true);

            // Enable the add and duplicate buttons if the collection can be added to
            bool canAddItem = _canAddItem!.Invoke();
            AddButton.IsEnabled = canAddItem;
            DuplicateButton.IsEnabled = canAddItem;
        }
    }

    private void PropertyListItem_PointerExited(object? sender, PointerRoutedEventArgs e)
    {
        VisualStateManager.GoToState(sender as Control, "HoverButtonsHidden", true);
    }

    private void Add_Click(object? sender, RoutedEventArgs e)
    {
        Guard.IsNotNull(PropertyView);

        var result = _onAdd!.Invoke(PropertyView.ItemIndex);
        if (result is ErrorResult error)
        {
            Log.Error($"Failed to add item: {error.Message}");
        }
    }

    private void Duplicate_Click(object? sender, RoutedEventArgs e)
    {
        Guard.IsNotNull(PropertyView);

        var result = _onDuplicate!.Invoke(PropertyView.ItemIndex);
        if (result is ErrorResult error)
        {
            Log.Error($"Failed to duplicate item: {error.Message}");
        }
    }

    private void Delete_Click(object? sender, RoutedEventArgs e)
    {
        Guard.IsNotNull(PropertyView);

        var result = _onDelete!.Invoke(PropertyView.ItemIndex);
        if (result is ErrorResult error)
        {
            Log.Error($"Failed to delete item: {error.Message}");
        }
    }
}
