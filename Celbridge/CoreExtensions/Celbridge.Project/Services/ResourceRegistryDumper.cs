using Celbridge.Utilities;
using Celbridge.Resources;
using Celbridge.Workspace;
using CommunityToolkit.Diagnostics;

namespace Celbridge.Commands.Services;

public class ResourceRegistryDumper : IResourceRegistryDumper, IDisposable
{
    private const string LogFilePrefix = "ResourceRegistry";

    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ILogSerializer _serializer;
    private readonly ILogger _logger;

    public ResourceRegistryDumper(
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper,
        ILogSerializer logSerializer,
        ILogger logger)
    {
        _messengerService = messengerService;
        _workspaceWrapper = workspaceWrapper;
        _serializer = logSerializer;
        _logger = logger;
    }

    public Result Initialize(string logFolderPath)
    {
        var initResult = _logger.Initialize(logFolderPath, LogFilePrefix, 0);
        if (initResult.IsFailure)
        {
            return initResult;
        }

        // Start listening for resource registry updates
        _messengerService.Register<ResourceRegistryUpdatedMessage>(this, OnResourceRegistryUpdatedMessage);

        return Result.Ok();
    }

    private void OnResourceRegistryUpdatedMessage(object recipient, ResourceRegistryUpdatedMessage message)
    {
        // Clear the dump file
        // Append the new contents of the resource registry

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ProjectService.ResourceRegistry;

        _logger.ClearLogFile();

        // Write all resources to the dump file

        // Todo: Recurse into each folder
        // Todo: Write list of expanded folders in the dump file.
        foreach (var resource in resourceRegistry.RootFolder.Children)
        {
            if (resource is IFileResource fileResource)
            {
                // Todo: Make a dictionary instead of a tuple for serialization
                var item = ("File", fileResource.Name, fileResource.ResourceId);
                WriteObject(item);
            }
            else if (resource is IFolderResource folderResource)
            {
                var item = ("Folder", folderResource.Name, folderResource.ResourceId, folderResource.IsExpanded);
                WriteObject(item);
            }
        }

    }

    public Result WriteObject(object? obj)
    {
        if (obj is null)
        {
            return Result.Fail($"Object is null");
        }

        try
        {
            string logEntry = _serializer.SerializeObject(obj, false);
            var writeResult = _logger.WriteLine(logEntry);
            if (writeResult.IsFailure)
            {
                return writeResult;
            }
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to write object to log. {ex}");
        }

        return Result.Ok();
    }

    private bool _disposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed objects here
                _messengerService.Unregister<ResourceRegistryUpdatedMessage>(this);
            }

            _disposed = true;
        }
    }

    ~ResourceRegistryDumper()
    {
        Dispose(false);
    }
}
