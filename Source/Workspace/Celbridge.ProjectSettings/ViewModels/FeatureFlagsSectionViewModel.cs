using System.Collections.ObjectModel;
using System.ComponentModel;
using Celbridge.Projects;
using Celbridge.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;

namespace Celbridge.ProjectSettings.ViewModels;

/// <summary>
/// Drives the Features section: every known feature flag, grouped by area, with an on/off toggle. A flag at its
/// default has no entry in the project's features table, and a flag switched away from it is written there.
/// Reset to Defaults clears every entry the section lists. Edits write straight through to the .celbridge file.
/// </summary>
public partial class FeatureFlagsSectionViewModel : ProjectSettingsSectionViewModel
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IFeatureFlags _featureFlags;

    public ObservableCollection<FeatureFlagGroupViewModel> Groups { get; } = new();

    /// <summary>
    /// True while the project file sets any flag the section lists, so a reset has something to clear.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetToDefaultsCommand))]
    private bool _canResetToDefaults;

    public string ResetButtonText => ProjectSettingsLabels.FeatureFlagsResetButton;

    public string ResetButtonTooltip => ProjectSettingsLabels.FeatureFlagsResetTooltip;

    public FeatureFlagsSectionViewModel(ProjectSettingsContext context, IStringLocalizer stringLocalizer)
        : base(context)
    {
        _stringLocalizer = stringLocalizer;
        _featureFlags = ServiceLocator.AcquireService<IFeatureFlags>();
    }

    public override void Load()
    {
        Groups.Clear();

        var config = GetConfig();
        if (config is null)
        {
            CanResetToDefaults = false;
            return;
        }

        foreach (var groupDescriptor in FeatureFlagCatalog.Groups)
        {
            var flags = new List<FeatureFlagItemViewModel>();
            foreach (var descriptor in groupDescriptor.Flags)
            {
                var info = new FeatureFlagItemInfo
                {
                    FlagName = descriptor.FlagName,
                    Title = _stringLocalizer.GetString(descriptor.TitleKey),
                    Description = _stringLocalizer.GetString(descriptor.DescriptionKey),
                    ApplicationValue = _featureFlags.GetApplicationValue(descriptor.FlagName),
                    ProjectValue = ResolveProjectValue(config, descriptor.FlagName),
                };

                var flag = new FeatureFlagItemViewModel(info, SetProjectValue);
                flag.PropertyChanged += OnFlagPropertyChanged;
                flags.Add(flag);
            }

            Groups.Add(new FeatureFlagGroupViewModel(_stringLocalizer.GetString(groupDescriptor.TitleKey), flags));
        }

        UpdateCanResetToDefaults();
    }

    [RelayCommand(CanExecute = nameof(CanResetToDefaults))]
    private void ResetToDefaults()
    {
        // Clearing the entries directly also removes one that matches its flag's default, which switching
        // a toggle would leave in place.
        EditConfig(draft =>
        {
            foreach (var groupDescriptor in FeatureFlagCatalog.Groups)
            {
                foreach (var descriptor in groupDescriptor.Flags)
                {
                    draft.RemoveFeatureFlag(descriptor.FlagName);
                }
            }
        });

        foreach (var group in Groups)
        {
            foreach (var flag in group.Flags)
            {
                flag.ShowDefault();
            }
        }
    }

    private void OnFlagPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FeatureFlagItemViewModel.HasProjectValue))
        {
            UpdateCanResetToDefaults();
        }
    }

    private void UpdateCanResetToDefaults()
    {
        CanResetToDefaults = Groups.Any(group => group.Flags.Any(flag => flag.HasProjectValue));
    }

    // The value the project's features table sets for a flag, or null when the project leaves it at the default.
    private static bool? ResolveProjectValue(ProjectConfig config, string flagName)
    {
        if (config.Features.TryGetValue(flagName, out var value))
        {
            return value;
        }

        return null;
    }

    private void SetProjectValue(string flagName, bool? value)
    {
        if (value is null)
        {
            EditConfig(draft => draft.RemoveFeatureFlag(flagName));
            return;
        }

        var enabled = value.Value;
        EditConfig(draft => draft.SetFeatureFlag(flagName, enabled));
    }
}
