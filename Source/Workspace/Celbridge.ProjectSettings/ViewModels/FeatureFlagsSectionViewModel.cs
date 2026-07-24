using System.Collections.ObjectModel;
using Celbridge.Projects;
using Celbridge.Settings;
using Microsoft.Extensions.Localization;

namespace Celbridge.ProjectSettings.ViewModels;

/// <summary>
/// Drives the Feature Flags section: every known feature flag with a tri-state control that pins it on or
/// off for the project, or clears the override to inherit the application default. Edits write straight
/// through to the .celbridge file.
/// </summary>
public class FeatureFlagsSectionViewModel : ProjectSettingsSectionViewModel
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IFeatureFlags _featureFlags;

    public ObservableCollection<FeatureFlagItemViewModel> FeatureFlags { get; } = new();

    public FeatureFlagsSectionViewModel(ProjectSettingsContext context, IStringLocalizer stringLocalizer)
        : base(context)
    {
        _stringLocalizer = stringLocalizer;
        _featureFlags = ServiceLocator.AcquireService<IFeatureFlags>();
    }

    public override void Load()
    {
        FeatureFlags.Clear();

        var config = GetConfig();
        if (config is null)
        {
            return;
        }

        foreach (var descriptor in FeatureFlagCatalog.Descriptors)
        {
            var info = new FeatureFlagItemInfo
            {
                FlagName = descriptor.FlagName,
                Title = _stringLocalizer.GetString(descriptor.TitleKey),
                Description = _stringLocalizer.GetString(descriptor.DescriptionKey),
                ApplicationValue = _featureFlags.GetApplicationValue(descriptor.FlagName),
                Selection = ResolveSelection(config, descriptor.FlagName),
            };

            FeatureFlags.Add(new FeatureFlagItemViewModel(info, SetSelection));
        }
    }

    // A flag present in the project's features table is pinned on or off; an absent flag inherits the default.
    private static FeatureFlagSelection ResolveSelection(ProjectConfig config, string flagName)
    {
        if (config.Features.TryGetValue(flagName, out var value))
        {
            return value ? FeatureFlagSelection.On : FeatureFlagSelection.Off;
        }

        return FeatureFlagSelection.Default;
    }

    private void SetSelection(string flagName, FeatureFlagSelection selection)
    {
        ProjectConfigEdit edit;
        if (selection == FeatureFlagSelection.Default)
        {
            edit = new RemoveFeatureFlagEdit(flagName);
        }
        else
        {
            edit = new SetFeatureFlagEdit(flagName, selection == FeatureFlagSelection.On);
        }

        WriteEdits(edit);
    }
}
