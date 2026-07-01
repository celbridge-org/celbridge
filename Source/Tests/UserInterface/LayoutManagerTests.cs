using Celbridge.Messaging;
using Celbridge.Messaging.Services;
using Celbridge.Settings;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Services;
using Celbridge.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Tests.UserInterface;

[TestFixture]
public class LayoutManagerTests
{
    private ServiceProvider? _serviceProvider;
    private IMessengerService _messengerService = null!;
    private ISettingsService _settingsService = null!;
    private IBindableWorkspaceSettings _workspaceSettings = null!;
    private LayoutManager _layoutManager = null!;

    [SetUp]
    public void Setup()
    {
        var services = new ServiceCollection();

        Logging.ServiceConfiguration.ConfigureServices(services);
        services.AddSingleton<IMessengerService, MessengerService>();

        _serviceProvider = services.BuildServiceProvider();

        _messengerService = _serviceProvider.GetRequiredService<IMessengerService>();
        _settingsService = Substitute.For<ISettingsService>();

        // Panel layout is Workspace-scoped, so it is read from and written to the
        // workspace settings facade reached through the workspace wrapper.
        _workspaceSettings = Substitute.For<IBindableWorkspaceSettings>();

        // Default to all panels visible. Set the value (rather than stubbing the
        // getter) so writes by the layout manager are reflected on subsequent reads.
        _workspaceSettings.PreferredRegionVisibility = LayoutRegion.All;

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceWrapper.IsWorkspacePageLoaded.Returns(true);
        workspaceWrapper.WorkspaceService.Returns(workspaceService);
        workspaceService.BindableWorkspaceSettings.Returns(_workspaceSettings);

        var logger = _serviceProvider.GetRequiredService<ILogger<LayoutManager>>();
        var featureFlags = Substitute.For<IFeatureFlags>();

        // Default to console panel feature enabled for tests
        featureFlags.IsEnabled(FeatureFlagConstants.ConsolePanel).Returns(true);

        _layoutManager = new LayoutManager(logger, _messengerService, _settingsService, workspaceWrapper, featureFlags);
    }

    [TearDown]
    public void TearDown()
    {
        (_serviceProvider as IDisposable)?.Dispose();
    }

    [Test]
    public void InitialState_LayoutModeIsDefault()
    {
        _layoutManager.LayoutMode.Should().Be(LayoutMode.Default);
    }

    [Test]
    public void InitialState_IsFullScreenIsFalse()
    {
        _layoutManager.IsFullScreen.Should().BeFalse();
    }

    [Test]
    public void InitialState_RegionVisibilityIsAllByDefault()
    {
        _layoutManager.RegionVisibility.Should().Be(LayoutRegion.All);
    }

    [Test]
    public void InitialState_AllPanelsAreVisible()
    {
        _layoutManager.IsContextPanelVisible.Should().BeTrue();
        _layoutManager.IsInspectorPanelVisible.Should().BeTrue();
        _layoutManager.IsConsolePanelVisible.Should().BeTrue();
    }

    [Test]
    public void TransitionToFocus_FromDefault_HidesSidePanels()
    {
        var result = _layoutManager.RequestLayoutTransition(LayoutTransition.Focus);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.LayoutMode.Should().Be(LayoutMode.Focus);
        _layoutManager.RegionVisibility.Should().Be(LayoutRegion.None);

        // Fullscreen is independent of the layout mode.
        _layoutManager.IsFullScreen.Should().BeFalse();
    }

    [Test]
    public void TransitionToPresentation_FromDefault_HidesSidePanels()
    {
        var result = _layoutManager.RequestLayoutTransition(LayoutTransition.Presentation);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.LayoutMode.Should().Be(LayoutMode.Presentation);
        _layoutManager.RegionVisibility.Should().Be(LayoutRegion.None);
        _layoutManager.IsFullScreen.Should().BeFalse();
    }

