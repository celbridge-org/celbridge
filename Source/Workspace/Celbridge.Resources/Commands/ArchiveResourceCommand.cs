using System.IO.Compression;
using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Resources.Helpers;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

public class ArchiveResourceCommand : CommandBase, IArchiveResourceCommand
{
    public override CommandFlags CommandFlags => CommandFlags.UpdateResources;

    public ResourceKey SourceResource { get; set; }
    public ResourceKey ArchiveResource { get; set; }
    public string Include { get; set; } = string.Empty;
    public string Exclude { get; set; } = string.Empty;
    public bool Overwrite { get; set; }
    public ArchiveResult ResultValue { get; private set; } = new ArchiveResult
    {
        Entries = 0,
        Size = 0,
        Archive = string.Empty
    };

    private readonly ILogger<ArchiveResourceCommand> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ResourceOperationNotifier _operationNotifier;

    public ArchiveResourceCommand(
        ILogger<ArchiveResourceCommand> logger,
        IWorkspaceWrapper workspaceWrapper,
        ResourceOperationNotifier operationNotifier)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
        _operationNotifier = operationNotifier;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var result = await ExecuteArchiveAsync();
        if (result.IsFailure)
        {
            var failedResources = new List<FailedResource>
            {
                new FailedResource(SourceResource, result.MessageChain)
            };

            await _operationNotifier.NotifyFailuresAsync(ResourceOperationType.Archive, failedResources);
        }

        return result;
    }

    private async Task<Result> ExecuteArchiveAsync()
    {
        if (!_workspaceWrapper.IsWorkspaceLoaded)
        {
            return Result.Fail("Workspace is not loaded");
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceRegistry = workspaceService.ResourceService.Registry;
        var resourceOpService = workspaceService.ResourceService.Operations;
        var resourceFileSystem = workspaceService.ResourceService.FileSystem;

        if (!ResourceKey.IsValidKey(SourceResource))
        {
            return Result.Fail($"Invalid source resource key: '{SourceResource}'");
        }

        if (!ResourceKey.IsValidKey(ArchiveResource))
        {
            return Result.Fail($"Invalid archive resource key: '{ArchiveResource}'");
        }

        var sourceInfoResult = await resourceFileSystem.GetInfoAsync(SourceResource);
        if (sourceInfoResult.IsFailure)
        {
            return Result.Fail($"Failed to probe source resource: '{SourceResource}'")
                .WithErrors(sourceInfoResult);
        }
        bool isFile = sourceInfoResult.Value.Kind == StorageItemKind.File;
        bool isFolder = sourceInfoResult.Value.Kind == StorageItemKind.Folder;

        if (!isFile && !isFolder)
        {
            return Result.Fail($"Resource not found: '{SourceResource}'");
        }

        var archiveInfoResult = await resourceFileSystem.GetInfoAsync(ArchiveResource);
        bool archiveExists = archiveInfoResult.IsSuccess
            && archiveInfoResult.Value.Kind == StorageItemKind.File;

        if (!Overwrite && archiveExists)
        {
            return Result.Fail($"Archive already exists: '{ArchiveResource}'. Set overwrite to true to replace it.");
        }

        var includeRegexes = ArchiveHelper.ParseGlobPatterns(Include);
        var excludeRegexes = ArchiveHelper.ParseGlobPatterns(Exclude);

        // If overwriting, delete the existing file first so it can be restored on undo
        if (Overwrite && archiveExists)
        {
            var deleteResult = await resourceOpService.DeleteAsync(ArchiveResource);
            if (deleteResult.IsFailure)
            {
                return deleteResult;
            }
        }

        int entryCount = 0;
        byte[] archiveBytes;

        try
        {
            var sourceFiles = await ArchiveHelper.CollectSourceFilesAsync(resourceFileSystem, SourceResource, isFolder);
            var includedFiles = sourceFiles
                .Where(sourceFile => ArchiveHelper.ShouldIncludeFile(sourceFile.EntryName, includeRegexes, excludeRegexes))
                .ToList();

            using var memoryStream = new MemoryStream();
            using (var zipArchive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var addResult = await ArchiveHelper.AddSourceFilesToArchiveAsync(zipArchive, resourceFileSystem, includedFiles);
                if (addResult.IsFailure)
                {
                    return addResult;
                }
            }

            // Disposing the ZipArchive flushes the central directory into
            // memoryStream; leaveOpen:true keeps the buffer accessible.
            archiveBytes = memoryStream.ToArray();
            entryCount = includedFiles.Count;
        }
        catch (IOException exception)
        {
            _logger.LogError(exception, "Failed to create archive");
            return Result.Fail($"Failed to create archive: {exception.Message}");
        }

        var createResult = await resourceOpService.CreateFileAsync(ArchiveResource, archiveBytes);
        if (createResult.IsFailure)
        {
            return createResult;
        }

        var archiveProbeResult = await resourceFileSystem.GetInfoAsync(ArchiveResource);
        long archiveSize = archiveProbeResult.IsSuccess
            ? archiveProbeResult.Value.Size
            : archiveBytes.Length;

        ResultValue = new ArchiveResult
        {
            Entries = entryCount,
            Size = archiveSize,
            Archive = ArchiveResource.ToString()
        };

        return Result.Ok();
    }
}
