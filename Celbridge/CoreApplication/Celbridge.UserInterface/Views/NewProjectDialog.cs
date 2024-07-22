using Celbridge.Projects;
using Celbridge.Dialog;

namespace Celbridge.UserInterface.Views;

public sealed partial class NewProjectDialog : ContentDialog, INewProjectDialog
{
    private IStringLocalizer _stringLocalizer;

    public NewProjectDialogViewModel ViewModel { get; }

    public LocalizedString TitleString => _stringLocalizer.GetString($"NewProjectDialog_Title");
    public LocalizedString CreateString => _stringLocalizer.GetString($"DialogButton_Create");
    public LocalizedString CancelString => _stringLocalizer.GetString($"DialogButton_Cancel");
    public LocalizedString ProjectNameString => _stringLocalizer.GetString($"NewProjectDialog_ProjectName");
    public LocalizedString ProjectNamePlaceholderString => _stringLocalizer.GetString($"NewProjectDialog_ProjectNamePlaceholder");
    public LocalizedString ProjectFolderString => _stringLocalizer.GetString($"NewProjectDialog_ProjectFolder");
    public LocalizedString ProjectFolderPlaceholderString => _stringLocalizer.GetString($"NewProjectDialog_ProjectFolderPlaceholder");

    public NewProjectDialog()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;

        _stringLocalizer = serviceProvider.GetRequiredService<IStringLocalizer>();

        var userInterfaceService = serviceProvider.GetRequiredService<IUserInterfaceService>();
        XamlRoot = userInterfaceService.XamlRoot as XamlRoot;

        ViewModel = serviceProvider.GetRequiredService<NewProjectDialogViewModel>();

        var newProjectName = 
            new TextBox()
                .Header(new TextBlock().Text(ProjectNameString))
                .Text(x => x.Bind(() => ViewModel.ProjectName)
                    .Mode(BindingMode.TwoWay)
                    .UpdateSourceTrigger(UpdateSourceTrigger.PropertyChanged))
                .MinWidth(200)
                .PlaceholderText(ProjectNamePlaceholderString);


        var selectFolder = new Grid()
            .ColumnDefinitions("*, 50")
            .Children(
                new TextBox()
                    .Grid(column: 0)
                    .Header(new TextBlock().Text(ProjectFolderString))
                    .MinWidth(200)
                    .PlaceholderText(ProjectFolderPlaceholderString)
                    .Margin(4)
                    .IsSpellCheckEnabled(false)
                    .Text(x => x.Bind(() => ViewModel.ProjectFolderPath)
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
                .Title(TitleString)
                .PrimaryButtonText(CreateString)
                .SecondaryButtonText(CancelString)
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




