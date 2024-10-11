using Celbridge.Commands;
using Celbridge.Explorer;
using Celbridge.Projects;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Documents.ViewModels;

public partial class EditorPreviewViewModel : ObservableObject
{
    private readonly ICommandService _commandService;

    [ObservableProperty]
    private string _previewHTML = string.Empty;

    public string ProjectFolderPath { get; }

    public EditorPreviewViewModel(
        ICommandService commandService,
        IProjectService projectService)
    {
        _commandService = commandService;

        Guard.IsNotNull(projectService.CurrentProject);
        ProjectFolderPath = projectService.CurrentProject.ProjectFolderPath;
    }

    public void NavigateToURL(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            // Navigating an empty URL is a no-op
            return;
        }

        _commandService.Execute<IOpenBrowserCommand>(command =>
        {
            command.URL = url;
        });      
    }
}
