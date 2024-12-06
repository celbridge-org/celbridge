using Celbridge.Inspector.Models;
using Celbridge.Inspector.ViewModels;
using Microsoft.Extensions.Localization;
using Microsoft.UI.Input;
using System.Collections.ObjectModel;
using Windows.System;
using Windows.UI.Core;

namespace Celbridge.Inspector.Views;

public partial class EntityInspector : UserControl, IInspector
{
    private IStringLocalizer _stringLocalizer;

    public LocalizedString AddComponentTooltipString => _stringLocalizer.GetString("EntityInspector_AddComponentTooltip");
    public LocalizedString ContextMenuAddTooltipString => _stringLocalizer.GetString("EntityInspector_ContextMenu_Add");
    public LocalizedString ContextMenuDeleteTooltipString => _stringLocalizer.GetString("EntityInspector_ContextMenu_Delete");
    public LocalizedString ContextMenuDuplicateTooltipString => _stringLocalizer.GetString("EntityInspector_ContextMenu_Duplicate");

    public EntityInspectorViewModel ViewModel { get; private set; }

    // Code gen requires a parameterless constructor
    public EntityInspector()
    {
        throw new NotImplementedException();
    }

    public EntityInspector(EntityInspectorViewModel viewModel)
    {
        this.InitializeComponent();

        ViewModel = viewModel;
        DataContext = ViewModel;

        var serviceProvider = ServiceLocator.ServiceProvider;
        _stringLocalizer = serviceProvider.GetRequiredService<IStringLocalizer>();

        Loaded += EntityInspector_Loaded;
    }

    private void EntityInspector_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnViewLoaded();
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

    private int _dragStartIndex = -1;

    private void ListView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        var listView = sender as ListView;
        if (listView?.ItemsSource is ObservableCollection<ComponentItem> items)
        {
            var draggedItem = e.Items.FirstOrDefault();
            _dragStartIndex = items.IndexOf((ComponentItem)draggedItem!);
        }
    }

    private void ListView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
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
            EntityContextMenu.Placement = FlyoutPlacementMode.Bottom;
            EntityContextMenu.ShowAt(button);
        }
    }

    private void ComponentItem_DisplayTextBlock_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is TextBlock textBlock)
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
        }
    }

    private void ComponentItem_EditTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            var parentGrid = (Grid)textBox.Parent;

            var textBlock = parentGrid.Children.OfType<TextBlock>().FirstOrDefault();
            if (textBlock != null)
            {
                textBox.Visibility = Visibility.Collapsed;
                textBlock.Visibility = Visibility.Visible;
            }

            var menuButton = parentGrid.Children.OfType<Button>().FirstOrDefault();
            if (menuButton != null)
            {
                menuButton.Visibility = Visibility.Visible;
            }
        }
    }

    private void ComponentItem_EditTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var key = e.Key;
        var shiftDown = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);

        // Shift + Enter adds a new component after the current component
        if (key == VirtualKey.Enter && 
            shiftDown &&
            sender is TextBox textBox)
        {
            var componentItem = textBox.DataContext as ComponentItem;
            if (componentItem != null)
            {
                int index = ViewModel.ComponentItems.IndexOf(componentItem);
                ViewModel.AddComponentCommand.Execute(index);
            }

            e.Handled = true;
        }
    }
}
