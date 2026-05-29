using System.IO.Compression;
using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Resources.Helpers;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

public class UnarchiveResourceCommand : CommandBase, IUnarchiveResourceCommand
{
    public override CommandFlags CommandFlags => CommandFlags.UpdateResources;

    public ResourceKey ArchiveResource { get; set; }
    public ResourceKey DestinationResource { get; set; }
    public bool Overwrite { get; set; }
    public UnarchiveResult ResultValue { get; private set; } = new UnarchiveResult
    {
        Entries = 0,
        Destination = string.Empty
    };

    private readonly ILogger<UnarchiveResourceCommand> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public UnarchiveResourceCommand(
        ILogger<UnarchiveResourceCommand> logger,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _messengerService = messengerService;
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var result = await ExecuteExtractAsync();
        if (result.IsFailure)
        {
            var archiveName = ArchiveResource.ResourceName;
            var failedItems = new List<string> { archiveName };
            _messengerService.Send(new ResourceOperationFailedMessage(ResourceOperationType.Extract, failedItems));
        }
        return result;
    }

    private async Task<Result> ExecuteExtractAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail("Workspace is not loaded");
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceRegistry = workspaceService.ResourceService.Registry;
        var resourceOpService = workspaceService.ResourceService.OperationService;
        var fileStorage = workspaceService.FileStorage;

        if (!ResourceKey.IsValidKey(ArchiveResource))
        {
            return Result.Fail($"Invalid archive resource key: '{ArchiveResource}'");
        }

        if (!ResourceKey.IsValidKey(DestinationResource))
        {
            return Result.Fail($"Invalid destination resource key: '{DestinationResource}'");
        }

        // Path resolution is still needed for entry-name validation and the
        // zip-slip canonicalization check below; the operation-service writes
        // themselves take ResourceKey arguments after cm-9c.
        var resolveDestinationResult = resourceRegistry.ResolveResourcePath(DestinationResource);
        if (resolveDestinationResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for resource: '{DestinationResource}'")
                .WithErrors(resolveDestinationResult);
        }
        var destinationPath = resolveDestinationResult.Value;

        var archiveInfoResult = await fileStorage.GetInfoAsync(ArchiveResource);
        if (archiveInfoResult.IsFailure
            || archiveInfoResult.Value.Kind != StorageItemKind.File)
        {
            return Result.Fail($"Archive not found: '{ArchiveResource}'");
        }

        int entryCount = 0;
        long totalExtractedBytes = 0;

