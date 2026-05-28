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
    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ArchiveResourceCommand(
        ILogger<ArchiveResourceCommand> logger,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _messengerService = messengerService;
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var result = await ExecuteArchiveAsync();
        if (result.IsFailure)
        {
            var sourceName = SourceResource.ResourceName;
            var failedItems = new List<string> { sourceName };
            _messengerService.Send(new ResourceOperationFailedMessage(ResourceOperationType.Archive, failedItems));
        }
        return result;
    }

    // Recursive walk via the chokepoint to collect every descendant file
    // together with the relative archive entry name. Mirrors the prior
    // Directory.GetFiles(..., AllDirectories) traversal but routes through
    // EnumerateFolderAsync so the read side honours the same containment
    // validation as the write side.
    private static async Task CollectArchiveEntriesAsync(
        IFileStorage fileStorage,
        ResourceKey folder,
        string relativePrefix,
        List<(ResourceKey Resource, string RelativePath)> entries)
    {
        var enumerateResult = await fileStorage.EnumerateFolderAsync(folder);
        if (enumerateResult.IsFailure)
        {
            return;
        }

        foreach (var item in enumerateResult.Value)
        {
            var name = item.Resource.ResourceName;
            var childRelative = string.IsNullOrEmpty(relativePrefix)
                ? name
                : $"{relativePrefix}/{name}";

            if (item.IsFolder)
            {
                await CollectArchiveEntriesAsync(fileStorage, item.Resource, childRelative, entries);
            }
            else
            {
                entries.Add((item.Resource, childRelative));
            }
        }
    }

    private async Task<Result> ExecuteArchiveAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail("Workspace is not loaded");
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceRegistry = workspaceService.ResourceService.Registry;
        var resourceOpService = workspaceService.ResourceService.OperationService;
        var fileStorage = workspaceService.FileStorage;

        if (!ResourceKey.IsValidKey(SourceResource))
        {
            return Result.Fail($"Invalid source resource key: '{SourceResource}'");
        }

        if (!ResourceKey.IsValidKey(ArchiveResource))
        {
            return Result.Fail($"Invalid archive resource key: '{ArchiveResource}'");
        }

        var resolveSourceResult = resourceRegistry.ResolveResourcePath(SourceResource);
        if (resolveSourceResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for resource: '{SourceResource}'")
                .WithErrors(resolveSourceResult);
        }
        var sourcePath = resolveSourceResult.Value;

        var resolveArchiveResult = resourceRegistry.ResolveResourcePath(ArchiveResource);
        if (resolveArchiveResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for resource: '{ArchiveResource}'")
                .WithErrors(resolveArchiveResult);
        }
        var archivePath = resolveArchiveResult.Value;

        var sourceInfoResult = await fileStorage.GetInfoAsync(SourceResource);
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

        var archiveInfoResult = await fileStorage.GetInfoAsync(ArchiveResource);
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
            var deleteResult = await resourceOpService.DeleteFileAsync(archivePath);
            if (deleteResult.IsFailure)
            {
                return deleteResult;
            }
        }

        // Build the zip to a temporary file first
        var tempPath = Path.GetTempFileName();
        int entryCount = 0;

        try
        {
            using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zipArchive = new ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: false))
            {
                if (isFile)
                {
                    var fileName = SourceResource.ResourceName;

                    if (ArchiveHelper.ShouldIncludeFile(fileName, includeRegexes, excludeRegexes))
                    {
                        var addResult = await ArchiveHelper.AddFileToArchiveAsync(zipArchive, fileStorage, SourceResource, fileName);
                        if (addResult.IsFailure)
                        {
                            return addResult;
                        }
                        entryCount++;
                    }
                }
                else
                {
                    var fileEntries = new List<(ResourceKey Resource, string RelativePath)>();
                    await CollectArchiveEntriesAsync(fileStorage, SourceResource, string.Empty, fileEntries);

                    foreach (var (fileResource, relativePath) in fileEntries)
                    {
                        if (!ArchiveHelper.ShouldIncludeFile(relativePath, includeRegexes, excludeRegexes))
                        {
                            continue;
                        }

                        var addResult = await ArchiveHelper.AddFileToArchiveAsync(zipArchive, fileStorage, fileResource, relativePath);
                        if (addResult.IsFailure)
                        {
                            return addResult;
                        }
                        entryCount++;
                    }
                }
            }

            // Read the temp file and register it as an undoable create operation.
            // The temp file lives under the OS temp folder, outside the project
            // tree, so the chokepoint contract does not apply.
            var archiveBytes = await File.ReadAllBytesAsync(tempPath);

            var createResult = await resourceOpService.CreateFileAsync(archivePath, archiveBytes);
            if (createResult.IsFailure)
            {
                return createResult;
            }

            var archiveProbeResult = await fileStorage.GetInfoAsync(ArchiveResource);
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
        catch (IOException exception)
        {
            _logger.LogError(exception, "Failed to create archive");
            return Result.Fail($"Failed to create archive: {exception.Message}");
        }
        finally
        {
            // Clean up temp file
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
