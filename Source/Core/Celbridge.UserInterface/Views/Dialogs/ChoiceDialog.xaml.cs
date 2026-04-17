using Celbridge.Dialog;

namespace Celbridge.UserInterface.Views;

public sealed partial class ChoiceDialog : ContentDialog, IChoiceDialog
{
    public ChoiceDialogViewModel ViewModel { get; }

    public ChoiceDialog()
    {
        var userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        XamlRoot = userInterfaceService.XamlRoot as XamlRoot;

        ViewModel = new ChoiceDialogViewModel();

        this.InitializeComponent();

        var stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        PrimaryButtonText = stringLocalizer.GetString("DialogButton_Ok");
        SecondaryButtonText = stringLocalizer.GetString("DialogButton_Cancel");

        this.EnableThemeSync();
    }

    public void Initialize(string titleText, string messageText, IReadOnlyList<string> options, int defaultIndex, ChoiceDialogCheckbox? checkbox)
    {
        ViewModel.TitleText = titleText;
        ViewModel.MessageText = messageText;
        ViewModel.SelectedIndex = defaultIndex;

        foreach (var option in options)
        {
            OptionsRadioButtons.Items.Add(new RadioButton { Content = option });
        }

        if (defaultIndex >= 0 && defaultIndex < options.Count)
        {
            OptionsRadioButtons.SelectedIndex = defaultIndex;
        }

        if (checkbox is not null)
        {
            OptionCheckbox.Content = checkbox.Text;
            OptionCheckbox.IsChecked = checkbox.DefaultChecked;
            ViewModel.CheckboxChecked = checkbox.DefaultChecked;
            OptionCheckbox.Visibility = Visibility.Visible;
        }
    }

    private void OptionsRadioButtons_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.SelectedIndex = OptionsRadioButtons.SelectedIndex;
    }

    public async Task<Result<ChoiceDialogResult>> ShowDialogAsync()
    {
        var dialogResult = await ShowAsync();
        if (dialogResult == ContentDialogResult.Primary)
        {
            var result = new ChoiceDialogResult(ViewModel.SelectedIndex, ViewModel.CheckboxChecked);
            return Result<ChoiceDialogResult>.Ok(result);
        }

        return Result<ChoiceDialogResult>.Fail("User cancelled the choice dialog");
    }
}
