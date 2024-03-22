namespace Celbridge.Views.Pages;

public sealed partial class NewProjectPage : Page
{
    public NewProjectPageViewModel ViewModel { get; }

    public LocalizedString Title => _stringLocalizer.GetString($"{nameof(NewProjectPage)}_{nameof(Title)}");

    private IStringLocalizer _stringLocalizer;

    public NewProjectPage()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;
        _stringLocalizer = serviceProvider.GetRequiredService<IStringLocalizer>();

        ViewModel = serviceProvider.GetRequiredService<NewProjectPageViewModel>();
        DataContext = ViewModel;

        this.DataContext<StartPageViewModel>((page, vm) => page
            .Content(new Grid()
                .HorizontalAlignment(HorizontalAlignment.Center)
                .VerticalAlignment(VerticalAlignment.Center)
                .Children(
                    new StackPanel()
                        .Spacing(6)
                        .Children(
                            new TextBlock()
                                .Text(Title),
                            new Button()
                                .Content("Select file")
                                .Command(ViewModel.SelectFileCommand)
                        )
                    )
                )
            );
    }
}




