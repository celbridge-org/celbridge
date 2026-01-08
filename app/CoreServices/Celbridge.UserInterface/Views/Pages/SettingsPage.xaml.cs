using Celbridge.Settings;

namespace Celbridge.UserInterface.Views;

/// <summary>
/// The very beginnings of the Settings Page.
/// </summary>
public sealed partial class SettingsPage : PersistentPage
{
    private readonly Dictionary<ApplicationColorTheme, string> _themeToNameLookupDictionary = new();

    private readonly IEditorSettings _editorSettings;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IUserInterfaceService _userInterfaceService;
    private string TitleString => _stringLocalizer.GetString($"SettingsPage_Title");
    private string ApplicationThemeString => _stringLocalizer.GetString($"SettingsPage_ApplicationTheme");

    public SettingsPage()
    {
        _editorSettings = ServiceLocator.AcquireService<IEditorSettings>();
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();

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

        Persistence = PersistenceLevel.Eternal;

        this.InitializeComponent();

        // Set up our Theme options and default selection.
        ApplicationThemeComboBox.ItemsSource = _themeToNameLookupDictionary;
        ApplicationThemeComboBox.DisplayMemberPath = "Value";
        ApplicationThemeComboBox.SelectedValuePath = "Key";

        ApplicationThemeComboBox.Loaded += ApplicationThemeComboBox_Loaded;
        ApplicationThemeComboBox.SelectionChanged += ApplicationThemeComboBox_SelectionChanged;
    }

    public override void PageUnloadInternal()
    {
        ApplicationThemeComboBox.Loaded -= ApplicationThemeComboBox_Loaded;
        ApplicationThemeComboBox.SelectionChanged -= ApplicationThemeComboBox_SelectionChanged;
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
