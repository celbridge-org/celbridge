using Celbridge.BaseLibrary.UserInterface;
using Celbridge.BaseLibrary.UserInterface.Dialog;
using Celbridge.ViewModels.Dialogs;

namespace Celbridge.Views.Dialogs;

public sealed partial class ProgressDialog : ContentDialog, IProgressDialog
{
    public ProgressDialogViewModel ViewModel { get; }

    public string TitleText
    {
        get => ViewModel.TitleText;
        set => ViewModel.TitleText = value;
    }

    public ProgressDialog()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;

        var userInterfaceService = serviceProvider.GetRequiredService<IUserInterfaceService>();
        XamlRoot = userInterfaceService.XamlRoot as XamlRoot;

        ViewModel = serviceProvider.GetRequiredService<ProgressDialogViewModel>();

        this.DataContext(ViewModel, (dialog, vm) => dialog
            .Title(x => x.Bind(() => ViewModel.TitleText).Mode(BindingMode.OneWay))
            .PrimaryButtonCommand(x => x.Bind(() => ViewModel.CancelCommand))
            .Content(new Grid()
                .HorizontalAlignment(HorizontalAlignment.Center)
                .VerticalAlignment(VerticalAlignment.Center)
                .Children(
                    new ProgressBar()
                        .Width(200)
                        .Height(20)
                        .IsIndeterminate(true)
                        )
                )
            );
    }

    public async Task ShowDialogAsync()
    {
        await ShowAsync();
    }
}