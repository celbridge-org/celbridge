using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.FileSystem;
using Celbridge.LanguageServers.Helpers;
using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Resources;
using Celbridge.Utilities;
using Celbridge.Workspace;

namespace Celbridge.LanguageServers.Services;

/// <summary>
/// A range of a document's text. Lines and columns count from 1.
/// </summary>
internal sealed record DocumentRange(int Line, int Column, int EndLine, int EndColumn);

/// <summary>
/// The edits to one file, which is named by its absolute path.
/// </summary>
internal sealed record FileEdits(string FilePath, List<RangeEdit> Edits);

/// <summary>
/// Carries out what a language server session asks of the workspace: applying an edit, opening a document and
/// reading a file. Safe to call from any thread.
/// </summary>
internal interface ILanguageServerWorkspace
{
    /// <summary>
    /// Applies edits to the project's files. Fails when a file is outside the project folder, or when the edits
    /// cannot be applied.
    /// </summary>
    Task<Result> ApplyEditAsync(IReadOnlyList<FileEdits> edits);

    /// <summary>
    /// Opens a file in the project folder as the active document, and selects the range. Returns false when the
    /// file is outside the project folder, or does not open.
    /// </summary>
    Task<bool> ShowDocumentAsync(string filePath, DocumentRange? selection);

    /// <summary>
    /// Returns a stamp that changes whenever the file changes. Returns an empty string when the file is missing.
    /// </summary>
    Task<string> GetFileStampAsync(string filePath);

    /// <summary>
    /// Reads the text of a file.
    /// </summary>
    Task<Result<string>> ReadFileTextAsync(string filePath);
}

internal sealed class LanguageServerWorkspace : ILanguageServerWorkspace
{
    private readonly ICommandService _commandService;
    private readonly IProjectService _projectService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ILocalFileSystem _fileSystem;
    private readonly ILogger<LanguageServerWorkspace> _logger;

    public LanguageServerWorkspace(
        ICommandService commandService,
        IProjectService projectService,
        IWorkspaceWrapper workspaceWrapper,
        ILocalFileSystem fileSystem,
        ILogger<LanguageServerWorkspace> logger)
    {
        _commandService = commandService;
        _projectService = projectService;
        _workspaceWrapper = workspaceWrapper;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public async Task<Result> ApplyEditAsync(IReadOnlyList<FileEdits> edits)
    {
        var fileRangeEdits = new List<FileRangeEdit>();
        foreach (var fileEdits in edits)
        {
            var resourceResult = GetProjectResource(fileEdits.FilePath);
            if (resourceResult.IsFailure)
            {
                return Result.Fail("The edit names a file outside the project")
                    .WithErrors(resourceResult);
            }
            var resource = resourceResult.Value;

            fileRangeEdits.Add(new FileRangeEdit(resource, fileEdits.Edits));
        }

        var applyResult = await _commandService.ExecuteAsync<IApplyDocumentEditsCommand, DocumentEditsResult>(command =>
        {
            command.Edits = fileRangeEdits;
        });
        if (applyResult.IsFailure)
        {
            return Result.Fail("Failed to apply a language server's edit")
                .WithErrors(applyResult);
        }
        var editResult = applyResult.Value;

        _logger.LogDebug("Applied a language server's edit to {Count} files", editResult.ChangedFiles.Count);

        return Result.Ok();
    }

    public async Task<bool> ShowDocumentAsync(string filePath, DocumentRange? selection)
    {
        var resourceResult = GetProjectResource(filePath);
        if (resourceResult.IsFailure)
        {
            return false;
        }
        var resource = resourceResult.Value;

        var location = string.Empty;
        if (selection is not null)
        {
            location = DocumentLocation.Compose(selection.Line, selection.Column, selection.EndLine, selection.EndColumn);
        }

        var openResult = await _commandService.ExecuteAsync<IOpenDocumentCommand, OpenDocumentOutcome>(command =>
        {
            command.FileResource = resource;
            command.Location = location;
            command.Activate = true;
        });
        if (openResult.IsFailure)
        {
            _logger.LogWarning(openResult, $"Failed to open '{resource}' for a language server");
            return false;
        }
        var outcome = openResult.Value;

        return outcome == OpenDocumentOutcome.Opened;
    }

    public async Task<string> GetFileStampAsync(string filePath)
    {
        var infoResult = await _fileSystem.GetInfoAsync(filePath);
        if (infoResult.IsFailure)
        {
            return string.Empty;
        }
        var info = infoResult.Value;

        if (info.Kind != StorageItemKind.File)
        {
            return string.Empty;
        }

        return $"{info.ModifiedUtc.Ticks}-{info.Size}";
    }

    public async Task<Result<string>> ReadFileTextAsync(string filePath)
    {
        return await _fileSystem.ReadAllTextAsync(filePath);
    }

    // Returns the resource key of a file inside the project folder, and refuses a file outside the folder.
    private Result<ResourceKey> GetProjectResource(string filePath)
    {
        var project = _projectService.CurrentProject;
        if (project is null
            || !_workspaceWrapper.IsWorkspaceLoaded)
        {
            return Result.Fail("No project is loaded");
        }

        if (!ProjectFolder.Contains(project.ProjectFolderPath, filePath))
        {
            return Result.Fail($"The file is outside the project folder: '{filePath}'");
        }

        var rootHandlers = _workspaceWrapper.WorkspaceService.ResourceService.RootHandlers;

        return rootHandlers.GetResourceKey(filePath);
    }
}
