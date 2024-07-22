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
        var serviceProvider = ServiceLocator.ServiceProvider;
        _stringLocalizer = serviceProvider.GetRequiredService<IStringLocalizer>();

        var userInterfaceService = serviceProvider.GetRequiredService<IUserInterfaceService>();
        XamlRoot = userInterfaceService.XamlRoot as XamlRoot;

        ViewModel = serviceProvider.GetRequiredService<AlertDialogViewModel>();

        this.DataContext(ViewModel, (dialog, vm) => dialog
            .Title(x => x.Bind(() => ViewModel.TitleText).Mode(BindingMode.OneWay))
            .CloseButtonText(OkString)
            .Content(new Grid()
                .HorizontalAlignment(HorizontalAlignment.Left)
                .VerticalAlignment(VerticalAlignment.Center)
                .Children(
                    new TextBlock()
                        .Text(x => x.Bind(() => ViewModel.MessageText).Mode(BindingMode.OneWay))
                        .TextWrapping(TextWrapping.WrapWholeWords)
                    )
                )
            );
    }

    public async Task ShowDialogAsync()
    {
        await ShowAsync();
    }
}