    [Test]
    public void TransitionToDefault_FromFocus_RestoresPreferredRegionVisibility()
    {
        _layoutManager.RequestLayoutTransition(LayoutTransition.Focus);

        var result = _layoutManager.RequestLayoutTransition(LayoutTransition.Default);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.LayoutMode.Should().Be(LayoutMode.Default);
        _layoutManager.RegionVisibility.Should().Be(LayoutRegion.All);
    }

    [Test]
    public void TransitionToSameMode_Succeeds()
    {
        _layoutManager.RequestLayoutTransition(LayoutTransition.Focus);

        var result = _layoutManager.RequestLayoutTransition(LayoutTransition.Focus);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.LayoutMode.Should().Be(LayoutMode.Focus);
    }

    [Test]
    public void ToggleFocus_FromDefault_EntersFocus()
    {
        var result = _layoutManager.RequestLayoutTransition(LayoutTransition.ToggleFocus);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.LayoutMode.Should().Be(LayoutMode.Focus);
        _layoutManager.RegionVisibility.Should().Be(LayoutRegion.None);
    }

    [Test]
    public void ToggleFocus_FromFocus_ReturnsToDefault()
    {
        _layoutManager.RequestLayoutTransition(LayoutTransition.Focus);

        var result = _layoutManager.RequestLayoutTransition(LayoutTransition.ToggleFocus);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.LayoutMode.Should().Be(LayoutMode.Default);
    }

    [Test]
    public void ToggleFocus_FromPresentation_ReturnsToDefault()
    {
        _layoutManager.RequestLayoutTransition(LayoutTransition.Presentation);

        var result = _layoutManager.RequestLayoutTransition(LayoutTransition.ToggleFocus);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.LayoutMode.Should().Be(LayoutMode.Default);
    }

    [Test]
    public void ToggleFullScreen_FromWindowed_EntersFullScreen()
    {
        var result = _layoutManager.RequestLayoutTransition(LayoutTransition.ToggleFullScreen);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.IsFullScreen.Should().BeTrue();

        // The layout mode is unaffected by the fullscreen toggle.
        _layoutManager.LayoutMode.Should().Be(LayoutMode.Default);
    }

    [Test]
    public void ToggleFullScreen_Twice_ReturnsToWindowed()
    {
        _layoutManager.RequestLayoutTransition(LayoutTransition.ToggleFullScreen);

        var result = _layoutManager.RequestLayoutTransition(LayoutTransition.ToggleFullScreen);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.IsFullScreen.Should().BeFalse();
    }

    [Test]
    public void ToggleFullScreen_DoesNotChangeLayoutMode()
    {
        _layoutManager.RequestLayoutTransition(LayoutTransition.Focus);

        _layoutManager.RequestLayoutTransition(LayoutTransition.ToggleFullScreen);

        _layoutManager.LayoutMode.Should().Be(LayoutMode.Focus);
        _layoutManager.IsFullScreen.Should().BeTrue();
    }

    [Test]
    public void LayoutModeChange_DoesNotChangeFullScreen()
    {
        _layoutManager.RequestLayoutTransition(LayoutTransition.ToggleFullScreen);

        _layoutManager.RequestLayoutTransition(LayoutTransition.Focus);

        _layoutManager.IsFullScreen.Should().BeTrue();
        _layoutManager.LayoutMode.Should().Be(LayoutMode.Focus);
    }

    [Test]
    public void SetRegionVisibility_HideSinglePanel_UpdatesVisibility()
    {
        _layoutManager.SetRegionVisibility(LayoutRegion.Primary, false);

        _layoutManager.IsContextPanelVisible.Should().BeFalse();
        _layoutManager.IsInspectorPanelVisible.Should().BeTrue();
        _layoutManager.IsConsolePanelVisible.Should().BeTrue();
    }

    [Test]
    public void SetRegionVisibility_ShowHiddenPanel_UpdatesVisibility()
    {
        _layoutManager.SetRegionVisibility(LayoutRegion.Console, false);
        _layoutManager.SetRegionVisibility(LayoutRegion.Console, true);

        _layoutManager.IsConsolePanelVisible.Should().BeTrue();
    }

