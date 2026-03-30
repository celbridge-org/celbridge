using System.IO.Compression;
using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Resources.Services;
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

        if (!ResourceKey.IsValidKey(ArchiveResource))
        {
            return Result.Fail($"Invalid archive resource key: '{ArchiveResource}'");
        }

        if (!ResourceKey.IsValidKey(DestinationResource))
        {
            return Result.Fail($"Invalid destination resource key: '{DestinationResource}'");
        }

        var resolveArchiveResult = resourceRegistry.ResolveResourcePath(ArchiveResource);
        if (resolveArchiveResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for resource: '{ArchiveResource}'")
                .WithErrors(resolveArchiveResult);
        }
        var archivePath = resolveArchiveResult.Value;

        var resolveDestinationResult = resourceRegistry.ResolveResourcePath(DestinationResource);
        if (resolveDestinationResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for resource: '{DestinationResource}'")
                .WithErrors(resolveDestinationResult);
        }
        var destinationPath = resolveDestinationResult.Value;

        if (!File.Exists(archivePath))
        {
            return Result.Fail($"Archive not found: '{ArchiveResource}'");
        }

        int entryCount = 0;
        long totalExtractedBytes = 0;

        try
        {
            using var fileStream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
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

                if (!Overwrite && File.Exists(outputPath))
                {
                    return Result.Fail(
                        $"File already exists: '{DestinationResource}/{entryName}'. " +
                        "Set overwrite to true to replace existing files.");
                }

                validEntries.Add(entry);

                // Collect parent folders that need to be created
                var outputFolder = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputFolder))
                {
                    ArchiveHelper.CollectFolderHierarchy(outputFolder, destinationPath, foldersToCreate);
                }
            }

            // Begin batch so all operations are a single undo unit
            resourceOpService.BeginBatch();

            try
            {
                // Create the destination folder if it doesn't exist
                if (!Directory.Exists(destinationPath))
                {
                    var parentFolder = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrEmpty(parentFolder) && !Directory.Exists(parentFolder))
                    {
                        Directory.CreateDirectory(parentFolder);
                    }

                    var createDestResult = await resourceOpService.CreateFolderAsync(destinationPath);
                    if (createDestResult.IsFailure)
                    {
                        return createDestResult;
                    }
                }

                // Create folders shallowest first (sorted by path length)
                var sortedFolders = foldersToCreate
                    .Where(folderPath => !Directory.Exists(folderPath))
                    .OrderBy(folderPath => folderPath.Length)
                    .ToList();

                foreach (var folderPath in sortedFolders)
                {
                    var createFolderResult = await resourceOpService.CreateFolderAsync(folderPath);
                    if (createFolderResult.IsFailure)
                    {
                        return createFolderResult;
                    }
                }

                // Extract files
                foreach (var entry in validEntries)
                {
                    var outputPath = Path.Combine(destinationPath, entry.FullName.Replace('/', Path.DirectorySeparatorChar));

                    // If overwriting, delete existing file first so it's preserved in trash for undo
                    if (Overwrite && File.Exists(outputPath))
                    {
                        var deleteResult = await resourceOpService.DeleteFileAsync(outputPath);
                        if (deleteResult.IsFailure)
                        {
                            return deleteResult;
                        }
                    }

                    // Read entry into byte array
                    using var entryStream = entry.Open();
                    using var memoryStream = new MemoryStream();
                    await entryStream.CopyToAsync(memoryStream);
                    var entryBytes = memoryStream.ToArray();

                    var createResult = await resourceOpService.CreateFileAsync(outputPath, entryBytes);
                    if (createResult.IsFailure)
                    {
                        return createResult;
                    }

                    entryCount++;
                }
            }
            finally
            {
                resourceOpService.CommitBatch();
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
}
