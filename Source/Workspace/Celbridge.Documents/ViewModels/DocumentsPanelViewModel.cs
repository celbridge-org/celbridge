using Celbridge.Commands;
using Celbridge.DataTransfer;
using Celbridge.Explorer;
using Celbridge.Messaging;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Documents.ViewModels;

public partial class DocumentsPanelViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;
    private readonly ICommandService _commandService;
    private readonly IDocumentsService _documentsService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public DocumentsPanelViewModel(
        IMessengerService messengerService,
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _messengerService = messengerService;
        _commandService = commandService;
        _workspaceWrapper = workspaceWrapper;
        _documentsService = workspaceWrapper.WorkspaceService.DocumentsService;
    }

    public void OnViewUnloaded()
    {
        _messengerService.UnregisterAll(this);
    }

    public async Task<Result<IDocumentView>> CreateDocumentView(ResourceKey fileResource, DocumentEditorId documentEditorId = default)
    {
        var createResult = await _documentsService.CreateDocumentView(fileResource, documentEditorId);
        if (createResult.IsFailure)
        {
            return Result<IDocumentView>.Fail($"Failed to create document view for file resource: '{fileResource}'")
                .WithErrors(createResult);
        }
        var documentView = createResult.Value;

        return Result<IDocumentView>.Ok(documentView);
    }

    public void OnCloseDocumentRequested(ResourceKey fileResource)
    {
        _commandService.Execute<ICloseDocumentCommand>(command =>
        {
            command.FileResource = fileResource;
        });
    }

    public void UpdatePendingSaveCount(int pendingSaveCount)
    {
        // Notify the StatusPanelViewModel about the current number of pending document saves.
        var message = new PendingDocumentSaveMessage(pendingSaveCount);
        _messengerService.Send(message);
    }

    public void OnDocumentLayoutChanged()
    {
        // Notify that the document layout has changed (documents opened, closed, or moved).
        // Receivers should query the service for current state.
        var message = new DocumentLayoutChangedMessage();
        _messengerService.Send(message);
    }

    public void OnActiveDocumentChanged(ResourceKey documentResource)
    {
        // Notify the DocumentsService about the currently active document.
        var message = new ActiveDocumentChangedMessage(documentResource);
        _messengerService.Send(message);
    }

    public void OnSectionRatiosChanged(List<double> ratios)
    {
        // Notify the DocumentsService about the section ratios change.
        var message = new SectionRatiosChangedMessage(ratios);
        _messengerService.Send(message);
    }

    public async Task StoreDocumentEditorState(ResourceKey fileResource, string? state)
    {
        await _documentsService.StoreDocumentEditorState(fileResource, state);
    }

    public ResourceKey GetResourceKey(IFileResource fileResource)
    {
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        return resourceRegistry.GetResourceKey(fileResource);
    }

    public Result<string> ResolveResourcePath(ResourceKey fileResource)
    {
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        return resourceRegistry.ResolveResourcePath(fileResource);
    }

    public void SelectFileForTab(ResourceKey fileResource)
    {
        _commandService.Execute<ISelectResourceCommand>(command =>
        {
            command.Resource = fileResource;
            command.ShowExplorerPanel = true;
        });
    }

    public void CopyResourceKeyForTab(ResourceKey fileResource)
    {
        _commandService.Execute<ICopyTextToClipboardCommand>(command =>
        {
            command.Text = fileResource.ToString();
            command.TransferMode = DataTransferMode.Copy;
        });
    }

    public void CopyFilePathForTab(string filePath)
    {
        _commandService.Execute<ICopyTextToClipboardCommand>(command =>
        {
            command.Text = filePath;
            command.TransferMode = DataTransferMode.Copy;
        });
    }

    public void OpenFileExplorerForTab(ResourceKey fileResource)
    {
        _commandService.Execute<IOpenFileManagerCommand>(command =>
        {
            command.Resource = fileResource;
        });
    }

    public void OpenApplicationForTab(ResourceKey fileResource)
    {
        _commandService.Execute<IOpenApplicationCommand>(command =>
        {
            command.Resource = fileResource;
        });
    }

    public record class EditorDisplayInfo(DocumentEditorId EditorId, string EditorDisplayName);

    public EditorDisplayInfo? ResolveEditorDisplayInfo(ResourceKey fileResource, DocumentEditorId documentEditorId)
    {
        var extension = Path.GetExtension(fileResource.ToString()).ToLowerInvariant();
        var editorRegistry = _documentsService.DocumentEditorRegistry;
        var factories = editorRegistry.GetFactoriesForFileExtension(extension);

        if (!documentEditorId.IsEmpty)
        {
            var factoryResult = editorRegistry.GetFactoryById(documentEditorId);
            if (factoryResult.IsSuccess)
            {
                var displayName = factories.Count >= 2 ? factoryResult.Value.DisplayName : string.Empty;
                return new EditorDisplayInfo(factoryResult.Value.EditorId, displayName);
            }
        }

        if (factories.Count >= 2)
        {
            var resolveResult = ResolveResourcePath(fileResource);
            var filePath = resolveResult.IsSuccess ? resolveResult.Value : string.Empty;

            foreach (var factory in factories)
            {
                if (factory.CanHandleResource(fileResource, filePath))
                {
                    return new EditorDisplayInfo(factory.EditorId, factory.DisplayName);
                }
            }
        }
        else if (factories.Count == 1)
        {
            return new EditorDisplayInfo(factories[0].EditorId, string.Empty);
        }

        return null;
    }

    public record class EditorChoiceInfo(
        IReadOnlyList<IDocumentEditorFactory> Factories,
        List<string> DisplayNames,
        int DefaultIndex);

    public EditorChoiceInfo? GetChoicesForFileExtension(string extension, DocumentEditorId currentEditorId)
    {
        var editorRegistry = _documentsService.DocumentEditorRegistry;
        var factories = editorRegistry.GetFactoriesForFileExtension(extension);

        if (factories.Count < 2)
        {
            return null;
        }

        int defaultIndex = 0;
        for (int i = 0; i < factories.Count; i++)
        {
            if (factories[i].EditorId == currentEditorId)
            {
                defaultIndex = i;
                break;
            }
        }

        var displayNames = factories.Select(factory => factory.DisplayName).ToList();
        return new EditorChoiceInfo(factories, displayNames, defaultIndex);
    }

    public void StoreEditorPreference(string extension, DocumentEditorId editorId)
    {
        _commandService.Execute<ISetEditorPreferenceCommand>(command =>
        {
            command.Extension = extension;
            command.EditorId = editorId;
        });
    }

}
