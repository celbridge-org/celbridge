using System.Collections.ObjectModel;
using Celbridge.Projects;
using Celbridge.Settings;
using Microsoft.Extensions.Localization;

namespace Celbridge.ProjectSettings.ViewModels;

/// <summary>
/// A known feature flag surfaced on the Feature Flags section: its flag name and the resource keys for its
/// localized title and description.
/// </summary>
internal sealed record FeatureFlagDescriptor(string FlagName, string TitleKey, string DescriptionKey);

/// <summary>
/// The known feature flags shown on the Feature Flags section, in display order. Mirrors FeatureFlagConstants;
/// each entry adds the localized title and description shown to the user.
/// </summary>
internal static class FeatureFlagCatalog
{
    public static readonly IReadOnlyList<FeatureFlagDescriptor> Descriptors = new List<FeatureFlagDescriptor>
    {
        new(FeatureFlagConstants.ConsolePanel, "ProjectSettings_FeatureFlag_ConsolePanel_Title", "ProjectSettings_FeatureFlag_ConsolePanel_Description"),
        new(FeatureFlagConstants.McpTools, "ProjectSettings_FeatureFlag_McpTools_Title", "ProjectSettings_FeatureFlag_McpTools_Description"),
        new(FeatureFlagConstants.WebAccessTools, "ProjectSettings_FeatureFlag_WebAccessTools_Title", "ProjectSettings_FeatureFlag_WebAccessTools_Description"),
        new(FeatureFlagConstants.WebViewDevTools, "ProjectSettings_FeatureFlag_WebViewDevTools_Title", "ProjectSettings_FeatureFlag_WebViewDevTools_Description"),
        new(FeatureFlagConstants.WebViewDevToolsEval, "ProjectSettings_FeatureFlag_WebViewDevToolsEval_Title", "ProjectSettings_FeatureFlag_WebViewDevToolsEval_Description"),
        new(FeatureFlagConstants.AnswerDialog, "ProjectSettings_FeatureFlag_AnswerDialog_Title", "ProjectSettings_FeatureFlag_AnswerDialog_Description"),
    };
}

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
