using Celbridge.Documents;
using Celbridge.Workspace;

namespace Celbridge.Extensions.Services;

public class ExtensionContext : IExtensionContext
{
    private readonly IDocumentsService _documentsService;

    public ExtensionContext(IWorkspaceWrapper workspaceWrapper)
    {
        _documentsService = workspaceWrapper.WorkspaceService.DocumentsService;
    }

    public Result AddPreviewProvider(PreviewProvider previewProvider)
    {
        var addResult = _documentsService.AddPreviewProvider(previewProvider);
        if (addResult.IsFailure)
        {
            return Result.Fail($"Failed to add preview provider via extension context.")
                .WithErrors(addResult);
        }

        return Result.Ok();
    }
}
