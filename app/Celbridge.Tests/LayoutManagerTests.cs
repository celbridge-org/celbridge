using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Messaging.Services;
using Celbridge.Settings;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Services;
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
        _editorSettings.PreferredPanelVisibility.Returns(PanelVisibilityFlags.All);

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
    public void InitialState_PanelVisibilityMatchesEditorSettings()
    {
        _layoutManager.PanelVisibility.Should().Be(PanelVisibilityFlags.All);
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
        var result = _layoutManager.RequestTransition(LayoutTransition.EnterFullScreen);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.WindowMode.Should().Be(WindowMode.FullScreen);
        _layoutManager.IsFullScreen.Should().BeTrue();
    }

    [Test]
    public void TransitionToZenMode_FromWindowed_HidesAllPanels()
    {
        var result = _layoutManager.RequestTransition(LayoutTransition.EnterZenMode);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.WindowMode.Should().Be(WindowMode.ZenMode);
        _layoutManager.PanelVisibility.Should().Be(PanelVisibilityFlags.None);
        _layoutManager.IsFullScreen.Should().BeTrue();
    }

    [Test]
    public void TransitionToPresenterMode_FromWindowed_HidesAllPanels()
    {
        var result = _layoutManager.RequestTransition(LayoutTransition.EnterPresenterMode);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.WindowMode.Should().Be(WindowMode.Presenter);
        _layoutManager.PanelVisibility.Should().Be(PanelVisibilityFlags.None);
        _layoutManager.IsFullScreen.Should().BeTrue();
    }

    [Test]
    public void TransitionToWindowed_FromFullScreen_RestoresPreferredPanelVisibility()
    {
        _layoutManager.RequestTransition(LayoutTransition.EnterFullScreen);

        var result = _layoutManager.RequestTransition(LayoutTransition.EnterWindowed);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.WindowMode.Should().Be(WindowMode.Windowed);
        _layoutManager.PanelVisibility.Should().Be(PanelVisibilityFlags.All);
        _layoutManager.IsFullScreen.Should().BeFalse();
    }

    [Test]
    public void TransitionToWindowed_FromZenMode_RestoresPreferredPanelVisibility()
    {
        _layoutManager.RequestTransition(LayoutTransition.EnterZenMode);

        var result = _layoutManager.RequestTransition(LayoutTransition.EnterWindowed);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.WindowMode.Should().Be(WindowMode.Windowed);
        _layoutManager.PanelVisibility.Should().Be(PanelVisibilityFlags.All);
    }

    [Test]
    public void TransitionToSameMode_Succeeds()
    {
        _layoutManager.RequestTransition(LayoutTransition.EnterFullScreen);

        var result = _layoutManager.RequestTransition(LayoutTransition.EnterFullScreen);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.WindowMode.Should().Be(WindowMode.FullScreen);
    }

    #endregion

    #region Toggle Zen Mode Tests

    [Test]
    public void ToggleZenMode_FromWindowed_EntersZenMode()
    {
        var result = _layoutManager.RequestTransition(LayoutTransition.ToggleZenMode);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.WindowMode.Should().Be(WindowMode.ZenMode);
        _layoutManager.PanelVisibility.Should().Be(PanelVisibilityFlags.None);
    }

    [Test]
    public void ToggleZenMode_FromZenMode_ReturnsToWindowed()
    {
        _layoutManager.RequestTransition(LayoutTransition.EnterZenMode);

        var result = _layoutManager.RequestTransition(LayoutTransition.ToggleZenMode);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.WindowMode.Should().Be(WindowMode.Windowed);
    }

    [Test]
    public void ToggleZenMode_FromFullScreen_ReturnsToWindowed()
    {
        _layoutManager.RequestTransition(LayoutTransition.EnterFullScreen);

        var result = _layoutManager.RequestTransition(LayoutTransition.ToggleZenMode);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.WindowMode.Should().Be(WindowMode.Windowed);
    }

    [Test]
    public void ToggleZenMode_FromWindowedWithAllPanelsCollapsed_RestoresAllPanels()
    {
        // Manually collapse all panels while in Windowed mode
        _layoutManager.SetPanelVisibility(PanelVisibilityFlags.Context, false);
        _layoutManager.SetPanelVisibility(PanelVisibilityFlags.Inspector, false);
        _layoutManager.SetPanelVisibility(PanelVisibilityFlags.Console, false);

        var result = _layoutManager.RequestTransition(LayoutTransition.ToggleZenMode);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.WindowMode.Should().Be(WindowMode.Windowed);
        _layoutManager.PanelVisibility.Should().Be(PanelVisibilityFlags.All);
    }

    #endregion

    #region Panel Visibility Tests

    [Test]
    public void SetPanelVisibility_HideSinglePanel_UpdatesVisibility()
    {
        _layoutManager.SetPanelVisibility(PanelVisibilityFlags.Context, false);

        _layoutManager.IsContextPanelVisible.Should().BeFalse();
        _layoutManager.IsInspectorPanelVisible.Should().BeTrue();
        _layoutManager.IsConsolePanelVisible.Should().BeTrue();
    }

    [Test]
    public void SetPanelVisibility_ShowHiddenPanel_UpdatesVisibility()
    {
        _layoutManager.SetPanelVisibility(PanelVisibilityFlags.Console, false);
        _layoutManager.SetPanelVisibility(PanelVisibilityFlags.Console, true);

        _layoutManager.IsConsolePanelVisible.Should().BeTrue();
    }

    [Test]
    public void TogglePanelVisibility_TogglesPanel()
    {
        _layoutManager.IsContextPanelVisible.Should().BeTrue();

        _layoutManager.TogglePanelVisibility(PanelVisibilityFlags.Context);

        _layoutManager.IsContextPanelVisible.Should().BeFalse();

        _layoutManager.TogglePanelVisibility(PanelVisibilityFlags.Context);

        _layoutManager.IsContextPanelVisible.Should().BeTrue();
    }

    [Test]
    public void SetPanelVisibility_InZenMode_ShowingPanelTransitionsToFullScreen()
    {
        _layoutManager.RequestTransition(LayoutTransition.EnterZenMode);

        _layoutManager.SetPanelVisibility(PanelVisibilityFlags.Context, true);

        _layoutManager.WindowMode.Should().Be(WindowMode.FullScreen);
        _layoutManager.IsContextPanelVisible.Should().BeTrue();
    }

    [Test]
    public void SetPanelVisibility_InFullScreen_HidingAllPanelsTransitionsToZenMode()
    {
        _layoutManager.RequestTransition(LayoutTransition.EnterFullScreen);

        _layoutManager.SetPanelVisibility(PanelVisibilityFlags.Context, false);
        _layoutManager.SetPanelVisibility(PanelVisibilityFlags.Inspector, false);
        _layoutManager.SetPanelVisibility(PanelVisibilityFlags.Console, false);

        _layoutManager.WindowMode.Should().Be(WindowMode.ZenMode);
        _layoutManager.PanelVisibility.Should().Be(PanelVisibilityFlags.None);
    }

    [Test]
    public void SetPanelVisibility_SameState_NoChange()
    {
        bool messageReceived = false;
        var recipient = new object();
        _messengerService.Register<PanelVisibilityChangedMessage>(recipient, (r, m) => messageReceived = true);

        _layoutManager.SetPanelVisibility(PanelVisibilityFlags.Context, true);

        messageReceived.Should().BeFalse();
    }

    #endregion

    #region Reset Layout Tests

    [Test]
    public void ResetLayout_RestoresAllPanelsVisible()
    {
        _layoutManager.SetPanelVisibility(PanelVisibilityFlags.Context, false);
        _layoutManager.SetPanelVisibility(PanelVisibilityFlags.Inspector, false);

        var result = _layoutManager.RequestTransition(LayoutTransition.ResetLayout);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.PanelVisibility.Should().Be(PanelVisibilityFlags.All);
    }

    [Test]
    public void ResetLayout_FromFullScreen_ReturnsToWindowed()
    {
        _layoutManager.RequestTransition(LayoutTransition.EnterFullScreen);

        var result = _layoutManager.RequestTransition(LayoutTransition.ResetLayout);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.WindowMode.Should().Be(WindowMode.Windowed);
    }

    [Test]
    public void ResetLayout_ResetsPanelSizesInEditorSettings()
    {
        _layoutManager.RequestTransition(LayoutTransition.ResetLayout);

        _editorSettings.Received(1).ContextPanelWidth = 300f;
        _editorSettings.Received(1).InspectorPanelWidth = 300f;
        _editorSettings.Received(1).ConsolePanelHeight = 350f;
    }

    [Test]
    public void ResetLayout_ResetsPreferredPanelVisibilityInEditorSettings()
    {
        _layoutManager.RequestTransition(LayoutTransition.ResetLayout);

        _editorSettings.Received().PreferredPanelVisibility = PanelVisibilityFlags.All;
    }

    #endregion

    #region Messaging Tests

    [Test]
    public void WindowModeChange_SendsWindowModeChangedMessage()
    {
        WindowModeChangedMessage? receivedMessage = null;
        var recipient = new object();
        _messengerService.Register<WindowModeChangedMessage>(recipient, (r, m) => receivedMessage = m);

        _layoutManager.RequestTransition(LayoutTransition.EnterFullScreen);

        receivedMessage.Should().NotBeNull();
        receivedMessage!.WindowMode.Should().Be(WindowMode.FullScreen);
    }

    [Test]
    public void PanelVisibilityChange_SendsPanelVisibilityChangedMessage()
    {
        PanelVisibilityChangedMessage? receivedMessage = null;
        var recipient = new object();
        _messengerService.Register<PanelVisibilityChangedMessage>(recipient, (r, m) => receivedMessage = m);

        _layoutManager.SetPanelVisibility(PanelVisibilityFlags.Context, false);

        receivedMessage.Should().NotBeNull();
        receivedMessage!.PanelVisibility.Should().Be(PanelVisibilityFlags.Inspector | PanelVisibilityFlags.Console);
    }

    #endregion

    #region Settings Persistence Tests

    [Test]
    public void SetPanelVisibility_InWindowedMode_UpdatesPreferredPanelVisibility()
    {
        _layoutManager.SetPanelVisibility(PanelVisibilityFlags.Context, false);

        var expectedVisibility = PanelVisibilityFlags.Inspector | PanelVisibilityFlags.Console;
        _editorSettings.Received().PreferredPanelVisibility = expectedVisibility;
    }

    [Test]
    public void SetPanelVisibility_InFullScreenMode_UpdatesPreferredPanelVisibility()
    {
        _layoutManager.RequestTransition(LayoutTransition.EnterFullScreen);
        _editorSettings.ClearReceivedCalls();

        _layoutManager.SetPanelVisibility(PanelVisibilityFlags.Inspector, false);

        var expectedVisibility = PanelVisibilityFlags.Context | PanelVisibilityFlags.Console;
        _editorSettings.Received().PreferredPanelVisibility = expectedVisibility;
    }

    [Test]
    public void SetPanelVisibility_ToNone_DoesNotUpdatePreferredPanelVisibility()
    {
        _layoutManager.RequestTransition(LayoutTransition.EnterFullScreen);
        _editorSettings.ClearReceivedCalls();

        // Hide all panels
        _layoutManager.SetPanelVisibility(PanelVisibilityFlags.Context, false);
        _layoutManager.SetPanelVisibility(PanelVisibilityFlags.Inspector, false);
        _editorSettings.ClearReceivedCalls();

        // The last panel being hidden should NOT persist None as preference
        _layoutManager.SetPanelVisibility(PanelVisibilityFlags.Console, false);

        _editorSettings.DidNotReceive().PreferredPanelVisibility = PanelVisibilityFlags.None;
    }

    #endregion

    #region Edge Cases

    [Test]
    public void MultipleQuickTransitions_MaintainsConsistentState()
    {
        _layoutManager.RequestTransition(LayoutTransition.EnterFullScreen);
        _layoutManager.RequestTransition(LayoutTransition.EnterZenMode);
        _layoutManager.RequestTransition(LayoutTransition.EnterWindowed);
        _layoutManager.RequestTransition(LayoutTransition.EnterPresenterMode);
        _layoutManager.RequestTransition(LayoutTransition.EnterWindowed);

        _layoutManager.WindowMode.Should().Be(WindowMode.Windowed);
        _layoutManager.IsFullScreen.Should().BeFalse();
        _layoutManager.PanelVisibility.Should().Be(PanelVisibilityFlags.All);
    }

    [Test]
    public void PanelVisibilityFlags_CombinationsWorkCorrectly()
    {
        _layoutManager.SetPanelVisibility(PanelVisibilityFlags.Context, false);
        _layoutManager.SetPanelVisibility(PanelVisibilityFlags.Inspector, false);

        _layoutManager.PanelVisibility.Should().Be(PanelVisibilityFlags.Console);
        _layoutManager.IsContextPanelVisible.Should().BeFalse();
        _layoutManager.IsInspectorPanelVisible.Should().BeFalse();
        _layoutManager.IsConsolePanelVisible.Should().BeTrue();
    }

    #endregion
}