    [Test]
    public void ToggleRegionVisibility_TogglesPanel()
    {
        _layoutManager.IsContextPanelVisible.Should().BeTrue();

        _layoutManager.ToggleRegionVisibility(LayoutRegion.Primary);

        _layoutManager.IsContextPanelVisible.Should().BeFalse();

        _layoutManager.ToggleRegionVisibility(LayoutRegion.Primary);

        _layoutManager.IsContextPanelVisible.Should().BeTrue();
    }

    [Test]
    public void SetRegionVisibility_InFocusMode_ReturnsToDefault()
    {
        _layoutManager.RequestLayoutTransition(LayoutTransition.Focus);

        // Manually showing a panel means the user is customizing the layout, so the mode returns to
        // Default rather than staying in Focus with a panel visible.
        _layoutManager.SetRegionVisibility(LayoutRegion.Primary, true);

        _layoutManager.LayoutMode.Should().Be(LayoutMode.Default);
        _layoutManager.IsContextPanelVisible.Should().BeTrue();
    }

    [Test]
    public void SetRegionVisibility_SameState_NoChange()
    {
        bool messageReceived = false;
        var recipient = new object();
        _messengerService.Register<RegionVisibilityChangedMessage>(recipient, (r, m) => messageReceived = true);

        _layoutManager.SetRegionVisibility(LayoutRegion.Primary, true);

        messageReceived.Should().BeFalse();
    }

    [Test]
    public void ResetLayout_RestoresAllPanelsVisible()
    {
        _layoutManager.SetRegionVisibility(LayoutRegion.Primary, false);
        _layoutManager.SetRegionVisibility(LayoutRegion.Secondary, false);

        var result = _layoutManager.RequestLayoutTransition(LayoutTransition.ResetLayout);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.RegionVisibility.Should().Be(LayoutRegion.All);
    }

    [Test]
    public void ResetLayout_FromFocus_ReturnsToDefault()
    {
        _layoutManager.RequestLayoutTransition(LayoutTransition.Focus);

        var result = _layoutManager.RequestLayoutTransition(LayoutTransition.ResetLayout);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.LayoutMode.Should().Be(LayoutMode.Default);
    }

    [Test]
    public void ResetLayout_FromFullScreen_ClearsFullScreen()
    {
        _layoutManager.RequestLayoutTransition(LayoutTransition.ToggleFullScreen);

        var result = _layoutManager.RequestLayoutTransition(LayoutTransition.ResetLayout);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.IsFullScreen.Should().BeFalse();
        _layoutManager.LayoutMode.Should().Be(LayoutMode.Default);
    }

    [Test]
    public void ResetLayout_ResetsPanelSizesInWorkspaceSettings()
    {
        _layoutManager.RequestLayoutTransition(LayoutTransition.ResetLayout);

        _workspaceSettings.Received(1).PrimaryPanelWidth = 300f;
        _workspaceSettings.Received(1).SecondaryPanelWidth = 300f;
        _workspaceSettings.Received(1).ConsolePanelHeight = 350f;
    }

    [Test]
    public void ResetLayout_ResetsPreferredRegionVisibilityInWorkspaceSettings()
    {
        _layoutManager.RequestLayoutTransition(LayoutTransition.ResetLayout);

        _workspaceSettings.Received().PreferredRegionVisibility = LayoutRegion.All;
    }

    [Test]
    public void LayoutModeChange_SendsLayoutModeChangedMessage()
    {
        LayoutModeChangedMessage? receivedMessage = null;
        var recipient = new object();
        _messengerService.Register<LayoutModeChangedMessage>(recipient, (r, m) => receivedMessage = m);

        _layoutManager.RequestLayoutTransition(LayoutTransition.Focus);

        receivedMessage.Should().NotBeNull();
        receivedMessage!.LayoutMode.Should().Be(LayoutMode.Focus);
    }

