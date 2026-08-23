using Celbridge.Documents;
using Celbridge.ProjectSettings.ViewModels;
using Celbridge.Resources;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Views.Controls;
using Microsoft.Extensions.Localization;

namespace Celbridge.ProjectSettings.Views;

/// <summary>
/// The Project Settings editor: a document view over the project's .celbridge file presenting its settings
/// as a rail of sections. Each section writes its edits straight through to the file, so this view carries
/// no buffer, never reports unsaved changes, and takes no part in the document save tick.
/// </summary>
public sealed partial class ProjectSettingsEditorView : UserControl, IDocumentView
{
    private readonly IStringLocalizer _stringLocalizer;

    public ProjectSettingsEditorViewModel ViewModel { get; }

    public string ReloadProjectText => _stringLocalizer.GetString("ProjectSettings_ReloadProject");
    public string ReloadCaptionText => _stringLocalizer.GetString("ProjectSettings_ReloadCaption");
    public string ConfigErrorTitleText => _stringLocalizer.GetString("ProjectSettings_ConfigErrorTitle");
    public string ConfigErrorCaptionText => _stringLocalizer.GetString("ProjectSettings_ConfigErrorCaption");
    public string OpenInCodeEditorText => _stringLocalizer.GetString("ProjectSettings_OpenInCodeEditor");

    public ProjectSettingsEditorView()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        ViewModel = ServiceLocator.AcquireService<ProjectSettingsEditorViewModel>();
        ViewModel.InitializeSections(BuildSections());

        InitializeComponent();

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ApplyReloadButtonEmphasis();
    }

    // The sections in rail order. The keys are persisted, so changing one drops the section a returning
    // user had open.
    private List<SettingsSection> BuildSections()
    {
        var informationView = new InformationSectionView
        {
            ViewModel = ViewModel.InformationSection
        };

        var packagesView = new PackagesSectionView
        {
            ViewModel = ViewModel.PackagesSection
        };

        var pagesView = new PagesSectionView
        {
            ViewModel = ViewModel.PagesSection
        };

        var fileEditorsView = new FileEditorsSectionView
        {
            ViewModel = ViewModel.FileEditorsSection
        };

        var featureFlagsView = new FeatureFlagsSectionView
        {
            ViewModel = ViewModel.FeatureFlagsSection
        };

        var sections = new List<SettingsSection>
        {
            new(
                "Information",
                "bs-info-circle",
                _stringLocalizer.GetString("ProjectSettings_InformationHeader"),
                _stringLocalizer.GetString("ProjectSettings_InformationDescription"),
                informationView),
            new(
                "Packages",
                "bs-box-seam",
                _stringLocalizer.GetString("ProjectSettings_PackagesHeader"),
                _stringLocalizer.GetString("ProjectSettings_PackagesDescription"),
                packagesView,
                ProjectSettingsLabels.PackagesSectionIssue),
            new(
                "Pages",
                "bs-globe",
                _stringLocalizer.GetString("ProjectSettings_PagesHeader"),
                _stringLocalizer.GetString("ProjectSettings_PagesDescription"),
                pagesView),
            new(
                "FileEditors",
                "bs-file-earmark",
                _stringLocalizer.GetString("ProjectSettings_FileEditorsHeader"),
                _stringLocalizer.GetString("ProjectSettings_FileEditorsDescription"),
                fileEditorsView),
            new(
                "FeatureFlags",
                "bs-flag",
                _stringLocalizer.GetString("ProjectSettings_FeatureFlagsHeader"),
                _stringLocalizer.GetString("ProjectSettings_FeatureFlagsDescription"),
                featureFlagsView),
        };

        return sections;
    }

    private void OpenInCodeEditorButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenInCodeEditor(FileResource);
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectSettingsEditorViewModel.HasPendingChanges))
        {
            ApplyReloadButtonEmphasis();
        }
    }

    // A Style cannot be bound in XAML without a converter, so the swap happens here.
    private void ApplyReloadButtonEmphasis()
    {
        var styleKey = ViewModel.HasPendingChanges ? "AccentButtonStyle" : "DefaultButtonStyle";
        if (Application.Current.Resources.TryGetValue(styleKey, out var style)
            && style is Style buttonStyle)
        {
            ReloadProjectButton.Style = buttonStyle;
        }
    }

    public ResourceKey FileResource { get; private set; }

    public EditorId EditorId { get; set; }

    public async Task<Result> SetFileResource(ResourceKey fileResource)
    {
        await Task.CompletedTask;

        // Existence is already checked by the documents service before the view is created, and nothing
        // here resolves the resource through the registry.
        FileResource = fileResource;

        return Result.Ok();
    }

    public async Task<Result> LoadContent()
    {
        await Task.CompletedTask;

        ViewModel.Refresh();

        return Result.Ok();
    }

    public bool HasUnsavedChanges => false;

    public Result<bool> UpdateSaveTimer(double deltaTime)
    {
        return false;
    }

    public async Task<Result> SaveDocument()
    {
        await Task.CompletedTask;

        return Result.Ok();
    }

    public WritableState WritableState { get; private set; } = WritableState.Writable;

    public void SetWritableState(WritableState state)
    {
        WritableState = state;
    }

    public async Task<Result> NavigateToLocation(string location)
    {
        await Task.CompletedTask;

        return Result.Ok();
    }

    public void FocusDocument()
    {
        Focus(FocusState.Programmatic);
    }

    public async Task<bool> CanClose()
    {
        await Task.CompletedTask;

        return true;
    }

    public async Task PrepareToClose()
    {
        await Task.CompletedTask;

        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    public async Task<string?> TrySaveEditorStateAsync()
    {
        await Task.CompletedTask;

        // The selected section is persisted to workspace settings by the view model, which restores it for
        // a new project as well as a restored tab, so there is no per-tab state to carry.
        return null;
    }

    public async Task RestoreEditorStateAsync(string state)
    {
        await Task.CompletedTask;
    }
}
