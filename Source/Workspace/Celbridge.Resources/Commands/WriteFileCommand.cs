using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

public class WriteFileCommand : CommandBase, IWriteFileCommand
{
    // Force a registry update so sidecar classification refreshes on every
    // write. Without this, overwriting an existing .cel file with broken TOML
    // would leave data_check_project returning the stale "Healthy" status
    // while data_get_field correctly rejects the file at read time.
    public override CommandFlags CommandFlags => CommandFlags.UpdateResources;

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
        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceRegistry = workspaceService.ResourceService.Registry;
        var fileSystem = workspaceService.ResourceFileSystem;

        var resolveResult = resourceRegistry.ResolveResourcePath(FileResource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for resource: '{FileResource}'")
                .WithErrors(resolveResult);
        }
        var resourcePath = resolveResult.Value;

        // Preserve existing line endings when overwriting. For a new file,
        // honour whatever endings the caller's content already uses (so a CSV
        // exporter emitting CRLF lands as CRLF on disk); fall back to the
        // platform default when the content has no line endings to detect.
        string targetSeparator;
        if (!File.Exists(resourcePath))
        {
            targetSeparator = LineEndingHelper.DetectSeparatorOrDefault(Content);
        }
        else
        {
            var existingContent = await File.ReadAllTextAsync(resourcePath);
            targetSeparator = LineEndingHelper.DetectSeparatorOrDefault(existingContent);
        }

        var contentToWrite = LineEndingHelper.ConvertLineEndings(Content, targetSeparator);

        var writeResult = await fileSystem.WriteAllTextAsync(FileResource, contentToWrite);
        if (writeResult.IsFailure)
        {
            return writeResult;
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
