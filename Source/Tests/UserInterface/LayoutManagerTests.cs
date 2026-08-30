using Celbridge.Messaging;
using Celbridge.Messaging.Services;
using Celbridge.Settings;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Services;
using Celbridge.Utilities;
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

        // Default to every area visible. Set the value (rather than stubbing the
        // getter) so writes by the layout manager are reflected on subsequent reads.
        _workspaceSettings.PreferredVisibleAreas = WorkspaceAreaHelper.AllAreasVisible;

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
    public void InitialState_EveryAreaIsVisibleByDefault()
    {
        _layoutManager.VisibleAreas.Should().BeEquivalentTo(WorkspaceAreaHelper.AllAreasVisible);
    }

    [Test]
    public void InitialState_EachAreaReportsVisible()
    {
        _layoutManager.IsAreaVisible(WorkspaceArea.Utility).Should().BeTrue();
        _layoutManager.IsAreaVisible(WorkspaceArea.Side).Should().BeTrue();
        _layoutManager.IsAreaVisible(WorkspaceArea.Bottom).Should().BeTrue();
    }

    [Test]
    public void TransitionToFocus_FromDefault_HidesEveryCollapsibleArea()
    {
        var result = _layoutManager.RequestLayoutTransition(LayoutTransition.Focus);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.LayoutMode.Should().Be(LayoutMode.Focus);
        _layoutManager.VisibleAreas.Should().BeEquivalentTo(VisibleAreas());

        // Fullscreen is independent of the layout mode.
        _layoutManager.IsFullScreen.Should().BeFalse();
    }

    [Test]
    public void TransitionToPresentation_FromDefault_HidesEveryCollapsibleArea()
    {
        var result = _layoutManager.RequestLayoutTransition(LayoutTransition.Presentation);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.LayoutMode.Should().Be(LayoutMode.Presentation);
        _layoutManager.VisibleAreas.Should().BeEquivalentTo(VisibleAreas());
        _layoutManager.IsFullScreen.Should().BeFalse();
    }

    [Test]
    public void TransitionToDefault_FromFocus_RestoresThePreferredAreas()
    {
        _layoutManager.RequestLayoutTransition(LayoutTransition.Focus);

        var result = _layoutManager.RequestLayoutTransition(LayoutTransition.Default);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.LayoutMode.Should().Be(LayoutMode.Default);
        _layoutManager.VisibleAreas.Should().BeEquivalentTo(WorkspaceAreaHelper.AllAreasVisible);
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
        _layoutManager.VisibleAreas.Should().BeEquivalentTo(VisibleAreas());
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
    public void SetAreaVisibility_HideOneArea_UpdatesVisibility()
    {
        _layoutManager.SetAreaVisibility(WorkspaceArea.Utility, false);

        _layoutManager.IsAreaVisible(WorkspaceArea.Utility).Should().BeFalse();
        _layoutManager.IsAreaVisible(WorkspaceArea.Side).Should().BeTrue();
        _layoutManager.IsAreaVisible(WorkspaceArea.Bottom).Should().BeTrue();
    }

    [Test]
    public void SetAreaVisibility_ShowHiddenArea_UpdatesVisibility()
    {
        _layoutManager.SetAreaVisibility(WorkspaceArea.Bottom, false);
        _layoutManager.SetAreaVisibility(WorkspaceArea.Bottom, true);

        _layoutManager.IsAreaVisible(WorkspaceArea.Bottom).Should().BeTrue();
    }

    [Test]
    public void ToggleAreaVisibility_TogglesTheArea()
    {
        _layoutManager.IsAreaVisible(WorkspaceArea.Utility).Should().BeTrue();

        _layoutManager.ToggleAreaVisibility(WorkspaceArea.Utility);

        _layoutManager.IsAreaVisible(WorkspaceArea.Utility).Should().BeFalse();

        _layoutManager.ToggleAreaVisibility(WorkspaceArea.Utility);

        _layoutManager.IsAreaVisible(WorkspaceArea.Utility).Should().BeTrue();
    }

    [Test]
    public void SetAreaVisibility_InFocusMode_ReturnsToDefault()
    {
        _layoutManager.RequestLayoutTransition(LayoutTransition.Focus);

        // Manually showing an area means the user is customizing the layout, so the mode returns to
        // Default rather than staying in Focus with an area visible.
        _layoutManager.SetAreaVisibility(WorkspaceArea.Utility, true);

        _layoutManager.LayoutMode.Should().Be(LayoutMode.Default);
        _layoutManager.IsAreaVisible(WorkspaceArea.Utility).Should().BeTrue();
    }

    [Test]
    public void SetAreaVisibility_InFocusMode_KeepsTheAreasTheModeHid()
    {
        _layoutManager.SetAreaVisibility(WorkspaceArea.Side, false);
        _layoutManager.RequestLayoutTransition(LayoutTransition.Focus);

        // Focus hides every collapsible area transiently, so showing one from there returns to the layout
        // the user prefers with that area shown.
        _layoutManager.SetAreaVisibility(WorkspaceArea.Utility, true);

        _layoutManager.VisibleAreas.Should()
            .BeEquivalentTo(VisibleAreas(WorkspaceArea.Utility, WorkspaceArea.Bottom));
        _workspaceSettings.PreferredVisibleAreas.Should()
            .BeEquivalentTo(VisibleAreas(WorkspaceArea.Utility, WorkspaceArea.Bottom));
    }

    [Test]
    public void SetAreaVisibility_InPresentationMode_PersistsTheComposedLayout()
    {
        // The Side area is now the only one the user has hidden.
        _layoutManager.SetAreaVisibility(WorkspaceArea.Side, false);
        _layoutManager.RequestLayoutTransition(LayoutTransition.Presentation);

        // Showing the Side area from Presentation composes a layout the stored preference does not hold.
        _layoutManager.SetAreaVisibility(WorkspaceArea.Side, true);

        _layoutManager.VisibleAreas.Should().BeEquivalentTo(WorkspaceAreaHelper.AllAreasVisible);
        _workspaceSettings.PreferredVisibleAreas.Should().BeEquivalentTo(WorkspaceAreaHelper.AllAreasVisible);
    }

    [Test]
    public void SetAreaVisibility_Main_FailsAndLeavesTheLayoutAlone()
    {
        var result = _layoutManager.SetAreaVisibility(WorkspaceArea.Main, false);

        result.IsFailure.Should().BeTrue();
        _layoutManager.IsAreaVisible(WorkspaceArea.Main).Should().BeTrue();
        _layoutManager.VisibleAreas.Should().BeEquivalentTo(WorkspaceAreaHelper.AllAreasVisible);
    }

    [Test]
    public void SetAreaVisibility_SameState_NoChange()
    {
        bool messageReceived = false;
        var recipient = new object();
        _messengerService.Register<AreaVisibilityChangedMessage>(recipient, (r, m) => messageReceived = true);

        _layoutManager.SetAreaVisibility(WorkspaceArea.Utility, true);

        messageReceived.Should().BeFalse();
    }

    [Test]
    public void ResetLayout_RestoresEveryAreaVisible()
    {
        _layoutManager.SetAreaVisibility(WorkspaceArea.Utility, false);
        _layoutManager.SetAreaVisibility(WorkspaceArea.Side, false);

        var result = _layoutManager.RequestLayoutTransition(LayoutTransition.ResetLayout);

        result.IsSuccess.Should().BeTrue();
        _layoutManager.VisibleAreas.Should().BeEquivalentTo(WorkspaceAreaHelper.AllAreasVisible);
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
    public void ResetLayout_ClearsTheHiddenAreasInWorkspaceSettings()
    {
        _layoutManager.RequestLayoutTransition(LayoutTransition.ResetLayout);

        AssertPersistedPreferredAreas(WorkspaceArea.Utility, WorkspaceArea.Bottom, WorkspaceArea.Side);
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
    public void AreaVisibilityChange_SendsAreaVisibilityChangedMessage()
    {
        AreaVisibilityChangedMessage? receivedMessage = null;
        var recipient = new object();
        _messengerService.Register<AreaVisibilityChangedMessage>(recipient, (r, m) => receivedMessage = m);

        _layoutManager.SetAreaVisibility(WorkspaceArea.Utility, false);

        receivedMessage.Should().NotBeNull();
        receivedMessage!.VisibleAreas.Should()
            .BeEquivalentTo(VisibleAreas(WorkspaceArea.Side, WorkspaceArea.Bottom));
    }

    [Test]
    public void RevealingAnArea_SendsFlashAreaMessage()
    {
        _layoutManager.SetAreaVisibility(WorkspaceArea.Utility, false);

        FlashAreaMessage? receivedMessage = null;
        var recipient = new object();
        _messengerService.Register<FlashAreaMessage>(recipient, (r, m) => receivedMessage = m);

        _layoutManager.SetAreaVisibility(WorkspaceArea.Utility, true);

        receivedMessage.Should().NotBeNull();
        receivedMessage!.Area.Should().Be(WorkspaceArea.Utility);
    }

    [Test]
    public void HidingAnArea_SendsNoFlashAreaMessage()
    {
        bool messageReceived = false;
        var recipient = new object();
        _messengerService.Register<FlashAreaMessage>(recipient, (r, m) => messageReceived = true);

        _layoutManager.SetAreaVisibility(WorkspaceArea.Utility, false);

        messageReceived.Should().BeFalse();
    }

    [Test]
    public void RevealingAnAreaFromFocus_FlashesOnlyTheRequestedArea()
    {
        // Focus hides every collapsible area, so asking for one back brings its neighbours with it.
        _layoutManager.RequestLayoutTransition(LayoutTransition.Focus);

        var flashedAreas = new List<WorkspaceArea>();
        var recipient = new object();
        _messengerService.Register<FlashAreaMessage>(recipient, (r, m) => flashedAreas.Add(m.Area));

        _layoutManager.SetAreaVisibility(WorkspaceArea.Bottom, true);

        _layoutManager.VisibleAreas.Should().BeEquivalentTo(WorkspaceAreaHelper.AllAreasVisible);
        flashedAreas.Should().Equal(WorkspaceArea.Bottom);
    }

    [Test]
    public void LayoutModeTransition_SendsNoFlashAreaMessage()
    {
        _layoutManager.RequestLayoutTransition(LayoutTransition.Focus);

        bool messageReceived = false;
        var recipient = new object();
        _messengerService.Register<FlashAreaMessage>(recipient, (r, m) => messageReceived = true);

        // Returning to Default brings back every area.
        _layoutManager.RequestLayoutTransition(LayoutTransition.Default);

        _layoutManager.VisibleAreas.Should().BeEquivalentTo(WorkspaceAreaHelper.AllAreasVisible);
        messageReceived.Should().BeFalse();
    }

    [Test]
    public void SetAreaVisibility_InDefaultMode_PersistsTheHiddenArea()
    {
        _layoutManager.SetAreaVisibility(WorkspaceArea.Utility, false);

        AssertPersistedPreferredAreas(WorkspaceArea.Bottom, WorkspaceArea.Side);
    }

    [Test]
    public void SetAreaVisibility_WhileFullScreen_PersistsTheHiddenArea()
    {
        // Fullscreen does not change the layout mode, so area changes still persist as preferred.
        _layoutManager.RequestLayoutTransition(LayoutTransition.ToggleFullScreen);
        _workspaceSettings.ClearReceivedCalls();

        _layoutManager.SetAreaVisibility(WorkspaceArea.Side, false);

        AssertPersistedPreferredAreas(WorkspaceArea.Utility, WorkspaceArea.Bottom);
    }

    [Test]
    public void SetAreaVisibility_HidingEveryArea_PersistsMainAsTheOnlyVisibleOne()
    {
        // Hide every collapsible area one by one in the Default layout.
        _layoutManager.SetAreaVisibility(WorkspaceArea.Utility, false);
        _layoutManager.SetAreaVisibility(WorkspaceArea.Side, false);
        _workspaceSettings.ClearReceivedCalls();

        // Hiding the last one persists Main alone, because the user explicitly chose to hide every area
        // that can be hidden.
        _layoutManager.SetAreaVisibility(WorkspaceArea.Bottom, false);

        AssertPersistedPreferredAreas();
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
        // Alignment is a layout preference rather than a mode, so unlike area visibility it is not
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
        _layoutManager.VisibleAreas.Should().BeEquivalentTo(WorkspaceAreaHelper.AllAreasVisible);
    }

    [Test]
    public void VisibleAreas_CombinationsWorkCorrectly()
    {
        _layoutManager.SetAreaVisibility(WorkspaceArea.Utility, false);
        _layoutManager.SetAreaVisibility(WorkspaceArea.Side, false);

        _layoutManager.VisibleAreas.Should().BeEquivalentTo(VisibleAreas(WorkspaceArea.Bottom));
        _layoutManager.IsAreaVisible(WorkspaceArea.Utility).Should().BeFalse();
        _layoutManager.IsAreaVisible(WorkspaceArea.Side).Should().BeFalse();
        _layoutManager.IsAreaVisible(WorkspaceArea.Bottom).Should().BeTrue();
    }

    // A visible set always holds Main, which cannot be hidden.
    private static IReadOnlySet<WorkspaceArea> VisibleAreas(params WorkspaceArea[] collapsibleAreas)
    {
        var visibleAreas = new HashSet<WorkspaceArea>(collapsibleAreas)
        {
            WorkspaceArea.Main
        };

        return visibleAreas;
    }

    // The setter is handed a fresh set each time, so the assertion matches on what the set holds rather than
    // on the instance the substitute captured. Main is always among them.
    private void AssertPersistedPreferredAreas(params WorkspaceArea[] collapsibleAreas)
    {
        var expectedAreas = VisibleAreas(collapsibleAreas);

        _workspaceSettings.Received().PreferredVisibleAreas =
            Arg.Is<IReadOnlySet<WorkspaceArea>>(visibleAreas => visibleAreas.SetEquals(expectedAreas));
    }
}
