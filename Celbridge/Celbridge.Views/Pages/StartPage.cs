namespace Celbridge.Views.Pages;

public sealed partial class StartPage : Page
{
    public StartPageViewModel ViewModel { get; }

    public LocalizedString OpenWorkspace => _stringLocalizer.GetString($"{nameof(StartPage)}_{nameof(OpenWorkspace)}");

    private IStringLocalizer _stringLocalizer;

    public StartPage()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;
        _stringLocalizer = serviceProvider.GetRequiredService<IStringLocalizer>();

        ViewModel = serviceProvider.GetRequiredService<StartPageViewModel>();
        DataContext = ViewModel;

        this.DataContext<StartPageViewModel>((page, vm) => page
            .Content(new Grid()
                .HorizontalAlignment(HorizontalAlignment.Center)
                .Children(
                    new Button()
                        .Content(OpenWorkspace)
                        .Command(() => vm.OpenWorkspacePageCommand)
            )));
    }
}




