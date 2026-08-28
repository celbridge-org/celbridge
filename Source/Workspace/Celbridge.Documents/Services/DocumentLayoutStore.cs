using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Workspace;

namespace Celbridge.Documents.Services;

/// <summary>
/// Owns the workspace-settings round trip for the documents panel: which tabs are open, in which
/// sections, and their saved editor state.
/// </summary>
public class DocumentLayoutStore
{
    private const string DocumentLayoutKey = "DocumentLayout";
    private const string ActiveDocumentKey = "ActiveDocument";
    private const string AreaLayoutKey = "AreaLayout";
    private const string DocumentEditorStatesKey = "DocumentEditorStates";

    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ICommandService _commandService;
    private readonly ILogger<DocumentLayoutStore> _logger;

    private IDocumentsPanel DocumentsPanel => _workspaceWrapper.WorkspaceService.DocumentsPanel;

    public DocumentLayoutStore(
        IWorkspaceWrapper workspaceWrapper,
        ICommandService commandService,
        ILogger<DocumentLayoutStore> logger)
    {
        _workspaceWrapper = workspaceWrapper;
        _commandService = commandService;
        _logger = logger;
    }

    /// <summary>
    /// Serialization DTO for a single open document tab.
    /// </summary>
    public record StoredDocumentAddress(string Resource, int WindowIndex, string Section, int TabOrder);

    /// <summary>
    /// Serialization DTO for one area's split position: the share taken by its primary section. Whether
    /// the area is split is not stored, because it follows from whether any document restores into its
    /// secondary section.
    /// </summary>
    public record StoredAreaLayout(double SplitRatio);

    public async Task StoreDocumentLayoutAsync()
    {
        var propertyBag = _workspaceWrapper.WorkspaceService.WorkspaceSettings.PropertyBag;
        Guard.IsNotNull(propertyBag);

        var storedAddresses = DocumentsPanel.GetOpenDocuments()
            .Select(document => new StoredDocumentAddress(
                document.FileResource.ToString(),
                document.Address.WindowIndex,
                document.Address.Section.ToToken(),
                document.Address.TabOrder))
            .OrderBy(address => address.WindowIndex)
            .ThenBy(address => address.Section)
            .ThenBy(address => address.TabOrder)
            .ToList();

        await propertyBag.SetPropertyAsync(DocumentLayoutKey, storedAddresses);
    }

    public async Task StoreActiveDocumentAsync()
    {
        var propertyBag = _workspaceWrapper.WorkspaceService.WorkspaceSettings.PropertyBag;
        Guard.IsNotNull(propertyBag);

        // Read the panel's active document directly. The gated IDocumentsService.ActiveDocument
        // reports Empty until the workspace page finishes loading, and this runs before that.
        var activeDocument = DocumentsPanel.ActiveDocument;
        await propertyBag.SetPropertyAsync(ActiveDocumentKey, activeDocument.ToString());
    }

    public async Task StoreAreaLayoutAsync()
    {
        var propertyBag = _workspaceWrapper.WorkspaceService.WorkspaceSettings.PropertyBag;
        Guard.IsNotNull(propertyBag);

        var areaLayout = new Dictionary<string, StoredAreaLayout>();
        foreach (var area in DocumentLayoutHelper.AllAreas)
        {
            areaLayout[area.GetWorkspaceArea().ToToken()] = new StoredAreaLayout(DocumentsPanel.GetAreaSplitRatio(area));
        }

        await propertyBag.SetPropertyAsync(AreaLayoutKey, areaLayout);
    }

    public async Task StoreDocumentEditorStatesAsync()
    {
        var propertyBag = _workspaceWrapper.WorkspaceService.WorkspaceSettings.PropertyBag;
        Guard.IsNotNull(propertyBag);

        // Start with existing saved states so that editors that aren't ready yet
        // (e.g., WebView still loading) preserve their previously saved state.
        var editorStates = await propertyBag.GetPropertyAsync<Dictionary<string, string>>(DocumentEditorStatesKey)
            ?? new Dictionary<string, string>();

        var openDocumentKeys = new HashSet<string>();

        foreach (var document in DocumentsPanel.GetOpenDocuments())
        {
            var resourceKey = document.FileResource.ToString();
            openDocumentKeys.Add(resourceKey);

            var documentView = DocumentsPanel.GetDocumentView(document.FileResource);
            if (documentView is null)
            {
                continue;
            }

            try
            {
                // A null or empty return means the editor is still initialising or has no state to
                // contribute. Keep the previously saved state rather than overwriting it.
                var state = await documentView.TrySaveEditorStateAsync();
                if (!string.IsNullOrEmpty(state))
                {
                    editorStates[resourceKey] = state;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, $"Could not save editor state for '{resourceKey}'");
            }
        }

        // Remove entries for documents that are no longer open
        var staleKeys = editorStates.Keys.Where(key => !openDocumentKeys.Contains(key)).ToList();
        foreach (var staleKey in staleKeys)
        {
            editorStates.Remove(staleKey);
        }

        await propertyBag.SetPropertyAsync(DocumentEditorStatesKey, editorStates);
    }

