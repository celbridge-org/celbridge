using System.ComponentModel;
using Celbridge.Settings;
using Celbridge.UserInterface.ViewModels.Controls;
using Microsoft.Extensions.Localization;

namespace Celbridge.UserInterface.Views.Controls;

/// <summary>
/// A field for entering a secret, for example an API key. The field stores the secret in a Protected
/// setting, and never shows the secret again.
/// </summary>
public sealed partial class SecretField : UserControl
{
    private readonly IStringLocalizer _stringLocalizer;

    public static readonly DependencyProperty SettingProperty = DependencyProperty.Register(
        nameof(Setting),
        typeof(SettingDescriptor<string>),
        typeof(SecretField),
        new PropertyMetadata(null, OnSettingChanged));

    /// <summary>
    /// The Protected setting where the field stores its secret.
    /// </summary>
    public SettingDescriptor<string>? Setting
    {
        get => (SettingDescriptor<string>?)GetValue(SettingProperty);
        set => SetValue(SettingProperty, value);
    }

    public SecretFieldViewModel ViewModel { get; }

    private string SetString => _stringLocalizer.GetString("SecretField_Set");
    private string ChangeString => _stringLocalizer.GetString("SecretField_Change");
    private string RemoveString => _stringLocalizer.GetString("SecretField_Remove");
    private string SaveString => _stringLocalizer.GetString("SecretField_Save");
    private string RemoveMessageString => _stringLocalizer.GetString("SecretField_RemoveMessage");
    private string CancelString => _stringLocalizer.GetString("DialogButton_Cancel");

    public SecretField()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        ViewModel = ServiceLocator.AcquireService<SecretFieldViewModel>();

        InitializeComponent();

        // A settings dialog swaps its sections in and out, so the field loads the stored state each time
        // the field appears.
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private static void OnSettingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SecretField field
            && field.IsLoaded)
        {
            field.LoadSetting();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // The bindings subscribe to the view model while the control loads. Subscribing after them means the
        // entry field is already visible when the view focuses the password box.
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        LoadSetting();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
    }

    private void LoadSetting()
    {
        var setting = Setting;
        if (setting is not null)
        {
            ViewModel.Initialize(setting);
        }
    }

    // The password box is unbound, so the XAML never reads the secret back. The view clears the box and
    // focuses it when the entry field appears.
    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ViewModel.IsEditVisible)
            || !ViewModel.IsEditVisible)
        {
            return;
        }

        SecretPasswordBox.Password = string.Empty;
        SecretPasswordBox.Focus(FocusState.Programmatic);
    }

    private void SecretPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        ViewModel.SecretInput = SecretPasswordBox.Password;
    }

    private void SecretPasswordBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter
            && ViewModel.IsSaveEnabled)
        {
            ViewModel.SaveCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            ViewModel.CancelChangeCommand.Execute(null);
            e.Handled = true;
        }
    }
}
