using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

public class WriteFileCommand : CommandBase, IWriteFileCommand
{
    private readonly ILogger<WriteFileCommand> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }
    public string Content { get; set; } = string.Empty;

    public WriteFileCommand(
        ILogger<WriteFileCommand> logger,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var resourceService = _workspaceWrapper.WorkspaceService.ResourceService;
        var resourceRegistry = resourceService.Registry;

        var resolveResult = resourceRegistry.ResolveResourcePath(FileResource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for resource: '{FileResource}'")
                .WithErrors(resolveResult);
        }
        var resourcePath = resolveResult.Value;

        var isNewFile = !File.Exists(resourcePath);

        // Preserve existing line endings when overwriting. Use the platform
        // default for new files.
        string targetSeparator;
        if (isNewFile)
        {
            targetSeparator = LineEndingHelper.PlatformDefault;
        }
        else
        {
            var existingContent = await File.ReadAllTextAsync(resourcePath);
            targetSeparator = LineEndingHelper.DetectSeparatorOrDefault(existingContent);
        }

        var contentToWrite = LineEndingHelper.ConvertLineEndings(Content, targetSeparator);

        var writeResult = await resourceService.FileWriter.WriteAllTextAsync(FileResource, contentToWrite);
        if (writeResult.IsFailure)
        {
            return writeResult;
        }

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

    public static void WriteFile(ResourceKey fileResource, string content)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IWriteFileCommand>(command =>
        {
            command.FileResource = fileResource;
            command.Content = content;
        });
    }
}
