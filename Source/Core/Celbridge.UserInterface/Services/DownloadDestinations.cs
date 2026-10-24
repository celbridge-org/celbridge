using Celbridge.Localization;
using Celbridge.Projects;
using Celbridge.Utilities;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Services;

/// <summary>
/// Chooses where downloads land: the folder the project names, and a free name in that folder for each
/// download. Records the destinations promised to downloads in progress, so each download gets a different one.
/// </summary>
internal sealed class DownloadDestinations
{
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectService _projectService;
    private readonly ILocalFileSystem _localFileSystem;
    private readonly ILocalizerService _localizerService;

    private readonly Lock _lock = new();

    // The destinations promised to downloads. A destination stays here until its file lands or the move fails.
    // A transfer ends before its move, so during the move only this set shows the destination is taken.
    private readonly HashSet<ResourceKey> _reservedDestinations = new();

    // Finding a free name and reserving it happen as one step. Two downloads of the same name started together
    // then get different names.
    private readonly SemaphoreSlim _reserveGate = new(1, 1);

    public DownloadDestinations(
        IWorkspaceWrapper workspaceWrapper,
        IProjectService projectService,
        ILocalFileSystem localFileSystem,
        ILocalizerService localizerService)
    {
        _workspaceWrapper = workspaceWrapper;
        _projectService = projectService;
        _localFileSystem = localFileSystem;
        _localizerService = localizerService;
    }

    /// <summary>
    /// Returns the full path of the folder downloads land in. Fails while no workspace is loaded.
    /// </summary>
    public Result<string> GetFolderPath()
    {
        if (!_workspaceWrapper.HasWorkspaceService)
        {
            return Result<string>.Fail("No workspace is loaded");
        }

        var rootHandlerRegistry = _workspaceWrapper.WorkspaceService.ResourceService.RootHandlers;
        var folderResult = ResolveDownloadsFolder(rootHandlerRegistry);
        if (folderResult.IsFailure)
        {
            return Result<string>.Fail().WithErrors(folderResult);
        }
        var downloadsFolder = folderResult.Value;

        var resolveResult = rootHandlerRegistry.ResolveResourcePath(downloadsFolder);
        if (resolveResult.IsFailure)
        {
            return Result<string>.Fail($"Failed to resolve the downloads folder '{downloadsFolder}'")
                .WithErrors(resolveResult);
        }

        try
        {
            return Path.GetFullPath(resolveResult.Value);
        }
        catch (Exception ex)
        {
            return Result<string>.Fail($"Failed to resolve the downloads folder '{downloadsFolder}'")
                .WithException(ex);
        }
    }

    /// <summary>
    /// Finds a free destination for the file name in the downloads folder, and reserves it until Release. A
    /// destination is free when it is absent from disk and not reserved. Appends " (N)" before the extension
    /// until the destination is free.
    /// </summary>
    public async Task<Result<ResourceKey>> ReserveAsync(string fileName)
    {
        if (!_workspaceWrapper.HasWorkspaceService)
        {
            return Result<ResourceKey>.Fail("No workspace is loaded");
        }

        var rootHandlerRegistry = _workspaceWrapper.WorkspaceService.ResourceService.RootHandlers;

        await _reserveGate.WaitAsync();
        try
        {
            var findResult = await FindFreeDestinationAsync(rootHandlerRegistry, fileName);
            if (findResult.IsFailure)
            {
                return findResult;
            }
            var destination = findResult.Value;

            lock (_lock)
            {
                _reservedDestinations.Add(destination);
            }

            return destination;
        }
        finally
        {
            _reserveGate.Release();
        }
    }

    /// <summary>
    /// Releases a destination ReserveAsync reserved, so another download may land there.
    /// </summary>
    public void Release(ResourceKey destination)
    {
        lock (_lock)
        {
            _reservedDestinations.Remove(destination);
        }
    }

