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

    // Recursive walk via the gateway to collect every descendant file
    // together with the relative archive entry name. Mirrors the prior
    // Directory.GetFiles(..., AllDirectories) traversal but routes through
    // EnumerateFolderAsync so the read side honours the same containment
    // validation as the write side.
    private static async Task CollectArchiveEntriesAsync(
        IResourceFileSystem resourceFileSystem,
        ResourceKey folder,
        string relativePrefix,
        List<(ResourceKey Resource, string RelativePath)> entries)
    {
        var enumerateResult = await resourceFileSystem.EnumerateFolderAsync(folder);
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
                await CollectArchiveEntriesAsync(resourceFileSystem, item.Resource, childRelative, entries);
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
            using var memoryStream = new MemoryStream();
            using (var zipArchive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                if (isFile)
                {
                    var fileName = SourceResource.ResourceName;

                    if (ArchiveHelper.ShouldIncludeFile(fileName, includeRegexes, excludeRegexes))
                    {
                        var addResult = await ArchiveHelper.AddFileToArchiveAsync(zipArchive, resourceFileSystem, SourceResource, fileName);
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
                    await CollectArchiveEntriesAsync(resourceFileSystem, SourceResource, string.Empty, fileEntries);

                    foreach (var (fileResource, relativePath) in fileEntries)
                    {
                        if (!ArchiveHelper.ShouldIncludeFile(relativePath, includeRegexes, excludeRegexes))
                        {
                            continue;
                        }

                        var addResult = await ArchiveHelper.AddFileToArchiveAsync(zipArchive, resourceFileSystem, fileResource, relativePath);
                        if (addResult.IsFailure)
                        {
                            return addResult;
                        }
                        entryCount++;
                    }
                }
            }

            // Disposing the ZipArchive flushes the central directory into
            // memoryStream; leaveOpen:true keeps the buffer accessible.
            archiveBytes = memoryStream.ToArray();
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
