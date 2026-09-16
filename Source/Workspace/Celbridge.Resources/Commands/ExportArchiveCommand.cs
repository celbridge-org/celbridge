using System.IO.Compression;
using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Localization;
using Celbridge.Logging;
using Celbridge.Resources.Helpers;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

public class ExportArchiveCommand : CommandBase, IExportArchiveCommand
{
    private const string ProgressTitleKey = "ResourceTree_ExportArchiveProgress";

    public override CommandFlags CommandFlags => CommandFlags.None;

    public ResourceKey SourceResource { get; set; }
    public string ArchiveFilePath { get; set; } = string.Empty;

    private readonly ILogger<ExportArchiveCommand> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ILocalFileSystem _fileSystem;
    private readonly IDialogService _dialogService;
    private readonly ILocalizerService _localizerService;
    private readonly ResourceOperationNotifier _operationNotifier;

    public ExportArchiveCommand(
        ILogger<ExportArchiveCommand> logger,
        IWorkspaceWrapper workspaceWrapper,
        ILocalFileSystem fileSystem,
        IDialogService dialogService,
        ILocalizerService localizerService,
        ResourceOperationNotifier operationNotifier)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
        _fileSystem = fileSystem;
        _dialogService = dialogService;
        _localizerService = localizerService;
        _operationNotifier = operationNotifier;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var archiveFileName = Path.GetFileName(ArchiveFilePath);
        var progressTitle = _localizerService.GetString(ProgressTitleKey, archiveFileName);

        Result result;
        using (_dialogService.AcquireProgressDialog(progressTitle))
        {
            result = await ExecuteExportAsync();
        }

        if (result.IsFailure)
        {
            var failedResources = new List<FailedResource>
            {
                new FailedResource(SourceResource, result.MessageChain)
            };

            await _operationNotifier.NotifyFailuresAsync(ResourceOperationType.Export, failedResources);
        }

        return result;
    }

    private async Task<Result> ExecuteExportAsync()
    {
        if (!_workspaceWrapper.IsWorkspaceLoaded)
        {
            return Result.Fail("Workspace is not loaded");
        }

        if (!ResourceKey.IsValidKey(SourceResource))
        {
            return Result.Fail($"Invalid source resource key: '{SourceResource}'");
        }

        if (!Path.IsPathFullyQualified(ArchiveFilePath))
        {
            return Result.Fail($"Archive file path is not an absolute path: '{ArchiveFilePath}'");
        }

        var resourceService = _workspaceWrapper.WorkspaceService.ResourceService;
        var resourceFileSystem = resourceService.FileSystem;

        var sourceInfoResult = await resourceFileSystem.GetInfoAsync(SourceResource);
        if (sourceInfoResult.IsFailure)
        {
            return Result.Fail($"Failed to probe source resource: '{SourceResource}'")
                .WithErrors(sourceInfoResult);
        }

        var sourceKind = sourceInfoResult.Value.Kind;
        if (sourceKind == StorageItemKind.NotFound)
        {
            return Result.Fail($"Resource not found: '{SourceResource}'");
        }

        var isFolder = sourceKind == StorageItemKind.Folder;
        var sourceFiles = await ArchiveHelper.CollectSourceFilesAsync(resourceFileSystem, SourceResource, isFolder);

        // A destination inside the project would be archived into itself. The save dialog can create an empty
        // file at the chosen path before the export runs, and an earlier export may already be there.
        var destinationKeyResult = resourceService.Registry.GetResourceKey(ArchiveFilePath);
        if (destinationKeyResult.IsSuccess)
        {
            var destinationKey = destinationKeyResult.Value;
            sourceFiles.RemoveAll(sourceFile => sourceFile.Resource == destinationKey);
        }

        // The archive is written beside the destination and moved into place once complete, so a failed
        // export never leaves a partial archive behind or loses the file it was replacing.
        var temporaryFilePath = $"{ArchiveFilePath}.{Guid.NewGuid():N}.tmp";

        var writeResult = await WriteArchiveFileAsync(resourceFileSystem, sourceFiles, temporaryFilePath);
        if (writeResult.IsFailure)
        {
            await CleanUpFailedExportAsync(temporaryFilePath);
            return writeResult;
        }

        var moveResult = await _fileSystem.MoveFileAsync(temporaryFilePath, ArchiveFilePath, overwrite: true);
        if (moveResult.IsFailure)
        {
            await CleanUpFailedExportAsync(temporaryFilePath);
            return Result.Fail($"Failed to move the archive into place: '{ArchiveFilePath}'")
                .WithErrors(moveResult);
        }

        return Result.Ok();
    }

    private async Task<Result> WriteArchiveFileAsync(
        IResourceFileSystem resourceFileSystem,
        IReadOnlyList<ArchiveSourceFile> sourceFiles,
        string filePath)
    {
        var openResult = await _fileSystem.OpenWriteAsync(filePath, WriteMode.CreateNew);
        if (openResult.IsFailure)
        {
            return Result.Fail($"Failed to create the archive file: '{filePath}'")
                .WithErrors(openResult);
        }

        try
        {
            await using var fileStream = openResult.Value;
            using var zipArchive = new ZipArchive(fileStream, ZipArchiveMode.Create);

            return await ArchiveHelper.AddSourceFilesToArchiveAsync(zipArchive, resourceFileSystem, sourceFiles);
        }
        catch (IOException exception)
        {
            _logger.LogError(exception, "Failed to write the exported archive");
            return Result.Fail($"Failed to write the archive: {exception.Message}");
        }
    }

    // Removes the partial temporary file. The save dialog can also create an empty file at the destination
    // before the export runs, which is removed rather than left as an archive that cannot be opened.
    private async Task CleanUpFailedExportAsync(string temporaryFilePath)
    {
        await DeleteFileIfPresentAsync(temporaryFilePath, deleteOnlyIfEmpty: false);
        await DeleteFileIfPresentAsync(ArchiveFilePath, deleteOnlyIfEmpty: true);
    }

    private async Task DeleteFileIfPresentAsync(string filePath, bool deleteOnlyIfEmpty)
    {
        var infoResult = await _fileSystem.GetInfoAsync(filePath);
        if (infoResult.IsFailure ||
            infoResult.Value.Kind != StorageItemKind.File)
        {
            return;
        }

        if (deleteOnlyIfEmpty &&
            infoResult.Value.Size > 0)
        {
            return;
        }

        var deleteResult = await _fileSystem.DeleteFileAsync(filePath);
        if (deleteResult.IsFailure)
        {
            _logger.LogWarning(deleteResult, $"Failed to delete file after a failed archive export: '{filePath}'");
        }
    }
}
