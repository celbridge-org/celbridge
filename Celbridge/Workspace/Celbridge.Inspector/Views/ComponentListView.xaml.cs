using Celbridge.Inspector.Models;
using Celbridge.Inspector.ViewModels;
using Celbridge.Logging;
using Microsoft.Extensions.Localization;
using Microsoft.UI.Input;
using System.Collections.ObjectModel;
using Windows.System;
using Windows.UI.Core;

namespace Celbridge.Inspector.Views;

public partial class ComponentListView : UserControl, IInspector
{
    private ILogger<ComponentListView> _logger;
    private IStringLocalizer _stringLocalizer;

    public LocalizedString AddComponentTooltipString => _stringLocalizer.GetString("EntityInspector_AddComponentTooltip");
    public LocalizedString ContextMenuAddTooltipString => _stringLocalizer.GetString("EntityInspector_ContextMenu_Add");
    public LocalizedString ContextMenuDeleteTooltipString => _stringLocalizer.GetString("EntityInspector_ContextMenu_Delete");
    public LocalizedString ContextMenuDuplicateTooltipString => _stringLocalizer.GetString("EntityInspector_ContextMenu_Duplicate");

    public ComponentListViewModel ViewModel { get; private set; }

    // Code gen requires a parameterless constructor
    public ComponentListView()
    {
        throw new NotImplementedException();
    }

    public ComponentListView(ComponentListViewModel viewModel)
    {
        this.InitializeComponent();

        ViewModel = viewModel;
        DataContext = ViewModel;

        var serviceProvider = ServiceLocator.ServiceProvider;
        _logger = serviceProvider.GetRequiredService<ILogger<ComponentListView>>();
        _stringLocalizer = serviceProvider.GetRequiredService<IStringLocalizer>();

        Loaded += (s, e) => ViewModel.OnViewLoaded();
        Unloaded += (s, e) => ViewModel.OnViewUnloaded();

#if WINDOWS
        // Remove the distracting animations when items are added or removed from the list
        ComponentList.ItemContainerTransitions.Clear();
#endif
    }

    public ResourceKey Resource
    {
        set => ViewModel.Resource = value;
        get => ViewModel.Resource;
    }

    private void UserControl_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var key = e.Key;

