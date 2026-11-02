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
using Celbridge.UserInterface;
using Celbridge.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Tests for the status half of the editor bridge. The host checks each new status from a page before any surface
/// draws it, and a page that starts again starts with no status.
/// </summary>
[TestFixture]
public class CustomDocumentHandlerStatusTests
{
    private IServiceProvider? _previousServiceProvider;
    private CustomDocumentViewModel _viewModel = null!;
    private CustomDocumentHandler _handler = null!;
    private List<DocumentStatus?> _reportedStatuses = null!;

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

        _reportedStatuses = new List<DocumentStatus?>();

        var metadata = new DocumentMetadata("C:/Projects/Acme/notes.md", "notes.md", "notes.md", "en");

        _handler = new CustomDocumentHandler(
            _viewModel,
            Substitute.For<ILogger<CustomDocumentHandlerStatusTests>>(),
            Substitute.For<IProjectService>(),
            Substitute.For<IReportWriter>(),
            () => metadata,
            () => throw new NotSupportedException(),
            status => _reportedStatuses.Add(status));
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

    [TestCase("info", StatusKind.Info)]
    [TestCase("success", StatusKind.Success)]
    [TestCase("warning", StatusKind.Warning)]
    [TestCase("critical", StatusKind.Critical)]
    public async Task SetStatus_ReportsTheKindAndTheLabel(string kind, StatusKind expectedKind)
    {
        await _handler.SetStatusAsync(kind, "Syncing");

        _reportedStatuses.Should().Equal(new DocumentStatus(expectedKind, "Syncing"));
    }

    [Test]
    public async Task SetStatus_AnUnknownKind_IsRefused()
    {
        var setStatus = () => _handler.SetStatusAsync("urgent", "Syncing");

        await setStatus.Should().ThrowAsync<ArgumentException>().WithMessage("*'urgent'*");
        _reportedStatuses.Should().BeEmpty();
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("\n\n")]
    public async Task SetStatus_AnEmptyLabel_IsRefused(string label)
    {
        var setStatus = () => _handler.SetStatusAsync("info", label);

        await setStatus.Should().ThrowAsync<ArgumentException>();
        _reportedStatuses.Should().BeEmpty();
    }

    [Test]
    public async Task SetStatus_KeepsOnlyTheFirstLineOfTheLabel()
    {
        await _handler.SetStatusAsync("critical", "Syncing\r\n3 files left");

        _reportedStatuses.Should().Equal(new DocumentStatus(StatusKind.Critical, "Syncing"));
    }

    [Test]
    public async Task ClearStatus_ReportsNoStatus()
    {
        await _handler.ClearStatusAsync();

        _reportedStatuses.Should().Equal(new DocumentStatus?[] { null });
    }

    [Test]
    public async Task Initialize_ClearsTheStatus()
    {
        await _handler.InitializeAsync(DocumentRpcMethods.CurrentProtocolVersion);

        _reportedStatuses.Should().Equal(new DocumentStatus?[] { null });
    }
}
