using Celbridge.Messaging;
using Celbridge.Messaging.Services;
using Celbridge.Settings;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Services;
using Celbridge.Utilities;
using Celbridge.Workspace;
using Celbridge.WorkspaceUI.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Tests.WorkspaceUI;

/// <summary>
/// Tests that changing the Bottom area alignment brings the area into view, so the alignment the user
/// picked is something they can see.
/// </summary>
[TestFixture]
public class SetBottomAreaAlignmentCommandTests
{
    private ServiceProvider? _serviceProvider;
    private LayoutManager _layoutManager = null!;

    [SetUp]
    public void Setup()
    {
        var services = new ServiceCollection();

        Logging.ServiceConfiguration.ConfigureServices(services);
        services.AddSingleton<IMessengerService, MessengerService>();

        _serviceProvider = services.BuildServiceProvider();

        var messengerService = _serviceProvider.GetRequiredService<IMessengerService>();
        var settingsService = Substitute.For<ISettingsService>();

        var workspaceSettings = Substitute.For<IBindableWorkspaceSettings>();
        workspaceSettings.PreferredVisibleAreas = WorkspaceAreaHelper.AllAreasVisible;

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceWrapper.IsWorkspaceLoaded.Returns(true);
        workspaceWrapper.WorkspaceService.Returns(workspaceService);
        workspaceService.BindableWorkspaceSettings.Returns(workspaceSettings);

        var logger = _serviceProvider.GetRequiredService<ILogger<LayoutManager>>();

        _layoutManager = new LayoutManager(logger, messengerService, settingsService, workspaceWrapper);
    }

    [TearDown]
    public void TearDown()
    {
        (_serviceProvider as IDisposable)?.Dispose();
    }

    [Test]
    public async Task Execute_HiddenBottomArea_AppliesAlignmentAndShowsTheArea()
    {
        _layoutManager.SetAreaVisibility(WorkspaceArea.Bottom, false);

        var command = new SetBottomAreaAlignmentCommand(_layoutManager)
        {
            Alignment = BottomAreaAlignment.Justify
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        _layoutManager.BottomAreaAlignment.Should().Be(BottomAreaAlignment.Justify);
        _layoutManager.IsAreaVisible(WorkspaceArea.Bottom).Should().BeTrue();
    }

    [Test]
    public async Task Execute_VisibleBottomArea_LeavesTheOtherAreasAlone()
    {
        var command = new SetBottomAreaAlignmentCommand(_layoutManager)
        {
            Alignment = BottomAreaAlignment.Left
        };

        await command.ExecuteAsync();

        _layoutManager.VisibleAreas.Should().BeEquivalentTo(WorkspaceAreaHelper.AllAreasVisible);
        _layoutManager.LayoutMode.Should().Be(LayoutMode.Default);
    }

    [Test]
    public async Task Execute_InFocusMode_ShowsTheAreaAndReturnsToTheDefaultLayout()
    {
        // Focus mode hides every collapsible area, so revealing the Bottom area is the same customization
        // as toggling it by hand and leaves the mode behind.
        _layoutManager.RequestLayoutTransition(LayoutTransition.Focus);

        var command = new SetBottomAreaAlignmentCommand(_layoutManager)
        {
            Alignment = BottomAreaAlignment.Right
        };

        await command.ExecuteAsync();

        _layoutManager.BottomAreaAlignment.Should().Be(BottomAreaAlignment.Right);
        _layoutManager.IsAreaVisible(WorkspaceArea.Bottom).Should().BeTrue();
        _layoutManager.LayoutMode.Should().Be(LayoutMode.Default);
    }
}
