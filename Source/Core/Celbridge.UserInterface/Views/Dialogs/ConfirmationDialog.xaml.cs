using Celbridge.Dialog;
using Celbridge.Logging;

namespace Celbridge.UserInterface.Views;

public sealed partial class ConfirmationDialog : ContentDialog, IConfirmationDialog
{
    private readonly ILogger<ConfirmationDialog> _logger;
    private readonly IMessengerService _messengerService;
    private Button? _secondaryButton;
    private bool _autoAnswered;

    public ConfirmationDialogViewModel ViewModel { get; }

    public bool IsDestructive { get; set; }

    public string TitleText
    {
        get => ViewModel.TitleText;
        set => ViewModel.TitleText = value;
    }

    public string MessageText
    {
        get => ViewModel.MessageText;
        set => ViewModel.MessageText = value;
    }

    public ConfirmationDialog()
    {
        var userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        XamlRoot = userInterfaceService.XamlRoot as XamlRoot;

        ViewModel = ServiceLocator.AcquireService<ConfirmationDialogViewModel>();
        _logger = ServiceLocator.AcquireService<ILogger<ConfirmationDialog>>();
        _messengerService = ServiceLocator.AcquireService<IMessengerService>();

        this.InitializeComponent();

        // Set default button text from localization after InitializeComponent.
        // Callers can override these before calling ShowDialogAsync().
        var stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        PrimaryButtonText = stringLocalizer.GetString("DialogButton_Ok");
        SecondaryButtonText = stringLocalizer.GetString("DialogButton_Cancel");

        this.EnableThemeSync();
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _secondaryButton = GetTemplateChild("SecondaryButton") as Button;
    }

    public async Task<bool> ShowDialogAsync()
    {
        ConfigureDefaultButton();

        _messengerService.Register<DialogAnswerMessage>(this, OnDialogAnswer);
        try
        {
            var result = await ShowAsync();
            if (_autoAnswered)
            {
                return true;
            }
            return result == ContentDialogResult.Primary;
        }
        finally
        {
            _messengerService.UnregisterAll(this);
        }
    }

    private void ConfigureDefaultButton()
    {
        if (IsDestructive)
        {
            // The confirm button stays accented as the action, but keyboard focus starts on Cancel and
            // no button is the Enter default, so pressing Enter cannot carry out the action by mistake.
            DefaultButton = ContentDialogButton.None;
            Opened += OnDestructiveDialogOpened;
        }
        else
        {
            DefaultButton = ContentDialogButton.Primary;
        }
    }

    private void OnDestructiveDialogOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        _secondaryButton?.Focus(FocusState.Programmatic);
    }

    private void OnDialogAnswer(object recipient, DialogAnswerMessage message)
    {
        if (message.Kind != DialogKind.Confirmation)
        {
            return;
        }

        _autoAnswered = true;
        _logger.LogInformation("Confirmation dialog answered automatically.");
        Hide();
    }
}
