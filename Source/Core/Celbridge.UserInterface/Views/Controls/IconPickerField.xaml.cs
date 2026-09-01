using Celbridge.Dialog;
using Celbridge.UserInterface.Helpers;
using Microsoft.Extensions.Localization;

namespace Celbridge.UserInterface.Views.Controls;

/// <summary>
/// The field a user names an icon in, used by every surface that asks for one so they cannot disagree
/// about which names are accepted or what a name draws.
/// </summary>
public sealed partial class IconPickerField : UserControl
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IIconService _iconService;
    private readonly IDialogService _dialogService;

    public static readonly DependencyProperty IconNameProperty = DependencyProperty.Register(
        nameof(IconName),
        typeof(string),
        typeof(IconPickerField),
        new PropertyMetadata(string.Empty, OnFieldStateChanged));

    public static readonly DependencyProperty DefaultSymbolProperty = DependencyProperty.Register(
        nameof(DefaultSymbol),
        typeof(IconSymbol?),
        typeof(IconPickerField),
        new PropertyMetadata(null, OnFieldStateChanged));

    /// <summary>
    /// The prefixed icon name the field holds, empty when the user has named none.
    /// </summary>
    public string IconName
    {
        get => (string?)GetValue(IconNameProperty) ?? string.Empty;
        set => SetValue(IconNameProperty, value);
    }

    /// <summary>
    /// The icon previewed while the field is empty, which is the glyph the surface itself draws for a
    /// blank field. Leave it unset where a blank field means no glyph at all.
    /// </summary>
    public IconSymbol? DefaultSymbol
    {
        get => (IconSymbol?)GetValue(DefaultSymbolProperty);
        set => SetValue(DefaultSymbolProperty, value);
    }

    public string LabelString => _stringLocalizer.GetString("IconPicker_FieldLabel");
    public string PlaceholderString => _stringLocalizer.GetString("IconPicker_FieldPlaceholder");
    public string HintString => _stringLocalizer.GetString("IconPicker_FieldHint");
    public string UnknownIconString => _stringLocalizer.GetString("IconPicker_UnknownIcon");
    public string BrowseTooltipString => _stringLocalizer.GetString("IconPicker_BrowseTooltip");

    public IconPickerField()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _iconService = ServiceLocator.AcquireService<IIconService>();
        _dialogService = ServiceLocator.AcquireService<IDialogService>();

        InitializeComponent();

        UpdateIconState();
    }

    private static void OnFieldStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is IconPickerField field)
        {
            field.UpdateIconState();
        }
    }

    // The preview and the warning are set on the elements rather than bound, so refreshing them cannot
    // push the field's committed value back over what the user is part way through typing.
    private void UpdateIconState()
    {
        var iconName = IconName.Trim();

        // Resolved the way the surface resolves it, so the preview shows the glyph the surface will draw.
        var previewIconName = IconNameResolver.Resolve(_iconService, iconName, DefaultSymbol);

        PreviewIcon.IconName = previewIconName;
        PreviewIcon.Visibility = previewIconName.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        // A blank field is unconfigured rather than wrong, so it does not report as unsupported.
        var isIconUnknown = iconName.Length > 0 && !_iconService.IsSupportedIcon(iconName);
        UnknownIconWarning.Visibility = isIconUnknown ? Visibility.Visible : Visibility.Collapsed;
    }

    private void IconTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        FocusNavigationHelper.CommitFieldOnEnter(sender, e);
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        _ = PickIconAsync();
    }

    private async Task PickIconAsync()
    {
        var pickResult = await _dialogService.ShowIconPickerDialogAsync(IconName.Trim());

        // The dialog reports a dismissal as a failure, so the field keeps the icon it had.
        if (pickResult.IsFailure)
        {
            return;
        }

        IconName = pickResult.Value;
    }
}
