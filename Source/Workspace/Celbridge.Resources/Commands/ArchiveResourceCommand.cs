using System.IO.Compression;
using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Resources.Services;
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

    public ArchiveResourceCommand(
        ILogger<ArchiveResourceCommand> logger,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail("Workspace is not loaded");
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceRegistry = workspaceService.ResourceService.Registry;
        var resourceOpService = workspaceService.ResourceService.OperationService;

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

        bool isFile = File.Exists(sourcePath);
        bool isFolder = Directory.Exists(sourcePath);

        if (!isFile && !isFolder)
        {
            return Result.Fail($"Resource not found: '{SourceResource}'");
        }

        if (!Overwrite && File.Exists(archivePath))
        {
            return Result.Fail($"Archive already exists: '{ArchiveResource}'. Set overwrite to true to replace it.");
        }

        var includeRegexes = ArchiveHelper.ParseGlobPatterns(Include);
        var excludeRegexes = ArchiveHelper.ParseGlobPatterns(Exclude);

        // If overwriting, delete the existing file first so it can be restored on undo
        if (Overwrite && File.Exists(archivePath))
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
                    var fileName = Path.GetFileName(sourcePath);

                    if (ArchiveHelper.ShouldIncludeFile(fileName, includeRegexes, excludeRegexes))
                    {
                        await ArchiveHelper.AddFileToArchiveAsync(zipArchive, sourcePath, fileName);
                        entryCount++;
                    }
                }
                else
                {
                    var filePaths = Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories);

                    foreach (var filePath in filePaths)
                    {
                        var fileAttributes = File.GetAttributes(filePath);
                        if (fileAttributes.HasFlag(System.IO.FileAttributes.ReparsePoint))
                        {
                            continue;
                        }

                        var relativePath = Path.GetRelativePath(sourcePath, filePath);
                        var entryName = relativePath.Replace('\\', '/');

                        if (!ArchiveHelper.ShouldIncludeFile(entryName, includeRegexes, excludeRegexes))
                        {
                            continue;
                        }

                        await ArchiveHelper.AddFileToArchiveAsync(zipArchive, filePath, entryName);
                        entryCount++;
                    }
                }
            }

            // Read the temp file and register it as an undoable create operation
            var archiveBytes = await File.ReadAllBytesAsync(tempPath);

            // Ensure parent folder exists
            var archiveFolder = Path.GetDirectoryName(archivePath);
            if (!string.IsNullOrEmpty(archiveFolder) && !Directory.Exists(archiveFolder))
            {
                Directory.CreateDirectory(archiveFolder);
            }

            var createResult = await resourceOpService.CreateFileAsync(archivePath, archiveBytes);
            if (createResult.IsFailure)
            {
                return createResult;
            }

            var archiveSize = new FileInfo(archivePath).Length;

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
