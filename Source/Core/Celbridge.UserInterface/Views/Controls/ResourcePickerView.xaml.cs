using Celbridge.UserInterface.Helpers;
using Celbridge.UserInterface.ViewModels.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Celbridge.UserInterface.Views.Controls;

/// <summary>
/// Browses the project one folder at a time, for a dialog that asks the user to choose a file or a folder. A
/// location bar with a breadcrumb sits above the folder's contents. Text typed in the search box lists the matches
/// from the whole project in place of the folder.
/// </summary>
public sealed partial class ResourcePickerView : UserControl
{
    private readonly IStringLocalizer _stringLocalizer;

    /// <summary>
    /// Raised when the user chooses a file from the list in open mode, with Enter or a double-click on the file. In
    /// folder mode, Enter or a double-click only opens a folder, and the primary button chooses the current folder.
    /// </summary>
    public event EventHandler<ResourceKey>? ResourceChosen;

    /// <summary>
    /// Raised when the user presses Escape in the search box or the list, with the search box empty.
    /// </summary>
    public event EventHandler? EscapePressed;

    public ResourcePickerViewModel ViewModel { get; }

    private string LoadingString => _stringLocalizer.GetString("ResourcePickerDialog_Loading");
    private string SearchPlaceholderString => _stringLocalizer.GetString("ResourcePickerDialog_SearchPlaceholder");
    private string SearchLocationString => _stringLocalizer.GetString("ResourcePickerDialog_SearchLocation");
    private string UpString => _stringLocalizer.GetString("ResourcePickerDialog_Up");

    public ResourcePickerView()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        ViewModel = ServiceLocator.AcquireService<ResourcePickerViewModel>();

        this.InitializeComponent();

        var panelCornerRadius = (double)Application.Current.Resources["PanelCornerRadius"];
        ListArea.CornerRadius = new CornerRadius(panelCornerRadius);
    }

    /// <summary>
    /// Gives the search box the keyboard.
    /// </summary>
    public void FocusSearchBox()
    {
        SearchTextBox.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// Gives the list the keyboard, after the list opens a folder or goes up. An empty folder gives the keyboard to
    /// the search box.
    /// </summary>
    private void FocusList()
    {
        // Opening a folder replaces the list's rows, and the keyboard leaves with the old rows. The list is laid out
        // first, so it has rows that can take the keyboard back.
        if (ViewModel.ShownItems.Count == 0)
        {
            FocusSearchBox();
            return;
        }

        ResourceList.UpdateLayout();
        ResourceList.Focus(FocusState.Keyboard);
    }

    /// <summary>
    /// Scrolls the selected item into view, when an item is selected.
    /// </summary>
    public void ShowSelectedItem()
    {
        var selectedItem = ViewModel.SelectedItem;
        if (selectedItem is null)
        {
            return;
        }

        // A list that has just been filled ignores a request to scroll until it has been laid out.
        ResourceList.UpdateLayout();
        ResourceList.ScrollIntoView(selectedItem);
    }

    private void Root_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            // Escape clears the search first, wherever the keyboard is. A second Escape then reaches the dialog.
            if (ViewModel.ClearSearch())
            {
                e.Handled = true;
                FocusSearchBox();
            }
        }
        else if (e.Key == VirtualKey.Up
            && IsGoUpModifierDown())
        {
            // On macOS, while the search box has text, Command+Up is left to the search box. macOS moves the caret
            // to the start of the text.
            if (OperatingSystem.IsMacOS()
                && ViewModel.IsSearching)
            {
                return;
            }

            e.Handled = true;
            if (IsInListItem(e.OriginalSource))
            {
                _ = GoUpFromListAsync();
            }
            else
            {
                _ = ViewModel.GoUpAsync();
            }
        }
        else if (e.Key == VirtualKey.F
            && EditKeyboard.IsCommandModifierDown())
        {
            e.Handled = true;
            FocusSearchBox();
        }
    }

    // The modifier that goes up a level with Up. Windows uses Alt, which matches File Explorer. macOS uses Command,
    // which matches the Finder.
    private static bool IsGoUpModifierDown()
    {
        if (OperatingSystem.IsMacOS())
        {
            return EditKeyboard.IsCommandModifierDown();
        }

        return EditKeyboard.IsAltDown();
    }

    private void UpButton_Click(object sender, RoutedEventArgs e)
    {
        _ = ViewModel.GoUpAsync();
    }

    private void LocationBreadcrumb_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (args.Item is ResourcePickerCrumb crumb)
        {
            _ = ViewModel.OpenFolderAsync(crumb.Folder);
        }
    }

    private void SearchTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Down)
        {
            if (ViewModel.ShownItems.Count > 0)
            {
                ResourceList.Focus(FocusState.Programmatic);
                if (ResourceList.SelectedIndex < 0)
                {
                    ResourceList.SelectedIndex = 0;
                }
            }
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape)
        {
            // Marks the key handled. The control that gets the keyboard back when the dialog closes then never
            // receives this Escape.
            e.Handled = true;
            EscapePressed?.Invoke(this, EventArgs.Empty);
        }
    }

    // A double-click on the empty space below the rows leaves the selected item alone.
    private void ResourceList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (!IsInListItem(e.OriginalSource))
        {
            return;
        }

        _ = InvokeSelectedItemAsync();
    }

    // Uses PreviewKeyDown, because the list handles Enter itself and a KeyDown handler never sees it. A dialog that
    // starts to close gives the keyboard back to the control that opened the dialog. WinUI delivers this same key
    // to that control. The handler marks the key handled. The Enter that chooses an item then never presses the
    // button that opened the dialog.
    private void ResourceList_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter
            && ViewModel.SelectedItem is not null)
        {
            e.Handled = true;
            _ = InvokeSelectedItemAsync();
        }
        else if (e.Key == VirtualKey.Down
            && ViewModel.SelectedItem is null
            && ViewModel.ShownItems.Count > 0)
        {
            // With nothing selected, Down selects the first item.
            e.Handled = true;
            ResourceList.SelectedIndex = 0;
            FocusList();
        }
        else if (e.Key == VirtualKey.Back
            && !ViewModel.IsSearching)
        {
            e.Handled = true;
            _ = GoUpFromListAsync();
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            EscapePressed?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task InvokeSelectedItemAsync()
    {
        var selectedItem = ViewModel.SelectedItem;
        if (selectedItem is null)
        {
            return;
        }

        var chosenResource = await ViewModel.InvokeItemAsync(selectedItem);
        if (chosenResource is not null)
        {
            ResourceChosen?.Invoke(this, chosenResource.Value);
            return;
        }

        FocusList();
    }

    private async Task GoUpFromListAsync()
    {
        await ViewModel.GoUpAsync();
        FocusList();
    }

    // Returns true if the source is one of the list's rows, or inside one.
    private bool IsInListItem(object source)
    {
        if (source is not DependencyObject element)
        {
            return false;
        }

        var item = VisualTree.GetAncestors(element, includeSelf: true)
            .OfType<ListViewItem>()
            .FirstOrDefault();

        return item is not null
            && ItemsControl.ItemsControlFromItemContainer(item) == ResourceList;
    }
}
