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
    public bool OpenDocument { get; set; } = true;

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

        var documentsService = _workspaceWrapper.WorkspaceService.DocumentsService;
        var documentsPanel = _workspaceWrapper.WorkspaceService.DocumentsPanel;
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var documentView = documentsPanel.GetDocumentView(Resource);

        if (documentView is not null)
        {
            var edit = await CreateDeleteLinesEdit(resourceRegistry);
            var applyResult = await documentView.ApplyEditsAsync(new[] { edit });
            if (applyResult.IsFailure)
            {
                _logger.LogError($"Failed to delete lines from document: {Resource}");
                return applyResult;
            }
        }
        else if (OpenDocument)
        {
            var openResult = await documentsService.OpenDocument(Resource, new OpenDocumentOptions(Activate: false));
            if (openResult.IsFailure)
            {
                _logger.LogWarning($"Failed to open document for deleting lines: {Resource}");
                return openResult;
            }

            documentView = documentsPanel.GetDocumentView(Resource);
            if (documentView is null)
            {
                return Result.Fail($"Document view not found after opening: {Resource}");
            }

            var edit = await CreateDeleteLinesEdit(resourceRegistry);
            var applyResult = await documentView.ApplyEditsAsync(new[] { edit });
            if (applyResult.IsFailure)
            {
                _logger.LogError($"Failed to delete lines from document: {Resource}");
                return applyResult;
            }
        }
        else
        {
            var deleteResult = await DeleteLinesFromDisk(resourceRegistry);
            if (deleteResult.IsFailure)
            {
                return deleteResult;
            }
        }

        return Result.Ok();
    }

    private async Task<TextEdit> CreateDeleteLinesEdit(IResourceRegistry resourceRegistry)
    {
        // Read the file to determine total line count so we know whether EndLine
        // is the last line. Monaco clamps out-of-range values, so a slightly stale
        // count is safe.
        var totalLineCount = await GetFileLineCount(resourceRegistry);
        return DeleteLinesHelper.CreateDeleteEdit(StartLine, EndLine, totalLineCount);
    }

    private async Task<int> GetFileLineCount(IResourceRegistry resourceRegistry)
    {
        var resolveResult = resourceRegistry.ResolveResourcePath(Resource);
        if (resolveResult.IsFailure)
        {
            return 0;
        }

        var resourcePath = resolveResult.Value;
        if (!File.Exists(resourcePath))
        {
            return 0;
        }

        var lines = await File.ReadAllLinesAsync(resourcePath);
        return lines.Length;
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
