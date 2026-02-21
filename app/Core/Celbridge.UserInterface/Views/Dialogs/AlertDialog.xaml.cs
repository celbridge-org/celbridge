using Celbridge.Dialog;

namespace Celbridge.UserInterface.Views;

public sealed partial class AlertDialog : ContentDialog, IAlertDialog
{
    private readonly IStringLocalizer _stringLocalizer;

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

        var userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        XamlRoot = userInterfaceService.XamlRoot as XamlRoot;

        ViewModel = ServiceLocator.AcquireService<AlertDialogViewModel>();

        this.InitializeComponent();

        this.EnableThemeSync();
    }

    public async Task ShowDialogAsync()
    {
        await ShowAsync();
    }
}
