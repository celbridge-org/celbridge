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
        _editorSettings.PreferredRegionVisibility.Should().Be(LayoutRegion.All);

        // Set a property
        _editorSettings.PreferredRegionVisibility = LayoutRegion.Primary;
        _editorSettings.PreferredRegionVisibility.Should().Be(LayoutRegion.Primary);

        // Reset the property to default
        _editorSettings.Reset();
        _editorSettings.PreferredRegionVisibility.Should().Be(LayoutRegion.All);
    }

    [Test]
    public void SettingChange_RaisesPropertyChanged()
    {
        var changedProperties = new List<string?>();
        _editorSettings.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        _editorSettings.PrimaryPanelWidth = 123f;

        changedProperties.Should().Contain(nameof(IEditorSettings.PrimaryPanelWidth));
    }
}
