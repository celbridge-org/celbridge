using Celbridge.BaseLibrary.Settings;
using Celbridge.CommonServices.Settings;
using Celbridge.Tests.Fakes;
using CommunityToolkit.Diagnostics;
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

        services.AddTransient<ISettingsGroup, FakeSettingsGroup>();
        services.AddSingleton<IEditorSettings, EditorSettings>();

        _serviceProvider = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {}

    [Test]
    public void ICanCanGetAndSetTheApplicationTheme()
    {
        Guard.IsNotNull(_serviceProvider);

        var editorSettings = _serviceProvider.GetRequiredService<IEditorSettings>();

        // Get the default value
        editorSettings.Theme.Should().Be(ApplicationColorTheme.Light.ToString());

        // Set a new value
        editorSettings.Theme = ApplicationColorTheme.Dark.ToString();
        editorSettings.Theme.Should().Be(ApplicationColorTheme.Dark.ToString());

        // Reset the settings
        editorSettings.Reset();
        editorSettings.Theme.Should().Be(ApplicationColorTheme.Light.ToString());

        // Check the default value system is working
        editorSettings.LeftPanelExpanded.Should().BeTrue();
    }
}
