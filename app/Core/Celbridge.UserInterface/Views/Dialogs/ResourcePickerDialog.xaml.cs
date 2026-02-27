using Celbridge.Dialog;
using Windows.System;

namespace Celbridge.UserInterface.Views;

public sealed partial class ResourcePickerDialog : ContentDialog, IResourcePickerDialog
{
    private readonly IStringLocalizer _stringLocalizer;
    private bool _confirmed;
    private string? _customTitle;

    public ResourcePickerDialogViewModel ViewModel { get; }

    public string TitleString => _customTitle ?? _stringLocalizer.GetString("ResourcePickerDialog_Title");
    public string OkString => _stringLocalizer.GetString("DialogButton_Ok");
    public string CancelString => _stringLocalizer.GetString("DialogButton_Cancel");
    public string SearchPlaceholderString => _stringLocalizer.GetString("ResourcePickerDialog_SearchPlaceholder");

    public ResourcePickerDialog()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        var userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        XamlRoot = userInterfaceService.XamlRoot as XamlRoot;

        ViewModel = ServiceLocator.AcquireService<ResourcePickerDialogViewModel>();

        this.InitializeComponent();
        this.EnableThemeSync();
    }

    public void SetTitle(string title)
    {
        _customTitle = title;
        // Update the Title binding since it was already evaluated
        Title = title;
    }
    private void Dialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        SearchTextBox.Focus(FocusState.Programmatic);
    }

    private void SearchTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Down)
        {
            // Move focus to the list
            if (ViewModel.FilteredItems.Count > 0)
            {
                ResourceListView.Focus(FocusState.Programmatic);
                if (ResourceListView.SelectedIndex < 0)
                {
                    ResourceListView.SelectedIndex = 0;
                }
            }
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape)
        {
            Hide();
        }
    }

    private void ResourceListView_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if (ViewModel.SelectedItem is not null)
        {
            _confirmed = true;
            Hide();
        }
    }

    private void ResourceListView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ViewModel.SelectedItem is not null)
        {
            _confirmed = true;
            Hide();
        }
        else if (e.Key == VirtualKey.Escape)
        {
            Hide();
        }
    }

    public async Task<Result<ResourceKey>> ShowDialogAsync()
    {
        _confirmed = false;
        var contentDialogResult = await ShowAsync();

        if (contentDialogResult == ContentDialogResult.Primary || _confirmed)
        {
            if (ViewModel.SelectedItem is { } selected)
            {
                return Result<ResourceKey>.Ok(selected.ResourceKey);
            }
        }

        return Result<ResourceKey>.Fail("Resource picker was cancelled");
    }
}
