using Celbridge.Resources.Services;
using Celbridge.Resources.Services.Roots;
using Celbridge.Server.Services;
using Celbridge.Tests.FileSystem;

namespace Celbridge.Tests.Server;

/// <summary>
/// Tests for the check the file server makes before it serves a project file. A URL path is judged as a
/// resource key, so a reserved folder, a link, a path through a link and a path in the wrong letter case are
/// refused.
/// </summary>
[TestFixture]
public class FileServerTests
{
    private string _testFolder = null!;
    private string _projectFolder = null!;
    private string _outsideFolder = null!;
    private RootHandlerRegistry _rootHandlers = null!;
    private ResourcePolicy _policy = null!;

    [SetUp]
    public void Setup()
    {
        _testFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(FileServerTests), Guid.NewGuid().ToString("N"));
        _projectFolder = Path.Combine(_testFolder, "Project");
        _outsideFolder = Path.Combine(_testFolder, "Outside");
        Directory.CreateDirectory(Path.Combine(_projectFolder, "docs"));
        Directory.CreateDirectory(Path.Combine(_projectFolder, ".git"));
        Directory.CreateDirectory(_outsideFolder);

        File.WriteAllText(Path.Combine(_projectFolder, "docs", "plain.txt"), "plain");
        File.WriteAllText(Path.Combine(_projectFolder, ".git", "config"), "git");
        File.WriteAllText(Path.Combine(_outsideFolder, "secret.txt"), "secret");
        File.CreateSymbolicLink(Path.Combine(_projectFolder, "docs", "linked.txt"), Path.Combine(_outsideFolder, "secret.txt"));
        Directory.CreateSymbolicLink(Path.Combine(_projectFolder, "linked"), _outsideFolder);

        _rootHandlers = new RootHandlerRegistry();
        _rootHandlers.RegisterRootHandler(new ProjectRootHandler(_projectFolder));
        _policy = TestResourcePolicy.CreateDefault();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testFolder))
        {
            Directory.Delete(_testFolder, recursive: true);
        }
    }

    [Test]
    public void CheckServedFile_PlainFile_IsServed()
    {
        var result = FileServer.CheckServedFile(_rootHandlers, _policy, ResourceKey.Empty, "docs/plain.txt");

        result.IsSuccess.Should().BeTrue(result.MessageChain);
    }

    [TestCase("docs/linked.txt")]
    [TestCase("linked/secret.txt")]
    [TestCase(".git/config")]
    [TestCase(".GIT/config")]
    [TestCase("../Outside/secret.txt")]
    public void CheckServedFile_IsRefused(string path)
    {
        // Resolving the folder first caches it, so the linked file is refused for itself.
        FileServer.CheckServedFile(_rootHandlers, _policy, ResourceKey.Empty, "docs/plain.txt");

        var result = FileServer.CheckServedFile(_rootHandlers, _policy, ResourceKey.Empty, path);

        result.IsFailure.Should().BeTrue();
    }

    [TestCase("DOCS/plain.txt")]
    [TestCase("docs/Plain.txt")]
    [Platform(Exclude = "Linux", Reason = "On a case-sensitive volume, names that differ in case name different items.")]
    public void CheckServedFile_PathInTheWrongCase_IsRefused(string path)
    {
        var result = FileServer.CheckServedFile(_rootHandlers, _policy, ResourceKey.Empty, path);

        result.IsFailure.Should().BeTrue();
        result.MessageChain.Should().Contain("does not match the name stored on disk");
    }

    [Test]
    public void CheckServedFile_FileInAPackageFolder_IsJudgedByItsKey()
    {
        var packageFolder = new ResourceKey("docs");

        FileServer.CheckServedFile(_rootHandlers, _policy, packageFolder, "plain.txt").IsSuccess.Should().BeTrue();
        FileServer.CheckServedFile(_rootHandlers, _policy, packageFolder, "linked.txt").IsFailure.Should().BeTrue();
    }

    [Test]
    public void Disable_UnregistersEveryPackageFolder()
    {
        var packageFolder = Path.Combine(_testFolder, "Package");
        Directory.CreateDirectory(packageFolder);
        File.WriteAllText(Path.Combine(packageFolder, "index.html"), "page");
        using var fileServer = new FileServer(Substitute.For<ILogger<FileServer>>());
        fileServer.RegisterPackageFolder("acme", packageFolder);
        fileServer.ResolvePackageFile("acme", "index.html")!.Exists.Should().BeTrue();

        fileServer.Disable();

        fileServer.ResolvePackageFile("acme", "index.html").Should().BeNull();
    }
}
