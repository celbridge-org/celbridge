using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Projects;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Views;

public sealed partial class ExplorerToolbar : UserControl
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;

    // Toolbar tooltip strings
    private string NewFileTooltipString => _stringLocalizer.GetString("ResourceTreeToolbar_NewFileTooltip");
    private string NewFolderTooltipString => _stringLocalizer.GetString("ResourceTreeToolbar_NewFolderTooltip");
    private string CollapseFoldersTooltipString => _stringLocalizer.GetString("ResourceTreeToolbar_CollapseFoldersTooltip");
    private string ProjectSettingsTooltipString => _stringLocalizer.GetString("ResourceTreeToolbar_ProjectSettingsTooltip");

    /// <summary>
    /// Event raised when the Add File button is clicked.
    /// </summary>
    public event EventHandler? NewFileClicked;

    /// <summary>
    /// Event raised when the Add Folder button is clicked.
    /// </summary>
    public event EventHandler? NewFolderClicked;

    /// <summary>
    /// Event raised when the Collapse Folders button is clicked.
    /// </summary>
    public event EventHandler? CollapseFoldersClicked;

    public ExplorerToolbar()
    {
        InitializeComponent();

        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _commandService = ServiceLocator.AcquireService<ICommandService>();
    }

    /// <summary>
    /// Sets the visibility of the toolbar based on whether the panel is active (has focus or mouse is over it).
    /// </summary>
    public void SetToolbarVisible(bool isVisible)
    {
        if (isVisible)
        {
            ToolbarFadeIn.Begin();
        }
        else
        {
            ToolbarFadeOut.Begin();
        }
    }

    private void NewFileButton_Click(object sender, RoutedEventArgs e)
    {
        NewFileClicked?.Invoke(this, EventArgs.Empty);
    }

    private void NewFolderButton_Click(object sender, RoutedEventArgs e)
    {
        NewFolderClicked?.Invoke(this, EventArgs.Empty);
    }

    private void CollapseFoldersButton_Click(object sender, RoutedEventArgs e)
    {
        CollapseFoldersClicked?.Invoke(this, EventArgs.Empty);
    }

    private void ProjectSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        OpenProjectSettings();
    }

    private void OpenProjectSettings()
    {
        // Get the project file path and open it as a document
        var projectService = ServiceLocator.AcquireService<IProjectService>();
        var currentProject = projectService.CurrentProject;
        if (currentProject is null)
        {
            return;
        }

        // Get the project file name (e.g., "myproject.celbridge")
        var projectFilePath = currentProject.ProjectFilePath;
        var projectFileName = Path.GetFileName(projectFilePath);

        // Create a resource key for the project file
        var fileResource = new ResourceKey(projectFileName);

        _commandService.Execute<IOpenDocumentCommand>(command =>
        {
            command.FileResource = fileResource;
        });
    }
}
