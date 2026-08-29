using Celbridge.Commands;
using Celbridge.DataTransfer;
using Celbridge.Documents.Services;
using Celbridge.Explorer;
using Celbridge.Messaging;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel;

namespace Celbridge.Documents.ViewModels;

public partial class WorkspacePanelViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;
    private readonly ICommandService _commandService;
    private readonly IDocumentsService _documentsService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ILayoutService _layoutService;

    public WorkspacePanelViewModel(
        IMessengerService messengerService,
        ICommandService commandService,
        ILayoutService layoutService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _messengerService = messengerService;
        _commandService = commandService;
        _layoutService = layoutService;
        _workspaceWrapper = workspaceWrapper;
        _documentsService = workspaceWrapper.WorkspaceService.DocumentsService;

        // The Reset Layout command and the splitter double-click both write the area size through the
        // settings facade, so the live layout follows the stored value.
        _workspaceWrapper.WorkspaceService.BindableWorkspaceSettings.PropertyChanged += OnWorkspaceSettingsChanged;
    }

    private void OnWorkspaceSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IBindableWorkspaceSettings.UtilityPanelWidth))
        {
            AreaSizeChanged?.Invoke(WorkspaceArea.Utility);
        }
        else if (e.PropertyName == nameof(IBindableWorkspaceSettings.BottomAreaHeight))
        {
            AreaSizeChanged?.Invoke(WorkspaceArea.Bottom);
        }
        else if (e.PropertyName == nameof(IBindableWorkspaceSettings.SideAreaWidth))
        {
            AreaSizeChanged?.Invoke(WorkspaceArea.Side);
        }
    }

    public void OnViewUnloaded()
    {
        // The workspace is torn down before its view leaves the visual tree, so on a project close the
        // settings object this subscribed to has already gone with it.
        if (_workspaceWrapper.HasWorkspaceService)
        {
            _workspaceWrapper.WorkspaceService.BindableWorkspaceSettings.PropertyChanged -= OnWorkspaceSettingsChanged;
        }

        _messengerService.UnregisterAll(this);
    }

    public async Task<Result<IDocumentView>> CreateDocumentView(ResourceKey fileResource, EditorId editorId = default)
    {
        var createResult = await _documentsService.CreateDocumentView(fileResource, editorId);
        if (createResult.IsFailure)
        {
            return Result<IDocumentView>.Fail($"Failed to create document view for file resource: '{fileResource}'")
                .WithErrors(createResult);
        }

        return createResult.Value.OkResult<IDocumentView>();
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

    /// <summary>
    /// Raised when a stored area size changes.
    /// </summary>
    public event Action<WorkspaceArea>? AreaSizeChanged;

    public void OnAreaLayoutChanged(DocumentArea area, bool isSplit, double splitRatio)
    {
        // Notify the DocumentsService about the area split change.
        var message = new AreaLayoutChangedMessage(area, isSplit, splitRatio);
        _messengerService.Send(message);
    }

    public float GetAreaSize(WorkspaceArea area)
    {
        var settings = _workspaceWrapper.WorkspaceService.BindableWorkspaceSettings;

        switch (area)
        {
            case WorkspaceArea.Utility:
                return settings.UtilityPanelWidth;

            case WorkspaceArea.Bottom:
                return settings.BottomAreaHeight;

            case WorkspaceArea.Side:
                return settings.SideAreaWidth;

            default:
                return 0;
        }
    }

    public void StoreAreaSize(WorkspaceArea area, float size)
    {
        var settings = _workspaceWrapper.WorkspaceService.BindableWorkspaceSettings;

        switch (area)
        {
            case WorkspaceArea.Utility:
                settings.UtilityPanelWidth = size;
                break;

            case WorkspaceArea.Bottom:
                settings.BottomAreaHeight = size;
                break;

            case WorkspaceArea.Side:
                settings.SideAreaWidth = size;
                break;
        }
    }

    public void ResetAreaSize(WorkspaceArea area)
    {
        _commandService.Execute<IResetAreaSizeCommand>(command =>
        {
            command.Area = area;
        });
    }

    public bool IsUtilityPanelVisible => _layoutService.IsAreaVisible(WorkspaceArea.Utility);

    public BottomAreaAlignment BottomAreaAlignment => _layoutService.BottomAreaAlignment;

    public bool IsAreaVisible(DocumentArea area)
    {
        if (!area.IsCollapsible())
        {
            return true;
        }

        return _layoutService.IsAreaVisible(area.GetWorkspaceArea());
    }

    public void SetAreaVisible(DocumentArea area, bool isVisible)
    {
        if (!area.IsCollapsible())
        {
            return;
        }

        _commandService.Execute<ISetAreaVisibilityCommand>(command =>
        {
            command.Area = area.GetWorkspaceArea();
            command.IsVisible = isVisible;
        });
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
            command.Text = fileResource.Path;
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

    public record class EditorDisplayInfo(EditorId EditorId, string EditorDisplayName);

    // Looks up the display name for the supplied editor id. Returns an empty label when only one
    // factory claims the extension (no disambiguation needed), and null when the editor id is empty
    // or unregistered.
    public EditorDisplayInfo? ResolveEditorDisplayInfo(ResourceKey fileResource, EditorId editorId)
    {
        if (editorId.IsEmpty)
        {
            return null;
        }

        var editorRegistry = _documentsService.DocumentEditorRegistry;
        var factoryResult = editorRegistry.GetFactoryById(editorId);
        if (factoryResult.IsFailure)
        {
            return null;
        }

        var extension = Path.GetExtension(fileResource.ToString()).ToLowerInvariant();
        var factoriesForExtension = editorRegistry.GetFactoriesForExtension(extension);
        var displayName = factoriesForExtension.Count >= 2 ? factoryResult.Value.DisplayName : string.Empty;
        return new EditorDisplayInfo(factoryResult.Value.EditorId, displayName);
    }

    public EditorPickList? GetEditorPickList(ResourceKey fileResource, EditorId currentEditorId)
    {
        return _documentsService.GetEditorPickList(fileResource, currentEditorId);
    }

    public async Task<Result> SetPreferredEditorAsync(ResourceKey fileResource, EditorId editorId)
    {
        return await _documentsService.SetPreferredEditorAsync(fileResource, editorId);
    }

    public record class UtilityTabInfo(string IconName, string Title, string Tooltip);

    // Looks up the title the named editor gives its document tabs. Returns an empty title when the editor
    // names its tabs after their file, which is every editor bar those bound to one fixed file.
    public string ResolveEditorTabTitle(EditorId editorId)
    {
        if (editorId.IsEmpty)
        {
            return string.Empty;
        }

        var factoryResult = _documentsService.DocumentEditorRegistry.GetFactoryById(editorId);
        if (factoryResult.IsFailure)
        {
            return string.Empty;
        }

        return factoryResult.Value.DocumentTabTitle;
    }

    // Resolves how a utility document presents as a tab, or null when the editor is not a utility.
    public UtilityTabInfo? ResolveUtilityTabInfo(EditorId editorId)
    {
        if (editorId.IsEmpty)
        {
            return null;
        }

        var editorRegistry = _documentsService.DocumentEditorRegistry;
        var factoryResult = editorRegistry.GetFactoryById(editorId);
        if (factoryResult.IsFailure)
        {
            return null;
        }

        if (factoryResult.Value is not CustomDocumentViewFactory { IsUtility: true } utilityFactory)
        {
            return null;
        }

        var descriptor = utilityFactory.Contribution.UtilityDescriptor;
        Guard.IsNotNull(descriptor);

        var iconIconName = descriptor.Icon;

        return new UtilityTabInfo(iconIconName, utilityFactory.DisplayName, utilityFactory.Description);
    }
}
