using Celbridge.Resources.Services;
using Celbridge.Resources.Services.Roots;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Direct tests for the cross-root dispatch logic: longest-prefix-wins match,
/// IsResolvable across roots, raw resolve via the matched handler, and
/// InvalidatePathCache propagation. Pulled out of ResourceRegistryTests so
/// the root-registration concern can be exercised on its own surface.
/// </summary>
[TestFixture]
public class RootHandlerRegistryTests
{
    private string _projectFolderPath = null!;
    private RootHandlerRegistry _rootRegistry = null!;

    [SetUp]
    public void Setup()
    {
        _projectFolderPath = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(RootHandlerRegistryTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectFolderPath);

        _rootRegistry = new RootHandlerRegistry();
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
    public void RegisterRootHandler_AddsHandlerKeyedByRootName()
    {
        var projectHandler = new ProjectRootHandler(_projectFolderPath);

        _rootRegistry.RegisterRootHandler(projectHandler);

        _rootRegistry.RootHandlers.Should().ContainKey(ResourceKey.DefaultRoot);
        _rootRegistry.RootHandlers[ResourceKey.DefaultRoot].Should().BeSameAs(projectHandler);
    }

    [Test]
    public void RegisterRootHandler_ReplacesExistingHandlerForSameRoot()
    {
        var firstHandler = new ProjectRootHandler(_projectFolderPath);
        var alternatePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(alternatePath);

        try
        {
            var secondHandler = new ProjectRootHandler(alternatePath);

            _rootRegistry.RegisterRootHandler(firstHandler);
            _rootRegistry.RegisterRootHandler(secondHandler);

            _rootRegistry.RootHandlers[ResourceKey.DefaultRoot].Should().BeSameAs(secondHandler);
        }
        finally
        {
            Directory.Delete(alternatePath, true);
        }
    }

    [Test]
    public void IsResolvable_ReturnsTrueForRegisteredRoot_FalseOtherwise()
    {
        _rootRegistry.RegisterRootHandler(
            new ProjectRootHandler(_projectFolderPath));

        _rootRegistry.IsResolvable(ResourceKey.Create("foo/bar")).Should().BeTrue();
        _rootRegistry.IsResolvable(ResourceKey.Empty).Should().BeTrue();
        _rootRegistry.IsResolvable(ResourceKey.Create("temp:foo")).Should().BeFalse();
    }

    [Test]
    public void GetResourceKey_DispatchesToLongestPrefixRoot()
    {
        var tempBacking = Path.Combine(_projectFolderPath, ".celbridge", "temp");
        Directory.CreateDirectory(tempBacking);

        _rootRegistry.RegisterRootHandler(
            new ProjectRootHandler(_projectFolderPath));
        _rootRegistry.RegisterRootHandler(
            new TempRootHandler(tempBacking));

        // Path under both roots — temp wins because its backing prefix is longer.
        var tempPath = Path.Combine(tempBacking, "staging", "x.txt");
        var tempKey = _rootRegistry.GetResourceKey(tempPath);
        tempKey.IsSuccess.Should().BeTrue();
        tempKey.Value.Root.Should().Be("temp");
        tempKey.Value.Path.Should().Be("staging/x.txt");

        // Path under project only.
        File.WriteAllText(Path.Combine(_projectFolderPath, "root.txt"), "x");
        var projectKey = _rootRegistry.GetResourceKey(Path.Combine(_projectFolderPath, "root.txt"));
        projectKey.IsSuccess.Should().BeTrue();
        projectKey.Value.Root.Should().Be(ResourceKey.DefaultRoot);
        projectKey.Value.Path.Should().Be("root.txt");
    }

    [Test]
    public void GetResourceKey_MatchesCaseInsensitivelyOnCaseInsensitiveFileSystems()
    {
        _rootRegistry.RegisterRootHandler(
            new ProjectRootHandler(_projectFolderPath));

        // A path whose backing-location portion differs only in case from how the
        // root was registered. On a case-insensitive volume (Windows, default APFS)
        // the dispatch must still recognise it as under the project root; a
        // case-sensitive volume (Linux) correctly rejects it.
        var differentlyCasedBacking = _projectFolderPath.ToUpperInvariant();
        var fullPath = Path.Combine(differentlyCasedBacking, "Notes", "todo.txt");

        var result = _rootRegistry.GetResourceKey(fullPath);

        if (OperatingSystem.IsLinux())
        {
            result.IsFailure.Should().BeTrue();
            result.FirstErrorMessage.Should().Contain("not under any registered resource root");
        }
        else
        {
            result.IsSuccess.Should().BeTrue();
            result.Value.Root.Should().Be(ResourceKey.DefaultRoot);
            result.Value.Path.Should().Be("Notes/todo.txt");
        }
    }

    [Test]
    public void GetResourceKey_FailsForPathOutsideEveryRoot()
    {
        _rootRegistry.RegisterRootHandler(
            new ProjectRootHandler(_projectFolderPath));

        var outsidePath = Path.Combine(Path.GetTempPath(), "somewhere_else", "file.txt");
        var result = _rootRegistry.GetResourceKey(outsidePath);

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("not under any registered resource root");
    }

    [Test]
    public void ResolveResourcePath_DelegatesToRegisteredHandler()
    {
        _rootRegistry.RegisterRootHandler(
            new ProjectRootHandler(_projectFolderPath));

        var resolved = _rootRegistry.ResolveResourcePath(ResourceKey.Create("a/b.txt"));

        resolved.IsSuccess.Should().BeTrue();
        resolved.Value.Should().Be(Path.GetFullPath(Path.Combine(_projectFolderPath, "a", "b.txt")));
    }

    [Test]
    public void ResolveResourcePath_FailsForUnregisteredRoot()
    {
        var result = _rootRegistry.ResolveResourcePath(ResourceKey.Create("temp:x"));

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("'temp'");
        result.FirstErrorMessage.Should().Contain("not registered");
    }
}