    [Test]
    public void FullScreenToggle_SendsFullScreenChangedMessage()
    {
        FullScreenChangedMessage? receivedMessage = null;
        var recipient = new object();
        _messengerService.Register<FullScreenChangedMessage>(recipient, (r, m) => receivedMessage = m);

        _layoutManager.RequestLayoutTransition(LayoutTransition.ToggleFullScreen);

        receivedMessage.Should().NotBeNull();
        receivedMessage!.IsFullScreen.Should().BeTrue();
    }

    [Test]
    public void RegionVisibilityChange_SendsRegionVisibilityChangedMessage()
    {
        RegionVisibilityChangedMessage? receivedMessage = null;
        var recipient = new object();
        _messengerService.Register<RegionVisibilityChangedMessage>(recipient, (r, m) => receivedMessage = m);

        _layoutManager.SetRegionVisibility(LayoutRegion.Primary, false);

        receivedMessage.Should().NotBeNull();
        receivedMessage!.RegionVisibility.Should().Be(LayoutRegion.Secondary | LayoutRegion.Console);
    }

    [Test]
    public void SetRegionVisibility_InDefaultMode_UpdatesPreferredRegionVisibility()
    {
        _layoutManager.SetRegionVisibility(LayoutRegion.Primary, false);

        var expectedVisibility = LayoutRegion.Secondary | LayoutRegion.Console;
        _workspaceSettings.Received().PreferredRegionVisibility = expectedVisibility;
    }

    [Test]
    public void SetRegionVisibility_WhileFullScreen_UpdatesPreferredRegionVisibility()
    {
        // Fullscreen does not change the layout mode, so panel changes still persist as preferred.
        _layoutManager.RequestLayoutTransition(LayoutTransition.ToggleFullScreen);
        _workspaceSettings.ClearReceivedCalls();

        _layoutManager.SetRegionVisibility(LayoutRegion.Secondary, false);

        var expectedVisibility = LayoutRegion.Primary | LayoutRegion.Console;
        _workspaceSettings.Received().PreferredRegionVisibility = expectedVisibility;
    }

    [Test]
    public void SetRegionVisibility_ToNone_UpdatesPreferredRegionVisibility()
    {
        // Hide all panels one by one in the Default layout.
        _layoutManager.SetRegionVisibility(LayoutRegion.Primary, false);
        _layoutManager.SetRegionVisibility(LayoutRegion.Secondary, false);
        _workspaceSettings.ClearReceivedCalls();

        // The last panel being hidden persists None as the preference, because the user explicitly
        // chose to hide all panels.
        _layoutManager.SetRegionVisibility(LayoutRegion.Console, false);

        _workspaceSettings.Received().PreferredRegionVisibility = LayoutRegion.None;
    }

    [Test]
    public void MultipleQuickTransitions_MaintainsConsistentState()
    {
        _layoutManager.RequestLayoutTransition(LayoutTransition.Focus);
        _layoutManager.RequestLayoutTransition(LayoutTransition.Presentation);
        _layoutManager.RequestLayoutTransition(LayoutTransition.Default);

        _layoutManager.LayoutMode.Should().Be(LayoutMode.Default);
        _layoutManager.RegionVisibility.Should().Be(LayoutRegion.All);
    }

    [Test]
    public void LayoutRegion_CombinationsWorkCorrectly()
    {
        _layoutManager.SetRegionVisibility(LayoutRegion.Primary, false);
        _layoutManager.SetRegionVisibility(LayoutRegion.Secondary, false);

        _layoutManager.RegionVisibility.Should().Be(LayoutRegion.Console);
        _layoutManager.IsContextPanelVisible.Should().BeFalse();
        _layoutManager.IsInspectorPanelVisible.Should().BeFalse();
        _layoutManager.IsConsolePanelVisible.Should().BeTrue();
    }
}
