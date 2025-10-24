using Celbridge.Dialog;
using Celbridge.Projects;

namespace Celbridge.UserInterface.Views;

public sealed partial class NewProjectDialog : ContentDialog, INewProjectDialog
{
    private const string InfoGlyph = "\ue946";

    private IStringLocalizer _stringLocalizer;

    public NewProjectDialogViewModel ViewModel { get; }

    public LocalizedString TitleString => _stringLocalizer.GetString($"NewProjectDialog_Title");
    public LocalizedString NewExampleProjectTitleString => _stringLocalizer.GetString($"NewExampleProjectDialog_Title");
    public LocalizedString CreateString => _stringLocalizer.GetString($"DialogButton_Create");
    public LocalizedString CancelString => _stringLocalizer.GetString($"DialogButton_Cancel");
    public LocalizedString ProjectNameString => _stringLocalizer.GetString($"NewProjectDialog_ProjectName");
    public LocalizedString ProjectNamePlaceholderString => _stringLocalizer.GetString($"NewProjectDialog_ProjectNamePlaceholder");
    public LocalizedString ProjectFolderString => _stringLocalizer.GetString($"NewProjectDialog_ProjectFolder");
    public LocalizedString ProjectFolderPlaceholderString => _stringLocalizer.GetString($"NewProjectDialog_ProjectFolderPlaceholder");
    public LocalizedString CreateSubfolderString => _stringLocalizer.GetString($"NewProjectDialog_CreateSubfolder");
    public LocalizedString CreateSubfolderTooltipString => _stringLocalizer.GetString($"NewProjectDialog_CreateSubfolderTooltip");
    public LocalizedString SaveLocationTooltipString => _stringLocalizer.GetString($"NewProjectDialog_SaveLocationTooltip");

    public NewProjectDialog(NewProjectConfigType configType = NewProjectConfigType.Standard)
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        var userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        XamlRoot = userInterfaceService.XamlRoot as XamlRoot;

        ViewModel = ServiceLocator.AcquireService<NewProjectDialogViewModel>();
        ViewModel.ConfigType = configType;

        var newProjectName = 
            new TextBox()
                .Header(new TextBlock().Text(ProjectNameString))
                .Text(x => x.Binding(() => ViewModel.ProjectName)
                    .Mode(BindingMode.TwoWay)
                    .UpdateSourceTrigger(UpdateSourceTrigger.PropertyChanged))
                .MinWidth(200)
                .PlaceholderText(ProjectNamePlaceholderString);

        var selectFolder = new Grid()
            .ColumnDefinitions("*, 50")
            .Children(
                new TextBox()
                    .Grid(column: 0)
                    .Header(new TextBlock().Text(ProjectFolderString))
                    .MinWidth(200)
                    .PlaceholderText(ProjectFolderPlaceholderString)
                    .Margin(4)
                    .IsSpellCheckEnabled(false)
                    .Text(x => x.Binding(() => ViewModel.DestFolderPath)
                        .Mode(BindingMode.TwoWay)
                        .UpdateSourceTrigger(UpdateSourceTrigger.PropertyChanged)),
                new Button()
                    .Grid(column: 1)
                    .VerticalAlignment(VerticalAlignment.Bottom)
                    .Margin(0,0,0,4)
                    .Content(new SymbolIcon(Symbol.Folder))
                    .Command(ViewModel.SelectFolderCommand)
                );

        var createSubfolder = new CheckBox()
            .HorizontalAlignment(HorizontalAlignment.Left)
            .Content(CreateSubfolderString)
            .IsChecked(x => x.Binding(() => ViewModel.CreateSubfolder).Mode(BindingMode.TwoWay))
            .ToolTipService(PlacementMode.Bottom, null, CreateSubfolderTooltipString);

        var fontFamily = ThemeResource.Get<FontFamily>("SymbolThemeFontFamily");

        var saveLocation = new StackPanel()
            .Orientation(Orientation.Horizontal)
            .Opacity(x => x.Binding(() => ViewModel.IsCreateButtonEnabled)
                .Mode(BindingMode.OneWay)
                .Convert(isEnabled => isEnabled ? 1 : 0))
            .Margin(0, 8, 0 ,0)
            .ToolTipService(PlacementMode.Top, null, SaveLocationTooltipString)
            .Children(
                new FontIcon()
                    .HorizontalAlignment(HorizontalAlignment.Left)
                    .VerticalAlignment(VerticalAlignment.Center)
                    .FontFamily(fontFamily)
                    .Glyph(InfoGlyph),
                new TextBlock()
                    .VerticalAlignment(VerticalAlignment.Center)
                    .Margin(6, 0, 0, 0)
                    .FontSize(12)
                    .Text(x => x.Binding(() => ViewModel.ProjectSaveLocation).Mode(BindingMode.OneWay))
                    .TextWrapping(TextWrapping.Wrap)
                    .Width(500)
                    .MaxWidth(500)
                    // %%% NOTE: The width above is to ensure that we don't get the wobble as the dialog resizes, but ideally I would like
                    //  the text block to wrap and resize vertically. So far I've only managed to get it to do this with significantly smaller widths. More attention is required on
                    //  this to get the desired effect.
            );

        var stackPanel = new StackPanel()
            .Orientation(Orientation.Vertical)
            .HorizontalAlignment(HorizontalAlignment.Stretch)
            .Spacing(8)
            .Children(newProjectName, selectFolder, createSubfolder, saveLocation);

        this.DataContext(ViewModel, (dialog, vm) =>
            dialog
                .Title(configType == NewProjectConfigType.Example ? NewExampleProjectTitleString : TitleString)
                .PrimaryButtonText(CreateString)
                .SecondaryButtonText(CancelString)
                .IsPrimaryButtonEnabled(x => x.Binding(() => ViewModel.IsCreateButtonEnabled).Mode(BindingMode.OneWay))
                .Content(stackPanel)
            );

        PrimaryButtonClick += CreateButtonClick;
        SecondaryButtonClick += CancelButtonClick;
    }

    private void CancelButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        Hide();
    }

    private void CreateButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ViewModel.CreateProjectCommand.Execute(null);
    }

    public async Task<Result<NewProjectConfig>> ShowDialogAsync()
    {
        var contentDialogResult = await ShowAsync();

        if (contentDialogResult == ContentDialogResult.Primary && 
            ViewModel.NewProjectConfig is not null)
        {
            return Result<NewProjectConfig>.Ok(ViewModel.NewProjectConfig);
        }

        return Result<NewProjectConfig>.Fail("Failed to create new project config");
    }
}




