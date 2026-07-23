using System.Collections.ObjectModel;
using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Logging;
using Celbridge.Packages;
using Celbridge.Projects;
using Celbridge.UserInterface.Helpers;

namespace Celbridge.ProjectSettings.ViewModels;

/// <summary>
/// Drives the Packages section: every user-curatable package with its contributions nested beneath, each
/// with an enable toggle and descriptor form fields. Toggles and field edits write straight through to
/// the .celbridge file.
/// </summary>
public class PackagesSectionViewModel : ProjectSettingsSectionViewModel
{
    private readonly IPackageLocalizationService _packageLocalization;
    private readonly ILogger<PackagesSectionViewModel> _logger;

    public ObservableCollection<PackageItemViewModel> Packages { get; } = new();

    public PackagesSectionViewModel(ProjectSettingsContext context, IPackageLocalizationService packageLocalization)
        : base(context)
    {
        _packageLocalization = packageLocalization;
        _logger = ServiceLocator.AcquireService<ILogger<PackagesSectionViewModel>>();
    }

    public override void Load()
    {
        Packages.Clear();

        var config = GetConfig();
        var packageService = WorkspaceService?.PackageService;
        if (config is null
            || packageService is null)
        {
            return;
        }

        var allPackages = packageService.GetAllPackages();

        var issuesByEditorId = packageService.GetContributionIssues()
            .GroupBy(issue => issue.EditorId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ContributionIssue>)group.ToArray());

