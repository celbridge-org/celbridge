using Microsoft.UI.Xaml.Controls.Primitives;

namespace Celbridge.UserInterface.Views.Controls;

/// <summary>
/// One section in a settings surface: the stable key it is persisted under, the icon and label shown for
/// it in the rail, the description of what it covers, the content shown while it is selected, and whether
/// it is the selected one.
/// </summary>
public sealed partial class SettingsSection : ObservableObject
{
    public SettingsSection(
        string key,
        string iconName,
        string label,
        string description,
        object content,
        string issueTooltip = "")
    {
        Key = key;
        IconName = iconName;
        Label = label;
        Description = description;
        Content = content;
        IssueTooltip = issueTooltip;
    }

    public string Key { get; }

    public string IconName { get; }

    public string Label { get; }

    public string Description { get; }

    public object Content { get; }

    /// <summary>
    /// The tooltip shown on the section's issue pip. Empty for a section that never reports an issue.
    /// </summary>
    public string IssueTooltip { get; }

    // Drives the rail row's checked state. Set by the owning view model so exactly one section carries it.
    [ObservableProperty]
    private bool _isSelected;

    // Raises a caution pip on the section's rail row, reporting that the section has something to look at.
    // Set by the owning view model.
    [ObservableProperty]
    private bool _hasIssue;
}

/// <summary>
/// The shared settings layout: a rail of sections beside the selected section's content, under a heading
/// band naming it. The owner supplies the sections and holds the selection, so it decides where the
/// selected section is remembered.
/// </summary>
public sealed partial class SettingsSectionSwitcher : UserControl
{
    private readonly Dictionary<SettingsSection, ScrollViewer> _sectionContainers = new();

    // The inset around a section's content, which the heading band above it also takes.
    private readonly double _sectionInset;

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

    /// <summary>
    /// Content shown in the rail below the section rows, for a surface that carries an action belonging to
    /// the whole surface rather than to one section.
    /// </summary>
    public object? RailFooter
    {
        get => GetValue(RailFooterProperty);
        set => SetValue(RailFooterProperty, value);
    }

    public static readonly DependencyProperty RailFooterProperty =
        DependencyProperty.Register(
            nameof(RailFooter),
            typeof(object),
            typeof(SettingsSectionSwitcher),
            new PropertyMetadata(null, OnRailFooterChanged));

    public SettingsSectionSwitcher()
    {
        this.InitializeComponent();

        var resources = Application.Current.Resources;

        var panelCornerRadius = (double)resources["PanelCornerRadius"];
        SectionArea.CornerRadius = new CornerRadius(panelCornerRadius);

        var navWidth = (double)resources["SectionNavWidth"];
        NavColumn.Width = new GridLength(navWidth);

        var footerGap = (double)resources["SectionFooterGap"];
        RailFooterPresenter.Margin = new Thickness(0, footerGap, 0, 0);

        _sectionInset = (double)resources["SectionInset"];
        SectionHeader.Padding = new Thickness(_sectionInset);
    }

    /// <summary>
    /// Gives the keyboard to the rail, the switcher's own navigation, and reports whether it took it.
    /// </summary>
    public bool FocusRail()
    {
        return RailItems.Focus(FocusState.Programmatic);
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

    private static void OnRailFooterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var switcher = (SettingsSectionSwitcher)d;
        switcher.RailFooterPresenter.Content = e.NewValue;
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
                Padding = new Thickness(_sectionInset),
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
