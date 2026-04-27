using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.Documents.Commands;

public class DeleteLinesCommand : CommandBase, IDeleteLinesCommand
{
    private readonly ILogger<DeleteLinesCommand> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey Resource { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }

    public DeleteLinesCommand(
        ILogger<DeleteLinesCommand> logger,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (StartLine < 1 || EndLine < StartLine)
        {
            return Result.Fail($"Invalid line range: startLine={StartLine}, endLine={EndLine}");
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        return await DeleteLinesFromDisk(resourceRegistry);
    }

    private async Task<Result> DeleteLinesFromDisk(IResourceRegistry resourceRegistry)
    {
        var resolveResult = resourceRegistry.ResolveResourcePath(Resource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for resource: '{Resource}'")
                .WithErrors(resolveResult);
        }
        var resourcePath = resolveResult.Value;

        if (!File.Exists(resourcePath))
        {
            return Result.Fail($"File not found: '{Resource}'");
        }

        var lines = new List<string>(await File.ReadAllLinesAsync(resourcePath));

        var deleteResult = DeleteLinesHelper.DeleteLinesFromList(lines, StartLine, EndLine);
        if (deleteResult.IsFailure)
        {
            return deleteResult;
        }

        await File.WriteAllLinesAsync(resourcePath, lines);

        return Result.Ok();
    }
}
