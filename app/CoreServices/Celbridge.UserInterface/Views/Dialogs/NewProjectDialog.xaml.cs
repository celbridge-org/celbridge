using Celbridge.Dialog;
using Celbridge.Projects;

namespace Celbridge.UserInterface.Views;

public sealed partial class NewProjectDialog : ContentDialog, INewProjectDialog
{
    private IStringLocalizer _stringLocalizer;

    public NewProjectDialogViewModel ViewModel { get; }

    public string TitleString => _stringLocalizer.GetString($"NewProjectDialog_Title");
    public string NewExampleProjectTitleString => _stringLocalizer.GetString($"NewExampleProjectDialog_Title");
    public string CreateString => _stringLocalizer.GetString($"DialogButton_Create");
    public string CancelString => _stringLocalizer.GetString($"DialogButton_Cancel");
    public string ProjectNameString => _stringLocalizer.GetString($"NewProjectDialog_ProjectName");
    public string ProjectNamePlaceholderString => _stringLocalizer.GetString($"NewProjectDialog_ProjectNamePlaceholder");
    public string ProjectFolderString => _stringLocalizer.GetString($"NewProjectDialog_ProjectFolder");
    public string ProjectFolderPlaceholderString => _stringLocalizer.GetString($"NewProjectDialog_ProjectFolderPlaceholder");
    public string CreateSubfolderString => _stringLocalizer.GetString($"NewProjectDialog_CreateSubfolder");
    public string CreateSubfolderTooltipString => _stringLocalizer.GetString($"NewProjectDialog_CreateSubfolderTooltip");
    public string SaveLocationTooltipString => _stringLocalizer.GetString($"NewProjectDialog_SaveLocationTooltip");

    public NewProjectDialog()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        var userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        XamlRoot = userInterfaceService.XamlRoot as XamlRoot;

        ViewModel = ServiceLocator.AcquireService<NewProjectDialogViewModel>();

        // Default to standard project title
        Title = TitleString;

        this.InitializeComponent();
    }

    public void Initialize(NewProjectConfigType configType)
    {
        ViewModel.ConfigType = configType;

        // Update the title based on the config type
        if (configType == NewProjectConfigType.Example)
        {
            Title = NewExampleProjectTitleString;
        }
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
