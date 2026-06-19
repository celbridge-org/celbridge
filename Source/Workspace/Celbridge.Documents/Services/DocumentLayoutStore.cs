using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.Documents.Services;

/// <summary>
/// Owns the workspace-settings round trip for the documents panel: which tabs
/// are open, in which sections, with which editor and saved view state.
/// Reads and writes the settings keys directly; DocumentsService delegates its
/// IDocumentsService persistence methods here.
/// </summary>
public class DocumentLayoutStore
{
    private const string DocumentLayoutKey = "DocumentLayout";
    private const string ActiveDocumentKey = "ActiveDocument";
    private const string SectionRatiosKey = "SectionRatios";
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
    /// Serialization DTO for a single open document tab. Public so the
    /// workspace-settings deserializer can reach it through the store. The
    /// document's editor is recovered from the sidecar at restore time (or
    /// from the per-extension default), so the layout never needs to persist
    /// the editor id directly.
    /// </summary>
    public record StoredDocumentAddress(string Resource, int WindowIndex, int SectionIndex, int TabOrder);

    public async Task StoreDocumentLayoutAsync()
    {
        var propertyBag = _workspaceWrapper.WorkspaceService.PropertyBag;
        Guard.IsNotNull(propertyBag);

        var storedAddresses = DocumentsPanel.GetOpenDocuments()
            .Select(document => new StoredDocumentAddress(
                document.FileResource.ToString(),
                document.Address.WindowIndex,
                document.Address.SectionIndex,
                document.Address.TabOrder))
            .OrderBy(address => address.WindowIndex)
            .ThenBy(address => address.SectionIndex)
            .ThenBy(address => address.TabOrder)
            .ToList();

        await propertyBag.SetPropertyAsync(DocumentLayoutKey, storedAddresses);
    }

    public async Task StoreActiveDocumentAsync(ResourceKey activeDocument)
    {
        var propertyBag = _workspaceWrapper.WorkspaceService.PropertyBag;
        Guard.IsNotNull(propertyBag);

        var fileResource = activeDocument.ToString();
        await propertyBag.SetPropertyAsync(ActiveDocumentKey, fileResource);
    }

    public async Task StoreSectionRatiosAsync(List<double> ratios)
    {
        var propertyBag = _workspaceWrapper.WorkspaceService.PropertyBag;
        Guard.IsNotNull(propertyBag);

        await propertyBag.SetPropertyAsync(SectionRatiosKey, ratios);
    }

