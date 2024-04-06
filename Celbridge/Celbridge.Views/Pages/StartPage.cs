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

        this.DataContext(ViewModel, (page, vm) => page
            .Content(new Grid()
                .HorizontalAlignment(HorizontalAlignment.Center)
                .VerticalAlignment(VerticalAlignment.Center)
                .Children(
                    new StackPanel()
                        .Spacing(6)
                        .Children(
                            new Button()
                                .Content(OpenWorkspace)
                                .Command(() => vm.OpenWorkspacePageCommand),
                            new Button()
                                .Content("Select file")
                                .Command(ViewModel.SelectFileCommand),
                            new Button()
                                .Content("Select folder")
                                .Command(ViewModel.SelectFolderCommand),
                            new Button()
                                .Content("Show Alert Dialog")
                                .Command(ViewModel.ShowAlertDialogCommand),
                            new Button()
                                .Content("Show Progress Dialog")
                                .Command(ViewModel.ShowProgressDialogCommand),
                            new Button()
                                .Content("Schedule Task")
                                .Command(ViewModel.ScheduleTaskCommand)
                                )
                            )
                        )
                    );
        }
}




