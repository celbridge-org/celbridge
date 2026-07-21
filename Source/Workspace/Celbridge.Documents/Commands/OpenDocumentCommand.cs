using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Explorer;
using Celbridge.Messaging;
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
    private readonly ILayoutService _layoutService;
    private readonly IMessengerService _messengerService;

    public ResourceKey FileResource { get; set; }

    public bool ForceReload { get; set; }

    public string Location { get; set; } = string.Empty;

    public int? TargetSectionIndex { get; set; }

    public int? TargetTabIndex { get; set; }

    public bool Activate { get; set; } = true;

    public EditorInstanceId EditorId { get; set; }

    public string? EditorStateJson { get; set; }

    public OpenDocumentOutcome ResultValue { get; private set; } = OpenDocumentOutcome.Opened;

    public OpenDocumentCommand(
        IStringLocalizer stringLocalizer,
        IDialogService dialogService,
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper,
        ILayoutService layoutService,
        IMessengerService messengerService)
    {
        _stringLocalizer = stringLocalizer;
        _dialogService = dialogService;
        _commandService = commandService;
        _workspaceWrapper = workspaceWrapper;
        _layoutService = layoutService;
        _messengerService = messengerService;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (_layoutService.IsConsoleMaximized)
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

            var confirmationOptions = new ConfirmationDialogOptions
            {
                PrimaryButtonText = primaryButtonText,
                SecondaryButtonText = secondaryButtonText
            };
            var confirmResult = await _dialogService.ShowConfirmationDialogAsync(title, message, confirmationOptions);
            if (confirmResult.IsSuccess && confirmResult.Value)
            {
                _commandService.Execute<IOpenApplicationCommand>(command =>
                {
                    command.Resource = FileResource;
                });
            }

            return Result.Fail($"This file format is not supported: '{FileResource}'");
        }

        DocumentAddress? address = TargetSectionIndex.HasValue
            ? new DocumentAddress(WindowIndex: 0, SectionIndex: TargetSectionIndex.Value, TabOrder: TargetTabIndex ?? 0)
            : null;

        var options = new OpenDocumentOptions(address, ForceReload, Location, Activate, EditorId, EditorStateJson);

        var openResult = await documentsService.OpenDocument(FileResource, options);

        if (openResult.IsFailure)
        {
            var title = _stringLocalizer.GetString("Documents_OpenDocumentFailedTitle");
            var message = _stringLocalizer.GetString("Documents_OpenDocumentFailedGeneric", FileResource.Path);
            await _dialogService.ShowAlertDialogAsync(title, message);

            return Result.Fail($"An error occurred while attempting to open '{FileResource}'")
                .WithErrors(openResult);
        }

        ResultValue = openResult.Value;

        // Flash the tab to draw the eye to it, but only when the document was actually opened (not a
        // cancelled open) and brought to the front.
        if (Activate
            && ResultValue == OpenDocumentOutcome.Opened)
        {
            _messengerService.Send(new FlashDocumentMessage(FileResource));
        }

        return Result.Ok();
    }

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
