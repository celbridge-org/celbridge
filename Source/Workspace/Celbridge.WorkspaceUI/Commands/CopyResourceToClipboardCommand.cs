using Celbridge.Commands;
using Celbridge.DataTransfer;
using Celbridge.Logging;

namespace Celbridge.WorkspaceUI.Commands;

public class CopyResourceToClipboardCommand : CommandBase, ICopyResourceToClipboardCommand
{
    public List<ResourceKey> SourceResources { get; set; } = new();
    public DataTransferMode TransferMode { get; set; }

    private readonly ILogger<CopyResourceToClipboardCommand> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IFileClipboard _fileClipboard;

    public CopyResourceToClipboardCommand(
        ILogger<CopyResourceToClipboardCommand> logger,
        IWorkspaceWrapper workspaceWrapper,
        IFileClipboard fileClipboard)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
        _fileClipboard = fileClipboard;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (SourceResources.Count == 0)
        {
            return Result.Ok();
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var files = new List<ClipboardFile>();

        foreach (var sourceResource in SourceResources)
        {
            var getResult = resourceRegistry.GetResource(sourceResource);
            if (getResult.IsFailure)
            {
                _logger.LogWarning($"Skipping resource '{sourceResource}' during clipboard copy: {getResult.DiagnosticReport}");
                continue;
            }
            var resource = getResult.Value;

            if (resource is IFileResource fileResource)
            {
                var resolveResult = resourceRegistry.ResolveResourcePath(fileResource);
                if (resolveResult.IsFailure)
                {
                    _logger.LogWarning($"Skipping resource '{sourceResource}' during clipboard copy: {resolveResult.DiagnosticReport}");
                    continue;
                }
                files.Add(new ClipboardFile(resolveResult.Value, IsFolder: false));
            }
            else if (resource is IFolderResource folderResource)
            {
                var resolveResult = resourceRegistry.ResolveResourcePath(folderResource);
                if (resolveResult.IsFailure)
                {
                    _logger.LogWarning($"Skipping resource '{sourceResource}' during clipboard copy: {resolveResult.DiagnosticReport}");
                    continue;
                }
                files.Add(new ClipboardFile(resolveResult.Value, IsFolder: true));
            }
        }

        if (files.Count == 0)
        {
            // Nothing to copy, treat it as a noop.
            return Result.Ok();
        }

        return await _fileClipboard.SetFilesAsync(files, TransferMode);
    }
}
