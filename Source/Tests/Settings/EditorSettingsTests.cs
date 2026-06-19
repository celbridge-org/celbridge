using Celbridge.Settings;
using Celbridge.Settings.Services;
using Celbridge.Tests.Helpers;
using Celbridge.Workspace;

namespace Celbridge.Tests.Settings;

[TestFixture]
public class EditorSettingsTests
{
    private IEditorSettings _editorSettings = null!;

    [SetUp]
    public void Setup()
    {
        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.IsWorkspacePageLoaded.Returns(false);

        var settingsService = new SettingsService(
            new NullLogger<SettingsService>(),
            new InMemorySettingsStore(),
            new FakeCredentialProtector(),
            workspaceWrapper);

        _editorSettings = new EditorSettings(settingsService);
    }

    [Test]
    public void ICanCanGetAndSetEditorSettings()
    {
        // Check the default value system is working
        _editorSettings.Theme.Should().Be(ApplicationColorTheme.System);

        // Set a property
        _editorSettings.Theme = ApplicationColorTheme.Dark;
        _editorSettings.Theme.Should().Be(ApplicationColorTheme.Dark);

        // Reset the property to default
        _editorSettings.Reset();
        _editorSettings.Theme.Should().Be(ApplicationColorTheme.System);
    }

    [Test]
    public void SettingChange_RaisesPropertyChanged()
    {
        var changedProperties = new List<string?>();
        _editorSettings.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        _editorSettings.PreferredWindowWidth = 123;

        changedProperties.Should().Contain(nameof(IEditorSettings.PreferredWindowWidth));
    }
}
