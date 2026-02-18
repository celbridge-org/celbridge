using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Messaging.Services;
using Celbridge.Settings;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Services;
using Celbridge.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Tests;

[TestFixture]
public class LayoutManagerTests
{
    private ServiceProvider? _serviceProvider;
    private IMessengerService _messengerService = null!;
    private IEditorSettings _editorSettings = null!;
    private LayoutManager _layoutManager = null!;

    [SetUp]
    public void Setup()
    {
        var services = new ServiceCollection();

        Logging.ServiceConfiguration.ConfigureServices(services);
        services.AddSingleton<IMessengerService, MessengerService>();

        _serviceProvider = services.BuildServiceProvider();

        _messengerService = _serviceProvider.GetRequiredService<IMessengerService>();
        _editorSettings = Substitute.For<IEditorSettings>();

        // Default to all panels visible
        _editorSettings.PreferredRegionVisibility.Returns(LayoutRegion.All);

        var logger = _serviceProvider.GetRequiredService<ILogger<LayoutManager>>();
        _layoutManager = new LayoutManager(logger, _messengerService, _editorSettings);
    }

    [TearDown]
    public void TearDown()
    {
        (_serviceProvider as IDisposable)?.Dispose();
    }

    #region Initial State Tests

    [Test]
    public void InitialState_WindowModeIsWindowed()
    {
        _layoutManager.WindowMode.Should().Be(WindowMode.Windowed);
    }

    [Test]
    public void InitialState_IsFullScreenIsFalse()
    {
        _layoutManager.IsFullScreen.Should().BeFalse();
    }

    [Test]
    public void InitialState_RegionVisibilityMatchesEditorSettings()
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

    #endregion

    #region Window Mode Transition Tests

    [Test]
    public void TransitionToFullScreen_FromWindowed_ChangesMode()
    {
        var result = _layoutManager.RequestWindowModeTransition(WindowModeTransition.EnterFullScreen);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.WindowMode.Should().Be(WindowMode.FullScreen);
        _layoutManager.IsFullScreen.Should().BeTrue();
    }

    [Test]
    public void TransitionToZenMode_FromWindowed_HidesAllPanels()
    {
        var result = _layoutManager.RequestWindowModeTransition(WindowModeTransition.EnterZenMode);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.WindowMode.Should().Be(WindowMode.ZenMode);
        _layoutManager.RegionVisibility.Should().Be(LayoutRegion.None);
        _layoutManager.IsFullScreen.Should().BeTrue();
    }

    [Test]
    public void TransitionToPresenterMode_FromWindowed_HidesAllPanels()
    {
        var result = _layoutManager.RequestWindowModeTransition(WindowModeTransition.EnterPresenterMode);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.WindowMode.Should().Be(WindowMode.Presenter);
        _layoutManager.RegionVisibility.Should().Be(LayoutRegion.None);
        _layoutManager.IsFullScreen.Should().BeTrue();
    }

    [Test]
    public void TransitionToWindowed_FromFullScreen_RestoresPreferredRegionVisibility()
    {
        _layoutManager.RequestWindowModeTransition(WindowModeTransition.EnterFullScreen);

        var result = _layoutManager.RequestWindowModeTransition(WindowModeTransition.EnterWindowed);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.WindowMode.Should().Be(WindowMode.Windowed);
        _layoutManager.RegionVisibility.Should().Be(LayoutRegion.All);
        _layoutManager.IsFullScreen.Should().BeFalse();
    }

    [Test]
    public void TransitionToWindowed_FromZenMode_RestoresPreferredRegionVisibility()
    {
        _layoutManager.RequestWindowModeTransition(WindowModeTransition.EnterZenMode);

        var result = _layoutManager.RequestWindowModeTransition(WindowModeTransition.EnterWindowed);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.WindowMode.Should().Be(WindowMode.Windowed);
        _layoutManager.RegionVisibility.Should().Be(LayoutRegion.All);
    }

