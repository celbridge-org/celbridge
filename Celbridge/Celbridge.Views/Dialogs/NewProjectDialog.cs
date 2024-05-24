using Celbridge.BaseLibrary.UserInterface;
using Celbridge.BaseLibrary.UserInterface.Dialog;
using Celbridge.ViewModels.Dialogs;

namespace Celbridge.Views.Dialogs;

public sealed partial class NewProjectDialog : ContentDialog, INewProjectDialog
{
    private IStringLocalizer _stringLocalizer;

    public NewProjectDialogViewModel ViewModel { get; }

    public LocalizedString TitleText => _stringLocalizer.GetString($"NewProjectDialog_Title");
    public LocalizedString CreateText => _stringLocalizer.GetString($"DialogButton_Create");
    public LocalizedString CancelText => _stringLocalizer.GetString($"DialogButton_Cancel");
    public LocalizedString ProjectNameText => _stringLocalizer.GetString($"NewProjectDialog_ProjectName");
    public LocalizedString ProjectNamePlaceholderText => _stringLocalizer.GetString($"NewProjectDialog_ProjectNamePlaceholder");

    public NewProjectDialog()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;

        _stringLocalizer = serviceProvider.GetRequiredService<IStringLocalizer>();

        var userInterfaceService = serviceProvider.GetRequiredService<IUserInterfaceService>();
        XamlRoot = userInterfaceService.XamlRoot as XamlRoot;

        ViewModel = serviceProvider.GetRequiredService<NewProjectDialogViewModel>();

        var newProjectName = 
            new TextBox()
                .Header(new TextBlock()
                            .Text(ProjectNameText))
                .Text(x => x.Bind(() => ViewModel.ProjectName)
                    .Mode(BindingMode.TwoWay)
                    .UpdateSourceTrigger(UpdateSourceTrigger.PropertyChanged))
                .MinWidth(200)
                .PlaceholderText(ProjectNamePlaceholderText);

        var stackPanel = 
            new StackPanel()
                .Orientation(Orientation.Vertical)
                .HorizontalAlignment(HorizontalAlignment.Left)
                .Children(newProjectName);

        this.DataContext(ViewModel, (dialog, vm) => 
            dialog
                .Title(TitleText)
                .PrimaryButtonText(CreateText)
                .SecondaryButtonText(CancelText)
                .IsPrimaryButtonEnabled(x => x.Bind(() => ViewModel.IsCreateButtonEnabled).Mode(BindingMode.OneWay))
                .Content(stackPanel)
            );

        PrimaryButtonClick += CreateButtonClick;
        SecondaryButtonClick += CancelButtonClick;
    }

    private void CancelButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        Hide();
    }

    private void CreateButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ViewModel.CreateProjectCommand.Execute(null);
    }

    public async Task ShowDialogAsync()
    {
        await ShowAsync();
    }
}




