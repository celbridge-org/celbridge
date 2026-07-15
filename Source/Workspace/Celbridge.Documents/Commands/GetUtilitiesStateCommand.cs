using Celbridge.Commands;
using Celbridge.Packages;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Documents.Commands;

// Agent-facing state reads split by the shape of the state. Many-source, UI-thread-bound structural state (open
// tabs, the utility catalog) is read live through a query command like this one: routing through the command
// queue marshals the read onto the UI thread and serializes it after prior commands, so callers observe state
// consistent with every previously enqueued command. Few-source scalar state (see AppStateProvider) is cached
// from broadcasts instead, because keeping such a cache coherent across the many paths that mutate structural
// state would drift. A general "cache everything" scheme was rejected: it moves the UI-thread read from
// per-query to per-change, which is more work for agent workloads.
public class GetUtilitiesStateCommand : CommandBase, IGetUtilitiesStateCommand
{
    public override CommandFlags CommandFlags => CommandFlags.SuppressCommandLog;

    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IPackageLocalizationService _packageLocalizationService;

    public UtilitiesStateSnapshot ResultValue { get; private set; }
        = new UtilitiesStateSnapshot(Array.Empty<UtilityInfo>());

    public GetUtilitiesStateCommand(
        IWorkspaceWrapper workspaceWrapper,
        IStringLocalizer stringLocalizer,
        IPackageLocalizationService packageLocalizationService)
    {
        _workspaceWrapper = workspaceWrapper;
        _stringLocalizer = stringLocalizer;
        _packageLocalizationService = packageLocalizationService;
    }

    public override async Task<Result> ExecuteAsync()
    {
        await Task.CompletedTask;

        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            ResultValue = new UtilitiesStateSnapshot(Array.Empty<UtilityInfo>());
            return Result.Ok();
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var utilityPanel = workspaceService.UtilityPanel;
        var packageService = workspaceService.PackageService;
        var documentsService = workspaceService.DocumentsService;

        var activeUtilityId = utilityPanel.ActiveUtilityId;

        // A utility is docked when its backing resource is open as a document. Capture the open-document
        // resources and the active document so each utility's mode and shown state can be resolved below.
        var openResources = new HashSet<ResourceKey>();
        foreach (var openDocument in documentsService.GetOpenDocuments())
        {
            openResources.Add(openDocument.FileResource);
        }
        var activeDocument = documentsService.ActiveDocument;

        var utilities = new List<UtilityInfo>();

        // Built-in Utility Panel surfaces are non-dockable, so they are always in the Utility Panel.
        string explorerName = _stringLocalizer.GetString("UtilityPanel_ExplorerTooltip");
        utilities.Add(new UtilityInfo(
            BuiltInUtilityIds.Explorer,
            explorerName,
            Location: DockLocation.UtilityPanel,
            IsShown: activeUtilityId == BuiltInUtilityIds.Explorer));

        string searchName = _stringLocalizer.GetString("UtilityPanel_SearchTooltip");
        utilities.Add(new UtilityInfo(
            BuiltInUtilityIds.Search,
            searchName,
            Location: DockLocation.UtilityPanel,
            IsShown: activeUtilityId == BuiltInUtilityIds.Search));

        // Package-custom utilities. Each is a persistent surface, in the rail or docked as a document tab.
        // When it is a document it is shown if its tab is the active document; when it is in the panel it is
        // shown if it is the active rail surface.
        foreach (var contribution in packageService.GetAllDocumentEditors())
        {
            if (contribution is not CustomDocumentEditorContribution { IsUtility: true } utility)
            {
                continue;
            }

            var utilityId = UtilityId.Create(utility.Package.Name, utility.Id);
            var displayName = ResolveLocalizedString(utility.Package, utility.DisplayName);

            var isDocumentDocked = false;
            var utilityResource = ResourceKey.Empty;
            if (utility.UtilityDescriptor is not null
                && ResourceKey.TryCreate(utility.UtilityDescriptor.Resource, out utilityResource))
            {
                isDocumentDocked = openResources.Contains(utilityResource);
            }

            var location = isDocumentDocked ? DockLocation.Document : DockLocation.UtilityPanel;

            var isShown = isDocumentDocked
                ? activeDocument == utilityResource
                : activeUtilityId == utilityId;

            utilities.Add(new UtilityInfo(utilityId, displayName, location, isShown));
        }

        ResultValue = new UtilitiesStateSnapshot(utilities);

        return Result.Ok();
    }

    private string ResolveLocalizedString(PackageInfo package, string key)
    {
        var localizationStrings = _packageLocalizationService.LoadStrings(package);
        if (localizationStrings.TryGetValue(key, out var localized))
        {
            return localized;
        }

        return key;
    }
}
