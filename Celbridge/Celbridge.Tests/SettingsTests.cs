using Celbridge.BaseLibrary.Core;
using Celbridge.BaseLibrary.Logging;
using Celbridge.BaseLibrary.Settings;
using Celbridge.CommonServices.Logging;
using Celbridge.CommonServices.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Tests;

public class MockApplicationSettings : IApplicationSettings
{
    private Dictionary<string, object> _settings = new Dictionary<string, object>();

    public Result SetValue<T>(string settingKey, T value) where T : notnull
    {
        _settings[settingKey] = value;
        return Result.Ok();
    }

    public Result<T> GetValue<T>(string settingKey) where T : notnull
    {
        if (_settings.TryGetValue(settingKey, out object? value))
        {
            var v = (T)value;
            if (v != null)
            {
                return Result<T>.Ok(v);
            }
        }

        return Result<T>.Fail("Failed to get value");
    }
}


[TestFixture]
public class SettingsTests
{
    private ServiceProvider? _serviceProvider;

    [SetUp]
    public void Setup()
    {
        var services = new ServiceCollection();

        services.AddSingleton<ILoggingService, LoggingService>();
        services.AddSingleton<IApplicationSettings, MockApplicationSettings>();
        services.AddSingleton<ISettingsService, SettingsService>();

        _serviceProvider = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown()
    {
        if (_serviceProvider != null)
        {
            (_serviceProvider as IDisposable)?.Dispose();
        }
    }

    public class TestValue
    {
        public string A { get; set; }
    }

    [Test]
    public void TestGetAndSet()
    {
        var settingsService = _serviceProvider!.GetRequiredService<ISettingsService>();

        // Define some data we want to store
        var testValue = new TestValue()
        {
            A = "An example value"
        };

        // Define a class externally, populate it and persist it via the settings service.
        var setResult = settingsService.SetValue("MyTestValue", testValue);
        setResult.IsSuccess.Should().BeTrue();

        var getResult = settingsService.GetValue<TestValue>("MyTestValue");
        getResult.IsSuccess.Should().BeTrue();
        getResult.Value.A.Should().BeSameAs("An example value");
    }
}
