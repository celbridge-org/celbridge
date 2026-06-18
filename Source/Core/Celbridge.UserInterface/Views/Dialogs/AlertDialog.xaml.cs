using Celbridge.Dialog;
using Celbridge.Logging;

namespace Celbridge.UserInterface.Views;

public sealed partial class AlertDialog : ContentDialog, IAlertDialog
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ILogger<AlertDialog> _logger;
    private readonly IMessengerService _messengerService;

    public AlertDialogViewModel ViewModel { get; }

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

    public string OkString => _stringLocalizer.GetString("DialogButton_Ok");

    public AlertDialog()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _logger = ServiceLocator.AcquireService<ILogger<AlertDialog>>();
        _messengerService = ServiceLocator.AcquireService<IMessengerService>();

        var userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        XamlRoot = userInterfaceService.XamlRoot as XamlRoot;

        ViewModel = ServiceLocator.AcquireService<AlertDialogViewModel>();

        this.InitializeComponent();

        this.EnableThemeSync();
    }

    public async Task ShowDialogAsync()
    {
        _messengerService.Register<DialogAnswerMessage>(this, OnDialogAnswer);
        try
        {
            await ShowAsync();
        }
        finally
        {
            _messengerService.UnregisterAll(this);
        }
    }

    private void OnDialogAnswer(object recipient, DialogAnswerMessage message)
    {
        if (message.Kind != DialogKind.Alert)
        {
            return;
        }

        _logger.LogInformation("Alert dialog answered automatically.");
        Hide();
    }
}
