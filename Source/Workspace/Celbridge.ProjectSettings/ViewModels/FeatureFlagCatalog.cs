using Celbridge.Settings;

namespace Celbridge.ProjectSettings.ViewModels;

/// <summary>
/// A known feature flag surfaced on the Feature Flags section: its flag name and the resource keys for its
/// localized title and description.
/// </summary>
internal sealed record FeatureFlagDescriptor(string FlagName, string TitleKey, string DescriptionKey);

/// <summary>
/// The known feature flags shown on the Feature Flags section, in display order. Every flag in
/// FeatureFlagConstants has one entry here adding the localized title and description shown to the user;
/// a parity test keeps the two lists in sync.
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
