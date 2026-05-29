using Celbridge.Resources.Helpers;

namespace Celbridge.Tests.Resources;

[TestFixture]
public class RootPathResolverTests
{
    private string? _projectFolder;

    [SetUp]
    public void Setup()
    {
        _projectFolder = Path.Combine(Path.GetTempPath(), $"Celbridge/{nameof(RootPathResolverTests)}");
        if (Directory.Exists(_projectFolder))
        {
            Directory.Delete(_projectFolder, true);
        }
        Directory.CreateDirectory(_projectFolder);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_projectFolder))
        {
            Directory.Delete(_projectFolder!, true);
        }
    }

    [Test]
    public void ValidateAndResolveSucceedsForValidKey()
    {
        Guard.IsNotNull(_projectFolder);

        var resolver = new RootPathResolver(ResourceKey.DefaultRoot, _projectFolder);
        var resourceKey = ResourceKey.Create("folder/file.txt");

        var resolveResult = resolver.ValidateAndResolve(resourceKey);

        var expectedPath = Path.GetFullPath(Path.Combine(_projectFolder, "folder", "file.txt"));
        resolveResult.IsSuccess.Should().BeTrue();
        resolveResult.Value.Should().Be(expectedPath);
    }

    [Test]
    public void ValidateAndResolveSucceedsForEmptyKey()
    {
        Guard.IsNotNull(_projectFolder);

        var resolver = new RootPathResolver(ResourceKey.DefaultRoot, _projectFolder);

        var resolveResult = resolver.ValidateAndResolve(ResourceKey.Empty);

        var expectedPath = Path.GetFullPath(_projectFolder);
        resolveResult.IsSuccess.Should().BeTrue();
        resolveResult.Value.Should().Be(expectedPath);
    }

    [Test]
    public void ValidateAndResolveCachesVerifiedFolders()
    {
        Guard.IsNotNull(_projectFolder);

        // Create the folder on disk so the first call does a full check
        var subFolder = Path.Combine(_projectFolder, "cached");
        Directory.CreateDirectory(subFolder);
        File.WriteAllText(Path.Combine(subFolder, "a.txt"), "test");

        var resolver = new RootPathResolver(ResourceKey.DefaultRoot, _projectFolder);

        // First call — verifies the folder
        resolver.ValidateAndResolve(ResourceKey.Create("cached/a.txt"));

        // Second call — should hit the cache (no way to assert directly, but it should not throw)
        resolver.ValidateAndResolve(ResourceKey.Create("cached/b.txt"));
    }

    [Test]
    public void InvalidateCacheClearsVerifiedFolders()
    {
        Guard.IsNotNull(_projectFolder);

        var subFolder = Path.Combine(_projectFolder, "ephemeral");
        Directory.CreateDirectory(subFolder);
        File.WriteAllText(Path.Combine(subFolder, "a.txt"), "test");

        var resolver = new RootPathResolver(ResourceKey.DefaultRoot, _projectFolder);

        // Cache the folder
        resolver.ValidateAndResolve(ResourceKey.Create("ephemeral/a.txt"));

        // Invalidate
        resolver.InvalidateCache();

        // Next call should re-verify (still succeeds since folder is clean)
        var resolveResult = resolver.ValidateAndResolve(ResourceKey.Create("ephemeral/a.txt"));
        resolveResult.IsSuccess.Should().BeTrue();
        resolveResult.Value.Should().NotBeEmpty();
    }

    [Test]
    public void ValidateAndResolveRejectsReparsePoint()
    {
        Guard.IsNotNull(_projectFolder);

        var outsideFolder = Path.Combine(
            Path.GetTempPath(), $"Celbridge/{nameof(RootPathResolverTests)}_outside");
        Directory.CreateDirectory(outsideFolder);

        var symlinkPath = Path.Combine(_projectFolder, "link_folder");

        try
        {
            Directory.CreateSymbolicLink(symlinkPath, outsideFolder);
        }
        catch (Exception)
        {
            Assert.Ignore("Cannot create symbolic links on this system");
            return;
        }

        try
        {
            var resolver = new RootPathResolver(ResourceKey.DefaultRoot, _projectFolder);

            var resolveResult = resolver.ValidateAndResolve(ResourceKey.Create("link_folder/file.txt"));
            resolveResult.IsFailure.Should().BeTrue();
            resolveResult.FirstErrorMessage.Should().Contain("symbolic link or junction");
        }
        finally
        {
            if (Directory.Exists(symlinkPath))
            {
                Directory.Delete(symlinkPath);
            }
            if (Directory.Exists(outsideFolder))
            {
                Directory.Delete(outsideFolder, true);
            }
        }
    }

    [Test]
    public void ValidateAndResolveAcceptsNonExistentPath()
    {
        Guard.IsNotNull(_projectFolder);

        var resolver = new RootPathResolver(ResourceKey.DefaultRoot, _projectFolder);

        // Non-existent paths should be accepted (for create operations)
        var resolveResult = resolver.ValidateAndResolve(ResourceKey.Create("new_folder/new_file.txt"));
        resolveResult.IsSuccess.Should().BeTrue();
        resolveResult.Value.Should().NotBeEmpty();
    }

    [Test]
    public void GetResourceKeyReturnsRootOnlyKeyForBackingLocation()
    {
        Guard.IsNotNull(_projectFolder);

        var resolver = new RootPathResolver(ResourceKey.DefaultRoot, _projectFolder);

        var keyResult = resolver.GetResourceKey(_projectFolder);

        keyResult.IsSuccess.Should().BeTrue();
        keyResult.Value.Root.Should().Be(ResourceKey.DefaultRoot);
        keyResult.Value.Path.Should().BeEmpty();
    }

    [Test]
    public void GetResourceKeyComposesRelativeSegmentsUnderTheRoot()
    {
        Guard.IsNotNull(_projectFolder);

        var resolver = new RootPathResolver(ResourceKey.DefaultRoot, _projectFolder);
        var fullPath = Path.Combine(_projectFolder, "folder", "file.txt");

        var keyResult = resolver.GetResourceKey(fullPath);

        keyResult.IsSuccess.Should().BeTrue();
        keyResult.Value.Path.Should().Be("folder/file.txt");
    }

    [Test]
    public void GetResourceKeyFailsForPathOutsideBackingLocation()
    {
        Guard.IsNotNull(_projectFolder);

        var resolver = new RootPathResolver(ResourceKey.DefaultRoot, _projectFolder);
        var outsidePath = Path.Combine(Path.GetTempPath(), $"Celbridge/{nameof(RootPathResolverTests)}_unrelated", "stray.txt");

        var keyResult = resolver.GetResourceKey(outsidePath);

        keyResult.IsFailure.Should().BeTrue();
        keyResult.FirstErrorMessage.Should().Contain("not under root");
    }

    [Test]
    public void GetResourceKeyCarriesThroughTheConfiguredRootName()
    {
        Guard.IsNotNull(_projectFolder);

        var resolver = new RootPathResolver("temp", _projectFolder);
        var fullPath = Path.Combine(_projectFolder, "sub", "scratch.txt");

        var keyResult = resolver.GetResourceKey(fullPath);

        keyResult.IsSuccess.Should().BeTrue();
        keyResult.Value.Root.Should().Be("temp");
        keyResult.Value.Path.Should().Be("sub/scratch.txt");
    }

    [Test]
    public void ValidateAndResolveRejectsIntermediateReparsePoint()
    {
        Guard.IsNotNull(_projectFolder);

        var outsideFolder = Path.Combine(
            Path.GetTempPath(), $"Celbridge/{nameof(RootPathResolverTests)}_outside2");
        Directory.CreateDirectory(outsideFolder);

        // Create a structure: project/parent/link -> outside
        var parentFolder = Path.Combine(_projectFolder, "parent");
        Directory.CreateDirectory(parentFolder);

        var symlinkPath = Path.Combine(parentFolder, "link");

        try
        {
            Directory.CreateSymbolicLink(symlinkPath, outsideFolder);
        }
        catch (Exception)
        {
            Assert.Ignore("Cannot create symbolic links on this system");
            return;
        }

        try
        {
            var resolver = new RootPathResolver(ResourceKey.DefaultRoot, _projectFolder);

            var resolveResult = resolver.ValidateAndResolve(ResourceKey.Create("parent/link/file.txt"));
            resolveResult.IsFailure.Should().BeTrue();
            resolveResult.FirstErrorMessage.Should().Contain("symbolic link or junction");
        }
        finally
        {
            if (Directory.Exists(symlinkPath))
            {
                Directory.Delete(symlinkPath);
            }
            if (Directory.Exists(outsideFolder))
            {
                Directory.Delete(outsideFolder, true);
            }
        }
    }
}
