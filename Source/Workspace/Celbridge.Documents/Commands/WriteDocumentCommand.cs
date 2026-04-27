using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.Documents.Commands;

public class WriteDocumentCommand : CommandBase, IWriteDocumentCommand
{
    private readonly ILogger<WriteDocumentCommand> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }
    public string Content { get; set; } = string.Empty;

    public WriteDocumentCommand(
        ILogger<WriteDocumentCommand> logger,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var resolveResult = resourceRegistry.ResolveResourcePath(FileResource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for resource: '{FileResource}'")
                .WithErrors(resolveResult);
        }
        var resourcePath = resolveResult.Value;

        var isNewFile = !File.Exists(resourcePath);
        if (isNewFile)
        {
            var parentFolder = Path.GetDirectoryName(resourcePath);
            if (!string.IsNullOrEmpty(parentFolder))
            {
                Directory.CreateDirectory(parentFolder);
            }
        }

        await File.WriteAllTextAsync(resourcePath, Content);

        if (isNewFile)
        {
            // Update the resource registry so the new file is immediately visible
            resourceRegistry.UpdateResourceRegistry();
        }

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void WriteDocument(ResourceKey fileResource, string content)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IWriteDocumentCommand>(command =>
        {
            command.FileResource = fileResource;
            command.Content = content;
        });
    }
}
