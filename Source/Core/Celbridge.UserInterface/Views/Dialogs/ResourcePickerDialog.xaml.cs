using Celbridge.Dialog;
using Celbridge.Logging;

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

    public ResourcePickerDialog()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _logger = ServiceLocator.AcquireService<ILogger<ResourcePickerDialog>>();
        _messengerService = ServiceLocator.AcquireService<IMessengerService>();

        var userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        XamlRoot = userInterfaceService.XamlRoot as XamlRoot;

        this.InitializeComponent();
        this.EnableThemeSync();

        ViewModel = ResourceSearchList.ViewModel;
    }

    public void SetTitle(string title)
    {
        _customTitle = title;
        // Update the Title binding since it was already evaluated
        Title = title;
    }

    private void Dialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        ResourceSearchList.FocusSearchBox();

        // The dialog opens at once and shows that it is loading. The walk fills the list when it finishes.
        _ = ViewModel.LoadItemsAsync();
    }

    private void ResourceSearchList_ItemInvoked(object? sender, EventArgs e)
    {
        _confirmed = true;
        Hide();
    }

    private void ResourceSearchList_EscapePressed(object? sender, EventArgs e)
    {
        Hide();
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
