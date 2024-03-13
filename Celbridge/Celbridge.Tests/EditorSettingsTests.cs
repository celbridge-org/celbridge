using Celbridge.BaseLibrary.Settings;
using Celbridge.Services.Settings;
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
        editorSettings.LeftPanelVisible.Should().BeTrue();

        // Set a property
        editorSettings.LeftPanelVisible = false;
        editorSettings.LeftPanelVisible.Should().BeFalse();

        // Reset the property to default
        editorSettings.Reset();
        editorSettings.LeftPanelVisible.Should().BeTrue();
    }
}
