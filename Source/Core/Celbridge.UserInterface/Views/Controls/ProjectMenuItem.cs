namespace Celbridge.UserInterface.Views.Controls;

/// <summary>
/// A menu row that presents a project as its name above the folder that contains it. MenuFlyoutItem carries
/// a single line of text, so this adds the supporting text that the row template presents below the name.
/// </summary>
public sealed class ProjectMenuItem : MenuFlyoutItem
{
    public static readonly DependencyProperty SecondaryTextProperty = DependencyProperty.Register(
        nameof(SecondaryText),
        typeof(string),
        typeof(ProjectMenuItem),
        new PropertyMetadata(string.Empty));

    /// <summary>
    /// Supporting text shown on a second line below the project name.
    /// </summary>
    public string SecondaryText
    {
        get => (string)GetValue(SecondaryTextProperty);
        set => SetValue(SecondaryTextProperty, value);
    }
}
