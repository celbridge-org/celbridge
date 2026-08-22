using Microsoft.UI.Xaml.Controls.Primitives;

namespace Celbridge.UserInterface.Views.Controls;

/// <summary>
/// One section in a settings surface: the stable key it is persisted under, the icon and label shown for
/// it in the rail, the description of what it covers, the content shown while it is selected, and whether
/// it is the selected one.
/// </summary>
public sealed partial class SettingsSection : ObservableObject
{
    public SettingsSection(string key, string iconName, string label, string description, object content)
    {
        Key = key;
        IconName = iconName;
        Label = label;
        Description = description;
        Content = content;
    }

    public string Key { get; }

    public string IconName { get; }

    public string Label { get; }

    public string Description { get; }

    public object Content { get; }

    // Drives the rail row's checked state. Set by the owning view model so exactly one section carries it.
    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// The shared settings layout: a rail of sections beside the selected section's content, under a heading
/// band naming it. The owner supplies the sections and holds the selection, so it decides where the
/// selected section is remembered.
/// </summary>
public sealed partial class SettingsSectionSwitcher : UserControl
{
    // The inset around a section's content, matching the heading band above it.
    private const double SectionContentInset = 16;

    private readonly Dictionary<SettingsSection, ScrollViewer> _sectionContainers = new();

    /// <summary>
    /// The sections to show, in rail order.
    /// </summary>
    public IReadOnlyList<SettingsSection>? Sections
    {
        get => (IReadOnlyList<SettingsSection>?)GetValue(SectionsProperty);
        set => SetValue(SectionsProperty, value);
    }

    public static readonly DependencyProperty SectionsProperty =
        DependencyProperty.Register(
            nameof(Sections),
            typeof(IReadOnlyList<SettingsSection>),
            typeof(SettingsSectionSwitcher),
            new PropertyMetadata(null, OnSectionsChanged));

    /// <summary>
    /// The section currently showing. Clicking a rail row writes the clicked section here, so an owner
    /// binding two-way sees the user's choice.
    /// </summary>
    public SettingsSection? SelectedSection
    {
        get => (SettingsSection?)GetValue(SelectedSectionProperty);
        set => SetValue(SelectedSectionProperty, value);
    }

    public static readonly DependencyProperty SelectedSectionProperty =
        DependencyProperty.Register(
            nameof(SelectedSection),
            typeof(SettingsSection),
            typeof(SettingsSectionSwitcher),
            new PropertyMetadata(null, OnSelectedSectionChanged));

    public SettingsSectionSwitcher()
    {
        this.InitializeComponent();

        double panelCornerRadius = (double)Application.Current.Resources["PanelCornerRadius"];
        SectionArea.CornerRadius = new CornerRadius(panelCornerRadius);
    }

    private static void OnSectionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var switcher = (SettingsSectionSwitcher)d;
        switcher.BuildSections();
    }

    private static void OnSelectedSectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var switcher = (SettingsSectionSwitcher)d;
        switcher.ApplySelection();
    }

    // Realizes every section's content up front. Each gets its own scroll container, so a section keeps
    // its scroll position while another one is showing.
    private void BuildSections()
    {
        SectionContent.Children.Clear();
        _sectionContainers.Clear();

        var sections = Sections;
        RailItems.ItemsSource = sections;

        if (sections is null)
        {
            ApplySelection();
            return;
        }

        foreach (var section in sections)
        {
            var sectionPresenter = new ContentControl
            {
                Content = section.Content,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                IsTabStop = false
            };

            var sectionContainer = new ScrollViewer
            {
                Padding = new Thickness(SectionContentInset),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollMode = ScrollMode.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = sectionPresenter,
                Visibility = Visibility.Collapsed
            };

            SectionContent.Children.Add(sectionContainer);
            _sectionContainers.Add(section, sectionContainer);
        }

        ApplySelection();
    }

    private void ApplySelection()
    {
        var selectedSection = SelectedSection;

        foreach (var sectionContainer in _sectionContainers)
        {
            var isSelected = ReferenceEquals(sectionContainer.Key, selectedSection);
            sectionContainer.Value.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
        }

        SectionLabel.Text = selectedSection?.Label ?? string.Empty;
        SectionDescription.Text = selectedSection?.Description ?? string.Empty;
    }

    private void RailButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton railButton
            || railButton.DataContext is not SettingsSection section)
        {
            return;
        }

        SelectedSection = section;

        // A toggle unchecks itself when clicked while already checked. The rail always has a section
        // showing, so the row follows the selection rather than its own toggle.
        railButton.IsChecked = section.IsSelected;
    }
}
