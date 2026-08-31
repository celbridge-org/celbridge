using Celbridge.Dialog;
using Celbridge.Logging;
using Windows.System;

namespace Celbridge.UserInterface.Views;

public sealed partial class IconPickerDialog : ContentDialog, IIconPickerDialog
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ILogger<IconPickerDialog> _logger;
    private readonly IMessengerService _messengerService;
    private bool _confirmed;

    public IconPickerDialogViewModel ViewModel { get; }

    public string TitleString => _stringLocalizer.GetString("IconPicker_DialogTitle");
    public string OkString => _stringLocalizer.GetString("DialogButton_Ok");
    public string CancelString => _stringLocalizer.GetString("DialogButton_Cancel");
    public string SearchPlaceholderString => _stringLocalizer.GetString("IconPicker_SearchPlaceholder");
    public string NoResultsString => _stringLocalizer.GetString("IconPicker_NoResults");

    public IconPickerDialog()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _logger = ServiceLocator.AcquireService<ILogger<IconPickerDialog>>();
        _messengerService = ServiceLocator.AcquireService<IMessengerService>();

        var userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        XamlRoot = userInterfaceService.XamlRoot as XamlRoot;

        ViewModel = ServiceLocator.AcquireService<IconPickerDialogViewModel>();

        this.InitializeComponent();
        this.EnableThemeSync();
    }

    private void Dialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        // The list opens on the icon the field already names, which is thousands of rows down for a name
        // late in the alphabet.
        if (ViewModel.SelectedItem is not null)
        {
            IconListView.ScrollIntoView(ViewModel.SelectedItem);
        }

        SearchTextBox.Focus(FocusState.Programmatic);
    }

    private void SearchTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Down)
        {
            // Move focus to the list
            if (ViewModel.FilteredItems.Count > 0)
            {
                IconListView.Focus(FocusState.Programmatic);
                if (IconListView.SelectedIndex < 0)
                {
                    IconListView.SelectedIndex = 0;
                }
            }
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape)
        {
            Hide();
        }
    }

    private void IconListView_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if (ViewModel.SelectedItem is not null)
        {
            _confirmed = true;
            Hide();
        }
    }

    private void IconListView_KeyDown(object sender, KeyRoutedEventArgs e)
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

    public async Task<Result<string>> ShowDialogAsync()
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
                    return selected.IconName;
                }
            }

            return Result.Fail("Icon picker was cancelled");
        }
        finally
        {
            _messengerService.UnregisterAll(this);
        }
    }

    private void OnDialogAnswer(object recipient, DialogAnswerMessage message)
    {
        if (message.Kind != DialogKind.IconPicker)
        {
            return;
        }

        var match = ViewModel.FilteredItems.FirstOrDefault(item => item.IconName == message.Payload);
        if (match is null)
        {
            _logger.LogWarning(
                $"Icon picker auto-answer failed: no filtered item matches icon name '{message.Payload}'.");
            Hide();
            return;
        }

        ViewModel.SelectedItem = match;
        _confirmed = true;
        _logger.LogInformation($"Icon picker answered automatically with '{message.Payload}'.");
        Hide();
    }
}
