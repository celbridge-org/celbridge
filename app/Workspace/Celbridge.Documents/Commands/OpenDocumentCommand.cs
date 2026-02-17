using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Explorer;
using Celbridge.UserInterface;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Documents.Commands;

public class OpenDocumentCommand : CommandBase, IOpenDocumentCommand
{
    public override CommandFlags CommandFlags => CommandFlags.SaveWorkspaceState;

    private readonly IStringLocalizer _stringLocalizer;
    private readonly IDialogService _dialogService;
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ILayoutManager _layoutManager;

    public ResourceKey FileResource { get; set; }

    public bool ForceReload { get; set; }

    public string Location { get; set; } = string.Empty;

    public int? TargetSectionIndex { get; set; }

    public OpenDocumentCommand(
        IStringLocalizer stringLocalizer,
        IDialogService dialogService,
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper,
        ILayoutManager layoutManager)
    {
        _stringLocalizer = stringLocalizer;
        _dialogService = dialogService;
        _commandService = commandService;
        _workspaceWrapper = workspaceWrapper;
        _layoutManager = layoutManager;
    }

    public override async Task<Result> ExecuteAsync()
    {
        // Restore console if maximized so user can see the document
        if (_layoutManager.IsConsoleMaximized)
        {
            _commandService.Execute<ISetConsoleMaximizedCommand>(command =>
            {
                command.IsMaximized = false;
            });
        }

        var documentsService = _workspaceWrapper.WorkspaceService.DocumentsService;

        var viewType = documentsService.GetDocumentViewType(FileResource);
        if (viewType == DocumentViewType.UnsupportedFormat)
        {
            var extension = Path.GetExtension(FileResource);
            var title = _stringLocalizer.GetString("Documents_UnsupportedFileFormatTitle");
            var message = _stringLocalizer.GetString("Documents_OpenDocumentFailedNotSupported", extension);
            var primaryButtonText = _stringLocalizer.GetString("ResourceTree_OpenApplication");
            var secondaryButtonText = _stringLocalizer.GetString("DialogButton_Cancel");

            var confirmResult = await _dialogService.ShowConfirmationDialogAsync(title, message, primaryButtonText, secondaryButtonText);
            if (confirmResult.IsSuccess && confirmResult.Value)
            {
                _commandService.Execute<IOpenApplicationCommand>(command =>
                {
                    command.Resource = FileResource;
                });
            }

            return Result.Fail($"This file format is not supported: '{FileResource}'");
        }

        Result openResult;
        if (TargetSectionIndex.HasValue)
        {
            // Open in the specified section
            openResult = await documentsService.OpenDocumentAtSection(FileResource, TargetSectionIndex.Value, ForceReload, Location);
        }
        else
        {
            // Open in the active section (default behavior)
            openResult = await documentsService.OpenDocument(FileResource, ForceReload, Location);
        }

        if (openResult.IsFailure)
        {
            // Alert the user that the document failed to open
            var file = Path.GetFileName(FileResource);
            var title = _stringLocalizer.GetString("Documents_OpenDocumentFailedTitle");
            var message = _stringLocalizer.GetString("Documents_OpenDocumentFailedGeneric", file);
            await _dialogService.ShowAlertDialogAsync(title, message);

            return Result.Fail($"An error occurred while attempting to open '{FileResource}'")
                .WithErrors(openResult);
        }

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //
    public static void OpenDocument(ResourceKey fileResource)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IOpenDocumentCommand>(command =>
        {
            command.FileResource = fileResource;
        });
    }

    public static void OpenDocument(ResourceKey fileResource, bool forceReload)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IOpenDocumentCommand>(command =>
        {
            command.FileResource = fileResource;
            command.ForceReload = forceReload;
        });
    }

    public static void OpenDocument(ResourceKey fileResource, bool forceReload, string location)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IOpenDocumentCommand>(command =>
        {
            command.FileResource = fileResource;
            command.ForceReload = forceReload;
            command.Location = location;
        });
    }
}
