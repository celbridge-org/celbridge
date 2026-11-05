using Celbridge.Localization;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Services;

/// <summary>
/// Finds a free name for each download in the folder the user chose. Records the destinations promised to
/// downloads in progress, so each download gets a different one.
/// </summary>
internal sealed class DownloadDestinations
{
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ILocalFileSystem _localFileSystem;
    private readonly ILocalizerService _localizerService;

    private readonly Lock _lock = new();

    // The keys of the destinations promised to downloads. A destination stays here until its download lands, fails
    // or is canceled. A transfer ends before its move, so during the move only this set shows the destination is
    // taken. Keys are compared without letter case, since Windows and macOS volumes ignore it.
    private readonly HashSet<string> _reservedDestinations = new(StringComparer.OrdinalIgnoreCase);

    // Finding a free name and reserving it happen as one step. Two downloads of the same name started together
    // then get different names.
    private readonly SemaphoreSlim _reserveGate = new(1, 1);

    public DownloadDestinations(
        IWorkspaceWrapper workspaceWrapper,
        ILocalFileSystem localFileSystem,
        ILocalizerService localizerService)
    {
        _workspaceWrapper = workspaceWrapper;
        _localFileSystem = localFileSystem;
        _localizerService = localizerService;
    }

    /// <summary>
    /// Finds a free destination for the file name in the folder, and reserves it until Release. An empty folder
    /// is the project folder itself. A destination is free when it is absent from disk and not reserved. Appends
    /// " (N)" before the extension until the destination is free.
    /// </summary>
    public async Task<Result<ResourceKey>> ReserveAsync(ResourceKey folder, string fileName)
    {
        if (!_workspaceWrapper.HasWorkspaceService)
        {
            return Result<ResourceKey>.Fail("No workspace is loaded");
        }

        var rootHandlerRegistry = _workspaceWrapper.WorkspaceService.ResourceService.RootHandlers;

        await _reserveGate.WaitAsync();
        try
        {
            var findResult = await FindFreeDestinationAsync(rootHandlerRegistry, folder, fileName);
            if (findResult.IsFailure)
            {
                return findResult;
            }
            var destination = findResult.Value;

            lock (_lock)
            {
                _reservedDestinations.Add(destination.ToString());
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
            _reservedDestinations.Remove(destination.ToString());
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
    /// Returns the row's message for a destination the resource policy denies. It names the reserved folder
    /// that matched.
    /// </summary>
    public string DescribeBlocked(Result probeResult, string fileName)
    {
        // Only Celbridge's own reserved folders can deny a destination. Nothing a project configures denies a
        // read or a write.
        if (probeResult.FirstException is not PolicyDenialError denial)
        {
            return GetString("Downloads_Failed");
        }

        var reservedName = denial.MatchedRule.Pattern.Split('/')[0];

        return GetString("Downloads_Blocked", fileName, reservedName);
    }

    private async Task<Result<ResourceKey>> FindFreeDestinationAsync(
        IRootHandlerRegistry rootHandlerRegistry,
        ResourceKey folder,
        string fileName)
    {
        if (!ResourceKey.IsValidSegment(fileName))
        {
            return Result<ResourceKey>.Fail($"'{fileName}' is not a valid download file name");
        }

        // Only the folder is resolved. The loop below checks the name against the files on disk. A name that
        // differs from an existing file's name only in letter case then takes the next free name.
        var resolveResult = rootHandlerRegistry.ResolveResourcePath(folder);
        if (resolveResult.IsFailure)
        {
            return Result<ResourceKey>.Fail($"Failed to resolve the download folder '{folder}'")
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
            return _reservedDestinations.Contains(resource.ToString());
        }
    }

    private string GetString(string name, params object[] arguments)
    {
        return _localizerService.GetString(name, arguments);
    }
}
