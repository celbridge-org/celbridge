using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Logging;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Documents.Commands;

public class ApplyEditsCommand : CommandBase, IApplyEditsCommand
{
    private readonly ILogger<ApplyEditsCommand> _logger;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IDialogService _dialogService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ICommandService _commandService;

    public List<DocumentEdit> Edits { get; set; } = new();

    public ApplyEditsCommand(
        ILogger<ApplyEditsCommand> logger,
        IStringLocalizer stringLocalizer,
        IDialogService dialogService,
        IWorkspaceWrapper workspaceWrapper,
        ICommandService commandService)
    {
        _logger = logger;
        _stringLocalizer = stringLocalizer;
        _dialogService = dialogService;
        _workspaceWrapper = workspaceWrapper;
        _commandService = commandService;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (Edits.Count == 0)
        {
            return Result.Ok();
        }

        var documentsService = _workspaceWrapper.WorkspaceService.DocumentsService;
        var documentsPanel = _workspaceWrapper.WorkspaceService.DocumentsPanel;
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var failedResources = new List<ResourceKey>();

        foreach (var documentEdit in Edits)
        {
            var resource = documentEdit.Resource;

            // Check if document is already open
            var documentView = documentsPanel.GetDocumentView(resource);

            // If not open, open it first
            if (documentView is null)
            {
                var openResult = await documentsService.OpenDocument(resource, forceReload: false, location: string.Empty);
                if (openResult.IsFailure)
                {
                    _logger.LogWarning($"Failed to open document for applying edits: {resource}");
                    failedResources.Add(resource);
                    continue;
                }

                // Get the document view after opening
                documentView = documentsPanel.GetDocumentView(resource);
                if (documentView is null)
                {
                    _logger.LogWarning($"Document view not found after opening: {resource}");
                    failedResources.Add(resource);
                    continue;
                }
            }

            // Apply the edits to the document
            var applyResult = await documentView.ApplyEditsAsync(documentEdit.Edits);
            if (applyResult.IsFailure)
            {
                _logger.LogWarning($"Failed to apply edits to document: {resource}");
                failedResources.Add(resource);
            }
        }

        if (failedResources.Count > 0)
        {
            // Log the error with all failed files
            var errorMessage = $"Failed to apply edits to the following documents: {string.Join(", ", failedResources)}";
            _logger.LogError(errorMessage);

            // Show localized alert to the user
            var alertTitle = _stringLocalizer.GetString("Documents_ApplyEditsFailedTitle");
            string alertMessage;
            if (failedResources.Count == 1)
            {
                var failedFile = failedResources[0].ToString();
                alertMessage = _stringLocalizer.GetString("Documents_ApplyEditsFailedSingle", failedFile);
            }
            else
            {
                alertMessage = _stringLocalizer.GetString("Documents_ApplyEditsFailedMultiple", failedResources.Count);
            }

            // Fire-and-forget to avoid blocking
            _ = _dialogService.ShowAlertDialogAsync(alertTitle, alertMessage);

            return Result.Fail(errorMessage);
        }

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void ApplyEdits(List<DocumentEdit> edits)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IApplyEditsCommand>(command =>
        {
            command.Edits = edits;
        });
    }
}
