namespace Celbridge.UserInterface.Views;

public sealed partial class HomePage : Page
{
    public HomePageViewModel ViewModel { get; }

    public HomePage()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;

        ViewModel = serviceProvider.GetRequiredService<HomePageViewModel>();

        this.DataContext(ViewModel, (page, vm) => page
            .Content(new Grid()
                .HorizontalAlignment(HorizontalAlignment.Center)
                .VerticalAlignment(VerticalAlignment.Center)
                .Children(
                    new StackPanel()
                        .Spacing(6)
                        .Children(
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
                                .Command(ViewModel.ShowProgressDialogCommand)
                                )
                            )
                        )
                    );
        }
}




