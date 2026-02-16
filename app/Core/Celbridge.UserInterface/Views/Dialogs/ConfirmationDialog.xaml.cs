using Celbridge.Dialog;

namespace Celbridge.UserInterface.Views;

public sealed partial class ConfirmationDialog : ContentDialog, IConfirmationDialog
{
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

        this.InitializeComponent();

        // Set default button text from localization after InitializeComponent.
        // Callers can override these before calling ShowDialogAsync().
        var stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        PrimaryButtonText = stringLocalizer.GetString("DialogButton_Ok");
        SecondaryButtonText = stringLocalizer.GetString("DialogButton_Cancel");
    }

    public async Task<bool> ShowDialogAsync()
    {
        var result = await ShowAsync();
        return result == ContentDialogResult.Primary;
    }
}
