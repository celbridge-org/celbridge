using Celbridge.Documents.ViewModels;
using Celbridge.Documents.Views;
using Celbridge.Host;
using Celbridge.Messaging;
using Celbridge.Messaging.Services;
using Celbridge.Projects;
using Celbridge.Reports;
using Celbridge.Resources;
using Celbridge.Tests.FileSystem;
using Celbridge.Tests.Helpers;
using Celbridge.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Tests for the editor bridge's pip calls. The host checks each new pip from a page before any surface draws it,
/// and a page that starts again starts with no pip.
/// </summary>
[TestFixture]
public class CustomDocumentRpcHandlerPipTests
{
    private IServiceProvider? _previousServiceProvider;
    private CustomDocumentViewModel _viewModel = null!;
    private CustomDocumentRpcHandler _handler = null!;
    private List<WorkspaceItemPip?> _reportedPips = null!;

    [SetUp]
    public void Setup()
    {
        // Loading content reads the file's info first. A file that is absent loads as an empty document.
        var resourceFileSystem = Substitute.For<IResourceFileSystem>();
        resourceFileSystem.GetInfoAsync(Arg.Any<ResourceKey>())
            .Returns(Result<StorageItemInfo>.Fail("not found"));

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Monitor.Returns(new FakeResourceMonitor());
        resourceService.FileSystem.Returns(resourceFileSystem);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        // The view model acquires the messenger through the global ServiceLocator, so the locator is restored
        // afterwards exactly as this fixture found it.
        var services = new ServiceCollection();
        services.AddSingleton<IMessengerService>(new MessengerService());
        services.AddSingleton(workspaceWrapper);
        _previousServiceProvider = ServiceLocator.ServiceProvider;
        ServiceLocator.Initialize(services.BuildServiceProvider());

        _viewModel = new CustomDocumentViewModel(workspaceWrapper, TestFileSystem.CreateLocal(), []);

        _reportedPips = new List<WorkspaceItemPip?>();

        var metadata = new DocumentMetadata("C:/Projects/Acme/notes.md", "notes.md", "notes.md", "en");

        _handler = new CustomDocumentRpcHandler(
            _viewModel,
            Substitute.For<ILogger<CustomDocumentRpcHandlerPipTests>>(),
            Substitute.For<IProjectService>(),
            Substitute.For<IReportWriter>(),
            new FakeResourceMonitor(),
            () => metadata,
            () => throw new NotSupportedException(),
            pip => _reportedPips.Add(pip),
            (watchId, changes) => throw new NotSupportedException());
    }

    [TearDown]
    public void TearDown()
    {
        _viewModel.Cleanup();

        if (_previousServiceProvider is not null)
        {
            ServiceLocator.Initialize(_previousServiceProvider);
        }
        else
        {
            ServiceLocator.Reset();
        }
    }

    [TestCase("neutral", PipKind.Neutral)]
    [TestCase("success", PipKind.Success)]
    [TestCase("notice", PipKind.Notice)]
    [TestCase("alert", PipKind.Alert)]
    public async Task SetPip_ReportsTheKindAndTheLabel(string kind, PipKind expectedKind)
    {
        await _handler.SetPipAsync(kind, "Syncing");

        _reportedPips.Should().Equal(new WorkspaceItemPip(expectedKind, "Syncing", IsPulsing: false));
    }

    [Test]
    public async Task SetPip_Pulses_WhenThePageAsks()
    {
        await _handler.SetPipAsync("neutral", "Syncing", pulse: true);

        _reportedPips.Should().Equal(new WorkspaceItemPip(PipKind.Neutral, "Syncing", IsPulsing: true));
    }

    [Test]
    public async Task SetPip_AnUnknownKind_IsRefused()
    {
        var setPip = () => _handler.SetPipAsync("urgent", "Syncing");

        await setPip.Should().ThrowAsync<ArgumentException>().WithMessage("*'urgent'*");
        _reportedPips.Should().BeEmpty();
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("\n\n")]
    public async Task SetPip_AnEmptyLabel_IsRefused(string label)
    {
        var setPip = () => _handler.SetPipAsync("neutral", label);

        await setPip.Should().ThrowAsync<ArgumentException>();
        _reportedPips.Should().BeEmpty();
    }

    [Test]
    public async Task SetPip_KeepsOnlyTheFirstLineOfTheLabel()
    {
        await _handler.SetPipAsync("alert", "Syncing\r\n3 files left");

        _reportedPips.Should().Equal(new WorkspaceItemPip(PipKind.Alert, "Syncing", IsPulsing: false));
    }

    [Test]
    public async Task ClearPip_ReportsNoPip()
    {
        await _handler.ClearPipAsync();

        _reportedPips.Should().Equal(new WorkspaceItemPip?[] { null });
    }

    [Test]
    public async Task Initialize_ClearsThePip()
    {
        await _handler.InitializeAsync(DocumentRpcMethods.CurrentProtocolVersion);

        _reportedPips.Should().Equal(new WorkspaceItemPip?[] { null });
    }
}
