using Celbridge.BaseLibrary.Settings;
using Celbridge.CommonServices.Settings;
using Celbridge.Tests.Fakes;
using CommunityToolkit.Diagnostics;

namespace Celbridge.Tests;

[TestFixture]
public class SettingsServiceTests
{
    private ISettingsService? _settingsService;

    [SetUp]
    public void Setup()
    {
        var fakeApplicationSettings = new FakeApplicationSettings();
        _settingsService = new SettingsService(fakeApplicationSettings);
    }

    [TearDown]
    public void TearDown()
    {}

    public class MySettingClass
    {
        public string A { get; set; }
    }

    [Test]
    public void ClientCanGetAndSetAValue()
    {
        Guard.IsNotNull(_settingsService);

        const string containerKey = "SomeContainer";
        const string settingKey = "SomeKey";

        // Define some data we want to store
        var mySetting = new MySettingClass()
        {
            A = "An example value"
        };

        // Set a value
        var setResult = _settingsService.SetValue(containerKey, settingKey, mySetting);
        setResult.IsSuccess.Should().BeTrue();

        // Get the value
        var getResult = _settingsService.GetValue<MySettingClass>(containerKey, settingKey);
        getResult.IsSuccess.Should().BeTrue();
        getResult.Value.A.Should().BeSameAs(mySetting.A);
    }

    [Test]
    public void ClientCantGetAnUndefinedValue()
    {
        Guard.IsNotNull(_settingsService);

        const string containerKey = "SomeContainer";
        const string settingKey = "SomeKey";

        // Get the value
        var getResult = _settingsService.GetValue<MySettingClass>(containerKey, settingKey);
        getResult.IsFailure.Should().BeTrue();
    }
}
