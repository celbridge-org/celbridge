using Celbridge.Dialog;

namespace Celbridge.UserInterface.Views;

public sealed partial class ConfirmationDialog : ContentDialog, IConfirmationDialog
{
    private readonly IStringLocalizer _stringLocalizer;

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

    public string OkString => _stringLocalizer.GetString("DialogButton_Ok");
    public string CancelString => _stringLocalizer.GetString("DialogButton_Cancel");

    public ConfirmationDialog()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;
        _stringLocalizer = serviceProvider.GetRequiredService<IStringLocalizer>();

        var userInterfaceService = serviceProvider.GetRequiredService<IUserInterfaceService>();
        XamlRoot = userInterfaceService.XamlRoot as XamlRoot;

        ViewModel = serviceProvider.GetRequiredService<ConfirmationDialogViewModel>();

        this.DataContext(ViewModel, (dialog, vm) => dialog
            .Title(x => x.Bind(() => ViewModel.TitleText).Mode(BindingMode.OneWay))
            .PrimaryButtonText(OkString)
            .SecondaryButtonText(CancelString)
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

    public async Task<bool> ShowDialogAsync()
    {
        var showResult = await ShowAsync();

        if (showResult == ContentDialogResult.Primary)
        {
            return true;
        }

        return false;
    }
}