    public async Task StoreDocumentEditorStatesAsync()
    {
        var propertyBag = _workspaceWrapper.WorkspaceService.PropertyBag;
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
                // A null / empty return from TrySaveEditorStateAsync means the editor is either
                // still initialising or has no state to contribute. In both cases we keep the
                // previously saved state rather than overwriting it. Losing state on a transient
                // "not ready" would surprise the user who carefully set scroll/zoom and then
                // happens to reload a workspace while a tab is mid-init.
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
        var propertyBag = _workspaceWrapper.WorkspaceService.PropertyBag;
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
            // Log at debug level so unexpected failures are visible without being alarming.
            _logger.LogDebug(ex, $"Failed to store editor state for '{fileResource}'");
        }
    }

    public async Task RestorePanelStateAsync()
    {
        var storedLayout = await LoadStoredLayoutAsync();

        if (storedLayout.SectionRatios is not null
            && storedLayout.SectionRatios.Count >= 1
            && storedLayout.SectionRatios.Count <= 3)
        {
            DocumentsPanel.SectionCount = storedLayout.SectionRatios.Count;
            DocumentsPanel.SetSectionRatios(storedLayout.SectionRatios);
        }

        if (storedLayout.Addresses is null
            || storedLayout.Addresses.Count == 0)
        {
            await OpenDefaultReadmeAsync();
            return;
        }

        await RestoreDocumentsAsync(storedLayout.Addresses, storedLayout.EditorStates);
        await RestoreActiveDocumentAsync();
    }

    private record StoredLayout(
        List<double>? SectionRatios,
        List<StoredDocumentAddress>? Addresses,
        Dictionary<string, string>? EditorStates);

    private async Task<StoredLayout> LoadStoredLayoutAsync()
    {
        var propertyBag = _workspaceWrapper.WorkspaceService.PropertyBag;
        Guard.IsNotNull(propertyBag);

        var sectionRatios = await propertyBag.GetPropertyAsync<List<double>>(SectionRatiosKey);

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

        return new StoredLayout(sectionRatios, storedAddresses, editorStates);
    }

    private async Task RestoreDocumentsAsync(
        IReadOnlyList<StoredDocumentAddress> storedAddresses,
        IReadOnlyDictionary<string, string>? editorStates)
    {
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        int currentSectionCount = DocumentsPanel.SectionCount;

        foreach (var stored in storedAddresses)
        {
            if (!ResourceKey.TryCreate(stored.Resource, out var fileResource))
            {
                _logger.LogWarning($"Invalid resource key '{stored.Resource}' found in previously open documents");
                continue;
            }

            var getResourceResult = resourceRegistry.GetResource(fileResource);
            if (getResourceResult.IsFailure)
            {
                _logger.LogWarning(getResourceResult, $"Failed to open document because '{fileResource}' resource does not exist.");
                continue;
            }

            var resolveResult = resourceRegistry.ResolveResourcePath(fileResource);
            if (resolveResult.IsFailure)
            {
                _logger.LogWarning(resolveResult, $"Failed to resolve path for resource: '{fileResource}'");
                continue;
            }

            var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;
            var infoResult = await resourceFileSystem.GetInfoAsync(fileResource);
            if (infoResult.IsFailure
                || infoResult.Value.Kind != StorageItemKind.File)
            {
                _logger.LogWarning($"Cannot access file for resource: '{fileResource}'");
                continue;
            }

            // Handle mismatch: if saved section doesn't exist, merge into last section
            int targetSection = Math.Min(stored.SectionIndex, currentSectionCount - 1);
            var address = new DocumentAddress(stored.WindowIndex, targetSection, stored.TabOrder);

            // Editor selection is resolved from the sidecar (or the per-extension
            // default), not from any persisted layout state. Passing Empty here
            // lets the factory consult the live sidecar instead of pinning a
            // possibly-stale id captured at the last shutdown.
            string? editorStateJson = null;
            editorStates?.TryGetValue(fileResource.ToString(), out editorStateJson);

            var restoreOptions = new OpenDocumentOptions(
                Address: address,
                Activate: false,
                EditorId: DocumentEditorId.Empty,
                EditorStateJson: editorStateJson);

            var openResult = await DocumentsPanel.OpenDocument(fileResource, restoreOptions);
            if (openResult.IsFailure)
            {
                _logger.LogWarning(openResult, $"Failed to open previously open document '{fileResource}'");
                await StoreDocumentEditorStateAsync(fileResource, null);
            }
        }
    }

    private async Task RestoreActiveDocumentAsync()
    {
        var propertyBag = _workspaceWrapper.WorkspaceService.PropertyBag;
        Guard.IsNotNull(propertyBag);

        var selectedDocument = await propertyBag.GetPropertyAsync<string>(ActiveDocumentKey);
        if (string.IsNullOrEmpty(selectedDocument))
        {
            return;
        }

        if (!ResourceKey.TryCreate(selectedDocument, out var selectedDocumentKey))
        {
            _logger.LogWarning($"Invalid resource key '{selectedDocument}' found for previously selected document");
            return;
        }

        // Set the active document. SectionContainer.SetActiveDocument also enforces the invariant
        // that every populated section has a selected tab, so non-active sections that had tabs
        // inserted during restore get a sensible default selection automatically.
        DocumentsPanel.ActiveDocument = selectedDocumentKey;
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
