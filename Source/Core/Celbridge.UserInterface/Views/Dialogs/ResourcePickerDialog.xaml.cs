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
    private ResourcePickerItem? _answeredItem;
    private string? _customTitle;

    public ResourcePickerDialogViewModel ViewModel { get; }

    public string TitleString => _customTitle ?? _stringLocalizer.GetString("ResourcePickerDialog_Title");
    public string OkString => _stringLocalizer.GetString("DialogButton_Ok");
    public string CancelString => _stringLocalizer.GetString("DialogButton_Cancel");
    public string LoadingString => _stringLocalizer.GetString("ResourcePickerDialog_Loading");

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

        // The dialog opens at once and shows that it is loading. The walk fills the list when it finishes.
        _ = ViewModel.LoadItemsAsync();
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
            // Handled, so the key doesn't reach the control that gets the keyboard back as the dialog closes.
            e.Handled = true;
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

    // Uses PreviewKeyDown because the list handles Enter itself, so a KeyDown handler never sees it. The key must
    // be marked handled. As soon as the dialog starts to close, WinUI gives the keyboard back to the control that
    // opened it and delivers this same key to that control. Otherwise, pressing Enter to choose an item would also
    // press the button that opened the picker and open it again.
    private void ResourceListView_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ViewModel.SelectedItem is not null)
        {
            _confirmed = true;
            Hide();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape)
        {
            Hide();
            e.Handled = true;
        }
    }

    public async Task<Result<ResourceKey>> ShowDialogAsync()
    {
        _confirmed = false;
        _answeredItem = null;
        _messengerService.Register<DialogAnswerMessage>(this, OnDialogAnswer);
        try
        {
            var contentDialogResult = await ShowAsync();

            if (contentDialogResult == ContentDialogResult.Primary || _confirmed)
            {
                var selected = _answeredItem ?? ViewModel.SelectedItem;
                if (selected is not null)
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

    private async void OnDialogAnswer(object recipient, DialogAnswerMessage message)
    {
        if (message.Kind != DialogKind.ResourcePicker)
        {
            return;
        }

        if (!ResourceKey.TryCreate(message.Payload, out var targetKey))
        {
            _logger.LogWarning(
                $"Resource picker auto-answer failed: payload '{message.Payload}' is not a valid resource key.");
            Hide();
            return;
        }

        // The answer can arrive before the walk has listed the items. The answer can also name an item beyond the
        // shown list.
        await ViewModel.LoadItemsAsync();

        var match = ViewModel.FindMatchingItem(targetKey);
        if (match is null)
        {
            _logger.LogWarning(
                $"Resource picker auto-answer failed: no filtered item matches resource key '{targetKey}'.");
            Hide();
            return;
        }

        _answeredItem = match;
        _confirmed = true;
        _logger.LogInformation($"Resource picker answered automatically with '{targetKey}'.");
        Hide();
    }
}