    public async Task StoreDocumentEditorStateAsync(ResourceKey fileResource, string? state)
    {
        var propertyBag = _workspaceWrapper.WorkspaceService.WorkspaceSettings.PropertyBag;
        Guard.IsNotNull(propertyBag);

        try
        {
            var editorStates = await propertyBag.GetPropertyAsync<Dictionary<string, string>>(DocumentEditorStatesKey)
                ?? new Dictionary<string, string>();

            var resourceKey = fileResource.ToString();
            if (!string.IsNullOrEmpty(state))
            {
                editorStates[resourceKey] = state;
            }
            else
            {
                editorStates.Remove(resourceKey);
            }

            await propertyBag.SetPropertyAsync(DocumentEditorStatesKey, editorStates);
        }
        catch (Exception ex)
        {
            // Best-effort persistence: losing editor state is a user convenience, not data loss.
            _logger.LogDebug(ex, $"Failed to store editor state for '{fileResource}'");
        }
    }

    public async Task RestorePanelStateAsync()
    {
        var storedLayout = await LoadStoredLayoutAsync();

        // The split position is applied before any tabs are opened, so an area that splits while restoring
        // opens at the position the user left it at.
        if (storedLayout.AreaLayout is not null)
        {
            foreach (var area in DocumentLayoutHelper.AllAreas)
            {
                if (!storedLayout.AreaLayout.TryGetValue(area.GetWorkspaceArea().ToToken(), out var areaLayout))
                {
                    continue;
                }

                DocumentsPanel.SetAreaSplitRatio(area, areaLayout.SplitRatio);
            }
        }

        if (storedLayout.Addresses is null
            || storedLayout.Addresses.Count == 0)
        {
            await OpenDefaultReadmeAsync();
            return;
        }

        await RestoreDocumentsAsync(storedLayout.Addresses, storedLayout.EditorStates);

        // A document whose file has gone since the last session leaves the section it was restoring into
        // empty, so fold away any split that ended up with nothing in it.
        foreach (var area in DocumentLayoutHelper.AllAreas)
        {
            DocumentsPanel.ReconcileAreaSplit(area);
        }

        await RestoreActiveDocumentAsync();
    }

    private record StoredLayout(
        Dictionary<string, StoredAreaLayout>? AreaLayout,
        List<StoredDocumentAddress>? Addresses,
        Dictionary<string, string>? EditorStates);

    private async Task<StoredLayout> LoadStoredLayoutAsync()
    {
        var propertyBag = _workspaceWrapper.WorkspaceService.WorkspaceSettings.PropertyBag;
        Guard.IsNotNull(propertyBag);

        Dictionary<string, StoredAreaLayout>? areaLayout = null;
        try
        {
            areaLayout = await propertyBag.GetPropertyAsync<Dictionary<string, StoredAreaLayout>>(AreaLayoutKey);
        }
        catch
        {
            _logger.LogDebug("Could not load the area layout - starting fresh");
        }

        // Try to load document addresses - if format is incompatible, just start fresh
        List<StoredDocumentAddress>? storedAddresses = null;
        try
        {
            storedAddresses = await propertyBag.GetPropertyAsync<List<StoredDocumentAddress>>(DocumentLayoutKey);
        }
        catch
        {
            // Old format or corrupted data - ignore and start fresh
            _logger.LogDebug("Could not load document addresses - starting fresh");
        }

        // Load saved editor states for restoration after documents are opened
        Dictionary<string, string>? editorStates = null;
        try
        {
            editorStates = await propertyBag.GetPropertyAsync<Dictionary<string, string>>(DocumentEditorStatesKey);
        }
        catch
        {
            _logger.LogDebug("Could not load editor states - starting fresh");
        }

        return new StoredLayout(areaLayout, storedAddresses, editorStates);
    }

