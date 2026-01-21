using Celbridge.Dialog;
using Celbridge.Projects;

namespace Celbridge.UserInterface.Views;

public sealed partial class NewProjectDialog : ContentDialog, INewProjectDialog
{
    private IStringLocalizer _stringLocalizer;

    public NewProjectDialogViewModel ViewModel { get; }

    public string TitleString => _stringLocalizer.GetString($"NewProjectDialog_Title");
    public string CreateString => _stringLocalizer.GetString($"DialogButton_Create");
    public string CancelString => _stringLocalizer.GetString($"DialogButton_Cancel");
    public string ChooseTemplateString => _stringLocalizer.GetString($"NewProjectDialog_ChooseTemplate");
    public string ChooseTemplateTooltipString => _stringLocalizer.GetString($"NewProjectDialog_ChooseTemplateTooltip");
    public string ProjectNameString => _stringLocalizer.GetString($"NewProjectDialog_ProjectName");
    public string ProjectNamePlaceholderString => _stringLocalizer.GetString($"NewProjectDialog_ProjectNamePlaceholder");
    public string ProjectNameTooltipString => _stringLocalizer.GetString($"NewProjectDialog_ProjectNameTooltip");
    public string ProjectFolderString => _stringLocalizer.GetString($"NewProjectDialog_ProjectFolder");
    public string ProjectFolderPlaceholderString => _stringLocalizer.GetString($"NewProjectDialog_ProjectFolderPlaceholder");
    public string ProjectFolderTooltipString => _stringLocalizer.GetString($"NewProjectDialog_ProjectFolderTooltip");
    public string BrowseFolderTooltipString => _stringLocalizer.GetString($"NewProjectDialog_BrowseFolderTooltip");
    public string CreateSubfolderString => _stringLocalizer.GetString($"NewProjectDialog_CreateSubfolder");
    public string CreateSubfolderTooltipString => _stringLocalizer.GetString($"NewProjectDialog_CreateSubfolderTooltip");
    public string SaveLocationTooltipString => _stringLocalizer.GetString($"NewProjectDialog_SaveLocationTooltip");
    public string InvalidProjectNameString => _stringLocalizer.GetString($"NewProjectDialog_InvalidProjectName");
    public string InvalidFolderPathString => _stringLocalizer.GetString($"NewProjectDialog_InvalidFolderPath");
    public string SubfolderAlreadyExistsString => _stringLocalizer.GetString($"NewProjectDialog_SubfolderAlreadyExists");
    public string ProjectFileAlreadyExistsString => _stringLocalizer.GetString($"NewProjectDialog_ProjectFileAlreadyExists");

    public string GetLocalizedErrorMessage(string errorKey)
    {
        return _stringLocalizer.GetString(errorKey);
    }

    public NewProjectDialog()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        var userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        XamlRoot = userInterfaceService.XamlRoot as XamlRoot;

        ViewModel = ServiceLocator.AcquireService<NewProjectDialogViewModel>();

        Title = TitleString;

        this.InitializeComponent();
    }

    private void ProjectNameTextBox_Loaded(object sender, RoutedEventArgs e)
    {
        ProjectNameTextBox.Focus(FocusState.Programmatic);
    }

    private void ProjectNameTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            // Move focus away from the TextBox when Enter is pressed
            // This allows the user to see validation results without submitting
            var options = new FindNextElementOptions
            {
                SearchRoot = this.XamlRoot?.Content
            };
            FocusManager.TryMoveFocus(FocusNavigationDirection.Next, options);
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