        BuildPackages(config, allPackages, issuesByEditorId);
    }

    // Each non-disabled, user-curatable package becomes a row with its contributions nested beneath it.
    // A recommended contribution shows enabled and toggles off, an optional one shows disabled and
    // toggles on, a required one shows no toggle. Overrides from the normalized config supply the toggle
    // state and any non-default config values.
    private void BuildPackages(
        ProjectConfig config,
        IReadOnlyList<Package> allPackages,
        IReadOnlyDictionary<string, IReadOnlyList<ContributionIssue>> issuesByEditorId)
    {
        var disabledPackages = new HashSet<string>(config.Celbridge.DisabledPackages, StringComparer.Ordinal);

        var overridesByRef = new Dictionary<(string Package, string Contribution), ContributionOverride>();
        foreach (var contributionOverride in config.ContributionOverrides)
        {
            overridesByRef[(contributionOverride.PackageName, contributionOverride.ContributionId)] = contributionOverride;
        }

        foreach (var package in allPackages)
        {
            var name = package.Info.Name;

            // Always-active packages are built-in infrastructure and are not user-curatable.
            if (Celbridge.Packages.BuiltInEditors.IsAlwaysActivePackage(name))
            {
                continue;
            }

            var isEnabled = !disabledPackages.Contains(name);

            var packageInfo = new PackageItemInfo
            {
                Name = name,
                NameLabel = PackageNameLabel(package),
                DisplayName = PackageDisplayName(package),
                ManifestPath = System.IO.Path.Combine(package.Info.PackageFolder, PackageConstants.ManifestFileName),
                CanOpenManifest = package.Info.Origin == PackageOrigin.Project
            };

            var packageItem = new PackageItemViewModel(packageInfo, isEnabled, SetPackageDisabled, OpenManifest);

            foreach (var contribution in package.Editors)
            {
                overridesByRef.TryGetValue((name, contribution.Id), out var contributionOverride);

                var contributionEnabled = contribution.Activation == ActivationPolicy.Optional
                    ? contributionOverride?.Enabled ?? false
                    : !(contributionOverride?.Disabled ?? false);

                var row = BuildContributionRow(contribution, contributionOverride, contributionEnabled, issuesByEditorId);
                packageItem.Contributions.Add(row);
            }

            Packages.Add(packageItem);
        }
    }

    private ContributionItemViewModel BuildContributionRow(
        EditorContribution contribution,
        ContributionOverride? contributionOverride,
        bool isEnabled,
        IReadOnlyDictionary<string, IReadOnlyList<ContributionIssue>> issuesByEditorId)
    {
        var packageName = contribution.Package.Name;
        var displayName = ContributionDisplayName(contribution);
        var fileTypes = contribution.FileTypes
            .Select(fileType => new FileTypeInfo(fileType.FileExtension.ToLowerInvariant(), fileType.Category))
            .ToArray();
        var editorId = EditorId.Create(packageName, contribution.Id).ToString();
        var iconName = contribution.UtilityDescriptor?.Icon ?? string.Empty;

        issuesByEditorId.TryGetValue(editorId, out var issues);

        var description = string.Empty;
        if (!string.IsNullOrWhiteSpace(contribution.Description))
        {
            description = PackageDisplayText.Resolve(_packageLocalization, contribution.Package, contribution.Description);
        }

        var info = new ContributionItemInfo
        {
            PackageName = packageName,
            ContributionId = contribution.Id,
            DisplayName = displayName,
            Description = description,
            IsUtility = contribution.IsUtility,
            IconName = iconName,
            ManifestPath = contribution.ManifestPath,
            CanOpenManifest = contribution.Package.Origin == PackageOrigin.Project,
            IsOptional = contribution.Activation == ActivationPolicy.Optional,
            CanToggle = contribution.Activation != ActivationPolicy.Required,
            EditorId = editorId,
            FileTypes = fileTypes,
            Issues = issues ?? []
        };

        var row = new ContributionItemViewModel(info, SetContributionEnabled, OpenManifest);

        foreach (var descriptor in contribution.ConfigDescriptors)
        {
            object? rawValue = null;
            contributionOverride?.Config.TryGetValue(descriptor.Key, out rawValue);
            var field = new ConfigFieldViewModel(descriptor, packageName, contribution.Id, rawValue, PackageDisplayText.Humanize(descriptor.Key), CommitContributionValue);
            row.ConfigFields.Add(field);
        }

        row.InitializeState(isEnabled);

        return row;
    }

    // Opens a package or editor manifest as a document. Only project manifests reach here, so a path that
    // resolves to no resource key means the package moved out from under the registry since discovery.
    private void OpenManifest(string manifestPath)
    {
        var resourceRegistry = WorkspaceService?.ResourceService.Registry;
        if (resourceRegistry is null)
        {
            return;
        }

        var getKeyResult = resourceRegistry.GetResourceKey(manifestPath);
        if (getKeyResult.IsFailure)
        {
            _logger.LogWarning(getKeyResult, $"Failed to open manifest: {manifestPath}");
            return;
        }
        var manifestResource = getKeyResult.Value;

        CommandService.Execute<IOpenDocumentCommand>(command => command.FileResource = manifestResource);
    }

    private void SetPackageDisabled(string packageName, bool disabled)
    {
        WriteEdits(new SetPackageDisabledEdit(packageName, disabled));
    }

    private void SetContributionEnabled(ContributionItemViewModel row, bool enabled)
    {
        ProjectConfigEdit edit;
        if (row.IsOptional)
        {
            edit = new SetContributionEnabledEdit(row.PackageName, row.ContributionId, enabled);
        }
        else
        {
            edit = new SetContributionDisabledEdit(row.PackageName, row.ContributionId, !enabled);
        }

        WriteEdits(edit);
    }

    private void CommitContributionValue(string packageName, string contributionId, string key, ConfigEditValue? value)
    {
        ProjectConfigEdit edit;
        if (value is null)
        {
            edit = new RemoveContributionValueEdit(packageName, contributionId, key);
        }
        else
        {
            edit = new SetContributionValueEdit(packageName, contributionId, key, value);
        }

        WriteEdits(edit);
    }

    private string PackageDisplayName(Package package)
    {
        var title = package.Info.Title;

        string name;
        if (string.IsNullOrWhiteSpace(title))
        {
            name = PackageDisplayText.HumanizeLastSegment(package.Info.Name);
        }
        else
        {
            name = PackageDisplayText.Resolve(_packageLocalization, package.Info, title);
        }

        return ProjectSettingsLabels.PackageName(name);
    }

    private string PackageNameLabel(Package package)
    {
        var name = package.Info.Name;
        if (package.Info.Origin == PackageOrigin.Bundled)
        {
            return ProjectSettingsLabels.BuiltInPackageName(name);
        }

        return name;
    }

    private string ContributionDisplayName(EditorContribution contribution)
    {
        var displayName = contribution.DisplayName;
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return PackageDisplayText.Humanize(contribution.Id);
        }

        return PackageDisplayText.Resolve(_packageLocalization, contribution.Package, displayName);
    }
}
