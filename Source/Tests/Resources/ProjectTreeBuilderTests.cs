using Celbridge.Resources.Models;
using Celbridge.Resources.Services;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Direct tests for the project tree builder: gateway-driven walk, policy-based
/// filtering, folders-before-files ordering, fresh instances on every call.
/// Targets the builder rather than going through ResourceRegistry so the
/// project-scope filter rules can be exercised cleanly.
/// </summary>
[TestFixture]
public class ProjectTreeBuilderTests
{
    private string _projectFolderPath = null!;
    private ProjectTreeBuilder _builder = null!;

    [SetUp]
    public void Setup()
    {
        _projectFolderPath = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(ProjectTreeBuilderTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectFolderPath);

        _builder = ProjectTreeBuilderTestHelper.Build(_projectFolderPath);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_projectFolderPath))
        {
            try
            {
                Directory.Delete(_projectFolderPath, true);
            }
            catch
            {
                // Best effort
            }
        }
    }

    [Test]
    public async Task BuildTree_ProducesFolderResource_WithProjectAsRoot()
    {
        var buildResult = await _builder.BuildTreeAsync();

        buildResult.IsSuccess.Should().BeTrue();
        var tree = buildResult.Value;
        tree.Should().NotBeNull();
        tree.Name.Should().BeEmpty();
        tree.ParentFolder.Should().BeNull();
        tree.Children.Should().BeEmpty();
    }

    [Test]
    public async Task BuildTree_AddsFilesAndFolders()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "root.txt"), "x");
        Directory.CreateDirectory(Path.Combine(_projectFolderPath, "sub"));
        File.WriteAllText(Path.Combine(_projectFolderPath, "sub", "child.md"), "y");

        var buildResult = await _builder.BuildTreeAsync();

        buildResult.IsSuccess.Should().BeTrue();
        var tree = buildResult.Value;
        tree.Children.Should().HaveCount(2);

        // Folders sort before files; "sub" comes first.
        tree.Children[0].Should().BeOfType<FolderResource>();
        tree.Children[0].Name.Should().Be("sub");

        tree.Children[1].Should().BeOfType<FileResource>();
        tree.Children[1].Name.Should().Be("root.txt");

        var sub = (FolderResource)tree.Children[0];
        sub.Children.Should().HaveCount(1);
        sub.Children[0].Name.Should().Be("child.md");
    }

    [Test]
    public async Task BuildTree_ExcludesDotPrefixedFiles_AndDotPrefixedFolders()
    {
        // Leading-dot names are project-hidden (covers .celbridge plus any
        // editor scratch files like .gitignore, .vscode/, etc.).
        File.WriteAllText(Path.Combine(_projectFolderPath, ".gitignore"), "x");
        File.WriteAllText(Path.Combine(_projectFolderPath, "visible.txt"), "y");
        Directory.CreateDirectory(Path.Combine(_projectFolderPath, ".vscode"));
        Directory.CreateDirectory(Path.Combine(_projectFolderPath, "src"));

        var buildResult = await _builder.BuildTreeAsync();

        buildResult.IsSuccess.Should().BeTrue();
        var tree = buildResult.Value;
        tree.Children.Select(c => c.Name).Should().BeEquivalentTo(new[] { "src", "visible.txt" });
    }

    [Test]
    public async Task BuildTree_ShowsFileWithWindowsHiddenAttribute()
    {
        // Visibility is decided by patterns, not the OS hidden attribute, so a
        // normally-named file stays visible even when Windows marks it hidden.
        // This keeps the resource tree identical across platforms.
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("Windows hidden attribute is only settable on Windows.");
        }

        var hiddenFilePath = Path.Combine(_projectFolderPath, "notes.txt");
        File.WriteAllText(hiddenFilePath, "x");
        File.SetAttributes(hiddenFilePath, File.GetAttributes(hiddenFilePath) | FileAttributes.Hidden);

        var buildResult = await _builder.BuildTreeAsync();

        buildResult.IsSuccess.Should().BeTrue();
        var tree = buildResult.Value;
        tree.Children.Select(c => c.Name).Should().Contain("notes.txt");
    }

    [Test]
    public async Task BuildTree_ExcludesPyCacheFolders()
    {
        Directory.CreateDirectory(Path.Combine(_projectFolderPath, "scripts", "__pycache__"));
        File.WriteAllText(Path.Combine(_projectFolderPath, "scripts", "__pycache__", "x.pyc"), "");
        File.WriteAllText(Path.Combine(_projectFolderPath, "scripts", "main.py"), "");

        var buildResult = await _builder.BuildTreeAsync();

        buildResult.IsSuccess.Should().BeTrue();
        var tree = buildResult.Value;
        var scripts = (FolderResource)tree.Children.Single(c => c.Name == "scripts");
        scripts.Children.Select(c => c.Name).Should().BeEquivalentTo(new[] { "main.py" });
    }

    [Test]
    public async Task BuildTree_ExcludesPythonLibFolder_OnlyWhenParentIsPython()
    {
        // Python/Lib is excluded (virtualenv pip packages). A "Lib" folder
        // anywhere else stays — the exclusion is keyed on the parent name.
        Directory.CreateDirectory(Path.Combine(_projectFolderPath, "Python", "Lib"));
        File.WriteAllText(Path.Combine(_projectFolderPath, "Python", "Lib", "pkg.py"), "");
        File.WriteAllText(Path.Combine(_projectFolderPath, "Python", "main.py"), "");

        Directory.CreateDirectory(Path.Combine(_projectFolderPath, "OtherProject", "Lib"));
        File.WriteAllText(Path.Combine(_projectFolderPath, "OtherProject", "Lib", "thing.txt"), "");

        var buildResult = await _builder.BuildTreeAsync();

        buildResult.IsSuccess.Should().BeTrue();
        var tree = buildResult.Value;
        var python = (FolderResource)tree.Children.Single(c => c.Name == "Python");
        python.Children.Select(c => c.Name).Should().BeEquivalentTo(new[] { "main.py" });

        var other = (FolderResource)tree.Children.Single(c => c.Name == "OtherProject");
        var otherLib = (FolderResource)other.Children.Single(c => c.Name == "Lib");
        otherLib.Children.Single().Name.Should().Be("thing.txt");
    }

    [Test]
    public async Task BuildTree_ReturnsFreshInstances_EveryCall()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "stable.txt"), "x");

        var firstResult = await _builder.BuildTreeAsync();
        var secondResult = await _builder.BuildTreeAsync();

        firstResult.IsSuccess.Should().BeTrue();
        secondResult.IsSuccess.Should().BeTrue();
        var first = firstResult.Value;
        var second = secondResult.Value;

        // Each call rebuilds the tree from scratch so stale UI-bound references
        // do not survive an undo/redo or rapid rebuild.
        first.Should().NotBeSameAs(second);
        first.Children[0].Should().NotBeSameAs(second.Children[0]);
    }

    [Test]
    public async Task BuildTree_FilesGetIcons()
    {
        File.WriteAllText(Path.Combine(_projectFolderPath, "foo.txt"), "x");

        var buildResult = await _builder.BuildTreeAsync();

        buildResult.IsSuccess.Should().BeTrue();
        var tree = buildResult.Value;
        var file = (FileResource)tree.Children.Single();
        file.Icon.Should().NotBeNull();
    }
}
