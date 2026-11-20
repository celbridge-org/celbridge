using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.Documents.Commands;

public class ApplyDocumentEditsCommand : CommandBase, IApplyDocumentEditsCommand
{
    private sealed record OpenDocumentEdit(IDocumentView View, FileRangeEdit FileEdit);

    private sealed record ClosedFileEdit(ResourceKey Resource, string NewText);

    private readonly IWorkspaceWrapper _workspaceWrapper;

    [RedactedInLogs]
    public List<FileRangeEdit> Edits { get; set; } = new();

    public DocumentEditsResult ResultValue { get; private set; } = new(Array.Empty<ResourceKey>());

    public ApplyDocumentEditsCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (!_workspaceWrapper.IsWorkspaceLoaded)
        {
            return Result.Fail("Workspace is not loaded");
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var documentsPanel = workspaceService.DocumentsPanel;
        var fileSystem = workspaceService.ResourceService.FileSystem;

        // Check every file before changing any. An open document changes in its editor. A closed file must be a
        // writable file, and each edit's range must fall inside the file's text.
        var openDocumentEdits = new List<OpenDocumentEdit>();
        var closedFileEdits = new List<ClosedFileEdit>();

        foreach (var fileEdit in Edits)
        {
            var resource = fileEdit.Resource;

            var documentView = documentsPanel.GetDocumentView(resource);
            if (documentView is not null)
            {
                openDocumentEdits.Add(new OpenDocumentEdit(documentView, fileEdit));
                continue;
            }

            var infoResult = await fileSystem.GetInfoAsync(resource);
            if (infoResult.IsFailure
                || infoResult.Value.Kind != StorageItemKind.File)
            {
                return Result.Fail($"File not found: '{resource}'")
                    .WithErrors(infoResult);
            }

            var info = infoResult.Value;
            if (info.Attributes.HasFlag(FileSystemAttributes.ReadOnly))
            {
                return Result.Fail($"File is read-only: '{resource}'");
            }

            var readResult = await fileSystem.ReadAllTextAsync(resource);
            if (readResult.IsFailure)
            {
                return Result.Fail($"Failed to read file: '{resource}'")
                    .WithErrors(readResult);
            }
            var text = readResult.Value;

            var applyResult = RangeEditHelper.ApplyEdits(text, fileEdit.Edits);
            if (applyResult.IsFailure)
            {
                return Result.Fail($"Failed to edit file: '{resource}'")
                    .WithErrors(applyResult);
            }
            var newText = applyResult.Value;

            closedFileEdits.Add(new ClosedFileEdit(resource, newText));
        }

        // An editor can still refuse its part, so the open documents change first. A refusal then leaves every
        // closed file unchanged.
        foreach (var openDocumentEdit in openDocumentEdits)
        {
            var resource = openDocumentEdit.FileEdit.Resource;

            var editResult = await openDocumentEdit.View.ApplyEdits(openDocumentEdit.FileEdit.Edits);
            if (editResult.IsFailure)
            {
                return Result.Fail($"Failed to edit the open document: '{resource}'")
                    .WithErrors(editResult);
            }
        }

        foreach (var closedFileEdit in closedFileEdits)
        {
            var resource = closedFileEdit.Resource;

            var writeResult = await fileSystem.WriteAllTextAsync(resource, closedFileEdit.NewText);
            if (writeResult.IsFailure)
            {
                return Result.Fail($"Failed to write file: '{resource}'")
                    .WithErrors(writeResult);
            }
        }

        var changedFiles = Edits
            .Select(fileEdit => fileEdit.Resource)
            .ToList();
        ResultValue = new DocumentEditsResult(changedFiles);

        return Result.Ok();
    }
}
