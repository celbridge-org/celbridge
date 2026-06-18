using Celbridge.Dialog;
using Celbridge.Logging;
using Windows.System;

namespace Celbridge.UserInterface.Views;

public sealed partial class SecretInputDialog : ContentDialog, ISecretInputDialog
{
    private readonly ILogger<SecretInputDialog> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IStringLocalizer _stringLocalizer;
    private string _submitButtonKey = "DialogButton_Ok";
    private bool _autoAnswered;
    private bool _pressedEnter;

    public SecretInputDialogViewModel ViewModel { get; }

    public string TitleText
    {
        get => ViewModel.TitleText;
        set => ViewModel.TitleText = value;
    }

    public string HeaderText
    {
        get => ViewModel.HeaderText;
        set => ViewModel.HeaderText = value;
    }

    public string SubmitButtonKey
    {
        get => _submitButtonKey;
        set
        {
            _submitButtonKey = value;
            PrimaryButtonText = _stringLocalizer.GetString(_submitButtonKey);
        }
    }

    private string CancelString => _stringLocalizer.GetString("DialogButton_Cancel");

    public SecretInputDialog()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _logger = ServiceLocator.AcquireService<ILogger<SecretInputDialog>>();
        _messengerService = ServiceLocator.AcquireService<IMessengerService>();

        var userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        XamlRoot = userInterfaceService.XamlRoot as XamlRoot;

        ViewModel = ServiceLocator.AcquireService<SecretInputDialogViewModel>();

        this.InitializeComponent();

        PrimaryButtonText = _stringLocalizer.GetString(_submitButtonKey);

        this.EnableThemeSync();
    }

    private void SecretPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        // Mirror the masked control's value into the view model so submit
        // enablement tracks it; the secret never appears in the view model
        // beyond this transient entry.
        ViewModel.SecretText = SecretPasswordBox.Password;
    }

    private void SecretPasswordBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            if (!ViewModel.IsSubmitEnabled)
            {
                return;
            }

            _pressedEnter = true;
            Hide();
        }
        else if (e.Key == VirtualKey.Escape)
        {
            Hide();
        }
    }

    public async Task<Result<string>> ShowDialogAsync()
    {
        _messengerService.Register<DialogAnswerMessage>(this, OnDialogAnswer);
        try
        {
            var contentDialogResult = await ShowAsync();
            if (_autoAnswered)
            {
                return Result<string>.Ok(ViewModel.SecretText);
            }
            if (contentDialogResult == ContentDialogResult.Primary || _pressedEnter)
            {
                return Result<string>.Ok(ViewModel.SecretText);
            }

            return Result<string>.Fail("The secret input dialog was cancelled");
        }
        finally
        {
            _messengerService.UnregisterAll(this);
        }
    }

    private void OnDialogAnswer(object recipient, DialogAnswerMessage message)
    {
        if (message.Kind != DialogKind.SecretInput)
        {
            return;
        }

        _autoAnswered = true;
        ViewModel.SecretText = message.Payload;
        _logger.LogInformation("Secret-input dialog answered automatically.");
        Hide();
    }
}