    private async Task RestoreDocumentsAsync(
        IReadOnlyList<StoredDocumentAddress> storedAddresses,
        IReadOnlyDictionary<string, string>? editorStates)
    {
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        foreach (var stored in storedAddresses)
        {
            if (!ResourceKey.TryCreate(stored.Resource, out var fileResource))
            {
                _logger.LogWarning($"Invalid resource key '{stored.Resource}' found in previously open documents");
                continue;
            }

            // An unrecognised section name comes from layout data written by a different section set.
            // MainLeft always exists, so it is the safe landing place.
            if (!DocumentSectionTokens.TryParse(stored.Section, out var storedSection))
            {
                storedSection = DocumentSection.MainLeft;
            }

            var targetSection = ResolveRestoreSection(storedSection);

            // Project resources use the registry fast path. Virtual-root keys (utils:, temp:, logs:) are
            // never in the registry, so the ResolveResourcePath and GetInfoAsync checks below validate
            // their existence instead.
            if (fileResource.Root == ResourceKey.DefaultRoot)
            {
                var getResourceResult = resourceRegistry.GetResource(fileResource);
                if (getResourceResult.IsFailure)
                {
                    _logger.LogWarning(getResourceResult, $"Failed to open document because '{fileResource}' resource does not exist.");
                    continue;
                }
            }

            var resolveResult = resourceRegistry.ResolveResourcePath(fileResource);
            if (resolveResult.IsFailure)
            {
                _logger.LogWarning(resolveResult, $"Failed to resolve path for resource: '{fileResource}'");
                continue;
            }

            // A stored utils: entry is one of the rail's own workspace items, both of which are registered
            // before this runs. A workspace-scoped one was docked last session, so its live view is
            // reparented into the saved tab position rather than a second view being created; an open-scoped
            // one opens as an ordinary document below, with the editor its rail item names.
            var railEditorId = EditorId.Empty;
            if (fileResource.Root == ProjectConstants.UtilsFolder)
            {
                var utilityService = _workspaceWrapper.WorkspaceService.UtilityService;

                var railItem = utilityService.FindRailItem(fileResource);
                if (railItem is null)
                {
                    _logger.LogWarning($"Cannot restore '{fileResource}': no rail item presents it.");
                    continue;
                }

                if (railItem.PanelView is not null)
                {
                    var utilityAddress = new DocumentAddress(stored.WindowIndex, targetSection, stored.TabOrder);

                    var restoreResult = await utilityService.RestoreDockedUtility(fileResource, utilityAddress);
                    if (restoreResult.IsFailure)
                    {
                        _logger.LogWarning(restoreResult, $"Failed to restore docked utility '{fileResource}'");
                    }
                    continue;
                }

                Guard.IsNotNull(railItem.Resource);
                railEditorId = railItem.Resource.Editor;
            }

            var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;
            var infoResult = await resourceFileSystem.GetInfoAsync(fileResource);
            if (infoResult.IsFailure
                || infoResult.Value.Kind != StorageItemKind.File)
            {
                _logger.LogWarning($"Cannot access file for resource: '{fileResource}'");
                continue;
            }

            var address = new DocumentAddress(stored.WindowIndex, targetSection, stored.TabOrder);

            // An empty editor id makes the factory resolve the editor from the sidecar (or the
            // per-extension default) rather than from persisted layout state. A rail item's document names
            // its editor instead: a utils: file has no sidecar and its extension is claimed by no editor.
            string? editorStateJson = null;
            editorStates?.TryGetValue(fileResource.ToString(), out editorStateJson);

            var restoreOptions = new OpenDocumentOptions(
                Address: address,
                Activate: false,
                EditorId: railEditorId,
                EditorStateJson: editorStateJson);

            var openResult = await DocumentsPanel.OpenDocument(fileResource, restoreOptions);
            if (openResult.IsFailure)
            {
                _logger.LogWarning(openResult, $"Failed to open previously open document '{fileResource}'");
                await StoreDocumentEditorStateAsync(fileResource, null);
            }
        }
    }

    // Folds a stored section into one that currently holds tabs. A secondary section whose area restored
    // unsplit folds into that area's primary section. A section in a collapsed area is kept: the area
    // holds its documents while hidden, and they reappear in place when it is shown again.
    // Splits an area when a document restores into its secondary section, so the split follows the
    // restored documents rather than a separately stored flag that could disagree with them.
    private DocumentSection ResolveRestoreSection(DocumentSection storedSection)
    {
        var area = storedSection.GetArea();
        if (storedSection.IsSecondarySection()
            && !DocumentsPanel.IsAreaSplit(area))
        {
            DocumentsPanel.SetAreaSplit(area, true);
        }

        return storedSection;
    }

    private async Task RestoreActiveDocumentAsync()
    {
        var propertyBag = _workspaceWrapper.WorkspaceService.WorkspaceSettings.PropertyBag;
        Guard.IsNotNull(propertyBag);

        var storedActiveDocument = await propertyBag.GetPropertyAsync<string>(ActiveDocumentKey);

        var activeDocument = ResourceKey.Empty;
        if (!string.IsNullOrEmpty(storedActiveDocument))
        {
            if (!ResourceKey.TryCreate(storedActiveDocument, out activeDocument))
            {
                _logger.LogWarning($"Invalid resource key '{storedActiveDocument}' found for previously selected document");
                activeDocument = ResourceKey.Empty;
            }
        }

        // Always delegate to the panel, even when the stored value is empty or invalid. The panel
        // restores this document when it is still open, and otherwise falls back so that any open
        // documents leave exactly one active document. The restored document is selected but not focused:
        // opening a project is not a request to type into whatever was open last.
        DocumentsPanel.ActiveDocument = activeDocument;
    }

    private async Task OpenDefaultReadmeAsync()
    {
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var readmeResource = new ResourceKey("readme.md");

        var normalizeResult = resourceRegistry.NormalizeResourceKey(readmeResource);
        if (normalizeResult.IsFailure)
        {
            return;
        }
        var normalizedResource = normalizeResult.Value;

        var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;
        var infoResult = await resourceFileSystem.GetInfoAsync(normalizedResource);
        if (infoResult.IsFailure
            || infoResult.Value.Kind != StorageItemKind.File)
        {
            return;
        }

        _commandService.Execute<IOpenDocumentCommand>(command =>
        {
            command.FileResource = normalizedResource;
            command.ForceReload = false;
        });
    }
}
