using Celbridge.Dialog;
using Celbridge.Logging;

namespace Celbridge.UserInterface.Views;

public sealed partial class ConfirmationDialog : ContentDialog, IConfirmationDialog
{
    private readonly ILogger<ConfirmationDialog> _logger;
    private readonly IMessengerService _messengerService;
    private bool _autoAnswered;

    public ConfirmationDialogViewModel ViewModel { get; }

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

    public async Task<bool> ShowDialogAsync()
    {
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

    private void OnDialogAnswer(object recipient, DialogAnswerMessage message)
    {
        _autoAnswered = true;
        _logger.LogInformation("Confirmation dialog answered automatically.");
        Hide();
    }
}