        // Delete selected component keyboard shortcut
        if (key == VirtualKey.Delete)
        {
            int deleteIndex = ComponentList.SelectedIndex;
            if (deleteIndex >= 0)
            {
                ViewModel.DeleteComponentCommand.Execute(deleteIndex);
            }
            e.Handled = true;
            return;
        }
    }

    private void ComponentList_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_focusCount != 0 ||
            ComponentList.SelectedIndex < 0)
        {
            // These shortcuts only apply when not editing a component type text box
            return;
        }

        if (e.Key == VirtualKey.D)
        {
            // Duplicate selected component keyboard shortcut

            var controlDown = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
            if (controlDown)
            {
                var componentItem = ComponentList.SelectedItem as ComponentItem;
                if (componentItem is not null)
                {
                    ViewModel.DuplicateComponentCommand.Execute(componentItem);
                }
            }
        }
        else if (e.Key == VirtualKey.Enter)
        {
            var shiftDown = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
            if (shiftDown)
            {
                // Shift + Enter adds a new component after the current component
                int componentIndex = ComponentList.SelectedIndex;
                ViewModel.AddComponentCommand.Execute(componentIndex);
                ViewModel.SelectedIndex = componentIndex + 1;
                e.Handled = true;
            }
            else
            {
                var listViewItem = ComponentList.ContainerFromIndex(ComponentList.SelectedIndex) as ListViewItem;
                if (listViewItem is not null)
                {
                    var textBlock = FindChild<TextBlock>(listViewItem, "DisplayTextBlock");
                    if (textBlock is not null)
                    {
                        SelectDisplayTextBlock(textBlock);

                        // Mark event as handled. Otherwise, the list view would handle the enter key event and select the list view item,
                        // causing the text box to lose focus.
                        e.Handled = true;
                    }
                }
            }
        }
        else if (e.Key == VirtualKey.Up || e.Key == VirtualKey.Down)
        {
            // Alt + Up/Down moves the selected component up or down
            var altDown = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu).HasFlag(CoreVirtualKeyStates.Down);
            if (altDown)
            {
                int componentIndex = ComponentList.SelectedIndex;
                int newComponentIndex = componentIndex + (e.Key == VirtualKey.Up ? -1 : 1);

                if (newComponentIndex < 0 || newComponentIndex >= ViewModel.ComponentItems.Count)
                {
                    // New index would be outside the collection bounds, ignore the input.
                    return;
                }

                // Update the list view to reflect the new order
                ViewModel.ComponentItems.Move(componentIndex, newComponentIndex);

                // Update the entity to reflect the new order
                ViewModel.MoveComponentCommand.Execute((componentIndex, newComponentIndex));

                e.Handled = true;
            }
        }
    }

    private int _dragStartIndex = -1;

    private void ComponentList_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        var listView = sender as ListView;
        if (listView?.ItemsSource is ObservableCollection<ComponentItem> items)
        {
            var draggedItem = e.Items.FirstOrDefault();
            _dragStartIndex = items.IndexOf((ComponentItem)draggedItem!);
        }
    }

    private void ComponentList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (sender.ItemsSource is ObservableCollection<ComponentItem> items)
        {
            var draggedItem = args.Items.FirstOrDefault();
            int dragEndIndex = items.IndexOf((ComponentItem)draggedItem!);

            ViewModel.MoveComponentCommand.Execute((_dragStartIndex, dragEndIndex));
        }

        _dragStartIndex = -1;
    }

    private void ComponentMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            ComponentContextMenu.Placement = FlyoutPlacementMode.Bottom;
            ComponentContextMenu.ShowAt(button);
        }
    }

    // If the user switches between editing component types for two different components, then the focus event for the
    // second component is sent _before_ the lost focus event for the first component.
    // We use a counter to track the number of focussed text boxes at any time, if it's greater than zero then we are in
    // component type editing mode.
    private int _focusCount;

    private void ComponentItem_DisplayTextBlock_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is TextBlock textBlock)
        {
            SelectDisplayTextBlock(textBlock);
        }
    }

    private void SelectDisplayTextBlock(TextBlock textBlock)
    {
        var parentGrid = (Grid)textBlock.Parent;

        var textBox = parentGrid.Children.OfType<TextBox>().FirstOrDefault();
        if (textBox != null)
        {
            textBlock.Visibility = Visibility.Collapsed;
            textBox.Visibility = Visibility.Visible;

            textBox.Focus(FocusState.Programmatic);
        }

        var menuButton = parentGrid.Children.OfType<Button>().FirstOrDefault();
        if (menuButton != null)
        {
            menuButton.Visibility = Visibility.Collapsed;
        }

        _focusCount++;

        ViewModel.IsEditingComponentType = _focusCount > 0;
    }

    private void ComponentItem_EditTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.SelectAll();
        }
    }

    private void ComponentItem_EditTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            var parentGrid = (Grid)textBox.Parent;

            if (parentGrid is null)
            {
                // Get a null parent here when the parent list view is destroyed while the text box has focus.
                // This happens when switching the inspector to another resource while editing the component type text.
                return;
            }

            var textBlock = parentGrid.Children.OfType<TextBlock>().FirstOrDefault();
            if (textBlock != null)
            {
                // Reset any entered text back to match the display text
                textBox.Text = textBlock.Text;

                textBox.Visibility = Visibility.Collapsed;
                textBlock.Visibility = Visibility.Visible;
            }

            var menuButton = parentGrid.Children.OfType<Button>().FirstOrDefault();
            if (menuButton != null)
            {
                menuButton.Visibility = Visibility.Visible;
            }

            _focusCount--;

            ViewModel.IsEditingComponentType = _focusCount > 0;
        }
    }

    private void ComponentItem_EditTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        bool selectListItem = false;

        if (e.Key == VirtualKey.Enter)
        {
            ViewModel.NotifyComponentTypeTextEntered();
            selectListItem = true;
        }
        else if (e.Key == VirtualKey.Escape)
        {
            selectListItem = true;
        }

        if (selectListItem)
        {
            var componentItem = textBox.DataContext as ComponentItem;
            Guard.IsNotNull(componentItem);

            int componentIndex = ViewModel.ComponentItems.IndexOf(componentItem);

            var listViewItem = ComponentList.ContainerFromIndex(componentIndex) as ListViewItem;
            if (listViewItem is not null)
            {
                listViewItem.IsSelected = true;
                listViewItem.Focus(FocusState.Programmatic);
            }
        }
    }

    private void EditTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var textBox = sender as TextBox;
        Guard.IsNotNull(textBox);

        var text = textBox.Text;
        ViewModel.NotifyComponentTypeTextChanged(text);
    }

    private T? FindChild<T>(DependencyObject parent, string childName) where T : FrameworkElement
    {
        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if (child is T childElement && childElement.Name == childName)
            {
                return childElement;
            }

            var foundChild = FindChild<T>(child, childName);
            if (foundChild != null)
            {
                return foundChild;
            }
        }
        return null;
    }
}
