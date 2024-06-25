using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.UserInterface;
using Celbridge.BaseLibrary.Dialog;

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
    public LocalizedString ProjectFolderText => _stringLocalizer.GetString($"NewProjectDialog_ProjectFolder");
    public LocalizedString ProjectFolderPlaceholderText => _stringLocalizer.GetString($"NewProjectDialog_ProjectFolderPlaceholder");

    public NewProjectDialog()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;

        _stringLocalizer = serviceProvider.GetRequiredService<IStringLocalizer>();

        var userInterfaceService = serviceProvider.GetRequiredService<IUserInterfaceService>();
        XamlRoot = userInterfaceService.XamlRoot as XamlRoot;

        ViewModel = serviceProvider.GetRequiredService<NewProjectDialogViewModel>();

        var newProjectName = 
            new TextBox()
                .Header(new TextBlock().Text(ProjectNameText))
                .Text(x => x.Bind(() => ViewModel.ProjectName)
                    .Mode(BindingMode.TwoWay)
                    .UpdateSourceTrigger(UpdateSourceTrigger.PropertyChanged))
                .MinWidth(200)
                .PlaceholderText(ProjectNamePlaceholderText);


        var selectFolder = new Grid()
            .ColumnDefinitions("*, 50")
            .Children(
                new TextBox()
                    .Grid(column: 0)
                    .Header(new TextBlock().Text(ProjectFolderText))
                    .MinWidth(200)
                    .PlaceholderText(ProjectFolderPlaceholderText)
                    .Margin(4)
                    .IsSpellCheckEnabled(false)
                    .Text(x => x.Bind(() => ViewModel.ProjectFolder)
                        .Mode(BindingMode.TwoWay)
                        .UpdateSourceTrigger(UpdateSourceTrigger.PropertyChanged)),
                new Button()
                    .Grid(column: 1)
                    .VerticalAlignment(VerticalAlignment.Bottom)
                    .Margin(0,0,0,4)
                    .Content(new SymbolIcon(Symbol.Folder))
                    .Command(ViewModel.SelectFolderCommand)
                );

        var stackPanel = 
            new StackPanel()
                .Orientation(Orientation.Vertical)
                .HorizontalAlignment(HorizontalAlignment.Left)
                .Children(newProjectName, selectFolder);

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

    public async Task<Result<NewProjectConfig>> ShowDialogAsync()
    {
        var contentDialogResult = await ShowAsync();

        if (contentDialogResult == ContentDialogResult.Primary && 
            ViewModel.NewProjectConfig is not null)
        {
            return Result<NewProjectConfig>.Ok(ViewModel.NewProjectConfig);
        }

        return Result<NewProjectConfig>.Fail("Failed to create new project config");
    }
}




