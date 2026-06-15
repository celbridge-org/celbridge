using Celbridge.Dialog;
using Celbridge.Logging;
using Windows.System;

namespace Celbridge.UserInterface.Views;

public sealed partial class ResourcePickerDialog : ContentDialog, IResourcePickerDialog
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ILogger<ResourcePickerDialog> _logger;
    private readonly IMessengerService _messengerService;
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
        _logger = ServiceLocator.AcquireService<ILogger<ResourcePickerDialog>>();
        _messengerService = ServiceLocator.AcquireService<IMessengerService>();

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
        _messengerService.Register<DialogAnswerMessage>(this, OnDialogAnswer);
        try
        {
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
        finally
        {
            _messengerService.UnregisterAll(this);
        }
    }

    private void OnDialogAnswer(object recipient, DialogAnswerMessage message)
    {
        if (!ResourceKey.TryCreate(message.Payload, out var targetKey))
        {
            _logger.LogWarning(
                $"Resource picker auto-answer failed: payload '{message.Payload}' is not a valid resource key.");
            Hide();
            return;
        }

        var match = ViewModel.FilteredItems.FirstOrDefault(item => item.ResourceKey.Equals(targetKey));
        if (match is null)
        {
            _logger.LogWarning(
                $"Resource picker auto-answer failed: no filtered item matches resource key '{targetKey}'.");
            Hide();
            return;
        }

        ViewModel.SelectedItem = match;
        _confirmed = true;
        _logger.LogInformation($"Resource picker answered automatically with '{targetKey}'.");
        Hide();
    }
}