        try
        {
            var openArchiveResult = await fileStorage.OpenReadAsync(ArchiveResource);
            if (openArchiveResult.IsFailure)
            {
                return Result.Fail($"Failed to open archive: '{ArchiveResource}'")
                    .WithErrors(openArchiveResult);
            }
            await using var fileStream = openArchiveResult.Value;
            using var zipArchive = new ZipArchive(fileStream, ZipArchiveMode.Read);

            // First pass: validate all entries and collect folder paths
            var validEntries = new List<ZipArchiveEntry>();
            var foldersToCreate = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in zipArchive.Entries)
            {
                var entryName = entry.FullName;

                if (string.IsNullOrEmpty(entryName) || entryName.EndsWith('/'))
                {
                    continue;
                }

                if (!ResourceKey.IsValidKey(entryName))
                {
                    return Result.Fail(
                        $"Archive contains an invalid entry name: '{entryName}'. " +
                        "Entry names must be valid resource keys (no '..', backslashes, or absolute paths).");
                }

                // Symlink protection
                if (ArchiveHelper.IsUnixSymlink(entry))
                {
                    continue;
                }

                // Zip bomb protection
                totalExtractedBytes += entry.Length;
                if (totalExtractedBytes > ArchiveHelper.MaxExtractedBytes)
                {
                    return Result.Fail(
                        $"Archive exceeds the maximum extracted size of {ArchiveHelper.MaxExtractedBytes / (1024 * 1024)} MB. " +
                        "Aborting extraction.");
                }

                var outputPath = Path.Combine(destinationPath, entryName.Replace('/', Path.DirectorySeparatorChar));

                // Path canonicalization check
                var normalizedOutputPath = Path.GetFullPath(outputPath);
                var normalizedDestinationPath = Path.GetFullPath(destinationPath + Path.DirectorySeparatorChar);
                if (!normalizedOutputPath.StartsWith(normalizedDestinationPath, StringComparison.OrdinalIgnoreCase))
                {
                    return Result.Fail(
                        $"Archive entry '{entryName}' would extract outside the destination folder. " +
                        "Aborting extraction.");
                }

                if (!Overwrite)
                {
                    var entryResource = DestinationResource.Combine(entryName);
                    var existingInfoResult = await fileStorage.GetInfoAsync(entryResource);
                    if (existingInfoResult.IsSuccess
                        && existingInfoResult.Value.Kind == StorageItemKind.File)
                    {
                        return Result.Fail(
                            $"File already exists: '{DestinationResource}/{entryName}'. " +
                            "Set overwrite to true to replace existing files.");
                    }
                }

                validEntries.Add(entry);

                // Collect parent folders that need to be created
                var outputFolder = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputFolder))
                {
                    ArchiveHelper.CollectFolderHierarchy(outputFolder, destinationPath, foldersToCreate);
                }
            }

            // Begin batch so all operations are a single undo unit.
            using var batch = resourceOpService.BeginBatch();

            // Create the destination folder and any missing ancestors through
            // the operation service so the whole chain lands in the unarchive's
            // undo batch.
            var destInfoResult = await fileStorage.GetInfoAsync(DestinationResource);
            if (destInfoResult.IsFailure)
            {
                return Result.Fail($"Failed to probe destination resource: '{DestinationResource}'")
                    .WithErrors(destInfoResult);
            }

            if (destInfoResult.Value.Kind == StorageItemKind.NotFound)
            {
                // CreateFolderAsync on the chokepoint is idempotent and creates
                // missing intermediate parents in one call. We still collect
                // and create ancestors one-at-a-time so each lands as its own
                // undoable operation inside the batch.
                var missingAncestorKeys = new List<ResourceKey>();
                var ancestorKey = DestinationResource.GetParent();
                while (!ancestorKey.IsEmpty)
                {
                    var ancestorInfoResult = await fileStorage.GetInfoAsync(ancestorKey);
                    if (ancestorInfoResult.IsFailure)
                    {
                        return Result.Fail($"Failed to probe ancestor resource: '{ancestorKey}'")
                            .WithErrors(ancestorInfoResult);
                    }
                    if (ancestorInfoResult.Value.Kind != StorageItemKind.NotFound)
                    {
                        break;
                    }
                    missingAncestorKeys.Add(ancestorKey);
                    ancestorKey = ancestorKey.GetParent();
                }
                missingAncestorKeys.Reverse();

                foreach (var key in missingAncestorKeys)
                {
                    var createAncestorResult = await resourceOpService.CreateFolderAsync(key);
                    if (createAncestorResult.IsFailure)
                    {
                        return createAncestorResult;
                    }
                }

                var createDestResult = await resourceOpService.CreateFolderAsync(DestinationResource);
                if (createDestResult.IsFailure)
                {
                    return createDestResult;
                }
            }

            // Filter shallowest-first so parents are created before children.
            var sortedFolderKeys = new List<ResourceKey>();
            foreach (var folderPath in foldersToCreate.OrderBy(path => path.Length))
            {
                var folderKey = BuildDescendantKey(DestinationResource, destinationPath, folderPath);
                var folderInfoResult = await fileStorage.GetInfoAsync(folderKey);
                if (folderInfoResult.IsFailure)
                {
                    return Result.Fail($"Failed to probe folder resource: '{folderKey}'")
                        .WithErrors(folderInfoResult);
                }
                if (folderInfoResult.Value.Kind == StorageItemKind.NotFound)
                {
                    sortedFolderKeys.Add(folderKey);
                }
            }

            foreach (var folderKey in sortedFolderKeys)
            {
                var createFolderResult = await resourceOpService.CreateFolderAsync(folderKey);
                if (createFolderResult.IsFailure)
                {
                    return createFolderResult;
                }
            }

            // Extract files
            foreach (var entry in validEntries)
            {
                var entryResource = DestinationResource.Combine(entry.FullName);

                // If overwriting, delete existing file first so it's preserved in trash for undo
                if (Overwrite)
                {
                    var existingInfoResult = await fileStorage.GetInfoAsync(entryResource);
                    if (existingInfoResult.IsSuccess
                        && existingInfoResult.Value.Kind == StorageItemKind.File)
                    {
                        var deleteResult = await resourceOpService.DeleteAsync(entryResource);
                        if (deleteResult.IsFailure)
                        {
                            return deleteResult;
                        }
                    }
                }

                // Read entry into byte array
                using var entryStream = entry.Open();
                using var memoryStream = new MemoryStream();
                await entryStream.CopyToAsync(memoryStream);
                var entryBytes = memoryStream.ToArray();

                var createResult = await resourceOpService.CreateFileAsync(entryResource, entryBytes);
                if (createResult.IsFailure)
                {
                    return createResult;
                }

                entryCount++;
            }
        }
        catch (IOException exception)
        {
            _logger.LogError(exception, "Failed to extract archive");
            return Result.Fail($"Failed to extract archive: {exception.Message}");
        }
        catch (InvalidDataException)
        {
            return Result.Fail($"The file '{ArchiveResource}' is not a valid zip archive.");
        }

        ResultValue = new UnarchiveResult
        {
            Entries = entryCount,
            Destination = DestinationResource.ToString()
        };

        return Result.Ok();
    }

    private static ResourceKey BuildDescendantKey(
        ResourceKey parentKey,
        string parentPath,
        string descendantPath)
    {
        var relative = Path.GetRelativePath(parentPath, descendantPath);
        var segments = relative.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);

        var key = parentKey;
        foreach (var segment in segments)
        {
            key = key.Combine(segment);
        }

        return key;
    }
}
