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
    /// Serialization DTO for a single open document tab.
    /// </summary>
    public record StoredDocumentAddress(string Resource, int WindowIndex, int SectionIndex, int TabOrder);

    public async Task StoreDocumentLayoutAsync()
    {
        var propertyBag = _workspaceWrapper.WorkspaceService.WorkspaceSettings.PropertyBag;
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

    public async Task StoreActiveDocumentAsync()
    {
        var propertyBag = _workspaceWrapper.WorkspaceService.WorkspaceSettings.PropertyBag;
        Guard.IsNotNull(propertyBag);

        // Read the panel's active document directly. The gated IDocumentsService.ActiveDocument
        // reports Empty until the workspace page finishes loading, and this runs before that.
        var activeDocument = DocumentsPanel.ActiveDocument;
        await propertyBag.SetPropertyAsync(ActiveDocumentKey, activeDocument.ToString());
    }

    public async Task StoreSectionRatiosAsync(List<double> ratios)
    {
        var propertyBag = _workspaceWrapper.WorkspaceService.WorkspaceSettings.PropertyBag;
        Guard.IsNotNull(propertyBag);

        await propertyBag.SetPropertyAsync(SectionRatiosKey, ratios);
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
        var propertyBag = _workspaceWrapper.WorkspaceService.WorkspaceSettings.PropertyBag;
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

            // A stored utils: entry means the utility was docked last session. Utilities are created eagerly
            // at workspace load, before this runs, so reparent the live utility into its saved tab position
            // instead of creating a second view.
            if (fileResource.Root == ProjectConstants.UtilsFolder)
            {
                int utilitySection = Math.Min(stored.SectionIndex, currentSectionCount - 1);
                var utilityAddress = new DocumentAddress(stored.WindowIndex, utilitySection, stored.TabOrder);

                var utilityService = _workspaceWrapper.WorkspaceService.UtilityService;
                var restoreResult = await utilityService.RestoreDockedUtility(fileResource, utilityAddress);
                if (restoreResult.IsFailure)
                {
                    _logger.LogWarning(restoreResult, $"Failed to restore docked utility '{fileResource}'");
                }
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

            // An empty editor id makes the factory resolve the editor from the sidecar (or the
            // per-extension default) rather than from persisted layout state.
            string? editorStateJson = null;
            editorStates?.TryGetValue(fileResource.ToString(), out editorStateJson);

            var restoreOptions = new OpenDocumentOptions(
                Address: address,
                Activate: false,
                EditorId: EditorId.Empty,
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
        // documents leave exactly one active document.
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
