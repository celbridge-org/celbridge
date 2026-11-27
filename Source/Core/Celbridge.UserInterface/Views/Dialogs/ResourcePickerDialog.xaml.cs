using Celbridge.Dialog;
using Celbridge.Logging;
using Celbridge.UserInterface.ViewModels.Controls;

namespace Celbridge.UserInterface.Views;

/// <summary>
/// Asks the user to choose a file to open, a place to save a file, or a folder, in the project. The dialog hosts the
/// resource picker view, which browses the project one folder at a time.
/// </summary>
public sealed partial class ResourcePickerDialog : ContentDialog, IResourcePickerDialog
{
    private const double ContentHeight = 440;

    // Save mode adds the name field under the list, so the dialog grows by the field's height and keeps the list's
    // height.
    private const double SaveModeContentHeight = 560;

    private readonly IStringLocalizer _stringLocalizer;
    private readonly ILogger<ResourcePickerDialog> _logger;
    private readonly IMessengerService _messengerService;

    // Set when the user, or an automated answer, chooses a resource.
    private ResourceKey? _chosenResource;

    // True from the moment the dialog is shown until the user first presses a key or the pointer.
    private bool _isOpening;

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

        // A button marks a pointer press on itself as handled. So the dialog listens for handled presses too, and
        // hears every press.
        AddHandler(PointerPressedEvent, new PointerEventHandler(Dialog_PointerPressed), handledEventsToo: true);
    }

    public async Task<Result<ResourceKey>> ShowDialogAsync()
    {
        _chosenResource = null;
        _isOpening = true;

        ContentGrid.Height = ViewModel.Mode == ResourcePickerMode.Save
            ? SaveModeContentHeight
            : ContentHeight;

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
        PickerView.FocusFirstField();

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

    // ContentDialog moves the keyboard to its first control once, either before or after Opened. In save mode, that
    // control is the search box. A move after Opened would take the keyboard from the field that
    // PickerView.FirstField names. So until the user first presses a key or the pointer, any programmatic move to
    // another control in the picker goes to that field instead.
    private void PickerView_GettingFocus(UIElement sender, GettingFocusEventArgs args)
    {
        if (!_isOpening
            || args.FocusState != FocusState.Programmatic)
        {
            return;
        }

        var firstField = PickerView.FirstField;
        if (!ReferenceEquals(args.NewFocusedElement, firstField))
        {
            args.TrySetNewFocusedElement(firstField);
        }
    }

    private void Dialog_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        _isOpening = false;
    }

    private void Dialog_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isOpening = false;
    }

    // The button's own close is cancelled. Choose records the choice, then closes the dialog. In save mode, the first
    // press on an existing file's name asks whether to replace the file, and the dialog stays open.
    private void Dialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;

        var chosenResource = ViewModel.GetConfirmedChoice();
        if (chosenResource is not null)
        {
            Choose(chosenResource.Value);
        }
    }

    // While the dialog asks whether to replace a file, Cancel and Escape take the question away, and the dialog stays
    // open.
    private void Dialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (_chosenResource is null
            && ViewModel.IsAskingToReplace)
        {
            args.Cancel = true;
            ViewModel.CancelReplace();
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

        // An automated answer replaces the user's answer, so the question whether to replace a file goes. The dialog
        // can then close, even when the check refuses the answer.
        ViewModel.CancelReplace();

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
