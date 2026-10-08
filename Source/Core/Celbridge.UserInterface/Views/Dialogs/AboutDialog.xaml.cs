using Celbridge.Dialog;

namespace Celbridge.UserInterface.Views;

public sealed partial class AboutDialog : ContentDialog, IAboutDialog
{
    private readonly IStringLocalizer _stringLocalizer;

    public AboutDialogViewModel ViewModel { get; }

    public string TitleString => _stringLocalizer.GetString("Menu_About");
    public string OkString => _stringLocalizer.GetString("DialogButton_Ok");
    public string WebsiteString => _stringLocalizer.GetString("Menu_About_Website");
    public string GitHubString => _stringLocalizer.GetString("Menu_About_GitHub");

    public AboutDialog()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        var userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        XamlRoot = userInterfaceService.XamlRoot as XamlRoot;

        ViewModel = ServiceLocator.AcquireService<AboutDialogViewModel>();

        this.InitializeComponent();

        this.EnableThemeSync();
    }

    public async Task ShowDialogAsync()
    {
        await ShowAsync();
    }

    private void WebsiteLink_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenWebsite();
    }

    private void GitHubLink_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenGitHub();
    }
}
