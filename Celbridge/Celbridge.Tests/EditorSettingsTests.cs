using Celbridge.BaseLibrary.Settings;
using Celbridge.Settings.Services;
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

        services.AddTransient<ISettingsGroup, TempSettingsGroup>();
        services.AddSingleton<IEditorSettings, EditorSettings>();

        _serviceProvider = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {}

    [Test]
    public void ICanCanGetAndSetEditorSettings()
    {
        Guard.IsNotNull(_serviceProvider);

        var editorSettings = _serviceProvider.GetRequiredService<IEditorSettings>();

        // Check the default value system is working
        editorSettings.IsLeftPanelVisible.Should().BeTrue();

        // Set a property
        editorSettings.IsLeftPanelVisible = false;
        editorSettings.IsLeftPanelVisible.Should().BeFalse();

        // Reset the property to default
        editorSettings.Reset();
        editorSettings.IsLeftPanelVisible.Should().BeTrue();
    }
}
