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
    private readonly IMessengerService _messengerService;
    private readonly ILayoutService _layoutService;
    private readonly IWindowModeService _windowModeService;

    public ResourceKey FileResource { get; set; }

    public bool ForceReload { get; set; }

    public string Location { get; set; } = string.Empty;

    public DocumentSection? TargetSection { get; set; }

    public int? TargetTabIndex { get; set; }

    public bool Activate { get; set; } = true;

    public EditorId EditorId { get; set; }

    public string? EditorStateJson { get; set; }

    public OpenDocumentOutcome ResultValue { get; private set; } = OpenDocumentOutcome.Opened;

    public OpenDocumentCommand(
        IStringLocalizer stringLocalizer,
        IDialogService dialogService,
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper,
        IMessengerService messengerService,
        ILayoutService layoutService,
        IWindowModeService windowModeService)
    {
        _stringLocalizer = stringLocalizer;
        _dialogService = dialogService;
        _commandService = commandService;
        _workspaceWrapper = workspaceWrapper;
        _messengerService = messengerService;
        _layoutService = layoutService;
        _windowModeService = windowModeService;
    }

    public override async Task<Result> ExecuteAsync()
    {
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

        // A caller naming a section but no index is asking for the section, not for a position, so the
        // document joins the end of that section's tab row rather than displacing the tab at index 0.
        var tabOrder = TargetTabIndex ?? DocumentAddress.AppendTabOrder;

        DocumentAddress? address = TargetSection.HasValue
            ? new DocumentAddress(WindowIndex: 0, Section: TargetSection.Value, TabOrder: tabOrder)
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

        if (ResultValue == OpenDocumentOutcome.Opened)
        {
            ShowTargetSectionArea();
            SelectTabIfSectionHasNone();
        }

        // Flash the tab to draw the eye to it, but only when the document was actually opened (not a
        // cancelled open) and brought to the front.
        if (Activate
            && ResultValue == OpenDocumentOutcome.Opened)
        {
            _messengerService.Send(new FlashDocumentMessage(FileResource));
        }

        return Result.Ok();
    }

    private void ShowTargetSectionArea()
    {
        if (!TargetSection.HasValue)
        {
            return;
        }

        // An activating open presents its own area. Showing an area ends Focus or Presentation, which a
        // background open leaves alone.
        if (Activate
            || _windowModeService.LayoutMode != LayoutMode.Default)
        {
            return;
        }

        var documentArea = TargetSection.Value.GetArea();
        if (!documentArea.IsCollapsible())
        {
            return;
        }

        var workspaceArea = documentArea.GetWorkspaceArea();
        _layoutService.SetAreaVisibility(workspaceArea, true);
    }

    // The tab strip never selects a tab added to it, so a background open into an empty section would leave
    // its one tab unselected over an empty area. The document is selected there without becoming active. An
    // activating open has selected it already.
    private void SelectTabIfSectionHasNone()
    {
        if (Activate)
        {
            return;
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;

        // Read back rather than taken from TargetSection, since a document can end up in another section.
        var openDocument = workspaceService.DocumentsService.FindOpenDocument(FileResource);
        if (openDocument is null)
        {
            return;
        }

        var section = openDocument.Address.Section;
        var documentsPanel = workspaceService.DocumentsPanel;
        if (!documentsPanel.GetSelectedDocument(section).IsEmpty)
        {
            return;
        }

        documentsPanel.SetSelectedDocument(section, FileResource);
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
