using Celbridge.Projects;

namespace Celbridge.WorkspaceUI.Services;

public sealed class WorkspaceSettingsService : IWorkspaceSettingsService, IDisposable
{
    private readonly ILocalFileSystem _fileSystem;

    private JsonWorkspaceStore? _workspaceStore;

    public IWorkspacePropertyBag? PropertyBag => _workspaceStore;

    public IWorkspaceSettingsStore? WorkspaceSettingsStore => _workspaceStore;

    public string? WorkspaceSettingsFolderPath { get; set; }

    public WorkspaceSettingsService(ILocalFileSystem fileSystem)
    {
        _fileSystem = fileSystem;

        // Workaround so that this check is not performed when running tests
        if (ServiceLocator.ServiceProvider is not null)
        {
            var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();
            // Only the workspace service is allowed to instantiate this service
            Guard.IsFalse(workspaceWrapper.IsWorkspacePageLoaded);
        }
    }

    public async Task<Result> AcquireWorkspaceSettingsAsync()
    {
        // Idempotent: called twice during load (page-load before the panels bind,
        // then the workspace loader). Reloading would discard unflushed in-memory writes.
        if (_workspaceStore is not null)
        {
            return Result.Ok();
        }

        if (string.IsNullOrEmpty(WorkspaceSettingsFolderPath))
        {
            return Result.Fail("The workspace settings folder has not been set.");
        }

        var createFolderResult = await EnsureSettingsFolderExistsAsync(WorkspaceSettingsFolderPath);
        if (createFolderResult.IsFailure)
        {
            return createFolderResult;
        }

        var filePath = Path.Combine(WorkspaceSettingsFolderPath, ProjectConstants.WorkspaceSettingsFile);

        var loadResult = await JsonWorkspaceStore.LoadAsync(_fileSystem, filePath);
        if (loadResult.IsFailure)
        {
            return Result.Fail($"Failed to load workspace settings file: {filePath}")
                .WithErrors(loadResult);
        }

        _workspaceStore = loadResult.Value;

        return Result.Ok();
    }

    public Result UnloadWorkspaceSettings()
    {
        _workspaceStore = null;

        return Result.Ok();
    }

    private async Task<Result> EnsureSettingsFolderExistsAsync(string folderPath)
    {
        var infoResult = await _fileSystem.GetInfoAsync(folderPath);
        bool folderExists = infoResult.IsSuccess
            && infoResult.Value.Kind == StorageItemKind.Folder;

        if (folderExists)
        {
            return Result.Ok();
        }

        return await _fileSystem.CreateFolderAsync(folderPath);
    }

    private bool _disposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                UnloadWorkspaceSettings();
            }

            _disposed = true;
        }
    }

    ~WorkspaceSettingsService()
    {
        Dispose(false);
    }
}
