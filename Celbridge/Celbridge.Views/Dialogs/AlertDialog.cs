using Celbridge.BaseLibrary.UserInterface;
using Celbridge.BaseLibrary.Dialog;
using Celbridge.ViewModels.Dialogs;

namespace Celbridge.Views.Dialogs;

public sealed partial class AlertDialog : ContentDialog, IAlertDialog
{
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

    public string CloseText
    {
        get => ViewModel.CloseText;
        set => ViewModel.CloseText = value;
    }

    public AlertDialog()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;

        var userInterfaceService = serviceProvider.GetRequiredService<IUserInterfaceService>();
        XamlRoot = userInterfaceService.XamlRoot as XamlRoot;

        ViewModel = serviceProvider.GetRequiredService<AlertDialogViewModel>();

        this.DataContext(ViewModel, (dialog, vm) => dialog
            .Title(x => x.Bind(() => ViewModel.TitleText).Mode(BindingMode.OneWay))
            .CloseButtonText(x => x.Bind(() => ViewModel.CloseText).Mode(BindingMode.OneWay))
            .Content(new Grid()
                .HorizontalAlignment(HorizontalAlignment.Center)
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




