using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Documents.Commands;

public class OpenDocumentCommand : CommandBase, IOpenDocumentCommand
{
    public override CommandFlags CommandFlags => CommandFlags.SaveWorkspaceState;

    private readonly IStringLocalizer _stringLocalizer;
    private readonly IDialogService _dialogService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey FileResource { get; set; }

    public bool ForceReload { get; set; }

    public string Location { get; set; } = string.Empty;

    public OpenDocumentCommand(
        IStringLocalizer stringLocalizer,
        IDialogService dialogService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _stringLocalizer = stringLocalizer;
        _dialogService = dialogService;
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var documentsService = _workspaceWrapper.WorkspaceService.DocumentsService;

        var viewType = documentsService.GetDocumentViewType(FileResource);
        if (viewType == DocumentViewType.UnsupportedFormat)
        {
            // Alert the user that the file format is not supported
            var file = Path.GetFileName(FileResource);
            var title = _stringLocalizer.GetString("Documents_OpenDocumentFailedTitle");
            var message = _stringLocalizer.GetString("Documents_OpenDocumentFailedNotSupported", file);
            await _dialogService.ShowAlertDialogAsync(title, message);

            return Result.Fail($"This file format is not supported: '{FileResource}'");
        }

        var openResult = await documentsService.OpenDocument(FileResource, ForceReload, Location);
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
