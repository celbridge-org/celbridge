using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

public class WriteFileCommand : CommandBase, IWriteFileCommand
{
    // A write can change a file's sidecar classification (e.g. a .cel file
    // becoming invalid TOML), so refresh the registry after every write.
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
        var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;

        var separatorResult = await ResolveTargetSeparatorAsync(resourceFileSystem);
        if (separatorResult.IsFailure)
        {
            return separatorResult;
        }
        var targetSeparator = separatorResult.Value;

        var contentToWrite = LineEndingHelper.ConvertLineEndings(Content, targetSeparator);

        return await resourceFileSystem.WriteAllTextAsync(FileResource, contentToWrite);
    }

    // Preserve the existing file's line endings on overwrite. For a new file,
    // honour whatever the caller's content already uses (so a CSV exporter
    // emitting CRLF lands as CRLF on disk); fall back to the platform default
    // when neither has line endings to detect.
    private async Task<Result<string>> ResolveTargetSeparatorAsync(IResourceFileSystem resourceFileSystem)
    {
        var infoResult = await resourceFileSystem.GetInfoAsync(FileResource);
        if (infoResult.IsFailure
            || infoResult.Value.Kind != StorageItemKind.File)
        {
            return LineEndingHelper.DetectSeparatorOrDefault(Content);
        }

        var readResult = await resourceFileSystem.ReadAllTextAsync(FileResource);
        if (readResult.IsFailure)
        {
            return Result<string>.Fail($"Failed to read existing file: '{FileResource}'")
                .WithErrors(readResult);
        }

        return LineEndingHelper.DetectSeparatorOrDefault(readResult.Value);
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
