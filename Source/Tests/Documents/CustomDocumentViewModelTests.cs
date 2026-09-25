using Celbridge.Documents.ViewModels;
using Celbridge.Messaging;
using Celbridge.Messaging.Services;
using Celbridge.Resources;
using Celbridge.Tests.FileSystem;
using Celbridge.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Tests.Documents;

/// <summary>
/// A custom editor such as the markdown preview reports each link clicked in its document, and the view model
/// resolves the link to the project resource it names. The resource pickers go the other way, turning a picked
/// resource into a path relative to the document. These tests pin both, including for a document at the top
/// of the project.
/// </summary>
[TestFixture]
public class CustomDocumentViewModelTests
{
    private IMessengerService _messengerService = null!;
    private IResourceRegistry _registry = null!;
    private IServiceProvider? _previousServiceProvider;
    private CustomDocumentViewModel _viewModel = null!;

    [SetUp]
    public void Setup()
    {
        _messengerService = new MessengerService();

        _registry = Substitute.For<IResourceRegistry>();

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(_registry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        // The view model acquires the messenger through the global ServiceLocator, so the locator is restored
        // afterwards exactly as this fixture found it.
        var services = new ServiceCollection();
        services.AddSingleton(_messengerService);
        _previousServiceProvider = ServiceLocator.ServiceProvider;
        ServiceLocator.Initialize(services.BuildServiceProvider());

        _viewModel = new CustomDocumentViewModel(workspaceWrapper, TestFileSystem.CreateLocal(), []);
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

    [TestCase("docs/notes.md", "other.md", "docs/other.md")]
    [TestCase("docs/notes.md", "./other.md", "docs/other.md")]
    [TestCase("docs/notes.md", "sub/", "docs/sub")]
    [TestCase("docs/notes.md", "../readme.md", "readme.md")]
    [TestCase("docs/notes.md", "/images/logo.png", "images/logo.png")]
    [TestCase("readme.md", "docs/notes.md", "docs/notes.md")]
    [TestCase("readme.md", "./docs/notes.md", "docs/notes.md")]
    [TestCase("readme.md", "/docs/notes.md", "docs/notes.md")]
    public void AProjectLink_ResolvesToTheResourceItNames(string document, string href, string expectedPath)
    {
        _viewModel.FileResource = new ResourceKey(document);

        var result = _viewModel.ResolveLinkTarget(href);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new ResourceKey(expectedPath));
    }

    [Test]
    public void AProjectLink_IsNotCheckedToExist()
    {
        // A link to a file the project does not have still resolves, so the broken link can be reported by its
        // project path.
        _viewModel.FileResource = new ResourceKey("docs/notes.md");

        var result = _viewModel.ResolveLinkTarget("missing.md");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new ResourceKey("docs/missing.md"));
        _registry.DidNotReceive().NormalizeResourceKey(Arg.Any<ResourceKey>());
    }

    [TestCase("other.md#install")]
    [TestCase("other.md?tab=2")]
    [TestCase("other.md?tab=2#install")]
    public void ALinkWithAQueryOrFragment_ResolvesToTheFileItNames(string href)
    {
        _viewModel.FileResource = new ResourceKey("docs/notes.md");

        var result = _viewModel.ResolveLinkTarget(href);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new ResourceKey("docs/other.md"));
    }

    [TestCase("my%20notes.md", "docs/my notes.md")]
    [TestCase("a%23b.md", "docs/a#b.md")]
    public void APercentEncodedLink_ResolvesToTheDecodedName(string href, string expectedPath)
    {
        _viewModel.FileResource = new ResourceKey("docs/notes.md");

        var result = _viewModel.ResolveLinkTarget(href);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new ResourceKey(expectedPath));
    }

    [TestCase("#install")]
    [TestCase("?tab=2")]
    public void ALinkThatIsOnlyAQueryOrFragment_ResolvesToItsOwnDocument(string href)
    {
        _viewModel.FileResource = new ResourceKey("docs/notes.md");

        var result = _viewModel.ResolveLinkTarget(href);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new ResourceKey("docs/notes.md"));
    }

    [TestCase("https://example.com/")]
    [TestCase("http://example.com/page.html")]
    public void AWebLink_ResolvesToNoResource(string href)
    {
        _viewModel.FileResource = new ResourceKey("docs/notes.md");

        var result = _viewModel.ResolveLinkTarget(href);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsEmpty.Should().BeTrue();
    }

    [TestCase("docs/notes.md", "../../outside.md")]
    [TestCase("docs/notes.md", "../../../outside.md")]
    [TestCase("readme.md", "../outside.md")]
    [TestCase("readme.md", "../../outside.md")]
    [TestCase("docs/notes.md", "../")]
    public void ALinkThatNamesNoResourceInTheProject_Fails(string document, string href)
    {
        _viewModel.FileResource = new ResourceKey(document);

        var result = _viewModel.ResolveLinkTarget(href);

        result.IsFailure.Should().BeTrue();
    }

    [TestCase("docs/notes.md", "docs/logo.png", "logo.png")]
    [TestCase("docs/notes.md", "images/logo.png", "../images/logo.png")]
    [TestCase("docs/a/notes.md", "docs/b/logo.png", "../b/logo.png")]
    [TestCase("readme.md", "images/logo.png", "images/logo.png")]
    public void APickedResource_IsGivenAPathRelativeToTheDocument(string document, string pickedPath, string expectedPath)
    {
        _viewModel.FileResource = new ResourceKey(document);

        var relativePath = _viewModel.GetRelativePathFromResourceKey(pickedPath);

        relativePath.Should().Be(expectedPath);
    }
}
