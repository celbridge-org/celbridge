using Celbridge.BaseLibrary.UserInterface;
using Celbridge.BaseLibrary.Dialog;

namespace Celbridge.UserInterface.ViewModels;

public sealed partial class InputTextDialog : ContentDialog, IInputTextDialog
{
    private readonly IStringLocalizer _stringLocalizer;

    public InputTextDialogViewModel ViewModel { get; }

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

    private LocalizedString OkText => _stringLocalizer.GetString($"DialogButton_Ok");
    private LocalizedString CancelText => _stringLocalizer.GetString($"DialogButton_Cancel");

    public InputTextDialog()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;

        _stringLocalizer = serviceProvider.GetRequiredService<IStringLocalizer>();

        var userInterfaceService = serviceProvider.GetRequiredService<IUserInterfaceService>();
        XamlRoot = userInterfaceService.XamlRoot as XamlRoot;

        ViewModel = serviceProvider.GetRequiredService<InputTextDialogViewModel>();

        this.DataContext(ViewModel, (dialog, vm) => dialog
            .Title(x => x.Bind(() => ViewModel.TitleText).Mode(BindingMode.OneWay))
            .PrimaryButtonText(OkText)
            .SecondaryButtonText(CancelText)
            .Content(new Grid()
                .HorizontalAlignment(HorizontalAlignment.Stretch)
                .VerticalAlignment(VerticalAlignment.Center)
                .Children(
                    new TextBox()
                        .Header(x => x.Bind(() => ViewModel.HeaderText).Mode(BindingMode.OneWay))
                        .Text(x => x.Bind(() => ViewModel.InputText).Mode(BindingMode.TwoWay).UpdateSourceTrigger(UpdateSourceTrigger.PropertyChanged))
                        .AcceptsReturn(false)
                    )
                )
            );
    }

    public async Task<Result<string>> ShowDialogAsync()
    {
        var contentDialogResult = await ShowAsync();
        if (contentDialogResult == ContentDialogResult.Primary)
        {
            return Result<string>.Ok(ViewModel.InputText);
        }

        return Result<string>.Fail("Failed to input text");
    }
}




