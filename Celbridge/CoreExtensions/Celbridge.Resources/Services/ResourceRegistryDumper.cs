using Celbridge.Logging;
using Celbridge.Resources;
using Celbridge.Utilities;
using Celbridge.Workspace;

namespace Celbridge.Commands.Services;

public class ResourceRegistryDumper : IResourceRegistryDumper, IDisposable
{
    private const string DumpFileName = "ResourceRegistry.jsonl";

    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ILogSerializer _serializer;
    private readonly IDumpFile _dumpFile;

    public ResourceRegistryDumper(
        IServiceProvider serviceProvider,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper,
        ILogSerializer logSerializer)
    {
        _messengerService = messengerService;
        _workspaceWrapper = workspaceWrapper;
        _serializer = logSerializer;

        // Create the dump file
        _dumpFile = serviceProvider.GetRequiredService<IDumpFile>();
    }

    public Result Initialize(string logFolderPath)
    {
        var dumpFilePath = Path.Combine(logFolderPath, DumpFileName);

        var initResult = _dumpFile.Initialize(dumpFilePath);
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

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.ResourceRegistry;

        _dumpFile.ClearFile();

        // Write all resources to the dump file
        WriteFolder(resourceRegistry.RootFolder);

        // Write expanded folders to the dump file
        foreach (var expandedFolder in resourceRegistry.ExpandedFolders)
        {
            WriteObject(expandedFolder);
        }

        void WriteFolder(IFolderResource folder)
        {
            foreach (var childResource in folder.Children)
            {

                var key = resourceRegistry.GetResourceKey(childResource);
                if (childResource is IFileResource childFileResource)
                {
                    var item = new Dictionary<string, object>
                    {
                        { "Key", key },
                        { "ResourceId", childFileResource.ResourceId }
                    };

                    WriteObject(item);
                }
                else if (childResource is IFolderResource childFolderResource)
                {
                    var item = new Dictionary<string, object>
                    {
                        { "Key", key },
                        { "IsExpanded", childFolderResource.IsExpanded },
                        { "ResourceId", childFolderResource.ResourceId }
                    };

                    WriteObject(item);
                    WriteFolder(childFolderResource);
                }
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
            var writeResult = _dumpFile.WriteLine(logEntry);
            if (writeResult.IsFailure)
            {
                return writeResult;
            }
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to write object to dump file. {ex}");
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
