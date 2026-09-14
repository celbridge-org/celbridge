using Celbridge.Settings;

namespace Celbridge.ProjectSettings.ViewModels;

/// <summary>
/// A known feature flag shown as a row in the Features section: its flag name and the resource keys for its
/// localized title and description.
/// </summary>
internal sealed record FeatureFlagDescriptor(string FlagName, string TitleKey, string DescriptionKey);

/// <summary>
/// A group of related feature flags, shown as one card in the Features section: the resource key for the
/// localized title of its area, and its flags in display order.
/// </summary>
internal sealed record FeatureFlagGroupDescriptor(string TitleKey, IReadOnlyList<FeatureFlagDescriptor> Flags);

/// <summary>
/// The known feature flags shown in the Features section, grouped by area and in display order. Every flag in
/// FeatureFlagConstants belongs to one group here, which adds the localized title and description shown to
/// the user.
/// </summary>
internal static class FeatureFlagCatalog
{
    public static readonly IReadOnlyList<FeatureFlagGroupDescriptor> Groups = new List<FeatureFlagGroupDescriptor>
    {
        new("ProjectSettings_FeatureFlagGroup_Web_Title", new List<FeatureFlagDescriptor>
        {
            new(FeatureFlagConstants.WebViewDevTools, "ProjectSettings_FeatureFlag_WebViewDevTools_Title", "ProjectSettings_FeatureFlag_WebViewDevTools_Description"),
            new(FeatureFlagConstants.WebViewDevToolsEval, "ProjectSettings_FeatureFlag_WebViewDevToolsEval_Title", "ProjectSettings_FeatureFlag_WebViewDevToolsEval_Description"),
            new(FeatureFlagConstants.WebViewLoadDiagnostics, "ProjectSettings_FeatureFlag_WebViewLoadDiagnostics_Title", "ProjectSettings_FeatureFlag_WebViewLoadDiagnostics_Description"),
        }),
        new("ProjectSettings_FeatureFlagGroup_Resources_Title", new List<FeatureFlagDescriptor>
        {
            new(FeatureFlagConstants.OpenCel, "ProjectSettings_FeatureFlag_OpenCel_Title", "ProjectSettings_FeatureFlag_OpenCel_Description"),
        }),
        new("ProjectSettings_FeatureFlagGroup_Documents_Title", new List<FeatureFlagDescriptor>
        {
            new(FeatureFlagConstants.NoteEditor, "ProjectSettings_FeatureFlag_NoteEditor_Title", "ProjectSettings_FeatureFlag_NoteEditor_Description"),
        }),
    };
}
