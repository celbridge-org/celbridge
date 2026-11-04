using Windows.System;

namespace Celbridge.UserInterface.Views.Controls;

/// <summary>
/// A search box above a list of the project's files or folders, for a dialog that asks the user to choose one.
/// </summary>
public sealed partial class ResourceSearchListView : UserControl
{
    private readonly IStringLocalizer _stringLocalizer;

    public static readonly DependencyProperty SearchBoxMarginProperty = DependencyProperty.Register(
        nameof(SearchBoxMargin),
        typeof(Thickness),
        typeof(ResourceSearchListView),
        new PropertyMetadata(new Thickness(0)));

    /// <summary>
    /// The margin around the search box, which leaves room for anything the dialog draws beside the box.
    /// </summary>
    public Thickness SearchBoxMargin
    {
        get => (Thickness)GetValue(SearchBoxMarginProperty);
        set => SetValue(SearchBoxMarginProperty, value);
    }

    /// <summary>
    /// Raised when the user chooses the selected item with Enter or a double-click.
    /// </summary>
    public event EventHandler? ItemInvoked;

    /// <summary>
    /// Raised when the user presses Escape in the search box or the list.
    /// </summary>
    public event EventHandler? EscapePressed;

    public ResourcePickerDialogViewModel ViewModel { get; }

    private string LoadingString => _stringLocalizer.GetString("ResourcePickerDialog_Loading");

    public ResourceSearchListView()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        ViewModel = ServiceLocator.AcquireService<ResourcePickerDialogViewModel>();

        this.InitializeComponent();
    }

    /// <summary>
    /// Gives the search box the keyboard.
    /// </summary>
    public void FocusSearchBox()
    {
        SearchTextBox.Focus(FocusState.Programmatic);
    }

    private void SearchTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Down)
        {
            if (ViewModel.FilteredItems.Count > 0)
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
            // Handled, so the key doesn't reach the control that gets the keyboard back when the dialog closes.
            e.Handled = true;
            EscapePressed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ResourceList_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if (ViewModel.SelectedItem is not null)
        {
            ItemInvoked?.Invoke(this, EventArgs.Empty);
        }
    }

    // Uses PreviewKeyDown because the list handles Enter itself, so a KeyDown handler never sees it. A dialog that
    // starts to close gives the keyboard back to the control that opened the dialog. WinUI delivers this same key
    // to that control. The handler marks the key handled, so the Enter that chooses an item never also presses the
    // button that opened the dialog.
    private void ResourceList_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ViewModel.SelectedItem is not null)
        {
            e.Handled = true;
            ItemInvoked?.Invoke(this, EventArgs.Empty);
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            EscapePressed?.Invoke(this, EventArgs.Empty);
        }
    }
}
