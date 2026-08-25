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
        _workspaceSettings.PreferredSurfaceVisibility = WorkspaceSurface.All;

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceWrapper.IsWorkspaceLoaded.Returns(true);
        workspaceWrapper.WorkspaceService.Returns(workspaceService);
        workspaceService.BindableWorkspaceSettings.Returns(_workspaceSettings);

        var logger = _serviceProvider.GetRequiredService<ILogger<LayoutManager>>();

        _layoutManager = new LayoutManager(logger, _messengerService, _settingsService, workspaceWrapper);
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
    public void InitialState_SurfaceVisibilityIsAllByDefault()
    {
        _layoutManager.SurfaceVisibility.Should().Be(WorkspaceSurface.All);
    }

    [Test]
    public void InitialState_AllPanelsAreVisible()
    {
        _layoutManager.IsUtilityPanelVisible.Should().BeTrue();
        _layoutManager.IsSideAreaVisible.Should().BeTrue();
        _layoutManager.IsBottomAreaVisible.Should().BeTrue();
    }

    [Test]
    public void TransitionToFocus_FromDefault_HidesSidePanels()
    {
        var result = _layoutManager.RequestLayoutTransition(LayoutTransition.Focus);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.LayoutMode.Should().Be(LayoutMode.Focus);
        _layoutManager.SurfaceVisibility.Should().Be(WorkspaceSurface.None);

        // Fullscreen is independent of the layout mode.
        _layoutManager.IsFullScreen.Should().BeFalse();
    }

    [Test]
    public void TransitionToPresentation_FromDefault_HidesSidePanels()
    {
        var result = _layoutManager.RequestLayoutTransition(LayoutTransition.Presentation);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.LayoutMode.Should().Be(LayoutMode.Presentation);
        _layoutManager.SurfaceVisibility.Should().Be(WorkspaceSurface.None);
        _layoutManager.IsFullScreen.Should().BeFalse();
    }

    [Test]
    public void TransitionToDefault_FromFocus_RestoresPreferredSurfaceVisibility()
    {
        _layoutManager.RequestLayoutTransition(LayoutTransition.Focus);

        var result = _layoutManager.RequestLayoutTransition(LayoutTransition.Default);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.LayoutMode.Should().Be(LayoutMode.Default);
        _layoutManager.SurfaceVisibility.Should().Be(WorkspaceSurface.All);
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
        _layoutManager.SurfaceVisibility.Should().Be(WorkspaceSurface.None);
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
    public void SetSurfaceVisibility_HideSinglePanel_UpdatesVisibility()
    {
        _layoutManager.SetSurfaceVisibility(WorkspaceSurface.UtilityPanel, false);

        _layoutManager.IsUtilityPanelVisible.Should().BeFalse();
        _layoutManager.IsSideAreaVisible.Should().BeTrue();
        _layoutManager.IsBottomAreaVisible.Should().BeTrue();
    }

    [Test]
    public void SetSurfaceVisibility_ShowHiddenPanel_UpdatesVisibility()
    {
        _layoutManager.SetSurfaceVisibility(WorkspaceSurface.BottomArea, false);
        _layoutManager.SetSurfaceVisibility(WorkspaceSurface.BottomArea, true);

        _layoutManager.IsBottomAreaVisible.Should().BeTrue();
    }

    [Test]
    public void ToggleSurfaceVisibility_TogglesPanel()
    {
        _layoutManager.IsUtilityPanelVisible.Should().BeTrue();

        _layoutManager.ToggleSurfaceVisibility(WorkspaceSurface.UtilityPanel);

        _layoutManager.IsUtilityPanelVisible.Should().BeFalse();

        _layoutManager.ToggleSurfaceVisibility(WorkspaceSurface.UtilityPanel);

        _layoutManager.IsUtilityPanelVisible.Should().BeTrue();
    }

    [Test]
    public void SetSurfaceVisibility_InFocusMode_ReturnsToDefault()
    {
        _layoutManager.RequestLayoutTransition(LayoutTransition.Focus);

        // Manually showing a panel means the user is customizing the layout, so the mode returns to
        // Default rather than staying in Focus with a panel visible.
        _layoutManager.SetSurfaceVisibility(WorkspaceSurface.UtilityPanel, true);

        _layoutManager.LayoutMode.Should().Be(LayoutMode.Default);
        _layoutManager.IsUtilityPanelVisible.Should().BeTrue();
    }

    [Test]
    public void SetSurfaceVisibility_InFocusMode_KeepsThePanelsTheModeHid()
    {
        _layoutManager.SetSurfaceVisibility(WorkspaceSurface.SideArea, false);
        _layoutManager.RequestLayoutTransition(LayoutTransition.Focus);

        // Focus hides every surface transiently, so showing one from there returns to the layout the user
        // prefers with that surface shown, rather than to the mode's empty layout plus that one surface.
        _layoutManager.SetSurfaceVisibility(WorkspaceSurface.UtilityPanel, true);

        _layoutManager.SurfaceVisibility.Should().Be(WorkspaceSurface.UtilityPanel | WorkspaceSurface.BottomArea);
        _workspaceSettings.PreferredSurfaceVisibility.Should()
            .Be(WorkspaceSurface.UtilityPanel | WorkspaceSurface.BottomArea);
    }

    [Test]
    public void SetSurfaceVisibility_SameState_NoChange()
    {
        bool messageReceived = false;
        var recipient = new object();
        _messengerService.Register<SurfaceVisibilityChangedMessage>(recipient, (r, m) => messageReceived = true);

        _layoutManager.SetSurfaceVisibility(WorkspaceSurface.UtilityPanel, true);

        messageReceived.Should().BeFalse();
    }

    [Test]
    public void ResetLayout_RestoresAllPanelsVisible()
    {
        _layoutManager.SetSurfaceVisibility(WorkspaceSurface.UtilityPanel, false);
        _layoutManager.SetSurfaceVisibility(WorkspaceSurface.SideArea, false);

        var result = _layoutManager.RequestLayoutTransition(LayoutTransition.ResetLayout);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.SurfaceVisibility.Should().Be(WorkspaceSurface.All);
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

        _workspaceSettings.Received(1).UtilityPanelWidth = 300f;
        _workspaceSettings.Received(1).SideAreaWidth = 300f;
        _workspaceSettings.Received(1).BottomAreaHeight = 350f;
    }

    [Test]
    public void ResetLayout_ResetsPreferredSurfaceVisibilityInWorkspaceSettings()
    {
        _layoutManager.RequestLayoutTransition(LayoutTransition.ResetLayout);

        _workspaceSettings.Received().PreferredSurfaceVisibility = WorkspaceSurface.All;
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
    public void SurfaceVisibilityChange_SendsSurfaceVisibilityChangedMessage()
    {
        SurfaceVisibilityChangedMessage? receivedMessage = null;
        var recipient = new object();
        _messengerService.Register<SurfaceVisibilityChangedMessage>(recipient, (r, m) => receivedMessage = m);

        _layoutManager.SetSurfaceVisibility(WorkspaceSurface.UtilityPanel, false);

        receivedMessage.Should().NotBeNull();
        receivedMessage!.SurfaceVisibility.Should().Be(WorkspaceSurface.SideArea | WorkspaceSurface.BottomArea);
    }

    [Test]
    public void SetSurfaceVisibility_InDefaultMode_UpdatesPreferredSurfaceVisibility()
    {
        _layoutManager.SetSurfaceVisibility(WorkspaceSurface.UtilityPanel, false);

        var expectedVisibility = WorkspaceSurface.SideArea | WorkspaceSurface.BottomArea;
        _workspaceSettings.Received().PreferredSurfaceVisibility = expectedVisibility;
    }

    [Test]
    public void SetSurfaceVisibility_WhileFullScreen_UpdatesPreferredSurfaceVisibility()
    {
        // Fullscreen does not change the layout mode, so panel changes still persist as preferred.
        _layoutManager.RequestLayoutTransition(LayoutTransition.ToggleFullScreen);
        _workspaceSettings.ClearReceivedCalls();

        _layoutManager.SetSurfaceVisibility(WorkspaceSurface.SideArea, false);

        var expectedVisibility = WorkspaceSurface.UtilityPanel | WorkspaceSurface.BottomArea;
        _workspaceSettings.Received().PreferredSurfaceVisibility = expectedVisibility;
    }

    [Test]
    public void SetSurfaceVisibility_ToNone_UpdatesPreferredSurfaceVisibility()
    {
        // Hide all panels one by one in the Default layout.
        _layoutManager.SetSurfaceVisibility(WorkspaceSurface.UtilityPanel, false);
        _layoutManager.SetSurfaceVisibility(WorkspaceSurface.SideArea, false);
        _workspaceSettings.ClearReceivedCalls();

        // The last panel being hidden persists None as the preference, because the user explicitly
        // chose to hide all panels.
        _layoutManager.SetSurfaceVisibility(WorkspaceSurface.BottomArea, false);

        _workspaceSettings.Received().PreferredSurfaceVisibility = WorkspaceSurface.None;
    }

    [Test]
    public void InitialState_BottomAreaAlignmentIsCenter()
    {
        _layoutManager.BottomAreaAlignment.Should().Be(BottomAreaAlignment.Center);
    }

    [Test]
    public void SetBottomAreaAlignment_UpdatesAlignmentAndPersistsIt()
    {
        _layoutManager.SetBottomAreaAlignment(BottomAreaAlignment.Justify);

        _layoutManager.BottomAreaAlignment.Should().Be(BottomAreaAlignment.Justify);
        _workspaceSettings.Received().BottomAreaAlignment = BottomAreaAlignment.Justify;
    }

    [Test]
    public void SetBottomAreaAlignment_SendsBottomAreaAlignmentChangedMessage()
    {
        BottomAreaAlignmentChangedMessage? receivedMessage = null;
        var recipient = new object();
        _messengerService.Register<BottomAreaAlignmentChangedMessage>(recipient, (r, m) => receivedMessage = m);

        _layoutManager.SetBottomAreaAlignment(BottomAreaAlignment.Left);

        receivedMessage.Should().NotBeNull();
        receivedMessage!.Alignment.Should().Be(BottomAreaAlignment.Left);
    }

    [Test]
    public void SetBottomAreaAlignment_SameAlignment_NoChange()
    {
        bool messageReceived = false;
        var recipient = new object();
        _messengerService.Register<BottomAreaAlignmentChangedMessage>(recipient, (r, m) => messageReceived = true);

        _layoutManager.SetBottomAreaAlignment(BottomAreaAlignment.Center);

        messageReceived.Should().BeFalse();
    }

    [Test]
    public void SetBottomAreaAlignment_InPresentationMode_StillPersists()
    {
        // Alignment is a layout preference rather than a mode, so unlike surface visibility it is not
        // treated as transient presentation state.
        _layoutManager.RequestLayoutTransition(LayoutTransition.Presentation);

        _layoutManager.SetBottomAreaAlignment(BottomAreaAlignment.Right);

        _workspaceSettings.Received().BottomAreaAlignment = BottomAreaAlignment.Right;
    }

    [Test]
    public void WorkspaceLoaded_RestoresStoredBottomAreaAlignment()
    {
        _workspaceSettings.BottomAreaAlignment = BottomAreaAlignment.Right;

        _messengerService.Send(new WorkspaceLoadedMessage());

        _layoutManager.BottomAreaAlignment.Should().Be(BottomAreaAlignment.Right);
    }

    [Test]
    public void ResetLayout_RestoresCenterBottomAreaAlignment()
    {
        _layoutManager.SetBottomAreaAlignment(BottomAreaAlignment.Justify);

        var result = _layoutManager.RequestLayoutTransition(LayoutTransition.ResetLayout);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.BottomAreaAlignment.Should().Be(BottomAreaAlignment.Center);
    }

    [Test]
    public void MultipleQuickTransitions_MaintainsConsistentState()
    {
        _layoutManager.RequestLayoutTransition(LayoutTransition.Focus);
        _layoutManager.RequestLayoutTransition(LayoutTransition.Presentation);
        _layoutManager.RequestLayoutTransition(LayoutTransition.Default);

        _layoutManager.LayoutMode.Should().Be(LayoutMode.Default);
        _layoutManager.SurfaceVisibility.Should().Be(WorkspaceSurface.All);
    }

    [Test]
    public void WorkspaceSurface_CombinationsWorkCorrectly()
    {
        _layoutManager.SetSurfaceVisibility(WorkspaceSurface.UtilityPanel, false);
        _layoutManager.SetSurfaceVisibility(WorkspaceSurface.SideArea, false);

        _layoutManager.SurfaceVisibility.Should().Be(WorkspaceSurface.BottomArea);
        _layoutManager.IsUtilityPanelVisible.Should().BeFalse();
        _layoutManager.IsSideAreaVisible.Should().BeFalse();
        _layoutManager.IsBottomAreaVisible.Should().BeTrue();
    }
}
