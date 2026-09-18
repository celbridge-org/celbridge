using System.Diagnostics;
using Celbridge.FileSystem;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Projects;
using Celbridge.Workspace;

namespace Celbridge.Python.Services;

/// <summary>
/// Deletes the uv cache and interpreter store that a project kept in its own Python folder before both moved
/// to a store the application shares, since nothing reads them now. Temporary: delete this class and its
/// registration once projects from before the shared store have had time to be opened again.
/// </summary>
public sealed class RetiredProjectCacheCleaner
{
    private readonly string[] _retiredFolderNames =
    {
        "uv_cache",
        "uv_python_installs",
    };

    private readonly IProjectService _projectService;
    private readonly ILocalFileSystem _fileSystem;
    private readonly ILogger<RetiredProjectCacheCleaner> _logger;

    public RetiredProjectCacheCleaner(
        IMessengerService messengerService,
        IProjectService projectService,
        ILocalFileSystem fileSystem,
        ILogger<RetiredProjectCacheCleaner> logger)
    {
        _projectService = projectService;
        _fileSystem = fileSystem;
        _logger = logger;

        messengerService.Register<WorkspaceLoadedMessage>(this, OnWorkspaceLoaded);
    }

    private void OnWorkspaceLoaded(object recipient, WorkspaceLoadedMessage message)
    {
        var project = _projectService.CurrentProject;
        if (project is null)
        {
            return;
        }

        // An instance of an earlier release may have the project open and still be using these folders.
        if (PythonInstaller.IsAnotherInstanceRunning())
        {
            return;
        }

        var projectPythonFolder = Path.Combine(project.ProjectDataFolderPath, ProjectConstants.PythonFolder);
        _ = Task.Run(() => RemoveRetiredFoldersAsync(projectPythonFolder));
    }

    private async Task RemoveRetiredFoldersAsync(string projectPythonFolder)
    {
        foreach (var folderName in _retiredFolderNames)
        {
            var folderPath = Path.Combine(projectPythonFolder, folderName);
            var infoResult = await _fileSystem.GetInfoAsync(folderPath);
            if (infoResult.IsFailure ||
                infoResult.Value.Kind != StorageItemKind.Folder)
            {
                continue;
            }

            var removalTimer = Stopwatch.StartNew();
            var deleteResult = await _fileSystem.DeleteFolderAsync(folderPath, recursive: true);
            if (deleteResult.IsFailure)
            {
                _logger.LogWarning("Failed to remove the retired project folder '{Path}': {Error}",
                    folderPath,
                    deleteResult.FirstErrorMessage);
                continue;
            }

            _logger.LogInformation("Removed the retired project folder '{Path}' in {DurationMs}ms",
                folderPath,
                removalTimer.ElapsedMilliseconds);
        }
    }
}
