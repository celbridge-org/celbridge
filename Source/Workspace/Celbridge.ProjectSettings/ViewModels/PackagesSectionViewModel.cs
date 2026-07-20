using System.Collections.ObjectModel;
using Celbridge.Documents;
using Celbridge.Packages;
using Celbridge.Projects;

namespace Celbridge.ProjectSettings.ViewModels;

/// <summary>
/// Drives the Packages section: every user-curatable package with its contributions nested beneath, each
/// with an enable toggle and descriptor form fields. Toggles and field edits write straight through to
/// the .celbridge file.
/// </summary>
public class PackagesSectionViewModel : ProjectSettingsSectionViewModel
{
    private readonly IPackageLocalizationService _packageLocalization;

    public ObservableCollection<PackageItemViewModel> Packages { get; } = new();

    public PackagesSectionViewModel(ProjectSettingsContext context, IPackageLocalizationService packageLocalization)
        : base(context)
    {
        _packageLocalization = packageLocalization;
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
        BuildPackages(config, allPackages);
    }

    // Each non-disabled, user-curatable package becomes a row with its contributions nested beneath it.
    // A recommended contribution shows enabled and toggles off, an optional one shows disabled and
    // toggles on, a required one shows no toggle. Overrides from the normalized config supply the toggle
    // state and any non-default config values.
    private void BuildPackages(ProjectConfig config, IReadOnlyList<Package> allPackages)
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
            var packageItem = new PackageItemViewModel(name, PackageDisplayName(package), isEnabled, SetPackageDisabled);

            foreach (var contribution in package.Editors)
            {
                overridesByRef.TryGetValue((name, contribution.Id), out var contributionOverride);

                var contributionEnabled = contribution.Activation == ActivationPolicy.Optional
                    ? contributionOverride?.Enabled ?? false
                    : !(contributionOverride?.Disabled ?? false);

                var row = BuildContributionRow(contribution, contributionOverride, contributionEnabled);
                packageItem.Contributions.Add(row);
            }

            Packages.Add(packageItem);
        }
    }

    private ContributionItemViewModel BuildContributionRow(
        EditorContribution contribution,
        ContributionOverride? contributionOverride,
        bool isEnabled)
    {
        var packageName = contribution.Package.Name;
        var displayName = ContributionDisplayName(contribution);
        var fileTypes = contribution.FileTypes
            .Select(fileType => new FileTypeInfo(fileType.FileExtension.ToLowerInvariant(), fileType.Category))
            .ToArray();
        var editorId = EditorInstanceId.Create(packageName, contribution.Id).ToString();

        var row = new ContributionItemViewModel(
            packageName,
            contribution.Id,
            displayName,
            contribution.IsUtility,
            contribution.Activation == ActivationPolicy.Optional,
            contribution.Activation != ActivationPolicy.Required,
            editorId,
            fileTypes,
            SetContributionEnabled);

        foreach (var descriptor in contribution.ConfigDescriptors)
        {
            object? rawValue = null;
            contributionOverride?.Config.TryGetValue(descriptor.Key, out rawValue);
            var field = new ConfigFieldViewModel(descriptor, packageName, contribution.Id, rawValue, Humanize(descriptor.Key), CommitContributionValue);
            row.ConfigFields.Add(field);
        }

        row.InitializeState(isEnabled);

        return row;
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
        var localized = TryResolveLocalizedString(package.Info, package.Info.Title);
        if (localized is not null)
        {
            return localized;
        }

        return HumanizeLastSegment(package.Info.Name);
    }

    private string ContributionDisplayName(EditorContribution contribution)
    {
        var localized = TryResolveLocalizedString(contribution.Package, contribution.DisplayName);
        if (localized is not null)
        {
            return localized;
        }

        return Humanize(contribution.Id);
    }

    // Resolves a manifest string that may be a localization key against the package's own localization
    // files. Returns null when the manifest value is blank or the key has no entry, so the caller falls
    // back to a humanized identifier rather than showing a raw key.
    private string? TryResolveLocalizedString(PackageInfo package, string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var localizationStrings = _packageLocalization.LoadStrings(package);
        if (localizationStrings.TryGetValue(key, out var localized))
        {
            return localized;
        }

        return null;
    }

    private static string HumanizeLastSegment(string identifier)
    {
        var segments = identifier.Split('.');
        var lastSegment = segments[^1];

        return Humanize(lastSegment);
    }

    private static string Humanize(string identifier)
    {
        var words = identifier
            .Split('-', '.')
            .Where(word => word.Length > 0)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]);

        return string.Join(" ", words);
    }
}