    /// <summary>
    /// Releases every reserved destination.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _reservedDestinations.Clear();
        }
    }

    /// <summary>
    /// Returns the row's message for a destination ReserveAsync could not provide. When the downloads setting
    /// differs from the folder's stored name, the message names the stored folder. The user can then correct
    /// the setting or rename the folder.
    /// </summary>
    public string DescribeUnavailable(Result reserveResult)
    {
        if (reserveResult.FirstException is StoredNameMismatchError { StoredResource: ResourceKey storedFolder } mismatch)
        {
            return GetString("Downloads_FolderNameMismatch", mismatch.Resource.Path, storedFolder.Path);
        }

        return GetString("Downloads_DestinationUnavailable");
    }

    /// <summary>
    /// Returns the row's message for a destination the resource policy denies. It names the reserved folder
    /// that matched.
    /// </summary>
    public string DescribeBlocked(Result probeResult, string fileName)
    {
        // Only Celbridge's own reserved folders can deny a destination. Nothing a project configures denies a
        // read or a write, and a downloads folder inside a reserved folder is refused before it is used.
        if (probeResult.FirstException is not PolicyDenialError denial)
        {
            return GetString("Downloads_DestinationUnavailable");
        }

        var reservedName = denial.MatchedRule.Pattern.Split('/')[0];

        return GetString("Downloads_Blocked", fileName, reservedName);
    }

    // Downloads land in the folder the project names. A project that names none gets downloads/, which keeps
    // the project root tidy after several downloads.
    private Result<ResourceKey> ResolveDownloadsFolder(IRootHandlerRegistry rootHandlerRegistry)
    {
        var configuredFolder = _projectService.CurrentProject?.Config.Resources.DownloadsFolder ?? string.Empty;

        return DownloadsFolderPath.Resolve(rootHandlerRegistry, _localFileSystem, configuredFolder);
    }

    private async Task<Result<ResourceKey>> FindFreeDestinationAsync(IRootHandlerRegistry rootHandlerRegistry, string fileName)
    {
        var folderResult = ResolveDownloadsFolder(rootHandlerRegistry);
        if (folderResult.IsFailure)
        {
            return Result<ResourceKey>.Fail().WithErrors(folderResult);
        }
        var downloadsFolder = folderResult.Value;

        if (!ResourceKey.TryCreate($"{downloadsFolder.Path}/{fileName}", out _))
        {
            return Result<ResourceKey>.Fail($"'{fileName}' is not a valid download file name");
        }

        // Only the folder is resolved. The loop below checks the name against the files on disk. A name that
        // differs from an existing file's name only in letter case then takes the next free name.
        var resolveResult = rootHandlerRegistry.ResolveResourcePath(downloadsFolder);
        if (resolveResult.IsFailure)
        {
            return Result<ResourceKey>.Fail($"Failed to resolve the downloads folder '{downloadsFolder}'")
                .WithErrors(resolveResult);
        }

        try
        {
            var requestedPath = Path.GetFullPath(Path.Combine(resolveResult.Value, fileName));
            var folderPath = Path.GetDirectoryName(requestedPath)!;
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(requestedPath);
            var extension = Path.GetExtension(requestedPath);
            var candidateName = Path.GetFileName(requestedPath);
            var count = 1;

            while (true)
            {
                var candidatePath = Path.Combine(folderPath, candidateName);

                var getKeyResult = rootHandlerRegistry.GetResourceKey(candidatePath);
                if (getKeyResult.IsFailure)
                {
                    return Result<ResourceKey>.Fail($"Failed to resolve the download destination '{candidatePath}'")
                        .WithErrors(getKeyResult);
                }
                var candidateResource = getKeyResult.Value;

                var infoResult = await _localFileSystem.GetInfoAsync(candidatePath);
                var existsOnDisk = infoResult.IsSuccess &&
                    infoResult.Value.Kind != StorageItemKind.NotFound;

                if (!existsOnDisk &&
                    !IsReserved(candidateResource))
                {
                    return candidateResource;
                }

                candidateName = string.IsNullOrEmpty(extension)
                    ? $"{nameWithoutExtension} ({count})"
                    : $"{nameWithoutExtension} ({count}){extension}";
                count++;
            }
        }
        catch (Exception ex)
        {
            return Result<ResourceKey>.Fail($"Failed to find a free download destination for '{fileName}'")
                .WithException(ex);
        }
    }

    private bool IsReserved(ResourceKey resource)
    {
        lock (_lock)
        {
            return _reservedDestinations.Contains(resource);
        }
    }

    private string GetString(string name, params object[] arguments)
    {
        return _localizerService.GetString(name, arguments);
    }
}
