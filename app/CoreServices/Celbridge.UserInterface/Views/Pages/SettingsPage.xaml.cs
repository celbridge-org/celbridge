using Celbridge.Projects;
using Celbridge.UserInterface.Models;
using Celbridge.UserInterface.ViewModels.Pages;
using System.Runtime.CompilerServices;
using Celbridge.Entities;
using Celbridge.Settings;
using Celbridge.UserInterface;
using Microsoft.Extensions.Localization;

namespace Celbridge.UserInterface.Views;

/// <summary>
/// The very beginnings of the Settings Page.
/// </summary>
public sealed partial class SettingsPage : PersistentPage
{
    Dictionary<UserInterfaceTheme, string> ThemeToNameLookupDictionary = new();

    private readonly IEditorSettings _editorSettings;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IUserInterfaceService _userInterfaceService;

    public SettingsPage()
    {
        _editorSettings = ServiceLocator.AcquireService<IEditorSettings>();
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();

        // Initialise our Theme lookup Dictionary.
        var ThemeValues = Enum.GetValues(typeof(UserInterfaceTheme));
        foreach (UserInterfaceTheme themeEntry in ThemeValues)
        {
            string name = $"Theme_" + Enum.GetName(typeof(UserInterfaceTheme), themeEntry);
            var localizedName = _stringLocalizer.GetString(name);
            if (localizedName == null)
            {
                throw new NotImplementedException("Cannot find localised string entry for '" + name + "'");
            }
            ThemeToNameLookupDictionary.Add(themeEntry, localizedName);
        }

        Persistence = PersistenceLevel.Eternal;

        this.InitializeComponent();

        // Set up our Theme options and default selection.
        ApplicationThemeComboBox.ItemsSource = ThemeToNameLookupDictionary;
        ApplicationThemeComboBox.DisplayMemberPath = "Value";
        ApplicationThemeComboBox.SelectedValuePath = "Key";

        // %%% Need to Unload these.
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
            UserInterfaceTheme theme = (UserInterfaceTheme)comboBox.SelectedValue;
            _userInterfaceService.UserInterfaceTheme = theme;
            _editorSettings.Theme = theme;
        }
    }

    private void ApplicationThemeComboBox_Loaded(object sender, RoutedEventArgs e)
    {
        // Wasn't able to set the selection directly with the values using SelectedItem due to instancing complications making
        //  it simpler to do it this way, unfortunately.
        int index = 0;
        foreach (KeyValuePair<UserInterfaceTheme, string> themeEntry in ThemeToNameLookupDictionary)
        {
            if (themeEntry.Key == _userInterfaceService.UserInterfaceTheme)
            {
                ApplicationThemeComboBox.SelectedIndex = index;
                return;
            }
            index++;
        }
    }
}