    [Test]
    public void TransitionToSameMode_Succeeds()
    {
        _layoutManager.RequestWindowModeTransition(WindowModeTransition.EnterFullScreen);

        var result = _layoutManager.RequestWindowModeTransition(WindowModeTransition.EnterFullScreen);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.WindowMode.Should().Be(WindowMode.FullScreen);
    }

    #endregion

    #region Toggle ZenMode Tests

    [Test]
    public void ToggleZenMode_FromWindowed_EntersZenMode()
    {
        var result = _layoutManager.RequestWindowModeTransition(WindowModeTransition.ToggleZenMode);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.WindowMode.Should().Be(WindowMode.ZenMode);
        _layoutManager.RegionVisibility.Should().Be(LayoutRegion.None);
    }

    [Test]
    public void ToggleZenMode_FromZenMode_ReturnsToWindowed()
    {
        _layoutManager.RequestWindowModeTransition(WindowModeTransition.EnterZenMode);

        var result = _layoutManager.RequestWindowModeTransition(WindowModeTransition.ToggleZenMode);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.WindowMode.Should().Be(WindowMode.Windowed);
    }

    [Test]
    public void ToggleZenMode_FromFullScreen_ReturnsToWindowed()
    {
        _layoutManager.RequestWindowModeTransition(WindowModeTransition.EnterFullScreen);

        var result = _layoutManager.RequestWindowModeTransition(WindowModeTransition.ToggleZenMode);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.WindowMode.Should().Be(WindowMode.Windowed);
    }

    [Test]
    public void ToggleZenMode_FromWindowedWithAllPanelsCollapsed_MaintainsNoPanel()
    {
        // Manually collapse all panels while in Windowed mode
        _layoutManager.SetRegionVisibility(LayoutRegion.Primary, false);
        _layoutManager.SetRegionVisibility(LayoutRegion.Secondary, false);
        _layoutManager.SetRegionVisibility(LayoutRegion.Console, false);

        // Toggle to ZenMode
        var result = _layoutManager.RequestWindowModeTransition(WindowModeTransition.ToggleZenMode);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.WindowMode.Should().Be(WindowMode.ZenMode);

        // Toggle back to Windowed - should restore the persisted preference (None)
        result = _layoutManager.RequestWindowModeTransition(WindowModeTransition.ToggleZenMode);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.WindowMode.Should().Be(WindowMode.Windowed);
        _layoutManager.RegionVisibility.Should().Be(LayoutRegion.None);
    }

    #endregion

    #region Panel Visibility Tests

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
    public void SetRegionVisibility_InZenMode_ShowingPanelTransitionsToFullScreen()
    {
        _layoutManager.RequestWindowModeTransition(WindowModeTransition.EnterZenMode);

        _layoutManager.SetRegionVisibility(LayoutRegion.Primary, true);

        _layoutManager.WindowMode.Should().Be(WindowMode.FullScreen);
        _layoutManager.IsContextPanelVisible.Should().BeTrue();
    }

