using Celbridge.Dialog;
using Celbridge.Logging;
using Celbridge.UserInterface.ViewModels.Controls;

namespace Celbridge.UserInterface.Views;

/// <summary>
/// Asks the user to choose a file to open, or a folder, in the project. The dialog hosts the resource picker view,
/// which browses the project one folder at a time.
/// </summary>
public sealed partial class ResourcePickerDialog : ContentDialog, IResourcePickerDialog
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ILogger<ResourcePickerDialog> _logger;
    private readonly IMessengerService _messengerService;

    // Set when the user, or an automated answer, chooses a resource.
    private ResourceKey? _chosenResource;

    public ResourcePickerViewModel ViewModel { get; }

    private string CancelString => _stringLocalizer.GetString("DialogButton_Cancel");

    public ResourcePickerDialog()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _logger = ServiceLocator.AcquireService<ILogger<ResourcePickerDialog>>();
        _messengerService = ServiceLocator.AcquireService<IMessengerService>();

        var userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        XamlRoot = userInterfaceService.XamlRoot as XamlRoot;

        this.InitializeComponent();
        this.EnableThemeSync();

        var panelCornerRadius = (double)Application.Current.Resources["PanelCornerRadius"];
        PreviewArea.CornerRadius = new CornerRadius(panelCornerRadius);

        ViewModel = PickerView.ViewModel;
    }

    public async Task<Result<ResourceKey>> ShowDialogAsync()
    {
        _chosenResource = null;
        _messengerService.Register<DialogAnswerMessage>(this, OnDialogAnswer);
        try
        {
            await ShowAsync();

            if (_chosenResource is not null)
            {
                return Result<ResourceKey>.Ok(_chosenResource.Value);
            }

            return Result<ResourceKey>.Fail("Resource picker was cancelled");
        }
        finally
        {
            _messengerService.UnregisterAll(this);
        }
    }

    private async void Dialog_Opened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        PickerView.FocusSearchBox();

        try
        {
            // The dialog opens at once. The start folder fills the list when it has been listed.
            await ViewModel.OpenAsync();
            PickerView.ShowSelectedItem();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open the resource picker's start folder");
        }
    }

    // The button's own close is cancelled. Choose records the choice, then closes the dialog.
    private void Dialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;

        var chosenResource = ViewModel.GetChoice();
        if (chosenResource is not null)
        {
            Choose(chosenResource.Value);
        }
    }

    private void PickerView_ResourceChosen(object? sender, ResourceKey resource)
    {
        Choose(resource);
    }

    private void PickerView_EscapePressed(object? sender, EventArgs e)
    {
        Hide();
    }

    private void Choose(ResourceKey resource)
    {
        _chosenResource = resource;
        Hide();
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

        try
        {
            var checkResult = await ViewModel.CheckAnswerAsync(targetKey);
            if (checkResult.IsFailure)
            {
                _logger.LogWarning(
                    $"Resource picker auto-answer failed: {checkResult.FirstErrorMessage}");
                Hide();
                return;
            }
            var answeredResource = checkResult.Value;

            _logger.LogInformation($"Resource picker answered automatically with '{answeredResource}'.");
            Choose(answeredResource);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check the resource picker's automated answer");
            Hide();
        }
    }
}
