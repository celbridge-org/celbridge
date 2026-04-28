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

        var resourceService = _workspaceWrapper.WorkspaceService.ResourceService;

        return await DeleteLinesFromDisk(resourceService);
    }

    private async Task<Result> DeleteLinesFromDisk(IResourceService resourceService)
    {
        var resolveResult = resourceService.Registry.ResolveResourcePath(Resource);
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

        var originalContent = await File.ReadAllTextAsync(resourcePath);
        var originalSeparator = LineEndingHelper.DetectSeparatorOrDefault(originalContent);
        var originalEndsWithNewline = LineEndingHelper.EndsWithNewline(originalContent);

        var lines = LineEndingHelper.SplitToContentLines(originalContent);

        var deleteResult = DeleteLinesHelper.DeleteLinesFromList(lines, StartLine, EndLine);
        if (deleteResult.IsFailure)
        {
            return deleteResult;
        }

        var output = string.Join(originalSeparator, lines);
        if (originalEndsWithNewline && output.Length > 0)
        {
            output += originalSeparator;
        }

        return await resourceService.FileWriter.WriteAllTextAsync(Resource, output);
    }
}
