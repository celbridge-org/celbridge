using Celbridge.Settings;
using Celbridge.Settings.Services;
using Celbridge.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Tests;

[TestFixture]
public class EditorSettingsTests
{
    private ServiceProvider? _serviceProvider;

    [SetUp]
    public void Setup()
    {
        var services = new ServiceCollection();

        services.AddTransient<ISettingsGroup, TempSettingsGroup>();
        services.AddSingleton<IEditorSettings, EditorSettings>();

        _serviceProvider = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    { }

    [Test]
    public void ICanCanGetAndSetEditorSettings()
    {
        Guard.IsNotNull(_serviceProvider);

        var editorSettings = _serviceProvider.GetRequiredService<IEditorSettings>();

        // Check the default value system is working
        editorSettings.PreferredRegionVisibility.Should().Be(LayoutRegion.All);

        // Set a property
        editorSettings.PreferredRegionVisibility = LayoutRegion.Primary;
        editorSettings.PreferredRegionVisibility.Should().Be(LayoutRegion.Primary);

        // Reset the property to default
        editorSettings.Reset();
        editorSettings.PreferredRegionVisibility.Should().Be(LayoutRegion.All);
    }
}
