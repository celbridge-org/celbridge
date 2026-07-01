using Celbridge.Settings;
using Celbridge.Settings.Services;
using Celbridge.Tests.Helpers;
using Celbridge.Workspace;

namespace Celbridge.Tests.Settings;

/// <summary>
/// Covers the descriptor-routed SettingsService for the Application and Workspace
/// scopes: defaults, write-through reads, presence checks, and reset.
/// Protected-scope behaviour lives in ProtectedScopeTests.
/// </summary>
[TestFixture]
public class SettingsServiceTests
{
    private static readonly SettingDescriptor<int> TestApplicationSetting =
        new("Test.ApplicationSetting", SettingScope.Application, 42);

    private static readonly SettingDescriptor<float> TestWorkspaceSetting =
        new("Test.WorkspaceSetting", SettingScope.Workspace, 1.5f);

    private FakeSettingsStore _settingsStore = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private SettingsService _settingsService = null!;

    [SetUp]
    public void Setup()
    {
        _settingsStore = new FakeSettingsStore();

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.IsWorkspacePageLoaded.Returns(false);

        _settingsService = new SettingsService(
            new NullLogger<SettingsService>(),
            _settingsStore,
            new FakeCredentialStore(),
            _workspaceWrapper);
    }

    [Test]
    public void Get_UnconfiguredApplicationSetting_ReturnsDefault()
    {
        _settingsService.Get(TestApplicationSetting).Should().Be(42);
    }

    [Test]
    public void SetThenGet_ApplicationSetting_ReturnsStoredValue()
    {
        _settingsService.Set(TestApplicationSetting, 7);

        _settingsService.Get(TestApplicationSetting).Should().Be(7);
    }

    [Test]
    public void IsConfigured_FalseByDefault_TrueAfterSet_FalseAfterReset()
    {
        _settingsService.IsConfigured(TestApplicationSetting).Should().BeFalse();

        _settingsService.Set(TestApplicationSetting, 7);
        _settingsService.IsConfigured(TestApplicationSetting).Should().BeTrue();

        _settingsService.Reset(TestApplicationSetting);
        _settingsService.IsConfigured(TestApplicationSetting).Should().BeFalse();
    }

    [Test]
    public void Reset_RestoresDefault()
    {
        _settingsService.Set(TestApplicationSetting, 7);

        _settingsService.Reset(TestApplicationSetting);

        _settingsService.Get(TestApplicationSetting).Should().Be(42);
    }

    [Test]
    public void TryGet_ApplicationSetting_AlwaysSucceeds()
    {
        var defaultResult = _settingsService.TryGet(TestApplicationSetting);
        defaultResult.IsSuccess.Should().BeTrue();
        defaultResult.Value.Should().Be(42);

        _settingsService.Set(TestApplicationSetting, 7);

        var storedResult = _settingsService.TryGet(TestApplicationSetting);
        storedResult.IsSuccess.Should().BeTrue();
        storedResult.Value.Should().Be(7);
    }

    [Test]
    public void IsScopeAvailable_Application_IsAlwaysTrue()
    {
        _settingsService.IsScopeAvailable(SettingScope.Application).Should().BeTrue();
    }

    [Test]
    public void WorkspaceScope_NoWorkspaceLoaded_IsUnavailableAndReadsDefault()
    {
        _settingsService.IsScopeAvailable(SettingScope.Workspace).Should().BeFalse();
        _settingsService.Get(TestWorkspaceSetting).Should().Be(1.5f);
        _settingsService.IsConfigured(TestWorkspaceSetting).Should().BeFalse();
    }

    [Test]
    public void WorkspaceScope_StoreAttached_RoutesToStore()
    {
        var store = new FakeSettingsStore();
        AttachWorkspaceStore(store);

        _settingsService.IsScopeAvailable(SettingScope.Workspace).Should().BeTrue();

        _settingsService.Set(TestWorkspaceSetting, 3.5f);
        _settingsService.Get(TestWorkspaceSetting).Should().Be(3.5f);
        _settingsService.IsConfigured(TestWorkspaceSetting).Should().BeTrue();

        _settingsService.Reset(TestWorkspaceSetting);
        _settingsService.Get(TestWorkspaceSetting).Should().Be(1.5f);
    }

    // Wires the workspace hub so that the settings service resolves the given
    // store as the live per-project store.
    private void AttachWorkspaceStore(ISettingsStore store)
    {
        var settingsService = Substitute.For<IWorkspaceSettingsService>();
        settingsService.WorkspaceSettingsStore.Returns(store);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.WorkspaceSettings.Returns(settingsService);

        _workspaceWrapper.IsWorkspacePageLoaded.Returns(true);
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);
    }
}
