using Celbridge.Commands;
using Celbridge.Explorer;
using Celbridge.Projects;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Documents.ViewModels;

public partial class EditorPreviewViewModel : ObservableObject
{
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    [ObservableProperty]
    private string _previewHTML = string.Empty;

    public string ProjectFolderPath { get; }

    [ObservableProperty]
    private string _filePath = string.Empty;

    public EditorPreviewViewModel(
        ICommandService commandService,
        IProjectService projectService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _commandService = commandService;
        _workspaceWrapper = workspaceWrapper;

        Guard.IsNotNull(projectService.CurrentProject);
        ProjectFolderPath = projectService.CurrentProject.ProjectFolderPath;
    }

    public void NavigateToURL(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            // Navigating to an empty URL is a no-op
            return;
        }

        _commandService.Execute<IOpenBrowserCommand>(command =>
        {
            command.URL = url;
        });      
    }

    public Result OpenRelativePath(string relativePath)
    {
        var parentFolder = Path.GetDirectoryName(FilePath);
        if (string.IsNullOrEmpty(parentFolder))
        {
            return Result.Fail("Failed to get parent folder for document file");
        }

        // Change the links file extension from .html to the extension of the edited file
        var fileExtension = Path.GetExtension(FilePath);
        var fullPath = Path.Combine(parentFolder, relativePath);
        fullPath = Path.ChangeExtension(fullPath, fileExtension);

        if (!File.Exists(fullPath))
        {
            return Result.Fail($"File does not exist at relative path: {relativePath}");
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry;

        var getResult = resourceRegistry.GetResourceKey(fullPath);
        if (getResult.IsFailure)
        {
            return Result.Fail($"Failed to get resource key for relative path: {relativePath}")
                .WithErrors(getResult);
        }
        var resourceKey = getResult.Value;

        _commandService.Execute<IOpenDocumentCommand>(command =>
        {
            command.FileResource = resourceKey;
        });

        return Result.Ok();
    }
}