    [Test]
    public void SetRegionVisibility_InFullScreen_HidingAllPanelsTransitionsToZenMode()
    {
        _layoutManager.RequestWindowModeTransition(WindowModeTransition.EnterFullScreen);

        _layoutManager.SetRegionVisibility(LayoutRegion.Primary, false);
        _layoutManager.SetRegionVisibility(LayoutRegion.Secondary, false);
        _layoutManager.SetRegionVisibility(LayoutRegion.Console, false);

        _layoutManager.WindowMode.Should().Be(WindowMode.ZenMode);
        _layoutManager.RegionVisibility.Should().Be(LayoutRegion.None);
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

    #endregion

    #region Reset Layout Tests

    [Test]
    public void ResetLayout_RestoresAllPanelsVisible()
    {
        _layoutManager.SetRegionVisibility(LayoutRegion.Primary, false);
        _layoutManager.SetRegionVisibility(LayoutRegion.Secondary, false);

        var result = _layoutManager.RequestWindowModeTransition(WindowModeTransition.ResetLayout);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.RegionVisibility.Should().Be(LayoutRegion.All);
    }

    [Test]
    public void ResetLayout_FromFullScreen_ReturnsToWindowed()
    {
        _layoutManager.RequestWindowModeTransition(WindowModeTransition.EnterFullScreen);

        var result = _layoutManager.RequestWindowModeTransition(WindowModeTransition.ResetLayout);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.WindowMode.Should().Be(WindowMode.Windowed);
    }

    [Test]
    public void ResetLayout_ResetsPanelSizesInEditorSettings()
    {
        _layoutManager.RequestWindowModeTransition(WindowModeTransition.ResetLayout);

        _editorSettings.Received(1).PrimaryPanelWidth = 300f;
        _editorSettings.Received(1).SecondaryPanelWidth = 300f;
        _editorSettings.Received(1).ConsolePanelHeight = 350f;
    }

    [Test]
    public void ResetLayout_ResetsPreferredRegionVisibilityInEditorSettings()
    {
        _layoutManager.RequestWindowModeTransition(WindowModeTransition.ResetLayout);

        _editorSettings.Received().PreferredRegionVisibility = LayoutRegion.All;
    }

    #endregion

    #region Messaging Tests

    [Test]
    public void WindowModeChange_SendsWindowModeChangedMessage()
    {
        WindowModeChangedMessage? receivedMessage = null;
        var recipient = new object();
        _messengerService.Register<WindowModeChangedMessage>(recipient, (r, m) => receivedMessage = m);

        _layoutManager.RequestWindowModeTransition(WindowModeTransition.EnterFullScreen);

        receivedMessage.Should().NotBeNull();
        receivedMessage!.WindowMode.Should().Be(WindowMode.FullScreen);
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

    #endregion

    #region Settings Persistence Tests

    [Test]
    public void SetRegionVisibility_InWindowedMode_UpdatesPreferredRegionVisibility()
    {
        _layoutManager.SetRegionVisibility(LayoutRegion.Primary, false);

        var expectedVisibility = LayoutRegion.Secondary | LayoutRegion.Console;
        _editorSettings.Received().PreferredRegionVisibility = expectedVisibility;
    }

    [Test]
    public void SetRegionVisibility_InFullScreenMode_UpdatesPreferredRegionVisibility()
    {
        _layoutManager.RequestWindowModeTransition(WindowModeTransition.EnterFullScreen);
        _editorSettings.ClearReceivedCalls();

        _layoutManager.SetRegionVisibility(LayoutRegion.Secondary, false);

        var expectedVisibility = LayoutRegion.Primary | LayoutRegion.Console;
        _editorSettings.Received().PreferredRegionVisibility = expectedVisibility;
    }

    [Test]
    public void SetRegionVisibility_ToNone_UpdatesPreferredRegionVisibility()
    {
        _layoutManager.RequestWindowModeTransition(WindowModeTransition.EnterFullScreen);
        _editorSettings.ClearReceivedCalls();

        // Hide all panels
        _layoutManager.SetRegionVisibility(LayoutRegion.Primary, false);
        _layoutManager.SetRegionVisibility(LayoutRegion.Secondary, false);
        _editorSettings.ClearReceivedCalls();

        // The last panel being hidden SHOULD persist None as preference
        // because the user explicitly chose to hide all panels
        _layoutManager.SetRegionVisibility(LayoutRegion.Console, false);

        _editorSettings.Received().PreferredRegionVisibility = LayoutRegion.None;
    }

    #endregion

    #region Edge Cases

    [Test]
    public void MultipleQuickTransitions_MaintainsConsistentState()
    {
        _layoutManager.RequestWindowModeTransition(WindowModeTransition.EnterFullScreen);
        _layoutManager.RequestWindowModeTransition(WindowModeTransition.EnterZenMode);
        _layoutManager.RequestWindowModeTransition(WindowModeTransition.EnterWindowed);
        _layoutManager.RequestWindowModeTransition(WindowModeTransition.EnterPresenterMode);
        _layoutManager.RequestWindowModeTransition(WindowModeTransition.EnterWindowed);

        _layoutManager.WindowMode.Should().Be(WindowMode.Windowed);
        _layoutManager.IsFullScreen.Should().BeFalse();
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

    #endregion
}
