using Celbridge.Settings;
using Celbridge.UserInterface.ViewModels.Pages;

namespace Celbridge.UserInterface.Views;

/// <summary>
/// The Settings Page for configuring application preferences.
/// </summary>
public sealed partial class SettingsPage : Page
{
    private readonly Dictionary<ApplicationColorTheme, string> _themeToNameLookupDictionary = new();

    private readonly IEditorSettings _editorSettings;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IUserInterfaceService _userInterfaceService;

    private string TitleString => _stringLocalizer.GetString("SettingsPage_Title");
    private string ApplicationThemeString => _stringLocalizer.GetString("SettingsPage_ApplicationTheme");
    private string WorkshopSectionString => _stringLocalizer.GetString("SettingsPage_WorkshopSection");
    private string WorkshopUrlString => _stringLocalizer.GetString("SettingsPage_WorkshopUrl");
    private string WorkshopUrlTooltipString => _stringLocalizer.GetString("SettingsPage_WorkshopUrlTooltip");
    private string ApplicationKeyString => _stringLocalizer.GetString("SettingsPage_ApplicationKey");
    private string ApplicationKeyTooltipString => _stringLocalizer.GetString("SettingsPage_ApplicationKeyTooltip");
    private string AuthorString => _stringLocalizer.GetString("SettingsPage_Author");
    private string AuthorTooltipString => _stringLocalizer.GetString("SettingsPage_AuthorTooltip");
    private string AuthorPlaceholderString => _stringLocalizer.GetString("SettingsPage_AuthorPlaceholder");
    private string SaveConnectionString => _stringLocalizer.GetString("SettingsPage_SaveConnection");
    private string ClearConnectionString => _stringLocalizer.GetString("SettingsPage_ClearConnection");
    private string ReplaceKeyString => _stringLocalizer.GetString("SettingsPage_ReplaceKey");
    private string CancelReplaceKeyString => _stringLocalizer.GetString("SettingsPage_CancelReplaceKey");

    public SettingsPageViewModel ViewModel { get; }

    public SettingsPage()
    {
        _editorSettings = ServiceLocator.AcquireService<IEditorSettings>();
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        ViewModel = ServiceLocator.AcquireService<SettingsPageViewModel>();

        // Initialise our Theme lookup Dictionary.
        var ThemeValues = Enum.GetValues(typeof(ApplicationColorTheme));
        foreach (ApplicationColorTheme themeEntry in ThemeValues)
        {
            var name = $"Theme_" + Enum.GetName(typeof(ApplicationColorTheme), themeEntry);
            var localizedName = _stringLocalizer.GetString(name);
            if (localizedName == null)
            {
                throw new NotImplementedException("Cannot find localised string entry for '" + name + "'");
            }
            _themeToNameLookupDictionary.Add(themeEntry, localizedName);
        }

        this.InitializeComponent();

        // Set up our Theme options and default selection.
        ApplicationThemeComboBox.ItemsSource = _themeToNameLookupDictionary;
        ApplicationThemeComboBox.DisplayMemberPath = "Value";
        ApplicationThemeComboBox.SelectedValuePath = "Key";

        ApplicationThemeComboBox.Loaded += ApplicationThemeComboBox_Loaded;
        ApplicationThemeComboBox.SelectionChanged += ApplicationThemeComboBox_SelectionChanged;

        Loaded += SettingsPage_Loaded;
        Unloaded += SettingsPage_Unloaded;
    }

    private async void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
    }

    private void SettingsPage_Unloaded(object sender, RoutedEventArgs e)
    {
        ApplicationThemeComboBox.Loaded -= ApplicationThemeComboBox_Loaded;
        ApplicationThemeComboBox.SelectionChanged -= ApplicationThemeComboBox_SelectionChanged;
        Loaded -= SettingsPage_Loaded;
        Unloaded -= SettingsPage_Unloaded;
    }

    private void ApplicationThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ComboBox? comboBox = sender as ComboBox;
        if (comboBox != null && comboBox.SelectedValue != null)
        {
            ApplicationColorTheme theme = (ApplicationColorTheme)comboBox.SelectedValue;
            _editorSettings.Theme = theme;
            _userInterfaceService.ApplyCurrentTheme();
        }
    }

    private void ApplicationThemeComboBox_Loaded(object sender, RoutedEventArgs e)
    {
        // Wasn't able to set the selection directly with the values using SelectedItem due to instancing complications making
        //  it simpler to do it this way, unfortunately.
        int index = 0;
        foreach (KeyValuePair<ApplicationColorTheme, string> themeEntry in _themeToNameLookupDictionary)
        {
            if (themeEntry.Key == _editorSettings.Theme)
            {
                ApplicationThemeComboBox.SelectedIndex = index;
                return;
            }
            index++;
        }
    }
}
