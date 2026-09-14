namespace Celbridge.ProjectSettings.ViewModels;

/// <summary>
/// One group card in the Features section: the localized title of its area and its flags.
/// </summary>
public sealed class FeatureFlagGroupViewModel
{
    public FeatureFlagGroupViewModel(string title, IReadOnlyList<FeatureFlagItemViewModel> flags)
    {
        Title = title;
        Flags = flags;
    }

    public string Title { get; }

    public IReadOnlyList<FeatureFlagItemViewModel> Flags { get; }
}
